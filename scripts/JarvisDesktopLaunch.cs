using System;
using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace JarvisSasariSa
{
    public static class JarvisDesktopLaunch
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct STARTUPINFO
        {
            public int cb;
            public string lpReserved;
            public string lpDesktop;
            public string lpTitle;
            public int dwX;
            public int dwY;
            public int dwXSize;
            public int dwYSize;
            public int dwXCountChars;
            public int dwYCountChars;
            public int dwFillAttribute;
            public int dwFlags;
            public short wShowWindow;
            public short cbReserved2;
            public IntPtr lpReserved2;
            public IntPtr hStdInput;
            public IntPtr hStdOutput;
            public IntPtr hStdError;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct PROCESS_INFORMATION
        {
            public IntPtr hProcess;
            public IntPtr hThread;
            public int dwProcessId;
            public int dwThreadId;
        }

        private const int STARTF_USESHOWWINDOW = 0x00000001;
        private const short SW_HIDE = 0;
        private const short SW_SHOWNORMAL = 1;
        private const uint CREATE_NEW_CONSOLE = 0x00000010;

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool CreateProcess(
            string lpApplicationName,
            string lpCommandLine,
            IntPtr lpProcessAttributes,
            IntPtr lpThreadAttributes,
            bool bInheritHandles,
            uint dwCreationFlags,
            IntPtr lpEnvironment,
            string lpCurrentDirectory,
            ref STARTUPINFO lpStartupInfo,
            out PROCESS_INFORMATION lpProcessInformation);

        public static int Main(string[] args)
        {
            if (args == null || args.Length == 0)
            {
                Console.Error.WriteLine("Usage: JarvisDesktopLaunch.exe [--hidden] <targetOrCmdLine>");
                return 1;
            }

            bool hidden = false;
            int startIdx = 0;
            if (args[0] == "--hidden")
            {
                hidden = true;
                startIdx = 1;
            }
            if (startIdx >= args.Length) return 1;

            string target = args[startIdx];
            string extraArgs = "";
            if (args.Length > startIdx + 1)
            {
                string[] rest = new string[args.Length - (startIdx + 1)];
                Array.Copy(args, startIdx + 1, rest, 0, rest.Length);
                for (int i = 0; i < rest.Length; i++)
                {
                    if (rest[i].Contains(" ") && !rest[i].StartsWith("\""))
                        rest[i] = "\"" + rest[i] + "\"";
                }
                extraArgs = string.Join(" ", rest);
            }

            string cmdLine;
            if (target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                target.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                string browser = FindBrowser();
                cmdLine = "\"" + browser + "\" --new-window --autoplay-policy=no-user-gesture-required \"" + target + "\"";
            }
            else if (target.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) ||
                     target.EndsWith(":", StringComparison.OrdinalIgnoreCase) ||
                     Directory.Exists(target))
            {
                cmdLine = "cmd.exe /c start \"\" \"" + target + "\"";
            }
            else
            {
                string resolved = ResolveExecutable(target);
                cmdLine = "\"" + resolved + "\"" + (string.IsNullOrEmpty(extraArgs) ? "" : " " + extraArgs);
            }

            STARTUPINFO si = new STARTUPINFO();
            si.cb = Marshal.SizeOf(si);
            si.lpDesktop = "Winsta0\\Default";
            si.dwFlags = STARTF_USESHOWWINDOW;
            si.wShowWindow = hidden ? SW_HIDE : SW_SHOWNORMAL;

            PROCESS_INFORMATION pi;
            bool ok = CreateProcess(
                null,
                cmdLine,
                IntPtr.Zero,
                IntPtr.Zero,
                false,
                CREATE_NEW_CONSOLE,
                IntPtr.Zero,
                "C:\\자비스",
                ref si,
                out pi);

            if (!ok)
            {
                int err = Marshal.GetLastWin32Error();
                Console.Out.WriteLine("{\"ok\":false,\"error\":\"CreateProcess failed (" + err + ")\"}");
                return 2;
            }

            Console.Out.WriteLine("{\"ok\":true,\"processId\":" + pi.dwProcessId + ",\"desktop\":\"Winsta0\\\\Default\"}");
            return 0;
        }

        private static string FindBrowser()
        {
            string[] candidates = new string[]
            {
                "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe",
                "C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe",
                "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe",
                "C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe"
            };
            foreach (string c in candidates)
            {
                if (File.Exists(c)) return c;
            }
            return "explorer.exe";
        }

        private static string ResolveExecutable(string name)
        {
            if (File.Exists(name)) return name;
            string n = name.ToLowerInvariant();
            if (n == "chrome" || n == "chrome.exe") return FindBrowser();
            if (n == "msedge" || n == "msedge.exe")
            {
                if (File.Exists("C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe"))
                    return "C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe";
            }
            string sys32 = Environment.GetFolderPath(Environment.SpecialFolder.System);
            string candidate = Path.Combine(sys32, name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
            if (File.Exists(candidate)) return candidate;
            string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string candidate2 = Path.Combine(winDir, name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe");
            if (File.Exists(candidate2)) return candidate2;
            return name;
        }
    }
}
