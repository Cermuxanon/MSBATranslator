using System.IO.Compression;
using System.Net.Http.Json;
using System.Text.Json.Nodes;
using MSBATranslator.Core.Config;

namespace MSBATranslator.Core.FlatData
{
    public static class Il2CppDownloader
    {
        private static readonly string InspectorDir = AppPaths.Il2CppInspectorDir;
        private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = true });

        private const string RepoOwner = "LukeFZ";
        private const string RepoName = "Il2CppInspectorRedux";
        private const string TargetZipName = "Il2CppInspectorRedux.Legacy.CLI-win-x64.zip";
        private const string TargetExeName = "Il2CppInspector.exe";
        private static readonly string LatestReleaseDownloadUrl = $"https://github.com/{RepoOwner}/{RepoName}/releases/latest/download/{TargetZipName}";

        static Il2CppDownloader()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("MSBATranslator-Client");
        }

        public static bool IsInstalled() => File.Exists(GetExePath());

        public static string GetExePath()
        {
            string mainExe = Path.Combine(InspectorDir, TargetExeName);
            if (File.Exists(mainExe)) return mainExe;

            if (Directory.Exists(InspectorDir))
            {
                var found = Directory.GetFiles(InspectorDir, TargetExeName, SearchOption.AllDirectories);
                if (found.Length > 0) return found[0];
            }

            return mainExe;
        }

        public static async Task<bool> DownloadAndInstallAsync()
        {
            try
            {
                Logger.Log("* Поиск актуального релиза Il2CppInspector Legacy CLI на GitHub");

                string downloadUrl = LatestReleaseDownloadUrl;
                string versionTag = "latest";

                try
                {
                    var releaseJson = await Http.GetFromJsonAsync<JsonObject>($"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest");
                    versionTag = releaseJson?["tag_name"]?.ToString() ?? "latest";

                    if (releaseJson?["assets"] is JsonArray assets)
                    {
                        foreach (var asset in assets)
                        {
                            string? name = asset?["name"]?.ToString();
                            string? url = asset?["browser_download_url"]?.ToString();

                            if (!string.IsNullOrEmpty(name) && !string.IsNullOrEmpty(url))
                            {

                                if (name.Contains("Legacy", StringComparison.OrdinalIgnoreCase) &&
                                    name.Contains("CLI", StringComparison.OrdinalIgnoreCase) &&
                                    name.Contains("win-x64", StringComparison.OrdinalIgnoreCase) &&
                                    name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                                {
                                    downloadUrl = url;
                                    Logger.Log($"+ Найден релиз: {versionTag} ({name})");
                                    break;
                                }
                            }
                        }
                    }
                }
                catch
                {
                    Logger.Log($"* Запрос к API пропущен, скачивание через прямую ссылку на актуальный релиз");
                }

                Logger.Log($"* Скачивание архива: {downloadUrl}");
                byte[] zipBytes = await Http.GetByteArrayAsync(downloadUrl);

                if (Directory.Exists(InspectorDir))
                {
                    Directory.Delete(InspectorDir, true);
                }
                Directory.CreateDirectory(InspectorDir);

                using (var ms = new MemoryStream(zipBytes))
                using (var archive = new ZipArchive(ms))
                {
                    archive.ExtractToDirectory(InspectorDir, true);
                }

                string exePath = GetExePath();
                if (File.Exists(exePath))
                {
                    Logger.Log($"+ Il2CppInspector успешно установлен ({versionTag}): {exePath}");
                    return true;
                }

                Logger.Log($"- Не удалось найти {TargetExeName} после распаковки архива.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка загрузки Il2CppInspector: {ex.Message}");
                return false;
            }
        }
    }
}