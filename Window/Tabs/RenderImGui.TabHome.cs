using System.Diagnostics;
using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Crypto;
using MSBATranslator.Core.Database;
using MSBATranslator.Core.Network;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        private static readonly Vector4 ColorGreen = new(0.3f, 0.9f, 0.3f, 1.0f);
        private static readonly Vector4 ColorRed = new(0.95f, 0.3f, 0.3f, 1.0f);
        private static readonly Vector4 ColorYellow = new(0.95f, 0.8f, 0.2f, 1.0f);
        private static readonly Vector4 ColorMuted = new(0.7f, 0.7f, 0.7f, 1.0f);

        private static Stopwatch _focusCheckTimer = Stopwatch.StartNew();
        private static string? _detectedDumpPath = null;
        private static bool _isExtractingKey = false;

        private static void RenderTabHome()
        {
            var cfg = AppConfig.Instance;

            if (_focusCheckTimer.ElapsedMilliseconds > 3000)
            {
                _focusCheckTimer.Restart();
                CheckAutoDump();
            }

            ImGui.Spacing();
            ImGui.TextDisabled("Основная БД (ОБЯЗАТЕЛЬНО)");
            ImGui.Spacing();

            bool hasBackup = BackupManager.HasBackup();
            if (!hasBackup)
            {
                ImGui.TextColored(ColorYellow, "* Бэкап не создан. Создание бэкапа перед началом обязательно.");
                ImGui.TextColored(ColorMuted, "  Перевод собирается на основе оригинальной базы (бэкапа).");
                ImGui.Spacing();

                if (ImGui.Button("Создать бэкап", new Vector2(200, 32)))
                {
                    if (File.Exists(cfg.ExcelDbPath))
                    {
                        if (BackupManager.CreateBackup(cfg.ExcelDbPath, out string err))
                        {
                            TranslationUpdater.MarkGameDbAsTracked(cfg.ExcelDbPath);
                            Logger.Log("+ Бэкап успешно создан!");
                        }
                        else
                        {
                            Logger.Log($"- Ошибка: {err}");
                        }
                    }
                    else
                    {
                        Logger.Log("- Не удалось создать бэкап: проверьте путь к ExcelDB.db");
                    }
                }
            }
            else
            {
                ImGui.TextColored(ColorGreen, "+ Бэкап сохранен");
                ImGui.SameLine();
                ImGui.TextColored(ColorMuted, $"| Создан: {cfg.BackupCreatedAt} | CRC32: {cfg.OriginalFileCrc32} | Размер: {cfg.OriginalFileSizeBytes / 1024 / 1024:N1} МБ");

                if (ImGui.Button("Обновить бэкап"))
                {
                    if (File.Exists(cfg.ExcelDbPath))
                    {
                        BackupManager.CreateBackup(cfg.ExcelDbPath, out _);
                        TranslationUpdater.MarkGameDbAsTracked(cfg.ExcelDbPath);
                    }
                }

                ImGui.TextColored(ColorMuted, "  При переустановке/обновлении игры обновляйте бэкап.");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextDisabled("SQLCipher HEX key");
            ImGui.Spacing();

            if (!hasBackup)
            {
                ImGui.TextColored(ColorRed, "* Сначала создайте бэкап оригинальной базы выше.");
                ImGui.TextColored(ColorMuted, "  Ключ извлекается и сразу валидируется на файле бэкапа.");
            }
            else
            {
                bool hasKey = !string.IsNullOrWhiteSpace(cfg.SqlHexKey);
                if (hasKey)
                {
                    ImGui.TextColored(ColorGreen, "+ Ключ базы:");
                    ImGui.SameLine();
                    ImGui.TextColored(ColorYellow, $"0x{cfg.SqlHexKey}");

                    if (ImGui.Button("Проверить на бэкапе"))
                    {
                        if (DbKeyValidator.TestKey(cfg.BackupFilePath, cfg.SqlHexKey, out string msg))
                        {
                            Logger.Log($"+ Успешно: {msg}");
                        }
                        else
                        {
                            Logger.Log($"- Ошибка: {msg}");
                        }
                    }

                    ImGui.SameLine();
                    if (ImGui.Button("Сбросить ключ"))
                    {
                        cfg.SqlHexKey = string.Empty;
                        cfg.Save();
                        Logger.Log("* Ключ базы сброшен.");
                    }

                    ImGui.TextColored(ColorMuted, "  При обновлениях игры по мимо бэкапа нужно так же проверять работоспособность ключа \"[Проверить на бэкапе]\", если ключ не подходит необходимо получить новый");
                }
                else
                {
                    ImGui.TextColored(ColorRed, "- Ключ не задан в конфигурации.");
                    ImGui.Spacing();

                    if (_isExtractingKey)
                    {
                        ImGui.TextColored(ColorYellow, "* Идет сканирование дампа и валидация ключа на бэкапе");
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(_detectedDumpPath))
                        {
                            ImGui.TextColored(ColorGreen, $"+ Обнаружен дамп: {_detectedDumpPath}");
                            ImGui.SameLine();
                            if (ImGui.Button("Извлечь и проверить ключ"))
                            {
                                ExtractAndValidateKeyAsync(_detectedDumpPath, cfg.BackupFilePath);
                            }
                        }
                        else
                        {
                            ImGui.TextColored(ColorMuted, "* Дамп не обнаружен в папке Temp.");
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Проверить папку Temp"))
                        {
                            CheckAutoDump();
                            if (!string.IsNullOrEmpty(_detectedDumpPath))
                                Logger.Log($"+ Дамп найден: {_detectedDumpPath}");
                            else
                                Logger.Log("* BlueArchive.DMP не найден в Temp.");
                        }

                        ImGui.SameLine();
                        if (ImGui.Button("Выбрать .DMP файл вручную..."))
                        {
                            FileDialogHelper.OpenFileDialogAsync("Minidump", ".dmp;.DMP", (file) =>
                            {
                                if (!string.IsNullOrEmpty(file))
                                {
                                    cfg.CustomDumpPath = file;
                                    cfg.Save();
                                    ExtractAndValidateKeyAsync(file, cfg.BackupFilePath);
                                }
                            });
                        }
                    }
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextDisabled("Игровые файлы");
            ImGui.Spacing();

            RenderPathRow("GameAssembly.dll", cfg.GameAssemblyPath, File.Exists(cfg.GameAssemblyPath), () => FileDialogHelper.OpenFileDialogAsync("GameAssembly", ".dll", p => { if (p != null) { cfg.GameAssemblyPath = p; cfg.Save(); } }));
            RenderPathRow("global-metadata.dat", cfg.MetadataPath, File.Exists(cfg.MetadataPath), () => FileDialogHelper.OpenFileDialogAsync("Metadata", ".dat", p => { if (p != null) { cfg.MetadataPath = p; cfg.Save(); } }));
            RenderPathRow("ExcelDB.db", cfg.ExcelDbPath, File.Exists(cfg.ExcelDbPath), () => FileDialogHelper.OpenFileDialogAsync("Database", ".db", p => { if (p != null) { cfg.ExcelDbPath = p; cfg.Save(); } }));

            ImGui.Spacing();
            if (ImGui.Button("Проверить Steam пути заново"))
            {
                cfg.AutoDetectGamePaths();
                cfg.Save();
                Logger.Log("* Поиск путей Steam завершен.");
            }
        }

        private static void RenderPathRow(string title, string currentPath, bool exists, Action onBrowse)
        {
            if (exists) { ImGui.TextColored(ColorGreen, "+"); }
            else { ImGui.TextColored(ColorRed, "-"); }

            ImGui.SameLine();
            ImGui.Text($"{title}:");
            ImGui.SameLine();
            ImGui.TextColored(exists ? ColorMuted : ColorRed, string.IsNullOrEmpty(currentPath) ? "<путь не задан>" : currentPath);

            ImGui.SameLine();
            if (ImGui.Button($"Обзор##{title}"))
            {
                onBrowse();
            }
        }

        private static void CheckAutoDump()
        {
            var cfg = AppConfig.Instance;
            if (!string.IsNullOrEmpty(cfg.CustomDumpPath) && File.Exists(cfg.CustomDumpPath))
            {
                _detectedDumpPath = cfg.CustomDumpPath;
                return;
            }

            var searchList = new List<string>();

            string sysTemp = Path.GetTempPath();
            if (!string.IsNullOrWhiteSpace(sysTemp))
                searchList.Add(Path.Combine(sysTemp, "BlueArchive.DMP"));

            string? envTemp = Environment.GetEnvironmentVariable("TEMP");
            if (!string.IsNullOrWhiteSpace(envTemp))
                searchList.Add(Path.Combine(envTemp, "BlueArchive.DMP"));

            string? envTmp = Environment.GetEnvironmentVariable("TMP");
            if (!string.IsNullOrWhiteSpace(envTmp))
                searchList.Add(Path.Combine(envTmp, "BlueArchive.DMP"));

            searchList.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Temp", "BlueArchive.DMP"));

            foreach (var p in searchList)
            {
                if (File.Exists(p))
                {
                    _detectedDumpPath = p;
                    return;
                }
            }

            _detectedDumpPath = null;
        }

        private static void ExtractAndValidateKeyAsync(string dumpPath, string backupDbPath)
        {
            _isExtractingKey = true;
            Task.Run(() =>
            {
                try
                {
                    Logger.Log($"* Сканирование дампа: {dumpPath}");
                    var keys = FastUniversalKeyExtractor.FindAllSqlKeysInDump(dumpPath);

                    if (keys.Count == 0)
                    {
                        Logger.Log("- Ключи в дампе памяти не найдены.");
                        return;
                    }

                    Logger.Log($"* Найдено кандидатов ключей: {keys.Count}. Проверка на файле бэкапа");

                    string? validKey = null;
                    foreach (var key in keys)
                    {
                        if (DbKeyValidator.TestKey(backupDbPath, key, out _))
                        {
                            validKey = key;
                            Logger.Log($"+ Верный ключ БД: 0x{key}");
                            break;
                        }
                    }

                    if (validKey != null)
                    {
                        AppConfig.Instance.SqlHexKey = validKey;
                        AppConfig.Instance.Save();
                        Logger.Log("+ Ключ успешно сохранен в конфигурации.");
                    }
                    else
                    {
                        Logger.Log("- Ни один из найденных ключей не подошел к бэкапу.");
                    }
                }
                catch (Exception ex)
                {
                    Logger.Log($"- Ошибка при обработке ключа: {ex.Message}");
                }
                finally
                {
                    _isExtractingKey = false;
                }
            });
        }
    }
}