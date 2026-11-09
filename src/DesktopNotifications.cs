using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace AutovisorPanel
{
    // Unpackaged Windows desktop notification identity. The app remains a single EXE.
    public sealed class DesktopNotifications : IDisposable
    {
        private const string AppId = "LPPPB.AutovisorPanel";
        private static readonly Guid ActivatorId = new Guid("4C172C3A-1430-435E-815B-109D5874E465");
        private readonly Factory factory;
        private uint registration;
        private object notifier;
        private Type xmlType;
        private Type toastType;
        private MethodInfo showMethod;
        private string logoUri;

        public DesktopNotifications(Action activate)
        {
            factory = new Factory(activate);
        }

        public void Start()
        {
            if (registration != 0) return;
            Application.OleRequired();
            IntPtr factoryPointer = Marshal.GetComInterfaceForObject(factory, typeof(IClassFactory));
            try
            {
                Guid id = ActivatorId;
                uint cookie;
                if (CoRegisterClassObject(ref id, factoryPointer, 4, 1, out cookie) >= 0)
                    registration = cookie;
            }
            finally { Marshal.Release(factoryPointer); }
        }

        public bool Show(Icon logo, string title, string body)
        {
            try
            {
                if (registration == 0) Start();
                if (registration == 0) return false;
                if (notifier == null) Initialize(logo);
                object document = Activator.CreateInstance(xmlType);
                xmlType.GetMethod("LoadXml", new[] { typeof(string) }).Invoke(document, new object[] { BuildXml(title, body, logoUri) });
                object toast = Activator.CreateInstance(toastType, new[] { document });
                showMethod.Invoke(notifier, new[] { toast });
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
        }

        internal static string BuildXml(string title, string body, string logoUri)
        {
            return "<toast launch=\"overview\"><visual><binding template=\"ToastGeneric\"><text>" +
                SecurityElement.Escape(title) + "</text><text>" + SecurityElement.Escape(body) +
                "</text><image placement=\"appLogoOverride\" src=\"" + SecurityElement.Escape(logoUri) +
                "\"/></binding></visual></toast>";
        }

        private void Initialize(Icon logo)
        {
            xmlType = WinRtType("Windows.Data.Xml.Dom.XmlDocument", "Windows.Data.Xml.Dom");
            toastType = WinRtType("Windows.UI.Notifications.ToastNotification", "Windows.UI.Notifications");
            Type manager = WinRtType("Windows.UI.Notifications.ToastNotificationManager", "Windows.UI.Notifications");
            Type notifierType = WinRtType("Windows.UI.Notifications.ToastNotifier", "Windows.UI.Notifications");
            showMethod = notifierType.GetMethod("Show", new[] { toastType });

            string directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutovisorPanel");
            Directory.CreateDirectory(directory);
            string image = Path.Combine(directory, "notification-icon.png");
            if (!File.Exists(image))
                using (Bitmap bitmap = logo.ToBitmap()) bitmap.Save(image, ImageFormat.Png);
            logoUri = new Uri(image).AbsoluteUri;

            string clsid = ActivatorId.ToString("B");
            using (RegistryKey server = Registry.CurrentUser.CreateSubKey(@"Software\Classes\CLSID\" + clsid + @"\LocalServer32"))
                server.SetValue(null, "\"" + Application.ExecutablePath + "\" -Embedding");
            using (RegistryKey identity = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + AppId))
            {
                identity.SetValue("DisplayName", "Autovisor 面板");
                identity.SetValue("IconUri", image);
                identity.SetValue("CustomActivator", clsid);
            }

            MethodInfo create = manager.GetMethod("CreateToastNotifier", new[] { typeof(string) });
            notifier = create.Invoke(null, new object[] { AppId });
        }

        private static Type WinRtType(string name, string metadata)
        {
            return Type.GetType(name + ", " + metadata + ", ContentType=WindowsRuntime", true);
        }

        public void Dispose()
        {
            if (registration == 0) return;
            CoRevokeClassObject(registration);
            registration = 0;
        }

        [ComVisible(true)]
        [Guid("00000001-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IClassFactory
        {
            [PreserveSig] int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr result);
            [PreserveSig] int LockServer([MarshalAs(UnmanagedType.Bool)] bool keepAlive);
        }

        [ComVisible(true)]
        [Guid("53E31837-6600-4A81-9395-75CFFE746F94")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface INotificationActivationCallback
        {
            [PreserveSig] int Activate([MarshalAs(UnmanagedType.LPWStr)] string appId,
                [MarshalAs(UnmanagedType.LPWStr)] string arguments, IntPtr input, uint inputCount);
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        public sealed class Factory : IClassFactory
        {
            private readonly Action activate;
            public Factory(Action activate) { this.activate = activate; }

            public int CreateInstance(IntPtr outer, ref Guid iid, out IntPtr result)
            {
                result = IntPtr.Zero;
                if (outer != IntPtr.Zero) return unchecked((int)0x80040110);
                IntPtr callback = Marshal.GetIUnknownForObject(new Activation(activate));
                try { return Marshal.QueryInterface(callback, ref iid, out result); }
                finally { Marshal.Release(callback); }
            }

            public int LockServer(bool keepAlive) { return 0; }
        }

        [ComVisible(true)]
        [ClassInterface(ClassInterfaceType.None)]
        public sealed class Activation : INotificationActivationCallback
        {
            private readonly Action activate;
            public Activation(Action activate) { this.activate = activate; }

            public int Activate(string appId, string arguments, IntPtr input, uint inputCount)
            {
                if (appId == AppId && arguments == "overview") activate();
                return 0;
            }
        }

        [DllImport("ole32.dll")]
        private static extern int CoRegisterClassObject(ref Guid id, IntPtr factory, uint context, uint flags, out uint cookie);

        [DllImport("ole32.dll")]
        private static extern int CoRevokeClassObject(uint cookie);
    }
}
