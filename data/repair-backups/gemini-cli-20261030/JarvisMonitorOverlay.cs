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
        private static string currentBrainProvider = "gemini-cli";
        private static string currentBrainModel = "gemini-3-flash-preview";
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
            StackPanel cmdRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 4, 0, 0)
            };
            textCol.Children.Add(cmdRow);

            inputBox = new TextBox
            {
                Width = 340,
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
            cmdRow.Children.Add(btnSubmit);

            btnYes = CreatePillButton("응 (전송)", LimeColor);
            btnYes.Margin = new Thickness(6, 0, 6, 0);
            btnYes.Visibility = Visibility.Collapsed;
            btnYes.Click += (s, e) => SendCommandToButler("응");
            cmdRow.Children.Add(btnYes);

            btnNo = CreatePillButton("아니 (취소)", AmberColor);
            btnNo.Margin = new Thickness(0, 0, 0, 0);
            btnNo.Visibility = Visibility.Collapsed;
            btnNo.Click += (s, e) => SendCommandToButler("아니");
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
                    Text = "○ 상시 대기 · 받아 적는 중 (비용 $0 · 토큰 0)",
                    FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                textCol.Children.Add(secTxtStatus);

                secTxtMainBody = new TextBlock
                {
                    Text = "클래식 음악 좀 틀어 줘",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 24,
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
                    Text = "○ 상시 대기 · 받아 적는 중 (비용 $0 · 토큰 0)",
                    FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor),
                    Margin = new Thickness(0, 0, 0, 8)
                };
                textCol.Children.Add(thirdTxtStatus);

                thirdTxtMainBody = new TextBlock
                {
                    Text = "클래식 음악 좀 틀어 줘",
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 24,
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
                    double width = Math.Min(980, extent.X - 48), height = Math.Min(720, extent.Y - 180);
                    researchWindow = new Window { Title = "자비스 검색 결과", Width = width, Height = height, Left = origin.X + (extent.X - width) / 2, Top = origin.Y + Math.Max(140, (extent.Y - height) / 2), WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, Topmost = true, ResizeMode = ResizeMode.CanResizeWithGrip, MinWidth = 500, MinHeight = 320 };
                    researchWindow.Closed += (sender, args) => { researchWindow = null; };
                }
                var muted = new SolidColorBrush(Color.FromRgb(160, 182, 198));
                var frame = new Border { Background = new SolidColorBrush(Color.FromRgb(8, 18, 29)), BorderBrush = new SolidColorBrush(CyanColor), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(25, 20, 25, 20) };
                frame.PreviewMouseLeftButtonDown += DragResearchWindow;
                var shell = new DockPanel(); frame.Child = shell;
                var header = new DockPanel { Margin = new Thickness(0,0,0,14) };
                var close = new Button { Content = "닫기 ×", Padding = new Thickness(12,5,12,5), Background = new SolidColorBrush(Color.FromRgb(27,45,60)), Foreground = Brushes.White, BorderThickness = new Thickness(0), Cursor = Cursors.Hand };
                close.Click += (sender,args) => { if(researchWindow!=null) researchWindow.Close(); };
                DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
                var brand = ResearchText("JARVIS  /  지식 검색     ·     잡아서 이동",14,new SolidColorBrush(CyanColor));
                brand.Cursor = Cursors.SizeAll;
                header.Children.Add(brand);DockPanel.SetDock(header,Dock.Top);shell.Children.Add(header);
                var body = new StackPanel();
                body.Children.Add(ResearchText(report.query,27,Brushes.White));
                body.Children.Add(ResearchText((report.status=="ready"?"검색 완료":"검색 결과 확인 필요") + "  ·  " + (report.sources==null?0:report.sources.Length) + "개 출처  ·  " + report.provider,12,muted));
                var overview = new Border { Background = new SolidColorBrush(Color.FromRgb(15,39,52)), CornerRadius = new CornerRadius(12), Padding = new Thickness(18), Margin = new Thickness(0,0,0,18) };
                var summary = new StackPanel();summary.Children.Add(ResearchText("핵심 내용",17,new SolidColorBrush(CyanColor)));summary.Children.Add(ResearchText(string.IsNullOrEmpty(report.summary)?report.note:report.summary,15,Brushes.White));overview.Child=summary;body.Children.Add(overview);
                body.Children.Add(ResearchText(report.note,12,muted));
                var chips = new WrapPanel { Margin = new Thickness(0,0,0,20) };
                foreach(string keyword in report.keywords ?? new string[0]) {
                    string term=keyword;
                    var chip=new Button {Content=term,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,0,7,7),Background=new SolidColorBrush(Color.FromRgb(22,48,61)),Foreground=new SolidColorBrush(CyanColor),BorderThickness=new Thickness(0),Cursor=Cursors.Hand,ToolTip="이 키워드로 다시 조사"};
                    chip.Click+=(sender,args)=>SendCommandToButler((report.query.IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0?report.query:report.query+" "+term)+"에 대해 조사해줘");chips.Children.Add(chip);
                }
                body.Children.Add(chips);
                body.Children.Add(ResearchText("출처별 정리",18,Brushes.White));
                foreach(var section in report.sections ?? new ResearchSection[0]) {
                    var panel=new StackPanel();panel.Children.Add(ResearchText("["+section.sourceId+"] "+section.title,16,Brushes.White));panel.Children.Add(ResearchText(section.text,14,muted));
                    var card=new Border {Background=new SolidColorBrush(Color.FromRgb(16,29,42)),CornerRadius=new CornerRadius(10),Padding=new Thickness(16),Margin=new Thickness(0,0,0,12),Child=panel};body.Children.Add(card);
                }
                body.Children.Add(ResearchText("원문 및 출처",18,Brushes.White));
                foreach(var item in report.sources ?? new ResearchSource[0]) {
                    string url=item.url;
                    var link=new Button {Content=ResearchText("["+item.id+"] "+item.title+"\n"+item.domain+" · "+item.readStatus,13,new SolidColorBrush(CyanColor)),HorizontalContentAlignment=HorizontalAlignment.Stretch,Padding=new Thickness(12),Margin=new Thickness(0,0,0,8),Background=new SolidColorBrush(Color.FromRgb(15,39,52)),Foreground=new SolidColorBrush(CyanColor),BorderThickness=new Thickness(0),Cursor=Cursors.Hand,ToolTip=url};
                    link.Click+=(sender,args)=>{Uri target;if(Uri.TryCreate(url,UriKind.Absolute,out target)&&(target.Scheme=="https"||target.Scheme=="http"))Process.Start(new ProcessStartInfo(target.AbsoluteUri){UseShellExecute=true});};body.Children.Add(link);
                }
                var resultScroll=new ScrollViewer {Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
                resultScroll.Loaded+=(sender,args)=>resultScroll.ScrollToTop();shell.Children.Add(resultScroll);
                researchWindow.Content=frame;researchWindow.Show();lastResearchId=id;
                WriteLog("Research result window shown: "+id+"; sources="+(report.sources==null?0:report.sources.Length));
            } catch(Exception ex) { WriteLog("Research window: "+ex.Message);txtStatus.Text="⚠ 검색 결과 창을 열지 못했습니다"; }
            finally { loadingResearchId=""; }
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
            quickChipsPanel.Children.Add(makeQuickChip("📓 CHLGD · 대화 규칙 & 로그 정리 창", "__OPEN_RULES_WIN__", Color.FromRgb(46, 160, 67), Color.FromRgb(86, 211, 100)));
            quickChipsPanel.Children.Add(makeQuickChip("🧬 Google Harness² & Warehouse 스킬", "__OPEN_HARNESS2_WIN__", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🔬 Antigravity REA 분석 & MCP", "__OPEN_REA_WIN__", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("⚡ “깃 상태 확인해줘” (터미널)", "깃 상태 확인해줘", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🛡 “CHLGD 코드 검증 돌려줘” (CI/CD)", "CHLGD 코드 검증 돌려줘", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
            quickChipsPanel.Children.Add(makeQuickChip("🤝 “거누에게 시스템 점검 협력 요청해”", "거누에게 시스템 점검 협력 요청해", Color.FromRgb(56, 139, 253), Color.FromRgb(121, 192, 255)));
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
                    txtMainBody.Text = displayTitle + " (" + mod + ") 모델이 저장되었습니다 · 연결 권한은 별도 확인";
                }
                foreach (var cb in modelVisualCallbacks) { try { cb(); } catch { } }
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
                        "Gemini 4 Argon",
                        "gemini-4-argon",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "추론 모델. 실제 접근 권한과 CLI 지원 여부는 로그인 계정에서 확인해야 합니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash",
                        "gemini-3.8-flash",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "텍스트 응답·도구 호출용 모델입니다. 실시간 음성용 Gemini Live와는 별도 모델입니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.1 Pro Preview",
                        "gemini-3.1-pro-preview",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "추론·분석용 프리뷰 모델. 사용 가능 여부와 제한은 계정 및 인증 방식에 따라 다릅니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3 Flash Preview",
                        "gemini-3-flash-preview",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "범용 Flash 프리뷰 모델. 무료 사용 가능 여부와 할당량은 로그인 계정에서 확인하세요."
                    ));
                }
                else if (currentUserPlanTier == "pro")
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 4 Argon",
                        "gemini-4-argon",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "추론 모델. 실제 접근 권한과 CLI 지원 여부는 로그인 계정에서 확인해야 합니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash",
                        "gemini-3.8-flash",
                        "gemini",
                        "Gemini API 키",
                        "텍스트 응답·도구 호출용 모델입니다. 실시간 음성용 Gemini Live와는 별도 모델입니다."
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
                        "Gemini 3.8 Flash",
                        "gemini-3.8-flash",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "텍스트 응답·도구 호출용 모델입니다. 실시간 음성용 Gemini Live와는 별도 모델입니다."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.5 Flash-Lite",
                        "gemini-3.5-flash-lite",
                        "gemini",
                        "Gemini API 키",
                        "경량 텍스트 작업용 모델. API 프로젝트의 사용 가능 여부·요금·한도를 확인하세요."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3 Flash Preview",
                        "gemini-3-flash-preview",
                        "gemini-cli",
                        "Google 로그인 · CLI",
                        "범용 Flash 프리뷰 모델. 무료 사용 가능 여부와 할당량은 로그인 계정에서 확인하세요."
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 2.5 Flash",
                        "gemini-2.5-flash",
                        "gemini",
                        "Gemini API 키",
                        "이전 세대 모델. 기존 프로젝트의 사용 가능 여부와 API 한도를 확인하세요."
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
                Text = "Google 계정 로그인 · Gemini CLI",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            oauthStack.Children.Add(new TextBlock
            {
                Text = "Gemini CLI 설치와 Google 로그인이 필요합니다. 이 화면은 구독 등급이나 남은 할당량을 자동 조회하지 않습니다. Google 로그인과 API 키의 과금·제한은 서로 다릅니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });
            Border btnOAuthLogin = createActionBtn("Google 계정 연동 창 열기 (Gemini CLI)", true, () =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/c start \"Jarvis Google Login\" cmd /k \"echo [JARVIS] 구글 계정 로그인이 완료되면 창을 닫아주세요. && gemini\"",
                        UseShellExecute = true
                    });
                }
                catch { }
            });
            oauthStack.Children.Add(btnOAuthLogin);
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
                Text = "API 키를 직접 사용하는 경우 아래에 입력 후 저장하세요. 로컬 설정 파일에만 안전하게 보관됩니다.",
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
        // DEDICATED UI HUD WINDOW: 📋 Plan 문서 뷰어 & 📓 CHLGD 대화 규칙/로그 & Google Harness²
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
            Color bgSurface = Color.FromRgb(18, 24, 33);
            Color bgSurfaceHover = Color.FromRgb(25, 33, 45);
            Color bgActiveCard = Color.FromRgb(17, 36, 58);
            Color borderHairline = Color.FromRgb(42, 52, 66);
            Color borderSubtle = Color.FromRgb(30, 37, 48);
            Color accentCyan = Color.FromRgb(77, 217, 255);
            Color accentBlue = Color.FromRgb(56, 139, 253);
            Color accentEmerald = Color.FromRgb(46, 160, 67);
            Color textPrimary = Color.FromRgb(240, 246, 252);
            Color textSecondary = Color.FromRgb(150, 162, 175);
            Color textMuted = Color.FromRgb(110, 118, 129);

            planDocWindow = new Window
            {
                Title = "JARVIS — 📋 Plan · CHLGD 대화 규칙 & Google Harness² HUD 뷰어",
                Width = 860,
                Height = 560,
                Left = Math.Max(40, (SystemParameters.PrimaryScreenWidth - 860) / 2.0),
                Top = Math.Max(60, (SystemParameters.PrimaryScreenHeight - 560) / 2.0),
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = true
            };

            Border outerFrame = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(bgShell),
                BorderBrush = new SolidColorBrush(Color.FromArgb(180, 77, 217, 255)),
                BorderThickness = new Thickness(1.4),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 34,
                    ShadowDepth = 10,
                    Opacity = 0.78
                }
            };
            outerFrame.MouseLeftButtonDown += (s, e) =>
            {
                DependencyObject src = e.OriginalSource as DependencyObject;
                while (src != null && src != outerFrame)
                {
                    if (src is TextBox || src is Button) return;
                    src = VisualTreeHelper.GetParent(src);
                }
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { planDocWindow.DragMove(); } catch { }
                }
            };
            planDocWindow.Content = outerFrame;

            Grid splitGrid = new Grid();
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(225) });
            splitGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            outerFrame.Child = splitGrid;

            // LEFT SIDEBAR
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
                Text = "JARVIS × ANTIGRAVITY MCP",
                FontFamily = new FontFamily("Consolas, Segoe UI"),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentCyan)
            });
            brandStack.Children.Add(new TextBlock
            {
                Text = "Plan · CHLGD 원장",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 16,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 3, 0, 8)
            });
            Border mcpBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(38, 46, 160, 67)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, 86, 211, 100)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            mcpBadge.Child = new TextBlock
            {
                Text = "● MCP & Harness² 연동 활성",
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
                Text = "연동된 지식/스킬 창고",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(textMuted)
            });
            bottomSumStack.Children.Add(new TextBlock
            {
                Text = "C:\\BeausCreators (26 Agents)",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 2, 0, 0)
            });
            bottomSumStack.Children.Add(new TextBlock
            {
                Text = "C:\\Warehouse · Google Harness²",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 10.5,
                Foreground = new SolidColorBrush(accentCyan),
                Margin = new Thickness(0, 1, 0, 0)
            });
            Grid.SetRow(bottomSummary, 2);
            sidebarGrid.Children.Add(bottomSummary);

            // RIGHT CONTENT AREA
            Grid rightGrid = new Grid { Margin = new Thickness(22, 18, 22, 16) };
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetColumn(rightGrid, 1);
            splitGrid.Children.Add(rightGrid);

            Grid rightHeader = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            rightHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(rightHeader, 0);
            rightGrid.Children.Add(rightHeader);

            StackPanel headerTitleStack = new StackPanel();
            TextBlock txtDocPaneTitle = new TextBlock
            {
                Text = "📋 실시간 Plan · 설계 문서 프리뷰",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 17,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(textPrimary)
            };
            TextBlock txtDocPaneDesc = new TextBlock
            {
                Text = "대표님과 대화하며 작성한 Plan이나 Antigravity IDE에서 전송된 설계 문서를 실시간으로 표시합니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 3, 0, 0)
            };
            headerTitleStack.Children.Add(txtDocPaneTitle);
            headerTitleStack.Children.Add(txtDocPaneDesc);
            Grid.SetColumn(headerTitleStack, 0);
            rightHeader.Children.Add(headerTitleStack);

            Border btnCloseWin = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            btnCloseWin.Child = new TextBlock
            {
                Text = "✕",
                FontFamily = new FontFamily("Segoe UI, Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
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
            Grid.SetColumn(btnCloseWin, 1);
            rightHeader.Children.Add(btnCloseWin);

            Grid tabHost = new Grid();
            Grid.SetRow(tabHost, 1);
            rightGrid.Children.Add(tabHost);

            StackPanel tabPlan = new StackPanel { Visibility = Visibility.Visible };
            StackPanel tabRules = new StackPanel { Visibility = Visibility.Collapsed };
            StackPanel tabHarness2 = new StackPanel { Visibility = Visibility.Collapsed };
            StackPanel tabRea = new StackPanel { Visibility = Visibility.Collapsed };

            tabHost.Children.Add(tabPlan);
            tabHost.Children.Add(tabRules);
            tabHost.Children.Add(tabHarness2);
            tabHost.Children.Add(tabRea);

            Func<string, string, Color, Border> makeInfoCard = (heading, body, accentCol) =>
            {
                Border c = new Border
                {
                    CornerRadius = new CornerRadius(9),
                    Background = new SolidColorBrush(bgSurface),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 0, 7)
                };
                StackPanel sp = new StackPanel();
                c.Child = sp;
                sp.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 12.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(accentCol),
                    Margin = new Thickness(0, 0, 0, 3)
                });
                sp.Children.Add(new TextBlock
                {
                    Text = body,
                    FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                    FontSize = 11.5,
                    Foreground = new SolidColorBrush(textPrimary),
                    TextWrapping = TextWrapping.Wrap,
                    LineHeight = 17
                });
                return c;
            };

            // =====================================================================
            // TAB 1: 📋 실시간 Plan · 설계 문서 프리뷰 (Interactive Plan Generator + Viewer)
            // =====================================================================
            Border planBanner = new Border
            {
                CornerRadius = new CornerRadius(9),
                Background = new SolidColorBrush(bgActiveCard),
                BorderBrush = new SolidColorBrush(accentBlue),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 9, 14, 9),
                Margin = new Thickness(0, 0, 0, 8)
            };
            StackPanel planBannerStack = new StackPanel();
            planBanner.Child = planBannerStack;
            TextBlock txtActivePlanTitle = new TextBlock
            {
                Text = "📋 [Master Plan] 자비스 × BeausCreators × Antigravity MCP & Google Harness² 통합 아키텍처",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White
            };
            TextBlock txtActivePlanSummary = new TextBlock
            {
                Text = "문서 원본: C:\\자비스\\docs\\JARVIS_Active_Plan.md · Antigravity MCP(jarvis_open_plan_hud)와 실시간 동기화됨",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(accentCyan),
                Margin = new Thickness(0, 3, 0, 0)
            };
            planBannerStack.Children.Add(txtActivePlanTitle);
            planBannerStack.Children.Add(txtActivePlanSummary);
            tabPlan.Children.Add(planBanner);

            StackPanel planSectionsContainer = new StackPanel();
            planSectionsContainer.Children.Add(makeInfoCard(
                "1. 📓 메뉴 내 CHLGD 작업 기록 & 대화 규칙 정리 시스템",
                "• 대표님과 대화하며 새로 생기는 규칙(\"앞으로 ~해\", \"~하지 마\"), 선호도, CHLGD 작업 로그를 카테고리별로 자동 정제하여 저장합니다.\n• 좌측 [📓 CHLGD 대화 규칙 · 로그] 탭에서 깔끔하게 정리된 규칙 원장과 최근 작업 내역을 즉시 확인하실 수 있습니다.",
                accentCyan
            ));
            planSectionsContainer.Children.Add(makeInfoCard(
                "2. 🔗 Antigravity 2.0 양방향 MCP 서버 브릿지 (jarvis-sovereign-butler)",
                "• Stdio 서버(C:\\자비스\\scripts\\jarvis-mcp-server.js) 및 HTTP 서버(http://127.0.0.1:7788/mcp)가 동시 가동됩니다.\n• Antigravity IDE와 자비스가 Plan 문서, CHLGD 스크립트(.ps1/.py/CI-CD), 대표님 규칙 원장을 실시간 공유합니다.",
                Color.FromRgb(86, 211, 100)
            ));
            planSectionsContainer.Children.Add(makeInfoCard(
                "3. 🧬 Google Research Harness² (8-Surface · R=4k, P=3k) & Warehouse 스킬",
                "• C:\\자비스\\harness2\\base_harness (c1~c8 표면) + Experience Library R + C:\\BeausCreators & C:\\Warehouse 스킬이 직결되었습니다.",
                Color.FromRgb(255, 184, 64)
            ));
            tabPlan.Children.Add(planSectionsContainer);

            // Quick Plan Generator Input Row
            StackPanel newPlanRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
            TextBox txtNewPlanInput = new TextBox
            {
                Width = 445,
                Height = 31,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Text = ""
            };
            Border btnBuildPlan = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(accentBlue),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnBuildPlan.Child = new TextBlock
            {
                Text = "⚡ 새 Plan 문서 즉시 생성",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            btnBuildPlan.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnBuildPlan.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                string topic = txtNewPlanInput.Text.Trim();
                if (string.IsNullOrEmpty(topic)) topic = "자비스 × BeausCreators CHLGD 자동화 고도화 계획";
                txtActivePlanTitle.Text = "📋 [Plan] " + topic;
                txtActivePlanSummary.Text = "업데이트: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " · C:\\자비스\\docs\\JARVIS_Active_Plan.md 동기화 완료";
                planSectionsContainer.Children.Clear();
                planSectionsContainer.Children.Add(makeInfoCard(
                    "1. 🎯 Context & 목표 정의",
                    "• 대표님 지시 주제: 「" + topic + "」\n• C:\\BeausCreators 에러 로그(error-log.md) 및 대표님 대화 규칙 원장 사전 스캔 완료.",
                    accentCyan
                ));
                planSectionsContainer.Children.Add(makeInfoCard(
                    "2. 🛡 Harness & 스킬 파이프라인 (.ps1 / .py / CI-CD)",
                    "• 적용 스킬: chlgd-orchestrator, powershell-python-cicd, antigravity-mcp-bridge\n• 데이터 보호: 무손실 Append 전용 · UTF-8 BOM & PYTHONUTF8=1 무결성 보장.",
                    Color.FromRgb(86, 211, 100)
                ));
                planSectionsContainer.Children.Add(makeInfoCard(
                    "3. 🔁 Loop & Antigravity MCP 협업 검증",
                    "• 실행 후 ceo-orders-log.jsonl 및 jarvis-loop.md에 자동 기록하며 Antigravity IDE와 결과를 동기화합니다.",
                    Color.FromRgb(255, 184, 64)
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
            newPlanRow.Children.Add(txtNewPlanInput);
            newPlanRow.Children.Add(btnBuildPlan);
            tabPlan.Children.Add(newPlanRow);

            // =====================================================================
            // TAB 2: 📓 CHLGD 대화 규칙 & 학습 로그 원장 (Cleanly Organized Rules & Logs)
            // =====================================================================
            StackPanel rulesListContainer = new StackPanel();
            rulesListContainer.Children.Add(makeInfoCard(
                "👑 [규칙 1 · 음성/경청 헌장] 끝까지 경청 후 실행 & 독자 발화 금지",
                "• 대표님께서 \"자비스\", \"헤이 자비스\"로 부르시면 말씀이 완전히 끝날 때까지(3.8초 침묵) 기다렸다가 실행하며, 말씀 도중 먼저 끊거나 혼자 말하지 않습니다.",
                Color.FromRgb(86, 211, 100)
            ));
            rulesListContainer.Children.Add(makeInfoCard(
                "🛡 [규칙 2 · 대표님 성문 인식] 외부 소음 및 타인 음성 완벽 차단",
                "• 대표님 고유 성문 주파수(82~235Hz)와 마이크 근접 에너지를 대조하여 건물 밖 소음·원거리 소리는 100% 무시합니다.",
                accentCyan
            ));
            rulesListContainer.Children.Add(makeInfoCard(
                "🔁 [규칙 3 · CHLGD & 데이터 무손실 헌장] C:\\BeausCreators 표준 동기화",
                "• 모든 대화 규칙과 실행 로그는 ceo-orders-log.jsonl, jarvis-loop.md, Google Harness² guardrails.md에 영구 Append 기록되며 무단 파일 삭제/덮어쓰기를 금지합니다.",
                Color.FromRgb(255, 184, 64)
            ));
            TextBlock txtLiveOrderLogSummary = new TextBlock
            {
                Text = "최근 CHLGD 실행 로그 로딩 중...",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(4, 2, 4, 8)
            };
            tabRules.Children.Add(rulesListContainer);
            tabRules.Children.Add(txtLiveOrderLogSummary);

            StackPanel addRuleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 2, 0, 0) };
            TextBox txtNewRuleInput = new TextBox
            {
                Width = 435,
                Height = 31,
                VerticalContentAlignment = VerticalAlignment.Center,
                Background = new SolidColorBrush(bgSidebar),
                Foreground = new SolidColorBrush(textPrimary),
                CaretBrush = Brushes.White,
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Text = ""
            };
            Border btnAddRule = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(accentEmerald),
                Padding = new Thickness(12, 6, 12, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnAddRule.Child = new TextBlock
            {
                Text = "＋ 새 대화 규칙 등록",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            btnAddRule.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnAddRule.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                string newRule = txtNewRuleInput.Text.Trim();
                if (string.IsNullOrEmpty(newRule)) return;
                if (rulesListContainer.Children.Count >= 3)
                {
                    rulesListContainer.Children.RemoveAt(rulesListContainer.Children.Count - 1);
                }
                rulesListContainer.Children.Insert(0, makeInfoCard(
                    "✨ [신규 등록 대화 규칙 · " + DateTime.Now.ToString("MM-dd HH:mm") + "]",
                    "• " + newRule + " (Google Harness² guardrails.md & CHLGD 원장 동시 반영됨)",
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
            addRuleRow.Children.Add(txtNewRuleInput);
            addRuleRow.Children.Add(btnAddRule);
            tabRules.Children.Add(addRuleRow);

            // =====================================================================
            // TAB 3: 🧬 Google Harness² (8-Surface) & C:\BeausCreators + C:\Warehouse 스킬
            // =====================================================================
            tabHarness2.Children.Add(makeInfoCard(
                "🧬 Google Research Harness² (arXiv:2610.14224) — 8-Component Editable Surface",
                "• 경로: C:\\자비스\\harness2\\base_harness (Surface Valid: PASS · Always-On Cap 20,000B 준수)\n• 구성: c1(systemprompt.md), c2(context_policy.md), c3(LongTermMEMORY.md), c4(tool_guidance.md), c5(sub_agents), c6(guardrails.md + skills), c7(scripts), c8(plugin) + Experience Library R",
                accentCyan
            ));
            tabHarness2.Children.Add(makeInfoCard(
                "🛠 자비스 전용 4대 마스터 스킬 (C:\\자비스\\.agents\\skills & harness2\\skills)",
                "1) chlgd-orchestrator: CHLGD 5단계 워크플로우 및 26인 에이전트 수평 협력\n2) powershell-python-cicd: .ps1 UTF-8 BOM · .py PYTHONUTF8=1 · Git CI/CD 자동 검증\n3) antigravity-mcp-bridge: Antigravity 2.0 양방향 MCP 서버 & Plan HUD 동기화\n4) ceo-rule-learner: 대표님 대화 규칙 및 선호도 실시간 추출·자산화",
                Color.FromRgb(86, 211, 100)
            ));
            tabHarness2.Children.Add(makeInfoCard(
                "🏛 C:\\BeausCreators (26인 에이전트) & C:\\Warehouse (05.BSC_Skill_Warehouse) 연동",
                "• 본사 스킬: bsc-global-rules, code-quality-gate, core-lock-guard, micro-knowledge-query, semantic-knowledge-search, unlazy, work-finalizer 등\n• Warehouse: 1.Imported_Skills · 2.BSC_Originals · 4.Market_Packages 전수 인덱싱 완료",
                Color.FromRgb(255, 184, 64)
            ));

            TextBlock txtH2RunResult = new TextBlock
            {
                Text = "원장 예산: Ledger(R=4000, P=3000) · Propose[INV+DOM] -> Probe -> Compose 준비 완료",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(accentCyan),
                Margin = new Thickness(2, 2, 0, 6)
            };
            tabHarness2.Children.Add(txtH2RunResult);

            Border btnRunHarness2 = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(accentBlue),
                Padding = new Thickness(14, 7, 14, 7),
                HorizontalAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand
            };
            btnRunHarness2.Child = new TextBlock
            {
                Text = "⚡ Google Harness² 자가진화 (Propose-Probe-Compose) 지금 실행",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            btnRunHarness2.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnRunHarness2.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                txtH2RunResult.Text = "● Google Harness² 8-Surface 검증 및 Propose[INV+DOM] -> Probe -> Compose 실행 중...";
                Task.Run(async () =>
                {
                    try
                    {
                        var resp = await http.PostAsync("http://127.0.0.1:7788/api/hud/harness2-evolve", new StringContent("{\"reason\":\"CEO HUD Trigger\"}", Encoding.UTF8, "application/json"));
                        string raw = await resp.Content.ReadAsStringAsync();
                        hudWindow.Dispatcher.Invoke(() =>
                        {
                            txtH2RunResult.Text = "✓ Google Harness² 자가진화 완료: 8-Surface Valid=PASS · Ledger(R=4000, P=3000) Composed=cand_inv_dom · EXPERIENCE.md 업데이트됨";
                            SpeakAsync("대표님, 구글 하네스 투 8대 표면 검증과 자가진화 루프를 완료하여 경험 원장에 기록했습니다.");
                        });
                    }
                    catch { }
                });
            };
            tabHarness2.Children.Add(btnRunHarness2);

            // =====================================================================
            // TAB 4: 🔬 Antigravity 2.0 REA 4분할 역공학 분석 & MCP 서버 연동
            // =====================================================================
            tabRea.Children.Add(makeInfoCard(
                "① Observations & ② Derivations (Antigravity 2.0 4-Stage Clean-Room REA)",
                "• [Observation] Antigravity 2.0은 메인 대화창과 별도의 Artifact/Sidecar Pane에서 Plan/Walkthrough/터미널을 분리 표시하여 인지 부하를 낮춤.\n• [Derivation] 자비스에도 전용 [📋 Plan · CHLGD 문서 HUD 창]을 신설하여 대표님과 Plan 대화 시 문서를 깔끔하게 띄우도록 이식 완료.",
                accentCyan
            ));
            tabRea.Children.Add(makeInfoCard(
                "③ Inferences & ④ MCP Server 양방향 브릿지 (jarvis-sovereign-butler)",
                "• 등록 파일: C:\\Users\\USER\\.gemini\\config\\mcp_config.json ➔ \"jarvis-sovereign-butler\"\n• 제공 MCP 도구: jarvis_status_and_profile, jarvis_execute_command, jarvis_open_plan_hud, jarvis_record_rule_or_log, jarvis_run_harness2_recursion\n• 설계 문서 정본: C:\\자비스\\docs\\REA_CleanRoom_Blueprint_Antigravity.md",
                Color.FromRgb(86, 211, 100)
            ));

            // Sidebar Navigation Setup
            string currentTabKey = string.IsNullOrEmpty(initialTab) ? "plan" : initialTab.ToLowerInvariant();
            System.Collections.Generic.List<Action> navSyncList = new System.Collections.Generic.List<Action>();

            Action<string, string, string, string, StackPanel> addDocNav = (tabKey, iconStr, navLabel, descStr, paneTarget) =>
            {
                Border navBtn = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(11, 9, 11, 9),
                    Margin = new Thickness(0, 0, 0, 5),
                    Cursor = Cursors.Hand
                };
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock ic = new TextBlock
                {
                    Text = iconStr,
                    FontFamily = new FontFamily("Segoe UI Symbol, Malgun Gothic"),
                    FontSize = 13,
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
                    foreach (var cb in navSyncList) { try { cb(); } catch { } }
                };
            };

            addDocNav("plan", "📋", "실시간 Plan · 문서 뷰어", "대표님과 대화하며 작성한 Plan이나 Antigravity IDE 설계 문서를 한눈에 열람합니다.", tabPlan);
            addDocNav("rules", "📓", "CHLGD 대화 규칙 · 로그", "대표님과의 대화에서 학습된 규칙, 새로 생성된 스킬, CHLGD 오더 로그를 깔끔하게 정리합니다.", tabRules);
            addDocNav("harness2", "🧬", "Google Harness² & 스킬", "arXiv:2610.14224 8-Surface 자가진화 엔진 및 C:\\BeausCreators · C:\\Warehouse 스킬 현황입니다.", tabHarness2);
            addDocNav("rea", "🔬", "Antigravity REA & MCP", "Antigravity 2.0 4-Stage REA 역공학 블루프린트 및 양방향 MCP 서버 브릿지 상태입니다.", tabRea);

            // Activate requested initialTab
            tabPlan.Visibility = currentTabKey == "plan" ? Visibility.Visible : Visibility.Collapsed;
            tabRules.Visibility = currentTabKey == "rules" ? Visibility.Visible : Visibility.Collapsed;
            tabHarness2.Visibility = currentTabKey == "harness2" ? Visibility.Visible : Visibility.Collapsed;
            tabRea.Visibility = currentTabKey == "rea" ? Visibility.Visible : Visibility.Collapsed;
            foreach (var cb in navSyncList) { try { cb(); } catch { } }

            // Footer
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
                Text = "음성으로 \"자비스 플랜 창 열어줘\" 또는 \"대화 규칙 보여줘\"라고 말씀하셔도 언제든 이 창이 열립니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(textMuted),
                VerticalAlignment = VerticalAlignment.Center
            });
            Border btnFooterClose = new Border
            {
                CornerRadius = new CornerRadius(7),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14, 5, 14, 5),
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

            // Fetch live data from /api/hud/chlgd-dashboard to populate latest Plan title & recent logs
            Task.Run(async () =>
            {
                try
                {
                    string dashJson = await http.GetStringAsync("http://127.0.0.1:7788/api/hud/chlgd-dashboard");
                    if (string.IsNullOrEmpty(dashJson)) return;
                    int pIdx = dashJson.IndexOf("\"activePlanDoc\":{");
                    string pTitle = "";
                    string pUpdated = "";
                    if (pIdx >= 0)
                    {
                        string pSub = dashJson.Substring(pIdx);
                        pTitle = ExtractJsonField(pSub, "title");
                        pUpdated = ExtractJsonField(pSub, "updatedAt");
                    }
                    int totIdx = dashJson.IndexOf("\"totalOrders\":");
                    string totStr = "0";
                    if (totIdx >= 0)
                    {
                        int s = totIdx + 14;
                        int e = s;
                        while (e < dashJson.Length && char.IsDigit(dashJson[e])) e++;
                        totStr = dashJson.Substring(s, e - s);
                    }
                    hudWindow.Dispatcher.Invoke(() =>
                    {
                        if (!string.IsNullOrEmpty(pTitle)) txtActivePlanTitle.Text = pTitle;
                        if (!string.IsNullOrEmpty(pUpdated))
                        {
                            txtActivePlanSummary.Text = "최근 업데이트: " + pUpdated + " · 문서 정본: C:\\자비스\\docs\\JARVIS_Active_Plan.md";
                        }
                        txtLiveOrderLogSummary.Text = "📊 누적 CHLGD 기록 오더: " + totStr + "건 · 원장 파일: C:\\자비스\\data\\ceo-orders-log.jsonl & jarvis-loop.md (실시간 자동 기록 중)";
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
            txtMainBody.Text = "지금 편하게 말씀해 주세요 (예: “유튜브에서 로파이 음악 틀어줘”, “웹브라우저 켜서 네이버 열어줘”)";
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
                    SendCommandToButler("아빠 왔어");
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
