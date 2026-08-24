using NativeFileDialogNET;
using MSBATranslator.Core;

namespace MSBATranslator.GUI
{
    public static class FileDialogHelper
    {
        public static bool IsBusy { get; private set; } = false;

        public static void OpenFileDialogAsync(string filterName, string extensionSpec, Action<string?> onResult)
        {
            if (IsBusy) return;
            IsBusy = true;

            var thread = new Thread(() =>
            {
                try
                {
                    using var dlg = new NativeFileDialog().SelectFile();
                    string cleanSpec = extensionSpec.Replace("*.", "").Replace(".", "");
                    if (!string.IsNullOrWhiteSpace(cleanSpec))
                    {
                        dlg.AddFilter(filterName, cleanSpec);
                    }

                    DialogResult result = dlg.Open(out string? file, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                    string? chosenFile = (result == DialogResult.Okay) ? file : null;
                    
                    onResult?.Invoke(chosenFile);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Open dialog error: {ex.Message}");
                    onResult?.Invoke(null);
                }
                finally
                {
                    IsBusy = false;
                }
            });

            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
        }

        public static void SaveFileDialogAsync(string filterName, string extensionSpec, string defaultName, Action<string?> onResult)
        {
            if (IsBusy) return;
            IsBusy = true;

            var thread = new Thread(() =>
            {
                try
                {
                    using var dlg = new NativeFileDialog().SaveFile();
                    string cleanSpec = extensionSpec.Replace("*.", "").Replace(".", "");
                    if (!string.IsNullOrWhiteSpace(cleanSpec))
                    {
                        dlg.AddFilter(filterName, cleanSpec);
                    }

                    DialogResult result = dlg.Open(out string? file, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), defaultName);
                    string? chosenFile = (result == DialogResult.Okay) ? file : null;

                    onResult?.Invoke(chosenFile);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Save dialog error: {ex.Message}");
                    onResult?.Invoke(null);
                }
                finally
                {
                    IsBusy = false;
                }
            });

            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
        }

        public static void SelectFolderDialogAsync(Action<string?> onResult)
        {
            if (IsBusy) return;
            IsBusy = true;

            var thread = new Thread(() =>
            {
                try
                {
                    using var dlg = new NativeFileDialog().SelectFolder();
                    DialogResult result = dlg.Open(out string[]? folders, Environment.GetFolderPath(Environment.SpecialFolder.Desktop));
                    string? chosenFolder = (result == DialogResult.Okay && folders != null && folders.Length > 0) ? folders[0] : null;

                    onResult?.Invoke(chosenFolder);
                }
                catch (Exception ex)
                {
                    Logger.Log($"Folder selection dialog error: {ex.Message}");
                    onResult?.Invoke(null);
                }
                finally
                {
                    IsBusy = false;
                }
            });

            if (OperatingSystem.IsWindows())
            {
                thread.SetApartmentState(ApartmentState.STA);
            }
            thread.Start();
        }
    }
}