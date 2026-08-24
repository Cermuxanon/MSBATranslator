using MSBATranslator.Core.Config;
using MSBATranslator.Core.Crypto;

namespace MSBATranslator.Core.Database
{
    public static class BackupManager
    {
        public static bool HasBackup()
        {
            return File.Exists(AppPaths.OriginalBackupFile) && new FileInfo(AppPaths.OriginalBackupFile).Length > 0;
        }

        public static bool CreateBackup(string sourceDbPath, out string errorMsg)
        {
            errorMsg = string.Empty;
            try
            {
                if (!File.Exists(sourceDbPath))
                {
                    errorMsg = "ExcelDB.db не найден";
                    return false;
                }

                if (!Directory.Exists(AppPaths.BackupsDir))
                {
                    Directory.CreateDirectory(AppPaths.BackupsDir);
                }

                var srcInfo = new FileInfo(sourceDbPath);
                byte[] bytes = File.ReadAllBytes(sourceDbPath);
                
                uint crc = Crc32Patcher.Compute(bytes);
                string crcHex = crc.ToString("X8");

                File.Copy(sourceDbPath, AppPaths.OriginalBackupFile, true);

                var cfg = AppConfig.Instance;
                cfg.BackupFilePath = AppPaths.OriginalBackupFile;
                cfg.BackupCreatedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                cfg.OriginalFileCreatedAt = srcInfo.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss");
                cfg.OriginalFileSizeBytes = srcInfo.Length;
                cfg.OriginalFileCrc32 = crcHex;
                cfg.Save();

                Logger.Log($"+ Бэкап создан: {AppPaths.OriginalBackupFile} (CRC32: {crcHex})");
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                Logger.Log($"- Ошибка создания бэкапа: {ex.Message}");
                return false;
            }
        }

        public static bool RestoreBackup(string targetDbPath, out string errorMsg)
        {
            errorMsg = string.Empty;
            try
            {
                if (!HasBackup())
                {
                    errorMsg = "Файл бэкапа не найден в Data/Backups";
                    return false;
                }

                string targetDir = Path.GetDirectoryName(targetDbPath) ?? "";
                if (!Directory.Exists(targetDir))
                {
                    Directory.CreateDirectory(targetDir);
                }

                File.Copy(AppPaths.OriginalBackupFile, targetDbPath, true);
                Logger.Log($"+ Оригинальная БД успешно восстановлена в {targetDbPath}");
                return true;
            }
            catch (Exception ex)
            {
                errorMsg = ex.Message;
                Logger.Log($"- Ошибка восстановления: {ex.Message}");
                return false;
            }
        }
    }
}