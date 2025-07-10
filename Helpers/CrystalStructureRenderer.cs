using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml;
using System;
using Windows.Foundation;
using Windows.UI;
using System.Runtime.InteropServices.WindowsRuntime;

namespace Atomic_WinUI.Helpers
{
    public static class CrystalStructureRenderer
    {
        //Handle rotations in 3DView
        public static (double x, double y, double z) Rotate(double x, double y, double z, double yaw, double pitch, double roll)
        {
            double cosr = Math.Cos(roll), sinr = Math.Sin(roll);
            double x0 = x * cosr - y * sinr;
            double y0 = x * sinr + y * cosr;
            double z0 = z;

            double cosy = Math.Cos(yaw), siny = Math.Sin(yaw);
            double x1 = x0 * cosy - z0 * siny;
            double z1 = x0 * siny + z0 * cosy;
            double y1 = y0;

            double cosp = Math.Cos(pitch), sinp = Math.Sin(pitch);
            double y2 = y1 * cosp - z1 * sinp;
            double z2 = y1 * sinp + z1 * cosp;
            return (x1, y2, z2);
        }

        //Functions to draw lines between matrix points from CrystalStructures
        public static void DrawLine(WriteableBitmap bmp, Point p1, Point p2, Color color)
        {
            int x0 = (int)p1.X, y0 = (int)p1.Y, x1 = (int)p2.X, y1 = (int)p2.Y;
            int dx = Math.Abs(x1 - x0), dy = Math.Abs(y1 - y0);
            int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, err = dx - dy;
            int w = bmp.PixelWidth, h = bmp.PixelHeight;
            using (var buf = bmp.PixelBuffer.AsStream())
            {
                while (true)
                {
                    if (x0 >= 0 && x0 < w && y0 >= 0 && y0 < h)
                    {
                        long idx = (y0 * w + x0) * 4;
                        buf.Position = idx;
                        buf.WriteByte(color.B);
                        buf.WriteByte(color.G);
                        buf.WriteByte(color.R);
                        buf.WriteByte(255);
                    }
                    if (x0 == x1 && y0 == y1) break;
                    int e2 = 2 * err;
                    if (e2 > -dy) { err -= dy; x0 += sx; }
                    if (e2 < dx) { err += dx; y0 += sy; }
                }
            }
        }
    }
}
