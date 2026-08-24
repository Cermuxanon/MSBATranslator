using System.Numerics;
using Hexa.NET.ImGui;
using MSBATranslator.Core;
using MSBATranslator.Core.Config;
using MSBATranslator.Core.Database;
using MSBATranslator.Core.FlatData;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        private static void MainWindow()
        {
            var viewport = ImGui.GetMainViewport();
            ImGui.SetNextWindowPos(viewport.Pos);
            ImGui.SetNextWindowSize(viewport.Size);
            ImGui.SetNextWindowBgAlpha(1.0f);

            ImGuiWindowFlags windowFlags = ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus | ImGuiWindowFlags.NoDocking;

            ImGui.Begin("MSBATranslator", windowFlags);

            float availHeight = ImGui.GetContentRegionAvail().Y;
            float logHeight = MathF.Max(120.0f, availHeight * 0.33f);
            float tabsHeight = MathF.Max(100.0f, availHeight - logHeight - 16.0f);

            if (ImGui.BeginChild("GUI.BeginChild", new Vector2(0, tabsHeight), ImGuiWindowFlags.None))
            {
                if (ImGui.BeginTabBar("GUI.BeginTabBar"))
                {
                    if (ImGui.BeginTabItem("Главная"))
                    {
                        RenderTabHome();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Бэкапы"))
                    {
                        RenderTabBackups();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("FlatData"))
                    {
                        RenderTabFlatData();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Перевод"))
                    {
                        RenderTabTranslation();
                        ImGui.EndTabItem();
                    }

                    if (ImGui.BeginTabItem("Прочее"))
                    {
                        if (ImGui.BeginTabBar("Other.BeginTabBar"))
                        {
                            if (ImGui.BeginTabItem("Экспорт таблиц [DB2Json]"))
                            {
                                RenderSubTabExport();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Прямая запаковка [Json2DB]"))
                            {
                                RenderSubTabRepack();
                                ImGui.EndTabItem();
                            }

                            if (ImGui.BeginTabItem("Генерация [repo]"))
                            {
                                RenderTabRepoGenerator();
                                ImGui.EndTabItem();
                            }

                            ImGui.EndTabBar();
                        }
                        ImGui.EndTabItem();
                    }

                    ImGui.EndTabBar();
                }
            }
            ImGui.EndChild();

            ImGui.Spacing();
            ImGui.SeparatorText("Logs:");
            ImGui.Spacing();

            if (ImGui.BeginChild("GUI.LogBeginChild", new Vector2(0, 0), ImGuiWindowFlags.None))
            {
                RenderLogConsolePanel();
            }

            ImGui.EndChild();
            ImGui.End();

            RenderStartupModals();
        }

        private static void RenderLogConsolePanel()
        {
            if (ImGui.BeginChild("Logs.BeginChild", new Vector2(0, 0), ImGuiWindowFlags.HorizontalScrollbar))
            {
                string[] logs = Logger.GetLogsSnapshot();
                for (int i = 0; i < logs.Length; i++)
                {
                    ImGui.TextUnformatted(logs[i]);
                }

                if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 5.0f)
                {
                    ImGui.SetScrollHereY(1.0f);
                }
            }
            ImGui.EndChild();
        }
    }
}