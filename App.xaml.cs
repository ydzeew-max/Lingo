using System;
using System.IO;
using System.Threading;
using System.Windows;
using Lingo.Services;

namespace Lingo
{
    public partial class App : System.Windows.Application
    {
        private const string MutexName = "Lingo_SingleInstance_App_Mutex_b55e";
        private const string EventName = "Lingo_SingleInstance_Show_Event_b55e";

        private static Mutex? _mutex;
        private static EventWaitHandle? _showEvent;
        private static bool _isExiting;

        public static SettingsService Settings { get; private set; } = null!;
        public static TranslationEngine Translator { get; private set; } = null!;

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                try
                {
                    File.AppendAllText("error.log", $"[{DateTime.Now}] Dispatcher exception: {args.Exception}\n");
                }
                catch { }
                args.Handled = true;
            };

            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                try
                {
                    File.AppendAllText("error.log", $"[{DateTime.Now}] Domain exception: {args.ExceptionObject}\n");
                }
                catch { }
            };

            string? imageArg = ExtractImageArgument(e.Args);

            // Single Instance Enforcement
            _mutex = new Mutex(true, MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                try
                {
                    if (!string.IsNullOrEmpty(imageArg))
                    {
                        string queueFile = GetPendingImageQueuePath();
                        Directory.CreateDirectory(Path.GetDirectoryName(queueFile)!);
                        File.WriteAllText(queueFile, imageArg);
                    }

                    using var existingEvent = EventWaitHandle.OpenExisting(EventName);
                    existingEvent.Set();
                }
                catch { }

                // Exit second instance immediately
                Environment.Exit(0);
                return;
            }

            // Create global event for receiving wake-up signals from secondary instances
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);

            // Listen for wake-up requests on a background thread
            var listenerThread = new Thread(() =>
            {
                while (_showEvent.WaitOne())
                {
                    if (_isExiting) break;

                    Current.Dispatcher.Invoke(() =>
                    {
                        string queueFile = GetPendingImageQueuePath();
                        if (File.Exists(queueFile))
                        {
                            try
                            {
                                string path = File.ReadAllText(queueFile).Trim();
                                File.Delete(queueFile);
                                if (File.Exists(path))
                                {
                                    OpenImageFileForTranslation(path);
                                    return;
                                }
                            }
                            catch { }
                        }

                        if (Current.MainWindow is MainWindow win)
                        {
                            win.ShowAndActivate();
                        }
                    });
                }
            })
            {
                IsBackground = true
            };
            listenerThread.Start();

            base.OnStartup(e);

            Settings = new SettingsService();
            Translator = new TranslationEngine();

            if (!string.IsNullOrEmpty(imageArg))
            {
                Current.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
                {
                    OpenImageFileForTranslation(imageArg);
                });
            }
        }

        private static string GetPendingImageQueuePath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Lingo", "pending_image.txt");
        }

        private static string? ExtractImageArgument(string[] args)
        {
            if (args == null || args.Length == 0) return null;

            foreach (var arg in args)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (arg.Equals("--image", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--translate", StringComparison.OrdinalIgnoreCase))
                    continue;

                string clean = arg.Trim('"', '\'');
                if (File.Exists(clean))
                {
                    return clean;
                }
            }

            return null;
        }

        public static void OpenImageFileForTranslation(string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return;

                using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var bmp = new System.Drawing.Bitmap(fs);
                var cloned = (System.Drawing.Bitmap)bmp.Clone();

                Lingo.MainWindow.OpenImageTranslateWindow(cloned);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to open image file {filePath}: {ex.Message}");
            }
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _isExiting = true;
            try
            {
                _showEvent?.Set();
                _showEvent?.Dispose();
                _mutex?.ReleaseMutex();
                _mutex?.Dispose();
            }
            catch { }

            base.OnExit(e);
        }
    }
}
