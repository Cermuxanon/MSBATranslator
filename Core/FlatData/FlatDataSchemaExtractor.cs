using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace MSBATranslator.Core.FlatData
{
    public class FlatTableFieldInfo
    {
        public string Name { get; set; } = string.Empty;
        public string TypeName { get; set; } = string.Empty;
        public bool IsVector { get; set; }
    }

    public class FlatTableSchema
    {
        public string TableName { get; set; } = string.Empty;
        public string FullTypeName { get; set; } = string.Empty;
        public List<FlatTableFieldInfo> Fields { get; set; } = new();
    }

    public static class FlatDataSchemaExtractor
    {
        public static List<FlatTableSchema> LoadedSchemas { get; private set; } = new();

        public static bool ExtractSchemasFromDll(string dllPath, out int tableCount)
        {
            tableCount = 0;
            LoadedSchemas.Clear();

            if (!File.Exists(dllPath))
            {
                Logger.Log($"- Файл сборки не найден: {dllPath}");
                return false;
            }

            try
            {
                using var assembly = AssemblyDefinition.ReadAssembly(dllPath);
                var schemas = new List<FlatTableSchema>();

                foreach (var module in assembly.Modules)
                {
                    foreach (var type in module.Types)
                    {
                        if (type.Namespace != null && type.Namespace.Contains("FlatData") && type.Name.EndsWith("Excel"))
                        {
                            var schema = new FlatTableSchema
                            {
                                TableName = type.Name,
                                FullTypeName = type.FullName
                            };

                            foreach (var prop in type.Properties)
                            {
                                if (prop.Name == "ByteBuffer") continue;

                                bool isVector = prop.Name.EndsWith("Length") && prop.PropertyType.Name == "Int32";
                                string fieldName = isVector ? prop.Name.Substring(0, prop.Name.Length - 6) : prop.Name;

                                schema.Fields.Add(new FlatTableFieldInfo
                                {
                                    Name = fieldName,
                                    TypeName = prop.PropertyType.Name,
                                    IsVector = isVector
                                });
                            }

                            schemas.Add(schema);
                        }
                    }
                }

                LoadedSchemas = schemas.OrderBy(s => s.TableName).ToList();
                tableCount = LoadedSchemas.Count;

                Logger.Log($"+ Извлечено схем таблиц FlatData: {tableCount}");
                return tableCount > 0;
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка извлечения схем FlatData через Cecil: {ex.Message}");
                return false;
            }
        }
    }
}