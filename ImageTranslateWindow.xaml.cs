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
        private double _fitScale = 1.0;
        private bool _isUserZoomed = false;
        private bool _isPanning = false;
        private WpfPoint _lastPanPoint;

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

            // Auto-size window to fit image comfortably up to screen bounds
            var workArea = SystemParameters.WorkArea;
            double targetWinW = Math.Min(workArea.Width * 0.90, Math.Max(MinWidth, _currentBitmap.Width + 60));
            double targetWinH = Math.Min(workArea.Height * 0.90, Math.Max(MinHeight, _currentBitmap.Height + 130));
            Width = targetWinW;
            Height = targetWinH;

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
            var rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, ImageLockMode.ReadOnly, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try
            {
                var bitmapSource = BitmapSource.Create(
                    bmp.Width, bmp.Height,
                    96, 96,
                    PixelFormats.Bgra32,
                    null,
                    bmpData.Scan0,
                    bmpData.Stride * bmp.Height,
                    bmpData.Stride);
                bitmapSource.Freeze();

                MainImageDisplay.Source = bitmapSource;
                MainImageDisplay.Width = bmp.Width;
                MainImageDisplay.Height = bmp.Height;

                OverlayCanvas.Width = bmp.Width;
                OverlayCanvas.Height = bmp.Height;
                OverlayCanvas.Children.Clear();
            }
            finally
            {
                bmp.UnlockBits(bmpData);
            }

            Dispatcher.InvokeAsync(() =>
            {
                FitImageToViewport();
            }, DispatcherPriority.Loaded);
        }

        private void ViewportGrid_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (!_isUserZoomed && _currentBitmap != null)
            {
                FitImageToViewport();
            }
        }

        private void FitImageToViewport()
        {
            if (_currentBitmap == null) return;

            double availW = Math.Max(150, ViewportGrid.ActualWidth > 0 ? ViewportGrid.ActualWidth - 36 : ActualWidth - 70);
            double availH = Math.Max(150, ViewportGrid.ActualHeight > 0 ? ViewportGrid.ActualHeight - 36 : ActualHeight - 130);

            double scaleX = availW / _currentBitmap.Width;
            double scaleY = availH / _currentBitmap.Height;
            double fitScale = Math.Min(scaleX, scaleY);

            // Scale to fit completely without any cropping
            _fitScale = fitScale < 1.0 ? fitScale : 1.0;
            _currentZoom = _fitScale;
            _isUserZoomed = false;

            ImageTranslateTransform.BeginAnimation(TranslateTransform.XProperty, null);
            ImageTranslateTransform.BeginAnimation(TranslateTransform.YProperty, null);
            ImageTranslateTransform.X = 0.0;
            ImageTranslateTransform.Y = 0.0;

            ApplyZoomAnimation(_currentZoom);
            UpdateZoomButtonText();
        }

        private async void StartRecognitionAndTranslation()
        {
            if (_currentBitmap == null) return;

            _tts.Stop();
            _cts?.Cancel();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            LoadingOverlay.Visibility = Visibility.Visible;

            string selectedSource = SourceLangCombo.SelectedItem as string ?? "Автоопределение";
            string targetLang = TargetLangCombo.SelectedItem as string ?? "Русский";
            string engine = (EngineCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "Google";

            try
            {
                // Step 1: Visual OCR with Multi-Engine Auto-Detection & High-Precision Line Segmentation
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
                    await Task.Delay(600, token);
                    LoadingOverlay.Visibility = Visibility.Collapsed;
                    return;
                }

                // Step 2: Context-Aware Paragraph Grouping & Translation
                bool isSameLang = CleanName(actualSourceLang).Equals(CleanName(targetLang), StringComparison.OrdinalIgnoreCase);

                if (isSameLang)
                {
                    foreach (var b in visualResult.Blocks)
                        b.TranslatedText = b.OriginalText;
                }
                else
                {
                    // Group nearby lines into coherent sentence/paragraph clusters for grammatically natural translation
                    var clusters = new List<List<OcrTextBlock>>();
                    var currentCluster = new List<OcrTextBlock>();

                    for (int i = 0; i < visualResult.Blocks.Count; i++)
                    {
                        var block = visualResult.Blocks[i];
                        if (currentCluster.Count == 0)
                        {
                            currentCluster.Add(block);
                        }
                        else
                        {
                            var prev = currentCluster.Last();
                            double vertGap = block.Y - (prev.Y + prev.Height);
                            if (vertGap >= -4.0 && vertGap < Math.Max(prev.Height, block.Height) * 1.5 && Math.Abs(block.X - prev.X) < 180.0)
                            {
                                currentCluster.Add(block);
                            }
                            else
                            {
                                clusters.Add(currentCluster);
                                currentCluster = new List<OcrTextBlock> { block };
                            }
                        }
                    }
                    if (currentCluster.Count > 0)
                    {
                        clusters.Add(currentCluster);
                    }

                    // Translate clusters in parallel with full paragraph context
                    var clusterTasks = clusters.Select(async cluster =>
                    {
                        if (cluster.Count == 1)
                        {
                            try
                            {
                                var res = await App.Translator.TranslateDetailedAsync(cluster[0].OriginalText, actualSourceLang, targetLang, engine, token);
                                cluster[0].TranslatedText = string.IsNullOrWhiteSpace(res.Text) ? cluster[0].OriginalText : res.Text;
                            }
                            catch
                            {
                                cluster[0].TranslatedText = cluster[0].OriginalText;
                            }
                            return;
                        }

                        // Multi-line contextual translation: preserves sentence structure across lines
                        string combinedText = string.Join("\n", cluster.Select(b => b.OriginalText));
                        try
                        {
                            var res = await App.Translator.TranslateDetailedAsync(combinedText, actualSourceLang, targetLang, engine, token);
                            if (!string.IsNullOrWhiteSpace(res.Text))
                            {
                                var translatedLines = res.Text.Split('\n');
                                if (translatedLines.Length == cluster.Count)
                                {
                                    for (int k = 0; k < cluster.Count; k++)
                                    {
                                        cluster[k].TranslatedText = translatedLines[k].Trim();
                                    }
                                    return;
                                }
                            }
                        }
                        catch { }

                        // Fallback to line-by-line if line count mismatch
                        foreach (var b in cluster)
                        {
                            try
                            {
                                var res = await App.Translator.TranslateDetailedAsync(b.OriginalText, actualSourceLang, targetLang, engine, token);
                                b.TranslatedText = string.IsNullOrWhiteSpace(res.Text) ? b.OriginalText : res.Text;
                            }
                            catch
                            {
                                b.TranslatedText = b.OriginalText;
                            }
                        }
                    }).ToArray();

                    await Task.WhenAll(clusterTasks);
                }

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

            double imgW = _currentBitmap != null ? _currentBitmap.Width : 1920.0;
            double imgH = _currentBitmap != null ? _currentBitmap.Height : 1080.0;

            int blockIndex = 0;
            foreach (var block in blocks)
            {
                if (string.IsNullOrWhiteSpace(block.TranslatedText))
                    continue;

                // Strict coordinate clamping within image bounds (prevent flying off screen)
                double origX = Math.Max(0.0, Math.Min(imgW - 10.0, block.X));
                double origY = Math.Max(0.0, Math.Min(imgH - 10.0, block.Y));

                double maxAvailableW = Math.Max(10.0, imgW - origX);
                double maxAvailableH = Math.Max(10.0, imgH - origY);

                double blockW = Math.Max(10.0, Math.Min(maxAvailableW, block.Width));
                double blockH = Math.Max(10.0, Math.Min(maxAvailableH, block.Height));

                // Pixel-accurate background reconstruction (supports gradients, shading, and solid colors)
                var bgBrush = CreateReconstructedBackgroundBrush(
                    _currentBitmap, 
                    (int)origX, (int)origY, (int)blockW, (int)blockH, 
                    block.BackgroundColor, 
                    out WpfColor smartTextColor);

                var blockBorder = new Border
                {
                    Width = blockW,
                    Height = blockH,
                    MaxWidth = maxAvailableW,
                    MaxHeight = maxAvailableH,
                    Background = bgBrush,
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

                // Viewbox with Left alignment strictly preserves line indents and scaling
                var viewbox = new Viewbox
                {
                    Stretch = Stretch.Uniform,
                    StretchDirection = StretchDirection.DownOnly,
                    HorizontalAlignment = WpfHorizontalAlignment.Left,
                    VerticalAlignment = WpfVerticalAlignment.Center,
                    MaxWidth = blockW,
                    MaxHeight = blockH,
                    Margin = new Thickness(1, 0, 1, 0)
                };

                // Dynamic font calculation
                double targetFontSize = Math.Max(9.5, Math.Min(24.0, blockH * 0.76));
                if (block.TranslatedText.Length > block.OriginalText.Length)
                {
                    double exp = (double)block.TranslatedText.Length / Math.Max(1, block.OriginalText.Length);
                    targetFontSize = Math.Max(9.0, targetFontSize / Math.Sqrt(exp));
                }

                var textBlock = new TextBlock
                {
                    Text = block.TranslatedText,
                    Foreground = new SolidColorBrush(smartTextColor),
                    FontFamily = new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = targetFontSize,
                    TextAlignment = WpfTextAlignment.Left,
                    TextWrapping = TextWrapping.NoWrap
                };

                viewbox.Child = textBlock;
                blockBorder.Child = viewbox;

                Canvas.SetLeft(blockBorder, origX);
                Canvas.SetTop(blockBorder, origY);

                OverlayCanvas.Children.Add(blockBorder);

                // Smooth cascade fade in
                var fadeAnim = new DoubleAnimation
                {
                    From = 0.0,
                    To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(160),
                    BeginTime = TimeSpan.FromMilliseconds(Math.Min(250, blockIndex * 6)),
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                blockBorder.BeginAnimation(OpacityProperty, fadeAnim);
                blockIndex++;
            }

            UpdateViewMode();
        }

        private System.Windows.Media.Brush CreateReconstructedBackgroundBrush(
            Bitmap? sourceBmp, 
            int blockX, int blockY, int blockW, int blockH, 
            WpfColor fallbackColor, 
            out WpfColor recommendedTextColor)
        {
            recommendedTextColor = (fallbackColor.R * 0.299 + fallbackColor.G * 0.587 + fallbackColor.B * 0.114 > 132) 
                ? WpfColor.FromRgb(15, 15, 15) 
                : WpfColor.FromRgb(250, 250, 250);

            if (sourceBmp == null || blockW < 4 || blockH < 4)
            {
                return new SolidColorBrush(fallbackColor);
            }

            try
            {
                int imgW = sourceBmp.Width;
                int imgH = sourceBmp.Height;

                int sampleTopY = Math.Max(0, blockY - 2);
                int sampleBotY = Math.Min(imgH - 1, blockY + blockH + 1);
                int sampleLeftX = Math.Max(0, blockX - 2);
                int sampleRightX = Math.Min(imgW - 1, blockX + blockW + 1);

                // Sample edge pixels
                int[] topRow = new int[blockW];
                int[] botRow = new int[blockW];
                for (int x = 0; x < blockW; x++)
                {
                    int px = Math.Clamp(blockX + x, 0, imgW - 1);
                    topRow[x] = sourceBmp.GetPixel(px, sampleTopY).ToArgb();
                    botRow[x] = sourceBmp.GetPixel(px, sampleBotY).ToArgb();
                }

                int[] leftCol = new int[blockH];
                int[] rightCol = new int[blockH];
                for (int y = 0; y < blockH; y++)
                {
                    int py = Math.Clamp(blockY + y, 0, imgH - 1);
                    leftCol[y] = sourceBmp.GetPixel(sampleLeftX, py).ToArgb();
                    rightCol[y] = sourceBmp.GetPixel(sampleRightX, py).ToArgb();
                }

                int cTL = topRow[0];
                int cTR = topRow[blockW - 1];
                int cBL = botRow[0];
                int cBR = botRow[blockW - 1];

                byte tlR = (byte)(cTL >> 16), tlG = (byte)(cTL >> 8), tlB = (byte)cTL;
                byte trR = (byte)(cTR >> 16), trG = (byte)(cTR >> 8), trB = (byte)cTR;
                byte blR = (byte)(cBL >> 16), blG = (byte)(cBL >> 8), blB = (byte)cBL;
                byte brR = (byte)(cBR >> 16), brG = (byte)(cBR >> 8), brB = (byte)cBR;

                // Bilinear boundary blend (Coons Patch) for pixel-for-pixel gradient & texture match
                byte[] pixels = new byte[blockW * blockH * 4];
                int offset = 0;
                long totalLum = 0;

                for (int y = 0; y < blockH; y++)
                {
                    double v = blockH > 1 ? (double)y / (blockH - 1) : 0.5;
                    double invV = 1.0 - v;

                    int leftPixel = leftCol[y];
                    int rightPixel = rightCol[y];
                    byte leftR = (byte)(leftPixel >> 16), leftG = (byte)(leftPixel >> 8), leftB = (byte)leftPixel;
                    byte rightR = (byte)(rightPixel >> 16), rightG = (byte)(rightPixel >> 8), rightB = (byte)rightPixel;

                    for (int x = 0; x < blockW; x++)
                    {
                        double u = blockW > 1 ? (double)x / (blockW - 1) : 0.5;
                        double invU = 1.0 - u;

                        int topPixel = topRow[x];
                        int botPixel = botRow[x];
                        byte topR = (byte)(topPixel >> 16), topG = (byte)(topPixel >> 8), topB = (byte)topPixel;
                        byte botR = (byte)(botPixel >> 16), botG = (byte)(botPixel >> 8), botB = (byte)botPixel;

                        double rTB = invV * topR + v * botR;
                        double gTB = invV * topG + v * botG;
                        double bTB = invV * topB + v * botB;

                        double rLR = invU * leftR + u * rightR;
                        double gLR = invU * leftG + u * rightG;
                        double bLR = invU * leftB + u * rightB;

                        double rCorner = invU * invV * tlR + u * invV * trR + invU * v * blR + u * v * brR;
                        double gCorner = invU * invV * tlG + u * invV * trG + invU * v * blG + u * v * brG;
                        double bCorner = invU * invV * tlB + u * invV * trB + invU * v * blB + u * v * brB;

                        int r = Math.Clamp((int)Math.Round(rTB + rLR - rCorner), 0, 255);
                        int g = Math.Clamp((int)Math.Round(gTB + gLR - gCorner), 0, 255);
                        int b = Math.Clamp((int)Math.Round(bTB + bLR - bCorner), 0, 255);

                        pixels[offset++] = (byte)b;
                        pixels[offset++] = (byte)g;
                        pixels[offset++] = (byte)r;
                        pixels[offset++] = 255;

                        totalLum += (long)(0.299 * r + 0.587 * g + 0.114 * b);
                    }
                }

                double avgLum = (double)totalLum / (blockW * blockH * 255.0);
                recommendedTextColor = avgLum > 0.52 ? WpfColor.FromRgb(15, 15, 15) : WpfColor.FromRgb(250, 250, 250);

                var patchSource = BitmapSource.Create(
                    blockW, blockH,
                    96, 96,
                    PixelFormats.Bgr32,
                    null,
                    pixels,
                    blockW * 4);
                patchSource.Freeze();

                return new ImageBrush(patchSource) { Stretch = Stretch.Fill };
            }
            catch
            {
                return new SolidColorBrush(fallbackColor);
            }
        }

        // ================= ZOOM & PAN LOGIC =================
        private void ViewportGrid_MouseWheel(object sender, WpfMouseWheelEventArgs e)
        {
            _isUserZoomed = true;
            double zoomFactor = e.Delta > 0 ? 1.15 : (1.0 / 1.15);
            double newZoom = Math.Max(0.2, Math.Min(6.0, _currentZoom * zoomFactor));

            if (Math.Abs(newZoom - _currentZoom) > 0.005)
            {
                _currentZoom = newZoom;
                ApplyZoomAnimation(_currentZoom);
                UpdateZoomButtonText();
            }

            e.Handled = true;
        }

        private void ApplyZoomAnimation(double targetScale)
        {
            var anim = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(160),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            ImageScaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, anim);
            ImageScaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, anim);
        }

        private void ViewportGrid_PreviewMouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            bool isMiddle = e.ChangedButton == MouseButton.Middle || e.MiddleButton == MouseButtonState.Pressed;
            bool isRight = e.ChangedButton == MouseButton.Right || e.RightButton == MouseButtonState.Pressed;
            bool isLeft = e.ChangedButton == MouseButton.Left || e.LeftButton == MouseButtonState.Pressed;

            // Middle button always pans everywhere in the viewport.
            // Right button pans when clicking the background (blocks handle RightButtonUp).
            // Left button pans when zoomed in or clicking viewport/canvas/image.
            if (isMiddle || (isRight && e.OriginalSource is not TextBlock) || 
                (isLeft && (_currentZoom > _fitScale * 1.05 || e.OriginalSource is System.Windows.Controls.Image || e.OriginalSource is Grid || e.OriginalSource is Canvas)))
            {
                _isPanning = true;
                _lastPanPoint = e.GetPosition(this);
                ViewportGrid.Cursor = WpfCursors.SizeAll;
                ViewportGrid.CaptureMouse();
                e.Handled = true;
            }
        }

        private void ViewportGrid_PreviewMouseMove(object sender, WpfMouseEventArgs e)
        {
            if (_isPanning)
            {
                bool isAnyButtonDown = e.MiddleButton == MouseButtonState.Pressed 
                                    || e.RightButton == MouseButtonState.Pressed 
                                    || e.LeftButton == MouseButtonState.Pressed;

                if (!isAnyButtonDown)
                {
                    _isPanning = false;
                    ViewportGrid.ReleaseMouseCapture();
                    ViewportGrid.Cursor = WpfCursors.Arrow;
                    return;
                }

                var curPos = e.GetPosition(this);
                double deltaX = curPos.X - _lastPanPoint.X;
                double deltaY = curPos.Y - _lastPanPoint.Y;

                ImageTranslateTransform.X += deltaX;
                ImageTranslateTransform.Y += deltaY;

                _lastPanPoint = curPos;
                e.Handled = true;
            }
        }

        private void ViewportGrid_PreviewMouseUp(object sender, WpfMouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                if (e.MiddleButton == MouseButtonState.Released && 
                    e.RightButton == MouseButtonState.Released && 
                    e.LeftButton == MouseButtonState.Released)
                {
                    _isPanning = false;
                    ViewportGrid.ReleaseMouseCapture();
                    ViewportGrid.Cursor = WpfCursors.Arrow;
                    e.Handled = true;
                }
            }
        }

        private void ZoomReset_Click(object sender, RoutedEventArgs e)
        {
            // Toggle between Fit to Viewport and 100% (1:1)
            if (Math.Abs(_currentZoom - _fitScale) < 0.05 && Math.Abs(_fitScale - 1.0) > 0.05)
            {
                _currentZoom = 1.0;
                _isUserZoomed = true;
                ApplyZoomAnimation(1.0);
            }
            else
            {
                FitImageToViewport();
            }
            UpdateZoomButtonText();
        }

        private void ResetZoom()
        {
            FitImageToViewport();
        }

        private void UpdateZoomButtonText()
        {
            if (Math.Abs(_currentZoom - _fitScale) < 0.03 && Math.Abs(_fitScale - 1.0) > 0.05)
            {
                ZoomResetBtn.Content = $"Вписать ({(int)Math.Round(_currentZoom * 100)}%)";
            }
            else
            {
                ZoomResetBtn.Content = $"{(int)Math.Round(_currentZoom * 100)}%";
            }
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
                        AnimateCheckmark(CopyAllIcon, CopyAllScale, PackIconKind.ContentCopy);
                    }
                    catch { }
                }
            }
        }

        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        private RenderTargetBitmap? GenerateTranslatedBitmap()
        {
            if (_currentBitmap == null) return null;

            int origW = _currentBitmap.Width;
            int origH = _currentBitmap.Height;

            if (origW < 1 || origH < 1) return null;

            try
            {
                IntPtr hBitmap = _currentBitmap.GetHbitmap();
                BitmapSource bmpSource;
                try
                {
                    bmpSource = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                        hBitmap,
                        IntPtr.Zero,
                        Int32Rect.Empty,
                        BitmapSizeOptions.FromEmptyOptions());
                }
                finally
                {
                    DeleteObject(hBitmap);
                }

                var drawingVisual = new DrawingVisual();
                using (var dc = drawingVisual.RenderOpen())
                {
                    // 1. Draw base image
                    dc.DrawImage(bmpSource, new Rect(0, 0, origW, origH));

                    // 2. Draw translated overlay blocks
                    if (_visualResult?.Blocks != null && _isTranslatedMode)
                    {
                        var typeface = new Typeface(new System.Windows.Media.FontFamily("Segoe UI Variable Text, Segoe UI, sans-serif"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
                        double pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

                        foreach (var block in _visualResult.Blocks)
                        {
                            if (string.IsNullOrWhiteSpace(block.TranslatedText)) continue;

                            double origX = Math.Max(0.0, Math.Min(origW - 10.0, block.X));
                            double origY = Math.Max(0.0, Math.Min(origH - 10.0, block.Y));
                            double blockW = Math.Max(10.0, Math.Min(origW - origX, block.Width));
                            double blockH = Math.Max(10.0, Math.Min(origH - origY, block.Height));

                            var bgBrush = CreateReconstructedBackgroundBrush(
                                _currentBitmap,
                                (int)origX, (int)origY, (int)blockW, (int)blockH,
                                block.BackgroundColor,
                                out WpfColor smartTextColor);

                            // Draw reconstructed background patch
                            dc.DrawRectangle(bgBrush, null, new Rect(origX, origY, blockW, blockH));

                            // Dynamic font calculation matching on-screen appearance
                            double targetFontSize = Math.Max(9.5, Math.Min(24.0, blockH * 0.76));
                            if (block.TranslatedText.Length > block.OriginalText.Length)
                            {
                                double exp = (double)block.TranslatedText.Length / Math.Max(1, block.OriginalText.Length);
                                targetFontSize = Math.Max(9.0, targetFontSize / Math.Sqrt(exp));
                            }

                            var formattedText = new FormattedText(
                                block.TranslatedText,
                                System.Globalization.CultureInfo.CurrentCulture,
                                System.Windows.FlowDirection.LeftToRight,
                                typeface,
                                targetFontSize,
                                new SolidColorBrush(smartTextColor),
                                pixelsPerDip);

                            // Fit within block bounds
                            if (formattedText.Width > blockW)
                            {
                                double fontScale = blockW / Math.Max(1.0, formattedText.Width);
                                targetFontSize = Math.Max(7.0, targetFontSize * fontScale);
                                formattedText = new FormattedText(
                                    block.TranslatedText,
                                    System.Globalization.CultureInfo.CurrentCulture,
                                    System.Windows.FlowDirection.LeftToRight,
                                    typeface,
                                    targetFontSize,
                                    new SolidColorBrush(smartTextColor),
                                    pixelsPerDip);
                            }

                            double textY = origY + Math.Max(0, (blockH - formattedText.Height) / 2.0);
                            dc.DrawText(formattedText, new WpfPoint(origX + 1.0, textY));
                        }
                    }
                }

                var rtb = new RenderTargetBitmap(origW, origH, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(drawingVisual);
                rtb.Freeze();
                return rtb;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error generating translated bitmap: {ex.Message}");
                return null;
            }
        }

        private void CopyImage_Click(object sender, RoutedEventArgs e)
        {
            var rtb = GenerateTranslatedBitmap();
            if (rtb == null) return;

            try
            {
                WpfClipboard.SetImage(rtb);
                AnimateCheckmark(CopyImageIcon, CopyImageScale, PackIconKind.ImageOutline);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to copy image: {ex.Message}");
            }
        }

        private void SaveImage_Click(object sender, RoutedEventArgs e)
        {
            var rtb = GenerateTranslatedBitmap();
            if (rtb == null) return;

            try
            {
                var sfd = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Сохранить изображение с переводом",
                    FileName = $"Lingo_Translated_{DateTime.Now:yyyyMMdd_HHmmss}.png",
                    Filter = "PNG Image (*.png)|*.png|JPEG Image (*.jpg)|*.jpg|Bitmap Image (*.bmp)|*.bmp"
                };

                if (sfd.ShowDialog(this) == true)
                {
                    string ext = Path.GetExtension(sfd.FileName).ToLowerInvariant();
                    BitmapEncoder encoder = ext switch
                    {
                        ".jpg" or ".jpeg" => new JpegBitmapEncoder { QualityLevel = 95 },
                        ".bmp" => new BmpBitmapEncoder(),
                        _ => new PngBitmapEncoder()
                    };

                    encoder.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = new FileStream(sfd.FileName, FileMode.Create, FileAccess.Write);
                    encoder.Save(fs);

                    AnimateCheckmark(SaveImageIcon, SaveImageScale, PackIconKind.ContentSaveOutline);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save image: {ex.Message}");
            }
        }

        private void AnimateCheckmark(PackIcon icon, ScaleTransform scale, PackIconKind originalKind = PackIconKind.ContentCopy)
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
                icon.Kind = originalKind;
                icon.Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary");
            };
            timer.Start();
        }

        private async void NewSnip_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _tts.Stop();
                _cts?.Cancel();

                // Hide current window so it is not visible during snipping
                Hide();
                await Task.Delay(160);

                var snipWindow = new SnippingWindow();
                bool? result = snipWindow.ShowDialog();

                if (result == true && snipWindow.CapturedBitmap != null)
                {
                    using var captured = snipWindow.CapturedBitmap;

                    // Open a brand new window from scratch with completely fresh layout & state
                    var newWin = new ImageTranslateWindow(captured)
                    {
                        Owner = Owner
                    };
                    newWin.Show();

                    // Destroy the old window completely
                    Close();
                }
                else
                {
                    // User cancelled snip, restore this window
                    Show();
                    Activate();
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
