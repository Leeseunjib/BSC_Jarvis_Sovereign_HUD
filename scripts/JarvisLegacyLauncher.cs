using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

// Compatibility entry point for old desktop links. Never load an old HUD,
// enumerate/terminate processes, compile sources, or replace installed files.
internal static class JarvisLegacyLauncher
{
    [STAThread]
    private static int Main()
    {
        try
        {
            string project = AppDomain.CurrentDomain.BaseDirectory;
            string installed = Path.Combine(project, "bin", "JarvisHud.exe");
            if (!File.Exists(installed)) throw new FileNotFoundException("The installed Jarvis HUD is missing.", installed);
            using (Process child = Process.Start(new ProcessStartInfo {
                FileName = installed, WorkingDirectory = project,
                UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
            })) { }
            return 0;
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "Jarvis launcher", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
    }
}
