using System.Text.RegularExpressions;

namespace MSBATranslator.Core.Database
{
    public static class TableNameHelper
    {
        private static readonly string[] SuffixesToRemove = 
        {
            "ExcelRepository",
            "ExcelDBSchema",
            "ExcelTable",
            "DBSchema",
            "Excel",
            "Table"
        };
        public static string NormalizeBaseName(string rawName)
        {
            if (string.IsNullOrWhiteSpace(rawName)) return string.Empty;
            string clean = Path.GetFileNameWithoutExtension(rawName);

            foreach (var suffix in SuffixesToRemove)
            {
                if (clean.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    clean = clean.Substring(0, clean.Length - suffix.Length);
                    break;
                }
            }

            return clean;
        }
        public static string? MatchDbTable(string rawName, IEnumerable<string> existingDbTables)
        {
            string baseName = NormalizeBaseName(rawName);
            var tableSet = existingDbTables as HashSet<string> ?? new HashSet<string>(existingDbTables, StringComparer.OrdinalIgnoreCase);

            string[] candidates = 
            {
                $"{baseName}ExcelTable",
                $"{baseName}ExcelDBSchema",
                $"{baseName}DBSchema",
                $"{baseName}Excel",
                $"{baseName}Table",
                baseName,
                rawName
            };

            return candidates.FirstOrDefault(c => tableSet.Contains(c));
        }
        public static Type? MatchFlatDataType(string rawName, System.Reflection.Assembly assembly)
        {
            string baseName = NormalizeBaseName(rawName);

            string[] candidates = 
            {
                $"MSBATranslator.FlatData.{baseName}Excel",
                $"MSBATranslator.FlatData.{baseName}",
                $"MSBATranslator.FlatData.{rawName}",
                $"FlatData.{baseName}Excel",
                $"FlatData.{baseName}"
            };

            foreach (var name in candidates)
            {
                var type = assembly.GetType(name);
                if (type != null) return type;
            }

            return null;
        }
    }
}