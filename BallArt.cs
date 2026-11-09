using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace DesktopBounce
{
    internal static class BallArt
    {
        internal static Bitmap Create(int diameter)
        {
            var bitmap = new Bitmap(diameter, diameter, PixelFormat.Format32bppPArgb);
            var pixels = new int[diameter * diameter];
            for (int row = 0; row < diameter; row++)
            {
                for (int column = 0; column < diameter; column++)
                {
                    pixels[row * diameter + column] = Pixel(column, row, diameter);
                }
            }

            var data = bitmap.LockBits(new Rectangle(0, 0, diameter, diameter), ImageLockMode.WriteOnly,
                PixelFormat.Format32bppPArgb);
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        private static int Pixel(int column, int row, int diameter)
        {
            const int samples = 4;
            int red = 0, green = 0, blue = 0, covered = 0;
            for (int sy = 0; sy < samples; sy++)
            {
                for (int sx = 0; sx < samples; sx++)
                {
                    double x = 2 * (column + (sx + 0.5) / samples) / diameter - 1;
                    double y = 2 * (row + (sy + 0.5) / samples) / diameter - 1;
                    double depth = 1 - x * x - y * y;
                    if (depth <= 0)
                        continue;

                    double z = Math.Sqrt(depth);
                    double light = Math.Max(0, -0.42 * x - 0.58 * y + 0.69 * z);
                    double glint = Math.Pow(Math.Max(0, -0.27 * x - 0.35 * y + 0.897 * z), 33);
                    double rim = Math.Pow(1 - z, 2) * Math.Max(0, 0.38 * x + 0.60 * y + 0.23);
                    double seam = Math.Exp(-Math.Pow((0.68 * x + 0.57 * y + 0.46 * z + 0.23) * 58, 2));
                    double seamEdge = Math.Exp(-Math.Pow((0.68 * x + 0.57 * y + 0.46 * z + 0.26) * 49, 2));
                    red += Channel(12 + 103 * light + 150 * glint + 25 * rim - 18 * seam + 21 * seamEdge);
                    green += Channel(78 + 157 * light + 34 * glint + 77 * rim - 44 * seam + 22 * seamEdge);
                    blue += Channel(58 + 81 * light + 103 * glint + 75 * rim - 25 * seam + 20 * seamEdge);
                    covered++;
                }
            }

            const int area = samples * samples;
            return ((255 * covered / area) << 24) | ((red / area) << 16) | ((green / area) << 8) | (blue / area);
        }

        private static int Channel(double value)
        {
            return (int)Math.Max(0, Math.Min(255, value));
        }
    }
}
