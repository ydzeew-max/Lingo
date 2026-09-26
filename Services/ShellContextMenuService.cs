using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Lingo.Services
{
    public static class ShellContextMenuService
    {
        private const string MenuTitle = "Перевести с Lingo";
        private static readonly string[] TargetAssociations = new[]
        {
            "image",
            ".png",
            ".jpg",
            ".jpeg",
            ".bmp",
            ".webp",
            ".gif",
            ".tiff"
        };

        public static bool IsContextMenuRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\image\shell\Lingo");
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static void EnsureRegisteredOnStartup(bool isEnabled)
        {
            try
            {
                if (!isEnabled) return;

                string currentExe = GetCurrentExePath();
                if (!File.Exists(currentExe)) return;

                // Check if already registered and points to the same exe
                using var cmdKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\image\shell\Lingo\command");
                string? registeredCmd = cmdKey?.GetValue("") as string;

                string expectedCmd = $"\"{currentExe}\" --image \"%1\"";
                if (registeredCmd != expectedCmd)
                {
                    // Register or update paths
                    SetContextMenuEnabled(true);
                }
            }
            catch { }
        }

        public static void SetContextMenuEnabled(bool enable)
        {
            try
            {
                if (enable)
                {
                    string exePath = GetCurrentExePath();
                    if (!File.Exists(exePath)) return;

                    foreach (var assoc in TargetAssociations)
                    {
                        string shellPath = $@"Software\Classes\SystemFileAssociations\{assoc}\shell\Lingo";
                        using var shellKey = Registry.CurrentUser.CreateSubKey(shellPath);
                        if (shellKey != null)
                        {
                            shellKey.SetValue("", MenuTitle);
                            shellKey.SetValue("Icon", $"\"{exePath}\",0");

                            using var cmdKey = shellKey.CreateSubKey("command");
                            cmdKey?.SetValue("", $"\"{exePath}\" --image \"%1\"");
                        }
                    }
                }
                else
                {
                    foreach (var assoc in TargetAssociations)
                    {
                        string shellPath = $@"Software\Classes\SystemFileAssociations\{assoc}\shell\Lingo";
                        Registry.CurrentUser.DeleteSubKeyTree(shellPath, false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating shell context menu: {ex.Message}");
            }
        }

        private static string GetCurrentExePath()
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
                return procPath;

            try
            {
                string? mainMod = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mainMod) && File.Exists(mainMod))
                    return mainMod;
            }
            catch { }

            string localExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lingo.exe");
            if (File.Exists(localExe))
                return localExe;

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish", "Lingo.exe");
        }
    }
}
