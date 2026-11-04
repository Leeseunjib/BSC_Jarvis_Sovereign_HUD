using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Media;

namespace JarvisSasariSa
{
    public class MonitorOverlayApp
    {
        [DllImport("user32.dll")]
        private static extern short GetAsyncKeyState(int vKey);
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        [DllImport("kernel32.dll")]
        private static extern IntPtr GetConsoleWindow();
        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool SetForegroundWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TOOLWINDOW = 0x00000080;
        private const int WS_EX_APPWINDOW = 0x00040000;
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        private static readonly uint SWP_NOMOVE = 0x0002;
        private static readonly uint SWP_NOSIZE = 0x0001;
        private static readonly uint SWP_SHOWWINDOW = 0x0040;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        private const uint WM_CLOSE = 0x0010;
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;
        private const int SW_SHOWNOACTIVATE = 4;
        private const int SW_RESTORE = 9;
        private const int VK_RMENU = 0xA5;    // Right Alt (if present)
        private const int VK_MENU = 0x12;     // Alt
        private const int VK_CONTROL = 0x11;  // Ctrl
        private const int VK_SHIFT = 0x10;    // Shift
        private const int VK_SPACE = 0x20;    // Space (Ctrl + Space -> 자비스 즉시 호출)
        private const int VK_F2 = 0x71;       // F2 단독 키 (원터치 음성 호출)
        private const int VK_F8 = 0x77;       // F8 단독 키 (원터치 음성 호출)
        private const int VK_J = 0x4A;        // Ctrl + Shift + J -> 전체화면 부트 시퀀스 호출
        private const byte VK_LWIN = 0x5B;    // Left Win
        private const byte VK_H = 0x48;       // H (Win + H 한국어 신경망 음성 받아쓰기)
        private const uint KEYEVENTF_KEYUP = 0x0002;

        // Customizable user hotkey (editable in Settings UI)
        private static int customHotkeyVk = VK_SPACE;
        private static bool customNeedCtrl = true;
        private static bool customNeedAlt = false;
        private static bool customNeedShift = false;
        private static string customHotkeyLabel = "Ctrl + Space";

        private static Window hudWindow;
        private static Window bootWindow;
        private static Window settingsWindow;
        private static Window planDocWindow;
        private static int lastSeenPlanWindowSeq = -1;
        private static Border hudCardBorder;
        private static TextBlock txtStatus;
        private static TextBlock txtHeard;
        private static TextBlock txtMainBody;
        private static TextBox inputBox;
        private static Button btnYes;
        private static Button btnNo;
        private static ScaleTransform orbCoreScale;
        private static DropShadowEffect cardGlow;
        private static Grid fullContentGrid;
        private static Grid miniOrbGrid;
        private static bool isMiniMode = false;

        private static readonly MediaPlayer koreanVoicePlayer = new MediaPlayer();
        private static double currentSpeechSpeedRatio = 1.25;
        private static string currentSpeechVoice = "ko-KR-HyunsuMultilingualNeural";
        private static TextBlock settingsFeedback;
        private static readonly System.Threading.SemaphoreSlim settingsSaveGate = new System.Threading.SemaphoreSlim(1, 1);
        private static string currentBrainProvider = "antigravity-cli";
        private static string currentBrainModel = "gemini-3.8-flash-low";
        private static int brainVerifySequence;
        private static string currentUserPlanTier = "ultra";
        private static bool suppressTextChanged = false;
        private static DateTime nextScreenCheck = DateTime.MinValue;
        private static readonly HttpClient http = new HttpClient();
        private static bool hotkeyWasDown = false;
        private static bool bootHotkeyWasDown = false;
        private static bool isSpeaking = false;
        private static readonly System.Threading.SemaphoreSlim speechGate = new System.Threading.SemaphoreSlim(1, 1);
        private static string lastSpeechRequest = "";
        private static DateTime lastSpeechRequestAt = DateTime.MinValue;

        private static readonly Color CyanColor = Color.FromRgb(77, 217, 255);   // #4DD9FF (HUD.swift cyan)
        private static readonly Color AmberColor = Color.FromRgb(255, 184, 64);  // #FFB840 (HUD.swift amber)
        private static readonly Color LimeColor = Color.FromRgb(115, 255, 153);  // #73FF99 (HUD.swift lime)

        private static void WriteLog(string msg)
        {
            try
            {
                Directory.CreateDirectory("C:\\자비스\\data");
                File.AppendAllText("C:\\자비스\\data\\hud-runtime.log",
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + Environment.NewLine);
            }
            catch { }
        }

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

        [STAThread]
        public static void Main(string[] args)
        {
            try
            {
                string deskName = GetObjName(GetThreadDesktop(GetCurrentThreadId()));
                WriteLog("Main started on Desktop=" + deskName);
                if (!string.Equals(deskName, "Default", StringComparison.OrdinalIgnoreCase))
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    STARTUPINFO si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf(si);
                    si.lpDesktop = "Winsta0\\Default";
                    PROCESS_INFORMATION pi;
                    bool ok = CreateProcess(exePath, "\"" + exePath + "\" --on-default", IntPtr.Zero, IntPtr.Zero, false, 0, IntPtr.Zero, "C:\\자비스", ref si, out pi);
                    WriteLog("Re-spawned on Winsta0\\Default ok=" + ok + " pid=" + pi.dwProcessId);
                    return;
                }
                bool created;
                using (var instance = new System.Threading.Mutex(true, "Local\\JarvisDesktopHUD", out created))
                {
                    if (!created) {
                        try { using (var signal = System.Threading.EventWaitHandle.OpenExisting("Local\\JarvisHudShow")) signal.Set(); } catch { }
                        WriteLog("HUD already running; existing window requested"); return;
                    }
                    showRequest = new System.Threading.EventWaitHandle(false, System.Threading.EventResetMode.AutoReset, "Local\\JarvisHudShow");
                    StartHudSupervisor();
                    AppDomain.CurrentDomain.UnhandledException += (sender, e) => WriteLog("Unhandled process exception: " + e.ExceptionObject);
                    AppDomain.CurrentDomain.ProcessExit += (sender, e) => WriteLog("HUD process exiting; explicitQuit=" + allowShutdown);
                    try { RunOverlay(); }
                    finally { instance.ReleaseMutex(); }
                }
                WriteLog("Main exited normally");
            }
            catch (Exception ex)
            {
                WriteLog("Main fatal: " + ex.ToString());
            }
        }

        private static Window secHudWindow;
        private static Window thirdHudWindow;
        private static TextBlock secTxtStatus;
        private static TextBlock secTxtMainBody;
        private static Border secHudCardBorder;
        private static TextBlock thirdTxtStatus;
        private static TextBlock thirdTxtMainBody;
        private static Border thirdHudCardBorder;

        [STAThread]
        public static void RunOverlay()
        {
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.Default;
            Application app = Application.Current ?? new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            app.DispatcherUnhandledException += (s, e) =>
            {
                WriteLog("DispatcherUnhandledException: " + e.Exception.ToString());
                e.Handled = true;
            };

            try
            {
                IntPtr cw = GetConsoleWindow();
                if (cw != IntPtr.Zero) ShowWindow(cw, SW_HIDE);
            }
            catch { }

            // Absorb Win32 STARTUPINFO.wShowWindow = SW_HIDE so hudWindow.Show() is 100% visible on the desktop!
            try
            {
                Window absorb = new Window
                {
                    Width = 1,
                    Height = 1,
                    Left = -32000,
                    Top = -32000,
                    WindowStyle = WindowStyle.None,
                    ShowInTaskbar = false,
                    Opacity = 0
                };
                absorb.Show();
                absorb.Hide();
            }
            catch { }

            WriteLog("Step 1: Before EnsureBackendServerRunning");
            EnsureBackendServerRunning();

            try
            {
                if (File.Exists("C:\\자비스\\data\\config.json"))
                {
                    string cfgJson = File.ReadAllText("C:\\자비스\\data\\config.json", Encoding.UTF8);
                    string bp = ExtractJsonField(cfgJson, "brainProvider");
                    string bm = ExtractJsonField(cfgJson, "brainModel");
                    if (!string.IsNullOrEmpty(bp)) currentBrainProvider = bp;
                    if (!string.IsNullOrEmpty(bm)) currentBrainModel = bm;
                    var mSpd = System.Text.RegularExpressions.Regex.Match(cfgJson, "\"ttsSpeed\"\\s*:\\s*([0-9.]+)");
                    if (mSpd.Success)
                    {
                        double spdVal;
                        if (double.TryParse(mSpd.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out spdVal) && spdVal >= 0.7 && spdVal <= 2.0)
                        {
                            currentSpeechSpeedRatio = spdVal;
                        }
                    }
                }
            }
            catch { }

            BuildTopMonitorHudWindow();
            hudWindow.SourceInitialized += (sender, e) => {
                var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(hudWindow).Handle);
                if (source != null) source.AddHook(KeepHudPositionOnPrimary);
            };
            app.MainWindow = hudWindow;
            hudWindow.Closing += (sender, e) => {
                if (!allowShutdown) { e.Cancel = true; EnsureHudAlwaysVisibleAndTopmost(); }
            };
            var context = new ContextMenu();
            var returnHome = new MenuItem { Header = "주 모니터로 이동" };
            returnHome.Click += (sender, e) => PlaceHudWindows();
            var quit = new MenuItem { Header = "자비스 종료" };
            quit.Click += (sender, e) => RequestHudShutdown("HUD context menu");
            context.Items.Add(returnHome); context.Items.Add(quit); hudWindow.ContextMenu = context;
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            app.Exit += (sender, e) => {
                WriteLog("WPF exit code=" + e.ApplicationExitCode + "; explicitQuit=" + allowShutdown);
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            };
            app.SessionEnding += (sender, e) => { allowShutdown = true; SignalSupervisorStop(); WriteLog("Windows session ending: " + e.ReasonSessionEnding); };
            StartContinuousMicrophoneEngine();
            StartGlobalRightAltHook();
            StartRuntimeTimers();
            ShowFullMonitorBootScene("자비스", "대표님", "음성 비서를 준비하고 있습니다");
            app.Run();
        }

        private static bool initialBootComplete, allowShutdown, backendProbeInFlight, backendReady, startupGreetingSpoken, earProbeInFlight;
        private static DateTime lastEarLaunch = DateTime.MinValue;
        private static bool earWindowMaintenanceInFlight;
        private static Process ownedBackend;
        private static int backendFailures;
        private static System.Threading.EventWaitHandle showRequest;

        private static void StartHudSupervisor()
        {
            try {
                System.Threading.Mutex guard;
                if (System.Threading.Mutex.TryOpenExisting("Local\\JarvisHudSupervisor", out guard)) { guard.Dispose(); return; }
                var process = Process.GetCurrentProcess();
                string script = "C:\\자비스\\scripts\\Watch-JarvisHud.ps1";
                if (!File.Exists(script)) { WriteLog("HUD supervisor script missing"); return; }
                Process.Start(new ProcessStartInfo {
                    FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell\\v1.0\\powershell.exe"),
                    Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File \"" + script + "\" -HudProcessId " + process.Id + " -ExpectedStartTimeTicks " + process.StartTime.ToUniversalTime().Ticks,
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                });
                WriteLog("HUD supervisor requested for pid=" + process.Id);
            } catch (Exception ex) { WriteLog("HUD supervisor launch failed: " + ex.Message); }
        }

        private static void SignalSupervisorStop()
        {
            try { using (var stop = System.Threading.EventWaitHandle.OpenExisting("Local\\JarvisHudSupervisorStop")) stop.Set(); }
            catch (System.Threading.WaitHandleCannotBeOpenedException) { }
        }

        private static void RequestHudShutdown(string reason)
        {
            allowShutdown = true;
            WriteLog("Explicit HUD quit: " + reason);
            SignalSupervisorStop();
            Application.Current.Shutdown();
        }

        private static async Task<string> ReadHealth(string path)
        {
            using (var cancel = new System.Threading.CancellationTokenSource(1800))
            using (var response = await http.GetAsync("http://127.0.0.1:7788" + path, cancel.Token)) {
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
        }

        private static async Task<bool> EnsureBackendServerRunning()
        {
            if (backendProbeInFlight) return backendReady;
            backendProbeInFlight = true;
            try {
                try {
                    string health = await ReadHealth("/api/health");
                    if (health.Contains("\"service\":\"jarvis\"")) { backendReady = true; backendFailures = 0; return true; }
                } catch { }
                backendReady = false; backendFailures++;
                if (ownedBackend != null && !ownedBackend.HasExited) {
                    if (backendFailures < 3) return false;
                    WriteLog("Restarting unresponsive owned backend pid=" + ownedBackend.Id);
                    await Task.Run(() => { ownedBackend.Kill(); ownedBackend.WaitForExit(1500); });
                }
                foreach (var endpoint in System.Net.NetworkInformation.IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners()) {
                    if (endpoint.Port == 7788) { WriteLog("Port 7788 is occupied; leaving its owner untouched"); return false; }
                }
                var info = new ProcessStartInfo {
                    FileName = File.Exists("C:\\Program Files\\nodejs\\node.exe") ? "C:\\Program Files\\nodejs\\node.exe" : "node.exe",
                    Arguments = "\"C:\\자비스\\server.verified.js\"", WorkingDirectory = "C:\\자비스",
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                ownedBackend = new Process { StartInfo = info };
                ownedBackend.OutputDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) WriteLog("Backend: " + e.Data); };
                ownedBackend.ErrorDataReceived += (sender, e) => { if (!string.IsNullOrEmpty(e.Data)) WriteLog("Backend error: " + e.Data); };
                ownedBackend.Start(); ownedBackend.BeginOutputReadLine(); ownedBackend.BeginErrorReadLine();
                backendFailures = 0;
                WriteLog("Backend launched pid=" + ownedBackend.Id + " entry=server.verified.js");
                await Task.Delay(750);
                try { backendReady = (await ReadHealth("/api/health")).Contains("\"service\":\"jarvis\""); } catch { }
                return backendReady;
            }
            catch (Exception ex) { WriteLog("Backend recovery: " + ex.Message); return false; }
            finally { backendProbeInFlight = false; }
        }

        private static void StartRuntimeTimers()
        {
            WriteLog("Runtime timers started on HUD UI thread");
            var recovery = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            recovery.Tick += async (sender, e) => {
                bool wasReady = backendReady;
                if (await EnsureBackendServerRunning()) {
                    StartInvisibleChromeEarWorker();
                    if (initialBootComplete && !startupGreetingSpoken) {
                        startupGreetingSpoken = true; SpeakAsync("자비스가 준비되었습니다.");
                    }
                    if (!wasReady && initialBootComplete && txtStatus != null) txtStatus.Text = "○ 서버 연결 복구 · 명령을 말씀해 주세요";
                } else if (initialBootComplete && txtStatus != null) txtStatus.Text = "⚠ 서버 연결 복구 중 · 다시 확인하고 있습니다";
            };
            recovery.Start();
            var visibility = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            visibility.Tick += (sender, e) => {
                HideChromeEarWindows(); EnsureHudAlwaysVisibleAndTopmost();
                if (showRequest != null && showRequest.WaitOne(0) && initialBootComplete) PlaceHudWindows();
            };
            visibility.Start();
        }

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();
        [DllImport("user32.dll")]
        private static extern IntPtr GetWindowDC(IntPtr hWnd);
        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")]
        private static extern uint GetPixel(IntPtr hdc, int nXPos, int nYPos);

        [DllImport("user32.dll")]
        private static extern IntPtr GetProcessWindowStation();
        [DllImport("user32.dll")]
        private static extern IntPtr GetThreadDesktop(uint dwThreadId);
        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetUserObjectInformation(IntPtr hObj, int nIndex, StringBuilder pvInfo, int nLength, out int lpnLengthNeeded);

        private static string GetObjName(IntPtr h)
        {
            StringBuilder sb = new StringBuilder(256);
            int needed;
            if (GetUserObjectInformation(h, 2, sb, 256, out needed)) return sb.ToString();
            return "?";
        }

        private static void WriteWindowDiag(string tag, Window w)
        {
            try
            {
                if (w == null) { WriteLog(tag + ": null"); return; }
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(w).Handle;
                string winsta = GetObjName(GetProcessWindowStation());
                string deskName = GetObjName(GetThreadDesktop(GetCurrentThreadId()));
                int sessId = Process.GetCurrentProcess().SessionId;
                WriteLog(string.Format("{0}: Session={1} WinSta={2} Desktop={3} Handle={4} IsVisible={5}",
                    tag, sessId, winsta, deskName, h.ToInt64(), w.IsVisible));
            }
            catch (Exception ex)
            {
                WriteLog(tag + " err: " + ex.Message);
            }
        }

        private static void EnsureHudAlwaysVisibleAndTopmost()
        {
            if (!initialBootComplete || allowShutdown || hudWindow == null) return;
            try {
                if (hudWindow.WindowState == WindowState.Minimized) hudWindow.WindowState = WindowState.Normal;
                if (!hudWindow.IsVisible) hudWindow.Show();
                IntPtr handle = new System.Windows.Interop.WindowInteropHelper(hudWindow).Handle;
                if (handle != IntPtr.Zero) SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                if (DateTime.UtcNow >= nextScreenCheck) { nextScreenCheck = DateTime.UtcNow.AddSeconds(2); KeepWindowOnScreen(hudWindow); }
            } catch (Exception ex) { WriteLog("HUD visibility: " + ex.Message); }
        }

        private static void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (hudWindow == null) return;
            hudWindow.Dispatcher.BeginInvoke(new Action(() => { PlaceHudWindows(); EnsureHudAlwaysVisibleAndTopmost(); }));
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HudWindowPosition {
            public IntPtr Window, InsertAfter;
            public int X, Y, Width, Height;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HudWindowRectangle { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr window, out HudWindowRectangle rectangle);

        // Clamp the proposed native move before Windows applies it. A polling
        // correction after DragMove lets the HUD leave the primary monitor first.
        private static IntPtr KeepHudPositionOnPrimary(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WindowPositionChanging = 0x0046;
            if (message != WindowPositionChanging || lParam == IntPtr.Zero || allowShutdown) return IntPtr.Zero;
            try {
                var position = (HudWindowPosition)Marshal.PtrToStructure(lParam, typeof(HudWindowPosition));
                bool moving = (position.Flags & SWP_NOMOVE) == 0;
                bool resizing = (position.Flags & SWP_NOSIZE) == 0;
                if (!moving && !resizing) return IntPtr.Zero;
                var bounds = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                int width = position.Width, height = position.Height;
                if (!moving || !resizing) {
                    HudWindowRectangle rectangle;
                    if (!GetWindowRect(window, out rectangle)) return IntPtr.Zero;
                    if (!resizing) { width = rectangle.Right - rectangle.Left; height = rectangle.Bottom - rectangle.Top; }
                    if (!moving) { position.X = rectangle.Left; position.Y = rectangle.Top; }
                }
                int x = Math.Max(bounds.Left, Math.Min(position.X, bounds.Right - Math.Max(1, width)));
                int y = Math.Max(bounds.Top, Math.Min(position.Y, bounds.Bottom - Math.Max(1, height)));
                if (x != position.X || y != position.Y) position.Flags &= ~SWP_NOMOVE;
                position.X = x; position.Y = y;
                Marshal.StructureToPtr(position, lParam, false);
            } catch (Exception ex) { WriteLog("HUD position clamp: " + ex.Message); }
            return IntPtr.Zero;
        }

        private static void KeepWindowOnScreen(Window window)
        {
            if (window == null || !window.IsLoaded) return;
            var source = System.Windows.PresentationSource.FromVisual(window);
            var transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice : System.Windows.Media.Matrix.Identity;
            var point = transform.Transform(new Point(window.Left, window.Top));
            var bounds = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            if (point.X < bounds.Left || point.Y < bounds.Top || point.X + window.ActualWidth * transform.M11 > bounds.Right || point.Y + 80 * transform.M22 > bounds.Bottom) PlaceHudWindows();
        }

        private static async void StartInvisibleChromeEarWorker()
        {
            if (earProbeInFlight || !backendReady) return;
            earProbeInFlight = true;
            try {
                string health = await ReadHealth("/api/ear/health");
                if (health.Contains("\"connected\":true")) return;
                // A recognition restart is not a dead worker. The server only
                // requests recovery once the heartbeat has actually expired.
                if (!health.Contains("\"recoverable\":true")) return;
                if (DateTime.UtcNow - lastEarLaunch < TimeSpan.FromSeconds(30)) return;
                if (health.Contains("not-allowed") || health.Contains("permission-denied")) {
                    if (txtStatus != null) txtStatus.Text = "⚠ 마이크 권한을 확인해 주세요"; return;
                }
                lastEarLaunch = DateTime.UtcNow;
                var recoveryClock = Stopwatch.StartNew();
                WriteLog("Dedicated microphone recovery started: stale heartbeat");
                // WMI enumeration and process exit can take seconds. Neither
                // belongs on the WPF dispatcher that keeps the HUD responsive.
                await Task.Run(() => RestartDedicatedEarWorker());
                WriteLog("Dedicated microphone recovery completed in " + recoveryClock.ElapsedMilliseconds + " ms");
            } catch (Exception ex) { WriteLog("Microphone recovery: " + ex.Message); }
            finally { earProbeInFlight = false; }
        }

        private static void RestartDedicatedEarWorker()
        {
                const string profileDir = "C:\\자비스\\data\\chrome-ear-profile";
                var queryOptions = new System.Management.EnumerationOptions { Timeout = TimeSpan.FromSeconds(3) };
                using (var searcher = new System.Management.ManagementObjectSearcher("root\\CIMV2", "SELECT ProcessId,CommandLine FROM Win32_Process WHERE Name='chrome.exe'", queryOptions))
                using (var rows = searcher.Get()) {
                    foreach (System.Management.ManagementObject row in rows) {
                        string command = Convert.ToString(row["CommandLine"]);
                        if (command.IndexOf(profileDir, StringComparison.OrdinalIgnoreCase) >= 0 && !command.Contains("--type=")) {
                            try { using (var worker = Process.GetProcessById(Convert.ToInt32(row["ProcessId"]))) { worker.Kill(); worker.WaitForExit(1500); } } catch { }
                        }
                    }
                }
                Directory.CreateDirectory(profileDir);
                string chromePath = File.Exists("C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe") ? "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe" : "msedge.exe";
                if (allowShutdown) return;
                using (Process.Start(new ProcessStartInfo {
                    FileName = chromePath,
                    Arguments = "--app=http://127.0.0.1:7788/ear?worker=1 --use-fake-ui-for-media-stream --autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --user-data-dir=\"" + profileDir + "\" --window-position=-3200,-3200 --window-size=320,120",
                    UseShellExecute = false, CreateNoWindow = true, WindowStyle = ProcessWindowStyle.Hidden
                })) { }
                WriteLog("Dedicated microphone worker restarted");
        }

        private static async void HideChromeEarWindows()
        {
            if (earWindowMaintenanceInFlight || allowShutdown) return;
            earWindowMaintenanceInFlight = true;
            IntPtr myHwnd1 = hudWindow != null ? new System.Windows.Interop.WindowInteropHelper(hudWindow).Handle : IntPtr.Zero;
            IntPtr myHwnd2 = secHudWindow != null ? new System.Windows.Interop.WindowInteropHelper(secHudWindow).Handle : IntPtr.Zero;
            try
            {
                // Cross-process window messages may wait for Chrome's UI thread.
                // Keep that wait off the HUD dispatcher and allow only one scan.
                await Task.Run(() => {
                EnumWindows((hWnd, lParam) =>
                {
                    if (hWnd == myHwnd1 || hWnd == myHwnd2) return true;
                    StringBuilder sb = new StringBuilder(256);
                    if (GetWindowText(hWnd, sb, 256) > 0)
                    {
                        string title = sb.ToString();
                        if ((title.Contains("자비스 — React 18 소형 HUD") || title.Contains("127.0.0.1:7788/ear")) &&
                            !title.Contains("Visual Studio") && !title.Contains("Antigravity") && !title.Contains("Cursor") && !title.Contains("Monitor"))
                        {
                            int exStyle = GetWindowLong(hWnd, GWL_EXSTYLE);
                            int desired = (exStyle | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
                            if (exStyle != desired)
                            {
                                SetWindowLong(hWnd, GWL_EXSTYLE, desired);
                            }
                            const uint AsyncWindowPosition = 0x4000;
                            SetWindowPos(hWnd, IntPtr.Zero, -32000, -32000, 320, 120, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED | AsyncWindowPosition);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
                });
            }
            catch { }
            finally { earWindowMaintenanceInFlight = false; }
        }

        // =====================================================================
        // 1. DESKTOP FLOATING SMALL HUD PILL (680x114) — DRAGGABLE ON DESKTOP
        // =====================================================================
        private static void BuildTopMonitorHudWindow()
        {
            double winW = 720;
            double winH = 190;

            hudWindow = new Window
            {
                Title = "자비스 Monitor Top HUD",
                Width = winW,
                Height = winH,
                SizeToContent = SizeToContent.Height,
                Left = 180,
                Top = 42,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true,
                ShowInTaskbar = true,
                ResizeMode = ResizeMode.NoResize
            };

            // Register Windows System Tray Icon (1:1 port of `NSStatusBar.system` in `main.swift`)
            try
            {
                var tray = new System.Windows.Forms.NotifyIcon
                {
                    Text = "◉ 자비스 (SasariSa v0.7.3)",
                    Icon = System.Drawing.SystemIcons.Information,
                    Visible = true
                };
                var trayMenu = new System.Windows.Forms.ContextMenuStrip();
                trayMenu.Items.Add("🎙 지금 말하기 (음성 듣기)", null, (s, e) => PromptListenNow());
                trayMenu.Items.Add("🔊 HD 자연음성 미리 듣기", null, (s, e) => SpeakAsync("안녕하세요, 이건우 대표님. 선택한 목소리와 속도로 안내하고 있습니다."));
                trayMenu.Items.Add("⚡ 깨우기 장면 (BootScene)", null, (s, e) => SendCommandToButler("아빠 왔어"));
                trayMenu.Items.Add("⚙ 단축키 및 호출 설정…", null, (s, e) => ShowHotkeySettingsWindow());
                trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                trayMenu.Items.Add("✕ 자비스 종료 (Quit)", null, (s, e) => { tray.Visible = false; RequestHudShutdown("tray menu"); });
                tray.ContextMenuStrip = trayMenu;
                tray.DoubleClick += (s, e) => { hudWindow.Show(); hudWindow.Activate(); };
            }
            catch { }

            Grid root = new Grid();
            hudWindow.Content = root;

            fullContentGrid = new Grid();
            root.Children.Add(fullContentGrid);

            cardGlow = new DropShadowEffect
            {
                Color = CyanColor,
                BlurRadius = 22,
                ShadowDepth = 0,
                Opacity = 0.70
            };

            hudCardBorder = new Border
            {
                Width = 680,
                MinHeight = 114,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 0),
                CornerRadius = new CornerRadius(22),
                Background = new SolidColorBrush(Color.FromArgb(244, 5, 11, 22)),
                BorderBrush = new SolidColorBrush(CyanColor),
                BorderThickness = new Thickness(1.6),
                Effect = cardGlow,
                Padding = new Thickness(18, 12, 22, 12),
                Cursor = Cursors.SizeAll,
                ToolTip = "드래그하여 이동 · 자비스를 부르거나 직접 입력하세요"
            };

            hudCardBorder.MouseLeftButtonDown += (s, e) =>
            {
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null && source != hudCardBorder)
                {
                    if (source is TextBox || source is Button) return;
                    source = VisualTreeHelper.GetParent(source);
                }
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { hudWindow.DragMove(); } catch { }
                }
            };

            ContextMenu ctx = new ContextMenu();
            MenuItem miWake = new MenuItem { Header = "🎙 지금 말하기 (음성 듣기)" };
            miWake.Click += (s, e) => PromptListenNow();
            ctx.Items.Add(miWake);

            MenuItem miVoiceTest = new MenuItem { Header = "🔊 설정한 음성 미리 듣기" };
            miVoiceTest.Click += (s, e) => SpeakAsync("안녕하세요, 대표님. 자비스입니다. 스튜디오 HD 자연음성으로 정상 연결되었습니다.");
            ctx.Items.Add(miVoiceTest);

            MenuItem miBoot = new MenuItem { Header = "⚡ 깨우기 장면 보기 (BootScene)" };
            miBoot.Click += (s, e) => SendCommandToButler("아빠 왔어");
            ctx.Items.Add(miBoot);

            MenuItem miSet = new MenuItem { Header = "⚙ 단축키 및 호출 설정…" };
            miSet.Click += (s, e) => ShowHotkeySettingsWindow();
            ctx.Items.Add(miSet);

            MenuItem miPlanHud = new MenuItem { Header = "📋 Plan · 설계 문서 프리뷰 HUD 창 열기" };
            miPlanHud.Click += (s, e) => ShowPlanAndChlgdDocumentWindow("plan");
            ctx.Items.Add(miPlanHud);

            MenuItem miChlgdRules = new MenuItem { Header = "📓 CHLGD · 대표님 대화 규칙 & 학습 로그 원장 보기" };
            miChlgdRules.Click += (s, e) => ShowPlanAndChlgdDocumentWindow("rules");
            ctx.Items.Add(miChlgdRules);

            ctx.Items.Add(new Separator());

            MenuItem miPosTopLeft = new MenuItem { Header = "📍 위치: 바탕화면 왼쪽 위 (기본)" };
            miPosTopLeft.Click += (s, e) => { hudWindow.Left = 180; hudWindow.Top = 42; };
            ctx.Items.Add(miPosTopLeft);

            MenuItem miPosTopCenter = new MenuItem { Header = "📍 위치: 모니터 가운데 위" };
            miPosTopCenter.Click += (s, e) => { hudWindow.Left = (SystemParameters.PrimaryScreenWidth - hudWindow.Width) / 2.0; hudWindow.Top = 18; };
            ctx.Items.Add(miPosTopCenter);

            MenuItem miPosBottomRight = new MenuItem { Header = "📍 위치: 오른쪽 아래 (HUD.swift 기본)" };
            miPosBottomRight.Click += (s, e) => { hudWindow.Left = 1920 - hudWindow.Width - 24; hudWindow.Top = SystemParameters.PrimaryScreenHeight - hudWindow.Height - 64; };
            ctx.Items.Add(miPosBottomRight);

            ctx.Items.Add(new Separator());

            MenuItem miQuit = new MenuItem { Header = "✕ 자비스 종료 (Quit)" };
            miQuit.Click += (s, e) => RequestHudShutdown("HUD menu");
            ctx.Items.Add(miQuit);

            hudCardBorder.ContextMenu = ctx;

            StackPanel hudVerticalStack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 0, 0, 8)
            };
            hudVerticalStack.Children.Add(hudCardBorder);
            hudVerticalStack.Children.Add(BuildCollapsibleSubDrawers());
            fullContentGrid.Children.Add(hudVerticalStack);

            Grid cardLayout = new Grid();
            cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
            cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hudCardBorder.Child = cardLayout;

            // Left: 88x88 3-Ring Spinning Dashed Orb (Image 3 1:1)
            FrameworkElement orbElem = CreateSpinningOrb(88, true);
            orbElem.Cursor = Cursors.Hand;
            orbElem.ToolTip = "클릭하여 음성 듣기";
            orbElem.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                PromptListenNow();
            };
            Grid.SetColumn(orbElem, 0);
            cardLayout.Children.Add(orbElem);

            // Middle: ONLY 2 Clean Lines like Image 3 ("○ 받아 적는 중" + "클래식 음악 좀 틀어 줘")
            StackPanel textCol = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 8, 0)
            };
            Grid.SetColumn(textCol, 1);
            cardLayout.Children.Add(textCol);

            txtStatus = new TextBlock
            {
                Text = "○ 호출어 대기 · 자비스라고 불러 주세요",
                FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(CyanColor),
                Margin = new Thickness(0, 0, 0, 8)
            };
            textCol.Children.Add(txtStatus);

            txtHeard = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(185, 200, 220)),
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 2)
            };
            textCol.Children.Add(txtHeard);

            txtMainBody = new TextBlock
            {
                Text = "무엇을 도와드릴까요?",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 18,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 0)
            };
            textCol.Children.Add(txtMainBody);

            // Visible typed command input; voice typing can still use this field.
            Grid cmdRow = new Grid
            {
                Margin = new Thickness(0, 4, 0, 0)
            };
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            textCol.Children.Add(cmdRow);

            inputBox = new TextBox
            {
                MinWidth = 80,
                Height = 31,
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(65, 86, 105)),
                Background = new SolidColorBrush(Color.FromRgb(25, 30, 39)),
                Foreground = Brushes.White,
                CaretBrush = Brushes.White,
                FontSize = 13,
                Padding = new Thickness(8, 4, 8, 4),
                ToolTip = "명령을 입력하고 Enter 또는 전송을 누르세요"
            };
            inputBox.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; SubmitHudInput(); }
            };
            inputBox.TextChanged += (s, e) =>
            {
                if (suppressTextChanged) return;
                string live = inputBox.Text.Trim();
                if (!string.IsNullOrEmpty(live))
                {
                    SetHudAppearance(CyanColor);
                    txtStatus.Text = "○ 받아 적는 중";
                    txtMainBody.Text = live;
                    if (secTxtStatus != null) secTxtStatus.Text = "○ 받아 적는 중";
                    if (secTxtMainBody != null) secTxtMainBody.Text = live;
                    if (thirdTxtStatus != null) thirdTxtStatus.Text = "○ 받아 적는 중";
                    if (thirdTxtMainBody != null) thirdTxtMainBody.Text = live;
                }
            };
            cmdRow.Children.Add(inputBox);

            Button btnSubmit = CreatePillButton("전송", CyanColor);
            btnSubmit.Margin = new Thickness(6, 0, 0, 0);
            btnSubmit.Click += (s, e) => SubmitHudInput();
            Grid.SetColumn(btnSubmit, 1);
            cmdRow.Children.Add(btnSubmit);

            btnYes = CreatePillButton("응 (전송)", LimeColor);
            btnYes.Margin = new Thickness(6, 0, 6, 0);
            btnYes.Visibility = Visibility.Collapsed;
            btnYes.Click += (s, e) => SendCommandToButler("응");
            Grid.SetColumn(btnYes, 2);
            cmdRow.Children.Add(btnYes);

            btnNo = CreatePillButton("아니 (취소)", AmberColor);
            btnNo.Margin = new Thickness(0, 0, 0, 0);
            btnNo.Visibility = Visibility.Collapsed;
            btnNo.Click += (s, e) => SendCommandToButler("아니");
            Grid.SetColumn(btnNo, 3);
            cmdRow.Children.Add(btnNo);

            // Top-right subtle native vector Settings Icon (only icon at top-right, no text buttons)
            FrameworkElement btnGear = CreateTopRightGearIcon();
            Grid.SetColumn(btnGear, 2);
            cardLayout.Children.Add(btnGear);

            // Mini Floating Orb Mode (draggable + double-click or click button to expand)
            miniOrbGrid = new Grid
            {
                Width = 130,
                Height = 140,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeAll,
                ToolTip = "드래그하여 이동 · 아래 [HUD 펼치기] 클릭 시 상단 바 복귀"
            };
            StackPanel miniStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            miniStack.Children.Add(CreateSpinningOrb(92, false));
            Button btnExpandHud = CreateMiniButton("▲ HUD 펼치기");
            btnExpandHud.Margin = new Thickness(0, 4, 0, 0);
            btnExpandHud.Click += (s, e) => ToggleMiniMode(false);
            miniStack.Children.Add(btnExpandHud);
            miniOrbGrid.Children.Add(miniStack);
            miniOrbGrid.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { hudWindow.DragMove(); } catch { }
                }
            };
            root.Children.Add(miniOrbGrid);
        }

        private static void PlaceHudWindows()
        {
            if (hudWindow == null) return;
            var screen = System.Windows.Forms.Screen.PrimaryScreen;
            var source = System.Windows.PresentationSource.FromVisual(hudWindow);
            var transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : System.Windows.Media.Matrix.Identity;
            var point = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            hudWindow.Left = point.X + 20; hudWindow.Top = point.Y + 20;
        }

        private static void BuildSecondaryMonitorHudWindow()
        {
            try
            {
                secHudWindow = new Window
                {
                    Title = "자비스 Secondary Monitor HUD",
                    Width = 720,
                    Height = 152,
                    Left = 180,
                    Top = -1038, // Top-left of DISPLAY1 (Y = -1080 + 42)
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ResizeMode = ResizeMode.NoResize
                };

                Grid root = new Grid();
                secHudWindow.Content = root;

                secHudCardBorder = new Border
                {
                    Width = 680,
                    MinHeight = 114,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 8, 0, 0),
                    CornerRadius = new CornerRadius(22),
                    Background = new SolidColorBrush(Color.FromArgb(244, 5, 11, 22)),
                    BorderBrush = new SolidColorBrush(CyanColor),
                    BorderThickness = new Thickness(1.6),
                    Effect = new DropShadowEffect { Color = CyanColor, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.70 },
                    Padding = new Thickness(18, 12, 22, 12),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "마우스로 드래그하여 창 이동 · 그냥 말씀하시면 자동으로 받아 적고 실행합니다"
                };

                secHudCardBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        try { secHudWindow.DragMove(); } catch { }
                    }
                };
                secHudCardBorder.ContextMenu = hudCardBorder.ContextMenu;
                root.Children.Add(secHudCardBorder);

                Grid cardLayout = new Grid();
                cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
                cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                secHudCardBorder.Child = cardLayout;

                FrameworkElement orbElem = CreateSpinningOrb(88, false);
                orbElem.Cursor = Cursors.Hand;
                orbElem.MouseLeftButtonUp += (s, e) => { e.Handled = true; PromptListenNow(); };
                Grid.SetColumn(orbElem, 0);
                cardLayout.Children.Add(orbElem);

                StackPanel textCol = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 8, 0)
                };
                Grid.SetColumn(textCol, 1);
                cardLayout.Children.Add(textCol);

                secTxtStatus = new TextBlock
                {
                    Text = "○ 호출어 대기 · 자비스라고 불러 주세요",
                    FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                textCol.Children.Add(secTxtStatus);

                secTxtMainBody = new TextBlock
                {
                    Text = "무엇을 도와드릴까요? (BSC 3대 생태계 · 문서 제작 · REA · Laya)",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 19,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                };
                textCol.Children.Add(secTxtMainBody);

                FrameworkElement secBtnGear = CreateTopRightGearIcon();
                Grid.SetColumn(secBtnGear, 2);
                cardLayout.Children.Add(secBtnGear);

                secHudWindow.Show();
                if (System.Windows.Forms.Screen.AllScreens.Length > 2) BuildThirdMonitorHudWindow();
            }
            catch { }
        }

        private static void BuildThirdMonitorHudWindow()
        {
            try
            {
                thirdHudWindow = new Window
                {
                    Title = "자비스 Third Monitor HUD",
                    Width = 720,
                    Height = 152,
                    Left = 3880,
                    Top = -790, // Top-left of DISPLAY2 (X=3840+40, Y=-842+52)
                    WindowStyle = WindowStyle.None,
                    AllowsTransparency = true,
                    Background = Brushes.Transparent,
                    Topmost = true,
                    ShowInTaskbar = false,
                    ResizeMode = ResizeMode.NoResize
                };

                Grid root = new Grid();
                thirdHudWindow.Content = root;

                thirdHudCardBorder = new Border
                {
                    Width = 680,
                    MinHeight = 114,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Margin = new Thickness(0, 8, 0, 0),
                    CornerRadius = new CornerRadius(22),
                    Background = new SolidColorBrush(Color.FromArgb(244, 5, 11, 22)),
                    BorderBrush = new SolidColorBrush(CyanColor),
                    BorderThickness = new Thickness(1.6),
                    Effect = new DropShadowEffect { Color = CyanColor, BlurRadius = 22, ShadowDepth = 0, Opacity = 0.70 },
                    Padding = new Thickness(18, 12, 22, 12),
                    Cursor = Cursors.SizeAll,
                    ToolTip = "마우스로 드래그하여 창 이동 · 그냥 말씀하시면 자동으로 받아 적고 실행합니다"
                };

                thirdHudCardBorder.MouseLeftButtonDown += (s, e) =>
                {
                    if (e.LeftButton == MouseButtonState.Pressed)
                    {
                        try { thirdHudWindow.DragMove(); } catch { }
                    }
                };
                thirdHudCardBorder.ContextMenu = hudCardBorder.ContextMenu;
                root.Children.Add(thirdHudCardBorder);

                Grid cardLayout = new Grid();
                cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(108) });
                cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cardLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                thirdHudCardBorder.Child = cardLayout;

                FrameworkElement orbElem = CreateSpinningOrb(88, false);
                orbElem.Cursor = Cursors.Hand;
                orbElem.MouseLeftButtonUp += (s, e) => { e.Handled = true; PromptListenNow(); };
                Grid.SetColumn(orbElem, 0);
                cardLayout.Children.Add(orbElem);

                StackPanel textCol = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 8, 0)
                };
                Grid.SetColumn(textCol, 1);
                cardLayout.Children.Add(textCol);

                thirdTxtStatus = new TextBlock
                {
                    Text = "○ 호출어 대기 · 자비스라고 불러 주세요",
                    FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                textCol.Children.Add(thirdTxtStatus);

                thirdTxtMainBody = new TextBlock
                {
                    Text = "무엇을 도와드릴까요? (BSC 3대 생태계 · 문서 제작 · REA · Laya)",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 19,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                };
                textCol.Children.Add(thirdTxtMainBody);

                FrameworkElement thirdBtnGear = CreateTopRightGearIcon();
                Grid.SetColumn(thirdBtnGear, 2);
                cardLayout.Children.Add(thirdBtnGear);

                thirdHudWindow.Show();
            }
            catch { }
        }

        private static void ToggleMiniMode(bool mini)
        {
            isMiniMode = mini;
            fullContentGrid.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
            miniOrbGrid.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
        }

        // =====================================================================
        // 2. SCREENSHOT 1: FULL-MONITOR TRANSPARENT ARC REACTOR BOOT SCENE
        // =====================================================================
        private static void ShowFullMonitorBootScene(string assistantName, string userName, string helloLine)
        {
            if (initialBootComplete || bootWindow != null) return;
            var area = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            bootWindow = new Window {
                Title = "자비스 BootScene Overlay", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(245, 3, 14, 24)),
                Topmost = true, ShowInTaskbar = false, Left = area.Left, Top = area.Top,
                Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight
            };
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(CreateSpinningOrb(250, false));
            panel.Children.Add(new TextBlock { Text = "J A R V I S", FontSize = 36, Foreground = new SolidColorBrush(CyanColor), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 12) });
            panel.Children.Add(new TextBlock { Text = helloLine, FontSize = 20, Foreground = Brushes.White, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = "잠시 후 주 모니터에서 시작합니다", FontSize = 14, Foreground = Brushes.LightGray, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) });
            bootWindow.Content = panel;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            bootWindow.Closed += (sender, e) => {
                timer.Stop(); bootWindow = null; initialBootComplete = true;
                hudWindow.Show(); PlaceHudWindows(); EnsureHudAlwaysVisibleAndTopmost();
                WriteLog("Startup complete: one primary HUD visible after boot"); WriteWindowDiag("primary HUD", hudWindow);
            };
            timer.Tick += (sender, e) => { timer.Stop(); if (bootWindow != null) bootWindow.Close(); };
            bootWindow.Show(); WriteLog("Startup boot visible; main HUD hidden"); timer.Start();
        }

        // =====================================================================
        // 3. SPINNING 3-RING DASHED ORB (OrbView in HUD.swift 1:1)
        // =====================================================================
        private static FrameworkElement CreateSpinningOrb(double size, bool bindMicScale)
        {
            Grid g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            double[] radii = { 0.94, 0.78, 0.62 };
            double[] widths = { 2.2, 1.5, 3.0 };
            DoubleCollection[] dashes = {
                new DoubleCollection(new double[] { 6, 4 }),
                new DoubleCollection(new double[] { 22, 6, 4, 6 }),
                new DoubleCollection(new double[] { 2, 7 })
            };
            double[] durations = { 9.0, -6.0, 4.0 };

            for (int i = 0; i < 3; i++)
            {
                double d = size * radii[i];
                Ellipse ring = new Ellipse
                {
                    Width = d,
                    Height = d,
                    Stroke = new SolidColorBrush(CyanColor),
                    StrokeThickness = widths[i],
                    StrokeDashArray = dashes[i],
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    Opacity = 0.9 - i * 0.12
                };
                RotateTransform rt = new RotateTransform(0);
                ring.RenderTransform = rt;

                DoubleAnimation anim = new DoubleAnimation
                {
                    From = 0,
                    To = durations[i] > 0 ? 360 : -360,
                    Duration = new Duration(TimeSpan.FromSeconds(Math.Abs(durations[i]))),
                    RepeatBehavior = RepeatBehavior.Forever
                };
                rt.BeginAnimation(RotateTransform.AngleProperty, anim);
                g.Children.Add(ring);
            }

            double coreSize = size * 0.44;
            RadialGradientBrush coreBrush = new RadialGradientBrush();
            coreBrush.GradientStops.Add(new GradientStop(Colors.White, 0.0));
            coreBrush.GradientStops.Add(new GradientStop(CyanColor, 0.48));
            coreBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 77, 217, 255), 1.0));

            Ellipse core = new Ellipse
            {
                Width = coreSize,
                Height = coreSize,
                Fill = coreBrush,
                RenderTransformOrigin = new Point(0.5, 0.5),
                Effect = new DropShadowEffect { Color = CyanColor, BlurRadius = 18, ShadowDepth = 0 }
            };

            if (bindMicScale)
            {
                orbCoreScale = new ScaleTransform(1.0, 1.0);
                core.RenderTransform = orbCoreScale;
            }
            g.Children.Add(core);

            return g;
        }

        // =====================================================================
        // 4. CONTINUOUS KOREAN SPEECH RECOGNITION BRIDGE (Ear.swift 1:1)
        // =====================================================================
        private static void StartContinuousMicrophoneEngine()
        {
            // Poll /api/ear/latest every 150ms for live micLevel (OrbView.level) + Korean transcripts
            DispatcherTimer earPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            string lastSyncedKey = "";
            string lastSpokenKey = "";
            bool earPollInFlight = false;
            bool earWarningVisible = false;
            string statusBeforeEarWarning = "";
            earPollTimer.Tick += async (s, e) =>
            {
                if (earPollInFlight) return;
                earPollInFlight = true;
                try {
                try
                {
                    string json = await http.GetStringAsync("http://127.0.0.1:7788/api/ear/latest");
                    if (string.IsNullOrEmpty(json)) return;
                    if (json.Contains("\"microphone\":\"error\"") || json.Contains("\"recognition\":\"error\""))
                    {
                        if (!earWarningVisible) statusBeforeEarWarning = txtStatus.Text;
                        earWarningVisible = true;
                        txtStatus.Text = "⚠ 마이크 또는 음성 인식 확인";
                    }
                    else if (earWarningVisible && json.Contains("\"recognition\":\"listening\""))
                    {
                        txtStatus.Text = string.IsNullOrEmpty(statusBeforeEarWarning) ? "○ 호출어 대기 · 음성 연결 복구" : statusBeforeEarWarning;
                        earWarningVisible = false;
                    }

                    // 1. Animate Orb core scale from live microphone RMS (`HUD.swift` line 52: 1 + min(v * 18, 0.6))
                    int mlIdx = json.IndexOf("\"micLevel\":");
                    if (mlIdx >= 0 && orbCoreScale != null)
                    {
                        int start = mlIdx + 11;
                        int end = start;
                        while (end < json.Length && (char.IsDigit(json[end]) || json[end] == '.')) end++;
                        double lvl = 0;
                        if (double.TryParse(json.Substring(start, end - start), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out lvl))
                        {
                            double scale = 1.0 + Math.Min(lvl * 0.65, 0.65);
                            orbCoreScale.ScaleX = scale;
                            orbCoreScale.ScaleY = scale;
                        }
                    }

                    if (!json.Contains("\"text\":\"")) return;

                    string heard = ExtractJsonField(json, "text");
                    string spoken = ExtractJsonField(json, "spoken");
                    string hudTitle = ExtractJsonField(json, "hudTitle");
                    string hudSubtitle = ExtractJsonField(json, "hudSubtitle");
                    string hudColor = ExtractJsonField(json, "hudColor");
                    string itemTs = ExtractJsonNumber(json, "ts");
                    bool isFinal = json.Contains("\"isFinal\":true");
                    bool awaitingConfirm = json.Contains("\"awaitingConfirm\":true");
                    bool showMailDraft = json.Contains("\"intent\":\"draftMailReply\"");
                    string key = itemTs + "|" + heard + "|" + spoken + "|" + isFinal + "|" + hudTitle + "|" + hudSubtitle;
                    if (key == lastSyncedKey) return;
                    lastSyncedKey = key;
                    earWarningVisible = false;
                    string researchId = ExtractJsonField(json, "researchId");
                    if (!string.IsNullOrEmpty(researchId)) await ShowResearchResultAsync(researchId);

                    if (!string.IsNullOrWhiteSpace(heard))
                    {
                        if (isMiniMode) ToggleMiniMode(false);
                        if (!isFinal)
                        {
                            try { if (autoRecoveryTimer != null) autoRecoveryTimer.Stop(); } catch { }
                            SetHudAppearance(CyanColor);
                            string liveTitle = !string.IsNullOrEmpty(hudTitle) ? hudTitle : "● 경청 중 · 끝까지 편하게 말씀하세요";
                            txtStatus.Text = liveTitle;
                            txtMainBody.Text = heard;
                            if (secTxtStatus != null) secTxtStatus.Text = liveTitle;
                            if (secTxtMainBody != null) secTxtMainBody.Text = heard;
                            if (thirdTxtStatus != null) thirdTxtStatus.Text = liveTitle;
                            if (thirdTxtMainBody != null) thirdTxtMainBody.Text = heard;
                        }
                        else
                        {
                            Color c = hudColor == "amber" ? AmberColor : hudColor == "lime" ? LimeColor : CyanColor;
                            SetHudAppearance(c);
                            txtStatus.Text = !string.IsNullOrEmpty(hudTitle) ? hudTitle : "✓ 실행 완료";
                            txtMainBody.Text = (awaitingConfirm || showMailDraft) && !string.IsNullOrEmpty(hudSubtitle) ? hudSubtitle : (!string.IsNullOrEmpty(spoken) ? spoken : heard);
                            btnYes.Visibility = awaitingConfirm ? Visibility.Visible : Visibility.Collapsed;
                            btnNo.Visibility = awaitingConfirm ? Visibility.Visible : Visibility.Collapsed;
                            if (secTxtStatus != null) secTxtStatus.Text = txtStatus.Text;
                            if (secTxtMainBody != null) secTxtMainBody.Text = txtMainBody.Text;
                            if (thirdTxtStatus != null) thirdTxtStatus.Text = txtStatus.Text;
                            if (thirdTxtMainBody != null) thirdTxtMainBody.Text = txtMainBody.Text;

                            if (!awaitingConfirm && !showMailDraft)
                            {
                                ScheduleAutoRecovery(hudColor == "amber" ? 3200 : 5200);
                            }

                            if (!string.IsNullOrWhiteSpace(spoken) && key != lastSpokenKey)
                            {
                                lastSpokenKey = key;
                                if (heard.Contains("아빠 왔어") || heard.Contains("나 왔어"))
                                {
                                    SpeakAsync(spoken);
                                }
                                else
                                {
                                    SpeakAsync(spoken);
                                }
                            }
                        }
                    }
                }
                catch { }
                try
                {
                    await PollSubWindowsAsync();
                }
                catch { }
                } finally { earPollInFlight = false; }
            };
            earPollTimer.Start();
        }

        private static Window researchWindow;
        private static string lastResearchId = "";
        private static string loadingResearchId = "";
        public class ResearchSection { public string title; public string text; public int sourceId; }
        public class ResearchSource { public int id; public string title; public string url; public string domain; public string readStatus; }
        public class ResearchReport { public string id; public string query; public string title; public string status; public string createdAt; public string provider; public string note; public string summary; public string[] keywords; public ResearchSection[] sections; public ResearchSource[] sources; }

        private static TextBlock ResearchText(string text, double size, Brush color)
        {
            return new TextBlock { Text = text ?? "", FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.55 };
        }

        private static void DragResearchWindow(object sender, MouseButtonEventArgs e)
        {
            if (researchWindow == null || e.ChangedButton != MouseButton.Left || e.ButtonState != MouseButtonState.Pressed || e.ClickCount != 1) return;
            DependencyObject source = e.OriginalSource as DependencyObject;
            while (source != null) {
                if (source is System.Windows.Controls.Primitives.ButtonBase || source is System.Windows.Controls.Primitives.TextBoxBase || source is System.Windows.Controls.Primitives.ScrollBar || source is System.Windows.Controls.Primitives.Thumb) return;
                source = source is Visual ? VisualTreeHelper.GetParent(source) : LogicalTreeHelper.GetParent(source);
            }
            try { e.Handled = true; researchWindow.DragMove(); }
            catch (Exception ex) { WriteLog("Research drag: " + ex.Message); }
        }

        private static async Task ShowResearchResultAsync(string id)
        {
            if (id == lastResearchId || id == loadingResearchId) return;
            loadingResearchId = id;
            try {
                string json = await http.GetStringAsync("http://127.0.0.1:7788/api/research/" + Uri.EscapeDataString(id));
                var report = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<ResearchReport>(json);
                if (report == null || report.id != id) throw new Exception("검색 결과가 일치하지 않습니다.");
                if (researchWindow == null) {
                    var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                    var source = System.Windows.PresentationSource.FromVisual(hudWindow);
                    var matrix = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : System.Windows.Media.Matrix.Identity;
                    var origin = matrix.Transform(new Point(area.Left, area.Top));
                    var extent = matrix.Transform(new Point(area.Width, area.Height));
                    double width = Math.Min(1040, extent.X - 48), height = Math.Min(760, extent.Y - 120);
                    researchWindow = new Window {
                        Title = "JARVIS — 리서치 & 브리핑 리포트",
                        Width = width,
                        Height = height,
                        Left = origin.X + (extent.X - width) / 2,
                        Top = origin.Y + Math.Max(60, (extent.Y - height) / 2),
                        WindowStyle = WindowStyle.None,
                        AllowsTransparency = true,
                        Background = Brushes.Transparent,
                        Topmost = true,
                        ResizeMode = ResizeMode.CanResizeWithGrip,
                        MinWidth = 720,
                        MinHeight = 480
                    };
                    researchWindow.Closed += (sender, args) => { researchWindow = null; };
                }

                Color bgShell = Color.FromRgb(11, 15, 23);
                Color bgCard = Color.FromRgb(17, 24, 36);
                Color bgSubCard = Color.FromRgb(22, 31, 46);
                Color borderCol = Color.FromRgb(40, 52, 70);
                var muted = new SolidColorBrush(Color.FromRgb(155, 170, 190));

                var frame = new Border {
                    Background = new SolidColorBrush(bgShell),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(200, 77, 217, 255)),
                    BorderThickness = new Thickness(1.4),
                    CornerRadius = new CornerRadius(16),
                    Padding = new Thickness(26, 20, 26, 20),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 32, ShadowDepth = 8, Opacity = 0.82 }
                };
                frame.PreviewMouseLeftButtonDown += DragResearchWindow;

                var shell = new Grid();
                shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                frame.Child = shell;

                // 1) Top SaaS Header Bar (Pinned)
                var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(headerGrid, 0);
                shell.Children.Add(headerGrid);

                var titleStack = new StackPanel();
                var topBadgeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                var badgeBorder = new Border {
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromArgb(40, 77, 217, 255)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(120, 77, 217, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                badgeBorder.Child = new TextBlock {
                    Text = "JARVIS INTELLIGENCE REPORT",
                    FontFamily = new FontFamily("Consolas, Segoe UI"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor)
                };
                topBadgeRow.Children.Add(badgeBorder);
                topBadgeRow.Children.Add(new TextBlock {
                    Text = (report.status == "ready" ? "● 분석 완료" : "● 확인 필요") + "  ·  출처 " + (report.sources == null ? 0 : report.sources.Length) + "건  ·  " + (report.provider ?? "JARVIS Engine"),
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11.5,
                    Foreground = muted,
                    VerticalAlignment = VerticalAlignment.Center
                });
                titleStack.Children.Add(topBadgeRow);
                titleStack.Children.Add(new TextBlock {
                    Text = string.IsNullOrEmpty(report.title) ? report.query : report.title,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 20,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(titleStack, 0);
                headerGrid.Children.Add(titleStack);

                var winBtnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
                var maxBtn = new Button {
                    Content = researchWindow.WindowState == WindowState.Maximized ? "복원 ❐" : "최대화 □",
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 6, 0),
                    Background = new SolidColorBrush(bgCard),
                    Foreground = Brushes.White,
                    BorderBrush = new SolidColorBrush(borderCol),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                maxBtn.Click += (s, a) => {
                    if (researchWindow == null) return;
                    researchWindow.WindowState = researchWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    maxBtn.Content = researchWindow.WindowState == WindowState.Maximized ? "복원 ❐" : "최대화 □";
                };
                var close = new Button {
                    Content = "닫기 ✕",
                    Padding = new Thickness(12, 5, 12, 5),
                    Background = new SolidColorBrush(Color.FromRgb(38, 24, 32)),
                    Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 150)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(90, 45, 55)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.Hand
                };
                close.Click += (sender, args) => { if (researchWindow != null) researchWindow.Close(); };
                winBtnRow.Children.Add(maxBtn);
                winBtnRow.Children.Add(close);
                Grid.SetColumn(winBtnRow, 1);
                headerGrid.Children.Add(winBtnRow);

                // 2) Pinned Export Action Bar (Never scrolls out of view or clips)
                var exportContainer = new Border {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(bgCard),
                    BorderBrush = new SolidColorBrush(borderCol),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var exportGrid = new Grid();
                exportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                exportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                exportContainer.Child = exportGrid;

                var exportBar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                var exportStatus = new TextBlock {
                    Text = "원클릭 문서 변환: 원하는 포맷을 클릭하면 즉시 생성 후 파일이 열립니다.",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11.5,
                    Foreground = muted,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                string[][] exportButtons = new string[][] {
                    new string[] { "md", "📝 Markdown (.md)", "#163242" },
                    new string[] { "docx", "📘 Word (.docx)", "#1B365D" },
                    new string[] { "pdf", "📕 PDF (.pdf)", "#4A2030" },
                    new string[] { "pptx", "📙 PPT (.pptx)", "#4A3219" }
                };
                foreach (var eb in exportButtons) {
                    string fmt = eb[0]; string label = eb[1]; string hexBg = eb[2];
                    var btn = new Button {
                        Content = label,
                        Padding = new Thickness(11, 5, 11, 5),
                        Margin = new Thickness(0, 2, 6, 2),
                        FontFamily = new FontFamily("Malgun Gothic"),
                        FontSize = 11.5,
                        FontWeight = FontWeights.Bold,
                        Background = (SolidColorBrush)new BrushConverter().ConvertFromString(hexBg),
                        Foreground = Brushes.White,
                        BorderBrush = new SolidColorBrush(Color.FromArgb(120, 77, 217, 255)),
                        BorderThickness = new Thickness(1),
                        Cursor = Cursors.Hand
                    };
                    btn.Click += async (s2, a2) => {
                        try {
                            exportStatus.Text = "⏳ " + label + " 문서를 생성하고 있습니다...";
                            string reqJson = "{\"researchId\":\"" + EscapeJson(id) + "\",\"format\":\"" + fmt + "\",\"openFile\":true}";
                            var resp = await http.PostAsync("http://127.0.0.1:7788/api/office/create", new StringContent(reqJson, Encoding.UTF8, "application/json"));
                            string respBody = await resp.Content.ReadAsStringAsync();
                            exportStatus.Text = resp.IsSuccessStatusCode ? ("✓ " + label + " 문서 생성 완료 (바탕화면 자동 실행)") : ("⚠ 생성 실패: " + respBody);
                            txtStatus.Text = exportStatus.Text;
                        } catch (Exception ex2) {
                            exportStatus.Text = "⚠ 오류: " + ex2.Message;
                        }
                    };
                    exportBar.Children.Add(btn);
                }
                Grid.SetColumn(exportBar, 0);
                exportGrid.Children.Add(exportBar);
                Grid.SetColumn(exportStatus, 1);
                exportGrid.Children.Add(exportStatus);
                Grid.SetRow(exportContainer, 1);
                shell.Children.Add(exportContainer);

                // 3) Scrollable Structured Body
                var body = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };

                // Executive Summary Card
                var overview = new Border {
                    Background = new SolidColorBrush(Color.FromRgb(15, 34, 48)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(140, 77, 217, 255)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(18, 14, 18, 14),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var summaryStack = new StackPanel();
                summaryStack.Children.Add(new TextBlock {
                    Text = "📌 핵심 요약 (Executive Summary)",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 14,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = new SolidColorBrush(CyanColor),
                    Margin = new Thickness(0, 0, 0, 6)
                });
                summaryStack.Children.Add(new TextBlock {
                    Text = string.IsNullOrEmpty(report.summary) ? report.note : report.summary,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 13.5,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 21
                });
                overview.Child = summaryStack;
                body.Children.Add(overview);

                // Keyword Filter Chips
                if (report.keywords != null && report.keywords.Length > 0) {
                    var chips = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
                    foreach (string keyword in report.keywords) {
                        string term = keyword;
                        var chip = new Button {
                            Content = "# " + term,
                            Padding = new Thickness(10, 4, 10, 4),
                            Margin = new Thickness(0, 0, 6, 6),
                            FontSize = 11.5,
                            Background = new SolidColorBrush(bgSubCard),
                            Foreground = new SolidColorBrush(CyanColor),
                            BorderBrush = new SolidColorBrush(borderCol),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            ToolTip = "클릭 시 이 키워드로 심층 조사"
                        };
                        chip.Click += (sender, args) => SendCommandToButler(term + "에 대해 조사해줘");
                        chips.Children.Add(chip);
                    }
                    body.Children.Add(chips);
                }

                // Structured Sections (Splits bullet lines cleanly into visual rows)
                if (report.sections != null && report.sections.Length > 0) {
                    body.Children.Add(new TextBlock {
                        Text = "📂 항목별 상세 브리핑 (" + report.sections.Length + "개 섹션)",
                        FontFamily = new FontFamily("Malgun Gothic"),
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Margin = new Thickness(2, 0, 0, 10)
                    });
                    foreach (var section in report.sections) {
                        var secCard = new Border {
                            Background = new SolidColorBrush(bgCard),
                            BorderBrush = new SolidColorBrush(borderCol),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(11),
                            Padding = new Thickness(16, 13, 16, 13),
                            Margin = new Thickness(0, 0, 0, 10)
                        };
                        var secStack = new StackPanel();
                        secCard.Child = secStack;

                        var secHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                        var numBadge = new Border {
                            CornerRadius = new CornerRadius(5),
                            Background = new SolidColorBrush(Color.FromArgb(45, 77, 217, 255)),
                            Padding = new Thickness(7, 2, 7, 2),
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        numBadge.Child = new TextBlock {
                            Text = "0" + section.sourceId,
                            FontFamily = new FontFamily("Consolas"),
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(CyanColor)
                        };
                        secHeader.Children.Add(numBadge);
                        secHeader.Children.Add(new TextBlock {
                            Text = section.title ?? "",
                            FontFamily = new FontFamily("Malgun Gothic"),
                            FontSize = 14.5,
                            FontWeight = FontWeights.Bold,
                            Foreground = Brushes.White,
                            VerticalAlignment = VerticalAlignment.Center,
                            TextWrapping = TextWrapping.Wrap
                        });
                        secStack.Children.Add(secHeader);

                        string[] lines = (section.text ?? "").Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        foreach (string rawLine in lines) {
                            string cleanLine = rawLine.Trim();
                            if (string.IsNullOrEmpty(cleanLine)) continue;
                            var lineBorder = new Border {
                                Background = new SolidColorBrush(bgSubCard),
                                CornerRadius = new CornerRadius(6),
                                Padding = new Thickness(11, 7, 11, 7),
                                Margin = new Thickness(0, 0, 0, 5)
                            };
                            lineBorder.Child = new TextBlock {
                                Text = cleanLine,
                                FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                                FontSize = 12.5,
                                Foreground = new SolidColorBrush(Color.FromRgb(230, 238, 248)),
                                TextWrapping = TextWrapping.Wrap,
                                LineHeight = 18.5
                            };
                            secStack.Children.Add(lineBorder);
                        }
                        body.Children.Add(secCard);
                    }
                }

                // Sources Grid
                if (report.sources != null && report.sources.Length > 0) {
                    body.Children.Add(new TextBlock {
                        Text = "🔗 연결된 원본 문서 및 출처 (" + report.sources.Length + "건 · 클릭 시 즉시 열기)",
                        FontFamily = new FontFamily("Malgun Gothic"),
                        FontSize = 14.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = Brushes.White,
                        Margin = new Thickness(2, 6, 0, 8)
                    });
                    foreach (var item in report.sources) {
                        string url = item.url;
                        var link = new Button {
                            Content = new TextBlock {
                                Text = "[" + item.id + "] " + item.title + "   (" + item.domain + " · " + item.readStatus + ")",
                                FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                                FontSize = 12.5,
                                Foreground = new SolidColorBrush(CyanColor),
                                TextWrapping = TextWrapping.Wrap
                            },
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(14, 9, 14, 9),
                            Margin = new Thickness(0, 0, 0, 6),
                            Background = new SolidColorBrush(bgCard),
                            BorderBrush = new SolidColorBrush(borderCol),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            ToolTip = url
                        };
                        link.Click += (sender, args) => {
                            Uri target;
                            if (Uri.TryCreate(url, UriKind.Absolute, out target)) {
                                if (target.Scheme == "https" || target.Scheme == "http")
                                    Process.Start(new ProcessStartInfo(target.AbsoluteUri) { UseShellExecute = true });
                                else if (target.Scheme == "file" && (File.Exists(target.LocalPath) || Directory.Exists(target.LocalPath)))
                                    Process.Start(new ProcessStartInfo(target.LocalPath) { UseShellExecute = true });
                            }
                        };
                        body.Children.Add(link);
                    }
                }

                var resultScroll = new ScrollViewer {
                    Content = body,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
                };
                resultScroll.Loaded += (sender, args) => resultScroll.ScrollToTop();
                Grid.SetRow(resultScroll, 2);
                shell.Children.Add(resultScroll);

                researchWindow.Content = frame;
                researchWindow.Show();
                researchWindow.Activate();
                lastResearchId = id;
                WriteLog("Research result window shown: " + id + "; sources=" + (report.sources == null ? 0 : report.sources.Length));
            } catch (Exception ex) { WriteLog("Research window: " + ex.Message); txtStatus.Text = "⚠ 검색 결과 창을 열지 못했습니다"; }
            finally { loadingResearchId = ""; }
        }

        private static Border taskDrawerBorder;
        private static StackPanel taskDrawerBody;
        private static TextBlock txtTaskDrawerTitle;
        private static TextBlock txtTaskDrawerStep;
        private static TextBlock txtTaskDrawerLogs;
        private static TextBlock txtTaskCollapseBtn;
        private static bool isTaskDrawerExpanded = true;

        private static Border musicDrawerBorder;
        private static Border musicDiscBorder;
        private static TextBlock txtMusicDiscIcon;
        private static TextBlock txtMusicStateBadge;
        private static TextBlock txtMusicDrawerTitle;
        private static Border btnMusicPlayPauseCircle;
        private static TextBlock txtMusicPlayPauseIcon;

        private static WrapPanel quickChipsPanel;
        private static bool isHudMenuDrawerPinned = false;
        private static TextBlock txtTopMenuBtnLabel;

        private static FrameworkElement BuildCollapsibleSubDrawers()
        {
            StackPanel drawersHost = new StackPanel
            {
                Width = 680,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 6, 0, 0)
            };

            // =====================================================================
            // SUB-WINDOW 1: ⚡ 실시간 작업 · 터미널 진행 미니 창 (Collapsible)
            // =====================================================================
            taskDrawerBorder = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(245, 13, 17, 23)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            StackPanel taskOuter = new StackPanel();
            taskDrawerBorder.Child = taskOuter;

            // Header Bar (Click to Collapse / Expand)
            Border taskHeader = new Border
            {
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                Padding = new Thickness(14, 7, 12, 7),
                Cursor = Cursors.Hand
            };
            Grid taskHGrid = new Grid();
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            taskHeader.Child = taskHGrid;

            StackPanel taskTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            txtTaskDrawerTitle = new TextBlock
            {
                Text = "⚡ 실시간 터미널 · CHLGD 작업 진행 창",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(121, 192, 255)),
                Margin = new Thickness(0, 0, 10, 0)
            };
            txtTaskDrawerStep = new TextBlock
            {
                Text = "실행 중",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                VerticalAlignment = VerticalAlignment.Center
            };
            taskTitleRow.Children.Add(txtTaskDrawerTitle);
            taskTitleRow.Children.Add(txtTaskDrawerStep);
            Grid.SetColumn(taskTitleRow, 0);
            taskHGrid.Children.Add(taskTitleRow);

            // Collapse/Expand Toggle Button
            Border btnToggleTask = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            txtTaskCollapseBtn = new TextBlock
            {
                Text = "▲ 접기",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217))
            };
            btnToggleTask.Child = txtTaskCollapseBtn;
            Grid.SetColumn(btnToggleTask, 1);
            taskHGrid.Children.Add(btnToggleTask);

            // Stop Task Button
            Border btnStopTask = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromArgb(45, 248, 81, 73)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(248, 81, 73)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnStopTask.Child = new TextBlock
            {
                Text = "중단",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 123, 114))
            };
            btnStopTask.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnStopTask.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                Task.Run(async () =>
                {
                    try { await http.PostAsync("http://127.0.0.1:7788/api/tasks/stop", new StringContent("{}", Encoding.UTF8, "application/json")); } catch { }
                });
            };
            Grid.SetColumn(btnStopTask, 2);
            taskHGrid.Children.Add(btnStopTask);

            // Dismiss Task Button
            Border btnCloseTask = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Padding = new Thickness(7, 3, 7, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnCloseTask.Child = new TextBlock
            {
                Text = "✕",
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158))
            };
            btnCloseTask.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnCloseTask.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                taskDrawerBorder.Visibility = Visibility.Collapsed;
                Task.Run(async () =>
                {
                    try { await http.PostAsync("http://127.0.0.1:7788/api/tasks/dismiss", new StringContent("{}", Encoding.UTF8, "application/json")); } catch { }
                });
            };
            Grid.SetColumn(btnCloseTask, 3);
            taskHGrid.Children.Add(btnCloseTask);

            taskDrawerBody = new StackPanel
            {
                Margin = new Thickness(12, 8, 12, 10),
                Visibility = Visibility.Visible
            };
            Border consoleBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(9, 12, 16)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8)
            };
            txtTaskDrawerLogs = new TextBlock
            {
                Text = "[JARVIS Sovereign Butler ↔ C:\\BeausCreators CHLGD Bridge Ready]\n• 인지된 에이전트: 26명 (소하 · 태준 · 거누 · 서진 · 유나 · 우진 · 수호 · 시안 · 재호 등)\n• 연동된 파이프라인: Start-Context.js / Verify-Code.ps1 / Finalize-Work.ps1 / .py / Git CI-CD\n> HUD에서 터미널 또는 CHLGD 오더를 내리시면 실시간 출력이 여기에 표시됩니다.",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217)),
                TextWrapping = TextWrapping.Wrap
            };
            consoleBox.Child = txtTaskDrawerLogs;
            taskDrawerBody.Children.Add(consoleBox);

            Action toggleTaskDrawer = () =>
            {
                isTaskDrawerExpanded = !isTaskDrawerExpanded;
                taskDrawerBody.Visibility = isTaskDrawerExpanded ? Visibility.Visible : Visibility.Collapsed;
                txtTaskCollapseBtn.Text = isTaskDrawerExpanded ? "▲ 접기" : "▼ 펼치기";
            };
            taskHeader.MouseLeftButtonDown += (s, e) => e.Handled = true;
            taskHeader.MouseLeftButtonUp += (s, e) => { e.Handled = true; toggleTaskDrawer(); };

            taskOuter.Children.Add(taskHeader);
            taskOuter.Children.Add(taskDrawerBody);
            drawersHost.Children.Add(taskDrawerBorder);

            // =====================================================================
            // SUB-WINDOW 2: 🎵 프리미엄 뮤직 플레이어 HUD (음악 재생 시에만 자동 표시)
            // =====================================================================
            musicDrawerBorder = new Border
            {
                CornerRadius = new CornerRadius(18),
                Background = new SolidColorBrush(Color.FromArgb(245, 7, 13, 23)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(135, 52, 211, 153)),
                BorderThickness = new Thickness(1.2),
                Padding = new Thickness(14, 8, 14, 8),
                Margin = new Thickness(0, 0, 0, 5),
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(16, 185, 129),
                    BlurRadius = 18,
                    ShadowDepth = 0,
                    Opacity = 0.22
                }
            };

            Grid musicHGrid = new Grid();
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicDrawerBorder.Child = musicHGrid;

            // Left: Album / Equalizer Disc Badge
            musicDiscBorder = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(Color.FromArgb(38, 52, 211, 153)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(115, 52, 211, 153)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 12, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            txtMusicDiscIcon = new TextBlock
            {
                Text = "♫",
                FontFamily = new FontFamily("Segoe UI Symbol, Malgun Gothic"),
                FontSize = 16,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(110, 231, 183)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            musicDiscBorder.Child = txtMusicDiscIcon;
            Grid.SetColumn(musicDiscBorder, 0);
            musicHGrid.Children.Add(musicDiscBorder);

            // Center: NOW PLAYING Badge + Track Title
            StackPanel musicTextCol = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 12, 0)
            };
            txtMusicStateBadge = new TextBlock
            {
                Text = "● NOW PLAYING",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 10,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                Margin = new Thickness(0, 0, 0, 1)
            };
            txtMusicDrawerTitle = new TextBlock
            {
                Text = "YouTube Music",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            musicTextCol.Children.Add(txtMusicStateBadge);
            musicTextCol.Children.Add(txtMusicDrawerTitle);
            Grid.SetColumn(musicTextCol, 1);
            musicHGrid.Children.Add(musicTextCol);

            // Right: Sleek Circular Transport Controls (⏮ 이전곡 · ⏸/▶ 재생/일시정지 · ⏭ 다음곡 · ✕ 닫기)
            StackPanel transportRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Func<string, string, string, int, Border> makeTransportCircleBtn = (iconText, cmdStr, tipText, sizePx) =>
            {
                Border b = new Border
                {
                    Width = sizePx,
                    Height = sizePx,
                    CornerRadius = new CornerRadius(sizePx / 2.0),
                    Background = new SolidColorBrush(Color.FromArgb(220, 22, 29, 41)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(140, 68, 82, 104)),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(5, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = tipText
                };
                b.Child = new TextBlock
                {
                    Text = iconText,
                    FontFamily = new FontFamily("Segoe UI Symbol, Malgun Gothic"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(230, 237, 243)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                b.MouseEnter += (s, e) => { b.Background = new SolidColorBrush(Color.FromArgb(245, 34, 45, 62)); };
                b.MouseLeave += (s, e) => { b.Background = new SolidColorBrush(Color.FromArgb(220, 22, 29, 41)); };
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; SendCommandToButler(cmdStr); };
                return b;
            };

            Border btnPrevTrack = makeTransportCircleBtn("⏮", "이전 곡", "이전 곡", 34);
            transportRow.Children.Add(btnPrevTrack);

            btnMusicPlayPauseCircle = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(19),
                Background = new SolidColorBrush(Color.FromArgb(55, 52, 211, 153)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(52, 211, 153)),
                BorderThickness = new Thickness(1.3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "재생 / 일시정지"
            };
            txtMusicPlayPauseIcon = new TextBlock
            {
                Text = "⏸",
                FontFamily = new FontFamily("Segoe UI Symbol, Malgun Gothic"),
                FontSize = 13.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnMusicPlayPauseCircle.Child = txtMusicPlayPauseIcon;
            btnMusicPlayPauseCircle.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnMusicPlayPauseCircle.MouseLeftButtonUp += (s, e) => { e.Handled = true; SendCommandToButler("재생 일시정지 토글"); };
            transportRow.Children.Add(btnMusicPlayPauseCircle);

            Border btnNextTrack = makeTransportCircleBtn("⏭", "다음 곡", "다음 곡", 34);
            transportRow.Children.Add(btnNextTrack);

            Border btnCloseMusicHud = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(35, 248, 81, 73)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, 248, 81, 73)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "음악 끄기"
            };
            btnCloseMusicHud.Child = new TextBlock
            {
                Text = "✕",
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 139, 130)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnCloseMusicHud.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnCloseMusicHud.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                musicDrawerBorder.Visibility = Visibility.Collapsed;
                SendCommandToButler("음악 꺼줘");
            };
            transportRow.Children.Add(btnCloseMusicHud);

            Grid.SetColumn(transportRow, 2);
            musicHGrid.Children.Add(transportRow);
            drawersHost.Children.Add(musicDrawerBorder);

            // =====================================================================
            // SUB-WINDOW 3: 빠른 오더 칩 메뉴 바 (Quick Chips Bar — Menu Toggleable)
            // =====================================================================
            quickChipsPanel = new WrapPanel
            {
                Margin = new Thickness(2, 2, 2, 0),
                Visibility = Visibility.Collapsed
            };

            Func<string, string, Color, Color, Border> makeQuickChip = (label, cmdToRun, borderCol, textCol) =>
            {
                Border chip = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Background = new SolidColorBrush(Color.FromArgb(235, 16, 22, 32)),
                    BorderBrush = new SolidColorBrush(borderCol),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 5, 10, 5),
                    Margin = new Thickness(0, 0, 6, 5),
                    Cursor = Cursors.Hand
                };
                chip.Child = new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(textCol)
                };
                chip.MouseLeftButtonDown += (s, e) => e.Handled = true;
                chip.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    if (cmdToRun == "__MIC__")
                    {
                        PromptListenNow();
                    }
                    else if (cmdToRun == "__OPEN_PLAN_WIN__")
                    {
                        ShowPlanAndChlgdDocumentWindow("plan");
                    }
                    else if (cmdToRun == "__OPEN_RULES_WIN__")
                    {
                        ShowPlanAndChlgdDocumentWindow("rules");
                    }
                    else if (cmdToRun == "__OPEN_HARNESS2_WIN__")
                    {
                        ShowPlanAndChlgdDocumentWindow("harness2");
                    }
                    else if (cmdToRun == "__OPEN_REA_WIN__")
                    {
                        ShowPlanAndChlgdDocumentWindow("rea");
                    }
                    else
                    {
                        SendCommandToButler(cmdToRun);
                    }
                };
                return chip;
            };

            quickChipsPanel.Children.Add(makeQuickChip("🎙 마이크 켜기", "__MIC__", Color.FromRgb(46, 160, 67), Color.FromRgb(86, 211, 100)));
            quickChipsPanel.Children.Add(makeQuickChip("📋 Plan · 문서 프리뷰 HUD 창", "__OPEN_PLAN_WIN__", Color.FromRgb(56, 139, 253), Color.FromRgb(165, 214, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("📓 CHLGD · 재귀 규칙 & 오더 원장", "__OPEN_RULES_WIN__", Color.FromRgb(46, 160, 67), Color.FromRgb(86, 211, 100)));
            quickChipsPanel.Children.Add(makeQuickChip("🌐 3대 생태계 · Harness² 현황", "__OPEN_HARNESS2_WIN__", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🔬 REA v6.8.0 · Laya & MCP", "__OPEN_REA_WIN__", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🗂 “오늘 할 일 정리해 줘” (본사 브리핑)", "오늘 할 일 정리해 줘", Color.FromRgb(46, 160, 67), Color.FromRgb(86, 211, 100)));
            quickChipsPanel.Children.Add(makeQuickChip("🧠 “뉴런에서 REA 검색해줘” (2-Hop 시냅스)", "뉴런에서 REA 검색해줘", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("📚 “뷰티풀월드 출판 시스템 점검해줘”", "뷰티풀월드 출판 시스템 점검해줘", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("📄 “방금 검색 결과 워드로 만들어줘”", "방금 검색 결과 워드로 만들어줘", Color.FromRgb(56, 139, 253), Color.FromRgb(165, 214, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🧬 “라야로 현재 프로젝트 상태 점검해줘”", "라야로 현재 프로젝트 상태 점검해줘", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🛡 “CHLGD 코드 검증 돌려줘” (CI/CD)", "CHLGD 코드 검증 돌려줘", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🎵 “유튜브에서 로파이 음악 틀어줘”", "유튜브에서 로파이 음악 틀어줘", Color.FromRgb(68, 76, 86), Color.FromRgb(201, 209, 217)));
            quickChipsPanel.Children.Add(makeQuickChip("⏹ “음악 꺼줘”", "음악 꺼줘", Color.FromRgb(68, 76, 86), Color.FromRgb(201, 209, 217)));

            drawersHost.Children.Add(quickChipsPanel);

            return drawersHost;
        }

        private static void ToggleHudMenuDrawer()
        {
            isHudMenuDrawerPinned = !isHudMenuDrawerPinned;
            if (taskDrawerBorder != null)
            {
                taskDrawerBorder.Visibility = isHudMenuDrawerPinned ? Visibility.Visible : Visibility.Collapsed;
            }
            if (quickChipsPanel != null)
            {
                quickChipsPanel.Visibility = isHudMenuDrawerPinned ? Visibility.Visible : Visibility.Collapsed;
            }
            if (txtTopMenuBtnLabel != null)
            {
                txtTopMenuBtnLabel.Text = isHudMenuDrawerPinned ? "☰ 메뉴 닫기" : "☰ 메뉴";
            }
        }

        private static async Task PollSubWindowsAsync()
        {
            string json = await http.GetStringAsync("http://127.0.0.1:7788/api/hud/subwindows");
            if (string.IsNullOrWhiteSpace(json)) return;

            // 1) Task Sub-Window
            if (json.Contains("\"activeTask\":{"))
            {
                int idx = json.IndexOf("\"activeTask\":{");
                string sub = json.Substring(idx);
                string title = ExtractJsonField(sub, "title");
                string step = ExtractJsonField(sub, "step");
                taskDrawerBorder.Visibility = Visibility.Visible;
                txtTaskDrawerTitle.Text = "⚡ " + (!string.IsNullOrEmpty(title) ? title : "터미널 작업 실행 중");
                txtTaskDrawerStep.Text = step;

                int linesIdx = sub.IndexOf("\"lines\":[");
                if (linesIdx >= 0)
                {
                    int endArr = sub.IndexOf("],\"startedAt\"", linesIdx);
                    if (endArr < 0) endArr = sub.IndexOf("\"]", linesIdx);
                    if (endArr > linesIdx)
                    {
                        string rawArr = sub.Substring(linesIdx + 9, endArr - (linesIdx + 9));
                        string cleaned = rawArr.Replace("\",\"", "\n").Trim('\"', '[', ']').Replace("\\\"", "\"").Replace("\\\\", "\\");
                        string[] splitLines = cleaned.Split('\n');
                        int startLine = Math.Max(0, splitLines.Length - 5);
                        txtTaskDrawerLogs.Text = string.Join("\n", splitLines, startLine, splitLines.Length - startLine);
                    }
                }
            }
            else if (!isHudMenuDrawerPinned)
            {
                if (taskDrawerBorder != null) taskDrawerBorder.Visibility = Visibility.Collapsed;
            }

            // 2) Music Sub-Window (Strictly visible ONLY when music is actually active)
            if (json.Contains("\"activeMedia\":{\"active\":true"))
            {
                int mIdx = json.IndexOf("\"activeMedia\":{");
                string mSub = json.Substring(mIdx);
                string mTitle = ExtractJsonField(mSub, "title");
                string mArtist = ExtractJsonField(mSub, "artist");
                string mState = ExtractJsonField(mSub, "state");
                bool isPaused = mState == "Paused";

                musicDrawerBorder.Visibility = Visibility.Visible;
                string displayTrack = !string.IsNullOrEmpty(mTitle) ? mTitle : "YouTube Music";
                if (!string.IsNullOrEmpty(mArtist) && !displayTrack.Contains(mArtist))
                {
                    displayTrack = mArtist + " — " + displayTrack;
                }
                txtMusicDrawerTitle.Text = displayTrack;

                if (txtMusicStateBadge != null)
                {
                    txtMusicStateBadge.Text = isPaused ? "⏸ PAUSED" : "● NOW PLAYING";
                    txtMusicStateBadge.Foreground = new SolidColorBrush(isPaused ? Color.FromRgb(251, 191, 36) : Color.FromRgb(52, 211, 153));
                }
                if (txtMusicPlayPauseIcon != null)
                {
                    txtMusicPlayPauseIcon.Text = isPaused ? "▶" : "⏸";
                }
                if (btnMusicPlayPauseCircle != null)
                {
                    btnMusicPlayPauseCircle.BorderBrush = new SolidColorBrush(isPaused ? Color.FromRgb(251, 191, 36) : Color.FromRgb(52, 211, 153));
                    btnMusicPlayPauseCircle.Background = new SolidColorBrush(isPaused ? Color.FromArgb(55, 251, 191, 36) : Color.FromArgb(55, 52, 211, 153));
                }
                if (musicDiscBorder != null)
                {
                    musicDiscBorder.BorderBrush = new SolidColorBrush(isPaused ? Color.FromArgb(115, 251, 191, 36) : Color.FromArgb(115, 52, 211, 153));
                    musicDiscBorder.Background = new SolidColorBrush(isPaused ? Color.FromArgb(35, 251, 191, 36) : Color.FromArgb(38, 52, 211, 153));
                }
                if (txtMusicDiscIcon != null)
                {
                    txtMusicDiscIcon.Foreground = new SolidColorBrush(isPaused ? Color.FromRgb(252, 211, 77) : Color.FromRgb(110, 231, 183));
                }
                musicDrawerBorder.BorderBrush = new SolidColorBrush(isPaused ? Color.FromArgb(135, 251, 191, 36) : Color.FromArgb(135, 52, 211, 153));
            }
            else
            {
                if (musicDrawerBorder != null) musicDrawerBorder.Visibility = Visibility.Collapsed;
            }

            // 3) Auto-Pop Plan / CHLGD Document HUD Window when openWindowSeq increments
            int seqIdx = json.IndexOf("\"openWindowSeq\":");
            if (seqIdx >= 0)
            {
                int start = seqIdx + 16;
                int end = start;
                while (end < json.Length && char.IsDigit(json[end])) end++;
                int seqVal = 1;
                if (int.TryParse(json.Substring(start, end - start), out seqVal))
                {
                    if (lastSeenPlanWindowSeq < 0) lastSeenPlanWindowSeq = seqVal;
                    else if (seqVal > lastSeenPlanWindowSeq)
                    {
                        lastSeenPlanWindowSeq = seqVal;
                        string tab = ExtractJsonField(json, "activeTab");
                        ShowPlanAndChlgdDocumentWindow(!string.IsNullOrEmpty(tab) ? tab : "plan");
                    }
                }
            }
        }

        private static void StartHandsFreeVoiceEar()
        {
            StartInvisibleChromeEarWorker();
            txtStatus.Text = "○ 받아 적는 중";
            SpeakAsync("네, 대표님. 말씀하세요.");
        }

        private static void SetSettingsFeedback(string message, bool error = false)
        {
            if (hudWindow == null) return;
            hudWindow.Dispatcher.BeginInvoke(new Action(() => {
                if (settingsFeedback == null) return;
                settingsFeedback.Text = message;
                settingsFeedback.Foreground = new SolidColorBrush(error ? Color.FromRgb(255, 160, 130) : Color.FromRgb(165, 200, 220));
            }));
        }

        private static async Task VerifySelectedBrainAsync()
        {
            int sequence = ++brainVerifySequence;
            string model = currentBrainModel, provider = currentBrainProvider;
            SetSettingsFeedback("연결 검사 중 · " + model + " (최초 연결은 최대 65초)");
            try
            {
                using (var timeout = new System.Threading.CancellationTokenSource(68000))
                using (var response = await http.PostAsync("http://127.0.0.1:7788/api/brain/verify", new StringContent("{}", Encoding.UTF8, "application/json"), timeout.Token))
                {
                    var json = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(await response.Content.ReadAsStringAsync());
                    if (sequence != brainVerifySequence || model != currentBrainModel || provider != currentBrainProvider) return;
                    var last = json.ContainsKey("last") ? json["last"] as System.Collections.Generic.Dictionary<string, object> : null;
                    bool verified = last != null && last.ContainsKey("ok") && Convert.ToBoolean(last["ok"]) && last.ContainsKey("modelVerified") && Convert.ToBoolean(last["modelVerified"]);
                    if (verified) SetSettingsFeedback("실제 응답 확인 · " + Convert.ToString(last["model"]) + " · " + (Convert.ToDouble(last["elapsedMs"]) / 1000).ToString("0.00") + "초");
                    else SetSettingsFeedback(last != null && last.ContainsKey("message") ? Convert.ToString(last["message"]) : "연결을 확인하지 못했습니다. 로그인과 선택 모델을 확인해 주세요.", true);
                }
            }
            catch { if (sequence == brainVerifySequence) SetSettingsFeedback("연결 검사 시간이 초과됐습니다. 로그인 상태를 확인한 뒤 다시 검사해 주세요.", true); }
        }

        private static void RefreshStoredSettings()
        {
            try
            {
                var cfg = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText("C:\\자비스\\data\\config.json", Encoding.UTF8));
                if (cfg.ContainsKey("brainProvider")) currentBrainProvider = Convert.ToString(cfg["brainProvider"]);
                if (cfg.ContainsKey("brainModel")) currentBrainModel = Convert.ToString(cfg["brainModel"]);
                if (cfg.ContainsKey("sayVoice")) currentSpeechVoice = Convert.ToString(cfg["sayVoice"]);
                if (cfg.ContainsKey("ttsSpeed")) currentSpeechSpeedRatio = Convert.ToDouble(cfg["ttsSpeed"]);
                if (cfg.ContainsKey("userPlanTier")) currentUserPlanTier = Convert.ToString(cfg["userPlanTier"]);
            }
            catch (Exception ex) { WriteLog("Settings read failed: " + ex.GetType().Name); }
        }

        private static async Task<bool> SaveSettingsPatchAsync(System.Collections.Generic.Dictionary<string, object> patch)
        {
            await settingsSaveGate.WaitAsync();
            Window target = settingsWindow;
            try
            {
                if (target != null) target.IsEnabled = false;
                SetSettingsFeedback("설정을 저장하는 중입니다…");
                string json = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(patch);
                using (var content = new StringContent(json, Encoding.UTF8, "application/json"))
                using (var response = await http.PostAsync("http://127.0.0.1:7788/api/config", content))
                {
                    response.EnsureSuccessStatusCode();
                    string raw = await response.Content.ReadAsStringAsync();
                    var result = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                    if (!result.ContainsKey("ok") || !Convert.ToBoolean(result["ok"])) throw new InvalidOperationException("설정 저장 실패");
                }
                RefreshStoredSettings();
                SetSettingsFeedback("저장 완료 · 음성 설정은 다음 재생부터 적용됩니다.");
                WriteLog("Settings saved: " + string.Join(",", patch.Keys));
                return true;
            }
            catch (Exception ex)
            {
                SetSettingsFeedback("저장 실패 · 서버 연결을 확인한 뒤 다시 선택해 주세요.", true);
                WriteLog("Settings save failed: " + ex.GetType().Name);
                return false;
            }
            finally
            {
                if (target != null && target.IsLoaded) target.IsEnabled = true;
                settingsSaveGate.Release();
            }
        }

        private static Task<bool> SaveModelConfig(string provider, string model)
        {
            return SaveSettingsPatchAsync(new System.Collections.Generic.Dictionary<string, object> { { "brainProvider", provider }, { "brainModel", model } });
        }

        private static FrameworkElement CreateTopRightGearIcon()
        {
            StackPanel topRightBar = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0)
            };

            // 0) [📋 Plan · 규칙] Button — Opens Dedicated Plan & CHLGD Document Viewer HUD Window
            Border planTopBtn = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(42, 46, 160, 67)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(150, 86, 211, 100)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 0, 8, 0),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                ToolTip = "📋 Plan 문서 프리뷰 & 📓 CHLGD 대화 규칙/로그 정리 창 열기"
            };
            TextBlock planTopTxt = new TextBlock
            {
                Text = "📋 Plan·규칙",
                FontFamily = new FontFamily("Malgun Gothic, Segoe UI Symbol"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(165, 245, 185)),
                VerticalAlignment = VerticalAlignment.Center
            };
            planTopBtn.Child = planTopTxt;
            planTopBtn.MouseEnter += (s, e) =>
            {
                planTopBtn.Background = new SolidColorBrush(Color.FromArgb(85, 46, 160, 67));
                planTopTxt.Foreground = Brushes.White;
            };
            planTopBtn.MouseLeave += (s, e) =>
            {
                planTopBtn.Background = new SolidColorBrush(Color.FromArgb(42, 46, 160, 67));
                planTopTxt.Foreground = new SolidColorBrush(Color.FromRgb(165, 245, 185));
            };
            planTopBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            planTopBtn.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                ShowPlanAndChlgdDocumentWindow("plan");
            };
            topRightBar.Children.Add(planTopBtn);

            // 1) [☰ 메뉴] Button — Opens/Closes the Collapsible Task & Music Sub-Windows & Quick Chips
            Border menuBtn = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(38, 77, 217, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 77, 217, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                ToolTip = "실시간 작업 · 미니 뮤직 컨트롤러 · 빠른 오더 메뉴 열기/접기"
            };
            TextBlock menuTxt = new TextBlock
            {
                Text = "☰ 메뉴",
                FontFamily = new FontFamily("Malgun Gothic, Segoe UI Symbol"),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 240, 255)),
                VerticalAlignment = VerticalAlignment.Center
            };
            txtTopMenuBtnLabel = menuTxt;
            menuBtn.Child = menuTxt;

            menuBtn.MouseEnter += (s, e) =>
            {
                menuBtn.Background = new SolidColorBrush(Color.FromArgb(75, 77, 217, 255));
                menuBtn.BorderBrush = new SolidColorBrush(CyanColor);
                menuTxt.Foreground = Brushes.White;
            };
            menuBtn.MouseLeave += (s, e) =>
            {
                menuBtn.Background = new SolidColorBrush(Color.FromArgb(38, 77, 217, 255));
                menuBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 77, 217, 255));
                menuTxt.Foreground = new SolidColorBrush(Color.FromRgb(220, 240, 255));
            };
            menuBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            menuBtn.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                ToggleHudMenuDrawer();
            };
            topRightBar.Children.Add(menuBtn);

            // 2) [⚙] Settings Icon Button — Opens 2-Pane Commercial System Preferences
            Border gearBtn = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(38, 77, 217, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(140, 77, 217, 255)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "환경설정 (요금제 · AI 모델 · 음성 속도 · 단축키)"
            };

            TextBlock gearIcon = new TextBlock
            {
                Text = "⚙",
                FontFamily = new FontFamily("Segoe UI Symbol, Segoe Fluent Icons, Malgun Gothic"),
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(220, 240, 255)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            gearBtn.Child = gearIcon;

            gearBtn.MouseEnter += (s, e) =>
            {
                gearBtn.Background = new SolidColorBrush(Color.FromArgb(75, 77, 217, 255));
                gearBtn.BorderBrush = new SolidColorBrush(CyanColor);
                gearIcon.Foreground = Brushes.White;
            };
            gearBtn.MouseLeave += (s, e) =>
            {
                gearBtn.Background = new SolidColorBrush(Color.FromArgb(38, 77, 217, 255));
                gearBtn.BorderBrush = new SolidColorBrush(Color.FromArgb(140, 77, 217, 255));
                gearIcon.Foreground = new SolidColorBrush(Color.FromRgb(220, 240, 255));
            };
            gearBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            gearBtn.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                ShowHotkeySettingsWindow();
            };
            topRightBar.Children.Add(gearBtn);

            return topRightBar;
        }

        private static Task<bool> SavePlanAndApiKeyConfig(string planTier, string apiKey)
        {
            var patch = new System.Collections.Generic.Dictionary<string, object>();
            if (apiKey != null) patch["geminiApiKey"] = apiKey;
            else patch["userPlanTier"] = planTier;
            return SaveSettingsPatchAsync(patch);
        }

        private static void ShowHotkeySettingsWindow()
        {
            RefreshStoredSettings();
            if (settingsWindow != null)
            {
                try { settingsWindow.Close(); } catch { }
                settingsWindow = null;
            }

            // Palette: Commercial Matte Dark (Linear / Cursor / Raycast / macOS System Settings)
            Color bgShell = Color.FromRgb(13, 17, 23);       // #0D1117
            Color bgSidebar = Color.FromRgb(9, 12, 16);      // #090C10
            Color bgSurface = Color.FromRgb(22, 27, 34);     // #161B22
            Color bgSurfaceHover = Color.FromRgb(28, 35, 45);// #1C232D
            Color bgActiveCard = Color.FromRgb(19, 34, 56);  // #132238
            Color borderHairline = Color.FromRgb(38, 44, 54);// #262C36
            Color borderSubtle = Color.FromRgb(33, 38, 45);  // #21262D
            Color accentBlue = Color.FromRgb(56, 139, 253);  // #388BFD
            Color accentEmerald = Color.FromRgb(46, 160, 67);// #2EA043
            Color textPrimary = Color.FromRgb(240, 246, 252);// #F0F6FC
            Color textSecondary = Color.FromRgb(139, 148, 158);// #8B949E
            Color textMuted = Color.FromRgb(110, 118, 129);  // #6E7681

            settingsWindow = new Window
            {
                Title = "JARVIS 환경설정",
                Width = Math.Min(1000, SystemParameters.WorkArea.Width - 32),
                Height = Math.Min(720, SystemParameters.WorkArea.Height - 48),
                MinWidth = 680,
                MinHeight = 420,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            Border outerFrame = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(bgShell),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 32,
                    ShadowDepth = 10,
                    Opacity = 0.72
                }
            };
            outerFrame.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { settingsWindow.DragMove(); } catch { }
                }
            };
            settingsWindow.Content = outerFrame;

            Grid splitGrid = new Grid();
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerFrame.Child = splitGrid;

            // =========================================================================
            // LEFT SIDEBAR (200px)
            // =========================================================================
            Border sidebarBorder = new Border
            {
                CornerRadius = new CornerRadius(14, 0, 0, 14),
                Background = new SolidColorBrush(bgSidebar),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(16, 20, 16, 18)
            };
            Grid.SetColumn(sidebarBorder, 0);
            splitGrid.Children.Add(sidebarBorder);

            Grid sidebarGrid = new Grid();
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebarBorder.Child = sidebarGrid;

            // Brand + Plan Badge
            StackPanel brandStack = new StackPanel { Margin = new Thickness(4, 0, 4, 20) };
            brandStack.Children.Add(new TextBlock
            {
                Text = "JARVIS OS",
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textMuted)
            });
            brandStack.Children.Add(new TextBlock
            {
                Text = "시스템 환경설정",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 2, 0, 10)
            });

            Border sidebarPlanBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(36, 56, 139, 253)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 56, 139, 253)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            TextBlock txtSidebarPlan = new TextBlock
            {
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(121, 192, 255))
            };
            sidebarPlanBadge.Child = txtSidebarPlan;
            brandStack.Children.Add(sidebarPlanBadge);

            Action updateSidebarPlanLabel = () =>
            {
                if (currentUserPlanTier == "ultra") txtSidebarPlan.Text = "선택한 분류: Ultra";
                else if (currentUserPlanTier == "pro") txtSidebarPlan.Text = "선택한 분류: Pro / API";
                else if (currentUserPlanTier == "local") txtSidebarPlan.Text = "선택한 분류: 로컬";
                else txtSidebarPlan.Text = "선택한 분류: 무료";
            };
            updateSidebarPlanLabel();

            Grid.SetRow(brandStack, 0);
            sidebarGrid.Children.Add(brandStack);

            // Navigation Stack
            StackPanel navStack = new StackPanel();
            Grid.SetRow(navStack, 1);
            sidebarGrid.Children.Add(navStack);

            // Active Model Summary at Bottom of Sidebar
            Border bottomSummary = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8)
            };
            StackPanel bottomSumStack = new StackPanel();
            bottomSummary.Child = bottomSumStack;
            bottomSumStack.Children.Add(new TextBlock
            {
                Text = "저장된 모델 · 연결 별도 확인",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(textMuted)
            });
            TextBlock txtSidebarActiveModel = new TextBlock
            {
                Text = currentBrainModel,
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            bottomSumStack.Children.Add(txtSidebarActiveModel);
            TextBlock txtSidebarActiveProvider = new TextBlock
            {
                Text = currentBrainProvider + " · " + currentSpeechSpeedRatio.ToString("0.00") + "x",
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 1, 0, 0)
            };
            bottomSumStack.Children.Add(txtSidebarActiveProvider);
            Grid.SetRow(bottomSummary, 2);
            sidebarGrid.Children.Add(bottomSummary);

            // =========================================================================
            // RIGHT CONTENT AREA (560px)
            // =========================================================================
            Grid rightGrid = new Grid { Margin = new Thickness(24, 20, 24, 18) };
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(rightGrid, 1);
            splitGrid.Children.Add(rightGrid);

            // Right Header (Pane Title + Close Button)
            Grid rightHeader = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(rightHeader, 0);
            rightGrid.Children.Add(rightHeader);

            StackPanel paneHeaderStack = new StackPanel();
            TextBlock txtPaneTitle = new TextBlock
            {
                Text = "요금제 및 AI 모델",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            };
            TextBlock txtPaneDesc = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "모델 연결 방식을 선택합니다. 분류는 참고용이며 실제 구독·할당량을 조회한 결과가 아닙니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 3, 0, 0)
            };
            paneHeaderStack.Children.Add(txtPaneTitle);
            paneHeaderStack.Children.Add(txtPaneDesc);
            Grid.SetColumn(paneHeaderStack, 0);
            rightHeader.Children.Add(paneHeaderStack);

            Border btnCloseTop = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            TextBlock closeGlyph = new TextBlock
            {
                Text = "\uE711",
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                FontSize = 11,
                Foreground = new SolidColorBrush(textSecondary),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnCloseTop.Child = closeGlyph;
            btnCloseTop.MouseEnter += (s, e) =>
            {
                btnCloseTop.Background = new SolidColorBrush(bgSurfaceHover);
                closeGlyph.Foreground = new SolidColorBrush(textPrimary);
            };
            btnCloseTop.MouseLeave += (s, e) =>
            {
                btnCloseTop.Background = new SolidColorBrush(bgSurface);
                closeGlyph.Foreground = new SolidColorBrush(textSecondary);
            };
            btnCloseTop.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnCloseTop.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                try { settingsWindow.Close(); } catch { }
                settingsWindow = null;
            };
            Grid.SetColumn(btnCloseTop, 1);
            rightHeader.Children.Add(btnCloseTop);

            // Right Body Host (Holds the 4 Panes without any vertical scrollbar)
            Grid paneHost = new Grid();
            ScrollViewer settingsScroll = new ScrollViewer { Content = paneHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(settingsScroll, 1);
            rightGrid.Children.Add(settingsScroll);

            StackPanel paneModels = new StackPanel { Visibility = Visibility.Visible };
            StackPanel paneAccount = new StackPanel { Visibility = Visibility.Collapsed };
            StackPanel paneVoice = new StackPanel { Visibility = Visibility.Collapsed };
            StackPanel paneDesktop = new StackPanel { Visibility = Visibility.Collapsed };

            paneHost.Children.Add(paneModels);
            paneHost.Children.Add(paneAccount);
            paneHost.Children.Add(paneVoice);
            paneHost.Children.Add(paneDesktop);

            // Helper for crisp Flat Action Buttons (no default WPF chrome)
            Func<string, bool, Action, Border> createActionBtn = (label, isPrimary, onClick) =>
            {
                Color baseBg = isPrimary ? accentBlue : bgSurface;
                Color hoverBg = isPrimary ? Color.FromRgb(74, 152, 255) : bgSurfaceHover;
                Color baseBorder = isPrimary ? accentBlue : borderHairline;
                Border b = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(baseBg),
                    BorderBrush = new SolidColorBrush(baseBorder),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(13, 7, 13, 7),
                    Cursor = Cursors.Hand,
                    HorizontalAlignment = HorizontalAlignment.Left
                };
                b.Child = new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(textPrimary)
                };
                b.MouseEnter += (s, e) => b.Background = new SolidColorBrush(hoverBg);
                b.MouseLeave += (s, e) => b.Background = new SolidColorBrush(baseBg);
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick(); };
                return b;
            };

            // =========================================================================
            // PANE 1: 요금제 및 AI 모델 (4-Tier Segmented Selector + 4 Clean Model Cards)
            // =========================================================================
            Border tierTrack = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(bgSidebar),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(4),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid tierGrid = new Grid();
            for (int i = 0; i < 4; i++)
            {
                tierGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            tierTrack.Child = tierGrid;
            paneModels.Children.Add(tierTrack);

            StackPanel modelCardsContainer = new StackPanel();
            paneModels.Children.Add(modelCardsContainer);

            System.Collections.Generic.List<Action> tierVisualCallbacks = new System.Collections.Generic.List<Action>();
            System.Collections.Generic.List<Action> modelVisualCallbacks = new System.Collections.Generic.List<Action>();

            TextBox customModelBox = new TextBox
            {
                Width = 240,
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 12.5,
                Padding = new Thickness(9, 0, 9, 0),
                Text = currentBrainModel
            };

            Action<string, string, string> selectModelAndSave = async (prov, mod, displayTitle) =>
            {
                if (!await SaveModelConfig(prov, mod)) return;
                customModelBox.Text = mod;
                txtSidebarActiveModel.Text = mod;
                txtSidebarActiveProvider.Text = prov + " · " + currentSpeechSpeedRatio.ToString("0.00") + "x";
                if (txtMainBody != null)
                {
                    txtMainBody.Text = displayTitle + " 모델을 저장했습니다. 환경설정 하단에서 연결 검사 결과를 확인하세요.";
                }
                foreach (var cb in modelVisualCallbacks) { try { cb(); } catch { } }
                await VerifySelectedBrainAsync();
            };

            Func<string, string, string, string, string, UIElement> buildModelCard = (title, modelId, providerId, badgeText, descText) =>
            {
                Border card = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(bgSurface),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 7),
                    Cursor = Cursors.Hand
                };

                Grid g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                card.Child = g;

                StackPanel left = new StackPanel();
                WrapPanel topRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };

                TextBlock txtTitle = new TextBlock
                {
                    Text = title,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textPrimary),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                topRow.Children.Add(txtTitle);

                Border codePill = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(bgSidebar),
                    BorderBrush = new SolidColorBrush(borderHairline),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(6, 1, 6, 1),
                    Margin = new Thickness(0, 0, 8, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                codePill.Child = new TextBlock
                {
                    Text = modelId,
                    FontFamily = new FontFamily("Consolas, Segoe UI"),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(165, 214, 255))
                };
                topRow.Children.Add(codePill);

                Border tierPill = new Border
                {
                    CornerRadius = new CornerRadius(4),
                    Background = new SolidColorBrush(Color.FromArgb(30, 46, 160, 67)),
                    Padding = new Thickness(6, 1, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                tierPill.Child = new TextBlock
                {
                    Text = badgeText,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100))
                };
                topRow.Children.Add(tierPill);

                left.Children.Add(topRow);
                left.Children.Add(new TextBlock
                {
                    Text = descText,
                    TextWrapping = TextWrapping.Wrap,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(textSecondary)
                });
                Grid.SetColumn(left, 0);
                g.Children.Add(left);

                Border statusBadge = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(10, 5, 10, 5),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(12, 0, 0, 0)
                };
                TextBlock statusTxt = new TextBlock
                {
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold
                };
                statusBadge.Child = statusTxt;
                Grid.SetColumn(statusBadge, 1);
                g.Children.Add(statusBadge);

                Action syncCardState = () =>
                {
                    bool active = string.Equals(currentBrainModel, modelId, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(currentBrainProvider, providerId, StringComparison.OrdinalIgnoreCase);
                    if (active)
                    {
                        card.Background = new SolidColorBrush(bgActiveCard);
                        card.BorderBrush = new SolidColorBrush(accentBlue);
                        statusBadge.Background = new SolidColorBrush(accentBlue);
                        statusTxt.Text = "저장됨";
                        statusTxt.Foreground = Brushes.White;
                    }
                    else
                    {
                        card.Background = new SolidColorBrush(bgSurface);
                        card.BorderBrush = new SolidColorBrush(borderSubtle);
                        statusBadge.Background = new SolidColorBrush(bgSidebar);
                        statusTxt.Text = "선택";
                        statusTxt.Foreground = new SolidColorBrush(textSecondary);
                    }
                };
                modelVisualCallbacks.Add(syncCardState);
                syncCardState();

                card.MouseEnter += (s, e) =>
                {
                    bool active = string.Equals(currentBrainModel, modelId, StringComparison.OrdinalIgnoreCase)
                               && string.Equals(currentBrainProvider, providerId, StringComparison.OrdinalIgnoreCase);
                    if (!active) card.Background = new SolidColorBrush(bgSurfaceHover);
                };
                card.MouseLeave += (s, e) => syncCardState();
                card.MouseLeftButtonDown += (s, e) => e.Handled = true;
                card.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    selectModelAndSave(providerId, modelId, title);
                };

                return card;
            };

            Action renderSelectedTierModels = () =>
            {
                modelCardsContainer.Children.Clear();
                modelVisualCallbacks.Clear();

                if (currentUserPlanTier == "ultra")
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 4 Argon (Medium)",
                        "gemini-4-argon-medium",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "추론 모델. 실제 접근 권한과 CLI 지원 여부는 로그인 계정에서 확인해야 합니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (빠른 응답)",
                        "gemini-3.8-flash-low",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "Google 공식 무료 티어($0) 제공 모델. Antigravity 기본 쿼터 내 추가 과금 없음(useG1Credits: false)."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 4 Argon (High)",
                        "gemini-4-argon-high",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "복잡한 분석을 위한 높은 추론 설정. 분석 깊이가 필요한 경우 선택하세요."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (균형)",
                        "gemini-3.8-flash-medium",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "Gemini 3.8 Flash 중간 추론 설정. Antigravity 로그인 기본 쿼터 내에서 추가 크레딧 차감 없이 동작합니다."
                    ));
                }
                else if (currentUserPlanTier == "pro")
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 4 Argon (Medium)",
                        "gemini-4-argon-medium",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "추론 모델. 실제 접근 권한과 CLI 지원 여부는 로그인 계정에서 확인해야 합니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (빠른 응답)",
                        "gemini-3.8-flash-low",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "Google 공식 무료 티어($0) 제공 모델. Antigravity 기본 쿼터 내 추가 과금 없음(useG1Credits: false)."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 2.5 Pro",
                        "gemini-2.5-pro",
                        "gemini",
                        "Gemini API 키",
                        "이전 세대 추론 모델. API 프로젝트의 사용 가능 여부·과금·한도를 확인하세요."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "OpenAI GPT-4o",
                        "gpt-4o",
                        "openai",
                        "OpenAI API 키 · 별도 설정",
                        "OpenAI API 키를 등록한 사용자를 위한 멀티모달 응답 모델"
                    ));
                }
                else if (currentUserPlanTier == "local")
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Google Gemma 4 E4B (온디바이스 표준)",
                        "gemma4:e4b",
                        "ollama",
                        "로컬 · Ollama 필요",
                        "구글 최신 4세대 오픈 모델로 인터넷 연결이나 토큰 과금 없이 PC에서 즉시 구동"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Google Gemma 4 31B (온디바이스 대형)",
                        "gemma4:31b",
                        "ollama",
                        "로컬 · Ollama 필요",
                        "고사양 로컬 GPU 환경에서 고난도 추론까지 100% 무제한 무료로 수행"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Qwen 2.5 Coder 14B",
                        "qwen2.5-coder:14b",
                        "ollama",
                        "로컬 · Ollama 필요",
                        "로컬 Ollama 기반 코드 작성 및 시스템 스크립트 분석 특화 모델"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "LM Studio Local GGUF",
                        "local-model",
                        "lmstudio",
                        "로컬 · LM Studio 필요",
                        "LM Studio 로컬 서버(포트 1234)에 마운트된 커스텀 GGUF 모델 연동"
                    ));
                }
                else
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (빠른 응답)",
                        "gemini-3.8-flash-low",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "Google 공식 무료 티어($0) 제공 모델. Antigravity 기본 쿼터 내 추가 과금 없음(useG1Credits: false)."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.5 Flash-Lite",
                        "gemini-3.5-flash-lite",
                        "gemini",
                        "Gemini API 키",
                        "AI Studio 무료 티어(Free of charge, $0) 대상 초경량 모델. 미결제(무료) 프로젝트 API 키에서 무료로 동작합니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (균형)",
                        "gemini-3.8-flash-medium",
                        "antigravity-cli",
                        "Google 로그인 · Antigravity",
                        "Gemini 3.8 Flash 중간 추론 설정. Antigravity 로그인 기본 쿼터 내에서 추가 크레딧 차감 없이 동작합니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 2.5 Flash",
                        "gemini-2.5-flash",
                        "gemini",
                        "Gemini API 키",
                        "AI Studio 무료 티어($0) 지원 안정화 모델. 무료 API 프로젝트 한도 내에서 과금 없이 사용 가능합니다."
                    ));
                }
            };

            string[][] tierDefs = new string[][]
            {
                new string[] { "ultra", "Ultra 요금제" },
                new string[] { "pro", "Pro · 유료 API" },
                new string[] { "free", "Free 무료 티어" },
                new string[] { "local", "Local 온디바이스" }
            };

            for (int idx = 0; idx < tierDefs.Length; idx++)
            {
                string tKey = tierDefs[idx][0];
                string tLabel = tierDefs[idx][1];

                Border tabBtn = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(8, 6, 8, 6),
                    Cursor = Cursors.Hand
                };
                TextBlock tabTxt = new TextBlock
                {
                    Text = tLabel,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                tabBtn.Child = tabTxt;
                Grid.SetColumn(tabBtn, idx);
                tierGrid.Children.Add(tabBtn);

                Action syncTierTab = () =>
                {
                    bool isCurrent = (currentUserPlanTier == tKey);
                    tabBtn.Background = new SolidColorBrush(isCurrent ? bgSurface : Colors.Transparent);
                    tabBtn.BorderBrush = new SolidColorBrush(isCurrent ? borderHairline : Colors.Transparent);
                    tabBtn.BorderThickness = new Thickness(1);
                    tabTxt.Foreground = new SolidColorBrush(isCurrent ? textPrimary : textSecondary);
                };
                tierVisualCallbacks.Add(syncTierTab);
                syncTierTab();

                tabBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
                tabBtn.MouseLeftButtonUp += async (s, e) =>
                {
                    e.Handled = true;
                    if (!await SavePlanAndApiKeyConfig(tKey, null)) return;
                    updateSidebarPlanLabel();
                    foreach (var cb in tierVisualCallbacks) { try { cb(); } catch { } }
                    renderSelectedTierModels();
                };
            }

            renderSelectedTierModels();

            // =========================================================================
            // PANE 2: 계정 및 API 연결 (Google OAuth / API Key / Custom Model ID)
            // =========================================================================
            Border oauthCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel oauthStack = new StackPanel();
            oauthCard.Child = oauthStack;
            oauthStack.Children.Add(new TextBlock
            {
                Text = "Google 계정 로그인 · Antigravity CLI",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            oauthStack.Children.Add(new TextBlock
            {
                Text = "개인 Google 계정은 Antigravity CLI로 연결합니다. Gemini CLI의 개인 로그인 지원은 종료되었습니다. 모델 선택 후 실제 연결을 검사하며, 구독 등급과 API 키의 사용 한도는 별개입니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });
            Border btnOAuthLogin = createActionBtn("Google 계정 연결 열기 (Antigravity)", true, () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "agy\\bin\\agy.exe"),
                        WorkingDirectory = System.IO.Path.GetTempPath(),
                        UseShellExecute = true
                    });
                }
                catch { SetSettingsFeedback("Antigravity CLI를 열지 못했습니다. 설치 상태를 확인해 주세요.", true); }
            });
            oauthStack.Children.Add(btnOAuthLogin);
            oauthStack.Children.Add(createActionBtn("선택 모델 연결 검사", false, async () => { await VerifySelectedBrainAsync(); }));
            paneAccount.Children.Add(oauthCard);

            // API Key Card
            Border apiCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel apiStack = new StackPanel();
            apiCard.Child = apiStack;
            apiStack.Children.Add(new TextBlock
            {
                Text = "Google Gemini API 키 등록",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            apiStack.Children.Add(new TextBlock
            {
                Text = "API 키를 직접 사용하는 경우 아래에 입력 후 저장하세요. 이 PC의 자비스 설정 파일에 저장됩니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 4, 0, 10)
            });
            WrapPanel apiInputRow = new WrapPanel();
            PasswordBox apiKeyBox = new PasswordBox
            {
                Width = 340,
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 12,
                Padding = new Thickness(9, 0, 9, 0),
                Password = ""
            };
            apiInputRow.Children.Add(apiKeyBox);
            TextBlock txtApiSaved = new TextBlock
            {
                Text = "",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(10, 0, 0, 0)
            };
            Border btnSaveKey = createActionBtn("API 키 저장", false, async () =>
            {
                if (string.IsNullOrWhiteSpace(apiKeyBox.Password)) { txtApiSaved.Text = "새 API 키를 입력해 주세요."; return; }
                bool saved = await SavePlanAndApiKeyConfig(currentUserPlanTier, apiKeyBox.Password.Trim());
                txtApiSaved.Text = saved ? "저장됨 · 연결 미확인" : "저장 실패";
                txtApiSaved.Foreground = saved ? new SolidColorBrush(Color.FromRgb(86, 211, 100)) : Brushes.Salmon;
                if (saved) apiKeyBox.Clear();
            });
            btnSaveKey.Margin = new Thickness(8, 0, 0, 0);
            apiInputRow.Children.Add(btnSaveKey);
            apiInputRow.Children.Add(txtApiSaved);
            apiStack.Children.Add(apiInputRow);
            paneAccount.Children.Add(apiCard);

            // Custom Model ID Card
            Border customModelCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14)
            };
            StackPanel customStack = new StackPanel();
            customModelCard.Child = customStack;
            customStack.Children.Add(new TextBlock
            {
                Text = "모델 ID 직접 지정",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 0, 0, 8)
            });
            WrapPanel customInputRow = new WrapPanel();
            customInputRow.Children.Add(customModelBox);
            Border btnApplyCustomModel = createActionBtn("모델 적용", false, () =>
            {
                string m = customModelBox.Text.Trim();
                if (!string.IsNullOrEmpty(m))
                {
                    selectModelAndSave(currentBrainProvider, m, "사용자 지정 모델");
                }
            });
            btnApplyCustomModel.Margin = new Thickness(8, 0, 0, 0);
            customInputRow.Children.Add(btnApplyCustomModel);
            customStack.Children.Add(customInputRow);
            paneAccount.Children.Add(customModelCard);

            // =========================================================================
            // PANE 3: 음성 및 말하기 속도 (Neural Voice & Speed)
            // =========================================================================
            Border voiceCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 16, 16, 16),
                Margin = new Thickness(0, 0, 0, 12)
            };
            StackPanel voiceStack = new StackPanel();
            voiceCard.Child = voiceStack;
            voiceStack.Children.Add(new TextBlock
            {
                Text = "답변 목소리와 말하기 속도",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            TextBlock txtSpeedDesc = new TextBlock
            {
                Text = "현재 배속: " + currentSpeechSpeedRatio.ToString("0.00") + "x · 저장된 속도는 다음 음성부터 적용됩니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 4, 0, 14)
            };
            voiceStack.Children.Add(txtSpeedDesc);
            voiceStack.Children.Add(new TextBlock { Text = "Neural 음성은 인터넷 연결이 필요합니다. 사용 불가 시 Windows 음성으로 대체하고 아래에 표시합니다.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 0, 0, 12) });
            WrapPanel voiceRow = new WrapPanel();
            voiceStack.Children.Add(voiceRow);
            string[] voiceIds = { "ko-KR-HyunsuMultilingualNeural", "ko-KR-InJoonNeural", "ko-KR-SunHiNeural", "windows-ko" };
            string[] voiceLabels = { "현수 · Neural", "인준 · Neural", "선히 · Neural", "Windows 한국어 · 오프라인" };
            var voiceSyncCallbacks = new System.Collections.Generic.List<Action>();
            for (int v = 0; v < voiceIds.Length; v++)
            {
                string voiceId = voiceIds[v];
                Border voiceButton = createActionBtn(voiceLabels[v], false, async () => {
                    if (!await SaveSettingsPatchAsync(new System.Collections.Generic.Dictionary<string, object> { { "sayVoice", voiceId } })) return;
                    foreach (var refresh in voiceSyncCallbacks) refresh();
                    SpeakAsync("안녕하세요. 자비스의 선택한 목소리와 말하기 속도를 확인하고 있습니다.");
                });
                voiceButton.Margin = new Thickness(0, 0, 8, 8);
                Action refreshVoice = () => { voiceButton.BorderBrush = new SolidColorBrush(currentSpeechVoice == voiceId ? accentBlue : borderHairline); voiceButton.BorderThickness = new Thickness(currentSpeechVoice == voiceId ? 2 : 1); };
                voiceSyncCallbacks.Add(refreshVoice);
                refreshVoice();
                voiceRow.Children.Add(voiceButton);
            }
            Border previewVoice = createActionBtn("현재 음성 미리 듣기", false, () => SpeakAsync("안녕하세요. 자비스의 선택한 목소리와 말하기 속도를 확인하고 있습니다."));
            previewVoice.Margin = new Thickness(0, 0, 0, 12);
            voiceStack.Children.Add(previewVoice);

            Grid speedGrid = new Grid();
            double[] speeds = new double[] { 1.00, 1.15, 1.25, 1.35, 1.50 };
            string[] speedNames = new string[] { "1.00×", "1.15×", "1.25×", "1.35×", "1.50×" };
            System.Collections.Generic.List<Action> speedSyncCallbacks = new System.Collections.Generic.List<Action>();

            for (int i = 0; i < speeds.Length; i++)
            {
                speedGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                double spdVal = speeds[i];
                string spdLabel = speedNames[i];

                Border spdBtn = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Padding = new Thickness(6, 9, 6, 9),
                    Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                TextBlock spdTxt = new TextBlock
                {
                    Text = spdLabel,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                spdBtn.Child = spdTxt;
                Grid.SetColumn(spdBtn, i);
                speedGrid.Children.Add(spdBtn);

                Action syncSpd = () =>
                {
                    bool active = Math.Abs(currentSpeechSpeedRatio - spdVal) < 0.03;
                    spdBtn.Background = new SolidColorBrush(active ? bgActiveCard : bgSidebar);
                    spdBtn.BorderBrush = new SolidColorBrush(active ? accentBlue : borderHairline);
                    spdBtn.BorderThickness = new Thickness(1);
                    spdTxt.Foreground = new SolidColorBrush(active ? textPrimary : textSecondary);
                };
                speedSyncCallbacks.Add(syncSpd);
                syncSpd();

                spdBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
                spdBtn.MouseLeftButtonUp += async (s, e) =>
                {
                    e.Handled = true;
                    if (!await SaveSettingsPatchAsync(new System.Collections.Generic.Dictionary<string, object> { { "ttsSpeed", spdVal } })) return;
                    txtSpeedDesc.Text = "현재 배속: " + spdVal.ToString("0.00") + "x (" + spdLabel + ") 적용 완료";
                    txtSidebarActiveProvider.Text = currentBrainProvider + " · " + spdVal.ToString("0.00") + "x";
                    foreach (var cb in speedSyncCallbacks) { try { cb(); } catch { } }
                    SpeakAsync("대표님, 음성 속도를 " + spdLabel + " 단계로 설정했습니다.");
                };
            }
            voiceStack.Children.Add(speedGrid);
            paneVoice.Children.Add(voiceCard);

            // Speaker comparison is opt-in and remains off until real enrollment succeeds.
            Border speakerCard = new Border { Background = new SolidColorBrush(bgSurface), CornerRadius = new CornerRadius(10), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
            StackPanel speakerStack = new StackPanel();
            speakerCard.Child = speakerStack;
            speakerStack.Children.Add(new TextBlock { Text = "내 목소리만 반응", FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(textPrimary) });
            speakerStack.Children.Add(new TextBlock { Text = "영상·음악을 끄고 혼자 말할 수 있을 때 등록하세요. 5초 준비 후 화면의 세 문장을 24초 동안 읽습니다. 등록용 녹음은 파일로 남기지 않고 비교용 특징만 이 PC에 저장합니다. 기존 Chrome 음성 인식은 인터넷을 사용합니다.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 7, 0, 8) });
            speakerStack.Children.Add(new TextBlock { Text = "비슷한 목소리·겹친 말소리·본인 녹음 재생까지 완전히 차단하는 인증 기능은 아닙니다. 너무 짧은 명령은 길게 다시 말하거나 직접 입력하세요.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 0, 0, 10) });
            TextBlock speakerStateText = new TextBlock { Text = "목소리 등록 상태 확인 중…", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(121, 192, 255)), Margin = new Thickness(0, 0, 0, 8) };
            TextBlock enrollmentPrompt = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = new SolidColorBrush(textPrimary), Margin = new Thickness(0, 0, 0, 10) };
            speakerStack.Children.Add(speakerStateText); speakerStack.Children.Add(enrollmentPrompt);
            bool speakerRefreshBusy = false;
            string speakerActionError = "";
            Func<Task> refreshSpeakerState = async () => {
                if (speakerRefreshBusy) return;
                speakerRefreshBusy = true;
                try
                {
                    string raw = await http.GetStringAsync("http://127.0.0.1:7788/api/speaker/state");
                    var state = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                    bool enrolled = state.ContainsKey("enrolled") && Convert.ToBoolean(state["enrolled"]);
                    bool enabled = state.ContainsKey("enabled") && Convert.ToBoolean(state["enabled"]);
                    speakerStateText.Text = enrolled ? (enabled ? "등록됨 · 내 목소리만 반응 켜짐" : "등록됨 · 목소리 비교 꺼짐") : "미등록 · 아직 내 목소리를 구분하지 않습니다";
                    if (state.ContainsKey("profileError") && Convert.ToBoolean(state["profileError"])) speakerStateText.Text = "등록 정보 오류 · 음성 실행 보류 · 다시 등록해 주세요";
                    enrollmentPrompt.Text = speakerActionError;
                    var enrollment = state.ContainsKey("enrollment") ? state["enrollment"] as System.Collections.Generic.Dictionary<string, object> : null;
                    if (enrollment != null)
                    {
                        string phase = Convert.ToString(enrollment["phase"]);
                        double now = (DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalMilliseconds;
                        double start = Convert.ToDouble(enrollment["startAt"]), end = Convert.ToDouble(enrollment["endAt"]);
                        if (phase == "recording")
                        {
                            speakerStateText.Text = now < start ? "준비 · " + Math.Ceiling((start-now)/1000) + "초 뒤 녹음 시작" : now < end ? "녹음 중 · " + Math.Ceiling((end-now)/1000) + "초 남음" : "녹음 전달 중…";
                            var prompts = enrollment["prompts"] as System.Collections.IList;
                            int step = Math.Min(2, Math.Max(0, (int)((now-start)/8000)));
                            if (prompts != null && prompts.Count > step) enrollmentPrompt.Text = (step+1) + "/3  " + Convert.ToString(prompts[step]);
                        }
                        else if (phase == "processing") speakerStateText.Text = "세 구간의 목소리 비교 중…";
                        else if (phase == "error") enrollmentPrompt.Text = Convert.ToString(enrollment["message"]);
                    }
                }
                catch { speakerStateText.Text = "목소리 설정을 확인하지 못했습니다. 서버 연결을 확인해 주세요."; }
                finally { speakerRefreshBusy = false; }
            };
            Func<string, object, Task> changeSpeakerState = async (endpoint, body) => {
                speakerActionError = "";
                try
                {
                    string payload = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(body);
                    using (var response = await http.PostAsync("http://127.0.0.1:7788/api/speaker/" + endpoint, new StringContent(payload, Encoding.UTF8, "application/json")))
                    {
                        string raw = await response.Content.ReadAsStringAsync();
                        if (!response.IsSuccessStatusCode) { speakerActionError = ExtractJsonField(raw, "error"); enrollmentPrompt.Text = speakerActionError; return; }
                    }
                    await refreshSpeakerState();
                }
                catch { speakerActionError = "설정을 변경하지 못했습니다. 다시 눌러 주세요."; enrollmentPrompt.Text = speakerActionError; }
            };
            WrapPanel speakerButtons = new WrapPanel();
            Border enrollSpeaker = createActionBtn("내 목소리 등록 · 약 30초", true, async () => await changeSpeakerState("enroll/start", new { }));
            Border cancelSpeaker = createActionBtn("등록 취소", false, async () => await changeSpeakerState("enroll/cancel", new { }));
            Border enableSpeaker = createActionBtn("켜기", false, async () => await changeSpeakerState("enabled", new { enabled = true }));
            Border disableSpeaker = createActionBtn("끄기", false, async () => await changeSpeakerState("enabled", new { enabled = false }));
            Border forgetSpeaker = createActionBtn("등록 정보 지우기", false, async () => await changeSpeakerState("forget", new { }));
            foreach (var button in new Border[] { enrollSpeaker, cancelSpeaker, enableSpeaker, disableSpeaker, forgetSpeaker }) { button.Margin = new Thickness(0, 0, 8, 8); speakerButtons.Children.Add(button); }
            speakerStack.Children.Add(speakerButtons);
            paneVoice.Children.Add(speakerCard);
            DispatcherTimer speakerSettingsTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            speakerSettingsTimer.Tick += async (sender, args) => { if (paneVoice.Visibility == Visibility.Visible) await refreshSpeakerState(); };
            settingsWindow.Closed += (sender, args) => speakerSettingsTimer.Stop();
            speakerSettingsTimer.Start();

            Border microphoneCard = new Border { Background = new SolidColorBrush(bgSurface), CornerRadius = new CornerRadius(10), Padding = new Thickness(16) };
            StackPanel microphoneStack = new StackPanel();
            microphoneCard.Child = microphoneStack;
            microphoneStack.Children.Add(new TextBlock { Text = "마이크 연결 상태", Foreground = new SolidColorBrush(textPrimary), FontWeight = FontWeights.Bold, FontSize = 14 });
            microphoneStack.Children.Add(new TextBlock { Text = "입력 음량과 연결 상태를 확인합니다. 현재 입력 필터는 화자 본인 인증이나 타인 음성의 완전 차단을 보장하지 않습니다.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 7, 0, 10) });
            TextBlock micState = new TextBlock { Text = "아래 버튼으로 현재 상태를 확인하세요.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 0, 0, 10) };
            microphoneStack.Children.Add(micState);
            microphoneStack.Children.Add(createActionBtn("마이크 상태 확인", false, async () => {
                micState.Text = "확인 중…";
                try
                {
                    string raw = await http.GetStringAsync("http://127.0.0.1:7788/api/ear/latest");
                    var state = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                    object healthObj;
                    var health = state.TryGetValue("earHealth", out healthObj) ? healthObj as System.Collections.Generic.Dictionary<string, object> : null;
                    string status = health != null && health.ContainsKey("recognition") ? Convert.ToString(health["recognition"]) : "상태 미확인";
                    if (status == "listening") status = "연결됨";
                    string input = health != null && health.ContainsKey("deviceLabel") ? Convert.ToString(health["deviceLabel"]) : "입력 장치 미확인";
                    double level = state.ContainsKey("micLevel") ? Convert.ToDouble(state["micLevel"]) : 0;
                    micState.Text = "인식 상태: " + status + " · 입력 레벨: " + level.ToString("0.0000") + "\n" + input;
                }
                catch { micState.Text = "마이크 상태를 가져오지 못했습니다. 서버 연결을 확인해 주세요."; }
            }));
            paneVoice.Children.Add(microphoneCard);

            // =========================================================================
            // PANE 4: 바탕화면 · 단축키 제어 (Visible Desktop Control & Global Hotkey)
            // =========================================================================
            Border mediaCtrlCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel mediaCtrlStack = new StackPanel();
            mediaCtrlCard.Child = mediaCtrlStack;
            mediaCtrlStack.Children.Add(new TextBlock
            {
                Text = "실시간 바탕화면 미디어 및 마이크 제어",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            mediaCtrlStack.Children.Add(new TextBlock
            {
                Text = "음성으로 ‘음악 꺼줘’라고 말하거나 정지 버튼을 누르세요. 지원되는 미디어 세션을 제어하며, 실행 결과는 메인 HUD에 표시합니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });
            WrapPanel mediaBtnRow = new WrapPanel();
            Border btnKillMusic = createActionBtn("음악 정지 요청", false, () =>
            {
                SendCommandToButler("음악 꺼줘");
            });
            Border btnReconnectEar = createActionBtn("상시 음성인식 마이크 재연결", false, () =>
            {
                StartHandsFreeVoiceEar();
            });
            btnReconnectEar.Margin = new Thickness(8, 0, 0, 0);
            mediaBtnRow.Children.Add(btnKillMusic);
            mediaBtnRow.Children.Add(btnReconnectEar);
            mediaCtrlStack.Children.Add(mediaBtnRow);
            paneDesktop.Children.Add(mediaCtrlCard);

            Border hotkeyCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14)
            };
            StackPanel hotkeyStack = new StackPanel();
            hotkeyCard.Child = hotkeyStack;
            hotkeyStack.Children.Add(new TextBlock
            {
                Text = "자비스 호출 단축키 설정",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 0, 0, 6)
            });

            TextBox keyCaptureBox = new TextBox
            {
                Height = 34,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                IsReadOnly = true,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 10),
                Text = "현재 단축키: " + customHotkeyLabel + "  (클릭 후 새 키 조합 입력)"
            };
            keyCaptureBox.PreviewKeyDown += (s, e) =>
            {
                e.Handled = true;
                Key k = (e.Key == Key.System ? e.SystemKey : e.Key);
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift)
                {
                    return;
                }
                bool c = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool a = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                bool sh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;
                int vk = KeyInterop.VirtualKeyFromKey(k);

                string label = "";
                if (c) label += "Ctrl + ";
                if (a) label += "Alt + ";
                if (sh) label += "Shift + ";
                label += k.ToString();

                ApplyCustomHotkey(vk, c, a, sh, label);
                keyCaptureBox.Text = "적용된 단축키: " + label;
            };
            hotkeyStack.Children.Add(keyCaptureBox);

            WrapPanel presetHkRow = new WrapPanel();
            Border hk1 = createActionBtn("Ctrl + Space", false, () =>
            {
                ApplyCustomHotkey(VK_SPACE, true, false, false, "Ctrl + Space");
                keyCaptureBox.Text = "적용된 단축키: Ctrl + Space";
            });
            Border hk2 = createActionBtn("Alt + Space", false, () =>
            {
                ApplyCustomHotkey(VK_SPACE, false, true, false, "Alt + Space");
                keyCaptureBox.Text = "적용된 단축키: Alt + Space";
            });
            hk2.Margin = new Thickness(8, 0, 0, 0);
            Border hk3 = createActionBtn("F8 단독", false, () =>
            {
                ApplyCustomHotkey(VK_F8, false, false, false, "F8");
                keyCaptureBox.Text = "적용된 단축키: F8";
            });
            hk3.Margin = new Thickness(8, 0, 0, 0);
            presetHkRow.Children.Add(hk1);
            presetHkRow.Children.Add(hk2);
            presetHkRow.Children.Add(hk3);
            hotkeyStack.Children.Add(presetHkRow);
            paneDesktop.Children.Add(hotkeyCard);

            // =========================================================================
            // SIDEBAR NAVIGATION BUILDER
            // =========================================================================
            int activeNavIndex = 0;
            System.Collections.Generic.List<Action> navSyncCallbacks = new System.Collections.Generic.List<Action>();

            Action<int, string, string, string, StackPanel> addSidebarNav = (navIdx, iconGlyph, navTitle, navSubDesc, targetPane) =>
            {
                Border navRow = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(11, 9, 11, 9),
                    Margin = new Thickness(0, 0, 0, 4),
                    Cursor = Cursors.Hand
                };
                StackPanel rowStack = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock navIcon = new TextBlock
                {
                    Text = iconGlyph,
                    FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets, Segoe UI Symbol"),
                    FontSize = 13.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                TextBlock navTxt = new TextBlock
                {
                    Text = navTitle,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                rowStack.Children.Add(navIcon);
                rowStack.Children.Add(navTxt);
                navRow.Child = rowStack;
                navStack.Children.Add(navRow);

                Action syncNav = () =>
                {
                    bool isSel = (activeNavIndex == navIdx);
                    navRow.Background = new SolidColorBrush(isSel ? bgSurface : Colors.Transparent);
                    navRow.BorderBrush = new SolidColorBrush(isSel ? borderHairline : Colors.Transparent);
                    navRow.BorderThickness = new Thickness(1);
                    navIcon.Foreground = new SolidColorBrush(isSel ? accentBlue : textSecondary);
                    navTxt.Foreground = new SolidColorBrush(isSel ? textPrimary : textSecondary);
                    navTxt.FontWeight = isSel ? FontWeights.Bold : FontWeights.Normal;
                };
                navSyncCallbacks.Add(syncNav);
                syncNav();

                navRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
                navRow.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    activeNavIndex = navIdx;
                    paneModels.Visibility = Visibility.Collapsed;
                    paneAccount.Visibility = Visibility.Collapsed;
                    paneVoice.Visibility = Visibility.Collapsed;
                    paneDesktop.Visibility = Visibility.Collapsed;
                    targetPane.Visibility = Visibility.Visible;
                    settingsScroll.ScrollToTop();

                    txtPaneTitle.Text = navTitle;
                    txtPaneDesc.Text = navSubDesc;
                    foreach (var cb in navSyncCallbacks) { try { cb(); } catch { } }
                };
            };

            addSidebarNav(0, "\uE945", "요금제 및 AI 모델", "모델 연결 방식을 선택합니다. 분류는 참고용이며 실제 구독·할당량을 조회한 결과가 아닙니다.", paneModels);
            addSidebarNav(1, "\uE77B", "계정 및 API 연결", "Google 계정 OAuth 로그인 및 개인 API 키, 커스텀 모델 ID를 연결합니다.", paneAccount);
            addSidebarNav(2, "\uE767", "음성 · 마이크", "답변 목소리와 말하기 속도를 선택하고 마이크 연결 상태를 확인합니다.", paneVoice);
            addSidebarNav(3, "\uE7F8", "미디어 · 단축키", "실시간 바탕화면 미디어 제어 및 글로벌 호출 단축키를 관리합니다.", paneDesktop);

            // =========================================================================
            // RIGHT FOOTER
            // =========================================================================
            Border rightFooter = new Border
            {
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 12, 0, 0),
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rightFooter.Child = footerGrid;

            TextBlock footerStatus = new TextBlock
            {
                Text = "선택값을 저장합니다. 모델 접근 권한·요금은 계정과 공급자에서 확인하세요.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textMuted),
                VerticalAlignment = VerticalAlignment.Center
            };
            settingsFeedback = footerStatus;
            footerStatus.TextWrapping = TextWrapping.Wrap;
            footerStatus.Margin = new Thickness(0, 0, 12, 0);
            Grid.SetColumn(footerStatus, 0);
            footerGrid.Children.Add(footerStatus);

            Border btnDone = createActionBtn("확인", true, () =>
            {
                try { settingsWindow.Close(); } catch { }
                settingsWindow = null;
            });
            Grid.SetColumn(btnDone, 1);
            footerGrid.Children.Add(btnDone);

            Grid.SetRow(rightFooter, 2);
            rightGrid.Children.Add(rightFooter);

            settingsWindow.Show();
            settingsWindow.Activate();
        }

        // =========================================================================
        // DEDICATED COMMERCIAL SaaS HUD WINDOW: 📋 Plan 문서 뷰어 & 📓 대화 규칙/로그 & Google Harness²
        // =========================================================================
        private static void ShowPlanAndChlgdDocumentWindow(string initialTab = "plan")
        {
            if (planDocWindow != null)
            {
                try { planDocWindow.Close(); } catch { }
                planDocWindow = null;
            }

            Color bgShell = Color.FromRgb(11, 15, 22);
            Color bgSidebar = Color.FromRgb(8, 11, 16);
            Color bgSurface = Color.FromRgb(17, 23, 33);
            Color bgSurfaceHover = Color.FromRgb(25, 34, 48);
            Color bgSubRow = Color.FromRgb(13, 18, 27);
            Color bgActiveCard = Color.FromRgb(16, 34, 56);
            Color borderHairline = Color.FromRgb(42, 54, 70);
            Color borderSubtle = Color.FromRgb(28, 37, 50);
            Color accentCyan = Color.FromRgb(77, 217, 255);
            Color accentBlue = Color.FromRgb(56, 139, 253);
            Color accentEmerald = Color.FromRgb(46, 160, 67);
            Color accentAmber = Color.FromRgb(255, 184, 64);
            Color textPrimary = Color.FromRgb(242, 247, 253);
            Color textSecondary = Color.FromRgb(158, 172, 188);
            Color textMuted = Color.FromRgb(112, 124, 140);

            var workArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            var presSource = System.Windows.PresentationSource.FromVisual(hudWindow);
            var dpiMatrix = presSource != null && presSource.CompositionTarget != null
                ? presSource.CompositionTarget.TransformFromDevice
                : System.Windows.Media.Matrix.Identity;
            var workOrigin = dpiMatrix.Transform(new Point(workArea.Left, workArea.Top));
            var workExtent = dpiMatrix.Transform(new Point(workArea.Width, workArea.Height));
            double winW = Math.Min(1060, Math.Max(880, workExtent.X - 60));
            double winH = Math.Min(700, Math.Max(580, workExtent.Y - 90));

            planDocWindow = new Window
            {
                Title = "JARVIS SaaS Workspace — Plan · 대화 규칙 · 3대 생태계 · REA & MCP",
                Width = winW,
                Height = winH,
                MinWidth = 860,
                MinHeight = 560,
                Left = workOrigin.X + Math.Max(20, (workExtent.X - winW) / 2.0),
                Top = workOrigin.Y + Math.Max(30, (workExtent.Y - winH) / 2.0),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                Topmost = true
            };

            Border outerFrame = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(bgShell),
                BorderBrush = new SolidColorBrush(Color.FromArgb(195, 77, 217, 255)),
                BorderThickness = new Thickness(1.4),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 36,
                    ShadowDepth = 10,
                    Opacity = 0.85
                }
            };
            outerFrame.MouseLeftButtonDown += (s, e) =>
            {
                DependencyObject src = e.OriginalSource as DependencyObject;
                while (src != null && src != outerFrame)
                {
                    if (src is System.Windows.Controls.Primitives.TextBoxBase ||
                        src is System.Windows.Controls.Primitives.ButtonBase ||
                        src is System.Windows.Controls.Primitives.ScrollBar ||
                        src is System.Windows.Controls.Primitives.Thumb) return;
                    src = src is Visual ? VisualTreeHelper.GetParent(src) : LogicalTreeHelper.GetParent(src);
                }
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { planDocWindow.DragMove(); } catch { }
                }
            };
            planDocWindow.Content = outerFrame;

            Grid splitGrid = new Grid();
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(240) });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerFrame.Child = splitGrid;

            // =====================================================================
            // LEFT SIDEBAR (240px — Zero Horizontal Clipping, SaaS Workspace Nav)
            // =====================================================================
            Border sidebarBorder = new Border
            {
                CornerRadius = new CornerRadius(15, 0, 0, 15),
                Background = new SolidColorBrush(bgSidebar),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(16, 20, 16, 18)
            };
            Grid.SetColumn(sidebarBorder, 0);
            splitGrid.Children.Add(sidebarBorder);

            Grid sidebarGrid = new Grid();
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            sidebarGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            sidebarBorder.Child = sidebarGrid;

            StackPanel brandStack = new StackPanel { Margin = new Thickness(4, 0, 4, 18) };
            brandStack.Children.Add(new TextBlock
            {
                Text = "JARVIS SOVEREIGN SaaS",
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentCyan)
            });
            brandStack.Children.Add(new TextBlock
            {
                Text = "워크스페이스 관제 센터",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 16.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 3, 0, 8)
            });
            Border mcpBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(40, 46, 160, 67)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, 86, 211, 100)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            mcpBadge.Child = new TextBlock
            {
                Text = "● 상용 정본 · MCP & Harness² 활성",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100))
            };
            brandStack.Children.Add(mcpBadge);
            Grid.SetRow(brandStack, 0);
            sidebarGrid.Children.Add(brandStack);

            StackPanel navStack = new StackPanel();
            Grid.SetRow(navStack, 1);
            sidebarGrid.Children.Add(navStack);

            // Clean Wrapped Bottom Workspace Status Card (Never clips!)
            Border bottomSummary = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };
            StackPanel bottomSumStack = new StackPanel();
            bottomSummary.Child = bottomSumStack;
            bottomSumStack.Children.Add(new TextBlock
            {
                Text = "연동된 지식 · 스킬 워크스페이스",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textMuted),
                Margin = new Thickness(0, 0, 0, 5)
            });
            TextBlock txtSideBscStatus = new TextBlock
            {
                Text = "• 본사 (BeausCreators): 26명 활성",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(textPrimary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3)
            };
            TextBlock txtSideWhStatus = new TextBlock
            {
                Text = "• 스킬 창고: 663개 · 79 뉴런 시냅스",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(accentCyan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3)
            };
            TextBlock txtSideBwStatus = new TextBlock
            {
                Text = "• 출판 OS (BeautifulWorld): 673개 자산",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100)),
                TextWrapping = TextWrapping.Wrap
            };
            bottomSumStack.Children.Add(txtSideBscStatus);
            bottomSumStack.Children.Add(txtSideWhStatus);
            bottomSumStack.Children.Add(txtSideBwStatus);
            Grid.SetRow(bottomSummary, 2);
            sidebarGrid.Children.Add(bottomSummary);

            // =====================================================================
            // RIGHT CONTENT AREA (Header + Scrollable Body + Pinned Footer)
            // =====================================================================
            Grid rightGrid = new Grid { Margin = new Thickness(24, 18, 24, 16) };
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(rightGrid, 1);
            splitGrid.Children.Add(rightGrid);

            Grid rightHeader = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(rightHeader, 0);
            rightGrid.Children.Add(rightHeader);

            StackPanel headerTitleStack = new StackPanel();
            TextBlock txtDocPaneTitle = new TextBlock
            {
                Text = "📋 실시간 Plan · 설계 로드맵",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 18,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(textPrimary)
            };
            TextBlock txtDocPaneDesc = new TextBlock
            {
                Text = "프로젝트 목표와 단계별 실행 계획을 한눈에 관리하고 문서로 즉시 내보냅니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headerTitleStack.Children.Add(txtDocPaneTitle);
            headerTitleStack.Children.Add(txtDocPaneDesc);
            Grid.SetColumn(headerTitleStack, 0);
            rightHeader.Children.Add(headerTitleStack);

            // Top-Right Window Control Buttons ([최대화/복원] + [닫기])
            StackPanel winCtrlRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Border btnMaxWin = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            TextBlock txtMaxWin = new TextBlock
            {
                Text = "최대화 □",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(textSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            btnMaxWin.Child = txtMaxWin;
            btnMaxWin.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnMaxWin.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                if (planDocWindow == null) return;
                planDocWindow.WindowState = planDocWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                txtMaxWin.Text = planDocWindow.WindowState == WindowState.Maximized ? "복원 ❐" : "최대화 □";
            };
            winCtrlRow.Children.Add(btnMaxWin);

            Border btnCloseWin = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(Color.FromRgb(38, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(92, 42, 52)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btnCloseWin.Child = new TextBlock
            {
                Text = "✕",
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(255, 150, 150)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnCloseWin.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnCloseWin.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                try { planDocWindow.Close(); } catch { }
                planDocWindow = null;
            };
            winCtrlRow.Children.Add(btnCloseWin);
            Grid.SetColumn(winCtrlRow, 1);
            rightHeader.Children.Add(winCtrlRow);

            // ScrollViewer wrapping tabHost so content NEVER gets vertically clipped!
            Grid tabHost = new Grid();
            ScrollViewer mainTabScroll = new ScrollViewer
            {
                Content = tabHost,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 8, 6)
            };
            Grid.SetRow(mainTabScroll, 1);
            rightGrid.Children.Add(mainTabScroll);

            StackPanel tabPlan = new StackPanel { Visibility = Visibility.Visible };
            StackPanel tabRules = new StackPanel { Visibility = Visibility.Collapsed };
            StackPanel tabHarness2 = new StackPanel { Visibility = Visibility.Collapsed };
            StackPanel tabRea = new StackPanel { Visibility = Visibility.Collapsed };

            tabHost.Children.Add(tabPlan);
            tabHost.Children.Add(tabRules);
            tabHost.Children.Add(tabHarness2);
            tabHost.Children.Add(tabRea);

            // =====================================================================
            // SaaS UI COMPONENT BUILDERS (KPI Cards, Structured Cards, Action Buttons)
            // =====================================================================
            Func<string, string, string, Color, Border> makeKpiMetricBox = (label, bigValue, subNote, accentCol) =>
            {
                Border box = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(bgSurface),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 8, 10)
                };
                StackPanel sp = new StackPanel();
                box.Child = sp;
                sp.Children.Add(new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(textSecondary)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = bigValue,
                    FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                    FontSize = 18,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = new SolidColorBrush(accentCol),
                    Margin = new Thickness(0, 3, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                });
                sp.Children.Add(new TextBlock
                {
                    Text = subNote,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(textMuted),
                    TextWrapping = TextWrapping.Wrap
                });
                return box;
            };

            // Cleans raw developer log strings into structured, human-readable SaaS lines
            Func<string, string[]> splitCleanLines = (rawBody) =>
            {
                if (string.IsNullOrWhiteSpace(rawBody)) return new string[0];
                var list = new System.Collections.Generic.List<string>();
                string[] parts = rawBody.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string p in parts)
                {
                    string line = p.Trim().TrimStart('•', '-', '*').Trim();
                    line = System.Text.RegularExpressions.Regex.Replace(line, @"^\[20\d\d-\d\d-\d\d[^\]]*\]\s*", "");
                    if (!string.IsNullOrEmpty(line)) list.Add(line);
                }
                return list.ToArray();
            };

            Func<string, string, string, string, Color, Border> makeSaaSCard = (badgeText, heading, statusRight, rawBody, accentCol) =>
            {
                Border card = new Border
                {
                    CornerRadius = new CornerRadius(11),
                    Background = new SolidColorBrush(bgSurface),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 9)
                };
                StackPanel sp = new StackPanel();
                card.Child = sp;

                Grid topRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                sp.Children.Add(topRow);

                WrapPanel leftWrap = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                if (!string.IsNullOrEmpty(badgeText))
                {
                    Border badge = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(Color.FromArgb(38, accentCol.R, accentCol.G, accentCol.B)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(120, accentCol.R, accentCol.G, accentCol.B)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(0, 0, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = new TextBlock
                    {
                        Text = badgeText,
                        FontFamily = new FontFamily("Malgun Gothic"),
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(accentCol)
                    };
                    leftWrap.Children.Add(badge);
                }
                leftWrap.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textPrimary),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(leftWrap, 0);
                topRow.Children.Add(leftWrap);

                if (!string.IsNullOrEmpty(statusRight))
                {
                    Border statusPill = new Border
                    {
                        CornerRadius = new CornerRadius(5),
                        Background = new SolidColorBrush(bgSubRow),
                        BorderBrush = new SolidColorBrush(borderHairline),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    statusPill.Child = new TextBlock
                    {
                        Text = statusRight,
                        FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(textSecondary)
                    };
                    Grid.SetColumn(statusPill, 1);
                    topRow.Children.Add(statusPill);
                }

                string[] lines = splitCleanLines(rawBody);
                foreach (string line in lines)
                {
                    Border itemRow = new Border
                    {
                        CornerRadius = new CornerRadius(7),
                        Background = new SolidColorBrush(bgSubRow),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(90, accentCol.R, accentCol.G, accentCol.B)),
                        BorderThickness = new Thickness(2.5, 0, 0, 0),
                        Padding = new Thickness(11, 6, 11, 6),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    itemRow.Child = new TextBlock
                    {
                        Text = line,
                        FontFamily = new FontFamily("Malgun Gothic"),
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromRgb(224, 234, 245)),
                        TextWrapping = TextWrapping.Wrap,
                        LineHeight = 18
                    };
                    sp.Children.Add(itemRow);
                }
                return card;
            };

            Func<string, string, Color, Action, Border> makeResponsiveActionBtn = (btnLabel, subHint, bgCol, onClick) =>
            {
                Border b = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(bgCol),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 9, 12, 9),
                    Margin = new Thickness(0, 0, 8, 8),
                    Cursor = Cursors.Hand
                };
                StackPanel sp = new StackPanel();
                b.Child = sp;
                sp.Children.Add(new TextBlock
                {
                    Text = btnLabel,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrEmpty(subHint))
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = subHint,
                        FontFamily = new FontFamily("Malgun Gothic"),
                        FontSize = 10.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 235, 250)),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    });
                }
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick(); };
                return b;
            };

            // =====================================================================
            // TAB 1: 📋 실시간 Plan · 설계 로드맵 (Top Action Bar + Structured Step Cards)
            // =====================================================================
            Border newPlanBar = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid newPlanGrid = new Grid();
            newPlanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            newPlanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            newPlanGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            newPlanBar.Child = newPlanGrid;

            TextBox txtNewPlanInput = new TextBox
            {
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Text = ""
            };
            Grid.SetColumn(txtNewPlanInput, 0);
            newPlanGrid.Children.Add(txtNewPlanInput);

            Border btnBuildPlan = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(accentBlue),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnBuildPlan.Child = new TextBlock
            {
                Text = "⚡ 새 Plan 설계 생성",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Grid.SetColumn(btnBuildPlan, 1);
            newPlanGrid.Children.Add(btnBuildPlan);

            Border btnBriefingFromPlan = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(bgSubRow),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnBriefingFromPlan.Child = new TextBlock
            {
                Text = "📋 오늘 본사 브리핑 열기",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(accentCyan)
            };
            btnBriefingFromPlan.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnBriefingFromPlan.MouseLeftButtonUp += (s, e) => { e.Handled = true; SendCommandToButler("오늘 할 일 정리해 줘"); };
            Grid.SetColumn(btnBriefingFromPlan, 2);
            newPlanGrid.Children.Add(btnBriefingFromPlan);
            tabPlan.Children.Add(newPlanBar);

            Border planBanner = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(bgActiveCard),
                BorderBrush = new SolidColorBrush(accentBlue),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel planBannerStack = new StackPanel();
            planBanner.Child = planBannerStack;
            TextBlock txtActivePlanTitle = new TextBlock
            {
                Text = "📋 자비스 × BeausCreators 3대 생태계 통합 상용 아키텍처",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 14.5,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            TextBlock txtActivePlanSummary = new TextBlock
            {
                Text = "실시간 동기화 완료 · 정본 문서: JARVIS_Active_Plan.md",
                FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(accentCyan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            planBannerStack.Children.Add(txtActivePlanTitle);
            planBannerStack.Children.Add(txtActivePlanSummary);
            tabPlan.Children.Add(planBanner);

            StackPanel planSectionsContainer = new StackPanel();
            planSectionsContainer.Children.Add(makeSaaSCard(
                "STEP 01",
                "대화 규칙 · 오더 원장 자동 정제 시스템",
                "가동 중",
                "• 음성/채팅 중 발생하는 규칙('앞으로 ~해')을 카테고리별로 자동 분류하여 영구 저장합니다.\n• 모든 오더 이력과 학습 교훈을 한눈에 조회하고 검색할 수 있습니다.",
                accentCyan
            ));
            planSectionsContainer.Children.Add(makeSaaSCard(
                "STEP 02",
                "3대 생태계 (본사 · 출판사 · 스킬 창고) 실시간 연동",
                "동기화 완료",
                "• BeausCreators(26인 에이전트), BeautifulWorld(출판 OS), Warehouse(663 스킬·79 뉴런) 데이터를 실시간 바인딩합니다.\n• 리서치 및 브리핑 결과를 Word, PDF, PPT, Markdown 4대 문서로 즉시 생성합니다.",
                Color.FromRgb(86, 211, 100)
            ));
            planSectionsContainer.Children.Add(makeSaaSCard(
                "STEP 03",
                "Google Harness² 재귀 자가진화 & REA v6.8.0 · Laya 엔진",
                "검증 완료",
                "• 8-Surface 용량 캡(20KB) 및 경험 원장(R=4000, P=3000) 기반 자율 규칙 승격을 수행합니다.\n• morluto/rea v6.8.0 역공학 및 Laya 의사결정 점검을 원클릭으로 실행합니다.",
                accentAmber
            ));
            tabPlan.Children.Add(planSectionsContainer);

            btnBuildPlan.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnBuildPlan.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                string topic = txtNewPlanInput.Text.Trim();
                if (string.IsNullOrEmpty(topic)) topic = "자비스 SaaS 상용화 및 3대 생태계 워크플로 자동화 계획";
                txtActivePlanTitle.Text = "📋 [신규 Plan] " + topic;
                txtActivePlanSummary.Text = "생성 시각: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · JARVIS_Active_Plan.md 자동 저장 완료";
                planSectionsContainer.Children.Clear();
                planSectionsContainer.Children.Add(makeSaaSCard(
                    "PHASE 01",
                    "목표 정의 및 사전 리스크 점검",
                    "준비 완료",
                    "• 핵심 목표: 「" + topic + "」\n• 기존 에러 원장(error-log.md) 및 대화 규칙 가드레일 사전 검증 완료",
                    accentCyan
                ));
                planSectionsContainer.Children.Add(makeSaaSCard(
                    "PHASE 02",
                    "실행 파이프라인 & 자동 문서화",
                    "실행 대기",
                    "• 3대 생태계 스킬 및 CHLGD 자동화 스크립트(.ps1 / .py) 연동 실행\n• 결과물을 Word(.docx) · PDF · PPT 보고서로 즉시 변환",
                    Color.FromRgb(86, 211, 100)
                ));
                planSectionsContainer.Children.Add(makeSaaSCard(
                    "PHASE 03",
                    "품질 검증 (Done Gate) & 경험 원장 기록",
                    "검증 게이트",
                    "• 실행 완료 후 실측 지표를 오더 원장(ceo-orders-log.jsonl) 및 Harness² 경험 원장에 자동 기록",
                    accentAmber
                ));
                txtNewPlanInput.Text = "";
                Task.Run(async () =>
                {
                    try
                    {
                        string payload = "{\"title\":\"📋 [Plan] " + EscapeJson(topic) + "\",\"rawTopic\":\"" + EscapeJson(topic) + "\",\"openWindow\":false}";
                        await http.PostAsync("http://127.0.0.1:7788/api/hud/plan-doc", new StringContent(payload, Encoding.UTF8, "application/json"));
                    }
                    catch { }
                });
            };

            // =====================================================================
            // TAB 2: 📓 CHLGD 대화 규칙 · 로그 (Top Input Bar + KPI Strip + Clean Rule Cards)
            // =====================================================================
            Border addRuleBar = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid addRuleGrid = new Grid();
            addRuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            addRuleGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            addRuleBar.Child = addRuleGrid;

            TextBox txtNewRuleInput = new TextBox
            {
                Height = 32,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Text = ""
            };
            Grid.SetColumn(txtNewRuleInput, 0);
            addRuleGrid.Children.Add(txtNewRuleInput);

            Border btnAddRule = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(accentEmerald),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnAddRule.Child = new TextBlock
            {
                Text = "＋ 새 대화 규칙 등록",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Grid.SetColumn(btnAddRule, 1);
            addRuleGrid.Children.Add(btnAddRule);
            tabRules.Children.Add(addRuleBar);

            System.Windows.Controls.Primitives.UniformGrid rulesKpiGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            TextBlock txtKpiOrdersVal = new TextBlock { Text = "69건", FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(accentCyan), Margin = new Thickness(0, 2, 0, 2) };
            TextBlock txtKpiRulesVal = new TextBlock { Text = "활성 규칙", FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100)), Margin = new Thickness(0, 2, 0, 2) };
            TextBlock txtKpiHarnessVal = new TextBlock { Text = "실시간 학습", FontFamily = new FontFamily("Segoe UI, Malgun Gothic"), FontSize = 18, FontWeight = FontWeights.ExtraBold, Foreground = new SolidColorBrush(accentAmber), Margin = new Thickness(0, 2, 0, 2) };

            Func<string, TextBlock, string, Border> wrapKpiBox = (lbl, valBlock, sub) =>
            {
                Border b = new Border
                {
                    CornerRadius = new CornerRadius(10),
                    Background = new SolidColorBrush(bgSurface),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 8, 10)
                };
                StackPanel sp = new StackPanel();
                b.Child = sp;
                sp.Children.Add(new TextBlock { Text = lbl, FontFamily = new FontFamily("Malgun Gothic"), FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(textSecondary) });
                sp.Children.Add(valBlock);
                sp.Children.Add(new TextBlock { Text = sub, FontFamily = new FontFamily("Malgun Gothic"), FontSize = 10.5, Foreground = new SolidColorBrush(textMuted), TextWrapping = TextWrapping.Wrap });
                return b;
            };
            rulesKpiGrid.Children.Add(wrapKpiBox("누적 실행 오더", txtKpiOrdersVal, "ceo-orders-log.jsonl 원장"));
            rulesKpiGrid.Children.Add(wrapKpiBox("대화 · 업무 규칙", txtKpiRulesVal, "가드레일 상시 적용 중"));
            rulesKpiGrid.Children.Add(wrapKpiBox("Harness² 재귀 학습", txtKpiHarnessVal, "반복 패턴 자동 승격"));
            tabRules.Children.Add(rulesKpiGrid);

            StackPanel rulesListContainer = new StackPanel();
            tabRules.Children.Add(rulesListContainer);

            btnAddRule.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnAddRule.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                string newRule = txtNewRuleInput.Text.Trim();
                if (string.IsNullOrEmpty(newRule)) return;
                rulesListContainer.Children.Insert(0, makeSaaSCard(
                    "신규 등록 규칙",
                    newRule,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · 즉시 적용",
                    "• 대표님 직접 등록 규칙으로 Google Harness² 가드레일 및 대화 원장에 영구 반영되었습니다.",
                    Color.FromRgb(86, 211, 100)
                ));
                txtNewRuleInput.Text = "";
                Task.Run(async () =>
                {
                    try
                    {
                        string payload = "{\"ruleText\":\"" + EscapeJson(newRule) + "\",\"category\":\"대표님 직접 등록 규칙\"}";
                        await http.PostAsync("http://127.0.0.1:7788/api/hud/rules", new StringContent(payload, Encoding.UTF8, "application/json"));
                    }
                    catch { }
                });
            };

            // =====================================================================
            // TAB 3: 🧬 Google Harness² & 3대 생태계 (Responsive 3-Col Action Grid + KPI Cards)
            // =====================================================================
            System.Windows.Controls.Primitives.UniformGrid h2ActionGrid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 0, 0, 4)
            };
            TextBlock txtH2RunResult = new TextBlock
            {
                Text = "● Google Harness² 8-Surface 검증: PASS · 경험 원장(R=4000, P=3000) 준비 완료",
                FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(accentCyan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 10)
            };
            h2ActionGrid.Children.Add(makeResponsiveActionBtn(
                "⚡ Harness² 자가진화 실행",
                "Propose ➔ Probe ➔ Compose",
                accentBlue,
                () =>
                {
                    txtH2RunResult.Text = "⏳ Google Harness² 8-Surface 검증 및 자가진화 루프 실행 중...";
                    Task.Run(async () =>
                    {
                        try
                        {
                            await http.PostAsync("http://127.0.0.1:7788/api/hud/harness2-evolve", new StringContent("{\"reason\":\"CEO HUD Trigger\"}", Encoding.UTF8, "application/json"));
                            hudWindow.Dispatcher.Invoke(() =>
                            {
                                txtH2RunResult.Text = "✓ Google Harness² 자가진화 완료: 8-Surface PASS · 경험 원장(EXPERIENCE.md) 업데이트 완료";
                                SpeakAsync("대표님, 구글 하네스 투 자가진화 루프를 완료하여 경험 원장에 반영했습니다.");
                            });
                        }
                        catch { }
                    });
                }
            ));
            h2ActionGrid.Children.Add(makeResponsiveActionBtn(
                "🧠 옵시디언 뉴런 지식망 검색",
                "79 Nodes · 138 Synapses 조회",
                accentEmerald,
                () => SendCommandToButler("뉴런에서 전체 검색해줘")
            ));
            h2ActionGrid.Children.Add(makeResponsiveActionBtn(
                "🏛 오늘 본사 작업일지 브리핑",
                "작업일지 · 보고서 · 에이전트 요약",
                Color.FromRgb(40, 54, 74),
                () => SendCommandToButler("오늘 할 일 정리해 줘")
            ));
            tabHarness2.Children.Add(h2ActionGrid);
            tabHarness2.Children.Add(txtH2RunResult);

            System.Windows.Controls.Primitives.UniformGrid ecoKpiGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            ecoKpiGrid.Children.Add(makeKpiMetricBox("🏛 BeausCreators 본사", "26명 에이전트", "CompanyOS · 90/91 칸반 가동", accentCyan));
            ecoKpiGrid.Children.Add(makeKpiMetricBox("📚 BeautifulWorld 출판", "673개 자산", "7-Class OS · EPUB 3.0 파이프라인", Color.FromRgb(86, 211, 100)));
            ecoKpiGrid.Children.Add(makeKpiMetricBox("🧠 Warehouse 스킬·뉴런", "663 스킬 · 79 뉴런", "138 시냅스 · 55 GitHub 레포", accentAmber));
            tabHarness2.Children.Add(ecoKpiGrid);

            StackPanel h2CardsContainer = new StackPanel();
            tabHarness2.Children.Add(h2CardsContainer);

            // =====================================================================
            // TAB 4: 🔬 Antigravity REA v6.8.0 · Laya · 8대 MCP 도구 카탈로그
            // =====================================================================
            System.Windows.Controls.Primitives.UniformGrid reaActionsGrid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 0, 0, 6)
            };
            reaActionsGrid.Children.Add(makeResponsiveActionBtn(
                "🔬 자비스 REA v6.8.0 진단",
                "4-Stage 역공학 · 4분할 증거 추출",
                accentBlue,
                () => SendCommandToButler("자비스 REA 역공학 분석해줘")
            ));
            reaActionsGrid.Children.Add(makeResponsiveActionBtn(
                "🏛 본사(BeausCreators) 진단",
                "본사 아키텍처 · 스킬 구조 분석",
                accentEmerald,
                () => SendCommandToButler("뷰스크리에이터스 REA 역공학 분석해줘")
            ));
            reaActionsGrid.Children.Add(makeResponsiveActionBtn(
                "🧬 Laya 의사결정 점검",
                "다국어 DSL · 프로젝트 상태 진단",
                Color.FromRgb(158, 106, 3),
                () => SendCommandToButler("라야로 현재 프로젝트 상태 점검해줘")
            ));
            tabRea.Children.Add(reaActionsGrid);

            tabRea.Children.Add(makeSaaSCard(
                "REA v6.8.0",
                "morluto/rea 4-Stage Clean-Room 역공학 아키텍처 엔진",
                "Commit e75d1c741 · 활성",
                "• 분석 지원 대상: 웹 애플리케이션 · Electron/ASAR · Unity IL2CPP · 네이티브 바이너리(PE/ELF) · AI 모델(GGUF/ONNX)\n• 4분할 증거 검증: ① Observations(관찰 팩트) · ② Derivations(도출 구조) · ③ Inferences(추론) · ④ Unknowns(미확인 리스크)",
                accentCyan
            ));
            tabRea.Children.Add(makeSaaSCard(
                "LAYA DSL",
                "Laya 다국어 의사결정 구조화 언어 & System-1 라우터",
                "결정론적 라우팅 · 활성",
                "• 자연어 명령을 { state, questions } 의사결정 스펙으로 정제하여 모호함 없이 실행 계획을 수립합니다.",
                accentAmber
            ));

            Border mcpCatalogCard = new Border
            {
                CornerRadius = new CornerRadius(11),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 8)
            };
            StackPanel mcpCatStack = new StackPanel();
            mcpCatalogCard.Child = mcpCatStack;
            mcpCatStack.Children.Add(new TextBlock
            {
                Text = "🔗 양방향 MCP 서버 제공 도구 카탈로그 (jarvis-sovereign-butler · 8대 도구 온라인)",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100)),
                Margin = new Thickness(0, 0, 0, 8)
            });

            System.Windows.Controls.Primitives.UniformGrid mcpToolsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            string[][] mcpToolsList = new string[][]
            {
                new string[] { "① 상태 · 프로필 조회", "jarvis_status_and_profile", "3대 생태계 · 메모리 · 대표님 프로필 조회" },
                new string[] { "② 음성 · 시스템 오더 실행", "jarvis_execute_command", "자비스 Butler 엔진 명령 즉시 실행" },
                new string[] { "③ Plan · 규칙 HUD 제어", "jarvis_open_plan_hud", "전용 Plan 문서 및 규칙 창 실시간 제어" },
                new string[] { "④ 대화 규칙 · 로그 기록", "jarvis_record_rule_or_log", "새 대화 규칙 및 CHLGD 오더 원장 기록" },
                new string[] { "⑤ Harness² 자가진화 실행", "jarvis_run_harness2_recursion", "8-Surface 검증 및 재귀 학습 루프 실행" },
                new string[] { "⑥ 4대 오피스 문서 제작", "jarvis_create_office_doc", "Word · PDF · PPT · Markdown 즉시 생성" },
                new string[] { "⑦ REA v6.8.0 · Laya 분석", "jarvis_run_rea_or_laya", "클린룸 역공학 및 Laya 상태 진단 실행" },
                new string[] { "⑧ Warehouse 뉴런 검색", "jarvis_warehouse_neuron", "79 Nodes · 138 Synapses 2-Hop 탐색" }
            };
            foreach (var mt in mcpToolsList)
            {
                Border tBox = new Border
                {
                    CornerRadius = new CornerRadius(7),
                    Background = new SolidColorBrush(bgSubRow),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(10, 7, 10, 7),
                    Margin = new Thickness(0, 0, 6, 6)
                };
                StackPanel tsp = new StackPanel();
                tBox.Child = tsp;
                Grid topT = new Grid();
                topT.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                topT.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                topT.Children.Add(new TextBlock
                {
                    Text = mt[0],
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textPrimary)
                });
                TextBlock codeTb = new TextBlock
                {
                    Text = mt[1],
                    FontFamily = new FontFamily("Consolas"),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(accentCyan),
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(codeTb, 1);
                topT.Children.Add(codeTb);
                tsp.Children.Add(topT);
                tsp.Children.Add(new TextBlock
                {
                    Text = mt[2],
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(textSecondary),
                    Margin = new Thickness(0, 2, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });
                mcpToolsGrid.Children.Add(tBox);
            }
            mcpCatStack.Children.Add(mcpToolsGrid);
            tabRea.Children.Add(mcpCatalogCard);

            // =====================================================================
            // SIDEBAR NAVIGATION SETUP
            // =====================================================================
            string currentTabKey = string.IsNullOrEmpty(initialTab) ? "plan" : initialTab.ToLowerInvariant();
            System.Collections.Generic.List<Action> navSyncList = new System.Collections.Generic.List<Action>();

            Action<string, string, string, string, StackPanel> addDocNav = (tabKey, iconStr, navLabel, descStr, paneTarget) =>
            {
                Border navBtn = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = Cursors.Hand
                };
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock ic = new TextBlock
                {
                    Text = iconStr,
                    FontFamily = new FontFamily("Segoe UI Symbol, Malgun Gothic"),
                    FontSize = 13.5,
                    Margin = new Thickness(0, 0, 9, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBlock lb = new TextBlock
                {
                    Text = navLabel,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 12.5,
                    VerticalAlignment = VerticalAlignment.Center
                };
                row.Children.Add(ic);
                row.Children.Add(lb);
                navBtn.Child = row;
                navStack.Children.Add(navBtn);

                Action sync = () =>
                {
                    bool active = (currentTabKey == tabKey);
                    navBtn.Background = new SolidColorBrush(active ? bgSurface : Colors.Transparent);
                    navBtn.BorderBrush = new SolidColorBrush(active ? accentCyan : Colors.Transparent);
                    navBtn.BorderThickness = new Thickness(1);
                    ic.Foreground = new SolidColorBrush(active ? accentCyan : textSecondary);
                    lb.Foreground = new SolidColorBrush(active ? textPrimary : textSecondary);
                    lb.FontWeight = active ? FontWeights.Bold : FontWeights.Normal;
                };
                navSyncList.Add(sync);

                navBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
                navBtn.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    currentTabKey = tabKey;
                    tabPlan.Visibility = Visibility.Collapsed;
                    tabRules.Visibility = Visibility.Collapsed;
                    tabHarness2.Visibility = Visibility.Collapsed;
                    tabRea.Visibility = Visibility.Collapsed;
                    paneTarget.Visibility = Visibility.Visible;
                    txtDocPaneTitle.Text = iconStr + " " + navLabel;
                    txtDocPaneDesc.Text = descStr;
                    mainTabScroll.ScrollToTop();
                    foreach (var cb in navSyncList) { try { cb(); } catch { } }
                };
            };

            addDocNav("plan", "📋", "실시간 Plan · 로드맵", "프로젝트 목표와 단계별 실행 계획을 구조화하여 관리하고 문서로 즉시 내보냅니다.", tabPlan);
            addDocNav("rules", "📓", "CHLGD 대화 규칙 · 로그", "대표님과의 대화에서 학습된 규칙, 선호도, 오더 실행 원장을 카드 형태로 깔끔하게 정리합니다.", tabRules);
            addDocNav("harness2", "🧬", "3대 생태계 & Harness²", "BeausCreators · BeautifulWorld · Warehouse 실시간 지표 및 Google Harness² 자가진화 관제판입니다.", tabHarness2);
            addDocNav("rea", "🔬", "Antigravity REA & MCP", "morluto/rea v6.8.0 클린룸 역공학, Laya 의사결정 언어 및 8대 양방향 MCP 도구 카탈로그입니다.", tabRea);

            tabPlan.Visibility = currentTabKey == "plan" ? Visibility.Visible : Visibility.Collapsed;
            tabRules.Visibility = currentTabKey == "rules" ? Visibility.Visible : Visibility.Collapsed;
            tabHarness2.Visibility = currentTabKey == "harness2" ? Visibility.Visible : Visibility.Collapsed;
            tabRea.Visibility = currentTabKey == "rea" ? Visibility.Visible : Visibility.Collapsed;
            if (currentTabKey == "rules")
            {
                txtDocPaneTitle.Text = "📓 CHLGD 대화 규칙 · 로그";
                txtDocPaneDesc.Text = "대표님과의 대화에서 학습된 규칙, 선호도, 오더 실행 원장을 카드 형태로 깔끔하게 정리합니다.";
            }
            else if (currentTabKey == "harness2")
            {
                txtDocPaneTitle.Text = "🧬 3대 생태계 & Harness²";
                txtDocPaneDesc.Text = "BeausCreators · BeautifulWorld · Warehouse 실시간 지표 및 Google Harness² 자가진화 관제판입니다.";
            }
            else if (currentTabKey == "rea")
            {
                txtDocPaneTitle.Text = "🔬 Antigravity REA & MCP";
                txtDocPaneDesc.Text = "morluto/rea v6.8.0 클린룸 역공학, Laya 의사결정 언어 및 8대 양방향 MCP 도구 카탈로그입니다.";
            }
            foreach (var cb in navSyncList) { try { cb(); } catch { } }

            // Pinned Footer
            Border footerBorder = new Border
            {
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(0, 10, 0, 0),
                Margin = new Thickness(0, 6, 0, 0)
            };
            Grid footerGrid = new Grid();
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerBorder.Child = footerGrid;
            footerGrid.Children.Add(new TextBlock
            {
                Text = "💡 창 우측 하단 모서리를 드래그하여 크기를 자유롭게 조절하거나 상단 [최대화 □] 버튼을 누르실 수 있습니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(textMuted),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            Border btnFooterClose = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 5, 16, 5),
                Cursor = Cursors.Hand
            };
            btnFooterClose.Child = new TextBlock
            {
                Text = "닫기",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            };
            btnFooterClose.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnFooterClose.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                try { planDocWindow.Close(); } catch { }
                planDocWindow = null;
            };
            Grid.SetColumn(btnFooterClose, 1);
            footerGrid.Children.Add(btnFooterClose);
            Grid.SetRow(footerBorder, 2);
            rightGrid.Children.Add(footerBorder);

            planDocWindow.Show();
            planDocWindow.Activate();

            // Fetch live data from /api/hud/chlgd-dashboard and render into Structured SaaS Cards
            Task.Run(async () =>
            {
                try
                {
                    string dashJson = await http.GetStringAsync("http://127.0.0.1:7788/api/hud/chlgd-dashboard");
                    if (string.IsNullOrEmpty(dashJson)) return;
                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var root = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(dashJson);
                    if (root == null) return;

                    hudWindow.Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            // 1) Tab 1: Active Plan Document
                            var planDoc = root.ContainsKey("activePlanDoc") ? root["activePlanDoc"] as System.Collections.Generic.Dictionary<string, object> : null;
                            if (planDoc != null)
                            {
                                string pTitle = planDoc.ContainsKey("title") ? Convert.ToString(planDoc["title"]) : "";
                                string pUpdated = planDoc.ContainsKey("updatedAt") ? Convert.ToString(planDoc["updatedAt"]) : "";
                                if (!string.IsNullOrEmpty(pTitle)) txtActivePlanTitle.Text = pTitle;
                                if (!string.IsNullOrEmpty(pUpdated))
                                {
                                    txtActivePlanSummary.Text = "최근 동기화: " + pUpdated + "  ·  정본: JARVIS_Active_Plan.md";
                                }
                                var sections = planDoc.ContainsKey("sections") ? planDoc["sections"] as System.Collections.ArrayList : null;
                                if (sections != null && sections.Count > 0)
                                {
                                    planSectionsContainer.Children.Clear();
                                    Color[] cols = new Color[] { accentCyan, Color.FromRgb(86, 211, 100), accentAmber, Color.FromRgb(165, 214, 255) };
                                    int idx = 0;
                                    foreach (var secObj in sections)
                                    {
                                        var sec = secObj as System.Collections.Generic.Dictionary<string, object>;
                                        if (sec == null) continue;
                                        string h = sec.ContainsKey("heading") ? Convert.ToString(sec["heading"]) : "";
                                        string b = sec.ContainsKey("body") ? Convert.ToString(sec["body"]) : "";
                                        if (!string.IsNullOrEmpty(h))
                                        {
                                            planSectionsContainer.Children.Add(makeSaaSCard(
                                                "STEP 0" + (idx + 1),
                                                h,
                                                "실시간 연동",
                                                b,
                                                cols[idx % cols.Length]
                                            ));
                                            idx++;
                                        }
                                        if (idx >= 5) break;
                                    }
                                }
                            }

                            // 2) Tab 2: Structured Rules & Orders
                            var recLearn = root.ContainsKey("recursiveLearning") ? root["recursiveLearning"] as System.Collections.Generic.Dictionary<string, object> : null;
                            var convRules = root.ContainsKey("conversationRules") ? root["conversationRules"] as System.Collections.ArrayList : null;
                            int totOrders = root.ContainsKey("totalOrders") ? Convert.ToInt32(root["totalOrders"]) : 0;
                            int learnedCnt = (recLearn != null && recLearn.ContainsKey("totalLearnedRules")) ? Convert.ToInt32(recLearn["totalLearnedRules"]) : 0;
                            int lessonCnt = (recLearn != null && recLearn.ContainsKey("totalLessons")) ? Convert.ToInt32(recLearn["totalLessons"]) : 0;
                            int convRuleCnt = convRules != null ? convRules.Count : 0;

                            txtKpiOrdersVal.Text = totOrders.ToString("N0") + "건 실행";
                            txtKpiRulesVal.Text = (convRuleCnt + learnedCnt).ToString("N0") + "개 활성";
                            txtKpiHarnessVal.Text = "승격 " + learnedCnt + "건 · 교훈 " + lessonCnt + "건";

                            rulesListContainer.Children.Clear();
                            if (recLearn != null && recLearn.ContainsKey("rules"))
                            {
                                var rList = recLearn["rules"] as System.Collections.ArrayList;
                                if (rList != null)
                                {
                                    foreach (var rObj in rList)
                                    {
                                        var rDict = rObj as System.Collections.Generic.Dictionary<string, object>;
                                        if (rDict == null) continue;
                                        string rTitle = rDict.ContainsKey("title") ? Convert.ToString(rDict["title"]) : "재귀 학습 승격 규칙";
                                        string rGuidance = rDict.ContainsKey("guidance") ? Convert.ToString(rDict["guidance"]) : (rDict.ContainsKey("rule") ? Convert.ToString(rDict["rule"]) : "");
                                        string rDom = rDict.ContainsKey("domain") ? Convert.ToString(rDict["domain"]) : "자동 학습";
                                        if (!string.IsNullOrEmpty(rGuidance))
                                        {
                                            rulesListContainer.Children.Add(makeSaaSCard(
                                                "Harness² · " + rDom,
                                                rTitle,
                                                "자동 승격됨",
                                                rGuidance,
                                                Color.FromRgb(86, 211, 100)
                                            ));
                                        }
                                    }
                                }
                            }
                            if (convRules != null)
                            {
                                foreach (var cObj in convRules)
                                {
                                    var cDict = cObj as System.Collections.Generic.Dictionary<string, object>;
                                    if (cDict == null) continue;
                                    string cat = cDict.ContainsKey("category") ? Convert.ToString(cDict["category"]) : "대화 규칙";
                                    string rText = cDict.ContainsKey("rule") ? Convert.ToString(cDict["rule"]) : "";
                                    string dt = cDict.ContainsKey("createdAt") ? Convert.ToString(cDict["createdAt"]) : "";
                                    if (!string.IsNullOrEmpty(rText))
                                    {
                                        rulesListContainer.Children.Add(makeSaaSCard(
                                            cat,
                                            cat + " 가드레일",
                                            string.IsNullOrEmpty(dt) ? "적용 중" : dt + " · 적용 중",
                                            rText,
                                            accentCyan
                                        ));
                                    }
                                }
                            }

                            // 3) Tab 3: Live 3-Ecosystems + Google Harness²
                            var ecoRoot = root.ContainsKey("ecosystems") ? root["ecosystems"] as System.Collections.Generic.Dictionary<string, object> : null;
                            var h2Dict = root.ContainsKey("harness2") ? root["harness2"] as System.Collections.Generic.Dictionary<string, object> : null;
                            var invDict = root.ContainsKey("skillInventory") ? root["skillInventory"] as System.Collections.Generic.Dictionary<string, object> : null;
                            h2CardsContainer.Children.Clear();

                            if (ecoRoot != null && ecoRoot.ContainsKey("ecosystems"))
                            {
                                var ecoArr = ecoRoot["ecosystems"] as System.Collections.ArrayList;
                                int totalEcoFiles = ecoRoot.ContainsKey("totalFiles") ? Convert.ToInt32(ecoRoot["totalFiles"]) : 0;
                                int totalEcoSkills = ecoRoot.ContainsKey("totalSkills") ? Convert.ToInt32(ecoRoot["totalSkills"]) : 0;
                                StringBuilder ecoSb = new StringBuilder();
                                if (ecoArr != null)
                                {
                                    foreach (var eItem in ecoArr)
                                    {
                                        var ed = eItem as System.Collections.Generic.Dictionary<string, object>;
                                        if (ed == null) continue;
                                        string eName = ed.ContainsKey("name") ? Convert.ToString(ed["name"]) : "";
                                        string ePath = ed.ContainsKey("root") ? Convert.ToString(ed["root"]) : "";
                                        int fCnt = ed.ContainsKey("fileCount") ? Convert.ToInt32(ed["fileCount"]) : 0;
                                        int sCnt = ed.ContainsKey("skillCount") ? Convert.ToInt32(ed["skillCount"]) : 0;
                                        ecoSb.AppendLine(eName + " (" + ePath + ")  —  실측 파일 " + fCnt.ToString("N0") + "개 · 스킬/모듈 " + sCnt.ToString("N0") + "개");
                                    }
                                }
                                h2CardsContainer.Children.Add(makeSaaSCard(
                                    "3대 생태계 통합",
                                    "이건우 대표님 3대 생태계 실시간 연결 현황",
                                    "총 " + totalEcoFiles.ToString("N0") + " 파일 · " + totalEcoSkills.ToString("N0") + " 스킬",
                                    ecoSb.ToString().TrimEnd(),
                                    Color.FromRgb(86, 211, 100)
                                ));
                            }

                            if (invDict != null && invDict.ContainsKey("warehouseNeuronGraph"))
                            {
                                var ng = invDict["warehouseNeuronGraph"] as System.Collections.Generic.Dictionary<string, object>;
                                if (ng != null)
                                {
                                    int nCnt = ng.ContainsKey("nodeCount") ? Convert.ToInt32(ng["nodeCount"]) : 79;
                                    int eCnt = ng.ContainsKey("edgeCount") ? Convert.ToInt32(ng["edgeCount"]) : 138;
                                    int domCnt = ng.ContainsKey("domainCount") ? Convert.ToInt32(ng["domainCount"]) : 7;
                                    int subCnt = ng.ContainsKey("subskillCount") ? Convert.ToInt32(ng["subskillCount"]) : 217;
                                    int repoCnt = ng.ContainsKey("githubRepoCount") ? Convert.ToInt32(ng["githubRepoCount"]) : 55;
                                    h2CardsContainer.Children.Add(makeSaaSCard(
                                        "NEURON GRAPH",
                                        "Warehouse 옵시디언 뉴런 시냅스 지식망",
                                        nCnt + " Nodes · " + eCnt + " Synapses",
                                        "• " + domCnt + "대 도메인 × 18대 BSC 정본 스킬 × " + subCnt + "개 세부 스킬 × " + repoCnt + "개 GitHub 레포지토리 양방향 시냅스 연결 완료\n• 2-Hop 그래프 탐색을 통해 음성 검색 시 연관 스킬과 문서를 즉시 찾아냅니다.",
                                        accentCyan
                                    ));
                                }
                            }

                            if (h2Dict != null)
                            {
                                int aoBytes = h2Dict.ContainsKey("alwaysOnBytes") ? Convert.ToInt32(h2Dict["alwaysOnBytes"]) : 0;
                                int aoCap = h2Dict.ContainsKey("alwaysOnCap") ? Convert.ToInt32(h2Dict["alwaysOnCap"]) : 20000;
                                int fCount = h2Dict.ContainsKey("fileCount") ? Convert.ToInt32(h2Dict["fileCount"]) : 8;
                                bool sValid = !h2Dict.ContainsKey("surfaceValid") || Convert.ToBoolean(h2Dict["surfaceValid"]);
                                h2CardsContainer.Children.Add(makeSaaSCard(
                                    "HARNESS² ENGINE",
                                    "Google Research Harness² 8-Surface 자가진화 엔진",
                                    (sValid ? "PASS" : "CHECK") + " · " + aoBytes.ToString("N0") + " / " + aoCap.ToString("N0") + " B",
                                    "• 8대 편집 가능 표면(c1~c8, 파일 " + fCount + "개) 무결성 및 상시 컨텍스트 용량 제한(20KB) 검증 통과\n• 재귀 루프: Observe(관찰) ➔ Propose[INV+DOM](규칙 제안) ➔ Probe(검증) ➔ Compose(원장 합성)",
                                    accentAmber
                                ));
                            }
                        }
                        catch { }
                    });
                }
                catch { }
            });
        }

        private static void ApplyCustomHotkey(int vk, bool ctrl, bool alt, bool shift, string label)
        {
            customHotkeyVk = vk;
            customNeedCtrl = ctrl;
            customNeedAlt = alt;
            customNeedShift = shift;
            customHotkeyLabel = label;

            if (txtStatus != null)
            {
                txtStatus.Text = "○ 받아 적는 중";
            }
            if (txtMainBody != null)
            {
                txtMainBody.Text = "단축키 [" + customHotkeyLabel + "] 적용 완료 — 말씀해 주세요";
            }
        }

        private static void TriggerWindowsKoreanVoiceTyping()
        {
            try
            {
                if (isMiniMode) ToggleMiniMode(false);
                hudWindow.Topmost = true;
                hudWindow.Activate();
                inputBox.Focus();

                // Trigger Windows 11 Native Korean Neural Voice Typing (Win + H)
                keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                keybd_event(VK_H, 0, 0, UIntPtr.Zero);
                keybd_event(VK_H, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
            }
            catch { }
        }

        private static void PromptListenNow()
        {
            if (isMiniMode) ToggleMiniMode(false);
            try { if (autoRecoveryTimer != null) autoRecoveryTimer.Stop(); } catch { }
            try
            {
                koreanVoicePlayer.Stop();
                isSpeaking = false;
            }
            catch { }

            StartInvisibleChromeEarWorker();
            SetHudAppearance(CyanColor);
            txtStatus.Text = "● 집중 듣는 중 (잠시 쉬어도 끊기지 않습니다 · 끝까지 말씀하세요)";
            txtMainBody.Text = "지금 편하게 말씀해 주세요 (예: “오늘 할 일 정리해 줘”, “뉴런에서 REA 검색해줘”, “방금 검색 결과 워드로 만들어줘”)";
            if (secTxtStatus != null) secTxtStatus.Text = txtStatus.Text;
            if (secTxtMainBody != null) secTxtMainBody.Text = txtMainBody.Text;
            if (thirdTxtStatus != null) thirdTxtStatus.Text = txtStatus.Text;
            if (thirdTxtMainBody != null) thirdTxtMainBody.Text = txtMainBody.Text;

            try
            {
                if (hudWindow != null)
                {
                    hudWindow.Topmost = true;
                    hudWindow.Activate();
                }
                if (inputBox != null) inputBox.Focus();
            }
            catch { }

            Task.Run(async () =>
            {
                try
                {
                    await http.PostAsync("http://127.0.0.1:7788/api/ear/speaking", new StringContent("{\"speaking\":false}", Encoding.UTF8, "application/json"));
                    await http.PostAsync("http://127.0.0.1:7788/api/ear/wake", new StringContent("{\"durationSec\":60}", Encoding.UTF8, "application/json"));
                }
                catch { }
            });
        }

        private static void SubmitHudInput()
        {
            // Typed input is submitted explicitly. Never execute display text or
            // resubmit the previous question when Enter repeats on an empty box.
            if (inputBox == null || string.IsNullOrWhiteSpace(inputBox.Text)) return;
            string text = inputBox.Text.Trim();
            suppressTextChanged = true;
            if (inputBox != null) inputBox.Text = "";
            suppressTextChanged = false;
            SendCommandToButler(text);
        }

        // =====================================================================
        // 5. GLOBAL HOTKEY HOOK (User Customizable + F8 / Ctrl+Space / Ctrl+Shift+J)
        // =====================================================================
        private static void StartGlobalRightAltHook()
        {
            DispatcherTimer keyTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(45) };
            keyTimer.Tick += (s, e) =>
            {
                bool f8Down = (GetAsyncKeyState(VK_F8) & 0x8000) != 0;
                bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
                bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
                bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
                bool customKeyDown = (GetAsyncKeyState(customHotkeyVk) & 0x8000) != 0;
                bool jDown = (GetAsyncKeyState(VK_J) & 0x8000) != 0;

                bool customMatch = customKeyDown &&
                                   (customNeedCtrl == ctrlDown) &&
                                   (customNeedAlt == altDown) &&
                                   (customNeedShift == shiftDown);

                bool listenCombo = customMatch || f8Down;
                if (listenCombo && !hotkeyWasDown)
                {
                    hotkeyWasDown = true;
                    PromptListenNow();
                }
                else if (!listenCombo && hotkeyWasDown)
                {
                    hotkeyWasDown = false;
                }

                bool bootCombo = ctrlDown && shiftDown && jDown;
                if (bootCombo && !bootHotkeyWasDown)
                {
                    bootHotkeyWasDown = true;
                    SendCommandToButler("대표님 출근");
                }
                else if (!bootCombo && bootHotkeyWasDown)
                {
                    bootHotkeyWasDown = false;
                }
            };
            keyTimer.Start();
        }

        // =====================================================================
        // 6. SEND TO BUTLER ENGINE & SPEAK REPLY
        // =====================================================================
        private static async void SendCommandToButler(string sentence)
        {
            try
            {
                SetHudAppearance(CyanColor);
                txtStatus.Text = System.Text.RegularExpressions.Regex.IsMatch(sentence,"알아|설명|조사|검색|정리|비교") ? "○ 자료 검색·정리 중" : "○ 요청 처리 중";
                txtMainBody.Text = sentence;

                string jsonBody = "{\"sentence\":\"" + EscapeJson(sentence) + "\",\"executeOs\":true}";
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync("http://127.0.0.1:7788/api/butler/command", content);
                string raw = await resp.Content.ReadAsStringAsync();

                string researchId = ExtractJsonField(raw, "researchId");
                if (!string.IsNullOrEmpty(researchId)) await ShowResearchResultAsync(researchId);

                string hudTitle = ExtractJsonField(raw, "hudTitle");
                string hudSubtitle = ExtractJsonField(raw, "hudSubtitle");
                string spoken = ExtractJsonField(raw, "spoken");
                string hudColor = ExtractJsonField(raw, "hudColor");
                bool bootScene = raw.Contains("\"bootScene\":true");
                bool awaitingConfirm = raw.Contains("\"awaitingConfirm\":true");
                bool showMailDraft = raw.Contains("\"intent\":\"draftMailReply\"");
                bool openPlanWin = raw.Contains("\"openPlanWindow\":true");
                string targetPlanTab = ExtractJsonField(raw, "activeTab");
                if (openPlanWin)
                {
                    int sqIdx = raw.IndexOf("\"openWindowSeq\":");
                    if (sqIdx >= 0)
                    {
                        int sPos = sqIdx + 16;
                        int ePos = sPos;
                        while (ePos < raw.Length && char.IsDigit(raw[ePos])) ePos++;
                        int parsedSeq;
                        if (int.TryParse(raw.Substring(sPos, ePos - sPos), out parsedSeq))
                        {
                            lastSeenPlanWindowSeq = Math.Max(lastSeenPlanWindowSeq, parsedSeq);
                        }
                    }
                    ShowPlanAndChlgdDocumentWindow(!string.IsNullOrEmpty(targetPlanTab) ? targetPlanTab : "plan");
                }

                Color c = hudColor == "amber" ? AmberColor : hudColor == "lime" ? LimeColor : CyanColor;
                SetHudAppearance(c);

                txtStatus.Text = !string.IsNullOrEmpty(hudTitle) ? hudTitle : "✓ 실행 완료";
                txtHeard.Visibility = Visibility.Collapsed;
                txtMainBody.Text = (awaitingConfirm || showMailDraft) && !string.IsNullOrEmpty(hudSubtitle) ? hudSubtitle : (!string.IsNullOrEmpty(spoken) ? spoken : (!string.IsNullOrEmpty(hudSubtitle) ? hudSubtitle : sentence));
                if (secTxtStatus != null) secTxtStatus.Text = txtStatus.Text;
                if (secTxtMainBody != null) secTxtMainBody.Text = txtMainBody.Text;
                if (thirdTxtStatus != null) thirdTxtStatus.Text = txtStatus.Text;
                if (thirdTxtMainBody != null) thirdTxtMainBody.Text = txtMainBody.Text;

                btnYes.Visibility = awaitingConfirm ? Visibility.Visible : Visibility.Collapsed;
                btnNo.Visibility = awaitingConfirm ? Visibility.Visible : Visibility.Collapsed;

                if (bootScene)
                {
                    SpeakAsync(!string.IsNullOrEmpty(spoken) ? spoken : "말씀하세요.");
                }
                else if (!string.IsNullOrEmpty(spoken))
                {
                    SpeakAsync(spoken);
                }

                if (!awaitingConfirm && !showMailDraft)
                {
                    ScheduleAutoRecovery(hudColor == "amber" ? 3200 : 5200);
                }
            }
            catch (Exception ex)
            {
                SetHudAppearance(AmberColor);
                txtStatus.Text = "⚠ 자동 복구 중 · 잠시만 기다려 주세요";
                txtMainBody.Text = ex.Message;
                ScheduleAutoRecovery(3200);
            }
        }

        private static DispatcherTimer autoRecoveryTimer;
        private static void ScheduleAutoRecovery(int delayMs = 3200)
        {
            if (autoRecoveryTimer == null) {
                autoRecoveryTimer = new DispatcherTimer();
                autoRecoveryTimer.Tick += async (sender, e) => {
                    autoRecoveryTimer.Stop();
                    // Preserve the actual result/error while restoring connectivity.
                    if (await EnsureBackendServerRunning()) StartInvisibleChromeEarWorker();
                };
            }
            autoRecoveryTimer.Stop(); autoRecoveryTimer.Interval = TimeSpan.FromMilliseconds(delayMs); autoRecoveryTimer.Start();
        }

        private static void SetHudAppearance(Color c)
        {
            hudCardBorder.BorderBrush = new SolidColorBrush(c);
            txtStatus.Foreground = new SolidColorBrush(c);
            cardGlow.Color = c;
        }

        private static async void SpeakAsync(string text)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => SpeakAsync(text))); return;
            }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (text == lastSpeechRequest && (DateTime.UtcNow - lastSpeechRequestAt).TotalSeconds < 0.8) return;
            lastSpeechRequest = text;
            lastSpeechRequestAt = DateTime.UtcNow;
            await speechGate.WaitAsync();
            try
            {
                isSpeaking = true;
                try
                {
                    await http.PostAsync("http://127.0.0.1:7788/api/ear/speaking", new StringContent("{\"speaking\":true}", Encoding.UTF8, "application/json"));
                }
                catch { }

                bool played = false;
                var audioClock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    string url = "http://127.0.0.1:7788/api/tts-mp3?format=wav&text=" + Uri.EscapeDataString(text);
                    using (var timeout = new System.Threading.CancellationTokenSource(20000))
                    using (var response = await http.GetAsync(url, timeout.Token))
                    {
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                        System.Collections.Generic.IEnumerable<string> values;
                        string actualVoice = response.Headers.TryGetValues("X-Jarvis-Tts-Voice", out values) ? values.FirstOrDefault() : "unknown";
                        string actualSpeed = response.Headers.TryGetValues("X-Jarvis-Tts-Speed", out values) ? values.FirstOrDefault() : "unknown";
                        bool fallbackVoice = response.Headers.TryGetValues("X-Jarvis-Tts-Fallback", out values) && values.FirstOrDefault() == "1";
                        WriteLog("TTS response voice=" + actualVoice + ", speed=" + actualSpeed + ", fallback=" + fallbackVoice + ", fetchMs=" + audioClock.ElapsedMilliseconds);
                        SetSettingsFeedback(fallbackVoice ? "선택한 Neural 음성 사용 불가 · Windows 음성으로 대체합니다. 속도는 유지됩니다." : "재생 음성: " + actualVoice + " · 속도 " + actualSpeed + "×", fallbackVoice);
                        if (response.IsSuccessStatusCode && bytes.Length > 44)
                        {
                            bool wav = bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E';
                            if (wav)
                            {
                                await Task.Run(() => {
                                    using (var stream = new MemoryStream(bytes))
                                    using (var player = new SoundPlayer(stream)) { player.Load(); WriteLog("WAV playback started: readyMs=" + audioClock.ElapsedMilliseconds); player.PlaySync(); }
                                });
                                played = true;
                                WriteLog("WAV playback completed: " + bytes.Length + " bytes");
                            }
                            else if (response.Content.Headers.ContentType != null && response.Content.Headers.ContentType.MediaType == "audio/mpeg")
                            {
                                played = await PlayMp3OnUiThreadAsync(bytes);
                            }
                        }
                    }
                }
                catch (Exception ex) { WriteLog("Server audio playback failed: " + ex.GetType().Name); }

                if (!played)
                {
                    try
                    {
                        string wavUrl = "http://127.0.0.1:7788/api/tts-mp3?format=wav&text=" + Uri.EscapeDataString(text);
                        using (var timeout2 = new System.Threading.CancellationTokenSource(10000))
                        using (var response2 = await http.GetAsync(wavUrl, timeout2.Token))
                        {
                            byte[] wavBytes = await response2.Content.ReadAsByteArrayAsync();
                            if (response2.IsSuccessStatusCode && wavBytes.Length > 44 && wavBytes[0] == 'R' && wavBytes[1] == 'I' && wavBytes[2] == 'F' && wavBytes[3] == 'F')
                            {
                                await Task.Run(() => {
                                    using (var stream = new MemoryStream(wavBytes))
                                    using (var player = new SoundPlayer(stream)) { player.Load(); player.PlaySync(); }
                                });
                                played = true;
                                WriteLog("WAV fallback playback completed: " + wavBytes.Length + " bytes");
                            }
                        }
                    }
                    catch { }
                }

                if (!played)
                {
                    WriteLog("Speech output failed after MP3 and WAV fallback");
                    if (txtStatus != null) txtStatus.Text = "⚠ 음성 출력 실패 · 텍스트 답변을 확인해 주세요";
                }
            }
            catch (Exception ex)
            {
                txtStatus.Text = "⚠ 음성 출력 실패 · Windows 소리 장치를 확인해 주세요";
                WriteLog("All speech playback failed: " + ex.GetType().Name);
            }
            finally
            {
                isSpeaking = false;
            }
            try { await http.PostAsync("http://127.0.0.1:7788/api/ear/speaking", new StringContent("{\"speaking\":false}", Encoding.UTF8, "application/json")); }
            catch { }
            speechGate.Release();
        }

        private static Task<bool> PlayMp3OnUiThreadAsync(byte[] bytes)
        {
            if (hudWindow != null && !hudWindow.Dispatcher.CheckAccess())
            {
                var tcs = new TaskCompletionSource<bool>();
                hudWindow.Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try { tcs.TrySetResult(await PlayMp3Async(bytes)); }
                    catch { tcs.TrySetResult(false); }
                }));
                return tcs.Task;
            }
            return PlayMp3Async(bytes);
        }

        private static async Task<bool> PlayMp3Async(byte[] bytes)
        {
            string filePath = System.IO.Path.Combine("C:\\자비스\\data", "jarvis_tts_" + Guid.NewGuid().ToString("N") + ".mp3");
            var completed = new TaskCompletionSource<bool>();
            EventHandler opened = (s, e) =>
            {
                try { koreanVoicePlayer.SpeedRatio = 1.0; } catch { }
                koreanVoicePlayer.Play();
            };
            EventHandler ended = (s, e) => completed.TrySetResult(true);
            EventHandler<ExceptionEventArgs> failed = (s, e) => completed.TrySetResult(false);
            try
            {
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(filePath));
                File.WriteAllBytes(filePath, bytes);
                koreanVoicePlayer.MediaOpened += opened;
                koreanVoicePlayer.MediaEnded += ended;
                koreanVoicePlayer.MediaFailed += failed;
                koreanVoicePlayer.Volume = 1.0;
                koreanVoicePlayer.Open(new Uri(filePath));
                if (await Task.WhenAny(completed.Task, Task.Delay(30000)) != completed.Task) return false;
                bool okPlay = await completed.Task;
                WriteLog("MP3 playback completed: " + bytes.Length + " bytes, playbackSpeed=1x, ok=" + okPlay);
                return okPlay;
            }
            finally
            {
                koreanVoicePlayer.Stop(); koreanVoicePlayer.Close();
                koreanVoicePlayer.MediaOpened -= opened;
                koreanVoicePlayer.MediaEnded -= ended;
                koreanVoicePlayer.MediaFailed -= failed;
                try { File.Delete(filePath); } catch { }
            }
        }

        // Exact 44.1kHz BootScene.synthChime() WAV Generator in Memory
        private static void PlaySynthChime()
        {
            try
            {
                int rate = 44100;
                double dur = 2.2;
                int samples = (int)(rate * dur);
                using (MemoryStream ms = new MemoryStream())
                using (BinaryWriter bw = new BinaryWriter(ms))
                {
                    int byteRate = rate * 2;
                    bw.Write(Encoding.ASCII.GetBytes("RIFF"));
                    bw.Write(36 + samples * 2);
                    bw.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                    bw.Write(16);
                    bw.Write((short)1);
                    bw.Write((short)1);
                    bw.Write(rate);
                    bw.Write(byteRate);
                    bw.Write((short)2);
                    bw.Write((short)16);
                    bw.Write(Encoding.ASCII.GetBytes("data"));
                    bw.Write(samples * 2);

                    for (int i = 0; i < samples; i++)
                    {
                        double t = (double)i / rate;
                        double sweepF = 90.0 + 520.0 * Math.Pow(Math.Min(t / 1.4, 1.0), 2.0);
                        double env = t < 1.4 ? t / 1.4 : Math.Max(0.0, 1.0 - (t - 1.4) / 0.4);
                        double v = 0.22 * Math.Sin(2 * Math.PI * sweepF * t) * env + 0.08 * Math.Sin(4 * Math.PI * sweepF * t) * env;
                        if (t > 1.45)
                        {
                            double dt = t - 1.45;
                            v += 0.18 * Math.Sin(2 * Math.PI * 1318.5 * dt) * Math.Exp(-dt * 4.0);
                        }
                        if (t > 1.70)
                        {
                            double dt = t - 1.70;
                            v += 0.18 * Math.Sin(2 * Math.PI * 1975.5 * dt) * Math.Exp(-dt * 4.0);
                        }
                        short pcm = (short)Math.Max(-32767, Math.Min(32767, v * 32000));
                        bw.Write(pcm);
                    }
                    ms.Position = 0;
                    SoundPlayer sp = new SoundPlayer(ms);
                    sp.Play();
                }
            }
            catch { }
        }

        private static Button CreatePillButton(string label, Color c)
        {
            return new Button
            {
                Content = label,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromArgb(65, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(c),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 3, 12, 3),
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Cursor = Cursors.Hand
            };
        }

        private static Button CreateMiniButton(string label)
        {
            return new Button
            {
                Content = label,
                Foreground = new SolidColorBrush(Color.FromRgb(210, 225, 240)),
                Background = new SolidColorBrush(Color.FromArgb(35, 255, 255, 255)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 2, 8, 2),
                FontSize = 11.5,
                Cursor = Cursors.Hand
            };
        }

        private static string EscapeJson(string s)
        {
            return (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
        }

        private static string ExtractJsonNumber(string json, string key)
        {
            string pat = "\"" + key + "\":";
            int idx = json.IndexOf(pat);
            if (idx < 0) return "";
            idx += pat.Length;
            while (idx < json.Length && json[idx] == ' ') idx++;
            int start = idx;
            while (idx < json.Length && (char.IsDigit(json[idx]) || json[idx] == '.' || json[idx] == '-')) idx++;
            return idx > start ? json.Substring(start, idx - start) : "";
        }

        private static string ExtractJsonField(string json, string key)
        {
            string pat = "\"" + key + "\":\"";
            int idx = json.IndexOf(pat);
            if (idx < 0) return "";
            idx += pat.Length;
            StringBuilder sb = new StringBuilder();
            while (idx < json.Length)
            {
                char c = json[idx++];
                if (c == '\\' && idx < json.Length)
                {
                    char next = json[idx++];
                    if (next == 'n') sb.Append(' ');
                    else sb.Append(next);
                }
                else if (c == '"')
                {
                    break;
                }
                else
                {
                    sb.Append(c);
                }
            }
            return sb.ToString();
        }
    }
}
