using Microsoft.Data.Sqlite;

namespace MSBATranslator.Core.Crypto
{
    public static class DbKeyValidator
    {
        public static bool TestKey(string dbPath, string hexKey, out string message)
        {
            message = string.Empty;

            if (!File.Exists(dbPath))
            {
                message = "Файл БД не найден";
                return false;
            }

            if (string.IsNullOrWhiteSpace(hexKey))
            {
                message = "Ключ пуст";
                return false;
            }

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
                    keyCmd.CommandText = $"PRAGMA key = \"x'{hexKey}'\";";
                    keyCmd.ExecuteNonQuery();
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type='table';";
                    var tableCount = cmd.ExecuteScalar();

                    message = $"+ Ключ подходит, найдено таблиц: {tableCount}";
                    return true;
                }
            }
            catch (Exception ex)
            {
                message = $"- Ошибка открытия базы: {ex.Message}";
                return false;
            }
        }
    }
}