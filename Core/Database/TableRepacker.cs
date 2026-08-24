using System.Text.Json;
using Google.FlatBuffers;
using Microsoft.Data.Sqlite;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Crypto;
using MSBATranslator.Core.FlatData;
using MSBATranslator.Core.Network;

namespace MSBATranslator.Core.Database
{
    public static class TableRepacker
    {
        public static bool IsRepacking { get; private set; } = false;

        public static async Task<bool> RepackDirectoryAsync(string inputJsonDir)
        {
            if (IsRepacking) return false;
            IsRepacking = true;

            return await Task.Run(() =>
            {
                try
                {
                    var cfg = AppConfig.Instance;
                    if (!File.Exists(cfg.BackupFilePath))
                    {
                        Logger.Log("- Оригинальный бэкап не найден. Создайте его на вкладке \"Главная\"");
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(cfg.SqlHexKey))
                    {
                        Logger.Log("- Ключ базы данных SQLCipher Не найден. Получите его на вкладке \"Главная\"");
                        return false;
                    }

                    if (!Directory.Exists(inputJsonDir))
                    {
                        Logger.Log($"- Папка с JSON не найдена: {inputJsonDir}");
                        return false;
                    }

                    var jsonFiles = Directory.GetFiles(inputJsonDir, "*.json");
                    if (jsonFiles.Length == 0)
                    {
                        Logger.Log("- В выбранной папке нет файлов .json.");
                        return false;
                    }

                    if (RoslynCompiler.CompiledAssembly == null)
                    {
                        string csDir = AppPaths.GeneratedFlatDataDir;
                        RoslynCompiler.CompileFlatDataInMemory(csDir, null);
                    }

                    var assembly = RoslynCompiler.CompiledAssembly;
                    if (assembly == null)
                    {
                        Logger.Log("- Не удалось загрузить сборку FlatData.");
                        return false;
                    }

                    string tempWorkingDb = Path.Combine(AppPaths.DataDir, "ExcelDB_repack_temp.db");
                    File.Copy(cfg.BackupFilePath, tempWorkingDb, true);

                    Logger.Log($"* Старт универсальной запаковки {jsonFiles.Length} файлов JSON в БД");
                    SQLitePCL.Batteries_V2.Init();

                    var csb = new SqliteConnectionStringBuilder
                    {
                        DataSource = tempWorkingDb,
                        Mode = SqliteOpenMode.ReadWrite,
                        Pooling = false
                    };

                    int totalUpdatedRows = 0;

                    using (var conn = new SqliteConnection(csb.ConnectionString))
                    {
                        conn.Open();

                        using (var keyCmd = conn.CreateCommand())
                        {
                            keyCmd.CommandText = $"PRAGMA key = \"x'{cfg.SqlHexKey}'\";";
                            keyCmd.ExecuteNonQuery();
                        }

                        using (var pragmaCmd = conn.CreateCommand())
                        {
                            pragmaCmd.CommandText = "PRAGMA synchronous = OFF; PRAGMA journal_mode = MEMORY; PRAGMA temp_store = MEMORY; PRAGMA cache_size = 100000;";
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
                        var sharedFbb = new FlatBufferBuilder(65536);

                        foreach (var jsonFile in jsonFiles)
                        {
                            string fileName = Path.GetFileNameWithoutExtension(jsonFile);
                            
                            string? matchedTable = TableNameHelper.MatchDbTable(fileName, existingDbTables);
                            if (matchedTable == null)
                            {
                                string baseName = TableNameHelper.NormalizeBaseName(fileName);
                                matchedTable = $"{baseName}DBSchema";
                            }

                            var classType = TableNameHelper.MatchFlatDataType(matchedTable, assembly);
                            if (classType == null)
                            {
                                Logger.Log($"* [Пропуск] {fileName}: тип FlatData не найден в сборке.");
                                continue;
                            }

                            string jsonText = File.ReadAllText(jsonFile);
                            using var doc = JsonDocument.Parse(jsonText);
                            var rowsToRepack = ExtractRowsFromJson(doc.RootElement);

                            if (rowsToRepack.Count == 0) continue;

                            var meta = FastMetaCache.GetOrCreate(fileName, matchedTable, classType);
                            if (meta == null) continue;

                            var existingBlobs = new Dictionary<long, byte[]>();
                            using (var readCmd = conn.CreateCommand())
                            {
                                readCmd.Transaction = transaction;
                                readCmd.CommandText = $"SELECT RowId, Bytes FROM [{matchedTable}];";
                                using var reader = readCmd.ExecuteReader();
                                while (reader.Read())
                                {
                                    long rId = reader.GetInt64(0);
                                    if (reader["Bytes"] is byte[] blob && blob.Length > 0)
                                    {
                                        existingBlobs[rId] = blob;
                                    }
                                }
                            }

                            using var updateCmd = conn.CreateCommand();
                            updateCmd.Transaction = transaction;
                            updateCmd.CommandText = $"UPDATE [{matchedTable}] SET Bytes = @bytes WHERE RowId = @rowId;";
                            var pBytes = updateCmd.Parameters.Add("@bytes", SqliteType.Blob);
                            var pRowId = updateCmd.Parameters.Add("@rowId", SqliteType.Integer);

                            using var insertCmd = conn.CreateCommand();
                            insertCmd.Transaction = transaction;
                            insertCmd.CommandText = $"INSERT INTO [{matchedTable}] (RowId, Bytes) VALUES (@rowId, @bytes);";
                            var pInsRowId = insertCmd.Parameters.Add("@rowId", SqliteType.Integer);
                            var pInsBytes = insertCmd.Parameters.Add("@bytes", SqliteType.Blob);

                            var strOffsetsBuf = new StringOffset[meta.StringFields.Length];
                            var vecOffsetsBuf = new VectorOffset[meta.VectorFields.Length];
                            int tableRowsPatched = 0;

                            foreach (var (rowId, jsonRow) in rowsToRepack)
                            {
                                object? existingInstance = null;
                                if (existingBlobs.TryGetValue(rowId, out var origBlob))
                                {
                                    var bb = new ByteBuffer(origBlob);
                                    existingInstance = meta.GetRootInvoker(bb);
                                }

                                byte[] newBlob = BuildFlatBufferFromJson(sharedFbb, meta, jsonRow, existingInstance, strOffsetsBuf, vecOffsetsBuf);
                                if (newBlob.Length > 0)
                                {
                                    pBytes.Value = newBlob;
                                    pRowId.Value = rowId;
                                    int affected = updateCmd.ExecuteNonQuery();

                                    if (affected == 0)
                                    {
                                        pInsRowId.Value = rowId;
                                        pInsBytes.Value = newBlob;
                                        insertCmd.ExecuteNonQuery();
                                    }

                                    tableRowsPatched++;
                                }
                            }

                            if (tableRowsPatched > 0)
                            {
                                totalUpdatedRows += tableRowsPatched;
                                Logger.Log($"+ Таблица {matchedTable}: запаковано {tableRowsPatched} строк.");
                            }
                        }

                        transaction.Commit();
                        conn.Close();
                    }

                    SqliteConnection.ClearAllPools();

                    string? gameDir = Path.GetDirectoryName(cfg.ExcelDbPath);
                    if (!string.IsNullOrEmpty(gameDir) && !Directory.Exists(gameDir))
                    {
                        Directory.CreateDirectory(gameDir);
                    }

                    File.Copy(tempWorkingDb, cfg.ExcelDbPath, true);

                    Logger.Log("* Математическая подгонка CRC32 под бэкап");
                    Crc32Patcher.SyncFileChecksum(cfg.ExcelDbPath, cfg.BackupFilePath);

                    TranslationUpdater.MarkGameDbAsTracked(cfg.ExcelDbPath);
                    Logger.Log($"+ Универсальная запаковка успешно завершена! Обновлено/добавлено строк: {totalUpdatedRows}");
                    return true;
                }
                catch (Exception ex)
                {
                    Logger.Log($"- Ошибка универсальной запаковки: {ex.Message}");
                    return false;
                }
                finally
                {
                    IsRepacking = false;
                }
            });
        }

        private static List<(long RowId, JsonElement RowData)> ExtractRowsFromJson(JsonElement root)
        {
            var result = new List<(long, JsonElement)>();

            if (root.ValueKind == JsonValueKind.Array)
            {
                long autoRowId = 1;
                foreach (var el in root.EnumerateArray())
                {
                    if (el.ValueKind != JsonValueKind.Object) continue;

                    long rowId = autoRowId;
                    if (TryGetJsonProperty(el, "RowId", out var rProp) && rProp.TryGetInt64(out long parsedId))
                    {
                        rowId = parsedId;
                        autoRowId = Math.Max(autoRowId, rowId + 1);
                    }
                    else
                    {
                        autoRowId++;
                    }

                    result.Add((rowId, el));
                }
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in root.EnumerateObject())
                {
                    if (long.TryParse(prop.Name, out long rId) && prop.Value.ValueKind == JsonValueKind.Object)
                    {
                        result.Add((rId, prop.Value));
                    }
                }
            }

            return result;
        }

        private static bool TryGetJsonProperty(JsonElement obj, string propName, out JsonElement value)
        {
            if (obj.ValueKind == JsonValueKind.Object)
            {
                if (obj.TryGetProperty(propName, out value)) return true;

                foreach (var p in obj.EnumerateObject())
                {
                    if (string.Equals(p.Name, propName, StringComparison.OrdinalIgnoreCase))
                    {
                        value = p.Value;
                        return true;
                    }
                }
            }
            value = default;
            return false;
        }

        private static byte[] BuildFlatBufferFromJson(
            FlatBufferBuilder fbb,
            FastTableMeta meta,
            JsonElement jsonRow,
            object? existingInstance,
            StringOffset[] strOffsetsBuf,
            VectorOffset[] vecOffsetsBuf)
        {
            fbb.Clear();

            for (int i = 0; i < meta.StringFields.Length; i++)
            {
                ref readonly var sf = ref meta.StringFields[i];
                string strVal = "";

                if (TryGetJsonProperty(jsonRow, sf.PropName, out var jp))
                {
                    if (jp.ValueKind == JsonValueKind.String)
                        strVal = jp.GetString() ?? "";
                    else if (jp.ValueKind != JsonValueKind.Null && jp.ValueKind != JsonValueKind.Undefined)
                        strVal = jp.ToString();
                }
                else if (existingInstance != null)
                {
                    strVal = sf.Getter(existingInstance) ?? "";
                }

                strOffsetsBuf[i] = fbb.CreateString(strVal);
            }

            for (int i = 0; i < meta.VectorFields.Length; i++)
            {
                ref readonly var vf = ref meta.VectorFields[i];
                if (TryGetJsonProperty(jsonRow, vf.PropName, out var jVec) && jVec.ValueKind == JsonValueKind.Array)
                {
                    vecOffsetsBuf[i] = BuildVectorFromJsonArray(fbb, jVec);
                }
                else if (existingInstance != null)
                {
                    int len = vf.LengthGetter(existingInstance);
                    if (len > 0)
                    {
                        vecOffsetsBuf[i] = FastMetaCache.BuildVectorFromInstanceFast(fbb, existingInstance, vf.ItemGetter, len);
                    }
                    else
                    {
                        vecOffsetsBuf[i] = default;
                    }
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
                ref readonly var sf = ref meta.ScalarFields[i];

                if (TryGetJsonProperty(jsonRow, sf.PropName, out var jp) && jp.ValueKind != JsonValueKind.Null)
                {
                    object? val = ConvertJsonScalar(jp, sf.ParamType);
                    if (val != null)
                    {
                        sf.AddInvoker(fbb, val);
                        continue;
                    }
                }

                if (existingInstance != null)
                {
                    sf.DirectCopy(fbb, existingInstance);
                }
                else
                {
                    object defVal = Activator.CreateInstance(sf.ParamType)!;
                    sf.AddInvoker(fbb, defVal);
                }
            }

            int offsetVal = meta.EndInvoker(fbb);
            fbb.Finish(offsetVal);
            return fbb.SizedByteArray();
        }

        private static VectorOffset BuildVectorFromJsonArray(FlatBufferBuilder fbb, JsonElement arrayEl)
        {
            int len = arrayEl.GetArrayLength();
            if (len == 0) return default;

            var first = arrayEl[0];

            if (first.ValueKind == JsonValueKind.Number || (first.ValueKind == JsonValueKind.String && double.TryParse(first.GetString(), out _)))
            {
                bool isFloat = false;
                bool isLong = false;

                for (int i = 0; i < len; i++)
                {
                    var item = arrayEl[i];
                    if (item.ValueKind == JsonValueKind.Number)
                    {
                        if (item.ToString().Contains('.')) { isFloat = true; break; }
                        if (!item.TryGetInt32(out _)) { isLong = true; }
                    }
                    else if (item.ValueKind == JsonValueKind.String && item.GetString() is string s)
                    {
                        if (s.Contains('.')) { isFloat = true; break; }
                        if (!int.TryParse(s, out _)) { isLong = true; }
                    }
                }

                if (isFloat)
                {
                    fbb.StartVector(4, len, 4);
                    for (int j = len - 1; j >= 0; j--)
                    {
                        var el = arrayEl[j];
                        float val = 0f;
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetSingle(out float f)) val = f;
                        else if (float.TryParse(el.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float pf)) val = pf;
                        fbb.AddFloat(val);
                    }
                    return fbb.EndVector();
                }
                else if (isLong)
                {
                    fbb.StartVector(8, len, 8);
                    for (int j = len - 1; j >= 0; j--)
                    {
                        var el = arrayEl[j];
                        long val = 0;
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out long l)) val = l;
                        else if (long.TryParse(el.GetString(), out long pl)) val = pl;
                        fbb.AddLong(val);
                    }
                    return fbb.EndVector();
                }
                else
                {
                    fbb.StartVector(4, len, 4);
                    for (int j = len - 1; j >= 0; j--)
                    {
                        var el = arrayEl[j];
                        int val = 0;
                        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt32(out int iv)) val = iv;
                        else if (int.TryParse(el.GetString(), out int piv)) val = piv;
                        fbb.AddInt(val);
                    }
                    return fbb.EndVector();
                }
            }
            else if (first.ValueKind == JsonValueKind.String)
            {
                var sOffsets = new StringOffset[len];
                for (int j = 0; j < len; j++)
                {
                    string s = arrayEl[j].GetString() ?? "";
                    sOffsets[j] = fbb.CreateString(s);
                }
                fbb.StartVector(4, len, 4);
                for (int j = len - 1; j >= 0; j--) fbb.AddOffset(sOffsets[j].Value);
                return fbb.EndVector();
            }
            else if (first.ValueKind == JsonValueKind.True || first.ValueKind == JsonValueKind.False)
            {
                fbb.StartVector(1, len, 1);
                for (int j = len - 1; j >= 0; j--) fbb.AddBool(arrayEl[j].ValueKind == JsonValueKind.True);
                return fbb.EndVector();
            }

            return default;
        }

        private static object? ConvertJsonScalar(JsonElement el, Type targetType)
        {
            if (el.ValueKind == JsonValueKind.Number)
            {
                if (targetType == typeof(int) && el.TryGetInt32(out int i)) return i;
                if (targetType == typeof(uint) && el.TryGetUInt32(out uint u)) return u;
                if (targetType == typeof(long) && el.TryGetInt64(out long l)) return l;
                if (targetType == typeof(ulong) && el.TryGetUInt64(out ulong ul)) return ul;
                if (targetType == typeof(short) && el.TryGetInt16(out short s)) return s;
                if (targetType == typeof(ushort) && el.TryGetUInt16(out ushort us)) return us;
                if (targetType == typeof(byte) && el.TryGetByte(out byte b)) return b;
                if (targetType == typeof(sbyte) && el.TryGetSByte(out sbyte sb)) return sb;
                if (targetType == typeof(float) && el.TryGetSingle(out float f)) return f;
                if (targetType == typeof(double) && el.TryGetDouble(out double d)) return d;
                if (targetType.IsEnum && el.TryGetInt32(out int ev)) return Enum.ToObject(targetType, ev);
            }
            else if (el.ValueKind == JsonValueKind.String)
            {
                string s = el.GetString() ?? "";
                if (targetType == typeof(int) && int.TryParse(s, out int i)) return i;
                if (targetType == typeof(uint) && uint.TryParse(s, out uint u)) return u;
                if (targetType == typeof(long) && long.TryParse(s, out long l)) return l;
                if (targetType == typeof(ulong) && ulong.TryParse(s, out ulong ul)) return ul;
                if (targetType == typeof(short) && short.TryParse(s, out short sh)) return sh;
                if (targetType == typeof(ushort) && ushort.TryParse(s, out ushort ush)) return ush;
                if (targetType == typeof(byte) && byte.TryParse(s, out byte b)) return b;
                if (targetType == typeof(sbyte) && sbyte.TryParse(s, out sbyte sb)) return sb;
                if (targetType == typeof(float) && float.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out float f)) return f;
                if (targetType == typeof(double) && double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double d)) return d;
                if (targetType == typeof(bool) && bool.TryParse(s, out bool bv)) return bv;
                if (targetType.IsEnum && Enum.TryParse(targetType, s, true, out object? parsed)) return parsed;
            }
            else if (el.ValueKind == JsonValueKind.True || el.ValueKind == JsonValueKind.False)
            {
                if (targetType == typeof(bool)) return el.GetBoolean();
                if (targetType == typeof(int)) return el.GetBoolean() ? 1 : 0;
            }

            return null;
        }
    }
}