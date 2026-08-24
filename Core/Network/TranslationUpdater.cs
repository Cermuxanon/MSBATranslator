using System.Net.Http.Headers;
using MSBATranslator.Core.Config;
using MSBATranslator.GUI;

namespace MSBATranslator.Core.Network
{
    public static class TranslationUpdater
    {
        private static readonly HttpClient Http = new();
        public static bool IsCheckingOrDownloading { get; private set; } = false;
        public static string StatusMessage { get; private set; } = string.Empty;
        public static bool IsUpdateAvailable { get; private set; } = false;
        public static string LatestRemoteDate { get; private set; } = string.Empty;
        public static string GameTrackedDate { get; private set; } = string.Empty;
        public static string GameCurrentDate { get; private set; } = string.Empty;

        static TranslationUpdater()
        {
            Http.DefaultRequestHeaders.UserAgent.ParseAdd("MSBATranslator-Client");
        }

        public static async Task CheckForUpdatesAsync(bool isStartupCheck = false)
        {
            if (IsCheckingOrDownloading) return;
            IsCheckingOrDownloading = true;
            StatusMessage = "Проверка обновлений в репозитории...";

            await Task.Run(async () =>
            {
                try
                {
                    var cfg = AppConfig.Instance;
                    string url = cfg.GetEffectiveDownloadUrl();

                    if (string.IsNullOrWhiteSpace(url))
                    {
                        StatusMessage = "URL репозитория не задан.";
                        return;
                    }

                    using var req = new HttpRequestMessage(HttpMethod.Head, url);
                    AttachAuthHeader(req, cfg.RemoteAuthToken, url);

                    if (!string.IsNullOrWhiteSpace(cfg.RemotePatchEtag))
                    {
                        string etag = cfg.RemotePatchEtag.Trim();
                        if (!etag.StartsWith("\"") && !etag.StartsWith("W/\""))
                        {
                            etag = $"\"{etag}\"";
                        }
                        req.Headers.TryAddWithoutValidation("If-None-Match", etag);
                    }

                    using var resp = await Http.SendAsync(req);

                    if (resp.StatusCode == System.Net.HttpStatusCode.NotModified)
                    {
                        IsUpdateAvailable = false;
                        StatusMessage = "Установлена актуальная версия перевода.";
                        if (!isStartupCheck) Logger.Log("+ Перевод актуален (Not Modified).");
                        return;
                    }

                    if (resp.IsSuccessStatusCode)
                    {
                        string newEtag = resp.Headers.ETag?.Tag?.Trim('"') 
                            ?? resp.Headers.ETag?.ToString().Trim('"') 
                            ?? string.Empty;

                        DateTimeOffset? serverTime = resp.Content.Headers.LastModified ?? resp.Headers.Date;
                        string dateStr = serverTime.HasValue
                            ? serverTime.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss")
                            : DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

                        LatestRemoteDate = dateStr;

                        bool fileExists = File.Exists(cfg.GetLocalPatchPath());
                        bool isEtagDifferent = !string.IsNullOrEmpty(newEtag) && newEtag != cfg.RemotePatchEtag;
                        bool isDateDifferent = !string.IsNullOrEmpty(cfg.RemotePatchLastModified) && cfg.RemotePatchLastModified != dateStr;

                        if (!fileExists || isEtagDifferent || isDateDifferent)
                        {
                            IsUpdateAvailable = true;
                            StatusMessage = $"Доступно обновление перевода! ({dateStr})";
                            Logger.Log($"* Найдена новая версия перевода в Git-репозитории ({dateStr})");

                            if (isStartupCheck && !cfg.SuppressTranslationUpdateModal)
                            {
                                RenderImGui.RequestTranslationUpdateModal = true;
                            }
                        }
                        else
                        {
                            IsUpdateAvailable = false;
                            StatusMessage = "Установлена актуальная версия перевода.";
                            if (!isStartupCheck) Logger.Log("+ Перевод актуален.");
                        }
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                    {
                        StatusMessage = "Файл перевода не найден по указанному URL.";
                        if (!isStartupCheck) Logger.Log("- patch_data.json.gz не найден по указанному URL.");
                    }
                    else if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized || resp.StatusCode == System.Net.HttpStatusCode.Forbidden)
                    {
                        StatusMessage = "Ошибка доступа к приватному репозиторию (проверьте Access Token).";
                        if (!isStartupCheck) Logger.Log("- Ошибка доступа к репозиторию. Укажите Access Token в настройках.");
                    }
                }
                catch (Exception ex)
                {
                    StatusMessage = $"Ошибка проверки: {ex.Message}";
                    if (!isStartupCheck) Logger.Log($"- Ошибка проверки обновлений: {ex.Message}");
                }
                finally
                {
                    IsCheckingOrDownloading = false;
                }
            });
        }

        public static async Task<bool> DownloadLatestPatchAsync()
        {
            if (IsCheckingOrDownloading) return false;
            IsCheckingOrDownloading = true;
            StatusMessage = "Загрузка перевода из Git-репозитория...";

            try
            {
                var cfg = AppConfig.Instance;
                string url = cfg.GetEffectiveDownloadUrl();
                string targetPath = cfg.GetLocalPatchPath();

                string? dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                Logger.Log($"* Скачивание архива перевода: {url}");

                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                AttachAuthHeader(req, cfg.RemoteAuthToken, url);

                using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
                resp.EnsureSuccessStatusCode();

                byte[] data = await resp.Content.ReadAsByteArrayAsync();
                await File.WriteAllBytesAsync(targetPath, data);

                DateTimeOffset? serverTime = resp.Content.Headers.LastModified ?? resp.Headers.Date;
                string dateStr = serverTime.HasValue 
                    ? serverTime.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss") 
                    : DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");

                string downloadedEtag = resp.Headers.ETag?.Tag?.Trim('"') 
                    ?? resp.Headers.ETag?.ToString().Trim('"') 
                    ?? string.Empty;

                cfg.RemotePatchEtag = downloadedEtag;
                cfg.RemotePatchLastModified = dateStr;
                cfg.LocalPatchDownloadedAt = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss");
                cfg.Save();

                IsUpdateAvailable = false;
                StatusMessage = $"Перевод успешно загружен ({data.Length / 1024 / 1024:N2} МБ)";
                Logger.Log($"+ Файл перевода сохранен: {targetPath} ({data.Length / 1024 / 1024:N2} МБ)");
                return true;
            }
            catch (Exception ex)
            {
                StatusMessage = $"Ошибка загрузки: {ex.Message}";
                Logger.Log($"- Ошибка скачивания перевода: {ex.Message}");
                return false;
            }
            finally
            {
                IsCheckingOrDownloading = false;
            }
        }

        private static void AttachAuthHeader(HttpRequestMessage req, string? token, string targetUrl)
        {
            if (string.IsNullOrWhiteSpace(token)) return;

            string cleanToken = token.Trim();
            if (targetUrl.Contains("gitlab", StringComparison.OrdinalIgnoreCase))
            {
                req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", cleanToken);
            }
            else
            {
                req.Headers.TryAddWithoutValidation("Authorization", $"Bearer {cleanToken}");
            }
        }

        public static bool CheckIfGameUpdated(out string trackedDate, out string currentDate)
        {
            trackedDate = string.Empty;
            currentDate = string.Empty;
            var cfg = AppConfig.Instance;

            if (string.IsNullOrEmpty(cfg.ExcelDbPath) || !File.Exists(cfg.ExcelDbPath))
            {
                return false;
            }

            var fi = new FileInfo(cfg.ExcelDbPath);
            currentDate = fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss");
            trackedDate = !string.IsNullOrEmpty(cfg.TrackedGameDbLastModified) 
                ? cfg.TrackedGameDbLastModified 
                : (!string.IsNullOrEmpty(cfg.BackupCreatedAt) ? cfg.BackupCreatedAt : "<нет данных>");

            GameTrackedDate = trackedDate;
            GameCurrentDate = currentDate;

            if (!string.IsNullOrEmpty(cfg.TrackedGameDbLastModified) &&
                (cfg.TrackedGameDbLastModified != currentDate || cfg.TrackedGameDbLength != fi.Length))
            {
                return true;
            }

            return false;
        }

        public static void MarkGameDbAsTracked(string dbPath)
        {
            if (!File.Exists(dbPath)) return;
            var fi = new FileInfo(dbPath);
            var cfg = AppConfig.Instance;
            cfg.TrackedGameDbLastModified = fi.LastWriteTime.ToString("dd.MM.yyyy HH:mm:ss");
            cfg.TrackedGameDbLength = fi.Length;
            cfg.Save();
        }
    }
}