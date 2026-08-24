using Hexa.NET.ImGui;
using Hexa.NET.ImGui.Backends.OpenGL3;
using Hexa.NET.ImGui.Backends.SDL3;
using Hexa.NET.OpenGL;
using Hexa.NET.SDL3;
using MSBATranslator.Core;
using MSBATranslator.GUI;
using SDLEvent = Hexa.NET.SDL3.SDLEvent;

namespace MSBATranslator.GUI
{
    public partial class RenderImGui
    {
        public static unsafe void initImgui()
        {
            SDL.SetHint(SDL.SDL_HINT_MOUSE_FOCUS_CLICKTHROUGH, "1");
            SDL.Init((uint)(SDLInitFlags.Events | SDLInitFlags.Video));

            float mainScale = SDL.GetDisplayContentScale(SDL.GetPrimaryDisplay());
            var window = SDL.CreateWindow(
                "MSBATranslator",
                (int)(1280 * mainScale),
                (int)(720 * mainScale),
                (ulong)(SDLWindowFlags.Resizable | SDLWindowFlags.Opengl | SDLWindowFlags.HighPixelDensity)
            );

            var windowId = SDL.GetWindowID(window);

            var guiContext = ImGui.CreateContext();
            ImGui.SetCurrentContext(guiContext);

            var io = ImGui.GetIO();
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableKeyboard;
            io.ConfigFlags |= ImGuiConfigFlags.NavEnableGamepad;
            io.ConfigFlags |= ImGuiConfigFlags.DockingEnable;
            io.ConfigViewportsNoAutoMerge = false;
            io.ConfigViewportsNoTaskBarIcon = false;

            SetupImGuiStyle();

            ImFontAtlasPtr fontAtlas = io.Fonts;
            uint* defaultRanges = ImGui.GetGlyphRangesDefault(fontAtlas);
            fixed (byte* pFontData = FontData.UbuntuR)
            {
                ImFontPtr font = fontAtlas.AddFontFromMemoryCompressedTTF(
                    pFontData,
                    FontData.UbuntuR.Length,
                    16.0f,
                    null,
                    defaultRanges
                );
            }
            io.ConfigDpiScaleFonts = true;

            var context = SDL.GLCreateContext(window);

            SDL.GLSetSwapInterval(1);

            ImGuiImplSDL3.SetCurrentContext(guiContext);
            if (!ImGuiImplSDL3.InitForOpenGL(
                new Hexa.NET.ImGui.Backends.SDL3.SDLWindowPtr((Hexa.NET.ImGui.Backends.SDL3.SDLWindow*)window.Handle),
                (void*)context.Handle))
            {
                Logger.Log($"Failed to init ImGui Impl SDL3");
                SDL.Quit();
                return;
            }

            ImGuiImplOpenGL3.SetCurrentContext(guiContext);
            if (!ImGuiImplOpenGL3.Init((byte*)null))
            {
                Logger.Log($"Failed to init ImGui Impl OpenGL3");
                SDL.Quit();
                return;
            }

            GL GL = new(new BindingsContext(window, context));
            GL.MakeCurrent();

            SDLEvent sdlEvent = default;
            bool exiting = false;

            while (!exiting)
            {
                while (SDL.PollEvent(ref sdlEvent))
                {
                    ImGuiImplSDL3.ProcessEvent((Hexa.NET.ImGui.Backends.SDL3.SDLEvent*)&sdlEvent);

                    switch ((SDLEventType)sdlEvent.Type)
                    {
                        case SDLEventType.Quit:
                        case SDLEventType.Terminating:
                            if (FileDialogHelper.IsBusy)
                                Environment.Exit(0);
                            exiting = true;
                            break;

                        case SDLEventType.WindowCloseRequested:
                            if (sdlEvent.Window.WindowID == windowId)
                            {
                                if (FileDialogHelper.IsBusy)
                                    Environment.Exit(0);
                                exiting = true;
                            }
                            break;
                    }
                }

                var windowFlags = SDL.GetWindowFlags(window);
                if ((windowFlags & (ulong)SDLWindowFlags.Minimized) != 0)
                {
                    Thread.Sleep(20);
                    continue;
                }

                int fbWidth, fbHeight;
                SDL.GetWindowSizeInPixels(window, &fbWidth, &fbHeight);
                GL.Viewport(0, 0, fbWidth, fbHeight);

                GL.ClearColor(0.12f, 0.12f, 0.12f, 1.0f);
                GL.Clear(GLClearBufferMask.ColorBufferBit);

                ImGuiImplOpenGL3.NewFrame();
                ImGuiImplSDL3.NewFrame();
                ImGui.NewFrame();

                MainWindow();

                ImGui.Render();

                ImGuiImplOpenGL3.RenderDrawData(ImGui.GetDrawData());
                GL.SwapBuffers();
            }

            ImGuiImplOpenGL3.Shutdown();
            ImGuiImplSDL3.Shutdown();
            ImGui.DestroyContext();
            GL.Dispose();

            SDL.DestroyWindow(window);
            SDL.Quit();
        }
    }
}