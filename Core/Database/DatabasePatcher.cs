using System.IO.Compression;
using System.Reflection;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Google.FlatBuffers;
using MSBATranslator.Core.Crypto;
using MSBATranslator.Core.Config;

namespace MSBATranslator.Core.Database
{
    public static class DatabasePatcher
    {
        public static HashSet<string> PeekTablesInPatch(string patchFilePath)
        {
            var foundTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!File.Exists(patchFilePath)) return foundTables;

            try
            {
                using var stream = File.OpenRead(patchFilePath);
                Stream inputStream = stream;

                if (patchFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    inputStream = new GZipStream(stream, CompressionMode.Decompress);
                }

                using var reader = new StreamReader(inputStream);
                string jsonHeader = reader.ReadToEnd();
                using var doc = JsonDocument.Parse(jsonHeader);

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    string baseName = TableNameHelper.NormalizeBaseName(prop.Name);
                    foundTables.Add(baseName);
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Patcher] Ошибка чтения содержимого патча: {ex.Message}");
            }

            return foundTables;
        }

        public static bool ApplyTranslationPatch(
            string backupDbPath,
            string targetGameDbPath,
            string hexKey,
            string patchFilePath,
            Assembly flatDataAssembly,
            IEnumerable<string>? selectedTableBaseNames = null)
        {
            if (!File.Exists(backupDbPath))
            {
                Logger.Log($"- Файл оригинального бэкапа не найден: {backupDbPath}");
                return false;
            }

            if (!File.Exists(patchFilePath))
            {
                Logger.Log($"- Файл перевода не найден: {patchFilePath}");
                return false;
            }

            try
            {
                HashSet<string>? allowedBaseNames = selectedTableBaseNames != null
                    ? new HashSet<string>(selectedTableBaseNames.Select(TableNameHelper.NormalizeBaseName), StringComparer.OrdinalIgnoreCase)
                    : null;

                Logger.Log("* Чтение и распаковка файла перевода");
                string deltaJson;

                if (patchFilePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
                {
                    using var fs = File.OpenRead(patchFilePath);
                    using var gz = new GZipStream(fs, CompressionMode.Decompress);
                    using var reader = new StreamReader(gz);
                    deltaJson = reader.ReadToEnd();
                }
                else
                {
                    deltaJson = File.ReadAllText(patchFilePath);
                }

                using var doc = JsonDocument.Parse(deltaJson);
                var root = doc.RootElement;

                string tempWorkingDb = Path.Combine(AppPaths.DataDir, "ExcelDB_patched.db");
                File.Copy(backupDbPath, tempWorkingDb, true);

                Logger.Log("* Открытие БД SQLCipher");
                SQLitePCL.Batteries_V2.Init();

                var csb = new SqliteConnectionStringBuilder
                {
                    DataSource = tempWorkingDb,
                    Mode = SqliteOpenMode.ReadWrite,
                    Pooling = false
                };

                var swTotal = System.Diagnostics.Stopwatch.StartNew();

                using (var conn = new SqliteConnection(csb.ConnectionString))
                {
                    conn.Open();

                    using (var keyCmd = conn.CreateCommand())
                    {
                        keyCmd.CommandText = $"PRAGMA key = \"x'{hexKey}'\";";
                        keyCmd.ExecuteNonQuery();
                    }

                    using (var pragmaCmd = conn.CreateCommand())
                    {
                        pragmaCmd.CommandText = "PRAGMA synchronous = OFF; PRAGMA journal_mode = OFF; PRAGMA locking_mode = EXCLUSIVE; PRAGMA temp_store = MEMORY; PRAGMA cache_size = -131072;";
                        pragmaCmd.ExecuteNonQuery();
                    }

                    var existingDbTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var listCmd = conn.CreateCommand())
                    {
                        listCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table';";
                        using var r = listCmd.ExecuteReader();
                        while (r.Read()) existingDbTables.Add(r.GetString(0));
                    }

                    using var transaction = conn.BeginTransaction();
                    int totalUpdatedRows = 0;

                    var sharedFbb = new FlatBufferBuilder(65536);
                    var groupCounters = new Dictionary<string, int>(1024);
                    var longCounters = new Dictionary<long, int>(1024);

                    foreach (var tableProp in root.EnumerateObject())
                    {
                        string rawJsonTableName = tableProp.Name;
                        string baseName = TableNameHelper.NormalizeBaseName(rawJsonTableName);

                        if (allowedBaseNames != null && !allowedBaseNames.Contains(baseName))
                        {
                            continue;
                        }

                        var tableData = tableProp.Value;
                        string? matchedDbTable = TableNameHelper.MatchDbTable(rawJsonTableName, existingDbTables);
                        if (matchedDbTable == null) continue;

                        var classType = TableNameHelper.MatchFlatDataType(matchedDbTable, flatDataAssembly);
                        if (classType == null) continue;

                        var meta = FastMetaCache.GetOrCreate(rawJsonTableName, matchedDbTable, classType);
                        if (meta == null) continue;

                        var tableDict = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                        foreach (var p in tableData.EnumerateObject())
                        {
                            tableDict[p.Name] = p.Value;
                        }

                        var strOffsetsBuf = new StringOffset[meta.StringFields.Length];
                        var vecOffsetsBuf = new VectorOffset[meta.VectorFields.Length];

                        var allBlobs = new List<(long rowId, byte[] blob)>();
                        using (var readCmd = conn.CreateCommand())
                        {
                            readCmd.CommandText = $"SELECT RowId, Bytes FROM [{matchedDbTable}];";
                            using var reader = readCmd.ExecuteReader();
                            while (reader.Read())
                            {
                                long rId = reader.GetInt64(0);
                                if (reader["Bytes"] is byte[] bData && bData.Length > 0)
                                {
                                    allBlobs.Add((rId, bData));
                                }
                            }
                        }

                        using var updateCmd = conn.CreateCommand();
                        updateCmd.Transaction = transaction;
                        updateCmd.CommandText = $"UPDATE [{matchedDbTable}] SET Bytes = @bytes WHERE RowId = @rowId;";
                        var pBytes = updateCmd.Parameters.Add("@bytes", SqliteType.Blob);
                        var pRowId = updateCmd.Parameters.Add("@rowId", SqliteType.Integer);

                        groupCounters.Clear();
                        longCounters.Clear();
                        int tableUpdatedRows = 0;

                        foreach (var (rowId, blob) in allBlobs)
                        {
                            var bb = new ByteBuffer(blob);
                            object instance = meta.GetRootInvoker(bb);

                            string rowKey = meta.KeyExtractor(instance, groupCounters, longCounters);

                            if (tableDict.TryGetValue(rowKey, out var transElem))
                            {
                                byte[] newBlob = RebuildFlatBufferBlobZeroAlloc(sharedFbb, instance, meta, transElem, strOffsetsBuf, vecOffsetsBuf);
                                if (newBlob.Length > 0)
                                {
                                    pBytes.Value = newBlob;
                                    pRowId.Value = rowId;
                                    updateCmd.ExecuteNonQuery();
                                    tableUpdatedRows++;
                                }
                            }
                        }

                        if (tableUpdatedRows > 0)
                        {
                            totalUpdatedRows += tableUpdatedRows;
                            Logger.Log($"+ Таблица {matchedDbTable}: обновлено {tableUpdatedRows} строк.");
                        }
                    }

                    transaction.Commit();
                    conn.Close();

                    swTotal.Stop();
                    Logger.Log($"+ Слияние завершено за {swTotal.Elapsed.TotalSeconds:N2} сек. Обновлено записей: {totalUpdatedRows}");
                }

                SqliteConnection.ClearAllPools();

                string? gameDir = Path.GetDirectoryName(targetGameDbPath);
                if (!string.IsNullOrEmpty(gameDir) && !Directory.Exists(gameDir))
                {
                    Directory.CreateDirectory(gameDir);
                }

                File.Copy(tempWorkingDb, targetGameDbPath, true);

                Logger.Log("* Подгонка CRC32 файла БД под эталон");
                bool crcPatched = Crc32Patcher.SyncFileChecksum(targetGameDbPath, backupDbPath);

                if (crcPatched)
                {
                    Logger.Log($"+ СЛИЯНИЕ ПЕРЕВОДА УСПЕШНО ЗАВЕРШЕНО");
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка слияния перевода: {ex.Message}");
                return false;
            }
        }

        private static byte[] RebuildFlatBufferBlobZeroAlloc(
            FlatBufferBuilder fbb,
            object instance, 
            FastTableMeta meta, 
            JsonElement transElem,
            StringOffset[] strOffsetsBuf,
            VectorOffset[] vecOffsetsBuf)
        {
            fbb.Clear();

            string? singleReplacement = null;
            Dictionary<string, string>? multiReplacements = null;

            if (transElem.ValueKind == JsonValueKind.String)
            {
                singleReplacement = transElem.GetString();
            }
            else if (transElem.ValueKind == JsonValueKind.Object)
            {
                multiReplacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var p in transElem.EnumerateObject())
                {
                    multiReplacements[p.Name] = p.Value.GetString() ?? "";
                }
            }

            for (int i = 0; i < meta.StringFields.Length; i++)
            {
                ref readonly var sf = ref meta.StringFields[i];
                string strVal = "";

                if (singleReplacement != null && sf.IsTargetText)
                {
                    strVal = singleReplacement;
                }
                else if (multiReplacements != null && multiReplacements.TryGetValue(sf.PropName, out var repl))
                {
                    strVal = repl;
                }
                else
                {
                    strVal = sf.Getter(instance);
                }

                strOffsetsBuf[i] = fbb.CreateString(strVal);
            }

            for (int i = 0; i < meta.VectorFields.Length; i++)
            {
                ref readonly var vf = ref meta.VectorFields[i];
                int len = vf.LengthGetter(instance);
                if (len > 0)
                {
                    vecOffsetsBuf[i] = FastMetaCache.BuildVectorFromInstanceFast(fbb, instance, vf.ItemGetter, len);
                }
                else
                {
                    vecOffsetsBuf[i] = default;
                }
            }

            meta.StartInvoker(fbb);

            for (int i = 0; i < meta.StringFields.Length; i++)
            {
                meta.StringFields[i].AddInvoker(fbb, strOffsetsBuf[i]);
            }

            for (int i = 0; i < meta.VectorFields.Length; i++)
            {
                var vOff = vecOffsetsBuf[i];
                if (vOff.Value != 0)
                {
                    meta.VectorFields[i].AddInvoker(fbb, vOff);
                }
            }

            for (int i = 0; i < meta.ScalarFields.Length; i++)
            {
                meta.ScalarFields[i].DirectCopy(fbb, instance);
            }

            int offsetVal = meta.EndInvoker(fbb);
            fbb.Finish(offsetVal);
            return fbb.SizedByteArray();
        }
    }
}