using System.Text.Json;
using MSBATranslator.Core.Network;

namespace MSBATranslator.Core.Config
{
    public class AppConfig
    {
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public string SqlHexKey { get; set; } = string.Empty;
        public string GameDirectory { get; set; } = string.Empty;
        public string GameAssemblyPath { get; set; } = string.Empty;
        public string MetadataPath { get; set; } = string.Empty;
        public string ExcelDbPath { get; set; } = string.Empty;
        public string CustomDumpPath { get; set; } = string.Empty;
        public string BackupFilePath { get; set; } = string.Empty;
        public string BackupCreatedAt { get; set; } = string.Empty;
        public string OriginalFileCreatedAt { get; set; } = string.Empty;
        public long OriginalFileSizeBytes { get; set; } = 0;
        public string OriginalFileCrc32 { get; set; } = string.Empty;
        public string TrackedGameDbLastModified { get; set; } = string.Empty;
        public long TrackedGameDbLength { get; set; } = 0;
        public string RemotePatchUrl { get; set; } = "https://raw.githubusercontent.com/Cermuxanon/MSBATranslator/main/Translation/patch_data.json.gz";
        public string RemoteAuthToken { get; set; } = string.Empty;
        public string RemotePatchEtag { get; set; } = string.Empty;
        public string RemotePatchLastModified { get; set; } = string.Empty;
        public string LocalPatchDownloadedAt { get; set; } = string.Empty;
        public bool SuppressGameUpdateModal { get; set; } = false;
        public bool SuppressTranslationUpdateModal { get; set; } = false;

        public static AppConfig Instance { get; private set; } = Load();

        public static AppConfig Load()
        {
            try
            {
                if (File.Exists(AppPaths.ConfigPath))
                {
                    string json = File.ReadAllText(AppPaths.ConfigPath);
                    var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions);
                    if (cfg != null) return cfg;
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Config] Ошибка чтения конфига: {ex.Message}");
            }

            var newConfig = new AppConfig();
            newConfig.AutoDetectGamePaths();
            newConfig.Save();
            return newConfig;
        }

        public void Save()
        {
            try
            {
                if (!Directory.Exists(AppPaths.DataDir))
                {
                    Directory.CreateDirectory(AppPaths.DataDir);
                }

                string json = JsonSerializer.Serialize(this, JsonOptions);
                File.WriteAllText(AppPaths.ConfigPath, json);
            }
            catch (Exception ex)
            {
                Logger.Log($"[Config] Ошибка сохранения конфига: {ex.Message}");
            }
        }

        public string GetEffectiveDownloadUrl() => GitUrlResolver.ToRawDownloadUrl(RemotePatchUrl);

        public string GetLocalPatchPath() => AppPaths.DefaultPatchFile;

        public void AutoDetectGamePaths()
        {
            string? detectedDir = GamePathDetector.FindSteamGamePath();
            if (!string.IsNullOrEmpty(detectedDir) && Directory.Exists(detectedDir))
            {
                GameDirectory = detectedDir;

                string asm = Path.Combine(detectedDir, "GameAssembly.dll");
                if (File.Exists(asm)) GameAssemblyPath = asm;

                string meta = Path.Combine(detectedDir, "BlueArchive_Data", "il2cpp_data", "Metadata", "global-metadata.dat");
                if (File.Exists(meta)) MetadataPath = meta;

                string db = Path.Combine(detectedDir, "BlueArchive_Data", "StreamingAssets", "PUB", "Resource", "Preload", "TableBundles", "ExcelDB.db");
                if (File.Exists(db)) ExcelDbPath = db;
            }
        }
    }
}