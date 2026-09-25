using System;
using System.Drawing;
using System.Windows;
using System.Windows.Forms;
using WpfApplication = System.Windows.Application;
using WpfPoint = System.Windows.Point;

namespace Lingo
{
    public partial class TrayMenuWindow : Window
    {
        private readonly Action _onOpen;
        private readonly Action _onSettings;
        private readonly Action _onExit;
        private bool _isClosing = false;

        public TrayMenuWindow(Action onOpen, Action onSettings, Action onExit)
        {
            InitializeComponent();
            _onOpen = onOpen;
            _onSettings = onSettings;
            _onExit = onExit;
        }

        public void ShowAtCursor(System.Drawing.Point cursor)
        {
            var screen = Screen.FromPoint(cursor);
            var source = PresentationSource.FromVisual(this);
            double dpiX = source?.CompositionTarget?.TransformToDevice.M11 ?? 1.0;
            double dpiY = source?.CompositionTarget?.TransformToDevice.M22 ?? 1.0;

            double menuWidth = Width > 0 ? Width : 190;
            double menuHeight = 155;

            double targetLeft = (cursor.X / dpiX) - (menuWidth / 2);
            double targetTop = (cursor.Y / dpiY) - menuHeight - 8;

            double workLeft = screen.WorkingArea.Left / dpiX;
            double workTop = screen.WorkingArea.Top / dpiY;
            double workRight = screen.WorkingArea.Right / dpiX;
            double workBottom = screen.WorkingArea.Bottom / dpiY;

            if (targetLeft + menuWidth > workRight)
                targetLeft = workRight - menuWidth - 8;
            if (targetLeft < workLeft)
                targetLeft = workLeft + 8;

            if (targetTop < workTop)
                targetTop = (cursor.Y / dpiY) + 8;
            if (targetTop + menuHeight > workBottom)
                targetTop = workBottom - menuHeight - 8;

            Left = targetLeft;
            Top = targetTop;

            Show();
            Activate();
            Focus();
        }

        private void Window_Deactivated(object? sender, EventArgs e)
        {
            CloseMenu();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            _onOpen.Invoke();
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            _onSettings.Invoke();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            CloseMenu();
            _onExit.Invoke();
        }

        private void CloseMenu()
        {
            if (_isClosing) return;
            _isClosing = true;
            try
            {
                Close();
            }
            catch { }
        }
    }
}
