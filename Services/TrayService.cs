using System;
using System.Drawing;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;

namespace Lingo.Services
{
    public class TrayService : IDisposable
    {
        private NotifyIcon? _notifyIcon;
        private readonly Action _showMainWindow;
        private readonly Action _openSettings;

        public TrayService(Action showMainWindow, Action openSettings)
        {
            _showMainWindow = showMainWindow;
            _openSettings = openSettings;
            InitializeTray();
        }

        private void InitializeTray()
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "Lingo - Переводчик",
                Icon = AppIconHelper.CreateAppIcon(),
                Visible = true
            };

            _notifyIcon.DoubleClick += (s, e) => _showMainWindow();

            _notifyIcon.MouseUp += (s, e) =>
            {
                if (e.Button == MouseButtons.Right)
                {
                    WpfApplication.Current.Dispatcher.Invoke(() =>
                    {
                        var cursor = Cursor.Position;
                        var menu = new TrayMenuWindow(
                            _showMainWindow,
                            _openSettings,
                            () =>
                            {
                                Dispose();
                                WpfApplication.Current.Shutdown();
                            }
                        );
                        menu.ShowAtCursor(cursor);
                    });
                }
                else if (e.Button == MouseButtons.Left)
                {
                    _showMainWindow();
                }
            };
        }

        public void Dispose()
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
        }
    }
}
