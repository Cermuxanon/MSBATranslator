using Microsoft.Win32;

namespace MSBATranslator.Core.Config
{
    public static class GamePathDetector
    {
        public static string? FindSteamGamePath()
        {
            if (!OperatingSystem.IsWindows()) return null;

            try
            {
                string? steamPath = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string;
                if (string.IsNullOrEmpty(steamPath))
                {
                    steamPath = Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string;
                }

                if (string.IsNullOrEmpty(steamPath))
                {
                    steamPath = @"C:\Program Files (x86)\Steam";
                }

                steamPath = steamPath.Replace('/', '\\');

                string defaultBaPath = Path.Combine(steamPath, "steamapps", "common", "BlueArchive");
                if (Directory.Exists(defaultBaPath) && File.Exists(Path.Combine(defaultBaPath, "GameAssembly.dll")))
                {
                    return defaultBaPath;
                }

                string vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
                if (File.Exists(vdfPath))
                {
                    foreach (var line in File.ReadAllLines(vdfPath))
                    {
                        if (line.Contains("\"path\""))
                        {
                            var parts = line.Split('"', StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length >= 4)
                            {
                                string libPath = parts[3].Replace(@"\\", @"\");
                                string checkPath = Path.Combine(libPath, "steamapps", "common", "BlueArchive");
                                if (Directory.Exists(checkPath) && File.Exists(Path.Combine(checkPath, "GameAssembly.dll")))
                                {
                                    return checkPath;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Log($"[Detector] Ошибка поиска пути Steam: {ex.Message}");
            }

            return null;
        }
    }
}