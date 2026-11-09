using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DesktopBounce
{
    internal sealed class SingleInstance : IDisposable
    {
        private readonly Mutex ownership;
        private readonly EventWaitHandle reopen;
        private WindowsFormsSynchronizationContext dispatcher;
        private RegisteredWaitHandle listener;
        private volatile bool disposed;

        internal SingleInstance()
        {
            string name = GetInstanceName();
            reopen = new EventWaitHandle(false, EventResetMode.AutoReset, name + ".Reopen");
            try
            {
                ownership = new Mutex(false, name + ".Owner");
                try
                {
                    IsPrimary = ownership.WaitOne(0);
                }
                catch (AbandonedMutexException)
                {
                    IsPrimary = true;
                }

                if (!IsPrimary)
                    reopen.Set();
            }
            catch
            {
                Dispose();
                throw;
            }
        }

        internal bool IsPrimary { get; private set; }

        internal void Listen(Action bringIntoView)
        {
            dispatcher = new WindowsFormsSynchronizationContext();
            listener = ThreadPool.RegisterWaitForSingleObject(reopen, (state, timedOut) =>
            {
                if (disposed)
                    return;

                try
                {
                    dispatcher.Post(ignored =>
                    {
                        if (!disposed)
                            bringIntoView();
                    }, null);
                }
                catch (InvalidOperationException)
                {
                    // Windows Forms can tear down its dispatch window as the app exits.
                }
            }, null, Timeout.Infinite, false);
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            if (listener != null)
                listener.Unregister(null);
            if (dispatcher != null)
                dispatcher.Dispose();
            if (IsPrimary)
                ownership.ReleaseMutex();
            if (ownership != null)
                ownership.Dispose();
            reopen.Dispose();
        }

        private static string GetInstanceName()
        {
            // Alternate desktops in the same Windows session each need their own visible ball.
            string user;
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                user = identity.User.Value;

            string station = GetObjectName(GetProcessWindowStation());
            string desktop = GetObjectName(GetThreadDesktop(GetCurrentThreadId()));
            return @"Local\DeskBounce.2." + user + "." + station + "." + desktop;
        }

        private static string GetObjectName(IntPtr handle)
        {
            const int name = 2;
            int bytes;
            GetUserObjectInformation(handle, name, null, 0, out bytes);
            if (bytes == 0)
                return string.Empty;
            var value = new StringBuilder(bytes / sizeof(char));
            return GetUserObjectInformation(handle, name, value, bytes, out bytes)
                ? value.ToString()
                : string.Empty;
        }

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint threadId);

        [DllImport("user32.dll")]
        private static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(IntPtr handle, int index, StringBuilder value,
            int length, out int needed);
    }
}
