using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace Lingo.Services
{
    public static class AppIconHelper
    {
        public static Icon CreateAppIcon()
        {
            try
            {
                using var bitmap = new Bitmap(32, 32);
                using (var g = Graphics.FromImage(bitmap))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);

                    int r = 8;
                    using var path = new GraphicsPath();
                    path.AddArc(0, 0, r, r, 180, 90);
                    path.AddArc(31 - r, 0, r, r, 270, 90);
                    path.AddArc(31 - r, 31 - r, r, r, 0, 90);
                    path.AddArc(0, 31 - r, r, r, 90, 90);
                    path.CloseFigure();

                    using var bgBrush = new SolidBrush(Color.FromArgb(255, 26, 26, 26));
                    g.FillPath(bgBrush, path);

                    using var borderPen = new Pen(Color.FromArgb(255, 65, 65, 65), 1.2f);
                    g.DrawPath(borderPen, path);

                    using var pen = new Pen(Color.FromArgb(255, 235, 235, 235), 3.2f)
                    {
                        StartCap = LineCap.Round,
                        EndCap = LineCap.Round,
                        LineJoin = LineJoin.Round
                    };

                    g.DrawLine(pen, 9, 7, 9, 23);
                    g.DrawLine(pen, 9, 23, 23, 23);
                    g.DrawLine(pen, 18, 18, 23, 23);
                }

                IntPtr hIcon = bitmap.GetHicon();
                return Icon.FromHandle(hIcon);
            }
            catch
            {
                return SystemIcons.Application;
            }
        }
    }
}
