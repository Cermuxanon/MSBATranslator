using System.IO;
using System.Numerics;
using System.Threading.Tasks;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        private static string _inputFullJsonFolder = string.Empty;
        private static string _outputRepoFolder = AppPaths.RepositoryDir;
        private static bool _isGeneratingRepo = false;

        private static void RenderTabRepoGenerator()
        {
            ImGui.Spacing();
            ImGui.TextColored(ColorMuted, "Генерация дельты и файлов перевода");
            ImGui.Spacing();
            ImGui.TextColored(ColorMuted, "Создает компактные файлы для передачи: RowId, Mapped*, и сжатый patch_data.json.gz.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.Text("Папка с полными \"готовыми\" .json файлами перевода:");
            ImGui.SameLine();
            ImGui.TextColored(string.IsNullOrEmpty(_inputFullJsonFolder) ? ColorYellow : ColorGreen,
                string.IsNullOrEmpty(_inputFullJsonFolder) ? "<выберите папку с .json>" : _inputFullJsonFolder);

            ImGui.SameLine();
            if (ImGui.Button("Обзор##InputJson"))
            {
                FileDialogHelper.SelectFolderDialogAsync(folder =>
                {
                    if (!string.IsNullOrEmpty(folder)) _inputFullJsonFolder = folder;
                });
            }

            ImGui.Spacing();

            ImGui.Text("Папка сохранения:");
            ImGui.SameLine();
            ImGui.TextColored(ColorMuted, _outputRepoFolder);

            ImGui.SameLine();
            if (ImGui.Button("Обзор##OutputRepo"))
            {
                FileDialogHelper.SelectFolderDialogAsync(folder =>
                {
                    if (!string.IsNullOrEmpty(folder)) _outputRepoFolder = folder;
                });
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            if (_isGeneratingRepo)
            {
                ImGui.TextColored(ColorYellow, "* Идет генерация файлов дельты и сжатие.");
            }
            else
            {
                if (ImGui.Button("Сгенерировать файлы перевода", new Vector2(360, 36)))
                {
                    if (string.IsNullOrEmpty(_inputFullJsonFolder) || !Directory.Exists(_inputFullJsonFolder))
                    {
                        Logger.Log("- Укажите корректную папку с .json файлами");
                        return;
                    }

                    _isGeneratingRepo = true;
                    Task.Run(() =>
                    {
                        try
                        {
                            RepoGenerator.GenerateRepositoryFiles(_inputFullJsonFolder, _outputRepoFolder);
                        }
                        finally
                        {
                            _isGeneratingRepo = false;
                        }
                    });
                }
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();

            ImGui.TextColored(ColorMuted, "Доп информация");
            ImGui.TextColored(ColorMuted, "RowId/                              Файлы формата { \"RowId\": \"Text\"}");
            ImGui.TextColored(ColorMuted, "Mapped/                              Тот же RowId сопоставленный по инвариантным ключам (Key, TLMID, GroupId и т.д.).");
            ImGui.TextColored(ColorMuted, "patch_data.json.gz               Готовый сжатый патч (~10 МБ) удобный для передачи.");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(ColorMuted, "Патч файл этот только для запаковки и передачи:  ");
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextColored(ColorMuted, "AcademyMessangerExcelTable");
            ImGui.TextColored(ColorMuted, "LocalizeExcelTable");
            ImGui.TextColored(ColorMuted, "LocalizeErrorExcelTable");
            ImGui.TextColored(ColorMuted, "LocalizeSkillExcelTable");
            ImGui.TextColored(ColorMuted, "LocalizeEtcExcelTable");
            ImGui.TextColored(ColorMuted, "LocalizeGachaShopExcelTable");
            ImGui.TextColored(ColorMuted, "LocalizeCharProfileExcelTable");
            ImGui.TextColored(ColorMuted, "CharacterVoiceSubtitleExcelTable");
            ImGui.TextColored(ColorMuted, "CharacterDialogSubtitleExcelTable");
            ImGui.TextColored(ColorMuted, "CharacterDialogBattlePassExcelTable");
            ImGui.TextColored(ColorMuted, "CharacterDialogExcelTable");
            ImGui.TextColored(ColorMuted, "CharacterDialogEventExcelTable");
            ImGui.TextColored(ColorMuted, "CharacterDialogEmojiExcelTable");
            ImGui.TextColored(ColorMuted, "ScenarioScriptExcelTable");
            ImGui.TextColored(ColorMuted, "ScenarioCharacterNameExcelTable");
            ImGui.TextColored(ColorMuted, "TutorialCharacterDialogExcelTable");

        }
    }
}