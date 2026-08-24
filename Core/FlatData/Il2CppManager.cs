using System.Diagnostics;
using MSBATranslator.Core.Config;

namespace MSBATranslator.Core.FlatData
{
    public static class Il2CppManager
    {
        public static readonly string DummyDllDir = AppPaths.DummyDllDir;
        public static readonly string TargetBlueArchiveDll = AppPaths.TargetBlueArchiveDll;

        public static bool HasDummyDll()
        {
            return File.Exists(TargetBlueArchiveDll) && new FileInfo(TargetBlueArchiveDll).Length > 0;
        }

        public static async Task<bool> RunDumperAsync(string binPath, string metaPath)
        {
            string exePath = Il2CppDownloader.GetExePath();

            if (!File.Exists(exePath))
            {
                Logger.Log($"- Исполняемый файл Il2CppInspector не найден: {exePath}");
                return false;
            }

            if (!File.Exists(binPath))
            {
                Logger.Log($"- GameAssembly.dll не найден: {binPath}");
                return false;
            }

            if (!File.Exists(metaPath))
            {
                Logger.Log($"- global-metadata.dat не найден: {metaPath}");
                return false;
            }

            string tempOutDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "Temp_Il2Cpp_Out");

            try
            {
                if (Directory.Exists(tempOutDir)) Directory.Delete(tempOutDir, true);
                Directory.CreateDirectory(tempOutDir);

                if (!Directory.Exists(DummyDllDir)) Directory.CreateDirectory(DummyDllDir);

                string args = $"-i \"{binPath}\" -m \"{metaPath}\" --select-outputs -d \"{tempOutDir}\" --suppress-dll-metadata";

                Logger.Log($"* Запуск процесса: {Path.GetFileName(exePath)}");
                Logger.Log($"* Аргументы: {args}");

                var psi = new ProcessStartInfo
                {
                    FileName = exePath,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = Path.GetDirectoryName(exePath) ?? ""
                };

                using var process = new Process { StartInfo = psi };

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Logger.Log($"  Il2Cpp: {e.Data.Trim()}");
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                        Logger.Log($"  Il2Cpp !: {e.Data.Trim()}");
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                await process.WaitForExitAsync();

                string producedDll = Path.Combine(tempOutDir, "BlueArchive.dll");
                if (!File.Exists(producedDll))
                {
                    var foundDlls = Directory.GetFiles(tempOutDir, "*.dll", SearchOption.AllDirectories);
                    if (foundDlls.Length > 0) producedDll = foundDlls[0];
                }

                if (File.Exists(producedDll))
                {
                    File.Copy(producedDll, TargetBlueArchiveDll, true);
                    Logger.Log($"+ Dummy DLL успешно получена и сохранена: {TargetBlueArchiveDll}");
                    return true;
                }

                Logger.Log("- BlueArchive.dll не была сгенерирована инспектором в выходной папке.");
                return false;
            }
            catch (Exception ex)
            {
                Logger.Log($"- Ошибка запуска Il2CppInspector: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (Directory.Exists(tempOutDir)) Directory.Delete(tempOutDir, true);
                }
                catch { }
            }
        }
    }
}