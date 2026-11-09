using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Text;

namespace DesktopBounce
{
    internal static class WindowSurfaces
    {
        private const uint NextWindow = 2;
        private const int ExtendedStyle = -20;
        private const int ToolWindow = 0x00000080;
        private const int Transparent = 0x00000020;
        private const int Layered = 0x00080000;
        private const int NoActivate = 0x08000000;
        private const int ExtendedFrameBounds = 9;
        private const int Cloaked = 14;
        private static readonly uint OwnProcessId = GetCurrentProcessId();
        private static readonly int RectSize = Marshal.SizeOf(typeof(NativeRect));

        internal static List<Rectangle> Capture(Rectangle workArea)
        {
            var surfaces = new List<Rectangle>();
            var occluders = new List<Rectangle>();
            var visited = new HashSet<IntPtr>();
            var className = new StringBuilder(256);
            IntPtr shell = GetShellWindow();

            // Follow the Z order so only windows drawn above this one can hide its edge.
            for (IntPtr window = GetTopWindow(IntPtr.Zero);
                window != IntPtr.Zero && visited.Add(window);
                window = GetWindow(window, NextWindow))
            {
                if (window == shell || !IsWindowVisible(window) || IsIconic(window))
                    continue;

                uint processId;
                if (GetWindowThreadProcessId(window, out processId) == 0 || processId == OwnProcessId)
                    continue;

                int cloakFlags;
                if (DwmGetWindowInt(window, Cloaked, out cloakFlags, sizeof(int)) == 0 && cloakFlags != 0)
                    continue;

                NativeRect nativeFrame;
                if (DwmGetWindowRect(window, ExtendedFrameBounds, out nativeFrame, RectSize) != 0 &&
                    !GetWindowRect(window, out nativeFrame))
                    continue;

                var frame = Rectangle.FromLTRB(nativeFrame.Left, nativeFrame.Top, nativeFrame.Right, nativeFrame.Bottom);
                Rectangle visible = Rectangle.Intersect(frame, workArea);
                if (visible.Width <= 0 || visible.Height <= 0)
                    continue;

                className.Clear();
                GetClassName(window, className, className.Capacity);
                if (IsShellSurface(className.ToString()))
                    continue;

                int style = GetWindowLong(window, ExtendedStyle);
                if ((style & (Layered | Transparent)) == (Layered | Transparent))
                    continue;

                if (frame.Top == visible.Top && (style & (ToolWindow | NoActivate)) == 0)
                    AddExposedTop(visible, occluders, surfaces);

                occluders.Add(visible);
            }

            return surfaces;
        }

        private static void AddExposedTop(Rectangle frame, List<Rectangle> occluders, List<Rectangle> surfaces)
        {
            int first = surfaces.Count;
            surfaces.Add(new Rectangle(frame.Left, frame.Top, frame.Width, 1));

            foreach (Rectangle occluder in occluders)
            {
                if (frame.Top < occluder.Top || frame.Top >= occluder.Bottom)
                    continue;

                for (int index = surfaces.Count - 1; index >= first; index--)
                {
                    Rectangle edge = surfaces[index];
                    if (occluder.Right <= edge.Left || occluder.Left >= edge.Right)
                        continue;

                    surfaces.RemoveAt(index);
                    if (edge.Left < occluder.Left)
                        surfaces.Insert(index, Rectangle.FromLTRB(edge.Left, edge.Top, occluder.Left, edge.Bottom));
                    if (edge.Right > occluder.Right)
                        surfaces.Add(Rectangle.FromLTRB(occluder.Right, edge.Top, edge.Right, edge.Bottom));
                }

                if (surfaces.Count == first)
                    break;
            }
        }

        private static bool IsShellSurface(string className)
        {
            switch (className)
            {
                case "Progman":
                case "WorkerW":
                case "Shell_TrayWnd":
                case "Shell_SecondaryTrayWnd":
                case "NotifyIconOverflowWindow":
                    return true;
                default:
                    return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRect
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentProcessId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetShellWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetTopWindow(IntPtr window);

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr window, uint command);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr window);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr window);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr window, StringBuilder className, int capacity);

        [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
        private static extern int GetWindowLong(IntPtr window, int index);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowRect(IntPtr window, int attribute, out NativeRect rect, int size);

        [DllImport("dwmapi.dll", EntryPoint = "DwmGetWindowAttribute")]
        private static extern int DwmGetWindowInt(IntPtr window, int attribute, out int value, int size);
    }
}
