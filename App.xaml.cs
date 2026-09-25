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

            // Single Instance Enforcement
            _mutex = new Mutex(true, MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Signal the already-running instance to restore from tray / show up
                try
                {
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
