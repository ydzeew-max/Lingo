using System;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Lingo.Services
{
    public static class ShellContextMenuService
    {
        private const string ShellKeyPath = @"Software\Classes\SystemFileAssociations\image\shell\Lingo";

        public static bool IsContextMenuRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(ShellKeyPath);
                return key != null;
            }
            catch
            {
                return false;
            }
        }

        public static void SetContextMenuEnabled(bool enable)
        {
            try
            {
                if (enable)
                {
                    string exePath = Process.GetCurrentProcess().MainModule?.FileName 
                        ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lingo.exe");

                    if (!File.Exists(exePath))
                    {
                        string localExe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Lingo.exe");
                        if (File.Exists(localExe)) exePath = localExe;
                    }

                    using var shellKey = Registry.CurrentUser.CreateSubKey(ShellKeyPath);
                    if (shellKey != null)
                    {
                        shellKey.SetValue("", "Перевести с Lingo");
                        shellKey.SetValue("Icon", $"\"{exePath}\",0");

                        using var cmdKey = shellKey.CreateSubKey("command");
                        cmdKey?.SetValue("", $"\"{exePath}\" --image \"%1\"");
                    }
                }
                else
                {
                    Registry.CurrentUser.DeleteSubKeyTree(ShellKeyPath, false);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error updating shell context menu: {ex.Message}");
            }
        }
    }
}
