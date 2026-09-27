using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WpfPoint = System.Windows.Point;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfMouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;

namespace Lingo
{
    public partial class SnippingWindow : Window
    {
        private WpfPoint _startPoint;
        private bool _isDragging;
        private Bitmap? _fullScreenCapture;
        public Bitmap? CapturedBitmap { get; private set; }

        public SnippingWindow()
        {
            InitializeComponent();

            Left = SystemParameters.VirtualScreenLeft;
            Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth;
            Height = SystemParameters.VirtualScreenHeight;

            // Capture crystal-clear 1:1 original desktop pixels BEFORE showing overlay
            CaptureDesktop();
        }

        private void CaptureDesktop()
        {
            try
            {
                int physLeft = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
                int physTop = System.Windows.Forms.SystemInformation.VirtualScreen.Top;
                int physWidth = System.Windows.Forms.SystemInformation.VirtualScreen.Width;
                int physHeight = System.Windows.Forms.SystemInformation.VirtualScreen.Height;

                if (physWidth > 0 && physHeight > 0)
                {
                    _fullScreenCapture = new Bitmap(physWidth, physHeight, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using var g = Graphics.FromImage(_fullScreenCapture);
                    g.CopyFromScreen(physLeft, physTop, 0, 0, new System.Drawing.Size(physWidth, physHeight), CopyPixelOperation.SourceCopy);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Capture desktop error: {ex.Message}");
            }
        }

        private void CancelSnip()
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                SelectionBorder.Visibility = Visibility.Collapsed;
            }
            DialogResult = false;
            Close();
        }

        private void Window_PreviewKeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelSnip();
                e.Handled = true;
            }
        }

        private void Window_PreviewMouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                CancelSnip();
                e.Handled = true;
            }
        }

        private void Window_KeyDown(object sender, WpfKeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CancelSnip();
                e.Handled = true;
            }
        }

        private void Window_MouseDown(object sender, WpfMouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                _startPoint = e.GetPosition(SelectionCanvas);
                _isDragging = true;

                Canvas.SetLeft(SelectionBorder, _startPoint.X);
                Canvas.SetTop(SelectionBorder, _startPoint.Y);
                SelectionBorder.Width = 0;
                SelectionBorder.Height = 0;
                SelectionBorder.Visibility = Visibility.Visible;
                CaptureMouse();
            }
        }

        private void Window_MouseMove(object sender, WpfMouseEventArgs e)
        {
            if (_isDragging)
            {
                var currentPoint = e.GetPosition(SelectionCanvas);

                double x = Math.Min(_startPoint.X, currentPoint.X);
                double y = Math.Min(_startPoint.Y, currentPoint.Y);
                double w = Math.Abs(currentPoint.X - _startPoint.X);
                double h = Math.Abs(currentPoint.Y - _startPoint.Y);

                Canvas.SetLeft(SelectionBorder, x);
                Canvas.SetTop(SelectionBorder, y);
                SelectionBorder.Width = w;
                SelectionBorder.Height = h;
            }
        }

        private void Window_MouseUp(object sender, WpfMouseButtonEventArgs e)
        {
            if (_isDragging)
            {
                _isDragging = false;
                ReleaseMouseCapture();

                var currentPoint = e.GetPosition(SelectionCanvas);

                if (_fullScreenCapture != null)
                {
                    try
                    {
                        int physLeft = System.Windows.Forms.SystemInformation.VirtualScreen.Left;
                        int physTop = System.Windows.Forms.SystemInformation.VirtualScreen.Top;

                        var startScreen = PointToScreen(_startPoint);
                        var endScreen = PointToScreen(currentPoint);

                        int pLeft = (int)Math.Round(Math.Min(startScreen.X, endScreen.X));
                        int pTop = (int)Math.Round(Math.Min(startScreen.Y, endScreen.Y));
                        int pRight = (int)Math.Round(Math.Max(startScreen.X, endScreen.X));
                        int pBottom = (int)Math.Round(Math.Max(startScreen.Y, endScreen.Y));

                        int cropX = Math.Max(0, pLeft - physLeft);
                        int cropY = Math.Max(0, pTop - physTop);
                        int cropW = Math.Max(0, pRight - pLeft);
                        int cropH = Math.Max(0, pBottom - pTop);

                        cropW = Math.Min(_fullScreenCapture.Width - cropX, cropW);
                        cropH = Math.Min(_fullScreenCapture.Height - cropY, cropH);

                        if (cropW > 6 && cropH > 6)
                        {
                            CapturedBitmap = _fullScreenCapture.Clone(new Rectangle(cropX, cropY, cropW, cropH), _fullScreenCapture.PixelFormat);
                            DialogResult = true;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Crop error: {ex.Message}");
                    }
                }

                _fullScreenCapture?.Dispose();
                _fullScreenCapture = null;
                Close();
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            _fullScreenCapture?.Dispose();
            _fullScreenCapture = null;
        }
    }
}
