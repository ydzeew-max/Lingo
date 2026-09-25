using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Lingo.Models;
using Lingo.Services;

namespace Lingo
{
    public partial class SettingsWindow : Window
    {
        public SettingsWindow()
        {
            InitializeComponent();
            LoadCurrentSettings();
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 45)
            {
                DragMove();
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is ScrollViewer scv)
            {
                scv.ScrollToVerticalOffset(scv.VerticalOffset - e.Delta * 0.4);
                e.Handled = true;
            }
        }

        private void LoadCurrentSettings()
        {
            var s = App.Settings.CurrentSettings;

            HotkeyText.Text = $"{s.HotkeyModifiers} + {s.HotkeyKey}";

            // Select matching API
            string currentApi = s.TranslationApi ?? "Google";
            bool matched = false;

            foreach (var item in TranslationApiCombo.Items)
            {
                if (item is ComboBoxItem cbi)
                {
                    string tag = cbi.Tag?.ToString() ?? cbi.Content?.ToString() ?? "";
                    if (tag.Equals(currentApi, StringComparison.OrdinalIgnoreCase) ||
                        cbi.Content?.ToString()?.Contains(currentApi, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        TranslationApiCombo.SelectedItem = cbi;
                        matched = true;
                        break;
                    }
                }
            }

            if (!matched && TranslationApiCombo.Items.Count > 0)
            {
                TranslationApiCombo.SelectedIndex = 0;
            }

            // Sync with real Windows Startup state
            bool isAutoStart = StartupService.IsStartupEnabled() || s.StartWithWindows;
            StartWithWindowsCheck.IsChecked = isAutoStart;

            // Minimize to tray setting
            MinimizeToTrayCheck.IsChecked = s.MinimizeToTrayOnClose;

            // Always on top setting
            AlwaysOnTopCheck.IsChecked = s.AlwaysOnTop;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var s = App.Settings.CurrentSettings;

            if (TranslationApiCombo.SelectedItem is ComboBoxItem selectedItem)
            {
                s.TranslationApi = selectedItem.Tag?.ToString() ?? "Google";
            }
            else
            {
                s.TranslationApi = "Google";
            }

            bool startWithWin = StartWithWindowsCheck.IsChecked ?? false;
            s.StartWithWindows = startWithWin;

            s.MinimizeToTrayOnClose = MinimizeToTrayCheck.IsChecked ?? false;
            s.AlwaysOnTop = AlwaysOnTopCheck.IsChecked ?? true;

            // Apply real Windows Registry autostart
            StartupService.SetStartup(startWithWin);

            App.Settings.Save(s);

            DialogResult = true;
            Close();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
