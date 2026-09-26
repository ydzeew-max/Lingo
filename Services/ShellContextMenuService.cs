using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Lingo.Services
{
    public static class ShellContextMenuService
    {
        private const string MenuTitle = "Translate with Lingo";
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

                using var cmdKey = Registry.CurrentUser.OpenSubKey(@"Software\Classes\SystemFileAssociations\image\shell\Lingo\command");
                string? registeredCmd = cmdKey?.GetValue("") as string;

                string expectedCmd = $"\"{currentExe}\" --image \"%1\"";
                if (registeredCmd != expectedCmd)
                {
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
                            shellKey.SetValue("", MenuTitle, RegistryValueKind.String);
                            shellKey.SetValue("Icon", $"\"{exePath}\",0", RegistryValueKind.String);

                            using var cmdKey = shellKey.CreateSubKey("command");
                            cmdKey?.SetValue("", $"\"{exePath}\" --image \"%1\"", RegistryValueKind.String);
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

        public static string GetCurrentExePath()
        {
            string? procPath = Environment.ProcessPath;
            if (!string.IsNullOrEmpty(procPath) && File.Exists(procPath))
                return Path.GetFullPath(procPath);

            try
            {
                string? mainMod = Process.GetCurrentProcess().MainModule?.FileName;
                if (!string.IsNullOrEmpty(mainMod) && File.Exists(mainMod))
                    return Path.GetFullPath(mainMod);
            }
            catch { }

            string localExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lingo.exe");
            if (File.Exists(localExe))
                return Path.GetFullPath(localExe);

            return Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "publish", "Lingo.exe"));
        }
    }
}
