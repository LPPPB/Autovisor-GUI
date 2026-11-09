using System;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace AutovisorPanel
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            bool created;
            Mutex mutex = new Mutex(true, "Local\\AutovisorPanel", out created);
            if (!created)
            {
                return;
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            string root = Path.GetDirectoryName(Application.ExecutablePath);

            try
            {
                Application.Run(new MainForm(root));
            }
            finally
            {
                mutex.ReleaseMutex();
            }
        }
    }
}
