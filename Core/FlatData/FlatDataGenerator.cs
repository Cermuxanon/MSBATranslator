using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

namespace MSBATranslator.Core.FlatData
{
    public class FlatProp
    {
        public string Name { get; set; } = "";
        public string Type { get; set; } = "";
        public bool IsList { get; set; }
        public int SlotIndex { get; set; }
        public bool IsDummy { get; set; }
    }

    public class GenerationResult
    {
        public int TotalGenerated { get; set; }
        public List<string> GeneratedEnums { get; set; } = new();
        public List<string> GeneratedStructs { get; set; } = new();
        public List<string> GeneratedTables { get; set; } = new();
        public string OutputDirectory { get; set; } = "";
    }

    public static class FlatDataGenerator
    {
        public static GenerationResult? LastResult { get; private set; }

        private static readonly HashSet<string> CSharpPrimitiveTypes = new()
        {
            "bool", "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong",
            "float", "double", "decimal", "char", "string", "object", "void"
        };

        private static readonly HashSet<string> CSharpKeywords = new()
        {
            "abstract", "as", "base", "bool", "break", "byte", "case", "catch", "char",
            "checked", "class", "const", "continue", "decimal", "default", "delegate",
            "do", "double", "else", "enum", "event", "explicit", "extern", "false",
            "finally", "fixed", "float", "for", "foreach", "goto", "if", "implicit",
            "in", "int", "interface", "internal", "is", "lock", "long", "namespace",
            "new", "null", "object", "operator", "out", "override", "params", "private",
            "protected", "public", "readonly", "ref", "return", "sbyte", "sealed",
            "short", "sizeof", "stackalloc", "static", "string", "struct", "switch",
            "this", "throw", "true", "try", "typeof", "uint", "ulong", "unchecked",
            "unsafe", "ushort", "using", "virtual", "void", "volatile", "while"
        };

        private static string EscapeIdentifier(string name) => CSharpKeywords.Contains(name) ? "@" + name : name;

        private static string EscapeTypeName(string typeName)
        {
            if (CSharpPrimitiveTypes.Contains(typeName)) return typeName;
            return CSharpKeywords.Contains(typeName) ? "@" + typeName : typeName;
        }

        private static string SanitizeName(string rawName)
        {
            if (string.IsNullOrEmpty(rawName)) return "";

            if (rawName.StartsWith("<") && rawName.Contains(">"))
            {
                int start = 1;
                int end = rawName.IndexOf('>');
                if (end > start)
                {
                    rawName = rawName.Substring(start, end - start);
                }
            }

            return new string(rawName.Where(c => char.IsLetterOrDigit(c) || c == '_').ToArray());
        }

        public static GenerationResult GenerateFromCecil(string dllPath, string outputDir, string targetNamespace = "MSBATranslator.FlatData")
        {
            var result = new GenerationResult { OutputDirectory = outputDir };

            if (!File.Exists(dllPath))
            {
                Logger.Log($"- Файл сборки не найден: {dllPath}");
                return result;
            }

            if (Directory.Exists(outputDir))
            {
                Directory.Delete(outputDir, true);
            }
            Directory.CreateDirectory(outputDir);

            Logger.Log($"* Чтение метаданных сборки: {Path.GetFileName(dllPath)}");

            var module = ModuleDefinition.ReadModule(dllPath);

            var allTypes = GetAllTypes(module)
                .Where(t => t.Namespace == "FlatData" ||
                            t.Namespace == "MX.Data.Excel" ||
                            t.Namespace.StartsWith("FlatData.") ||
                            t.Interfaces.Any(i => i.InterfaceType.Name.Contains("IFlatbufferObject")))
                .ToList();

            var knownEnums = new HashSet<string>(
                allTypes.Where(t => t.IsEnum).Select(t => CleanRawName(t.Name))
            );

            GenerateUnityStubs(outputDir, targetNamespace);

            foreach (var type in allTypes)
            {
                if (type.Name.StartsWith("<") || type.Name.Contains("<>") || type.IsAbstract || type.Name.StartsWith("Base")) continue;

                string cleanClassName = CleanRawName(type.Name);

                if (type.IsEnum)
                {
                    GenerateEnum(type, outputDir, targetNamespace);
                    result.GeneratedEnums.Add(cleanClassName);
                    continue;
                }

                if (type.IsInterface || type.HasGenericParameters) continue;

                var props = ExtractProperties(type);

                if (props.Count == 0 && !type.Name.EndsWith("Table")) continue;

                if (cleanClassName.EndsWith("Table") && props.Count == 0)
                {
                    string innerType = cleanClassName.Substring(0, cleanClassName.Length - 5);
                    props.Add(new FlatProp { Name = "DataList", Type = innerType, IsList = true, SlotIndex = 0 });
                    result.GeneratedTables.Add(cleanClassName);
                }
                else
                {
                    result.GeneratedStructs.Add(cleanClassName);
                }

                string csCode = BuildClassCode(cleanClassName, props, targetNamespace, knownEnums);
                File.WriteAllText(Path.Combine(outputDir, $"{cleanClassName}.cs"), csCode);
            }

            result.TotalGenerated = result.GeneratedEnums.Count + result.GeneratedStructs.Count + result.GeneratedTables.Count;
            LastResult = result;

            Logger.Log($"+ Генерация FlatData успешно");
            Logger.Log($"+ Всего сгенерировано файлов: {result.TotalGenerated}");
            Logger.Log($"+ Enums: {result.GeneratedEnums.Count}");
            Logger.Log($"+ Structs: {result.GeneratedStructs.Count}");
            Logger.Log($"+ Tables: {result.GeneratedTables.Count}");

            return result;
        }

        private static IEnumerable<TypeDefinition> GetAllTypes(ModuleDefinition module) => module.Types.SelectMany(GetAllTypesAndNested);

        private static IEnumerable<TypeDefinition> GetAllTypesAndNested(TypeDefinition type)
        {
            yield return type;
            if (type.HasNestedTypes)
            {
                foreach (var nested in type.NestedTypes.SelectMany(GetAllTypesAndNested))
                    yield return nested;
            }
        }

        private static int? ExtractVTableOffsetFromMethod(MethodDefinition? method)
        {
            if (method == null || !method.HasBody) return null;

            var instructions = method.Body.Instructions;
            for (int i = 0; i < instructions.Count; i++)
            {
                var inst = instructions[i];
                if ((inst.OpCode == OpCodes.Call || inst.OpCode == OpCodes.Callvirt) &&
                    inst.Operand is MethodReference targetMethod &&
                    targetMethod.Name == "__offset")
                {
                    if (i > 0)
                    {
                        var prev = instructions[i - 1];
                        if (prev.OpCode == OpCodes.Ldc_I4)
                            return (int)prev.Operand;
                        if (prev.OpCode == OpCodes.Ldc_I4_S)
                            return Convert.ToInt32(prev.Operand);
                        if (prev.OpCode.Value >= OpCodes.Ldc_I4_0.Value && prev.OpCode.Value <= OpCodes.Ldc_I4_8.Value)
                        {
                            return prev.OpCode.Value - OpCodes.Ldc_I4_0.Value;
                        }
                    }
                }
            }
            return null;
        }

        private static List<FlatProp> ExtractProperties(TypeDefinition type)
        {
            var props = new List<FlatProp>();
            var processedNames = new HashSet<string>();

            bool hasCilOffsets = false;
            var slotMap = new Dictionary<int, FlatProp>();

            if (type.HasProperties)
            {
                foreach (var p in type.Properties)
                {
                    if (p.Name == "ByteBuffer" || p.Name.StartsWith("<")) continue;

                    string pName = SanitizeName(p.Name);
                    if (string.IsNullOrEmpty(pName)) continue;

                    string pType = GetCleanTypeName(p.PropertyType);
                    bool isList = false;

                    if (pName.EndsWith("Length") && pName.Length > 6)
                    {
                        string baseName = pName.Substring(0, pName.Length - 6);
                        var itemMethod = type.Methods.FirstOrDefault(m => SanitizeName(m.Name) == baseName && m.Parameters.Count == 1);
                        if (itemMethod != null)
                        {
                            pName = baseName;
                            pType = GetCleanTypeName(itemMethod.ReturnType);
                            isList = true;
                        }
                    }

                    if (processedNames.Contains(pName)) continue;
                    processedNames.Add(pName);

                    int? vtableOffset = ExtractVTableOffsetFromMethod(p.GetMethod);
                    if (vtableOffset.HasValue && vtableOffset.Value >= 4)
                    {
                        hasCilOffsets = true;
                        int slotIndex = (vtableOffset.Value - 4) / 2;
                        slotMap[slotIndex] = new FlatProp { Name = pName, Type = pType, IsList = isList, SlotIndex = slotIndex };
                    }
                    else
                    {
                        props.Add(new FlatProp { Name = pName, Type = pType, IsList = isList, SlotIndex = props.Count });
                    }
                }
            }

            if (hasCilOffsets && slotMap.Count > 0)
            {
                int maxSlot = slotMap.Keys.Max();
                var result = new List<FlatProp>();
                for (int i = 0; i <= maxSlot; i++)
                {
                    if (slotMap.TryGetValue(i, out var prop))
                    {
                        result.Add(prop);
                    }
                    else
                    {
                        result.Add(new FlatProp { Name = $"DummySlot{i}", Type = "int", IsList = false, SlotIndex = i, IsDummy = true });
                    }
                }
                return result;
            }

            if (props.Count > 0)
            {
                return props;
            }

            if (type.HasFields)
            {
                foreach (var f in type.Fields)
                {
                    if (f.IsSpecialName || f.IsStatic || f.Name == "__p" || f.Name.StartsWith("<") || f.Name.Contains("BackingField")) continue;

                    string fName = SanitizeName(f.Name);
                    if (string.IsNullOrEmpty(fName) || processedNames.Contains(fName)) continue;
                    processedNames.Add(fName);

                    props.Add(new FlatProp { Name = fName, Type = GetCleanTypeName(f.FieldType), IsList = false, SlotIndex = props.Count });
                }
            }

            if (props.Count == 0 && type.HasMethods)
            {
                foreach (var m in type.Methods)
                {
                    if (m.IsStatic || !m.IsPublic || m.HasParameters || m.ReturnType.Name == "Void") continue;
                    if (m.Name.StartsWith("<") || m.Name.StartsWith("__") || m.Name.StartsWith("GetRootAs") || m.Name == "ByteBuffer") continue;

                    string mName = SanitizeName(m.Name);
                    if (mName.EndsWith("Length")) mName = mName.Substring(0, mName.Length - 6);

                    if (string.IsNullOrEmpty(mName) || processedNames.Contains(mName)) continue;
                    processedNames.Add(mName);

                    props.Add(new FlatProp { Name = mName, Type = GetCleanTypeName(m.ReturnType), IsList = false, SlotIndex = props.Count });
                }
            }

            return props;
        }

        private static void GenerateUnityStubs(string outputDir, string targetNamespace)
        {
            string stubsCode = $@"namespace {targetNamespace}
            {{
                public struct Vector2 {{ public float x; public float y; }}
                public struct Vector3 {{ public float x; public float y; public float z; }}
                public struct Color {{ public float r; public float g; public float b; public float a; }}
                public class Transform {{ }}
                public enum UIFxGroupRenderQueuePriorityOrder : int {{ Normal = 0 }}
            }}";
            File.WriteAllText(Path.Combine(outputDir, "UnityStubs.cs"), stubsCode);
        }

        private static void GenerateEnum(TypeDefinition type, string outputDir, string targetNamespace)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"namespace {targetNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    public enum {EscapeTypeName(CleanRawName(type.Name))} : int");
            sb.AppendLine("    {");

            foreach (var field in type.Fields)
            {
                if (field.IsSpecialName || field.Name.StartsWith("<")) continue;
                string fieldName = SanitizeName(field.Name);
                if (string.IsNullOrEmpty(fieldName)) continue;

                sb.AppendLine($"        {EscapeIdentifier(fieldName)} = {field.Constant},");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");

            File.WriteAllText(Path.Combine(outputDir, $"{CleanRawName(type.Name)}.cs"), sb.ToString());
        }

        private static string BuildClassCode(string className, List<FlatProp> props, string targetNamespace, HashSet<string> knownEnums)
        {
            var sb = new StringBuilder();
            sb.AppendLine("#nullable disable");
            sb.AppendLine("using System;");
            sb.AppendLine("using Google.FlatBuffers;");
            sb.AppendLine();
            sb.AppendLine($"namespace {targetNamespace}");
            sb.AppendLine("{");
            sb.AppendLine($"    public struct {EscapeTypeName(className)} : IFlatbufferObject");
            sb.AppendLine("    {");
            sb.AppendLine("        private Table __p;");
            sb.AppendLine("        public ByteBuffer ByteBuffer => __p.bb;");
            sb.AppendLine("        public void __init(int _i, ByteBuffer _bb) { __p = new Table(_i, _bb); }");
            sb.AppendLine($"        public {className} __assign(int _i, ByteBuffer _bb) {{ __init(_i, _bb); return this; }}");
            sb.AppendLine();
            sb.AppendLine($"        public static {className} GetRootAs{className}(ByteBuffer _bb) => GetRootAs{className}(_bb, new {className}());");
            sb.AppendLine($"        public static {className} GetRootAs{className}(ByteBuffer _bb, {className} obj) => obj.__assign(_bb.GetInt(_bb.Position) + _bb.Position, _bb);");
            sb.AppendLine();
            sb.AppendLine("        private string GetStringSafe(int stringOffset)");
            sb.AppendLine("        {");
            sb.AppendLine("            if (stringOffset < 0 || stringOffset + 4 > __p.bb.Length) return null;");
            sb.AppendLine("            try { return __p.__string(stringOffset); } catch { return null; }");
            sb.AppendLine("        }");
            sb.AppendLine();

            foreach (var p in props)
            {
                if (p.IsDummy) continue;

                int offset = 4 + (p.SlotIndex * 2);
                string safePropName = EscapeIdentifier(p.Name);
                string safePropType = EscapeTypeName(p.Type);

                if (p.IsList)
                {
                    sb.AppendLine($"        public {safePropType} {safePropName}(int j) {{ int o = __p.__offset({offset}); return o != 0 ? {GetVectorGetter(p.Type, knownEnums)} : default; }}");
                    sb.AppendLine($"        public int {safePropName}Length {{ get {{ int o = __p.__offset({offset}); return o != 0 ? __p.__vector_len(o) : 0; }} }}");
                }
                else
                {
                    sb.AppendLine($"        public {safePropType} {safePropName} {{ get {{ int o = __p.__offset({offset}); return o != 0 ? {GetScalarGetter(p.Type, knownEnums)} : default; }} }}");
                }
            }

            sb.AppendLine();
            sb.AppendLine($"        public static void Start{className}(FlatBufferBuilder builder) => builder.StartTable({props.Count});");

            foreach (var p in props)
            {
                if (p.IsDummy) continue;

                string safePropName = EscapeIdentifier(p.Name);
                string paramType = GetBuilderParamType(p.Type, p.IsList, knownEnums);
                string addCall = GetBuilderAddCall(p.SlotIndex, p.Type, safePropName, p.IsList, knownEnums);

                sb.AppendLine($"        public static void Add{p.Name}(FlatBufferBuilder builder, {paramType} {safePropName}) => {addCall}");
            }

            sb.AppendLine($"        public static Offset<{className}> End{className}(FlatBufferBuilder builder) => new Offset<{className}>(builder.EndTable());");

            sb.AppendLine("    }");
            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GetBuilderParamType(string type, bool isList, HashSet<string> knownEnums)
        {
            if (isList) return "VectorOffset";
            if (type == "string") return "StringOffset";
            if (CSharpPrimitiveTypes.Contains(type) || knownEnums.Contains(type)) return EscapeTypeName(type);
            return $"Offset<{EscapeTypeName(type)}>";
        }

        private static string GetBuilderAddCall(int slot, string type, string paramName, bool isList, HashSet<string> knownEnums)
        {
            if (isList || type == "string" || (!CSharpPrimitiveTypes.Contains(type) && !knownEnums.Contains(type)))
            {
                return $"builder.AddOffset({slot}, {paramName}.Value, 0);";
            }

            return type switch
            {
                "long" => $"builder.AddLong({slot}, {paramName}, 0L);",
                "ulong" => $"builder.AddUlong({slot}, {paramName}, 0UL);",
                "int" => $"builder.AddInt({slot}, {paramName}, 0);",
                "uint" => $"builder.AddUint({slot}, {paramName}, 0U);",
                "short" => $"builder.AddShort({slot}, {paramName}, 0);",
                "ushort" => $"builder.AddUshort({slot}, {paramName}, 0);",
                "float" => $"builder.AddFloat({slot}, {paramName}, 0.0f);",
                "double" => $"builder.AddDouble({slot}, {paramName}, 0.0d);",
                "bool" => $"builder.AddBool({slot}, {paramName}, false);",
                "byte" => $"builder.AddByte({slot}, {paramName}, 0);",
                "sbyte" => $"builder.AddSbyte({slot}, {paramName}, 0);",
                _ => $"builder.AddInt({slot}, (int){paramName}, 0);"
            };
        }

        private static string GetCleanTypeName(TypeReference typeRef)
        {
            if (typeRef == null) return "object";

            if (typeRef is GenericInstanceType git)
            {
                if (git.Name.StartsWith("Nullable`")) return GetCleanTypeName(git.GenericArguments[0]);
                string baseName = CleanRawName(git.Name);
                string args = string.Join(", ", git.GenericArguments.Select(GetCleanTypeName));
                return $"{baseName}<{args}>";
            }

            if (typeRef.IsArray) return $"{GetCleanTypeName(typeRef.GetElementType())}[]";

            string name = CleanRawName(typeRef.Name);
            return name switch
            {
                "Int64" => "long", "UInt64" => "ulong", "Int32" => "int", "UInt32" => "uint",
                "Int16" => "short", "UInt16" => "ushort", "Boolean" => "bool", "Single" => "float",
                "Double" => "double", "String" => "string", "Byte" => "byte", "SByte" => "sbyte",
                "Void" => "void", _ => name
            };
        }

        private static string CleanRawName(string rawName)
        {
            rawName = SanitizeName(rawName);
            if (rawName.Contains('`'))
            {
                return rawName.Split('`')[0];
            }
            return rawName;
        }

        private static string GetScalarGetter(string type, HashSet<string> knownEnums)
        {
            if (type.EndsWith("[]"))
            {
                string elemType = type.Substring(0, type.Length - 2);
                return $"Array.Empty<{EscapeTypeName(elemType)}()>";
            }

            return type switch
            {
                "long" => "__p.bb.GetLong(o + __p.bb_pos)",
                "ulong" => "__p.bb.GetUlong(o + __p.bb_pos)",
                "int" => "__p.bb.GetInt(o + __p.bb_pos)",
                "uint" => "__p.bb.GetUint(o + __p.bb_pos)",
                "short" => "__p.bb.GetShort(o + __p.bb_pos)",
                "ushort" => "__p.bb.GetUshort(o + __p.bb_pos)",
                "float" => "__p.bb.GetFloat(o + __p.bb_pos)",
                "double" => "__p.bb.GetDouble(o + __p.bb_pos)",
                "bool" => "0 != __p.bb.Get(o + __p.bb_pos)",
                "byte" => "__p.bb.Get(o + __p.bb_pos)",
                "sbyte" => "(sbyte)__p.bb.Get(o + __p.bb_pos)",
                "string" => "GetStringSafe(o + __p.bb_pos)",
                _ => knownEnums.Contains(type)
                    ? $"({EscapeTypeName(type)})__p.bb.GetInt(o + __p.bb_pos)"
                    : $"new {EscapeTypeName(type)}().__assign(__p.__indirect(o + __p.bb_pos), __p.bb)"
            };
        }

        private static string GetVectorGetter(string type, HashSet<string> knownEnums)
        {
            if (type.EndsWith("[]")) type = type.Substring(0, type.Length - 2);

            return type switch
            {
                "uint" => "__p.bb.GetUint(__p.__vector(o) + j * 4)",
                "int" => "__p.bb.GetInt(__p.__vector(o) + j * 4)",
                "long" => "__p.bb.GetLong(__p.__vector(o) + j * 8)",
                "ulong" => "__p.bb.GetUlong(__p.__vector(o) + j * 8)",
                "short" => "__p.bb.GetShort(__p.__vector(o) + j * 2)",
                "ushort" => "__p.bb.GetUshort(__p.__vector(o) + j * 2)",
                "float" => "__p.bb.GetFloat(__p.__vector(o) + j * 4)",
                "double" => "__p.bb.GetDouble(__p.__vector(o) + j * 8)",
                "bool" => "0 != __p.bb.Get(__p.__vector(o) + j * 1)",
                "byte" => "__p.bb.Get(__p.__vector(o) + j * 1)",
                "sbyte" => "(sbyte)__p.bb.Get(__p.__vector(o) + j * 1)",
                "string" => "GetStringSafe(__p.__vector(o) + j * 4)",
                _ => knownEnums.Contains(type)
                    ? $"({EscapeTypeName(type)})__p.bb.GetInt(__p.__vector(o) + j * 4)"
                    : $"new {EscapeTypeName(type)}().__assign(__p.__indirect(__p.__vector(o) + j * 4), __p.bb)"
            };
        }
    }
}