using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Lingo.Models;
using Lingo.Services;
using MaterialDesignThemes.Wpf;
using WpfApplication = System.Windows.Application;
using WpfClipboard = System.Windows.Clipboard;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfDataFormats = System.Windows.DataFormats;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfColor = System.Windows.Media.Color;

namespace Lingo
{
    public partial class MainWindow : Window
    {
        private static readonly string[] SupportedLanguages = new[]
        {
            "Русский",
            "English",
            "Deutsch (Немецкий)",
            "Français (Французский)",
            "Español (Испанский)",
            "Italiano (Итальянский)",
            "中文 (Китайский)",
            "日本語 (Японский)",
            "한국어 (Корейский)",
            "Português (Португальский)",
            "Türkçe (Турецкий)",
            "العربية (Арабский)",
            "Polski (Польский)",
            "Українська (Украинский)",
            "Nederlands (Нидерландский)"
        };

        private HotkeyService? _hotkeyService;
        private TrayService? _trayService;
        private CancellationTokenSource? _cts;
        private readonly DispatcherTimer _debounceTimer;
        private readonly OcrService _ocrService = new();
        private readonly TextToSpeechService _tts = new();
        private readonly VoiceInputService _voiceInput = new();
        private bool _isInitializing = true;
        private double _currentRotation = 0;

        public MainWindow()
        {
            InitializeComponent();
            InitializeLanguageDropdowns();

            _tts.SpeechStarted += Tts_SpeechStarted;
            _tts.SpeechFinished += Tts_SpeechFinished;
            _voiceInput.TextRecognized += VoiceInput_TextRecognized;

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                PerformTranslation();
            };

            Loaded += MainWindow_Loaded;
            Unloaded += MainWindow_Unloaded;
            StateChanged += MainWindow_StateChanged;

            Topmost = App.Settings.CurrentSettings.AlwaysOnTop;

            _trayService = new TrayService(ShowAndActivate, OpenSettingsWindow);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 45)
            {
                DragMove();
            }
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (App.Settings.CurrentSettings.MinimizeToTrayOnClose)
            {
                e.Cancel = true;
                Hide();
                MemoryOptimizer.TrimMemory();
            }
            else
            {
                _trayService?.Dispose();
                base.OnClosing(e);
            }
        }

        private void InitializeLanguageDropdowns()
        {
            _isInitializing = true;

            SourceLangCombo.ItemsSource = SupportedLanguages;
            TargetLangCombo.ItemsSource = SupportedLanguages;

            string savedSource = App.Settings.CurrentSettings.SourceLanguage;
            string savedTarget = App.Settings.CurrentSettings.TargetLanguage;

            SelectComboItem(SourceLangCombo, savedSource, "English");
            SelectComboItem(TargetLangCombo, savedTarget, "Русский");

            _isInitializing = false;
        }

        private void SelectComboItem(WpfComboBox combo, string? name, string fallback)
        {
            if (string.IsNullOrWhiteSpace(name))
                name = fallback;

            string clean = CleanName(name);

            foreach (var item in combo.Items)
            {
                if (item is string s && (s.Equals(name, StringComparison.OrdinalIgnoreCase) || CleanName(s).Equals(clean, StringComparison.OrdinalIgnoreCase)))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            // Fallback match
            foreach (var item in combo.Items)
            {
                if (item is string s && CleanName(s).Equals(CleanName(fallback), StringComparison.OrdinalIgnoreCase))
                {
                    combo.SelectedItem = item;
                    return;
                }
            }

            if (combo.Items.Count > 0)
                combo.SelectedIndex = 0;
        }

        private string CleanName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            int idx = name.IndexOf(" (");
            if (idx > 0) return name.Substring(0, idx).Trim();
            idx = name.IndexOf(" [");
            if (idx > 0) return name.Substring(0, idx).Trim();
            return name.Trim();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Topmost = App.Settings.CurrentSettings.AlwaysOnTop;

            if (App.IsStartingWithImage)
            {
                // Launched directly with an image: hide main window and stay in tray
                Hide();
            }
            else
            {
                PlayEntranceAnimation();
            }

            try
            {
                var handle = new WindowInteropHelper(this).Handle;
                _hotkeyService = new HotkeyService();
                _hotkeyService.Register(handle, HotkeyService.MOD_CONTROL | HotkeyService.MOD_ALT, 0x54); // Ctrl+Alt+T and Ctrl+Alt+S
                _hotkeyService.HotkeyPressed += HotkeyService_HotkeyPressed;
                _hotkeyService.SnipHotkeyPressed += HotkeyService_SnipHotkeyPressed;
            }
            catch { }
        }

        private void PlayEntranceAnimation()
        {
            Opacity = 0.0;
            var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleXAnim = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleYAnim = new DoubleAnimation(0.96, 1.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var transYAnim = new DoubleAnimation(8.0, 0.0, TimeSpan.FromMilliseconds(240))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeAnim);
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, transYAnim);
        }

        private void MainWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            _voiceInput.Dispose();
            _tts.Dispose();
            _hotkeyService?.Dispose();
            _trayService?.Dispose();
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                MemoryOptimizer.TrimMemory();
            }
        }

        private void HotkeyService_HotkeyPressed(object? sender, EventArgs e)
        {
            ToggleVisibility();
        }

        private async void HotkeyService_SnipHotkeyPressed(object? sender, EventArgs e)
        {
            await TriggerScreenSnipAsync();
        }

        public async Task TriggerScreenSnipAsync()
        {
            try
            {
                bool wasVisible = IsVisible && WindowState != WindowState.Minimized;
                if (wasVisible)
                {
                    Hide();
                    await Task.Delay(140);
                }

                var snipWindow = new SnippingWindow();
                bool? result = snipWindow.ShowDialog();

                if (result == true && snipWindow.CapturedBitmap != null)
                {
                    using var captured = snipWindow.CapturedBitmap;
                    OpenImageTranslateWindow(captured);
                }
                else if (wasVisible)
                {
                    ShowAndActivate();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Snip hotkey error: {ex.Message}");
            }
        }

        public void ToggleVisibility()
        {
            Dispatcher.Invoke(() =>
            {
                if (IsVisible && WindowState != WindowState.Minimized && IsActive)
                {
                    Hide();
                    MemoryOptimizer.TrimMemory();
                }
                else
                {
                    ShowAndActivate();
                }
            });
        }

        public void ShowAndActivate()
        {
            Dispatcher.Invoke(() =>
            {
                if (WindowState == WindowState.Minimized)
                {
                    WindowState = WindowState.Normal;
                }
                Show();
                Activate();
                Focus();
                PlayEntranceAnimation();
            });
        }

        private async void PerformTranslation()
        {
            if (_isInitializing || InputTextBox == null || OutputTextBox == null)
                return;

            string sourceLang = SourceLangCombo.SelectedItem as string ?? "English";
            string targetLang = TargetLangCombo.SelectedItem as string ?? "Русский";
            string textToTranslate = InputTextBox.Text;

            // Save user language preferences
            App.Settings.CurrentSettings.SourceLanguage = sourceLang;
            App.Settings.CurrentSettings.TargetLanguage = targetLang;
            App.Settings.Save(App.Settings.CurrentSettings);

            if (string.IsNullOrWhiteSpace(textToTranslate))
            {
                OutputTextBox.Text = string.Empty;
                OfflineStatusBadge.Visibility = Visibility.Collapsed;
                return;
            }

            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            try
            {
                string api = App.Settings.CurrentSettings.TranslationApi;
                var result = await Task.Run(async () =>
                {
                    return await App.Translator.TranslateDetailedAsync(textToTranslate, sourceLang, targetLang, api, token);
                }, token);

                if (!token.IsCancellationRequested)
                {
                    OutputTextBox.Text = result.Text;

                    if (result.IsOfflineFallback)
                    {
                        OfflineStatusBadge.Visibility = Visibility.Visible;
                        OfflineStatusText.Text = "Нет сети: офлайн-база";
                    }
                    else if (api.Equals("offline", StringComparison.OrdinalIgnoreCase))
                    {
                        OfflineStatusBadge.Visibility = Visibility.Visible;
                        OfflineStatusText.Text = "Офлайн-словарь";
                    }
                    else
                    {
                        OfflineStatusBadge.Visibility = Visibility.Collapsed;
                    }
                }
            }
            catch { }
        }

        private void InputTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;
            _debounceTimer.Stop();
            _debounceTimer.Start();
        }

        private void InputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            var anim = new ColorAnimation
            {
                To = WpfColor.FromRgb(96, 165, 250), // Subtle blue accent
                Duration = TimeSpan.FromMilliseconds(180)
            };
            var brush = new SolidColorBrush(WpfColor.FromRgb(51, 51, 51));
            InputCardBorder.BorderBrush = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }

        private void InputTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            var anim = new ColorAnimation
            {
                To = WpfColor.FromRgb(51, 51, 51),
                Duration = TimeSpan.FromMilliseconds(200)
            };
            if (InputCardBorder.BorderBrush is SolidColorBrush brush)
            {
                brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
        }

        private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            PerformTranslation();
        }

        private void SwapLanguages_Click(object sender, RoutedEventArgs e)
        {
            // Smooth Rotation Animation for Swap Button
            _currentRotation += 180;
            var anim = new DoubleAnimation
            {
                To = _currentRotation,
                Duration = TimeSpan.FromSeconds(0.24),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };
            var transform = new RotateTransform();
            SwapButton.RenderTransform = transform;
            SwapButton.RenderTransformOrigin = new WpfPoint(0.5, 0.5);
            transform.BeginAnimation(RotateTransform.AngleProperty, anim);

            // Subtle Cross-fade Animation on Text
            var fadeOut = new DoubleAnimation(0.5, 1.0, TimeSpan.FromMilliseconds(220));
            InputTextBox.BeginAnimation(OpacityProperty, fadeOut);
            OutputTextBox.BeginAnimation(OpacityProperty, fadeOut);

            _isInitializing = true;

            int srcIdx = SourceLangCombo.SelectedIndex;
            int tgtIdx = TargetLangCombo.SelectedIndex;

            SourceLangCombo.SelectedIndex = tgtIdx;
            TargetLangCombo.SelectedIndex = srcIdx;

            // Swap text contents if available
            string input = InputTextBox.Text;
            string output = OutputTextBox.Text;

            if (!string.IsNullOrWhiteSpace(output) && !output.StartsWith("["))
            {
                InputTextBox.Text = output;
                OutputTextBox.Text = input;
            }

            _isInitializing = false;
            PerformTranslation();
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenSettingsWindow();
        }

        public void OpenSettingsWindow()
        {
            Dispatcher.Invoke(() =>
            {
                try
                {
                    var settingsWindow = new SettingsWindow
                    {
                        Owner = this
                    };
                    if (settingsWindow.ShowDialog() == true)
                    {
                        Topmost = App.Settings.CurrentSettings.AlwaysOnTop;
                        PerformTranslation();
                    }
                }
                catch { }
            });
        }

        private void ClearInput_Click(object sender, RoutedEventArgs e)
        {
            _debounceTimer.Stop();
            InputTextBox.Text = string.Empty;
            OutputTextBox.Text = string.Empty;
            OfflineStatusBadge.Visibility = Visibility.Collapsed;
            OcrStatusBadge.Visibility = Visibility.Collapsed;
            InputTextBox.Focus();
        }

        private async void PhotoSnip_Click(object sender, RoutedEventArgs e)
        {
            await TriggerScreenSnipAsync();
        }

        private async void PasteImage_Click(object sender, RoutedEventArgs e)
        {
            await ProcessClipboardImageAsync();
        }

        private void OpenFile_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Выберите изображение с текстом",
                    Filter = "Изображения (*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff)|*.png;*.jpg;*.jpeg;*.bmp;*.webp;*.tiff|Все файлы (*.*)|*.*"
                };

                if (dialog.ShowDialog(this) == true)
                {
                    using var fs = new System.IO.FileStream(dialog.FileName, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                    using var bmp = new System.Drawing.Bitmap(fs);
                    OpenImageTranslateWindow(bmp);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open file error: {ex.Message}");
            }
        }

        private void Window_PreviewDragOver(object sender, WpfDragEventArgs e)
        {
            if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
            {
                e.Effects = WpfDragDropEffects.Copy;
                e.Handled = true;
            }
        }

        private void Window_PreviewDrop(object sender, WpfDragEventArgs e)
        {
            try
            {
                if (e.Data.GetDataPresent(WpfDataFormats.FileDrop))
                {
                    string[]? files = e.Data.GetData(WpfDataFormats.FileDrop) as string[];
                    if (files != null && files.Length > 0)
                    {
                        string file = files[0];
                        string ext = System.IO.Path.GetExtension(file).ToLowerInvariant();
                        if (ext is ".png" or ".jpg" or ".jpeg" or ".bmp" or ".webp" or ".tiff")
                        {
                            using var fs = new System.IO.FileStream(file, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite);
                            using var bmp = new System.Drawing.Bitmap(fs);
                            OpenImageTranslateWindow(bmp);
                            e.Handled = true;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Window drop error: {ex.Message}");
            }
        }

        private async void InputTextBox_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.V && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                if (System.Windows.Forms.Clipboard.ContainsImage())
                {
                    e.Handled = true;
                    await ProcessClipboardImageAsync();
                }
            }
        }

        private async Task ProcessClipboardImageAsync()
        {
            try
            {
                if (System.Windows.Forms.Clipboard.ContainsImage())
                {
                    using var img = System.Windows.Forms.Clipboard.GetImage();
                    if (img != null)
                    {
                        using var bmp = new System.Drawing.Bitmap(img);
                        OpenImageTranslateWindow(bmp);
                        await ProcessBitmapForOcrAsync(bmp);
                    }
                }
            }
            catch { }
        }

        public static void OpenImageTranslateWindow(System.Drawing.Bitmap bitmap)
        {
            try
            {
                // Close any existing photo translation windows to guarantee a clean slate from scratch
                foreach (var win in System.Windows.Application.Current.Windows.OfType<ImageTranslateWindow>().ToList())
                {
                    try { win.Close(); } catch { }
                }

                var mainWindow = System.Windows.Application.Current.MainWindow;
                var imgWin = new ImageTranslateWindow(bitmap)
                {
                    Owner = mainWindow != null && mainWindow.IsVisible ? mainWindow : null
                };
                imgWin.Show();
                imgWin.Activate();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Open image window error: {ex.Message}");
            }
        }

        private async Task ProcessBitmapForOcrAsync(System.Drawing.Bitmap bitmap)
        {
            if (bitmap == null) return;

            OcrStatusBadge.Visibility = Visibility.Visible;
            OcrStatusText.Text = "Распознавание...";

            try
            {
                string sourceLang = SourceLangCombo.SelectedItem as string ?? "English";
                string recognized = await Task.Run(async () =>
                {
                    return await _ocrService.RecognizeBitmapAsync(bitmap, sourceLang);
                });

                if (!string.IsNullOrWhiteSpace(recognized))
                {
                    InputTextBox.Text = recognized;
                    OcrStatusText.Text = "Текст распознан";
                    
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        if (OcrStatusText.Text == "Текст распознан")
                            OcrStatusBadge.Visibility = Visibility.Collapsed;
                    };
                    timer.Start();

                    PerformTranslation();
                }
                else
                {
                    OcrStatusText.Text = "Текст не найден";
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        OcrStatusBadge.Visibility = Visibility.Collapsed;
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                OcrStatusBadge.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"OCR processing error: {ex.Message}");
            }
        }

        private async Task ProcessFileForOcrAsync(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !System.IO.File.Exists(filePath))
                return;

            OcrStatusBadge.Visibility = Visibility.Visible;
            OcrStatusText.Text = "Распознавание...";

            try
            {
                string sourceLang = SourceLangCombo.SelectedItem as string ?? "English";
                string recognized = await Task.Run(async () =>
                {
                    return await _ocrService.RecognizeImageFileAsync(filePath, sourceLang);
                });

                if (!string.IsNullOrWhiteSpace(recognized))
                {
                    InputTextBox.Text = recognized;
                    OcrStatusText.Text = "Текст распознан";

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        if (OcrStatusText.Text == "Текст распознан")
                            OcrStatusBadge.Visibility = Visibility.Collapsed;
                    };
                    timer.Start();

                    PerformTranslation();
                }
                else
                {
                    OcrStatusText.Text = "Текст не найден";
                    var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (s, e) =>
                    {
                        timer.Stop();
                        OcrStatusBadge.Visibility = Visibility.Collapsed;
                    };
                    timer.Start();
                }
            }
            catch (Exception ex)
            {
                OcrStatusBadge.Visibility = Visibility.Collapsed;
                System.Diagnostics.Debug.WriteLine($"File OCR error: {ex.Message}");
            }
        }

        private void CopyOutput_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(OutputTextBox.Text))
            {
                try
                {
                    WpfClipboard.SetText(OutputTextBox.Text);

                    // Animated pop checkmark
                    CopyIcon.Kind = PackIconKind.Check;
                    CopyIcon.Foreground = new SolidColorBrush(WpfColor.FromRgb(52, 211, 153)); // Emerald green

                    var scalePop = new DoubleAnimation
                    {
                        From = 0.35,
                        To = 1.0,
                        Duration = TimeSpan.FromMilliseconds(200),
                        EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
                    };
                    CopyIconScale.BeginAnimation(ScaleTransform.ScaleXProperty, scalePop);
                    CopyIconScale.BeginAnimation(ScaleTransform.ScaleYProperty, scalePop);

                    var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
                    timer.Tick += (s, ev) =>
                    {
                        timer.Stop();
                        var fadeBack = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(160));
                        CopyIcon.BeginAnimation(OpacityProperty, fadeBack);
                        CopyIcon.Kind = PackIconKind.ContentCopy;
                        CopyIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                    };
                    timer.Start();
                }
                catch { }
            }
        }

        private void Tts_SpeechStarted(object? sender, string? tag)
        {
            Dispatcher.Invoke(() =>
            {
                if (tag == "input")
                {
                    SpeakInputIcon.Kind = PackIconKind.StopCircleOutline;
                    SpeakInputIcon.Foreground = new SolidColorBrush(WpfColor.FromRgb(96, 165, 250)); // Blue accent
                    SpeakInputButton.ToolTip = "Остановить озвучку (клик)";
                }
                else if (tag == "output")
                {
                    SpeakOutputIcon.Kind = PackIconKind.StopCircleOutline;
                    SpeakOutputIcon.Foreground = new SolidColorBrush(WpfColor.FromRgb(52, 211, 153)); // Emerald green
                    SpeakOutputButton.ToolTip = "Остановить озвучку (клик)";
                }
            });
        }

        private void Tts_SpeechFinished(object? sender, string? tag)
        {
            Dispatcher.Invoke(() =>
            {
                SpeakInputIcon.Kind = PackIconKind.VolumeHigh;
                SpeakInputIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                SpeakInputButton.ToolTip = "Озвучить исходный текст (клик)";

                SpeakOutputIcon.Kind = PackIconKind.VolumeHigh;
                SpeakOutputIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                SpeakOutputButton.ToolTip = "Озвучить перевод (клик)";
            });
        }

        private void SpeakInput_Click(object sender, RoutedEventArgs e)
        {
            string text = InputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            string lang = SourceLangCombo.SelectedItem as string ?? "Русский";
            _tts.ToggleSpeak(text, lang, "input");
        }

        private void SpeakOutput_Click(object sender, RoutedEventArgs e)
        {
            string text = OutputTextBox.Text;
            if (string.IsNullOrWhiteSpace(text)) return;
            string lang = TargetLangCombo.SelectedItem as string ?? "English";
            _tts.ToggleSpeak(text, lang, "output");
        }

        private void VoiceInput_Click(object sender, RoutedEventArgs e)
        {
            if (InputTextBox.Text.StartsWith("Выделите текст"))
            {
                InputTextBox.Text = string.Empty;
            }

            _tts.Stop();

            InputTextBox.Focus();
            InputTextBox.CaretIndex = InputTextBox.Text.Length;

            _voiceInput.TriggerVoiceInput();

            // Visual feedback: coral red pulse
            VoiceInputIcon.Foreground = new SolidColorBrush(WpfColor.FromRgb(239, 68, 68));
            VoiceInputButton.ToolTip = "Голосовой ввод активен (говорите в микрофон, либо нажмите Win + H)";

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3.5) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                VoiceInputIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextSecondary");
                VoiceInputButton.ToolTip = "Голосовой ввод (клик)";
            };
            timer.Start();
        }

        private void VoiceInput_TextRecognized(object? sender, string text)
        {
            Dispatcher.Invoke(() =>
            {
                if (string.IsNullOrWhiteSpace(text)) return;
                if (string.IsNullOrWhiteSpace(InputTextBox.Text) || InputTextBox.Text.StartsWith("Выделите текст"))
                {
                    InputTextBox.Text = text;
                }
                else
                {
                    InputTextBox.Text += " " + text;
                }
                InputTextBox.CaretIndex = InputTextBox.Text.Length;
                PerformTranslation();
            });
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
            MemoryOptimizer.TrimMemory();
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            if (App.Settings.CurrentSettings.MinimizeToTrayOnClose)
            {
                Hide();
                MemoryOptimizer.TrimMemory();
            }
            else
            {
                _trayService?.Dispose();
                WpfApplication.Current.Shutdown();
            }
        }
    }
}
