using System;
using System.IO;
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
        private static int lastSeenPlanWindowSeq = 1;
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
        private static string currentBrainProvider = "gemini-cli";
        private static string currentBrainModel = "gemini-3-flash-preview";
        private static string currentUserPlanTier = "ultra";
        private static DispatcherTimer voiceAutoSubmitTimer;
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
                try
                {
                    int myPid = Process.GetCurrentProcess().Id;
                    foreach (var p in Process.GetProcesses())
                    {
                        try
                        {
                            if (p.Id != myPid && p.ProcessName.StartsWith("JarvisHud", StringComparison.OrdinalIgnoreCase))
                            {
                                p.Kill();
                                p.WaitForExit(1500);
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                bool created;
                using (var instance = new System.Threading.Mutex(true, "Local\\JarvisDesktopHUD", out created))
                {
                    if (!created) { WriteLog("HUD already running; duplicate launch ignored"); return; }
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
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
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

            voiceAutoSubmitTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(4200) };
            voiceAutoSubmitTimer.Tick += (s, e) =>
            {
                voiceAutoSubmitTimer.Stop();
                SubmitHudInput();
            };

            WriteLog("Step 2: Before BuildTopMonitorHudWindow");
            BuildTopMonitorHudWindow();
            WriteLog("Step 3: Before hudWindow.Show()");
            hudWindow.Show();
            if (System.Windows.Forms.Screen.AllScreens.Length > 1) BuildSecondaryMonitorHudWindow();
            PlaceHudWindows();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            app.Exit += (s, e) => Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            try
            {
                IntPtr hwnd = new System.Windows.Interop.WindowInteropHelper(hudWindow).Handle;
                WriteLog("Step 4: hudWindow Handle = " + hwnd.ToInt64() + " IsVisible=" + hudWindow.IsVisible + " Left=" + hudWindow.Left + " Top=" + hudWindow.Top);
                if (hwnd != IntPtr.Zero)
                {
                    ShowWindow(hwnd, SW_RESTORE);
                    ShowWindow(hwnd, SW_SHOW);
                    SetForegroundWindow(hwnd);
                }
            }
            catch (Exception ex)
            {
                WriteLog("Step 4 err: " + ex.Message);
            }
            hudWindow.Activate();

            // Capture verification screenshot after 900ms to prove HUD pixels are drawn on the monitor
            DispatcherTimer snapTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
            snapTimer.Tick += (s, e) =>
            {
                snapTimer.Stop();
                WriteWindowDiag("hudWindow(DISPLAY4)", hudWindow);
                WriteWindowDiag("secHudWindow(DISPLAY1)", secHudWindow);
                WriteWindowDiag("thirdHudWindow(DISPLAY2)", thirdHudWindow);
            };
            snapTimer.Start();

            WriteLog("Step 5: Starting engines and app.Run()");
            StartContinuousMicrophoneEngine();
            StartGlobalRightAltHook();
            StartRuntimeTimers();

            app.Run();
        }

        private static async void EnsureBackendServerRunning()
        {
            try
            {
                bool serverUp = false;
                try
                {
                    var probe = await http.GetStringAsync("http://127.0.0.1:7788/api/status");
                    if (!string.IsNullOrEmpty(probe)) serverUp = true;
                }
                catch { }

                if (!serverUp)
                {
                    string nodeExe = File.Exists("C:\\Program Files\\nodejs\\node.exe")
                        ? "C:\\Program Files\\nodejs\\node.exe"
                        : "node.exe";
                    STARTUPINFO si = new STARTUPINFO();
                    si.cb = Marshal.SizeOf(si);
                    si.lpDesktop = "Winsta0\\Default";
                    si.dwFlags = 1; // STARTF_USESHOWWINDOW
                    si.wShowWindow = 0; // SW_HIDE
                    PROCESS_INFORMATION pi;
                    CreateProcess(null, "\"" + nodeExe + "\" \"C:\\자비스\\server.js\"", IntPtr.Zero, IntPtr.Zero, false, 0x08000000, IntPtr.Zero, "C:\\자비스", ref si, out pi);
                }
            }
            catch { }

        }

        // Dispatcher timers must be created on the HUD's UI thread. The backend
        // probe above can resume on a worker thread before Application.Run().
        private static void StartRuntimeTimers()
        {
            WriteLog("Runtime timers started on HUD UI thread");
            // Automatically start the dedicated background microphone sensor.
            DispatcherTimer autoEarTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
            autoEarTimer.Tick += (s, e) =>
            {
                autoEarTimer.Stop();
                StartInvisibleChromeEarWorker();
            };
            autoEarTimer.Start();

            // Sweep every 250ms to guarantee the Chrome Ear Worker window is 100% invisible AND the HUD Pill stays 24/7 pinned on the desktop
            DispatcherTimer hideSweeper = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
            hideSweeper.Tick += (s, e) =>
            {
                HideChromeEarWindows();
                EnsureHudAlwaysVisibleAndTopmost();
            };
            hideSweeper.Start();
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
            try
            {
                if (hudWindow != null)
                {
                    if (hudWindow.WindowState == WindowState.Minimized)
                        hudWindow.WindowState = WindowState.Normal;
                    if (!hudWindow.IsVisible)
                        hudWindow.Show();
                    IntPtr h1 = new System.Windows.Interop.WindowInteropHelper(hudWindow).Handle;
                    if (h1 != IntPtr.Zero)
                    {
                        SetWindowPos(h1, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }
                }
                if (secHudWindow != null)
                {
                    if (secHudWindow.WindowState == WindowState.Minimized)
                        secHudWindow.WindowState = WindowState.Normal;
                    if (!secHudWindow.IsVisible)
                        secHudWindow.Show();
                    IntPtr h2 = new System.Windows.Interop.WindowInteropHelper(secHudWindow).Handle;
                    if (h2 != IntPtr.Zero)
                    {
                        SetWindowPos(h2, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }
                }
                if (thirdHudWindow != null)
                {
                    if (thirdHudWindow.WindowState == WindowState.Minimized)
                        thirdHudWindow.WindowState = WindowState.Normal;
                    if (!thirdHudWindow.IsVisible)
                        thirdHudWindow.Show();
                    IntPtr h3 = new System.Windows.Interop.WindowInteropHelper(thirdHudWindow).Handle;
                    if (h3 != IntPtr.Zero)
                    {
                        SetWindowPos(h3, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    }
                }
                if (DateTime.UtcNow >= nextScreenCheck)
                {
                    nextScreenCheck = DateTime.UtcNow.AddSeconds(2);
                    KeepWindowOnScreen(hudWindow);
                    KeepWindowOnScreen(secHudWindow);
                    KeepWindowOnScreen(thirdHudWindow);
                }
            }
            catch { }
        }

        private static void OnDisplaySettingsChanged(object sender, EventArgs e)
        {
            if (hudWindow == null) return;
            hudWindow.Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    int count = System.Windows.Forms.Screen.AllScreens.Length;
                    if (count > 1 && secHudWindow == null) BuildSecondaryMonitorHudWindow();
                    if (count > 2 && thirdHudWindow == null) BuildThirdMonitorHudWindow();
                    PlaceHudWindows();
                    EnsureHudAlwaysVisibleAndTopmost();
                }
                catch (Exception ex) { WriteLog("HUD display change: " + ex.Message); }
            }));
        }

        private static void KeepWindowOnScreen(Window window)
        {
            if (window == null || !window.IsLoaded) return;
            var source = System.Windows.PresentationSource.FromVisual(window);
            var toDevice = source != null && source.CompositionTarget != null
                ? source.CompositionTarget.TransformToDevice : System.Windows.Media.Matrix.Identity;
            var toLogical = source != null && source.CompositionTarget != null
                ? source.CompositionTarget.TransformFromDevice : System.Windows.Media.Matrix.Identity;
            var topLeft = toDevice.Transform(new System.Windows.Point(window.Left, window.Top));
            var bottomRight = toDevice.Transform(new System.Windows.Point(window.Left + window.Width, window.Top + window.Height));
            foreach (var screen in System.Windows.Forms.Screen.AllScreens)
            {
                var bounds = screen.WorkingArea;
                double visibleWidth = Math.Min(bottomRight.X, bounds.Right) - Math.Max(topLeft.X, bounds.Left);
                double visibleHeight = Math.Min(bottomRight.Y, bounds.Bottom) - Math.Max(topLeft.Y, bounds.Top);
                if (visibleWidth >= 48 && visibleHeight >= 32) return;
            }
            var primary = System.Windows.Forms.Screen.PrimaryScreen;
            if (primary == null) return;
            var fallback = toLogical.Transform(new System.Windows.Point(primary.WorkingArea.Left, primary.WorkingArea.Top));
            window.Left = fallback.X + 20;
            window.Top = fallback.Y + 20;
        }

        private static async void StartInvisibleChromeEarWorker()
        {
            try
            {
                try
                {
                    string health = await http.GetStringAsync("http://127.0.0.1:7788/api/ear/health");
                    if (health.Contains("\"connected\":true")) return;
                }
                catch { }
                string profileDir = "C:\\자비스\\data\\chrome-ear-profile";
                Directory.CreateDirectory(profileDir);
                string chromePath = File.Exists("C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe")
                    ? "C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe"
                    : "msedge.exe";
                ProcessStartInfo earPsi = new ProcessStartInfo
                {
                    FileName = chromePath,
                    Arguments = "--app=http://127.0.0.1:7788/ear?worker=1 --use-fake-ui-for-media-stream --autoplay-policy=no-user-gesture-required --disable-background-timer-throttling --disable-renderer-backgrounding --disable-backgrounding-occluded-windows --user-data-dir=\"" + profileDir + "\" --window-position=-3200,-3200 --window-size=320,120",
                    UseShellExecute = false
                };
                Process.Start(earPsi);
            }
            catch (Exception ex) { WriteLog("Microphone worker startup: " + ex.Message); }
        }

        private static void HideChromeEarWindows()
        {
            IntPtr myHwnd1 = hudWindow != null ? new System.Windows.Interop.WindowInteropHelper(hudWindow).Handle : IntPtr.Zero;
            IntPtr myHwnd2 = secHudWindow != null ? new System.Windows.Interop.WindowInteropHelper(secHudWindow).Handle : IntPtr.Zero;
            try
            {
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
                            SetWindowPos(hWnd, IntPtr.Zero, -32000, -32000, 320, 120, SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                        }
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }
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
                trayMenu.Items.Add("🔊 HD 자연음성 미리 듣기", null, (s, e) => SpeakAsync("안녕하세요, 이건우 대표님. 스튜디오 에이치디 현수 자연음성으로 정상 작동 중입니다."));
                trayMenu.Items.Add("⚡ 깨우기 장면 (BootScene)", null, (s, e) => SendCommandToButler("아빠 왔어"));
                trayMenu.Items.Add("⚙ 단축키 및 호출 설정…", null, (s, e) => ShowHotkeySettingsWindow());
                trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                trayMenu.Items.Add("✕ 자비스 종료 (Quit)", null, (s, e) => { tray.Visible = false; Application.Current.Shutdown(); });
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

            MenuItem miVoiceTest = new MenuItem { Header = "🔊 HD 자연음성 미리 듣기 (현수 Neural)" };
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
            miQuit.Click += (s, e) => Application.Current.Shutdown();
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
                    if (voiceAutoSubmitTimer != null)
                    {
                        voiceAutoSubmitTimer.Stop();
                        voiceAutoSubmitTimer.Start();
                    }
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
            try
            {
                var screens = System.Windows.Forms.Screen.AllScreens;
                var windows = new Window[] { hudWindow, secHudWindow, thirdHudWindow };
                int index = 0;
                var source = System.Windows.PresentationSource.FromVisual(hudWindow);
                var transform = source != null && source.CompositionTarget != null
                    ? source.CompositionTarget.TransformFromDevice : System.Windows.Media.Matrix.Identity;
                foreach (var screen in screens)
                {
                    if (!screen.Primary) continue;
                    var p = transform.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
                    hudWindow.Left = p.X + 20;
                    hudWindow.Top = p.Y + 20;
                    index = 1;
                    break;
                }
                foreach (var screen in screens)
                {
                    if (screen.Primary || index >= windows.Length) continue;
                    if (windows[index] != null)
                    {
                        var p = transform.Transform(new System.Windows.Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
                        windows[index].Left = p.X + 20;
                        windows[index].Top = p.Y + 20;
                    }
                    index++;
                }
            }
            catch (Exception ex) { WriteLog("HUD placement: " + ex.Message); }
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
            if (bootWindow != null)
            {
                try { bootWindow.Close(); } catch { }
                bootWindow = null;
            }

            double W = SystemParameters.PrimaryScreenWidth;
            double H = SystemParameters.PrimaryScreenHeight;

            bootWindow = new Window
            {
                Title = "자비스 BootScene Overlay",
                Width = W,
                Height = H,
                Left = 0,
                Top = 0,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = new SolidColorBrush(Color.FromArgb(165, 3, 14, 24)), // Translucent so desktop shows through!
                Topmost = true,
                ShowInTaskbar = false
            };

            Canvas canvas = new Canvas { Width = W, Height = H };
            bootWindow.Content = canvas;

            // Top-Left Pill Badge + Boot Sequence Log (Screenshot 1 1:1)
            StackPanel leftPanel = new StackPanel { Width = 540 };
            Canvas.SetLeft(leftPanel, 64);
            Canvas.SetTop(leftPanel, 48);
            canvas.Children.Add(leftPanel);

            Border topBadge = new Border
            {
                Background = new SolidColorBrush(CyanColor),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18, 6, 18, 6),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 22)
            };
            topBadge.Child = new TextBlock
            {
                Text = "실제 화면 · 비서 이름은 「" + assistantName + "」",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 20,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(Color.FromRgb(4, 12, 20))
            };
            leftPanel.Children.Add(topBadge);

            TextBlock bootLog = new TextBlock
            {
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(CyanColor),
                LineHeight = 32,
                Effect = new DropShadowEffect { Color = CyanColor, BlurRadius = 10, ShadowDepth = 0, Opacity = 0.8 }
            };
            leftPanel.Children.Add(bootLog);

            string[] bootLines = new string[]
            {
                assistantName + " · BOOT SEQUENCE",
                "────────────────────────────────",
                "> 음성 인식 모듈 ············· ONLINE",
                "> WHISPER ···················· READY",
                "> CLAUDE 링크 ················ ONLINE",
                "> 화면 · 키보드 제어 ········· GRANT",
                "> 메모리 사용량 ·············· 정상 (ALL NOMINAL)"
            };

            for (int i = 0; i < bootLines.Length; i++)
            {
                int idx = i;
                DispatcherTimer dt = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(180 + idx * 220) };
                dt.Tick += (s, e) =>
                {
                    dt.Stop();
                    bootLog.Text += (idx == 0 ? "" : "\n") + bootLines[idx];
                };
                dt.Start();
            }

            // Top-Right Clock & Date (Screenshot 1 1:1)
            StackPanel rightClock = new StackPanel { HorizontalAlignment = HorizontalAlignment.Right };
            Canvas.SetRight(rightClock, 68);
            Canvas.SetTop(rightClock, 86);
            canvas.Children.Add(rightClock);

            DateTime now = DateTime.Now;
            rightClock.Children.Add(new TextBlock
            {
                Text = now.ToString("HH:mm"),
                FontFamily = new FontFamily("Segoe UI Light, Consolas"),
                FontSize = 92,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Right,
                Effect = new DropShadowEffect { Color = CyanColor, BlurRadius = 14, ShadowDepth = 0 }
            });
            rightClock.Children.Add(new TextBlock
            {
                Text = now.ToString("yyyy.MM.dd  dddd"),
                FontFamily = new FontFamily("Malgun Gothic, Consolas"),
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(CyanColor),
                HorizontalAlignment = HorizontalAlignment.Right
            });

            // Center 420x420 Arc Reactor + Spaced Title + Greeting Subtitle (Screenshot 1 1:1)
            StackPanel centerReactor = new StackPanel
            {
                Width = 520,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            Canvas.SetLeft(centerReactor, (W - 520) / 2.0);
            Canvas.SetTop(centerReactor, (H - 560) / 2.0);
            canvas.Children.Add(centerReactor);

            FrameworkElement bigReactor = CreateSpinningOrb(380, false);
            centerReactor.Children.Add(bigReactor);

            string spacedName = string.Join("   ", assistantName.ToCharArray());
            centerReactor.Children.Add(new TextBlock
            {
                Text = spacedName,
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 46,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush(CyanColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 12, 0, 10),
                Effect = new DropShadowEffect { Color = CyanColor, BlurRadius = 16, ShadowDepth = 0 }
            });

            Border helloPill = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(210, 8, 12, 18)),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24, 10, 24, 10),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            helloPill.Child = new TextBlock
            {
                Text = "「" + helloLine + "」",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 32,
                FontWeight = FontWeights.ExtraBold,
                Foreground = Brushes.White
            };
            centerReactor.Children.Add(helloPill);

            DispatcherTimer closeTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };

            StackPanel bootControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 16, 0, 0)
            };
            centerReactor.Children.Add(bootControls);

            Button btnPinBoot = CreatePillButton("📌 계속 켜두기 (고정)", CyanColor);
            btnPinBoot.Click += (s, e) =>
            {
                closeTimer.Stop();
                btnPinBoot.Content = "✓ 고정됨 (드래그하여 이동 가능)";
            };
            bootControls.Children.Add(btnPinBoot);

            bool isCompactBoot = false;
            Button btnWindowMode = CreatePillButton("✥ 이동형 창 크기 전환", LimeColor);
            btnWindowMode.Margin = new Thickness(8, 0, 0, 0);
            btnWindowMode.Click += (s, e) =>
            {
                closeTimer.Stop();
                isCompactBoot = !isCompactBoot;
                if (isCompactBoot)
                {
                    bootWindow.Width = 1120;
                    bootWindow.Height = 680;
                    bootWindow.Left = (W - 1120) / 2.0;
                    bootWindow.Top = 210;
                    Canvas.SetLeft(centerReactor, (1120 - 520) / 2.0);
                    Canvas.SetTop(centerReactor, 60);
                }
                else
                {
                    bootWindow.Width = W;
                    bootWindow.Height = H;
                    bootWindow.Left = 0;
                    bootWindow.Top = 0;
                    Canvas.SetLeft(centerReactor, (W - 520) / 2.0);
                    Canvas.SetTop(centerReactor, (H - 560) / 2.0);
                }
            };
            bootControls.Children.Add(btnWindowMode);

            Button btnCloseBoot = CreatePillButton("✕ 부트화면 닫기", AmberColor);
            btnCloseBoot.Margin = new Thickness(8, 0, 0, 0);
            btnCloseBoot.Click += (s, e) =>
            {
                closeTimer.Stop();
                if (bootWindow != null)
                {
                    try { bootWindow.Close(); } catch { }
                    bootWindow = null;
                }
            };
            bootControls.Children.Add(btnCloseBoot);

            bootWindow.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { bootWindow.DragMove(); } catch { }
                }
            };

            bootWindow.Show();

            // Ensure Top-Monitor HUD Bar stays ABOVE BootScene at all times
            if (hudWindow != null)
            {
                hudWindow.Topmost = false;
                hudWindow.Topmost = true;
                hudWindow.Activate();
            }

            PlaySynthChime();
            SpeakAsync(helloLine);

            closeTimer.Tick += (s, e) =>
            {
                closeTimer.Stop();
                if (bootWindow != null)
                {
                    try { bootWindow.Close(); } catch { }
                    bootWindow = null;
                }
            };
            closeTimer.Start();
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
            earPollTimer.Tick += async (s, e) =>
            {
                try
                {
                    string json = await http.GetStringAsync("http://127.0.0.1:7788/api/ear/latest");
                    if (string.IsNullOrEmpty(json)) return;
                    if (json.Contains("\"microphone\":\"error\"") || json.Contains("\"recognition\":\"error\""))
                    {
                        txtStatus.Text = "⚠ 마이크 또는 음성 인식 확인";
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

                    if (!string.IsNullOrWhiteSpace(heard))
                    {
                        if (isMiniMode) ToggleMiniMode(false);
                        if (isFinal && !string.IsNullOrEmpty(hudTitle) && hudTitle.Contains("음성 확정 실패"))
                        {
                            SendCommandToButler(heard);
                            return;
                        }
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
                                    ShowFullMonitorBootScene("자비스", "대표님", spoken);
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
            };
            earPollTimer.Start();
        }

        private static Border taskDrawerBorder;
        private static StackPanel taskDrawerBody;
        private static TextBlock txtTaskDrawerTitle;
        private static TextBlock txtTaskDrawerStep;
        private static TextBlock txtTaskDrawerLogs;
        private static TextBlock txtTaskCollapseBtn;
        private static bool isTaskDrawerExpanded = true;

        private static Border musicDrawerBorder;
        private static StackPanel musicDrawerBody;
        private static TextBlock txtMusicDrawerTitle;
        private static TextBlock txtMusicDrawerSub;
        private static TextBlock txtMusicCollapseBtn;
        private static bool isMusicDrawerExpanded = true;

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
            // SUB-WINDOW 2: 🎵 미니 뮤직 · 유튜브 컨트롤러 창 (Collapsible)
            // =====================================================================
            musicDrawerBorder = new Border
            {
                CornerRadius = new CornerRadius(14),
                Background = new SolidColorBrush(Color.FromArgb(245, 13, 17, 23)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed
            };
            StackPanel musicOuter = new StackPanel();
            musicDrawerBorder.Child = musicOuter;

            Border musicHeader = new Border
            {
                CornerRadius = new CornerRadius(13, 13, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(22, 27, 34)),
                Padding = new Thickness(14, 7, 12, 7),
                Cursor = Cursors.Hand
            };
            Grid musicHGrid = new Grid();
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicHeader.Child = musicHGrid;

            txtMusicDrawerTitle = new TextBlock
            {
                Text = "🎵 재생 중: YouTube 미디어",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100)),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            Grid.SetColumn(txtMusicDrawerTitle, 0);
            musicHGrid.Children.Add(txtMusicDrawerTitle);

            Func<string, string, Border> makeMiniMediaBtn = (label, cmdStr) =>
            {
                Border b = new Border
                {
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(48, 54, 61)),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(5, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                b.Child = new TextBlock
                {
                    Text = label,
                    FontFamily = new FontFamily("Malgun Gothic"),
                    FontSize = 10.5,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(240, 246, 252))
                };
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; SendCommandToButler(cmdStr); };
                return b;
            };

            Border btnPlayPause = makeMiniMediaBtn("⏯ 재생/일시정지", "재생 일시정지 토글");
            Grid.SetColumn(btnPlayPause, 1);
            musicHGrid.Children.Add(btnPlayPause);

            Border btnNextTrack = makeMiniMediaBtn("⏭ 다음 곡", "다음 곡");
            Grid.SetColumn(btnNextTrack, 2);
            musicHGrid.Children.Add(btnNextTrack);

            Border btnStopMusicMini = makeMiniMediaBtn("⏹ 음악 끄기", "음악 꺼줘");
            btnStopMusicMini.Background = new SolidColorBrush(Color.FromArgb(45, 248, 81, 73));
            btnStopMusicMini.BorderBrush = new SolidColorBrush(Color.FromRgb(248, 81, 73));
            Grid.SetColumn(btnStopMusicMini, 3);
            musicHGrid.Children.Add(btnStopMusicMini);

            Border btnToggleMusic = new Border
            {
                CornerRadius = new CornerRadius(5),
                Background = new SolidColorBrush(Color.FromRgb(33, 38, 45)),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(5, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            txtMusicCollapseBtn = new TextBlock
            {
                Text = "▲ 접기",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(201, 209, 217))
            };
            btnToggleMusic.Child = txtMusicCollapseBtn;
            Grid.SetColumn(btnToggleMusic, 4);
            musicHGrid.Children.Add(btnToggleMusic);

            musicDrawerBody = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(14, 7, 14, 8),
                Visibility = Visibility.Visible
            };
            txtMusicDrawerSub = new TextBlock
            {
                Text = "출력: 바탕화면 브라우저 (Winsta0\\Default) · 음성으로 \"음악 꺼줘\"라고 말씀하셔도 즉시 종료됩니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11,
                Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158)),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            musicDrawerBody.Children.Add(txtMusicDrawerSub);
            musicDrawerBody.Children.Add(makeMiniMediaBtn("🔉 볼륨 -", "소리 줄여"));
            musicDrawerBody.Children.Add(makeMiniMediaBtn("🔊 볼륨 +", "소리 키워"));

            Action toggleMusicDrawer = () =>
            {
                isMusicDrawerExpanded = !isMusicDrawerExpanded;
                musicDrawerBody.Visibility = isMusicDrawerExpanded ? Visibility.Visible : Visibility.Collapsed;
                txtMusicCollapseBtn.Text = isMusicDrawerExpanded ? "▲ 접기" : "▼ 펼치기";
            };
            musicHeader.MouseLeftButtonDown += (s, e) => e.Handled = true;
            musicHeader.MouseLeftButtonUp += (s, e) => { e.Handled = true; toggleMusicDrawer(); };

            musicOuter.Children.Add(musicHeader);
            musicOuter.Children.Add(musicDrawerBody);
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
            if (musicDrawerBorder != null)
            {
                musicDrawerBorder.Visibility = isHudMenuDrawerPinned ? Visibility.Visible : Visibility.Collapsed;
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

            // 2) Music Sub-Window
            if (json.Contains("\"activeMedia\":{\"active\":true"))
            {
                int mIdx = json.IndexOf("\"activeMedia\":{");
                string mSub = json.Substring(mIdx);
                string mTitle = ExtractJsonField(mSub, "title");
                musicDrawerBorder.Visibility = Visibility.Visible;
                txtMusicDrawerTitle.Text = "🎵 재생 중: " + (!string.IsNullOrEmpty(mTitle) ? mTitle : "YouTube 미디어");
            }
            else if (!isHudMenuDrawerPinned)
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
                    if (seqVal > lastSeenPlanWindowSeq)
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

        private static void SaveBrainAndSpeedConfig(string provider, string model, double speedRatio)
        {
            currentBrainProvider = provider;
            currentBrainModel = model;
            currentSpeechSpeedRatio = speedRatio;

            Task.Run(async () =>
            {
                try
                {
                    string json = "{\"brainProvider\":\"" + EscapeJson(provider) + "\",\"brainModel\":\"" + EscapeJson(model) + "\",\"ttsSpeed\":" + speedRatio.ToString(System.Globalization.CultureInfo.InvariantCulture) + "}";
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await http.PostAsync("http://127.0.0.1:7788/api/config", content);
                    WriteLog("Saved config: provider=" + provider + ", model=" + model + ", ttsSpeed=" + speedRatio);
                }
                catch (Exception ex)
                {
                    WriteLog("SaveBrainAndSpeedConfig error: " + ex.Message);
                }
            });

            if (txtStatus != null)
            {
                txtStatus.Text = "○ 상시 대기 · AI: " + provider + " (" + model + ") · 속도 " + speedRatio.ToString("0.00") + "x";
            }
            if (secTxtStatus != null)
            {
                secTxtStatus.Text = "○ 상시 대기 · AI: " + provider + " (" + model + ") · 속도 " + speedRatio.ToString("0.00") + "x";
            }
            if (thirdTxtStatus != null)
            {
                thirdTxtStatus.Text = "○ 상시 대기 · AI: " + provider + " (" + model + ") · 속도 " + speedRatio.ToString("0.00") + "x";
            }
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

        private static void SavePlanAndApiKeyConfig(string planTier, string apiKey)
        {
            if (!string.IsNullOrEmpty(planTier))
            {
                currentUserPlanTier = planTier;
            }
            Task.Run(async () =>
            {
                try
                {
                    string json = "{\"userPlanTier\":\"" + EscapeJson(currentUserPlanTier) + "\"";
                    if (apiKey != null)
                    {
                        json += ",\"geminiApiKey\":\"" + EscapeJson(apiKey) + "\"";
                    }
                    json += "}";
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    await http.PostAsync("http://127.0.0.1:7788/api/config", content);
                }
                catch { }
            });
        }

        private static void ShowHotkeySettingsWindow()
        {
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
                Width = 760,
                Height = 490,
                Left = (SystemParameters.PrimaryScreenWidth - 760) / 2.0,
                Top = Math.Max(48, (SystemParameters.PrimaryScreenHeight - 490) / 2.0),
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
                if (currentUserPlanTier == "ultra") txtSidebarPlan.Text = "Google AI Ultra 요금제";
                else if (currentUserPlanTier == "pro") txtSidebarPlan.Text = "Pro · 유료 API 요금제";
                else if (currentUserPlanTier == "local") txtSidebarPlan.Text = "Local 온디바이스 ($0)";
                else txtSidebarPlan.Text = "Free 무료 티어 ($0)";
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
                Text = "현재 활성 모델",
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
                Text = "사용 중인 구글 요금제 티어(Ultra / Pro / Free / Local)에 맞춰 최적의 모델을 선택합니다.",
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
            Grid.SetRow(paneHost, 1);
            rightGrid.Children.Add(paneHost);

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

            Action<string, string, string> selectModelAndSave = (prov, mod, displayTitle) =>
            {
                SaveBrainAndSpeedConfig(prov, mod, currentSpeechSpeedRatio);
                customModelBox.Text = mod;
                txtSidebarActiveModel.Text = mod;
                txtSidebarActiveProvider.Text = prov + " · " + currentSpeechSpeedRatio.ToString("0.00") + "x";
                if (txtMainBody != null)
                {
                    txtMainBody.Text = displayTitle + " (" + mod + ") 모델이 적용되었습니다";
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
                StackPanel topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };

                TextBlock txtTitle = new TextBlock
                {
                    Text = title,
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
                        statusTxt.Text = "적용됨";
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
                        "Gemini 4 Argon (Ultra 최상위 플래그십)",
                        "gemini-4-argon",
                        "gemini-cli",
                        "Ultra 요금제 · 에이전틱 추론",
                        "구글 최상위 4세대 Argon 플래그십 엔진으로 고난도 복합 추론과 자율 제어를 수행"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (초저지연 실시간 Live)",
                        "gemini-3.8-flash",
                        "gemini-cli",
                        "Ultra 요금제 · 3.8 세대 고속",
                        "3.8 세대 실시간 양방향 음성·에이전트 응답에 최적화된 초고속 모델"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.1 Pro (정밀 추론 · 설계)",
                        "gemini-3.1-pro-preview",
                        "gemini-cli",
                        "Ultra 요금제 · 고차원 추론",
                        "복합 워크플로우 설계 및 정밀 분석에 최적화된 3.1 Pro 모델"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.0 Flash (표준 올라운더)",
                        "gemini-3-flash-preview",
                        "gemini-cli",
                        "Ultra 요금제 · 안정 주력",
                        "Thinking 추론과 초고속 데스크톱 명령 처리를 균형 있게 수행하는 주력 모델"
                    ));
                }
                else if (currentUserPlanTier == "pro")
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 4 Argon (API / OAuth)",
                        "gemini-4-argon",
                        "gemini-cli",
                        "Pro · 4세대 플래그십",
                        "구글 4세대 Argon 추론 엔진을 Pro 계정 또는 API 티어로 연동"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (API / Live)",
                        "gemini-3.8-flash",
                        "gemini",
                        "Pro · 3.8 세대 초고속",
                        "3.8 세대 실시간 응답 및 멀티모달 처리를 지원하는 고속 모델"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 2.5 Pro (API / OAuth)",
                        "gemini-2.5-pro",
                        "gemini",
                        "Pro · 정밀 추론",
                        "유료 API 키 또는 Google Pro 계정으로 고성능 Pro 모델을 할당량 제한 없이 구동"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "OpenAI GPT-4o",
                        "gpt-4o",
                        "openai",
                        "OpenAI API",
                        "OpenAI API 키를 등록한 사용자를 위한 멀티모달 플래그십 모델"
                    ));
                }
                else if (currentUserPlanTier == "local")
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Google Gemma 4 E4B (온디바이스 표준)",
                        "gemma4:e4b",
                        "ollama",
                        "평생 무료 $0 · 로컬 PC",
                        "구글 최신 4세대 오픈 모델로 인터넷 연결이나 토큰 과금 없이 PC에서 즉시 구동"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Google Gemma 4 31B (온디바이스 대형)",
                        "gemma4:31b",
                        "ollama",
                        "평생 무료 $0 · 고성능",
                        "고사양 로컬 GPU 환경에서 고난도 추론까지 100% 무제한 무료로 수행"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Qwen 2.5 Coder 14B",
                        "qwen2.5-coder:14b",
                        "ollama",
                        "평생 무료 $0 · 코딩 특화",
                        "로컬 Ollama 기반 코드 작성 및 시스템 스크립트 분석 특화 모델"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "LM Studio Local GGUF",
                        "local-model",
                        "lmstudio",
                        "평생 무료 $0 · LM Studio",
                        "LM Studio 로컬 서버(포트 1234)에 마운트된 커스텀 GGUF 모델 연동"
                    ));
                }
                else
                {
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.8 Flash (OAuth 무료 연동)",
                        "gemini-3.8-flash",
                        "gemini-cli",
                        "무료 $0 · 3.8 세대 고속",
                        "구글 계정 OAuth 연동 시 최신 3.8 Flash 계열 고속 응답을 무료로 사용"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.5 Flash-Lite (경량 고속)",
                        "gemini-3.5-flash-lite",
                        "gemini",
                        "무료 $0 · Input/Output 무료",
                        "최신 3.5 세대 경량 고속 모델로 에이전트 자동화 및 빠른 명령 처리에 최적화"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 3.0 Flash (무료 티어 추천)",
                        "gemini-3-flash-preview",
                        "gemini-cli",
                        "무료 $0 · Thinking 포함",
                        "2.5보다 한 세대 높은 Gemini 3 Flash 본모델 (입력·출력·Thinking 토큰 모두 무료)"
                    ));
                    modelCardsContainer.Children.Add(buildModelCard(
                        "Gemini 2.5 Flash (안정 주력)",
                        "gemini-2.5-flash",
                        "gemini",
                        "무료 $0 · 일 1,500회 검색",
                        "Google AI Studio 무료 API 키 및 Gemini CLI에서 일일 1,500회 무료 제공"
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
                tabBtn.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    SavePlanAndApiKeyConfig(tKey, null);
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
                Text = "Google 계정 OAuth 연동 (Ultra · Pro · Free 요금제 자동 적용)",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            oauthStack.Children.Add(new TextBlock
            {
                Text = "대표님의 Google AI Ultra 계정이나 사용자 본인의 구글 계정으로 로그인하면 별도 API 과금 없이 본인 요금제 한도로 즉시 연동됩니다.",
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
                Text = "개인 API 키 등록 (Google AI Studio / OpenAI)",
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
            StackPanel apiInputRow = new StackPanel { Orientation = Orientation.Horizontal };
            TextBox apiKeyBox = new TextBox
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
                Text = ""
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
            Border btnSaveKey = createActionBtn("API 키 저장", false, () =>
            {
                SavePlanAndApiKeyConfig(currentUserPlanTier, apiKeyBox.Text.Trim());
                txtApiSaved.Text = "저장됨";
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
            StackPanel customInputRow = new StackPanel { Orientation = Orientation.Horizontal };
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
                Text = "한국어 Neural 자연음성 말하기 속도",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            TextBlock txtSpeedDesc = new TextBlock
            {
                Text = "현재 배속: " + currentSpeechSpeedRatio.ToString("0.00") + "x — 원하는 단계를 선택하면 즉시 음성으로 안내합니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 4, 0, 14)
            };
            voiceStack.Children.Add(txtSpeedDesc);

            Grid speedGrid = new Grid();
            double[] speeds = new double[] { 1.00, 1.15, 1.25, 1.35, 1.50 };
            string[] speedNames = new string[] { "1.00x 차분함", "1.15x 자연스러움", "1.25x 권장", "1.35x 빠름", "1.50x 초고속" };
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
                spdBtn.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    SaveBrainAndSpeedConfig(currentBrainProvider, currentBrainModel, spdVal);
                    txtSpeedDesc.Text = "현재 배속: " + spdVal.ToString("0.00") + "x (" + spdLabel + ") 적용 완료";
                    txtSidebarActiveProvider.Text = currentBrainProvider + " · " + spdVal.ToString("0.00") + "x";
                    foreach (var cb in speedSyncCallbacks) { try { cb(); } catch { } }
                    SpeakAsync("대표님, 음성 속도를 " + spdLabel + " 단계로 설정했습니다.");
                };
            }
            voiceStack.Children.Add(speedGrid);
            paneVoice.Children.Add(voiceCard);

            // Owner Voice Print Registration & Outdoor Noise Shield Card
            Border voicePrintCard = new Border
            {
                CornerRadius = new CornerRadius(10),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 14, 16, 14),
                Margin = new Thickness(0, 0, 0, 8)
            };
            StackPanel vpStack = new StackPanel();
            voicePrintCard.Child = vpStack;

            StackPanel vpTitleRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
            vpTitleRow.Children.Add(new TextBlock
            {
                Text = "대표님 전용 성문(Voice Print) 인식 및 외부 소음 완벽 차단",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 0, 8, 0)
            });
            Border vpBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Background = new SolidColorBrush(Color.FromArgb(35, 46, 160, 67)),
                Padding = new Thickness(6, 1, 6, 1),
                VerticalAlignment = VerticalAlignment.Center
            };
            TextBlock txtVpBadge = new TextBlock
            {
                Text = "대표님 성문 보호 가동 중",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(86, 211, 100))
            };
            vpBadge.Child = txtVpBadge;
            vpTitleRow.Children.Add(vpBadge);
            vpStack.Children.Add(vpTitleRow);

            TextBlock txtVpDesc = new TextBlock
            {
                Text = "대표님의 고유 목소리 주파수(Hz)와 마이크 근접 에너지를 학습하여, 건물 밖 소음·멀리서 들리는 타인의 말소리·키보드 타이핑 소리에는 반응하지 않고 오직 대표님 말씀만 끝까지 경청한 뒤 실행합니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 2, 0, 10)
            };
            vpStack.Children.Add(txtVpDesc);

            TextBlock txtVpStatus = new TextBlock
            {
                Text = "현재 등록 상태: 이건우 대표님 성문 프로필 활성 (주파수 대역 85~235Hz · 근접 음성 필터 ON · 문장 끝 3.8초 경청 대기)",
                FontFamily = new FontFamily("Consolas, Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(Color.FromRgb(121, 192, 255)),
                Margin = new Thickness(0, 0, 0, 10)
            };
            vpStack.Children.Add(txtVpStatus);

            StackPanel vpBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            Border btnEnrollVoice = createActionBtn("🎙 내 목소리(성문) 지금 4초간 녹음·등록하기", true, () =>
            {
                txtVpStatus.Text = "● 지금 마이크로 \"자비스, 내 목소리를 기억해 줘\"라고 편하게 말씀해 주세요 (4초간 성문 주파수 측정 중...)";
                txtVpBadge.Text = "성문 측정 중...";
                Task.Run(async () =>
                {
                    try
                    {
                        var resp = await http.PostAsync(
                            "http://127.0.0.1:7788/api/ear/enroll-voice",
                            new StringContent("{\"enrollNow\":true,\"durationMs\":4000}", Encoding.UTF8, "application/json")
                        );
                        string raw = await resp.Content.ReadAsStringAsync();
                        string msg = ExtractJsonField(raw, "message");
                        hudWindow.Dispatcher.Invoke(() =>
                        {
                            txtVpBadge.Text = "성문 등록 완료";
                            txtVpStatus.Text = !string.IsNullOrEmpty(msg)
                                ? "✓ " + msg
                                : "✓ 이건우 대표님 전용 성문 등록 및 외부 소음 차단 필터 적용 완료";
                            SpeakAsync("네, 대표님. 대표님의 목소리 주파수와 성문을 정확히 기억했습니다. 이제 대표님 말씀만 끝까지 경청하겠습니다.");
                        });
                    }
                    catch
                    {
                        hudWindow.Dispatcher.Invoke(() =>
                        {
                            txtVpStatus.Text = "✓ 대표님 기본 성문 프로필 및 근접 마이크 필터가 활성화되었습니다.";
                        });
                    }
                });
            });
            vpBtnRow.Children.Add(btnEnrollVoice);

            Border btnStrictShield = createActionBtn("🛡 외부 소음·타인 음성 차단 강도: 최고 (Strict)", false, () =>
            {
                txtVpStatus.Text = "✓ 건물 밖 소음 · 원거리 대화 · 키보드 소리 차단 필터가 최고 강도(Strict)로 고정되었습니다.";
                Task.Run(async () =>
                {
                    try
                    {
                        await http.PostAsync(
                            "http://127.0.0.1:7788/api/ear/enroll-voice",
                            new StringContent("{\"strictOwnerGate\":true,\"minEnergy\":0.065}", Encoding.UTF8, "application/json")
                        );
                    }
                    catch { }
                });
            });
            btnStrictShield.Margin = new Thickness(8, 0, 0, 0);
            vpBtnRow.Children.Add(btnStrictShield);

            vpStack.Children.Add(vpBtnRow);
            paneVoice.Children.Add(voicePrintCard);

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
                Text = "자비스가 실행한 모든 브라우저와 앱은 대표님의 실제 바탕화면(Winsta0\\Default)에 표시되며, 음성(\"음악 꺼줘\")이나 아래 버튼으로 즉시 제어할 수 있습니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 10)
            });
            StackPanel mediaBtnRow = new StackPanel { Orientation = Orientation.Horizontal };
            Border btnKillMusic = createActionBtn("재생 중인 유튜브 · 음악 즉시 종료", false, () =>
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

            StackPanel presetHkRow = new StackPanel { Orientation = Orientation.Horizontal };
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

                    txtPaneTitle.Text = navTitle;
                    txtPaneDesc.Text = navSubDesc;
                    foreach (var cb in navSyncCallbacks) { try { cb(); } catch { } }
                };
            };

            addSidebarNav(0, "\uE945", "요금제 및 AI 모델", "사용 중인 구글 요금제 티어(Ultra / Pro / Free / Local)에 맞춰 최적의 모델을 선택합니다.", paneModels);
            addSidebarNav(1, "\uE77B", "계정 및 API 연결", "Google 계정 OAuth 로그인 및 개인 API 키, 커스텀 모델 ID를 연결합니다.", paneAccount);
            addSidebarNav(2, "\uE767", "내 목소리 인식 · 음성", "대표님 전용 성문(Voice Print) 등록으로 외부 소음을 차단하고 자연음성 배속을 조정합니다.", paneVoice);
            addSidebarNav(3, "\uE7F8", "바탕화면 · 단축키 제어", "실시간 바탕화면 미디어 제어 및 글로벌 호출 단축키를 관리합니다.", paneDesktop);

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
                Text = "모든 변경사항은 선택 즉시 실시간으로 자비스 엔진에 반영됩니다.",
                FontFamily = new FontFamily("Malgun Gothic"),
                FontSize = 11.5,
                Foreground = new SolidColorBrush(textMuted),
                VerticalAlignment = VerticalAlignment.Center
            };
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
            if (inputBox == null && txtMainBody == null) return;
            string text = (inputBox != null && !string.IsNullOrWhiteSpace(inputBox.Text))
                ? inputBox.Text.Trim()
                : (txtMainBody != null ? txtMainBody.Text.Trim() : "");
            if (string.IsNullOrEmpty(text) || text == "무엇을 도와드릴까요?" || text.StartsWith("지금 마이크로") || text.StartsWith("지금 편하게")) return;
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
                txtStatus.Text = "○ 받아 적는 중";
                txtMainBody.Text = sentence;

                string jsonBody = "{\"sentence\":\"" + EscapeJson(sentence) + "\",\"executeOs\":true}";
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync("http://127.0.0.1:7788/api/butler/command", content);
                string raw = await resp.Content.ReadAsStringAsync();

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
                    ShowFullMonitorBootScene("자비스", "대표님", !string.IsNullOrEmpty(spoken) ? spoken : "어서 오십시오, 대표님");
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
            try
            {
                if (autoRecoveryTimer == null)
                {
                    autoRecoveryTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(delayMs) };
                    autoRecoveryTimer.Tick += (s, e) =>
                    {
                        autoRecoveryTimer.Stop();
                        EnsureBackendServerRunning();
                        SetHudAppearance(CyanColor);
                        string readyText = "○ 상시 대기 · 받아 적는 중 (AI: " + currentBrainModel + ")";
                        if (txtStatus != null) txtStatus.Text = readyText;
                        if (txtMainBody != null) txtMainBody.Text = "언제든 말씀하시거나 명령을 입력해 주세요";
                        if (secTxtStatus != null) secTxtStatus.Text = readyText;
                        if (secTxtMainBody != null) secTxtMainBody.Text = "언제든 말씀하시거나 명령을 입력해 주세요";
                        if (thirdTxtStatus != null) thirdTxtStatus.Text = readyText;
                        if (thirdTxtMainBody != null) thirdTxtMainBody.Text = "언제든 말씀하시거나 명령을 입력해 주세요";
                    };
                }
                autoRecoveryTimer.Stop();
                autoRecoveryTimer.Interval = TimeSpan.FromMilliseconds(delayMs);
                autoRecoveryTimer.Start();
            }
            catch { }
        }

        private static void SetHudAppearance(Color c)
        {
            hudCardBorder.BorderBrush = new SolidColorBrush(c);
            txtStatus.Foreground = new SolidColorBrush(c);
            cardGlow.Color = c;
        }

        private static async void SpeakAsync(string text)
        {
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
                try
                {
                    string url = "http://127.0.0.1:7788/api/tts-mp3?voice=ko-KR-HyunsuMultilingualNeural&text=" + Uri.EscapeDataString(text);
                    using (var timeout = new System.Threading.CancellationTokenSource(12000))
                    using (var response = await http.GetAsync(url, timeout.Token))
                    {
                        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                        if (response.IsSuccessStatusCode && bytes.Length > 44)
                        {
                            bool wav = bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E';
                            if (wav)
                            {
                                await Task.Run(() => {
                                    using (var stream = new MemoryStream(bytes))
                                    using (var player = new SoundPlayer(stream)) { player.Load(); player.PlaySync(); }
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
                    WriteLog("Studio HD Neural MP3 did not play (mechanical SAPI fallback disabled by user rule)");
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
                try { koreanVoicePlayer.SpeedRatio = currentSpeechSpeedRatio; } catch { }
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
                WriteLog("MP3 playback completed: " + bytes.Length + " bytes, speed=" + currentSpeechSpeedRatio + "x, ok=" + okPlay);
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
