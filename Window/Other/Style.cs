using System.Numerics;
using Hexa.NET.ImGui;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        public static void SetupImGuiStyle()
        {
            var style = ImGui.GetStyle();

            style.Alpha = 1.0f;
            style.DisabledAlpha = 0.6f;
            style.WindowPadding = new Vector2(8.0f, 8.0f);
            style.WindowRounding = 0f;
            style.WindowBorderSize = 1.0f;
            style.WindowMinSize = new Vector2(32.0f, 32.0f);
            style.WindowTitleAlign = new Vector2(0.0f, 0.5f);
            style.WindowMenuButtonPosition = ImGuiDir.Right;
            style.ChildRounding = 3.0f;
            style.ChildBorderSize = 1.0f;
            style.PopupRounding = 3.0f;
            style.PopupBorderSize = 1.0f;
            style.FramePadding = new Vector2(4.0f, 3.0f);
            style.FrameRounding = 3.0f;
            style.FrameBorderSize = 1.0f;
            style.ItemSpacing = new Vector2(8.0f, 4.0f);
            style.ItemInnerSpacing = new Vector2(4.0f, 4.0f);
            style.CellPadding = new Vector2(4.0f, 2.0f);
            style.IndentSpacing = 21.0f;
            style.ColumnsMinSpacing = 6.0f;
            style.ScrollbarSize = 11f;
            style.ScrollbarRounding = 18.0f;
            style.GrabMinSize = 10.0f;
            style.GrabRounding = 3.0f;
            style.TabRounding = 3.0f;
            style.TabBorderSize = 0.0f;
            style.TabCloseButtonMinWidthSelected = -1f;
            style.TabCloseButtonMinWidthUnselected = 0.0f;
            style.ColorButtonPosition = ImGuiDir.Right;
            style.ButtonTextAlign = new Vector2(0.5f, 0.5f);
            style.SelectableTextAlign = new Vector2(0.0f, 0.0f);

            style.Colors[(int)ImGuiCol.Text] = new Vector4(1.0f, 1.0f, 1.0f, 1.0f);
            style.Colors[(int)ImGuiCol.TextDisabled] = new Vector4(0.6f, 0.6f, 0.6f, 1.0f);
            style.Colors[(int)ImGuiCol.WindowBg] = new Vector4(0.1254902f, 0.1254902f, 0.1254902f, 1.0f);
            style.Colors[(int)ImGuiCol.ChildBg] = new Vector4(0.1254902f, 0.1254902f, 0.1254902f, 1.0f);
            style.Colors[(int)ImGuiCol.PopupBg] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.Border] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.BorderShadow] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            style.Colors[(int)ImGuiCol.FrameBg] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.FrameBgHovered] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.FrameBgActive] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.TitleBg] = new Vector4(0.1254902f, 0.1254902f, 0.1254902f, 1.0f);
            style.Colors[(int)ImGuiCol.TitleBgActive] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.TitleBgCollapsed] = new Vector4(0.1254902f, 0.1254902f, 0.1254902f, 1.0f);
            style.Colors[(int)ImGuiCol.MenuBarBg] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.ScrollbarBg] = new Vector4(0.1254902f, 0.1254902f, 0.1254902f, 1.0f);
            style.Colors[(int)ImGuiCol.ScrollbarGrab] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabHovered] = new Vector4(0.3019608f, 0.3019608f, 0.3019608f, 1.0f);
            style.Colors[(int)ImGuiCol.ScrollbarGrabActive] = new Vector4(0.34901962f, 0.34901962f, 0.34901962f, 1.0f);
            style.Colors[(int)ImGuiCol.CheckMark] = new Vector4(0.0f, 0.47058824f, 0.84313726f, 1.0f);
            style.Colors[(int)ImGuiCol.SliderGrab] = new Vector4(0.0f, 0.47058824f, 0.84313726f, 1.0f);
            style.Colors[(int)ImGuiCol.SliderGrabActive] = new Vector4(0.0f, 0.32941177f, 0.6f, 1.0f);
            style.Colors[(int)ImGuiCol.Button] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.ButtonHovered] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.ButtonActive] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.Header] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.HeaderHovered] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.HeaderActive] = new Vector4(0.3019608f, 0.3019608f, 0.3019608f, 1.0f);
            style.Colors[(int)ImGuiCol.Separator] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.SeparatorHovered] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.SeparatorActive] = new Vector4(0.3019608f, 0.3019608f, 0.3019608f, 1.0f);
            style.Colors[(int)ImGuiCol.ResizeGrip] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.ResizeGripHovered] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.ResizeGripActive] = new Vector4(0.3019608f, 0.3019608f, 0.3019608f, 1.0f);
            style.Colors[(int)ImGuiCol.Tab] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.TabHovered] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.TabSelected] = new Vector4(0.2509804f, 0.2509804f, 0.2509804f, 1.0f);
            style.Colors[(int)ImGuiCol.TabSelectedOverline] = new Vector4(0.16862746f, 0.16862746f, 0.16862746f, 1.0f);
            style.Colors[(int)ImGuiCol.TabDimmedSelected] = new Vector4(0.21568628f, 0.21568628f, 0.21568628f, 1.0f);
            style.Colors[(int)ImGuiCol.PlotLines] = new Vector4(0.0f, 0.47058824f, 0.84313726f, 1.0f);
            style.Colors[(int)ImGuiCol.PlotLinesHovered] = new Vector4(0.0f, 0.32941177f, 0.6f, 1.0f);
            style.Colors[(int)ImGuiCol.PlotHistogram] = new Vector4(0.0f, 0.47058824f, 0.84313726f, 1.0f);
            style.Colors[(int)ImGuiCol.PlotHistogramHovered] = new Vector4(0.0f, 0.32941177f, 0.6f, 1.0f);
            style.Colors[(int)ImGuiCol.TableHeaderBg] = new Vector4(0.1882353f, 0.1882353f, 0.2f, 1.0f);
            style.Colors[(int)ImGuiCol.TableBorderStrong] = new Vector4(0.30980393f, 0.30980393f, 0.34901962f, 1.0f);
            style.Colors[(int)ImGuiCol.TableBorderLight] = new Vector4(0.22745098f, 0.22745098f, 0.24705882f, 1.0f);
            style.Colors[(int)ImGuiCol.TableRowBg] = new Vector4(0.0f, 0.0f, 0.0f, 0.0f);
            style.Colors[(int)ImGuiCol.TableRowBgAlt] = new Vector4(1.0f, 1.0f, 1.0f, 0.06f);
            style.Colors[(int)ImGuiCol.TextSelectedBg] = new Vector4(0.0f, 0.47058824f, 0.84313726f, 1.0f);
            style.Colors[(int)ImGuiCol.DragDropTarget] = new Vector4(1.0f, 1.0f, 0.0f, 0.9f);
            style.Colors[(int)ImGuiCol.NavCursor] = new Vector4(0.25882354f, 0.5882353f, 0.9764706f, 1.0f);
            style.Colors[(int)ImGuiCol.NavWindowingHighlight] = new Vector4(1.0f, 1.0f, 1.0f, 0.7f);
            style.Colors[(int)ImGuiCol.NavWindowingDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.2f);
            style.Colors[(int)ImGuiCol.ModalWindowDimBg] = new Vector4(0.8f, 0.8f, 0.8f, 0.35f);
        }
    }
}