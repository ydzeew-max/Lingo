using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Lingo.Models;
using Lingo.Services;
using MaterialDesignThemes.Wpf;
using WpfClipboard = System.Windows.Clipboard;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfPoint = System.Windows.Point;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;
using WpfTextAlignment = System.Windows.TextAlignment;
using WpfCursors = System.Windows.Input.Cursors;
using WpfColor = System.Windows.Media.Color;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using WpfMouseWheelEventArgs = System.Windows.Input.MouseWheelEventArgs;

namespace Lingo
{
    public partial class ImageTranslateWindow : Window
    {
        private static readonly string[] SourceLanguages = new[]
        {
            "Автоопределение",
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

        private static readonly string[] TargetLanguages = new[]
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

        private Bitmap _currentBitmap;
        private OcrVisualResult? _visualResult;
        private readonly OcrService _ocrService = new();
        private readonly TextToSpeechService _tts = new();
        private CancellationTokenSource? _cts;
        private bool _isInitializing = true;
        private bool _isTranslatedMode = true;

        // Zoom & Pan state
        private double _currentZoom = 1.0;
        private bool _isPanning = false;
        private WpfPoint _panStartPoint;
        private double _initialTranslateX;
        private double _initialTranslateY;

        public ImageTranslateWindow(Bitmap bitmap)
        {
            InitializeComponent();
            _currentBitmap = (Bitmap)bitmap.Clone();

            _tts.SpeechStarted += Tts_SpeechStarted;
            _tts.SpeechFinished += Tts_SpeechFinished;

            Loaded += Window_Loaded;
            Closed += Window_Closed;
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (e.ButtonState == MouseButtonState.Pressed && e.GetPosition(this).Y < 46)
            {
                DragMove();
            }
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            // Smooth Window Entrance Animation (Fade + Scale In)
            PlayEntranceAnimation();

            SourceLangCombo.ItemsSource = SourceLanguages;
            TargetLangCombo.ItemsSource = TargetLanguages;

            // Default: Auto-detect source, Russian target
            SourceLangCombo.SelectedIndex = 0; // "Автоопределение"
            SelectComboItem(TargetLangCombo, App.Settings.CurrentSettings.TargetLanguage, "Русский");

            // Select active translation engine
            string currentApi = App.Settings.CurrentSettings.TranslationApi ?? "Google";
            foreach (var item in EngineCombo.Items)
            {
                if (item is ComboBoxItem cbi && (cbi.Tag?.ToString()?.Equals(currentApi, StringComparison.OrdinalIgnoreCase) == true))
                {
                    EngineCombo.SelectedItem = cbi;
                    break;
                }
            }

            DisplayImage(_currentBitmap);

            _isInitializing = false;
            StartRecognitionAndTranslation();
        }

        private void PlayEntranceAnimation()
        {
            Opacity = 0.0;
            var fadeAnim = new DoubleAnimation
            {
                From = 0.0,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.24),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleXAnim = new DoubleAnimation
            {
                From = 0.96,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.26),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleYAnim = new DoubleAnimation
            {
                From = 0.96,
                To = 1.0,
                Duration = TimeSpan.FromSeconds(0.26),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            var transYAnim = new DoubleAnimation
            {
                From = 10.0,
                To = 0.0,
                Duration = TimeSpan.FromSeconds(0.26),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeAnim);
            WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXAnim);
            WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYAnim);
            WindowTranslate.BeginAnimation(TranslateTransform.YProperty, transYAnim);
        }

        private void Window_Closed(object? sender, EventArgs e)
        {
            _cts?.Cancel();
            _tts.Dispose();
            _currentBitmap.Dispose();
        }

        private void DisplayImage(Bitmap bmp)
        {
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            ms.Position = 0;

            var bitmapImage = new BitmapImage();
            bitmapImage.BeginInit();
            bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
            bitmapImage.StreamSource = ms;
            bitmapImage.EndInit();
            bitmapImage.Freeze();

            MainImageDisplay.Source = bitmapImage;
            MainImageDisplay.Width = bmp.Width;
            MainImageDisplay.Height = bmp.Height;

            OverlayCanvas.Width = bmp.Width;
            OverlayCanvas.Height = bmp.Height;
            OverlayCanvas.Children.Clear();

            ResetZoom();
        }

        private async void StartRecognitionAndTranslation()
        {
            if (_currentBitmap == null) return;

            _tts.Stop();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            LoadingOverlay.Visibility = Visibility.Visible;
            LoadingText.Text = "Распознавание и перевод текста...";

            string selectedSource = SourceLangCombo.SelectedItem as string ?? "Автоопределение";
            string targetLang = TargetLangCombo.SelectedItem as string ?? "Русский";
            string engine = (EngineCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Google";

            try
            {
                // Step 1: Visual OCR with Multi-Engine Auto-Detection, Paragraph Clustering & Collision Resolution
                var visualResult = await Task.Run(async () =>
                {
                    return await _ocrService.RecognizeVisualBlocksAsync(_currentBitmap, selectedSource);
                }, token);

                if (token.IsCancellationRequested) return;

                _visualResult = visualResult;

                // Smart language adaptation
                string actualSourceLang = visualResult.DetectedLanguage;

                if (selectedSource.StartsWith("Авто", StringComparison.OrdinalIgnoreCase))
                {
                    if (actualSourceLang.Equals("Русский", StringComparison.OrdinalIgnoreCase) && CleanName(targetLang).Equals("Русский", StringComparison.OrdinalIgnoreCase))
                    {
                        // Text is Russian, auto-switch target to English
                        _isInitializing = true;
                        SelectComboItem(TargetLangCombo, "English", "English");
                        targetLang = "English";
                        _isInitializing = false;
                    }
                    else if (actualSourceLang.Equals("English", StringComparison.OrdinalIgnoreCase) && CleanName(targetLang).Equals("English", StringComparison.OrdinalIgnoreCase))
                    {
                        // Text is English, auto-switch target to Russian
                        _isInitializing = true;
                        SelectComboItem(TargetLangCombo, "Русский", "Русский");
                        targetLang = "Русский";
                        _isInitializing = false;
                    }
                }
                else
                {
                    actualSourceLang = selectedSource;
                }

                if (visualResult.Blocks.Count == 0)
                {
                    LoadingText.Text = "Текст на изображении не обнаружен";
                    await Task.Delay(1200, token);
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                // Step 2: Translate all detected text blocks in parallel
                bool isSameLang = CleanName(actualSourceLang).Equals(CleanName(targetLang), StringComparison.OrdinalIgnoreCase);

                var translationTasks = visualResult.Blocks.Select(async block =>
                {
                    if (isSameLang)
                    {
                        block.TranslatedText = block.OriginalText;
                        return;
                    }

                    try
                    {
                        var res = await App.Translator.TranslateDetailedAsync(block.OriginalText, actualSourceLang, targetLang, engine, token);
                        block.TranslatedText = string.IsNullOrWhiteSpace(res.Text) ? block.OriginalText : res.Text;
                    }
                    catch
                    {
                        block.TranslatedText = block.OriginalText;
                    }
                }).ToArray();

                await Task.WhenAll(translationTasks);

                if (token.IsCancellationRequested) return;

                // Step 3: Render in-place text replacement overlay with cascade animation
                RenderOverlayBlocks(visualResult.Blocks);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Translation error: {ex.Message}");
            }
            finally
            {
                LoadingOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private void RenderOverlayBlocks(List<OcrTextBlock> blocks)
        {
            OverlayCanvas.Children.Clear();

            int blockIndex = 0;
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.TranslatedText))
                    continue;

                double origX = block.X;
                double origY = block.Y;
                double blockW = Math.Max(10, block.Width);
                double blockH = Math.Max(10, block.Height);

                // Allow width to expand gracefully if translated text is longer, without overflowing image bounds
                if (_currentBitmap != null && block.TranslatedText.Length > block.OriginalText.Length)
                {
                    double ratio = (double)block.TranslatedText.Length / Math.Max(1, block.OriginalText.Length);
                    double desiredW = blockW * Math.Min(1.30, ratio);
                    double maxW = Math.Max(blockW, _currentBitmap.Width - origX - 4);
                    blockW = Math.Min(maxW, desiredW);
                }

                var blockBorder = new Border
                {
                    Width = blockW,
                    Height = blockH,
                    Background = new SolidColorBrush(block.BackgroundColor), // 100% opaque solid background matching substrate
                    CornerRadius = new CornerRadius(0),
                    BorderThickness = new Thickness(0),
                    Cursor = WpfCursors.Hand,
                    ClipToBounds = true,
                    Opacity = 0.0
                };

                // Click on block: Left-click copies, Right-click speaks line
                string copyText = block.TranslatedText;
                blockBorder.ToolTip = "ЛКМ — скопировать, ПКМ — озвучить строку";
                blockBorder.MouseLeftButtonUp += (s, e) =>
                {
                    try
                    {
                        WpfClipboard.SetText(copyText);
                        var flash = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(160));
                        blockBorder.BeginAnimation(OpacityProperty, flash);
                    }
                    catch { }
                };

                blockBorder.MouseRightButtonUp += (s, e) =>
                {
                    string targetLang = TargetLangCombo.SelectedItem as string ?? "Русский";
                    _tts.ToggleSpeak(copyText, targetLang, $"block_{origY}");
                };

                // Viewbox with Left alignment strictly preserves line indents, bullet points, and margins!
                var viewbox = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    Margin = new Thickness(1, 0, 1, 0)
                };

                var textBlock = new TextBlock
                {
                    Text = block.TranslatedText,
                    Foreground = new SolidColorBrush(block.TextColor),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = Math.Max(9.0, blockH * 0.78),
                    TextAlignment = WpfTextAlignment.Left,
                    TextWrapping = TextWrapping.NoWrap
                };

                viewbox.Child = textBlock;
                blockBorder.Child = viewbox;

                Canvas.SetLeft(blockBorder, origX);
                Canvas.SetTop(blockBorder, origY);

                OverlayCanvas.Children.Add(blockBorder);

                // Smooth fade in
                var fadeAnim = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(160),
                    BeginTime = TimeSpan.FromMilliseconds(Math.Min(250, blockIndex * 8)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                blockBorder.BeginAnimation(OpacityProperty, fadeAnim);
                blockIndex++;
            }

            UpdateViewMode();
        }

        // ================= ZOOM & PAN LOGIC =================
        private void ViewportGrid_MouseWheel(object sender, WpfMouseWheelEventArgs e)
        {
            double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            double newZoom = Math.Max(0.5, Math.Min(4.5, _currentZoom * zoomFactor));

            if (Math.Abs(newZoom - _currentZoom) > 0.01)
            {
                _currentZoom = newZoom;
                ApplyZoomAnimation(_currentZoom);
                ZoomResetBtn.Content = $"{(int)Math.Round(_currentZoom * 100)}%";
            }

            e.Handled = true;
        }

        private void ApplyZoomAnimation(double targetScale)
        {
            var anim = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            ImageScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            ImageScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void ViewportGrid_MouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Middle || (e.ChangedButton == MouseButton.Left && _currentZoom > 1.05))
            {
                _isPanning = true;
                _panStartPoint = e.GetPosition(ViewportGrid);
                _initialTranslateX = ImageTranslateTransform.X;
                _initialTranslateY = ImageTranslateTransform.Y;
                ViewportGrid.Cursor = WpfCursors.SizeAll;
                ViewportGrid.CaptureMouse();
            }
        }

        private void ViewportGrid_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (_isPanning)
            {
                var curPos = e.GetPosition(ViewportGrid);
                double deltaX = curPos.X - _panStartPoint.X;
                double deltaY = curPos.Y - _panStartPoint.Y;

                ImageTranslateTransform.X = _initialTranslateX + deltaX;
                ImageTranslateTransform.Y = _initialTranslateY + deltaY;
            }
        }

        private void ViewportGrid_MouseUp(object sender, WpfMouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ViewportGrid.ReleaseMouseCapture();
                ViewportGrid.Cursor = WpfCursors.Arrow;
            }
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            ResetZoom();
        }

        private void ResetZoom()
        {
            _currentZoom = 1.0;
            ApplyZoomAnimation(1.0);

            var resetX = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
            var resetY = new DoubleAnimation(0.0, TimeSpan.FromMilliseconds(180)) { EasingFunction = new QuadraticEase() };
            ImageTranslateTransform.BeginAnimation(TranslateTransform.XProperty, resetX);
            ImageTranslateTransform.BeginAnimation(TranslateTransform.YProperty, resetY);

            ZoomResetBtn.Content = "100%";
        }

        // ================= VIEW MODES & TOASTS =================
        private void UpdateViewMode()
        {
            if (_isTranslatedMode)
            {
                OverlayCanvas.Visibility = Visibility.Visible;
                ToggleModeIcon.Kind = PackIconKind.EyeOutline;
                ToggleModeBtn.Background = new SolidColorBrush(WpfColor.FromRgb(55, 55, 55));
                ToggleModeBtn.Foreground = new SolidColorBrush(WpfColor.FromRgb(255, 255, 255));
                ToggleModeBtn.ToolTip = "Скрыть перевод / Показать оригинал (клик)";
            }
            else
            {
                OverlayCanvas.Visibility = Visibility.Collapsed;
                ToggleModeIcon.Kind = PackIconKind.EyeOffOutline;
                ToggleModeBtn.Background = new SolidColorBrush(WpfColor.FromRgb(35, 35, 35));
                ToggleModeBtn.Foreground = (SolidColorBrush)FindResource("TextSecondary");
                ToggleModeBtn.ToolTip = "Показать перевод (клик)";
            }
        }

        private void ToggleMode_Click(object sender, RoutedEventArgs e)
        {
            _isTranslatedMode = !_isTranslatedMode;
            UpdateViewMode();
        }

        private void Language_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (TargetLangCombo.SelectedItem is string tgt)
            {
                App.Settings.CurrentSettings.TargetLanguage = tgt;
                App.Settings.Save(App.Settings.CurrentSettings);
            }
            StartRecognitionAndTranslation();
        }

        private void EngineCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            if (EngineCombo.SelectedItem is ComboBoxItem cbi && cbi.Tag != null)
            {
                App.Settings.CurrentSettings.TranslationApi = cbi.Tag.ToString()!;
                App.Settings.Save(App.Settings.CurrentSettings);
            }
            StartRecognitionAndTranslation();
        }

        private void SwapLanguages_Click(object sender, RoutedEventArgs e)
        {
            _isInitializing = true;

            string src = SourceLangCombo.SelectedItem as string ?? "Автоопределение";
            string tgt = TargetLangCombo.SelectedItem as string ?? "Русский";

            if (src.StartsWith("Авто", StringComparison.OrdinalIgnoreCase))
            {
                // If source was auto, swap detected language with target
                string detected = _visualResult?.DetectedLanguage ?? "Русский";
                SelectComboItem(SourceLangCombo, tgt, "Русский");
                SelectComboItem(TargetLangCombo, detected, "English");
            }
            else
            {
                SelectComboItem(SourceLangCombo, tgt, "English");
                SelectComboItem(TargetLangCombo, src, "Русский");
            }

            _isInitializing = false;
            StartRecognitionAndTranslation();
        }

        private void Tts_SpeechStarted(object? sender, string? tag)
        {
            Dispatcher.Invoke(() =>
            {
                SpeakAllIcon.Kind = PackIconKind.StopCircleOutline;
                SpeakAllIcon.Foreground = new SolidColorBrush(WpfColor.FromRgb(52, 211, 153)); // Emerald green
                SpeakAllBtn.ToolTip = "Остановить озвучку (клик)";
            });
        }

        private void Tts_SpeechFinished(object? sender, string? tag)
        {
            Dispatcher.Invoke(() =>
            {
                SpeakAllIcon.Kind = PackIconKind.VolumeHigh;
                SpeakAllIcon.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
                SpeakAllBtn.ToolTip = "Озвучить переведенный текст (клик)";
            });
        }

        private void SpeakAll_Click(object sender, RoutedEventArgs e)
        {
            if (_visualResult?.Blocks != null && _visualResult.Blocks.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var b in _visualResult.Blocks)
                {
                    if (!string.IsNullOrWhiteSpace(b.TranslatedText))
                    {
                        sb.AppendLine(b.TranslatedText);
                    }
                }

                string full = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(full))
                {
                    string targetLang = TargetLangCombo.SelectedItem as string ?? "Русский";
                    _tts.ToggleSpeak(full, targetLang, "image_all");
                }
            }
        }

        private void CopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_visualResult?.Blocks != null && _visualResult.Blocks.Count > 0)
            {
                var sb = new StringBuilder();
                foreach (var b in _visualResult.Blocks)
                {
                    if (!string.IsNullOrWhiteSpace(b.TranslatedText))
                    {
                        sb.AppendLine(b.TranslatedText);
                    }
                }

                string full = sb.ToString().Trim();
                if (!string.IsNullOrWhiteSpace(full))
                {
                    try
                    {
                        WpfClipboard.SetText(full);
                        AnimateCheckmark(CopyAllIcon, CopyAllScale);
                    }
                    catch { }
                }
            }
        }

        private void AnimateCheckmark(PackIcon icon, ScaleTransform scale)
        {
            icon.Kind = PackIconKind.Check;
            icon.Foreground = new SolidColorBrush(WpfColor.FromRgb(52, 211, 153)); // Emerald green

            var scalePop = new DoubleAnimation
            {
                From = 0.35,
                To = 1.0,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new BackEase { Amplitude = 0.45, EasingMode = EasingMode.EaseOut }
            };
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, scalePop);
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, scalePop);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
            timer.Tick += (s, ev) =>
            {
                timer.Stop();
                var fadeBack = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(160));
                icon.BeginAnimation(OpacityProperty, fadeBack);
                icon.Kind = PackIconKind.ContentCopy;
                icon.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
            };
            timer.Start();
        }

        private async void NewSnip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _tts.Stop();
                Hide();
                await Task.Delay(150);

                var snipWindow = new SnippingWindow();
                bool? result = snipWindow.ShowDialog();

                Show();
                Activate();

                if (result == true && snipWindow.CapturedBitmap != null)
                {
                    _currentBitmap.Dispose();
                    _currentBitmap = (Bitmap)snipWindow.CapturedBitmap.Clone();
                    DisplayImage(_currentBitmap);
                    StartRecognitionAndTranslation();
                }
            }
            catch (Exception ex)
            {
                Show();
                System.Diagnostics.Debug.WriteLine($"New snip error: {ex.Message}");
            }
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

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Maximize_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized)
            {
                WindowState = WindowState.Normal;
                MaximizeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CropSquare;
            }
            else
            {
                WindowState = WindowState.Maximized;
                MaximizeIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.WindowRestore;
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
