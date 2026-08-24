using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Database;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        private static void RenderTabBackups()
        {
            var cfg = AppConfig.Instance;
            bool hasBackup = BackupManager.HasBackup();

            ImGui.Spacing();
            ImGui.TextDisabled("Бэкап");
            ImGui.Spacing();

            if (!hasBackup)
            {
                ImGui.TextColored(ColorYellow, "* Бэкап оригинального файла ExcelDB.db отсутствует.");
                ImGui.Spacing();
                if (ImGui.Button("Создать бэкап из текущих файлов игры", new Vector2(250, 32)))
                {
                    if (File.Exists(cfg.ExcelDbPath))
                    {
                        BackupManager.CreateBackup(cfg.ExcelDbPath, out _);
                    }
                    else
                    {
                        Logger.Log("- Укажите корректный путь к ExcelDB.db на вкладке \"Главная\"");
                    }
                }
            }
            else
            {
                ImGui.TextColored(ColorGreen, "+ Бэкап найдена в папке Data/Backups/ExcelDB_original.db");
                ImGui.Spacing();

                ImGui.BulletText($"Дата создания бэкапа: {cfg.BackupCreatedAt}");
                ImGui.BulletText($"Дата оригинального файла: {cfg.OriginalFileCreatedAt}");
                ImGui.BulletText($"CRC32 бэкапа: {cfg.OriginalFileCrc32}");
                ImGui.BulletText($"Размер файла: {cfg.OriginalFileSizeBytes:N0} байт ({cfg.OriginalFileSizeBytes / 1024 / 1024:N1} МБ)");

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                if (ImGui.Button("Восстановить оригинальную БД в игру", new Vector2(280, 36)))
                {
                    if (!string.IsNullOrEmpty(cfg.ExcelDbPath))
                    {
                        if (BackupManager.RestoreBackup(cfg.ExcelDbPath, out string err))
                        {
                            Logger.Log("+ Успешно восстановлено!");
                        }
                        else
                        {
                            Logger.Log($"- Ошибка восстановления: {err}");
                        }
                    }
                }

                ImGui.SameLine();
                if (ImGui.Button("Перезаписать бэкап текущей базой из файлов игры", new Vector2(360, 36)))
                {
                    if (File.Exists(cfg.ExcelDbPath))
                    {
                        BackupManager.CreateBackup(cfg.ExcelDbPath, out _);
                    }
                }
                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();
            }
        }
    }
}