using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Timer = System.Windows.Forms.Timer;

[assembly: AssemblyTitle("Desk Bounce")]
[assembly: AssemblyProduct("Desk Bounce")]
[assembly: AssemblyDescription("A bouncy ball for your Windows desktop")]
[assembly: AssemblyVersion("1.2.0.0")]

namespace DesktopBounce
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);

            try
            {
                using (var instance = new SingleInstance())
                {
                    if (!instance.IsPrimary)
                        return;

                    using (var app = new BounceApp())
                    {
                        instance.Listen(app.BringIntoView);
                        Application.Run(app);
                    }
                }
            }
            catch (Exception error)
            {
                MessageBox.Show(error.Message, "Desk Bounce", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }

    internal sealed class BounceApp : ApplicationContext
    {
        private const float MinimumSoundImpact = 95f;
        private static readonly IList<Rectangle> NoSurfaces = new Rectangle[0];
        private readonly Timer timer = new Timer { Interval = 12 };
        private readonly NotifyIcon tray = new NotifyIcon();
        private readonly ContextMenu menu = new ContextMenu();
        private readonly MenuItem pauseItem = new MenuItem("&Pause");
        private readonly MenuItem windowsItem = new MenuItem("Bounce off &windows") { Checked = true };
        private readonly MenuItem soundItem = new MenuItem("&Sound") { Checked = true };
        private readonly Icon icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private readonly BallPhysics ball = new BallPhysics(29);
        private readonly BounceAudio audio = new BounceAudio();
        private readonly BallOverlay overlay;
        private Rectangle bounds;
        private IList<Rectangle> surfaces = NoSurfaces;
        private double lastFrame;
        private double nextScan;
        private double revealUntil;
        private float squash;
        private float impactAngle;
        private bool disposed;

        internal BounceApp()
        {
            try
            {
                overlay = new BallOverlay(2 * (int)ball.Radius);
                var title = new MenuItem("Desk Bounce") { Enabled = false };
                var resetItem = new MenuItem("&Reset ball", (sender, args) => BringIntoView());
                var exitItem = new MenuItem("E&xit", (sender, args) => ExitThread());
                menu.MenuItems.AddRange(new[] { title, new MenuItem("-"), pauseItem, resetItem,
                    windowsItem, soundItem, new MenuItem("-"), exitItem });
                pauseItem.Click += (sender, args) => TogglePause();
                windowsItem.Click += (sender, args) =>
                {
                    windowsItem.Checked = !windowsItem.Checked;
                    RefreshSurfaces();
                };
                soundItem.Click += (sender, args) =>
                {
                    soundItem.Checked = !soundItem.Checked;
                    if (!soundItem.Checked)
                        audio.Stop();
                };
                tray.Icon = icon ?? SystemIcons.Application;
                tray.Text = "Desk Bounce";
                tray.ContextMenu = menu;
                tray.DoubleClick += (sender, args) => BringIntoView();
                tray.Visible = true;

                BringIntoView();
                timer.Tick += Animate;
                timer.Start();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal void BringIntoView()
        {
            audio.Stop();
            Point cursor = Cursor.Position;
            bounds = Screen.FromPoint(cursor).WorkingArea;
            ball.Reset(bounds, cursor);
            RefreshSurfaces();
            squash = 0;
            lastFrame = clock.Elapsed.TotalSeconds;
            revealUntil = lastFrame + 0.45;
            overlay.Draw(ball.Position, 0, 0);
            overlay.Reveal();
        }

        private void TogglePause()
        {
            if (timer.Enabled)
            {
                timer.Stop();
                audio.Stop();
                pauseItem.Text = "&Resume";
                return;
            }

            ball.ForgetCursor();
            lastFrame = clock.Elapsed.TotalSeconds;
            nextScan = lastFrame;
            pauseItem.Text = "&Pause";
            timer.Start();
        }

        private void RefreshSurfaces()
        {
            surfaces = windowsItem.Checked ? WindowSurfaces.Capture(bounds) : NoSurfaces;
            nextScan = clock.Elapsed.TotalSeconds + 0.08;
        }

        private void Animate(object sender, EventArgs args)
        {
            double now = clock.Elapsed.TotalSeconds;
            float elapsed = (float)(now - lastFrame);
            lastFrame = now;
            if (now < revealUntil)
                return;

            if (now >= nextScan)
            {
                bounds = Screen.FromPoint(Point.Round(ball.Position)).WorkingArea;
                RefreshSurfaces();
            }

            ball.Step(elapsed, bounds, surfaces, Cursor.Position);
            PointF impact = ball.Impact;
            float strength = (float)Math.Sqrt(impact.X * impact.X + impact.Y * impact.Y);
            if (soundItem.Checked && strength >= MinimumSoundImpact)
                audio.Play(strength);
            squash = Math.Max(0, squash - elapsed * 1.55f);
            if (strength > 160)
            {
                squash = Math.Min(0.24f, strength / 5500f);
                impactAngle = (float)(Math.Atan2(impact.Y, impact.X) * 180 / Math.PI);
            }
            overlay.Draw(ball.Position, squash, impactAngle);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && !disposed)
            {
                disposed = true;
                timer.Dispose();
                audio.Dispose();
                tray.Dispose();
                menu.Dispose();
                if (icon != null)
                    icon.Dispose();
                if (overlay != null)
                    overlay.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
