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

        public static bool IsStartingWithImage { get; private set; }

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
            IsStartingWithImage = !string.IsNullOrEmpty(imageArg);

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
                        File.WriteAllText(queueFile, imageArg, System.Text.Encoding.UTF8);
                        Thread.Sleep(40);
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
                        string? targetPath = null;

                        for (int attempt = 0; attempt < 8; attempt++)
                        {
                            try
                            {
                                if (File.Exists(queueFile))
                                {
                                    targetPath = File.ReadAllText(queueFile, System.Text.Encoding.UTF8).Trim();
                                    File.Delete(queueFile);
                                    break;
                                }
                            }
                            catch (IOException)
                            {
                                Thread.Sleep(30);
                            }
                            catch { break; }
                        }

                        if (!string.IsNullOrEmpty(targetPath) && File.Exists(targetPath))
                        {
                            OpenImageFileForTranslation(targetPath);
                            return;
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

        private static string? ExtractImageArgument(string[]? args)
        {
            var allArgs = new System.Collections.Generic.List<string>();
            if (args != null) allArgs.AddRange(args);
            try
            {
                var cmdLine = Environment.GetCommandLineArgs();
                if (cmdLine != null && cmdLine.Length > 1)
                {
                    for (int i = 1; i < cmdLine.Length; i++)
                    {
                        if (!allArgs.Contains(cmdLine[i])) allArgs.Add(cmdLine[i]);
                    }
                }
            }
            catch { }

            foreach (var arg in allArgs)
            {
                if (string.IsNullOrWhiteSpace(arg)) continue;
                if (arg.Equals("--image", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-i", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("--translate", StringComparison.OrdinalIgnoreCase))
                    continue;

                string clean = arg.Trim('"', '\'', ' ');
                if (File.Exists(clean))
                {
                    return Path.GetFullPath(clean);
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
