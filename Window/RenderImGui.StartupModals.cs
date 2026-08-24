using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Network;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        public static bool RequestTranslationUpdateModal = false;
        public static bool RequestGameUpdatedModal = false;
        private static bool _activeNoticeGame = false;
        private static bool _activeNoticeTranslation = false;

        private static void RenderStartupModals()
        {
            var cfg = AppConfig.Instance;

            if (RequestGameUpdatedModal && !cfg.SuppressGameUpdateModal)
            {
                _activeNoticeGame = true;
                RequestGameUpdatedModal = false;
            }

            if (RequestTranslationUpdateModal && !cfg.SuppressTranslationUpdateModal)
            {
                _activeNoticeTranslation = true;
                RequestTranslationUpdateModal = false;
            }

            if (_activeNoticeGame || _activeNoticeTranslation)
            {
                ImGui.OpenPopup("Уведомление##RenderStartupModals");
            }

            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

            ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0.0f, 0.0f, 0.0f, 0.72f));

            bool isModalOpen = true;
            if (ImGui.BeginPopupModal("Уведомление##RenderStartupModals", ref isModalOpen, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar))
            {
                if (_activeNoticeGame)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(ColorYellow, "Внимание: возможно обновление игры!");
                    ImGui.Spacing();

                    string trackedDate = TranslationUpdater.GameTrackedDate;
                    string currentDate = TranslationUpdater.GameCurrentDate;

                    ImGui.TextWrapped($"Относительно последней запаковки/бэкапа БД ({trackedDate}) и времени изменения БД в файлах игры ({currentDate}) возможно, что игра обновилась.");
                    ImGui.Spacing();
                    ImGui.TextColored(ColorRed, "Сделайте бэкап и проверьте работоспособность SQL ключа.");
                    ImGui.Spacing();

                    bool suppressGame = cfg.SuppressGameUpdateModal;
                    if (ImGui.Checkbox("Не напоминать об изменениях файлов игры", ref suppressGame))
                    {
                        cfg.SuppressGameUpdateModal = suppressGame;
                        cfg.Save();
                    }

                    if (_activeNoticeTranslation)
                    {
                        ImGui.Spacing();
                        ImGui.Separator();
                    }
                }

                if (_activeNoticeTranslation)
                {
                    ImGui.Spacing();
                    ImGui.TextColored(ColorGreen, "Доступно обновление перевода на GitHub!");
                    ImGui.Spacing();

                    string localDate = !string.IsNullOrEmpty(cfg.LocalPatchDownloadedAt) ? cfg.LocalPatchDownloadedAt : "<еще не скачивался>";
                    string remoteDate = !string.IsNullOrEmpty(TranslationUpdater.LatestRemoteDate) ? TranslationUpdater.LatestRemoteDate : cfg.RemotePatchLastModified;

                    ImGui.TextWrapped($"Последний раз вы скачивали перевод: {localDate}");
                    ImGui.TextWrapped($"В репозитории он от: {remoteDate}");
                    ImGui.Spacing();

                    bool suppressTrans = cfg.SuppressTranslationUpdateModal;
                    if (ImGui.Checkbox("Не напоминать об обновлениях перевода", ref suppressTrans))
                    {
                        cfg.SuppressTranslationUpdateModal = suppressTrans;
                        cfg.Save();
                    }
                }

                ImGui.Spacing();
                ImGui.Separator();
                ImGui.Spacing();

                float buttonWidth = 160f;
                float windowWidth = ImGui.GetWindowSize().X;
                ImGui.SetCursorPosX((windowWidth - buttonWidth) * 0.5f);

                if (ImGui.Button("Продолжить", new Vector2(buttonWidth, 32)))
                {
                    if (_activeNoticeGame)
                    {
                        TranslationUpdater.MarkGameDbAsTracked(cfg.ExcelDbPath);
                    }

                    _activeNoticeGame = false;
                    _activeNoticeTranslation = false;
                    ImGui.CloseCurrentPopup();
                }

                ImGui.EndPopup();
            }
            ImGui.PopStyleColor();
        }
    }
}