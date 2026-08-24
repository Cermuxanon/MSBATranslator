using System.Linq.Expressions;
using System.Reflection;
using Google.FlatBuffers;

namespace MSBATranslator.Core.Database
{
    public delegate void FastScalarCopyDelegate(FlatBufferBuilder fbb, object instance);

    public struct FastStringFieldMeta
    {
        public string PropName;
        public bool IsTargetText;
        public Func<object, string> Getter;
        public Action<FlatBufferBuilder, StringOffset> AddInvoker;
    }

    public struct FastVectorFieldMeta
    {
        public string PropName;
        public Func<object, int> LengthGetter;
        public Func<object, int, object?> ItemGetter;
        public Action<FlatBufferBuilder, VectorOffset> AddInvoker;
    }

    public struct FastScalarFieldMeta
    {
        public string PropName;
        public Type ParamType;
        public FastScalarCopyDelegate DirectCopy;
        public Action<FlatBufferBuilder, object> AddInvoker;
    }
    public struct FastExportField
    {
        public string Name;
        public Func<object, object?> Getter;
    }

    public class FastTableMeta
    {
        public string DbTableName { get; set; } = "";
        public string ClassName { get; set; } = "";
        public Type ClassType { get; set; } = null!;
        public Action<FlatBufferBuilder> StartInvoker { get; set; } = null!;
        public Func<FlatBufferBuilder, int> EndInvoker { get; set; } = null!;
        public Func<ByteBuffer, object> GetRootInvoker { get; set; } = null!;
        public Func<object, Dictionary<string, int>, Dictionary<long, int>, string> KeyExtractor { get; set; } = null!;

        public FastScalarFieldMeta[] ScalarFields { get; set; } = Array.Empty<FastScalarFieldMeta>();
        public FastStringFieldMeta[] StringFields { get; set; } = Array.Empty<FastStringFieldMeta>();
        public FastVectorFieldMeta[] VectorFields { get; set; } = Array.Empty<FastVectorFieldMeta>();

        public FastExportField[] ExportFields { get; set; } = Array.Empty<FastExportField>();
    }

    public static class FastMetaCache
    {
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, FastTableMeta?> Cache = new();

        public static FastTableMeta? GetOrCreate(string dbTable, Type classType)
        {
            return GetOrCreate(dbTable, dbTable, classType);
        }

        public static FastTableMeta? GetOrCreate(string rawTableName, string dbTable, Type classType)
        {
            return Cache.GetOrAdd(classType, t => BuildMeta(rawTableName, dbTable, t));
        }

        private static FastTableMeta? BuildMeta(string rawTableName, string dbTable, Type classType)
        {
            string actualClassName = classType.Name;
            var startMethod = classType.GetMethod($"Start{actualClassName}", BindingFlags.Public | BindingFlags.Static);
            var endMethod = classType.GetMethod($"End{actualClassName}", BindingFlags.Public | BindingFlags.Static);
            var getRootMethod = classType.GetMethod($"GetRootAs{actualClassName}", new[] { typeof(ByteBuffer) });

            if (startMethod == null || endMethod == null || getRootMethod == null) return null;

            var valueField = endMethod.ReturnType.GetField("Value");
            if (valueField == null) return null;

            var bbParam = Expression.Parameter(typeof(ByteBuffer), "bb");
            var getRootCall = Expression.Call(getRootMethod, bbParam);
            var getRootLambda = Expression.Lambda<Func<ByteBuffer, object>>(Expression.Convert(getRootCall, typeof(object)), bbParam).Compile();

            var fbbParam = Expression.Parameter(typeof(FlatBufferBuilder), "fbb");
            var startCall = Expression.Call(startMethod, fbbParam);
            var startLambda = Expression.Lambda<Action<FlatBufferBuilder>>(startCall, fbbParam).Compile();

            var endCall = Expression.Call(endMethod, fbbParam);
            var endValueAccess = Expression.Field(endCall, valueField);
            var endLambda = Expression.Lambda<Func<FlatBufferBuilder, int>>(endValueAccess, fbbParam).Compile();

            var addMethods = classType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name.StartsWith("Add") && m.GetParameters().Length == 2)
                .ToList();

            var scalarFields = new List<FastScalarFieldMeta>();
            var stringFields = new List<FastStringFieldMeta>();
            var vectorFields = new List<FastVectorFieldMeta>();

            var instParam = Expression.Parameter(typeof(object), "inst");
            var castInst = Expression.Convert(instParam, classType);

            foreach (var m in addMethods)
            {
                string propName = m.Name.Substring(3);
                Type pType = m.GetParameters()[1].ParameterType;

                if (pType == typeof(StringOffset))
                {
                    var prop = classType.GetProperty(propName);
                    if (prop == null) continue;

                    var propAccess = Expression.Property(castInst, prop);
                    var strGetter = Expression.Lambda<Func<object, string>>(
                        Expression.Coalesce(propAccess, Expression.Constant(string.Empty)), instParam).Compile();

                    var offParam = Expression.Parameter(typeof(StringOffset), "off");
                    var addCall = Expression.Call(m, fbbParam, offParam);
                    var addInvoker = Expression.Lambda<Action<FlatBufferBuilder, StringOffset>>(addCall, fbbParam, offParam).Compile();

                    stringFields.Add(new FastStringFieldMeta
                    {
                        PropName = propName,
                        IsTargetText = IsTargetTextField(propName),
                        Getter = strGetter,
                        AddInvoker = addInvoker
                    });
                }
                else if (pType == typeof(VectorOffset))
                {
                    var lenProp = classType.GetProperty($"{propName}Length");
                    var itemMethod = classType.GetMethod(propName, new[] { typeof(int) });
                    if (lenProp == null || itemMethod == null) continue;

                    var lenAccess = Expression.Property(castInst, lenProp);
                    var lenGetter = Expression.Lambda<Func<object, int>>(lenAccess, instParam).Compile();

                    var idxParam = Expression.Parameter(typeof(int), "idx");
                    var itemCall = Expression.Call(castInst, itemMethod, idxParam);
                    var itemGetter = Expression.Lambda<Func<object, int, object?>>(
                        Expression.Convert(itemCall, typeof(object)), instParam, idxParam).Compile();

                    var vOffParam = Expression.Parameter(typeof(VectorOffset), "vOff");
                    var addCall = Expression.Call(m, fbbParam, vOffParam);
                    var addInvoker = Expression.Lambda<Action<FlatBufferBuilder, VectorOffset>>(addCall, fbbParam, vOffParam).Compile();

                    vectorFields.Add(new FastVectorFieldMeta
                    {
                        PropName = propName,
                        LengthGetter = lenGetter,
                        ItemGetter = itemGetter,
                        AddInvoker = addInvoker
                    });
                }
                else
                {
                    var prop = classType.GetProperty(propName);
                    if (prop != null && prop.PropertyType == pType)
                    {
                        var propAccess = Expression.Property(castInst, prop);
                        var directAddCall = Expression.Call(m, fbbParam, propAccess);
                        var directCopyLambda = Expression.Lambda<FastScalarCopyDelegate>(directAddCall, fbbParam, instParam).Compile();

                        var valParam = Expression.Parameter(typeof(object), "val");
                        var castVal = Expression.Convert(valParam, pType);
                        var addObjCall = Expression.Call(m, fbbParam, castVal);
                        var addObjLambda = Expression.Lambda<Action<FlatBufferBuilder, object>>(addObjCall, fbbParam, valParam).Compile();

                        scalarFields.Add(new FastScalarFieldMeta
                        {
                            PropName = propName,
                            ParamType = pType,
                            DirectCopy = directCopyLambda,
                            AddInvoker = addObjLambda
                        });
                    }
                }
            }

            var props = classType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var exportFieldsList = new List<FastExportField>();
            var vectorNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var p in props)
            {
                if (p.Name.EndsWith("Length") && p.PropertyType == typeof(int))
                {
                    vectorNames.Add(p.Name.Substring(0, p.Name.Length - 6));
                }
            }

            foreach (var p in props)
            {
                if (p.Name == "ByteBuffer" || p.Name == "__p" || p.Name.EndsWith("Length")) continue;

                string propName = p.Name;

                if (vectorNames.Contains(propName))
                {
                    var lenProp = classType.GetProperty($"{propName}Length");
                    var itemMethod = classType.GetMethod(propName, new[] { typeof(int) });
                    if (lenProp != null && itemMethod != null)
                    {
                        var vfGetter = (Func<object, object?>)(inst =>
                        {
                            int len = (int)(lenProp.GetValue(inst) ?? 0);
                            var list = new List<object?>(len);
                            for (int j = 0; j < len; j++)
                            {
                                list.Add(itemMethod.Invoke(inst, new object[] { j }));
                            }
                            return list;
                        });
                        exportFieldsList.Add(new FastExportField { Name = propName, Getter = vfGetter });
                    }
                }
                else
                {
                    var propAccess = Expression.Property(castInst, p);
                    var getter = Expression.Lambda<Func<object, object?>>(
                        Expression.Convert(propAccess, typeof(object)), instParam).Compile();
                    exportFieldsList.Add(new FastExportField { Name = propName, Getter = getter });
                }
            }

            return new FastTableMeta
            {
                DbTableName = dbTable,
                ClassName = actualClassName,
                ClassType = classType,
                StartInvoker = startLambda,
                EndInvoker = endLambda,
                GetRootInvoker = getRootLambda,
                KeyExtractor = TableMapper.CompileKeyExtractor(rawTableName: rawTableName, classType: classType),
                ScalarFields = scalarFields.ToArray(),
                StringFields = stringFields.ToArray(),
                VectorFields = vectorFields.ToArray(),
                ExportFields = exportFieldsList.ToArray()
            };
        }

        private static bool IsTargetTextField(string propName)
        {
            return propName.Equals("En", StringComparison.OrdinalIgnoreCase)
                || propName.Equals("TextEn", StringComparison.OrdinalIgnoreCase)
                || propName.Equals("LocalizeEN", StringComparison.OrdinalIgnoreCase)
                || propName.Equals("MessageEN", StringComparison.OrdinalIgnoreCase)
                || propName.Equals("NameEn", StringComparison.OrdinalIgnoreCase);
        }

        public static VectorOffset BuildVectorFromInstanceFast(FlatBufferBuilder fbb, object instance, Func<object, int, object?> itemGetter, int len)
        {
            object? first = itemGetter(instance, 0);
            if (first == null) return default;

            Type elemType = first.GetType();

            if (elemType == typeof(uint))
            {
                fbb.StartVector(4, len, 4);
                for (int j = len - 1; j >= 0; j--) fbb.AddUint(Convert.ToUInt32(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(int) || elemType.IsEnum)
            {
                fbb.StartVector(4, len, 4);
                for (int j = len - 1; j >= 0; j--) fbb.AddInt(Convert.ToInt32(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(long))
            {
                fbb.StartVector(8, len, 8);
                for (int j = len - 1; j >= 0; j--) fbb.AddLong(Convert.ToInt64(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(ulong))
            {
                fbb.StartVector(8, len, 8);
                for (int j = len - 1; j >= 0; j--) fbb.AddUlong(Convert.ToUInt64(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(float))
            {
                fbb.StartVector(4, len, 4);
                for (int j = len - 1; j >= 0; j--) fbb.AddFloat(Convert.ToSingle(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(double))
            {
                fbb.StartVector(8, len, 8);
                for (int j = len - 1; j >= 0; j--) fbb.AddDouble(Convert.ToDouble(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(bool))
            {
                fbb.StartVector(1, len, 1);
                for (int j = len - 1; j >= 0; j--) fbb.AddBool(Convert.ToBoolean(itemGetter(instance, j)));
                return fbb.EndVector();
            }
            if (elemType == typeof(string))
            {
                var sOffsets = new StringOffset[len];
                for (int j = 0; j < len; j++)
                {
                    string s = itemGetter(instance, j)?.ToString() ?? "";
                    sOffsets[j] = fbb.CreateString(s);
                }
                fbb.StartVector(4, len, 4);
                for (int j = len - 1; j >= 0; j--) fbb.AddOffset(sOffsets[j].Value);
                return fbb.EndVector();
            }

            return default;
        }

        public static object ConvertToParamFast(object val, Type targetType)
        {
            if (targetType == typeof(int)) return Convert.ToInt32(val);
            if (targetType == typeof(uint)) return Convert.ToUInt32(val);
            if (targetType == typeof(long)) return Convert.ToInt64(val);
            if (targetType == typeof(ulong)) return Convert.ToUInt64(val);
            if (targetType == typeof(short)) return Convert.ToInt16(val);
            if (targetType == typeof(ushort)) return Convert.ToUInt16(val);
            if (targetType == typeof(byte)) return Convert.ToByte(val);
            if (targetType == typeof(sbyte)) return Convert.ToSByte(val);
            if (targetType == typeof(bool)) return Convert.ToBoolean(val);
            if (targetType == typeof(float)) return Convert.ToSingle(val);
            if (targetType == typeof(double)) return Convert.ToDouble(val);
            if (targetType.IsEnum) return Enum.ToObject(targetType, Convert.ToInt32(val));
            return val;
        }
    }
}