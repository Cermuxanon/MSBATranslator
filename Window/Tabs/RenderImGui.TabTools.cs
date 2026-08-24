using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Database;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        private static string _exportOutputDir = AppPaths.JsonExportDir;
        private static string _repackInputFolder = AppPaths.JsonExportDir;
        private static string _tableFilterSearch = string.Empty;
        private static List<ExportTableItem> _loadedDbTables = new();
        private static void RenderSubTabExport()
        {
            var cfg = AppConfig.Instance;
            bool isReady = File.Exists(cfg.BackupFilePath) && !string.IsNullOrWhiteSpace(cfg.SqlHexKey);

            ImGui.Spacing();
            ImGui.TextDisabled("Простой экспортёр таблиц из БД в Json");
            ImGui.Spacing();

            ImGui.Text("Папка для сохранения JSON:");
            ImGui.SameLine();
            ImGui.TextColored(ColorGreen, _exportOutputDir);
            ImGui.SameLine();
            if (ImGui.Button("Обзор##ExportDir"))
            {
                FileDialogHelper.SelectFolderDialogAsync(folder => { if (!string.IsNullOrEmpty(folder)) _exportOutputDir = folder; });
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (!isReady)
            {
                ImGui.TextColored(ColorYellow, "* Для экспорта создайте бэкап и получите ключ на вкладке \"Главная\"");
                return;
            }

            if (ImGui.Button("Загрузить список таблиц из БД", new Vector2(300, 40)))
            {
                _loadedDbTables = TableExporter.GetDatabaseTables();
                Logger.Log($"+ Загружено таблиц: {_loadedDbTables.Count}");
            }
            ImGui.SameLine();
            if (ImGui.Button("Экспортировать выбранные таблицы", new Vector2(300, 40)))
            {
                var targets = _loadedDbTables.Where(t => t.IsSelected).Select(t => t.Name).ToList();
                if (targets.Count == 0)
                {
                    Logger.Log("- Выберите хотя бы одну таблицу для экспорта.");
                    return;
                }

                _ = TableExporter.ExportTablesAsync(_exportOutputDir, targets);
            }
            if (TableExporter.IsExporting)
            {
                ImGui.ProgressBar(TableExporter.ExportProgress, new Vector2(608, 20), $"Экспорт: {(int)(TableExporter.ExportProgress * 100)}%");
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (_loadedDbTables.Count > 0)
            {
                ImGui.InputText("##0", ref _tableFilterSearch, 64);
                ImGui.SameLine();
                if (ImGui.Button("Выбрать все")) _loadedDbTables.ForEach(t => t.IsSelected = true);
                ImGui.SameLine();
                if (ImGui.Button("Снять все")) _loadedDbTables.ForEach(t => t.IsSelected = false);

                int selectedCount = _loadedDbTables.Count(t => t.IsSelected);
                ImGui.TextColored(ColorMuted, $"Выбрано: {selectedCount} из {_loadedDbTables.Count}");

                if (ImGui.BeginChild("ExportTablesList", new Vector2(0, 0), ImGuiWindowFlags.HorizontalScrollbar))
                {
                    foreach (var tbl in _loadedDbTables)
                    {
                        if (!string.IsNullOrEmpty(_tableFilterSearch) && !tbl.Name.Contains(_tableFilterSearch, StringComparison.OrdinalIgnoreCase))
                            continue;

                        bool sel = tbl.IsSelected;
                        if (ImGui.Checkbox(tbl.Name, ref sel))
                        {
                            tbl.IsSelected = sel;
                        }
                    }
                }
                ImGui.EndChild();
            }
        }

        private static void RenderSubTabRepack()
        {
            ImGui.Spacing();
            ImGui.TextDisabled("Запаковка JSON файлов в ДБ");
            ImGui.Spacing();

            ImGui.Text("Папка с JSON файлами:");
            ImGui.SameLine();
            ImGui.TextColored(Directory.Exists(_repackInputFolder) ? ColorGreen : ColorRed, _repackInputFolder);
            ImGui.SameLine();
            if (ImGui.Button("Обзор##RepackJsonDir"))
            {
                FileDialogHelper.SelectFolderDialogAsync(folder => { if (!string.IsNullOrEmpty(folder)) _repackInputFolder = folder; });
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            if (ImGui.Button("Запаковать таблицы в игру", new Vector2(260, 40)))
            {
                _ = TableRepacker.RepackDirectoryAsync(_repackInputFolder);
            }
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }
    }
}