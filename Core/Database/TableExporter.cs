using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using Google.FlatBuffers;
using Microsoft.Data.Sqlite;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.FlatData;

namespace MSBATranslator.Core.Database
{
    public class ExportTableItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = false;
    }

    public static class TableExporter
    {
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };

        public static bool IsExporting { get; private set; } = false;
        public static float ExportProgress { get; private set; } = 0f;

        public static List<ExportTableItem> GetDatabaseTables()
        {
            var tables = new List<ExportTableItem>();
            var cfg = AppConfig.Instance;
            string dbPath = File.Exists(cfg.BackupFilePath) ? cfg.BackupFilePath : cfg.ExcelDbPath;

            if (!File.Exists(dbPath) || string.IsNullOrWhiteSpace(cfg.SqlHexKey)) 
                return tables;

            try
            {
                SQLitePCL.Batteries_V2.Init();
                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = dbPath,
                    Mode = SqliteOpenMode.ReadOnly
                };

                using var conn = new SqliteConnection(csb.ConnectionString);
                conn.Open();

                using (var keyCmd = conn.CreateCommand())
                {
                    keyCmd.CommandText = $"PRAGMA key = \"x'{cfg.SqlHexKey}'\";";
                    keyCmd.ExecuteNonQuery();
                }

                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name;";
                using var r = cmd.ExecuteReader();

                while (r.Read())
                {
                    string tName = r.GetString(0);
                    if (tName.StartsWith("sqlite_", StringComparison.OrdinalIgnoreCase)) continue;

                    tables.Add(new ExportTableItem { Name = tName, IsSelected = false });
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка получения списка таблиц: {ex.Message}");
            }

            return tables;
        }

        public static async Task<bool> ExportTablesAsync(string outputDirectory, List<string> selectedTables)
        {
            if (IsExporting) return false;
            IsExporting = true;
            ExportProgress = 0f;

            return await Task.Run(() =>
            {
                try
                {
                    var cfg = AppConfig.Instance;
                    string dbPath = File.Exists(cfg.BackupFilePath) ? cfg.BackupFilePath : cfg.ExcelDbPath;

                    EnsureFlatDataAssembly();
                    if (RoslynCompiler.CompiledAssembly == null)
                    {
                        Logger.Log("- Не удалось загрузить сборку FlatData для экспорта.");
                        return false;
                    }

                    if (!Directory.Exists(outputDirectory))
                    {
                        Directory.CreateDirectory(outputDirectory);
                    }

                    SQLitePCL.Batteries_V2.Init();
                    var csb = new SqliteConnectionStringBuilder
                    {
                        DataSource = dbPath,
                        Mode = SqliteOpenMode.ReadOnly
                    };

                    using var conn = new SqliteConnection(csb.ConnectionString);
                    conn.Open();

                    using (var keyCmd = conn.CreateCommand())
                    {
                        keyCmd.CommandText = $"PRAGMA key = \"x'{cfg.SqlHexKey}'\";";
                        keyCmd.ExecuteNonQuery();
                    }

                    int totalTables = selectedTables.Count;
                    int processedTables = 0;
                    int totalRowsExported = 0;

                    foreach (var tableName in selectedTables)
                    {
                        processedTables++;
                        ExportProgress = (float)processedTables / totalTables;

                        var classType = TableNameHelper.MatchFlatDataType(tableName, RoslynCompiler.CompiledAssembly);
                        if (classType == null)
                        {
                            Logger.Log($"* [Пропуск] {tableName}: структура не найдена в FlatData.");
                            continue;
                        }

                        var meta = FastMetaCache.GetOrCreate(tableName, tableName, classType);
                        if (meta == null) continue;

                        var rowsList = new List<Dictionary<string, object?>>();

                        using var selectCmd = conn.CreateCommand();
                        selectCmd.CommandText = $"SELECT RowId, Bytes FROM [{tableName}];";
                        using var reader = selectCmd.ExecuteReader();

                        while (reader.Read())
                        {
                            long rowId = reader.GetInt64(0);
                            if (reader["Bytes"] is byte[] blob && blob.Length > 0)
                            {
                                var bb = new ByteBuffer(blob);
                                object instance = meta.GetRootInvoker(bb);
                                if (instance == null) continue;

                                var rowDict = new Dictionary<string, object?>(meta.ExportFields.Length + 1)
                                {
                                    ["RowId"] = rowId
                                };

                                for (int i = 0; i < meta.ExportFields.Length; i++)
                                {
                                    ref readonly var ef = ref meta.ExportFields[i];
                                    rowDict[ef.Name] = ef.Getter(instance);
                                }

                                rowsList.Add(rowDict);
                            }
                        }

                        if (rowsList.Count > 0)
                        {
                            string baseName = TableNameHelper.NormalizeBaseName(tableName);
                            string outJson = Path.Combine(outputDirectory, $"{baseName}.json");
                            File.WriteAllText(outJson, JsonSerializer.Serialize(rowsList, JsonOpts));
                            totalRowsExported += rowsList.Count;
                            Logger.Log($"+ Экспорт {tableName} -> {Path.GetFileName(outJson)}: {rowsList.Count} строк.");
                        }
                    }

                    Logger.Log($"+ Экспорт успешно завершен! Таблиц: {processedTables}, строк: {totalRowsExported}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"- Ошибка экспорта: {ex.Message}");
                    return false;
                }
                finally
                {
                    IsExporting = false;
                    ExportProgress = 1.0f;
                }
            });
        }

        private static void EnsureFlatDataAssembly()
        {
            if (RoslynCompiler.CompiledAssembly == null)
            {
                string csDir = AppPaths.GeneratedFlatDataDir;
                RoslynCompiler.CompileFlatDataInMemory(csDir, null);
            }
        }
    }
}