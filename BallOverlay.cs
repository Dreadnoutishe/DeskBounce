using System;
using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DesktopBounce
{
    internal sealed class BallOverlay : NativeWindow, IDisposable
    {
        private const int WsPopup = unchecked((int)0x80000000);
        private const int WsExTopmost = 0x00000008;
        private const int WsExTransparent = 0x00000020;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExLayered = 0x00080000;
        private const int WsExNoActivate = 0x08000000;

        private readonly int diameter;
        private readonly int size;
        private Bitmap sprite;
        private Bitmap canvas;
        private Graphics drawing;
        private IntPtr dc;
        private IntPtr dib;
        private IntPtr oldBitmap;
        private Point previousLocation;
        private float previousSquash = -1;
        private float previousAngle;
        private bool shown;
        private bool disposed;

        internal BallOverlay(int diameter)
        {
            this.diameter = diameter;
            size = (int)Math.Ceiling(diameter * 1.4) + 2;
            try
            {
                sprite = BallArt.Create(diameter);
                dc = CreateCompatibleDC(IntPtr.Zero);
                if (dc == IntPtr.Zero)
                    throw new Win32Exception();

                var header = new DibHeader
                {
                    HeaderSize = Marshal.SizeOf(typeof(DibHeader)),
                    Width = size,
                    Height = -size,
                    Planes = 1,
                    BitCount = 32
                };
                IntPtr bits;
                dib = CreateDIBSection(dc, ref header, 0, out bits, IntPtr.Zero, 0);
                if (dib == IntPtr.Zero)
                    throw new Win32Exception();

                oldBitmap = SelectObject(dc, dib);
                if (oldBitmap == IntPtr.Zero)
                    throw new Win32Exception();

                canvas = new Bitmap(size, size, size * 4, PixelFormat.Format32bppPArgb, bits);
                drawing = Graphics.FromImage(canvas);
                drawing.CompositingMode = CompositingMode.SourceCopy;
                drawing.InterpolationMode = InterpolationMode.HighQualityBicubic;
                drawing.PixelOffsetMode = PixelOffsetMode.HighQuality;
                CreateHandle(new CreateParams
                {
                    Caption = "Desk Bounce",
                    Style = WsPopup,
                    ExStyle = WsExTopmost | WsExTransparent | WsExToolWindow | WsExLayered | WsExNoActivate,
                    Width = size,
                    Height = size
                });
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void Draw(PointF center, float squash, float angle)
        {
            var location = new Point((int)Math.Round(center.X - size / 2f), (int)Math.Round(center.Y - size / 2f));
            bool shapeChanged = squash != previousSquash || (squash > 0 && angle != previousAngle);
            if (shown && !shapeChanged && location == previousLocation)
                return;

            if (shapeChanged)
            {
                drawing.Clear(Color.Transparent);
                if (squash == 0)
                {
                    drawing.DrawImageUnscaled(sprite, (size - diameter) / 2, (size - diameter) / 2);
                }
                else
                {
                    drawing.ResetTransform();
                    drawing.TranslateTransform(size / 2f, size / 2f);
                    drawing.RotateTransform(angle);
                    drawing.ScaleTransform(1 - squash, 1 + squash * 0.55f);
                    drawing.RotateTransform(-angle);
                    drawing.DrawImage(sprite, new Rectangle(-diameter / 2, -diameter / 2, diameter, diameter),
                        0, 0, diameter, diameter, GraphicsUnit.Pixel);
                    drawing.ResetTransform();
                }
                drawing.Flush(FlushIntention.Sync);
            }

            var extent = new Size(size, size);
            var source = Point.Empty;
            var blend = new BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            if (!UpdateLayeredWindow(Handle, IntPtr.Zero, ref location, ref extent, dc, ref source, 0, ref blend, 2))
                throw new Win32Exception();

            if (!shown)
                Reveal();
            previousLocation = location;
            previousSquash = squash;
            previousAngle = angle;
        }

        internal void Reveal()
        {
            const int noSize = 0x0001;
            const int noMove = 0x0002;
            const int noActivate = 0x0010;
            const int showWindow = 0x0040;
            var topmost = new IntPtr(-1);
            if (!SetWindowPos(Handle, topmost, 0, 0, 0, 0, noSize | noMove | noActivate | showWindow))
                throw new Win32Exception();
            shown = true;
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (Handle != IntPtr.Zero)
                DestroyHandle();
            if (drawing != null)
                drawing.Dispose();
            if (canvas != null)
                canvas.Dispose();
            if (sprite != null)
                sprite.Dispose();
            if (oldBitmap != IntPtr.Zero)
                SelectObject(dc, oldBitmap);
            if (dib != IntPtr.Zero)
                DeleteObject(dib);
            if (dc != IntPtr.Zero)
                DeleteDC(dc);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct DibHeader
        {
            internal int HeaderSize;
            internal int Width;
            internal int Height;
            internal short Planes;
            internal short BitCount;
            internal int Compression;
            internal int ImageSize;
            internal int PixelsPerMeterX;
            internal int PixelsPerMeterY;
            internal int ColorsUsed;
            internal int ColorsImportant;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct BlendFunction
        {
            internal byte BlendOp;
            internal byte BlendFlags;
            internal byte SourceConstantAlpha;
            internal byte AlphaFormat;
        }

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateCompatibleDC(IntPtr source);

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr dc, ref DibHeader header, uint usage,
            out IntPtr bits, IntPtr section, uint offset);

        [DllImport("gdi32.dll")]
        private static extern IntPtr SelectObject(IntPtr dc, IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr gdiObject);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteDC(IntPtr dc);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter, int x, int y,
            int width, int height, int flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr screen, ref Point position,
            ref Size size, IntPtr source, ref Point sourcePosition, int colorKey, ref BlendFunction blend, int flags);
    }
}
