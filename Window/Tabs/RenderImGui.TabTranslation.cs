using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Database;
using MSBATranslator.Core.FlatData;
using MSBATranslator.Core.Network;

namespace MSBATranslator.GUI
{
    public class TranslationTableOption
    {
        public string BaseName { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public bool IsSelected { get; set; } = true;
    }

    public class TranslationCategoryGroup
    {
        public string Title { get; set; } = string.Empty;
        public List<TranslationTableOption> Items { get; set; } = new();
    }

    public partial class RenderImGui
    {
        private static string _patchFilePath = AppPaths.DefaultPatchFile;
        private static bool _isInstallingTranslation = false;
        private static bool _showAdvancedNetworkSettings = false;
        private static HashSet<string> _availableTablesInPatch = new(StringComparer.OrdinalIgnoreCase);
        private static string _lastCheckedPatchFile = string.Empty;

        private static readonly List<TranslationCategoryGroup> TranslationGroups = new()
        {
            new()
            {
                Title = "Сам сюжет (Visual Novel)",
                Items = new()
                {
                    new() { BaseName = "ScenarioScript", Description = "диалогах основных и побочных историй" },
                    new() { BaseName = "ScenarioCharacterName", Description = "имена персонажей, названия клубы и т.д. в диалогах" }
                }
            },
            new()
            {
                Title = "Обучение",
                Items = new()
                {
                    new() { BaseName = "TutorialCharacterDialog", Description = "фразы во время обучения" }
                }
            },
            new()
            {
                Title = "Фразы персонажей",
                Items = new()
                {
                    new() { BaseName = "CharacterDialog", Description = "основные фразы персонажей" },
                    new() { BaseName = "CharacterDialogEvent", Description = "фразы в различных ивентах и событиях" },
                    new() { BaseName = "CharacterDialogEmoji", Description = "прочие фразы" },
                    new() { BaseName = "CharacterDialogBattlePass", Description = "фразы персонажей на батлпассе" },
                    new() { BaseName = "CharacterVoiceSubtitle", Description = "прочие фразы" },
                    new() { BaseName = "CharacterDialogSubtitle", Description = "прочие фразы" }
                }
            },
            new()
            {
                Title = "Интерфейс",
                Items = new()
                {
                    new() { BaseName = "Localize", Description = "общий текст в игре" },
                    new() { BaseName = "LocalizeError", Description = "ошибки и предупреждения" },
                    new() { BaseName = "LocalizeSkill", Description = "названия и описания скилов" },
                    new() { BaseName = "LocalizeEtc", Description = "описания предметов, системные надписи, прч" },
                    new() { BaseName = "LocalizeGachaShop", Description = "баннеры" },
                    new() { BaseName = "LocalizeCharProfile", Description = "информация о персонажах (имя, фамилия, рост, возраст, хобби и т.д.)" }
                }
            },
            new()
            {
                Title = "MomoTalk",
                Items = new()
                {
                    new() { BaseName = "AcademyMessanger", Description = "сообщения и ответы в MomoTalk" }
                }
            }
        };

        private static void RefreshPatchTablesIfNeeded()
        {
            if (_lastCheckedPatchFile != _patchFilePath && File.Exists(_patchFilePath))
            {
                _availableTablesInPatch = DatabasePatcher.PeekTablesInPatch(_patchFilePath);
                _lastCheckedPatchFile = _patchFilePath;
            }
            else if (!File.Exists(_patchFilePath))
            {
                _availableTablesInPatch.Clear();
                _lastCheckedPatchFile = string.Empty;
            }
        }

        private static void RenderTabTranslation()
        {
            var cfg = AppConfig.Instance;
            RefreshPatchTablesIfNeeded();

            bool hasBackup = BackupManager.HasBackup();
            bool hasKey = !string.IsNullOrWhiteSpace(cfg.SqlHexKey);
            bool hasPatchFile = File.Exists(_patchFilePath);

            ImGui.Spacing();
            ImGui.TextDisabled("Слияние перевода в игру");
            ImGui.Spacing();

            if (!string.IsNullOrEmpty(TranslationUpdater.StatusMessage))
            {
                var col = TranslationUpdater.IsUpdateAvailable ? ColorYellow : ColorMuted;
                ImGui.TextColored(col, $"* {TranslationUpdater.StatusMessage}");
            }

            if (TranslationUpdater.IsCheckingOrDownloading)
            {
                ImGui.TextColored(ColorYellow, "* Идет передача данных");
            }
            else
            {
                if (ImGui.Button("Проверить обновления", new Vector2(200, 30)))
                {
                    _ = TranslationUpdater.CheckForUpdatesAsync(isStartupCheck: false);
                }

                ImGui.SameLine();
                if (ImGui.Button("Скачать перевод", new Vector2(200, 30)))
                {
                    _ = Task.Run(async () =>
                    {
                        bool ok = await TranslationUpdater.DownloadLatestPatchAsync();
                        if (ok)
                        {
                            _patchFilePath = AppPaths.DefaultPatchFile;
                            _lastCheckedPatchFile = string.Empty;
                            RefreshPatchTablesIfNeeded();
                        }
                    });
                }

                ImGui.SameLine();
                if (ImGui.Button("Доп настройки", new Vector2(200, 30)))
                {
                    _showAdvancedNetworkSettings = !_showAdvancedNetworkSettings;
                }
            }

            if (_showAdvancedNetworkSettings)
            {
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.TextDisabled("Параметры удаленного Git-репозитория (GitHub / GitLab / Gitea / Direct URL):");

                string currentUrl = cfg.RemotePatchUrl;
                if (ImGui.InputText("URL файла (*.gz)", ref currentUrl, 512))
                {
                    cfg.RemotePatchUrl = currentUrl;
                    cfg.Save();
                }

                string currentToken = cfg.RemoteAuthToken;
                if (ImGui.InputText("Access Token (Если необходимо)", ref currentToken, 256, ImGuiInputTextFlags.Password))
                {
                    cfg.RemoteAuthToken = currentToken;
                    cfg.Save();
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Файл перевода:");
            ImGui.SameLine();
            ImGui.TextColored(hasPatchFile ? ColorGreen : ColorRed, _patchFilePath);
            ImGui.SameLine();
            if (ImGui.Button("Выбрать файл..."))
            {
                FileDialogHelper.OpenFileDialogAsync("Patch files", ".gz;.json", file =>
                {
                    if (!string.IsNullOrEmpty(file))
                    {
                        _patchFilePath = file;
                        _lastCheckedPatchFile = string.Empty;
                        RefreshPatchTablesIfNeeded();
                    }
                });
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (_isInstallingTranslation)
            {
                ImGui.TextColored(ColorYellow, "* Идет процесс слияния выбранных локалей перевода с БД игры");
            }
            else
            {
                var selectedTables = TranslationGroups
                    .SelectMany(g => g.Items)
                    .Where(i => i.IsSelected && _availableTablesInPatch.Contains(i.BaseName))
                    .Select(i => i.BaseName)
                    .ToList();

                bool canInstall = hasBackup && hasKey && hasPatchFile && File.Exists(cfg.ExcelDbPath) && selectedTables.Count > 0;

                if (!canInstall)
                {
                    if (!hasBackup) ImGui.TextColored(ColorYellow, "* Создайте бэкап оригинальной БД на вкладке Главная.");
                    else if (!hasKey) ImGui.TextColored(ColorYellow, "* Получите ключ базы данных на вкладке Главная.");
                    else if (!hasPatchFile) ImGui.TextColored(ColorYellow, "* Скачайте или выберите файл перевода.");
                    else if (selectedTables.Count == 0) ImGui.TextColored(ColorYellow, "* Отметьте хотя бы одну таблицу для сливания.");
                }
                else
                {
                    if (ImGui.Button($"Слияние перевода ({selectedTables.Count})", new Vector2(200, 40)))
                    {
                        _isInstallingTranslation = true;
                        Task.Run(() =>
                        {
                            try
                            {
                                RoslynCompiler.EnsureCompiled();

                                if (RoslynCompiler.CompiledAssembly == null)
                                {
                                    Logger.Log("- Ошибка: не удалось скомпилировать структуры FlatData.");
                                    return;
                                }

                                bool patched = DatabasePatcher.ApplyTranslationPatch(
                                    cfg.BackupFilePath,
                                    cfg.ExcelDbPath,
                                    cfg.SqlHexKey,
                                    _patchFilePath,
                                    RoslynCompiler.CompiledAssembly,
                                    selectedTables
                                );

                                if (patched)
                                {
                                    TranslationUpdater.MarkGameDbAsTracked(cfg.ExcelDbPath);
                                }
                            }
                            finally
                            {
                                _isInstallingTranslation = false;
                            }
                        });
                    }
                }
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextDisabled("Выбор таблиц для импорта:");
            ImGui.SameLine();
            if (ImGui.Button("Выбрать все"))
            {
                foreach (var g in TranslationGroups)
                    foreach (var it in g.Items) it.IsSelected = true;
            }
            ImGui.SameLine();
            if (ImGui.Button("Снять все"))
            {
                foreach (var g in TranslationGroups)
                    foreach (var it in g.Items) it.IsSelected = false;
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (ImGui.BeginChild("TranslationSelectionChild", new Vector2(0, 0), ImGuiWindowFlags.HorizontalScrollbar))
            {
                bool isFirstGroup = true;

                foreach (var group in TranslationGroups)
                {
                    if (!isFirstGroup)
                    {
                        ImGui.Spacing();
                        ImGui.Separator();
                    }
                    isFirstGroup = false;

                    ImGui.TextDisabled(group.Title);
                    ImGui.Spacing();

                    foreach (var item in group.Items)
                    {
                        bool inPatch = _availableTablesInPatch.Contains(item.BaseName);
                        bool isChecked = item.IsSelected;

                        if (ImGui.Checkbox($"##cb_{item.BaseName}", ref isChecked))
                        {
                            item.IsSelected = isChecked;
                        }

                        ImGui.SameLine();
                        if (inPatch)
                        {
                            ImGui.Text(item.BaseName);
                        }
                        else
                        {
                            ImGui.TextColored(ColorMuted, $"{item.BaseName} (нет в патче)");
                        }

                        ImGui.SameLine();
                        ImGui.TextColored(ColorMuted, $"- {item.Description}");
                    }
                }
            }
            ImGui.EndChild();
        }
    }
}
