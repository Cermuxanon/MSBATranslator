using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.FlatData;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        private static bool _isProcessingFlatData = false;
        private static readonly string GeneratedOutputDir = AppPaths.GeneratedFlatDataDir;
        private static void RenderTabFlatData()
        {
            var cfg = AppConfig.Instance;
            bool isInspectorReady = Il2CppDownloader.IsInstalled();
            bool hasDummyDll = Il2CppManager.HasDummyDll();
            var res = FlatDataGenerator.LastResult;

            ImGui.Spacing();
            ImGui.TextDisabled("Генерация структур FlatData");
            ImGui.Spacing();

            if (isInspectorReady)
            {
                ImGui.TextColored(ColorGreen, "+ Il2CppInspector CLI найден");
                ImGui.SameLine();
                ImGui.TextColored(ColorMuted, $"| {Il2CppDownloader.GetExePath()}");
            }
            else
            {
                ImGui.TextColored(ColorYellow, "* Il2CppInspector CLI еще не скачан.");
                ImGui.SameLine();
                if (ImGui.Button("Автоматически загрузить Il2CppInspector"))
                {
                    _isProcessingFlatData = true;
                    Task.Run(async () =>
                    {
                        await Il2CppDownloader.DownloadAndInstallAsync();
                        _isProcessingFlatData = false;
                    });
                }
            }

            ImGui.Spacing();

            if (hasDummyDll)
            {
                ImGui.TextColored(ColorGreen, "+ BlueArchive.dll найден");
                ImGui.SameLine();
                var info = new FileInfo(Il2CppManager.TargetBlueArchiveDll);
                ImGui.TextColored(ColorMuted, $"| Размер: {info.Length / 1024 / 1024:N1} МБ | Дата: {info.LastWriteTime:dd.MM.yyyy HH:mm}");
            }
            else
            {
                ImGui.TextColored(ColorYellow, "* BlueArchive.dll отсутствует в Data/DummyDll.");
            }

            ImGui.Spacing();

            if (res != null && res.TotalGenerated > 0)
            {
                ImGui.TextColored(ColorGreen, $"+ Сгенерировано структур FlatData: {res.TotalGenerated}");
                ImGui.SameLine();
                ImGui.TextColored(ColorMuted, $"| Enums: {res.GeneratedEnums.Count} | Structs: {res.GeneratedStructs.Count} | Tables**: {res.GeneratedTables.Count}");
            }
            else if (Directory.Exists(GeneratedOutputDir) && Directory.GetFiles(GeneratedOutputDir, "*.cs").Length > 0)
            {
                int count = Directory.GetFiles(GeneratedOutputDir, "*.cs").Length;
                ImGui.TextColored(ColorGreen, $"+ Найдено сгенерированных C# файлов в папке: {count}");
            }
            else
            {
                ImGui.TextColored(ColorMuted, "* FlatData еще не сгенерирован.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (_isProcessingFlatData)
            {
                ImGui.TextColored(ColorYellow, "* Идет генерация метаданных/структур C#.");
            }
            else
            {
                if (ImGui.Button("Выполнить генерацию FlatData", new Vector2(320, 40)))
                {
                    _isProcessingFlatData = true;
                    Task.Run(async () =>
                    {
                        try
                        {
                            if (!Il2CppDownloader.IsInstalled())
                            {
                                bool downloaded = await Il2CppDownloader.DownloadAndInstallAsync();
                                if (!downloaded) return;
                            }

                            bool dumped = await Il2CppManager.RunDumperAsync(cfg.GameAssemblyPath, cfg.MetadataPath);
                            if (dumped)
                            {
                                FlatDataGenerator.GenerateFromCecil(Il2CppManager.TargetBlueArchiveDll, GeneratedOutputDir);
                            }
                        }
                        finally
                        {
                            _isProcessingFlatData = false;
                        }
                    });
                }

                ImGui.SameLine();
                if (hasDummyDll && ImGui.Button("Сгенерировать C# код из существующей DLL", new Vector2(320, 40)))
                {
                    _isProcessingFlatData = true;
                    Task.Run(() =>
                    {
                        try
                        {
                            FlatDataGenerator.GenerateFromCecil(Il2CppManager.TargetBlueArchiveDll, GeneratedOutputDir);
                        }
                        finally
                        {
                            _isProcessingFlatData = false;
                        }
                    });
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (res != null && res.TotalGenerated > 0)
            {
                ImGui.TextUnformatted($"Список структур ({res.TotalGenerated}):");
                if (ImGui.BeginChild("FlatDataListChild", new Vector2(0, 180), ImGuiWindowFlags.HorizontalScrollbar))
                {
                    foreach (var tbl in res.GeneratedTables)
                        ImGui.TextColored(ColorGreen, $"Table: {tbl}");

                    foreach (var str in res.GeneratedStructs)
                        ImGui.TextColored(ColorYellow, $"Struct: {str}");

                    foreach (var enm in res.GeneratedEnums)
                        ImGui.TextColored(ColorMuted, $"Enum: {enm}");
                }
                ImGui.EndChild();
            }
        }
    }
}