using MSBATranslator.Core.Config;
using MSBATranslator.Core.Network;
using MSBATranslator.GUI;

namespace MSBATranslator
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (TranslationUpdater.CheckIfGameUpdated(out _, out _))
            {
                if (!AppConfig.Instance.SuppressGameUpdateModal)
                {
                    RenderImGui.RequestGameUpdatedModal = true;
                }
            }
            _ = Task.Run(() => TranslationUpdater.CheckForUpdatesAsync(isStartupCheck: true));

            RenderImGui.initImgui();
        }
    }
}