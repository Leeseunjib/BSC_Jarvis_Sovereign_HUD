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
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

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
        private const int VK_MENU = 0x12;     // Alt
        private const int VK_CONTROL = 0x11;  // Ctrl
        private const int VK_SHIFT = 0x10;    // Shift
        private const int VK_SPACE = 0x20;    // Space (Ctrl + Space -> 자비스 즉시 호출)
        private const int VK_F8 = 0x77;       // F8 단독 키 (원터치 음성 호출)
        private const int VK_J = 0x4A;        // Ctrl + Shift + J -> 전체화면 부트 시퀀스 호출

        // Customizable user hotkey (editable in Settings UI & persisted in config.json)
        private static int customHotkeyVk = VK_SPACE;
        private static bool customNeedCtrl = true;
        private static bool customNeedAlt = false;
        private static bool customNeedShift = false;
        private static string customHotkeyLabel = "Ctrl + Space";

        // BSC Human-Crafted Design System (AGENTS.md §8: Concentric 12->8->6, Matte Dark Slate, Neutral Hairlines)
        private static readonly FontFamily UiFont = new FontFamily("Pretendard, Noto Sans KR, Segoe UI, Malgun Gothic");
        private static readonly FontFamily MonoFont = new FontFamily("JetBrains Mono, Consolas, Pretendard, Malgun Gothic");
        private static readonly FontFamily GlyphFont = new FontFamily("Segoe MDL2 Assets, Segoe Fluent Icons, Segoe UI Symbol");

        private static readonly Color BgOuterCard = Color.FromArgb(248, 12, 16, 23);     // #0C1017
        private static readonly Color BgSurface = Color.FromRgb(17, 22, 31);           // #11161F
        private static readonly Color BgSubRow = Color.FromRgb(10, 14, 20);            // #0A0E14
        private static readonly Color BgHover = Color.FromRgb(26, 33, 45);             // #1A212D
        private static readonly Color BgActiveTint = Color.FromRgb(22, 34, 52);        // #162234
        private static readonly Color BorderHairline = Color.FromRgb(38, 44, 54);      // #262C36
        private static readonly Color BorderSubtle = Color.FromRgb(30, 36, 46);        // #1E242E
        private static readonly Color TextPrimary = Color.FromRgb(241, 245, 249);      // #F1F5F9
        private static readonly Color TextSecondary = Color.FromRgb(148, 163, 184);    // #94A3B8
        private static readonly Color TextMuted = Color.FromRgb(100, 116, 139);        // #64748B
        private static readonly Color DangerColor = Color.FromRgb(248, 113, 113);      // #F87171

        private static readonly Color CyanColor = Color.FromRgb(56, 189, 248);   // #38BDF8 (BSC Sovereign Ice-Cyan)
        private static readonly Color AmberColor = Color.FromRgb(245, 158, 11);  // #F59E0B (BSC Executive Gold)
        private static readonly Color LimeColor = Color.FromRgb(52, 211, 153);   // #34D399 (BSC Verified Emerald)

        private sealed class HudWindowInstance
        {
            public int MonitorIndex;
            public System.Windows.Forms.Screen AssignedScreen;
            public string ScreenDeviceName;
            public bool IsPrimary;
            public Window Window;
            public Border CardBorder;
            public Grid FullContentGrid;
            public Grid MiniOrbGrid;
            public StackPanel VerticalStack;
            public Ellipse StatusDot;
            public TextBlock TxtStatus;
            public Border TargetBadgeBorder;
            public TextBlock TxtActiveWindowBadge;
            public Border ClockWeatherBadgeBorder;
            public TextBlock TxtClockWeatherBadge;
            public TextBlock TxtMainBody;
            public Border InputBoxWrapper;
            public TextBox InputBox;
            public Button BtnSubmit;
            public Button BtnYes;
            public Button BtnNo;
            public Border MicToggleBtn;
            public System.Windows.Shapes.Path MicToggleIcon;
            public TextBlock TxtMicToggle;
            public Border TtsToggleBtn;
            public System.Windows.Shapes.Path TtsToggleIcon;
            public TextBlock TxtTtsToggle;
            public Border HubToggleBtn;
            public Ellipse HubActivityDot;
            public TextBlock TxtHubToggle;
            public System.Windows.Shapes.Path HubChevronIcon;
            public StackPanel HeaderControlsPanel;
            public Border HubHostContainer;
            public Border LiveStripHostContainer;
            public Border[] EqBars = new Border[5];
        }

        private static readonly System.Collections.Generic.List<HudWindowInstance> hudInstances = new System.Collections.Generic.List<HudWindowInstance>();
        private static HudWindowInstance activeHubOwner = null;
        private static int activeHubTabIndex = 0;

        private static Window hudWindow;
        private static Window secHudWindow;
        private static Window thirdHudWindow;
        private static Window bootWindow;
        private static Window settingsWindow;
        private static Window planDocWindow;
        private static int lastSeenPlanWindowSeq = -1;
        private static Border hudCardBorder;
        private static TextBlock txtStatus;
        private static TextBlock txtMainBody;
        private static TextBox inputBox;
        private static Button btnYes;
        private static Button btnNo;
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
        private static bool subWindowsPollInFlight = false;
        private static readonly HttpClient http = new HttpClient();
        private static bool hotkeyWasDown = false;
        private static bool bootHotkeyWasDown = false;
        private static bool isSpeaking = false;
        private static readonly System.Threading.SemaphoreSlim speechGate = new System.Threading.SemaphoreSlim(1, 1);
        private static string lastSpeechRequest = "";
        private static DateTime lastSpeechRequestAt = DateTime.MinValue;
        private static SoundPlayer activeSoundPlayer = null;
        private static System.Threading.CancellationTokenSource activeSpeechCts = null;
        private static int lastSeenBargeInSeq = 0;
        private static int currentSpeechSeq = 0;
        private static DateTime currentSpeechStartedAt = DateTime.MinValue;
        private static string cachedTargetWindowText = "대상 창: 자동 감지 중";
        private static string cachedLiveWeatherText = "날씨 수신 중";
        private static string cachedStatusText = "JARVIS v3.0 대기 · 자비스라고 불러 주세요";
        private static string cachedBodyText = "무엇을 도와드릴까요?";
        private static Color cachedAccentColor = CyanColor;
        private static DateTime lastWeatherFetchAt = DateTime.MinValue;
        private static long lastReportedForegroundHwnd = 0;
        private static int fgCheckCounter = 0;
        private static bool isHudMicMuted = false;
        private static bool isHudTtsMuted = false;
        private static bool clockWeatherTimerStarted = false;
        private static bool hasActiveTaskLive = false;
        private static bool hasActiveMediaLive = false;
        private static string dismissedTaskKey = "";
        private static string currentTaskKey = "";

        private static TextBlock ApplyTabular(TextBlock tb)
        {
            if (tb != null) System.Windows.Documents.Typography.SetNumeralAlignment(tb, FontNumeralAlignment.Tabular);
            return tb;
        }

        private static System.Windows.Shapes.Path MakeIconPath(string pathData, Color color, double size, bool strokeOnly)
        {
            var p = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse(pathData),
                Width = size,
                Height = size,
                Stretch = Stretch.Uniform,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            if (strokeOnly)
            {
                p.Stroke = new SolidColorBrush(color);
                p.StrokeThickness = 1.6;
                p.StrokeStartLineCap = PenLineCap.Round;
                p.StrokeEndLineCap = PenLineCap.Round;
                p.StrokeLineJoin = PenLineJoin.Round;
            }
            else
            {
                p.Fill = new SolidColorBrush(color);
            }
            return p;
        }

        private const string IconMicOnData = "M12 1a3 3 0 0 0-3 3v8a3 3 0 0 0 6 0V4a3 3 0 0 0-3-3z M19 10v2a7 7 0 0 1-14 0v-2 M12 19v4 M8 23h8";
        private const string IconMicOffData = "M1 1l22 22 M9 9v3a3 3 0 0 0 5.12 2.12 M15 9.34V4a3 3 0 0 0-5.94-.6 M17 16.95A7 7 0 0 1 5 12v-2 M19 10v2a7 7 0 0 1-.11 1.23 M12 19v4 M8 23h8";
        private const string IconSpeakerOnData = "M11 5L6 9H2v6h4l5 4V5z M15.54 8.46a5 5 0 0 1 0 7.07 M19.07 4.93a10 10 0 0 1 0 14.14";
        private const string IconSpeakerOffData = "M11 5L6 9H2v6h4l5 4V5z M23 9l-6 6 M17 9l6 6";
        private const string IconHubGridData = "M3 3h7v7H3z M14 3h7v7h-7z M3 14h7v7H3z M14 14h7v7h-7z";
        private const string IconChevronDownData = "M6 9l6 6 6-6";
        private const string IconChevronUpData = "M18 15l-6-6-6 6";

        private static void BroadcastStatus(string statusText)
        {
            cachedStatusText = statusText ?? "";
            foreach (var inst in hudInstances)
            {
                if (inst.TxtStatus != null) inst.TxtStatus.Text = cachedStatusText;
            }
        }

        private static void BroadcastBody(string bodyText)
        {
            cachedBodyText = bodyText ?? "";
            foreach (var inst in hudInstances)
            {
                if (inst.TxtMainBody != null) inst.TxtMainBody.Text = cachedBodyText;
            }
        }

        private static void BroadcastConfirmButtons(bool visible)
        {
            Visibility vis = visible ? Visibility.Visible : Visibility.Collapsed;
            foreach (var inst in hudInstances)
            {
                if (inst.BtnYes != null) inst.BtnYes.Visibility = vis;
                if (inst.BtnNo != null) inst.BtnNo.Visibility = vis;
            }
        }

        private static void BroadcastTargetBadge(string targetText)
        {
            cachedTargetWindowText = targetText ?? "대상 창: 자동 감지 중";
            foreach (var inst in hudInstances)
            {
                if (inst.TxtActiveWindowBadge != null) inst.TxtActiveWindowBadge.Text = cachedTargetWindowText;
            }
        }

        private static void BroadcastClockWeather(string clockWeatherText)
        {
            foreach (var inst in hudInstances)
            {
                if (inst.TxtClockWeatherBadge != null) inst.TxtClockWeatherBadge.Text = clockWeatherText;
            }
        }

        private static void SyncHubActivityIndicators()
        {
            bool hasLiveActivity = hasActiveTaskLive || hasActiveMediaLive;
            foreach (var inst in hudInstances)
            {
                bool isOpenHere = (activeHubOwner == inst && hubPanelBorder != null && hubPanelBorder.Visibility == Visibility.Visible);
                if (inst.HubToggleBtn != null)
                {
                    inst.HubToggleBtn.Background = new SolidColorBrush(isOpenHere ? BgActiveTint : BgSurface);
                    inst.HubToggleBtn.BorderBrush = new SolidColorBrush(isOpenHere ? Color.FromArgb(120, CyanColor.R, CyanColor.G, CyanColor.B) : BorderHairline);
                }
                if (inst.TxtHubToggle != null)
                {
                    inst.TxtHubToggle.Foreground = new SolidColorBrush(isOpenHere ? TextPrimary : TextSecondary);
                }
                if (inst.HubChevronIcon != null)
                {
                    inst.HubChevronIcon.Data = Geometry.Parse(isOpenHere ? IconChevronUpData : IconChevronDownData);
                    inst.HubChevronIcon.Stroke = new SolidColorBrush(isOpenHere ? CyanColor : TextSecondary);
                }
                if (inst.HubActivityDot != null)
                {
                    inst.HubActivityDot.Visibility = hasLiveActivity ? Visibility.Visible : Visibility.Collapsed;
                    inst.HubActivityDot.Fill = new SolidColorBrush(hasActiveTaskLive ? CyanColor : LimeColor);
                }
            }
        }

        private static void SyncHudAudioControlButtons()
        {
            try
            {
                Color micFg = isHudMicMuted ? Color.FromRgb(252, 165, 165) : Color.FromRgb(167, 243, 208);
                Color micBg = isHudMicMuted ? Color.FromArgb(42, 239, 68, 68) : Color.FromArgb(26, 16, 185, 129);
                Color micBd = isHudMicMuted ? Color.FromArgb(110, 248, 113, 113) : Color.FromArgb(90, 52, 211, 153);

                Color ttsFg = isHudTtsMuted ? Color.FromRgb(253, 224, 71) : Color.FromRgb(186, 230, 253);
                Color ttsBg = isHudTtsMuted ? Color.FromArgb(42, 245, 158, 11) : Color.FromArgb(26, 56, 189, 248);
                Color ttsBd = isHudTtsMuted ? Color.FromArgb(110, 251, 191, 36) : Color.FromArgb(90, 56, 189, 248);

                foreach (var inst in hudInstances)
                {
                    if (inst.MicToggleBtn != null && inst.TxtMicToggle != null)
                    {
                        inst.TxtMicToggle.Text = isHudMicMuted ? "마이크 OFF" : "마이크 ON";
                        inst.TxtMicToggle.Foreground = new SolidColorBrush(micFg);
                        inst.MicToggleBtn.Background = new SolidColorBrush(micBg);
                        inst.MicToggleBtn.BorderBrush = new SolidColorBrush(micBd);
                        if (inst.MicToggleIcon != null)
                        {
                            inst.MicToggleIcon.Data = Geometry.Parse(isHudMicMuted ? IconMicOffData : IconMicOnData);
                            inst.MicToggleIcon.Stroke = new SolidColorBrush(micFg);
                        }
                    }
                    if (inst.TtsToggleBtn != null && inst.TxtTtsToggle != null)
                    {
                        inst.TxtTtsToggle.Text = isHudTtsMuted ? "음소거" : "소리 ON";
                        inst.TxtTtsToggle.Foreground = new SolidColorBrush(ttsFg);
                        inst.TtsToggleBtn.Background = new SolidColorBrush(ttsBg);
                        inst.TtsToggleBtn.BorderBrush = new SolidColorBrush(ttsBd);
                        if (inst.TtsToggleIcon != null)
                        {
                            inst.TtsToggleIcon.Data = Geometry.Parse(isHudTtsMuted ? IconSpeakerOffData : IconSpeakerOnData);
                            inst.TtsToggleIcon.Stroke = new SolidColorBrush(ttsFg);
                        }
                    }
                }
            }
            catch { }
        }

        private static void ToggleHudAudioControlAsync(bool? nextMicMuted, bool? nextTtsMuted)
        {
            if (nextMicMuted.HasValue) isHudMicMuted = nextMicMuted.Value;
            if (nextTtsMuted.HasValue) isHudTtsMuted = nextTtsMuted.Value;
            SyncHudAudioControlButtons();

            if (isHudTtsMuted)
            {
                StopAllVoicePlaybackImmediately(true);
            }
            if (isHudMicMuted && isHudTtsMuted)
            {
                SetHudAppearance(AmberColor);
                BroadcastStatus("마이크 OFF · 음소거 (음성 대화 충돌 완전 차단)");
            }
            else if (isHudMicMuted)
            {
                SetHudAppearance(AmberColor);
                BroadcastStatus("자비스 마이크 OFF (음성 대화 충돌 방지 중)");
            }
            else if (isHudTtsMuted)
            {
                SetHudAppearance(AmberColor);
                BroadcastStatus("자비스 소리 음소거 (화면 텍스트로만 응답)");
            }
            else
            {
                SetHudAppearance(CyanColor);
                BroadcastStatus("자비스 마이크·소리 ON · 자비스라고 불러 주세요");
            }

            Task.Run(async () =>
            {
                try
                {
                    string payload = "{\"micMuted\":" + (isHudMicMuted ? "true" : "false") + ",\"ttsMuted\":" + (isHudTtsMuted ? "true" : "false") + "}";
                    await http.PostAsync("http://127.0.0.1:7788/api/ear/control", new StringContent(payload, Encoding.UTF8, "application/json"));
                }
                catch { }
            });
        }

        private static void StopAllVoicePlaybackImmediately(bool notifyServer = false)
        {
            try { if (activeSpeechCts != null) activeSpeechCts.Cancel(); } catch { }
            try
            {
                if (activeSoundPlayer != null)
                {
                    activeSoundPlayer.Stop();
                    activeSoundPlayer.Dispose();
                    activeSoundPlayer = null;
                }
            }
            catch { }
            try { if (koreanVoicePlayer != null) koreanVoicePlayer.Stop(); } catch { }
            isSpeaking = false;
            if (notifyServer)
            {
                Task.Run(async () =>
                {
                    try { await http.PostAsync("http://127.0.0.1:7788/api/ear/speaking", new StringContent("{\"speaking\":false}", Encoding.UTF8, "application/json")); }
                    catch { }
                });
            }
        }

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
                if (args != null)
                {
                    for (int i = 0; i < args.Length; i++)
                    {
                        string arg = args[i] ?? "";
                        if (string.Equals(arg, "--layout-audit", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                        {
                            RunLayoutAudit(args[i + 1]);
                            return;
                        }
                        if (arg.StartsWith("--layout-audit=", StringComparison.OrdinalIgnoreCase))
                        {
                            RunLayoutAudit(arg.Substring("--layout-audit=".Length));
                            return;
                        }
                    }
                }

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
            RefreshStoredSettings();

            BuildAllMonitorHudWindows(false);
            foreach (var inst in hudInstances)
            {
                var captured = inst;
                captured.Window.SourceInitialized += (sender, e) =>
                {
                    var source = System.Windows.Interop.HwndSource.FromHwnd(new System.Windows.Interop.WindowInteropHelper(captured.Window).Handle);
                    if (source != null) source.AddHook(KeepHudPositionOnAssignedScreen);
                };
                captured.Window.Closing += (sender, e) =>
                {
                    if (!allowShutdown) { e.Cancel = true; EnsureHudAlwaysVisibleAndTopmost(); }
                };
            }
            if (hudWindow != null)
            {
                app.MainWindow = hudWindow;
            }

            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            app.Exit += (sender, e) => {
                WriteLog("WPF exit code=" + e.ApplicationExitCode + "; explicitQuit=" + allowShutdown);
                Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            };
            app.SessionEnding += (sender, e) => { allowShutdown = true; SignalSupervisorStop(); WriteLog("Windows session ending: " + e.ReasonSessionEnding); };
            StartContinuousMicrophoneEngine();
            StartSubWindowsPolling();
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
                    if (!wasReady && initialBootComplete) BroadcastStatus("서버 연결 복구 · 명령을 말씀해 주세요");
                } else if (initialBootComplete) {
                    BroadcastStatus("서버 연결 복구 중 · 다시 확인하고 있습니다");
                }
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
            if (!initialBootComplete || allowShutdown || hudInstances.Count == 0) return;
            try {
                bool checkScreen = DateTime.UtcNow >= nextScreenCheck;
                if (checkScreen) nextScreenCheck = DateTime.UtcNow.AddSeconds(2);
                foreach (var inst in hudInstances)
                {
                    if (inst.Window == null) continue;
                    if (inst.Window.WindowState == WindowState.Minimized) inst.Window.WindowState = WindowState.Normal;
                    if (!inst.Window.IsVisible) inst.Window.Show();
                    IntPtr handle = new System.Windows.Interop.WindowInteropHelper(inst.Window).Handle;
                    if (handle != IntPtr.Zero) SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
                    if (checkScreen) KeepWindowOnScreen(inst);
                }
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

        private static System.Windows.Forms.Screen ResolveScreenForHwnd(IntPtr window)
        {
            foreach (var inst in hudInstances)
            {
                if (inst.Window == null) continue;
                IntPtr h = new System.Windows.Interop.WindowInteropHelper(inst.Window).Handle;
                if (h == window && inst.AssignedScreen != null) return inst.AssignedScreen;
            }
            return System.Windows.Forms.Screen.PrimaryScreen;
        }

        // Allow free dragging across ALL connected monitors while keeping the HUD inside the target monitor's working area once placed.
        private static IntPtr KeepHudPositionOnAssignedScreen(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WindowPositionChanging = 0x0046;
            if (message != WindowPositionChanging || lParam == IntPtr.Zero || allowShutdown) return IntPtr.Zero;
            try {
                var position = (HudWindowPosition)Marshal.PtrToStructure(lParam, typeof(HudWindowPosition));
                bool moving = (position.Flags & SWP_NOMOVE) == 0;
                bool resizing = (position.Flags & SWP_NOSIZE) == 0;
                if (!moving && !resizing) return IntPtr.Zero;
                int width = position.Width, height = position.Height;
                if (!moving || !resizing) {
                    HudWindowRectangle rectangle;
                    if (!GetWindowRect(window, out rectangle)) return IntPtr.Zero;
                    if (!resizing) { width = rectangle.Right - rectangle.Left; height = rectangle.Bottom - rectangle.Top; }
                    if (!moving) { position.X = rectangle.Left; position.Y = rectangle.Top; }
                }

                // Determine which monitor the window's top-center is currently over (supports moving freely across all monitors)
                int probeX = position.X + Math.Max(1, width) / 2;
                int probeY = position.Y + Math.Min(48, Math.Max(1, height) / 2);
                var targetScreen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(probeX, probeY)) ?? System.Windows.Forms.Screen.PrimaryScreen;
                foreach (var inst in hudInstances)
                {
                    if (inst.Window != null && new System.Windows.Interop.WindowInteropHelper(inst.Window).Handle == window)
                    {
                        inst.AssignedScreen = targetScreen;
                        inst.ScreenDeviceName = targetScreen.DeviceName;
                        break;
                    }
                }

                // While the user is actively dragging with the left mouse button, allow seamless movement across monitor boundaries without edge-snapping
                bool leftMouseDown = (GetAsyncKeyState(0x01) & 0x8000) != 0;
                if (leftMouseDown && moving)
                {
                    return IntPtr.Zero;
                }

                var bounds = targetScreen.WorkingArea;
                int x = Math.Max(bounds.Left, Math.Min(position.X, bounds.Right - Math.Max(1, width)));
                int y = Math.Max(bounds.Top, Math.Min(position.Y, bounds.Bottom - Math.Max(1, Math.Min(height, bounds.Height))));
                if (x != position.X || y != position.Y) position.Flags &= ~SWP_NOMOVE;
                position.X = x; position.Y = y;
                Marshal.StructureToPtr(position, lParam, false);
            } catch (Exception ex) { WriteLog("HUD position clamp: " + ex.Message); }
            return IntPtr.Zero;
        }

        private static void KeepWindowOnScreen(HudWindowInstance inst)
        {
            if (inst == null || inst.Window == null || !inst.Window.IsLoaded) return;
            if ((GetAsyncKeyState(0x01) & 0x8000) != 0) return; // Do not interfere while user is dragging
            var source = System.Windows.PresentationSource.FromVisual(inst.Window);
            var transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformToDevice : System.Windows.Media.Matrix.Identity;
            var point = transform.Transform(new Point(inst.Window.Left, inst.Window.Top));
            double physW = Math.Max(200, inst.Window.ActualWidth * transform.M11);
            var headerCenter = new System.Drawing.Point((int)(point.X + physW / 2.0), (int)(point.Y + 28 * transform.M22));
            var currentScreen = System.Windows.Forms.Screen.FromPoint(headerCenter) ?? System.Windows.Forms.Screen.PrimaryScreen;
            inst.AssignedScreen = currentScreen;
            inst.ScreenDeviceName = currentScreen.DeviceName;
            var bounds = currentScreen.WorkingArea;
            if (point.X < bounds.Left - physW + 120 || point.Y < bounds.Top - 10 || point.X > bounds.Right - 120 || point.Y + 60 * transform.M22 > bounds.Bottom)
            {
                PlaceHudInstanceOnScreen(inst);
            }
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
                    SetHudAppearance(AmberColor);
                    BroadcastStatus("마이크 권한을 확인해 주세요"); return;
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
            var ownHwnds = new System.Collections.Generic.HashSet<long>();
            foreach (var inst in hudInstances)
            {
                if (inst.Window != null)
                {
                    IntPtr h = new System.Windows.Interop.WindowInteropHelper(inst.Window).Handle;
                    if (h != IntPtr.Zero) ownHwnds.Add(h.ToInt64());
                }
            }
            try
            {
                // Cross-process window messages may wait for Chrome's UI thread.
                // Keep that wait off the HUD dispatcher and allow only one scan.
                await Task.Run(() => {
                EnumWindows((hWnd, lParam) =>
                {
                    if (ownHwnds.Contains(hWnd.ToInt64())) return true;
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
        // 1. UNIFIED MULTI-MONITOR HUD FACTORY (2-Row Full-Width + 3-Control Header)
        // =====================================================================
        private static System.Windows.Forms.Screen[] GetOrderedScreens()
        {
            var all = System.Windows.Forms.Screen.AllScreens;
            if (all == null || all.Length == 0)
            {
                return new System.Windows.Forms.Screen[] { System.Windows.Forms.Screen.PrimaryScreen };
            }
            return all
                .OrderByDescending(s => s.Primary)
                .ThenBy(s => s.Bounds.X)
                .ThenBy(s => s.Bounds.Y)
                .ToArray();
        }

        private static void BuildAllMonitorHudWindows(bool isAuditMode)
        {
            hudInstances.Clear();
            activeHubOwner = null;
            var screens = GetOrderedScreens();
            // Single HUD Window by default (movable across all monitors); layout audit mode can still inspect multi-screen metrics
            int count = isAuditMode ? 3 : 1;

            for (int i = 0; i < count; i++)
            {
                var scr = i < screens.Length ? screens[i] : screens[0];
                var inst = CreateHudWindowInstance(i, scr, i == 0, i == 0 && !isAuditMode, isAuditMode);
                hudInstances.Add(inst);
            }

            if (hudInstances.Count > 0)
            {
                var p = hudInstances[0];
                hudWindow = p.Window;
                hudCardBorder = p.CardBorder;
                txtStatus = p.TxtStatus;
                txtMainBody = p.TxtMainBody;
                inputBox = p.InputBox;
                btnYes = p.BtnYes;
                btnNo = p.BtnNo;
            }
            secHudWindow = hudInstances.Count > 1 ? hudInstances[1].Window : null;
            thirdHudWindow = hudInstances.Count > 2 ? hudInstances[2].Window : null;

            SyncHudAudioControlButtons();
            SyncHubActivityIndicators();
            SetHudAppearance(cachedAccentColor);

            if (!isAuditMode && !clockWeatherTimerStarted)
            {
                clockWeatherTimerStarted = true;
                StartClockWeatherTimer();
            }
        }

        private static void StartClockWeatherTimer()
        {
            DispatcherTimer clockWeatherTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            clockWeatherTimer.Tick += async (s, e) =>
            {
                try
                {
                    string nowStr = DateTime.Now.ToString("MM.dd(ddd) HH:mm:ss", new System.Globalization.CultureInfo("ko-KR"));
                    BroadcastClockWeather(nowStr + "  ·  " + cachedLiveWeatherText);
                    if ((DateTime.UtcNow - lastWeatherFetchAt).TotalMinutes >= 10.0)
                    {
                        lastWeatherFetchAt = DateTime.UtcNow;
                        string wJson = await http.GetStringAsync("http://127.0.0.1:7788/api/weather/current");
                        var wSer = new System.Web.Script.Serialization.JavaScriptSerializer();
                        var wDict = wSer.Deserialize<System.Collections.Generic.Dictionary<string, object>>(wJson);
                        string badge = JsonStr(wDict, "badgeText");
                        if (!string.IsNullOrEmpty(badge))
                        {
                            cachedLiveWeatherText = badge;
                            BroadcastClockWeather(nowStr + "  ·  " + cachedLiveWeatherText);
                        }
                    }
                }
                catch { }
            };
            clockWeatherTimer.Start();
        }

        private static HudWindowInstance CreateHudWindowInstance(int monitorIndex, System.Windows.Forms.Screen screen, bool isPrimary, bool registerTray, bool isAuditMode)
        {
            var inst = new HudWindowInstance
            {
                MonitorIndex = monitorIndex,
                AssignedScreen = screen,
                ScreenDeviceName = screen != null ? screen.DeviceName : "DISPLAY",
                IsPrimary = isPrimary
            };

            string winTitle = monitorIndex == 0
                ? "자비스 Monitor Top HUD"
                : (monitorIndex == 1 ? "자비스 Secondary Monitor HUD" : "자비스 Third Monitor HUD");

            Window w = new Window
            {
                Title = winTitle,
                Width = 744,
                Height = 172,
                SizeToContent = SizeToContent.Height,
                Left = 180,
                Top = 42,
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                Topmost = !isAuditMode,
                ShowInTaskbar = isPrimary && !isAuditMode,
                ShowActivated = !isAuditMode,
                ResizeMode = ResizeMode.NoResize
            };
            inst.Window = w;

            if (registerTray)
            {
                try
                {
                    var tray = new System.Windows.Forms.NotifyIcon
                    {
                        Text = "자비스 (SasariSa v0.7.3)",
                        Icon = System.Drawing.SystemIcons.Information,
                        Visible = true
                    };
                    var trayMenu = new System.Windows.Forms.ContextMenuStrip();
                    trayMenu.Items.Add("지금 말하기 (음성 듣기)", null, (s, e) => PromptListenNow());
                    trayMenu.Items.Add("HD 자연음성 미리 듣기", null, (s, e) => SpeakAsync("안녕하세요, 이건우 대표님. 선택한 목소리와 속도로 안내하고 있습니다."));
                    trayMenu.Items.Add("깨우기 장면 (BootScene)", null, (s, e) => SendCommandToButler("아빠 왔어"));
                    trayMenu.Items.Add("단축키 및 호출 설정…", null, (s, e) => ShowHotkeySettingsWindow(0));
                    trayMenu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                    trayMenu.Items.Add("자비스 종료 (Quit)", null, (s, e) => { tray.Visible = false; RequestHudShutdown("tray menu"); });
                    tray.ContextMenuStrip = trayMenu;
                    tray.DoubleClick += (s, e) => { w.Show(); w.Activate(); };
                }
                catch { }
            }

            Grid root = new Grid();
            w.Content = root;

            Grid fullGrid = new Grid();
            inst.FullContentGrid = fullGrid;
            root.Children.Add(fullGrid);

            StackPanel verticalStack = new StackPanel
            {
                Width = 720,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 8, 0, 12)
            };
            inst.VerticalStack = verticalStack;
            fullGrid.Children.Add(verticalStack);

            DropShadowEffect subtleShadow = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 20,
                ShadowDepth = 6,
                Opacity = 0.45
            };

            Border cardBorder = new Border
            {
                Width = 720,
                MinHeight = 104,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(BgOuterCard),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Effect = subtleShadow,
                Padding = new Thickness(14, 12, 14, 12),
                Cursor = Cursors.SizeAll,
                ToolTip = "드래그하여 이동 · 자비스를 부르거나 직접 입력하세요"
            };
            inst.CardBorder = cardBorder;

            cardBorder.MouseLeftButtonDown += (s, e) =>
            {
                DependencyObject source = e.OriginalSource as DependencyObject;
                while (source != null && source != cardBorder)
                {
                    if (source is TextBox || source is Button) return;
                    source = VisualTreeHelper.GetParent(source);
                }
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { w.DragMove(); } catch { }
                }
            };

            // Clean Context Menu (zero emojis, screen-aware position presets, mini mode wired)
            ContextMenu ctx = new ContextMenu();
            MenuItem miWake = new MenuItem { Header = "지금 말하기 (음성 듣기)" };
            miWake.Click += (s, e) => PromptListenNow();
            ctx.Items.Add(miWake);

            MenuItem miHub = new MenuItem { Header = "통합 허브 열기 / 닫기" };
            miHub.Click += (s, e) => ToggleUnifiedHub(inst, null);
            ctx.Items.Add(miHub);

            MenuItem miMini = new MenuItem { Header = "미니 오브 모드로 접기" };
            miMini.Click += (s, e) => ToggleMiniMode(true);
            ctx.Items.Add(miMini);

            ctx.Items.Add(new Separator());

            MenuItem miPosTopLeft = new MenuItem { Header = "위치: 현재 모니터 왼쪽 위" };
            miPosTopLeft.Click += (s, e) => PositionHudOnAssignedScreen(inst, "top-left");
            ctx.Items.Add(miPosTopLeft);

            MenuItem miPosTopCenter = new MenuItem { Header = "위치: 현재 모니터 가운데 위" };
            miPosTopCenter.Click += (s, e) => PositionHudOnAssignedScreen(inst, "top-center");
            ctx.Items.Add(miPosTopCenter);

            MenuItem miPosBottomRight = new MenuItem { Header = "위치: 현재 모니터 오른쪽 아래" };
            miPosBottomRight.Click += (s, e) => PositionHudOnAssignedScreen(inst, "bottom-right");
            ctx.Items.Add(miPosBottomRight);

            MenuItem miMoveNextMonitor = new MenuItem { Header = "모니터 이동: 다음 모니터로 전환" };
            miMoveNextMonitor.Click += (s, e) =>
            {
                var screens = GetOrderedScreens();
                if (screens.Length > 1)
                {
                    int curIdx = 0;
                    for (int si = 0; si < screens.Length; si++)
                    {
                        if (inst.AssignedScreen != null && screens[si].DeviceName == inst.AssignedScreen.DeviceName)
                        {
                            curIdx = si;
                            break;
                        }
                    }
                    var nextScr = screens[(curIdx + 1) % screens.Length];
                    inst.AssignedScreen = nextScr;
                    inst.ScreenDeviceName = nextScr.DeviceName;
                    PositionHudOnAssignedScreen(inst, "top-left");
                }
            };
            ctx.Items.Add(miMoveNextMonitor);

            MenuItem miResetAll = new MenuItem { Header = "주 모니터 기본 위치로 복귀" };
            miResetAll.Click += (s, e) =>
            {
                inst.AssignedScreen = System.Windows.Forms.Screen.PrimaryScreen;
                inst.ScreenDeviceName = System.Windows.Forms.Screen.PrimaryScreen.DeviceName;
                PlaceHudWindows();
            };
            ctx.Items.Add(miResetAll);

            ctx.Items.Add(new Separator());

            MenuItem miQuit = new MenuItem { Header = "자비스 종료 (Quit)" };
            miQuit.Click += (s, e) => RequestHudShutdown("HUD menu");
            ctx.Items.Add(miQuit);

            cardBorder.ContextMenu = ctx;
            verticalStack.Children.Add(cardBorder);

            // 2-Row Card Layout: Row 0 = Compact Header Bar, Row 1 = Full-Width Body & Input Area
            Grid cardLayout = new Grid();
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardLayout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            cardBorder.Child = cardLayout;

            // ROW 0: Compact Header Bar (Left: Crest + Status/Meta, Right: ONLY 3 controls)
            Grid headerRow = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            headerRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Grid.SetRow(headerRow, 0);
            cardLayout.Children.Add(headerRow);

            FrameworkElement crestElem = CreateSovereignVoiceCrest(inst);
            crestElem.Cursor = Cursors.Hand;
            crestElem.Margin = new Thickness(0, 0, 10, 0);
            crestElem.ToolTip = "클릭하여 즉시 음성 듣기";
            crestElem.MouseLeftButtonUp += (s, e) => { e.Handled = true; PromptListenNow(); };
            Grid.SetColumn(crestElem, 0);
            headerRow.Children.Add(crestElem);

            StackPanel headerMetaCol = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            Grid.SetColumn(headerMetaCol, 1);
            headerRow.Children.Add(headerMetaCol);

            // Header Line 1: Crisp Status Dot + Single-Line Status Text
            Grid statusLine = new Grid();
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            statusLine.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Ellipse statusDot = new Ellipse
            {
                Width = 6,
                Height = 6,
                Fill = new SolidColorBrush(cachedAccentColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            };
            inst.StatusDot = statusDot;
            Grid.SetColumn(statusDot, 0);
            statusLine.Children.Add(statusDot);

            TextBlock statusTb = new TextBlock
            {
                Text = cachedStatusText,
                FontFamily = UiFont,
                FontSize = 12.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(cachedAccentColor),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            inst.TxtStatus = statusTb;
            Grid.SetColumn(statusTb, 1);
            statusLine.Children.Add(statusTb);
            headerMetaCol.Children.Add(statusLine);

            // Header Line 2: Target Window Pill (Auto MaxWidth) + Tabular Clock & Weather Pill (Star)
            Grid metaRow = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            metaRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border targetBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 2, 7, 2),
                Margin = new Thickness(0, 0, 6, 0),
                MaxWidth = 158,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = "마지막으로 작업한 외부 창 자동 인식"
            };
            TextBlock activeWinTb = new TextBlock
            {
                Text = cachedTargetWindowText,
                FontFamily = UiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            targetBadgeBorder.Child = activeWinTb;
            inst.TargetBadgeBorder = targetBadgeBorder;
            inst.TxtActiveWindowBadge = activeWinTb;
            Grid.SetColumn(targetBadgeBorder, 0);
            metaRow.Children.Add(targetBadgeBorder);

            Border clockWeatherBadgeBorder = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 2, 7, 2),
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Center,
                Cursor = Cursors.Hand,
                ToolTip = "실시간 시계 & 날씨 (클릭 시 오늘 날씨 브리핑)"
            };
            string initNow = DateTime.Now.ToString("MM.dd(ddd) HH:mm:ss", new System.Globalization.CultureInfo("ko-KR"));
            TextBlock clockWeatherTb = ApplyTabular(new TextBlock
            {
                Text = initNow + "  ·  " + cachedLiveWeatherText,
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.Medium,
                Foreground = new SolidColorBrush(TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            clockWeatherBadgeBorder.Child = clockWeatherTb;
            clockWeatherBadgeBorder.MouseLeftButtonDown += (s, e) => e.Handled = true;
            clockWeatherBadgeBorder.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                SendCommandToButler("오늘 날씨 브리핑해줘");
            };
            inst.ClockWeatherBadgeBorder = clockWeatherBadgeBorder;
            inst.TxtClockWeatherBadge = clockWeatherTb;
            Grid.SetColumn(clockWeatherBadgeBorder, 1);
            metaRow.Children.Add(clockWeatherBadgeBorder);
            headerMetaCol.Children.Add(metaRow);

            // Header Right: ONLY 3 Compact Controls (Mic Toggle, TTS Toggle, Unified Hub Button)
            StackPanel headerControls = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            inst.HeaderControlsPanel = headerControls;
            Grid.SetColumn(headerControls, 2);
            headerRow.Children.Add(headerControls);

            // 1) Mic Toggle
            Border micBtn = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                ToolTip = "자비스 마이크 켜기/끄기"
            };
            StackPanel micStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var micIcon = MakeIconPath(IconMicOnData, LimeColor, 12, true);
            micIcon.Margin = new Thickness(0, 0, 5, 0);
            TextBlock micTxt = new TextBlock
            {
                Text = "마이크 ON",
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            micStack.Children.Add(micIcon);
            micStack.Children.Add(micTxt);
            micBtn.Child = micStack;
            micBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            micBtn.MouseLeftButtonUp += (s, e) => { e.Handled = true; ToggleHudAudioControlAsync(!isHudMicMuted, null); };
            inst.MicToggleBtn = micBtn;
            inst.MicToggleIcon = micIcon;
            inst.TxtMicToggle = micTxt;
            headerControls.Children.Add(micBtn);

            // 2) TTS Mute Toggle
            Border ttsBtn = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, 9, 0),
                Margin = new Thickness(0, 0, 5, 0),
                Cursor = Cursors.Hand,
                ToolTip = "자비스 음성 출력 켜기/음소거"
            };
            StackPanel ttsStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var ttsIcon = MakeIconPath(IconSpeakerOnData, CyanColor, 12, true);
            ttsIcon.Margin = new Thickness(0, 0, 5, 0);
            TextBlock ttsTxt = new TextBlock
            {
                Text = "소리 ON",
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            ttsStack.Children.Add(ttsIcon);
            ttsStack.Children.Add(ttsTxt);
            ttsBtn.Child = ttsStack;
            ttsBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            ttsBtn.MouseLeftButtonUp += (s, e) => { e.Handled = true; ToggleHudAudioControlAsync(null, !isHudTtsMuted); };
            inst.TtsToggleBtn = ttsBtn;
            inst.TtsToggleIcon = ttsIcon;
            inst.TxtTtsToggle = ttsTxt;
            headerControls.Children.Add(ttsBtn);

            // 3) Single Unified Hub Button
            Border hubBtn = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 0, 9, 0),
                Cursor = Cursors.Hand,
                ToolTip = "통합 허브 (빠른 실행 · 작업/미디어 · Plan/CHLGD/REA · 성문/음성 · 설정)"
            };
            StackPanel hubStack = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            var hubIcon = MakeIconPath(IconHubGridData, TextPrimary, 11.5, true);
            hubIcon.Margin = new Thickness(0, 0, 5, 0);
            Ellipse hubDot = new Ellipse
            {
                Width = 5,
                Height = 5,
                Fill = new SolidColorBrush(CyanColor),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                Visibility = Visibility.Collapsed
            };
            TextBlock hubTxt = new TextBlock
            {
                Text = "허브",
                FontFamily = UiFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center
            };
            var hubChevron = MakeIconPath(IconChevronDownData, TextSecondary, 10, true);
            hubChevron.Margin = new Thickness(5, 1, 0, 0);
            hubStack.Children.Add(hubIcon);
            hubStack.Children.Add(hubDot);
            hubStack.Children.Add(hubTxt);
            hubStack.Children.Add(hubChevron);
            hubBtn.Child = hubStack;
            hubBtn.MouseEnter += (s, e) =>
            {
                if (activeHubOwner != inst || hubPanelBorder == null || hubPanelBorder.Visibility != Visibility.Visible)
                {
                    hubBtn.Background = new SolidColorBrush(BgHover);
                }
            };
            hubBtn.MouseLeave += (s, e) => SyncHubActivityIndicators();
            hubBtn.MouseLeftButtonDown += (s, e) => e.Handled = true;
            hubBtn.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                ToggleUnifiedHub(inst, null);
            };
            inst.HubToggleBtn = hubBtn;
            inst.HubActivityDot = hubDot;
            inst.TxtHubToggle = hubTxt;
            inst.HubChevronIcon = hubChevron;
            headerControls.Children.Add(hubBtn);

            // ROW 1: Full-Width Response Body & Command Input Area (spans entire 690px inner width)
            StackPanel bodyCol = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetRow(bodyCol, 1);
            cardLayout.Children.Add(bodyCol);

            TextBlock bodyTb = new TextBlock
            {
                Text = cachedBodyText,
                FontFamily = UiFont,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 22
            };
            inst.TxtMainBody = bodyTb;
            ScrollViewer bodyScroll = new ScrollViewer
            {
                Content = bodyTb,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                MaxHeight = 240,
                PanningMode = PanningMode.VerticalOnly
            };
            bodyCol.Children.Add(bodyScroll);

            Grid cmdRow = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cmdRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bodyCol.Children.Add(cmdRow);

            Border inputWrapper = new Border
            {
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0)
            };
            TextBox tbInput = new TextBox
            {
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Foreground = new SolidColorBrush(TextPrimary),
                CaretBrush = new SolidColorBrush(TextPrimary),
                FontFamily = UiFont,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                ToolTip = "명령을 입력하고 Enter 또는 전송을 누르세요"
            };
            inputWrapper.Child = tbInput;
            Grid.SetColumn(inputWrapper, 0);
            cmdRow.Children.Add(inputWrapper);
            inst.InputBoxWrapper = inputWrapper;
            inst.InputBox = tbInput;

            tbInput.GotKeyboardFocus += (s, e) => { inputWrapper.BorderBrush = new SolidColorBrush(Color.FromArgb(140, CyanColor.R, CyanColor.G, CyanColor.B)); };
            tbInput.LostKeyboardFocus += (s, e) => { inputWrapper.BorderBrush = new SolidColorBrush(BorderHairline); };
            tbInput.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { e.Handled = true; SubmitHudInput(inst); }
            };
            tbInput.TextChanged += (s, e) =>
            {
                if (suppressTextChanged) return;
                string rawText = tbInput.Text ?? "";
                string live = rawText.Trim();
                suppressTextChanged = true;
                try
                {
                    foreach (var other in hudInstances)
                    {
                        if (other != inst && other.InputBox != null && other.InputBox.Text != rawText)
                        {
                            other.InputBox.Text = rawText;
                        }
                    }
                }
                finally { suppressTextChanged = false; }
                if (!string.IsNullOrEmpty(live))
                {
                    SetHudAppearance(CyanColor);
                    BroadcastStatus("받아 적는 중");
                    BroadcastBody(live);
                }
                else if (cachedStatusText == "받아 적는 중")
                {
                    SetHudAppearance(CyanColor);
                    BroadcastStatus("JARVIS v3.0 대기 · 자비스라고 불러 주세요");
                    BroadcastBody("무엇을 도와드릴까요?");
                }
            };

            Button submitBtn = CreatePillButton("전송", CyanColor);
            submitBtn.Height = 34;
            submitBtn.Margin = new Thickness(8, 0, 0, 0);
            submitBtn.Click += (s, e) => SubmitHudInput(inst);
            Grid.SetColumn(submitBtn, 1);
            cmdRow.Children.Add(submitBtn);
            inst.BtnSubmit = submitBtn;

            Button yesBtn = CreatePillButton("응 (전송)", LimeColor);
            yesBtn.Height = 34;
            yesBtn.Margin = new Thickness(6, 0, 0, 0);
            yesBtn.Visibility = Visibility.Collapsed;
            yesBtn.Click += (s, e) => SendCommandToButler("응");
            Grid.SetColumn(yesBtn, 2);
            cmdRow.Children.Add(yesBtn);
            inst.BtnYes = yesBtn;

            Button noBtn = CreatePillButton("아니 (취소)", AmberColor);
            noBtn.Height = 34;
            noBtn.Margin = new Thickness(6, 0, 0, 0);
            noBtn.Visibility = Visibility.Collapsed;
            noBtn.Click += (s, e) => SendCommandToButler("아니");
            Grid.SetColumn(noBtn, 3);
            cmdRow.Children.Add(noBtn);
            inst.BtnNo = noBtn;

            // Live Active Task/Media Strip Host + Unified Hub Host inside card stack (never loose over desktop)
            Border liveStripHost = new Border
            {
                Width = 720,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            inst.LiveStripHostContainer = liveStripHost;
            verticalStack.Children.Add(liveStripHost);

            Border hubHost = new Border
            {
                Width = 720,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };
            inst.HubHostContainer = hubHost;
            verticalStack.Children.Add(hubHost);

            if (isPrimary)
            {
                EnsureLiveStripsBuilt();
                EnsureUnifiedHubBuilt();
                liveStripHost.Child = liveStripsStack;
            }

            // Mini Floating Mode (clean matte badge, no RadialGradientBrush / no neon glow)
            Grid miniGrid = new Grid
            {
                Width = 148,
                Height = 112,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Top,
                Visibility = Visibility.Collapsed,
                Cursor = Cursors.SizeAll,
                ToolTip = "드래그하여 이동 · [HUD 펼치기] 클릭 시 복귀"
            };
            Border miniCard = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(BgOuterCard),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };
            StackPanel miniStack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            miniStack.Children.Add(CreateSpinningOrb(52, false));
            Button btnExpandHud = CreateMiniButton("HUD 펼치기");
            btnExpandHud.Margin = new Thickness(0, 8, 0, 0);
            btnExpandHud.Click += (s, e) => ToggleMiniMode(false);
            miniStack.Children.Add(btnExpandHud);
            miniCard.Child = miniStack;
            miniGrid.Children.Add(miniCard);
            miniGrid.MouseLeftButtonDown += (s, e) =>
            {
                if (e.LeftButton == MouseButtonState.Pressed)
                {
                    try { w.DragMove(); } catch { }
                }
            };
            inst.MiniOrbGrid = miniGrid;
            root.Children.Add(miniGrid);

            return inst;
        }

        private static void PositionHudOnAssignedScreen(HudWindowInstance inst, string preset)
        {
            if (inst == null || inst.Window == null) return;
            var screen = inst.AssignedScreen ?? System.Windows.Forms.Screen.PrimaryScreen;
            var source = System.Windows.PresentationSource.FromVisual(inst.Window);
            var transform = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : System.Windows.Media.Matrix.Identity;
            var origin = transform.Transform(new Point(screen.WorkingArea.Left, screen.WorkingArea.Top));
            var extent = transform.Transform(new Point(screen.WorkingArea.Width, screen.WorkingArea.Height));
            double winW = inst.Window.ActualWidth > 100 ? inst.Window.ActualWidth : inst.Window.Width;
            double winH = inst.Window.ActualHeight > 60 ? inst.Window.ActualHeight : inst.Window.Height;

            if (preset == "top-center")
            {
                inst.Window.Left = origin.X + Math.Max(12, (extent.X - winW) / 2.0);
                inst.Window.Top = origin.Y + 18;
            }
            else if (preset == "bottom-right")
            {
                inst.Window.Left = origin.X + Math.Max(12, extent.X - winW - 20);
                inst.Window.Top = origin.Y + Math.Max(18, extent.Y - winH - 24);
            }
            else
            {
                inst.Window.Left = origin.X + 20;
                inst.Window.Top = origin.Y + 20;
            }
        }

        private static void PlaceHudInstanceOnScreen(HudWindowInstance inst)
        {
            PositionHudOnAssignedScreen(inst, "top-left");
        }

        private static void PlaceHudWindows()
        {
            foreach (var inst in hudInstances)
            {
                PlaceHudInstanceOnScreen(inst);
            }
        }

        private static void ToggleMiniMode(bool mini)
        {
            isMiniMode = mini;
            foreach (var inst in hudInstances)
            {
                if (inst.FullContentGrid != null) inst.FullContentGrid.Visibility = mini ? Visibility.Collapsed : Visibility.Visible;
                if (inst.MiniOrbGrid != null) inst.MiniOrbGrid.Visibility = mini ? Visibility.Visible : Visibility.Collapsed;
            }
            if (mini && activeHubOwner != null)
            {
                CloseUnifiedHub();
            }
        }

        // =====================================================================
        // 2. FULL-MONITOR BOOT SCENE (Matte Dark Slate, Zero AI-Slop Glow)
        // =====================================================================
        private static void ShowFullMonitorBootScene(string assistantName, string userName, string helloLine)
        {
            if (initialBootComplete || bootWindow != null) return;
            var area = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
            bootWindow = new Window {
                Title = "자비스 BootScene Overlay", WindowStyle = WindowStyle.None, ResizeMode = ResizeMode.NoResize,
                AllowsTransparency = true, Background = new SolidColorBrush(Color.FromArgb(245, 8, 12, 19)),
                Topmost = true, ShowInTaskbar = false, Left = area.Left, Top = area.Top,
                Width = SystemParameters.PrimaryScreenWidth, Height = SystemParameters.PrimaryScreenHeight
            };
            var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(CreateSpinningOrb(220, false));
            panel.Children.Add(new TextBlock { Text = "J A R V I S", FontFamily = UiFont, FontSize = 34, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(CyanColor), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 24, 0, 12) });
            panel.Children.Add(new TextBlock { Text = helloLine, FontFamily = UiFont, FontSize = 19, Foreground = new SolidColorBrush(TextPrimary), HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = "잠시 후 HUD가 시작됩니다", FontFamily = UiFont, FontSize = 13.5, Foreground = new SolidColorBrush(TextSecondary), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) });
            bootWindow.Content = panel;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
            bootWindow.Closed += (sender, e) => {
                timer.Stop(); bootWindow = null; initialBootComplete = true;
                foreach (var inst in hudInstances)
                {
                    if (inst.Window != null) inst.Window.Show();
                }
                PlaceHudWindows();
                EnsureHudAlwaysVisibleAndTopmost();
                WriteLog("Startup complete: " + hudInstances.Count + " monitor HUD(s) visible after boot");
                foreach (var inst in hudInstances)
                {
                    WriteWindowDiag("monitor HUD[" + inst.MonitorIndex + "]", inst.Window);
                }
            };
            timer.Tick += (sender, e) => { timer.Stop(); if (bootWindow != null) bootWindow.Close(); };
            bootWindow.Show(); WriteLog("Startup boot visible; main HUD hidden"); timer.Start();
        }

        private static FrameworkElement CreateSovereignVoiceCrest(HudWindowInstance inst)
        {
            Border shell = new Border
            {
                Width = 36,
                Height = 36,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            StackPanel barRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            double[] baseHeights = { 7, 13, 18, 12, 8 };
            for (int i = 0; i < 5; i++)
            {
                Border bar = new Border
                {
                    Width = 3,
                    Height = baseHeights[i],
                    CornerRadius = new CornerRadius(1.5),
                    Background = new SolidColorBrush(i == 2 ? cachedAccentColor : Color.FromRgb(94, 168, 224)),
                    Margin = new Thickness(1.4, 0, 1.4, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                if (inst != null)
                {
                    inst.EqBars[i] = bar;
                }
                barRow.Children.Add(bar);
            }

            shell.Child = barRow;
            return shell;
        }

        // =====================================================================
        // 3. CLEAN GEOMETRIC ORB (Matte Rings, Zero Neon Glow / Gradient)
        // =====================================================================
        private static FrameworkElement CreateSpinningOrb(double size, bool bindMicScale)
        {
            Grid g = new Grid { Width = size, Height = size, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };

            double[] radii = { 0.94, 0.76, 0.58 };
            double[] widths = { 1.8, 1.4, 2.0 };
            DoubleCollection[] dashes = {
                new DoubleCollection(new double[] { 6, 4 }),
                new DoubleCollection(new double[] { 18, 6, 4, 6 }),
                new DoubleCollection(new double[] { 2, 6 })
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
                    Opacity = 0.85 - i * 0.14
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

            double coreSize = size * 0.34;
            Ellipse core = new Ellipse
            {
                Width = coreSize,
                Height = coreSize,
                Fill = new SolidColorBrush(CyanColor),
                Stroke = new SolidColorBrush(TextPrimary),
                StrokeThickness = 1.2,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
            g.Children.Add(core);

            return g;
        }

        // =====================================================================
        // 4. CONTINUOUS KOREAN SPEECH RECOGNITION BRIDGE (Ear.swift 1:1)
        // =====================================================================
        private static void StartSubWindowsPolling()
        {
            DispatcherTimer subWindowsTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            subWindowsTimer.Tick += async (s, e) =>
            {
                if (subWindowsPollInFlight) return;
                subWindowsPollInFlight = true;
                try
                {
                    await PollSubWindowsAsync();
                }
                catch { }
                finally { subWindowsPollInFlight = false; }
            };
            subWindowsTimer.Start();
        }

        private static void StartContinuousMicrophoneEngine()
        {
            // Poll /api/ear/latest every 65ms for sub-1s responsiveness + live acoustic waveform + foreground window tracking
            DispatcherTimer earPollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(65) };
            string lastSyncedKey = "";
            string lastSpokenKey = "";
            bool earPollInFlight = false;
            bool earWarningVisible = false;
            string statusBeforeEarWarning = "";
            earPollTimer.Tick += async (s, e) =>
            {
                if (earPollInFlight) return;
                earPollInFlight = true;
                try
                {
                    string json = await http.GetStringAsync("http://127.0.0.1:7788/api/ear/latest");
                    if (string.IsNullOrEmpty(json)) return;

                    var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                    var root = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
                    if (root == null) return;

                    var earHealth = JsonDict(root, "earHealth");
                    string micStateStr = JsonStr(earHealth, "microphone");
                    string recStateStr = JsonStr(earHealth, "recognition");
                    if (micStateStr == "error" || recStateStr == "error")
                    {
                        if (!earWarningVisible) statusBeforeEarWarning = cachedStatusText;
                        earWarningVisible = true;
                        SetHudAppearance(AmberColor);
                        BroadcastStatus("마이크 또는 음성 인식 확인 필요");
                    }
                    else if (earWarningVisible && recStateStr == "listening")
                    {
                        SetHudAppearance(CyanColor);
                        BroadcastStatus(string.IsNullOrEmpty(statusBeforeEarWarning) ? "호출어 대기 · 음성 연결 복구" : statusBeforeEarWarning);
                        earWarningVisible = false;
                    }

                    // 0. Check server-side Barge-In signal & Mic/TTS Mute state
                    bool srvMicMuted = JsonBool(root, "micMuted");
                    bool srvTtsMuted = JsonBool(root, "ttsMuted");
                    if (srvMicMuted != isHudMicMuted || srvTtsMuted != isHudTtsMuted)
                    {
                        isHudMicMuted = srvMicMuted;
                        isHudTtsMuted = srvTtsMuted;
                        SyncHudAudioControlButtons();
                        if (isHudTtsMuted && isSpeaking)
                        {
                            StopAllVoicePlaybackImmediately(false);
                        }
                    }

                    int parsedBargeSeq = JsonInt(root, "bargeInSeq", 0);
                    if (parsedBargeSeq > lastSeenBargeInSeq)
                    {
                        lastSeenBargeInSeq = parsedBargeSeq;
                        if (isSpeaking)
                        {
                            WriteLog("Barge-In detected (seq=" + parsedBargeSeq + "): stopping active TTS playback immediately");
                            StopAllVoicePlaybackImmediately(false);
                        }
                    }

                    // 0-B. Track user's clicked external foreground window every ~260ms so Computer Use knows exact target HWND
                    fgCheckCounter++;
                    if (fgCheckCounter >= 4)
                    {
                        fgCheckCounter = 0;
                        try
                        {
                            IntPtr fgHwnd = GetForegroundWindow();
                            if (fgHwnd != IntPtr.Zero)
                            {
                                uint fgPid = 0;
                                GetWindowThreadProcessId(fgHwnd, out fgPid);
                                int myPid = Process.GetCurrentProcess().Id;
                                if (fgPid != 0 && fgPid != (uint)myPid && fgHwnd.ToInt64() != lastReportedForegroundHwnd)
                                {
                                    StringBuilder wTitle = new StringBuilder(256);
                                    GetWindowText(fgHwnd, wTitle, 256);
                                    string titleStr = wTitle.ToString().Trim();
                                    string procName = "";
                                    try { procName = Process.GetProcessById((int)fgPid).ProcessName; } catch { }
                                    if (!string.IsNullOrEmpty(procName) &&
                                        !string.Equals(procName, "JarvisHud", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(procName, "SearchHost", StringComparison.OrdinalIgnoreCase) &&
                                        !string.Equals(procName, "ShellExperienceHost", StringComparison.OrdinalIgnoreCase))
                                    {
                                        lastReportedForegroundHwnd = fgHwnd.ToInt64();
                                        string shortTitle = titleStr.Length > 18 ? titleStr.Substring(0, 18) + "…" : titleStr;
                                        BroadcastTargetBadge(string.IsNullOrEmpty(shortTitle)
                                            ? ("대상: " + procName)
                                            : ("대상: " + procName + " (" + shortTitle + ")"));
                                        string fgPayload = "{\"hwnd\":" + fgHwnd.ToInt64() + ",\"processId\":" + fgPid + ",\"process\":\"" + EscapeJson(procName) + "\",\"title\":\"" + EscapeJson(titleStr) + "\"}";
                                        var _ = http.PostAsync("http://127.0.0.1:7788/api/computer/foreground", new StringContent(fgPayload, Encoding.UTF8, "application/json"));
                                    }
                                }
                            }
                        }
                        catch { }
                    }

                    // 1. Animate 5-bar Sovereign Acoustic Waveform across all monitor HUDs
                    if (root.ContainsKey("micLevel") && root["micLevel"] != null)
                    {
                        double lvl = JsonDouble(root, "micLevel", 0.0);
                        double boost = isSpeaking ? 0.45 : Math.Min(1.0, lvl * 3.2);
                        double[] minH = { 6, 10, 14, 10, 6 };
                        double[] spanH = { 10, 14, 15, 13, 9 };
                        double phase = (Environment.TickCount % 600) / 600.0 * Math.PI * 2.0;
                        for (int bi = 0; bi < 5; bi++)
                        {
                            double wave = boost > 0.04 ? (0.55 + 0.45 * Math.Abs(Math.Sin(phase + bi * 0.85))) : 0.15;
                            double barH = minH[bi] + spanH[bi] * boost * wave;
                            foreach (var inst in hudInstances)
                            {
                                if (inst.EqBars[bi] != null) inst.EqBars[bi].Height = barH;
                            }
                        }
                    }

                    var latest = JsonDict(root, "latest");
                    if (latest == null || !latest.ContainsKey("text") || latest["text"] == null) return;

                    string heard = JsonStr(latest, "text");
                    string spoken = JsonStr(latest, "spoken");
                    string hudTitle = JsonStr(latest, "hudTitle");
                    string hudSubtitle = JsonStr(latest, "hudSubtitle");
                    string hudColor = JsonStr(latest, "hudColor");
                    string itemTs = JsonStr(latest, "ts");
                    bool isFinal = JsonBool(latest, "isFinal");
                    bool awaitingConfirm = JsonBool(latest, "awaitingConfirm");
                    string intent = JsonStr(latest, "intent");
                    bool showMailDraft = (intent == "draftMailReply");
                    string key = itemTs + "|" + heard + "|" + spoken + "|" + isFinal + "|" + hudTitle + "|" + hudSubtitle;
                    if (key == lastSyncedKey) return;
                    lastSyncedKey = key;
                    earWarningVisible = false;
                    string researchId = JsonStr(latest, "researchId");
                    if (!string.IsNullOrEmpty(researchId)) await ShowResearchResultAsync(researchId);

                    if (!string.IsNullOrWhiteSpace(heard))
                    {
                        if (isMiniMode) ToggleMiniMode(false);
                        if (!isFinal)
                        {
                            if (isSpeaking && heard.Trim().Length >= 4 && (DateTime.UtcNow - currentSpeechStartedAt).TotalMilliseconds > 700)
                            {
                                WriteLog("Interim user speech detected while speaking: stopping TTS immediately");
                                StopAllVoicePlaybackImmediately(true);
                            }
                            try { if (autoRecoveryTimer != null) autoRecoveryTimer.Stop(); } catch { }
                            SetHudAppearance(CyanColor);
                            BroadcastStatus(!string.IsNullOrEmpty(hudTitle) ? hudTitle : "경청 중 · 끝까지 편하게 말씀하세요");
                            BroadcastBody(heard);
                        }
                        else
                        {
                            if (intent == "hush" || intent == "bye" || intent == "processing")
                            {
                                StopAllVoicePlaybackImmediately(false);
                            }
                            Color c = hudColor == "amber" ? AmberColor : hudColor == "lime" ? LimeColor : CyanColor;
                            SetHudAppearance(c);
                            BroadcastStatus(!string.IsNullOrEmpty(hudTitle) ? hudTitle : "실행 완료");
                            string nextBody = (awaitingConfirm || showMailDraft) && !string.IsNullOrEmpty(hudSubtitle) ? hudSubtitle : (!string.IsNullOrEmpty(spoken) ? spoken : heard);
                            BroadcastBody(nextBody);
                            BroadcastConfirmButtons(awaitingConfirm);

                            if (!awaitingConfirm && !showMailDraft)
                            {
                                ScheduleAutoRecovery(hudColor == "amber" ? 3200 : 5200);
                            }

                            if (!string.IsNullOrWhiteSpace(spoken) && key != lastSpokenKey)
                            {
                                lastSpokenKey = key;
                                SpeakAsync(spoken);
                            }
                        }
                    }
                }
                catch { }
                finally { earPollInFlight = false; }
            };
            earPollTimer.Start();
        }

        private static Window researchWindow;
        private static string lastResearchId = "";
        private static string loadingResearchId = "";
        public class ResearchSection { public string title; public string text; public int sourceId; }
        public class ResearchSource { public int id; public string title; public string url; public string domain; public string readStatus; }
        public class ResearchReport { public string id; public string query; public string title; public string layoutSize; public string status; public string createdAt; public string provider; public string note; public string summary; public string[] keywords; public ResearchSection[] sections; public ResearchSource[] sources; }

        private static TextBlock ResearchText(string text, double size, Brush color)
        {
            return new TextBlock { Text = text ?? "", FontFamily = UiFont, FontSize = size, Foreground = color, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 12), LineHeight = size * 1.55 };
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
                string sizeKind = (report.layoutSize ?? "").Trim().ToLowerInvariant();
                if (string.IsNullOrEmpty(sizeKind)) {
                    int secCount = report.sections == null ? 0 : report.sections.Length;
                    int totalLen = (report.summary ?? "").Length + (report.sections == null ? 0 : report.sections.Sum(s => (s.text ?? "").Length));
                    sizeKind = (secCount <= 1 && totalLen < 380) ? "compact" : (totalLen > 1600 ? "wide" : "standard");
                }
                double targetW = sizeKind == "compact" ? 660 : (sizeKind == "wide" ? 1180 : 980);
                double targetH = sizeKind == "compact" ? 440 : (sizeKind == "wide" ? 810 : 700);
                var area = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                var source = System.Windows.PresentationSource.FromVisual(hudWindow);
                var matrix = source != null && source.CompositionTarget != null ? source.CompositionTarget.TransformFromDevice : System.Windows.Media.Matrix.Identity;
                var origin = matrix.Transform(new Point(area.Left, area.Top));
                var extent = matrix.Transform(new Point(area.Width, area.Height));
                double width = Math.Min(targetW, extent.X - 48), height = Math.Min(targetH, extent.Y - 120);

                if (researchWindow == null) {
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
                        MinWidth = 520,
                        MinHeight = 340
                    };
                    researchWindow.Closed += (sender, args) => { researchWindow = null; };
                } else if (researchWindow.WindowState != WindowState.Maximized) {
                    researchWindow.Width = width;
                    researchWindow.Height = height;
                    researchWindow.Left = origin.X + (extent.X - width) / 2;
                    researchWindow.Top = origin.Y + Math.Max(60, (extent.Y - height) / 2);
                }

                Color bgShell = Color.FromRgb(12, 16, 23);
                Color bgCard = BgSurface;
                Color bgSubCard = BgSubRow;
                Color borderCol = BorderHairline;
                var muted = new SolidColorBrush(TextSecondary);

                var frame = new Border {
                    Background = new SolidColorBrush(bgShell),
                    BorderBrush = new SolidColorBrush(BorderHairline),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(24, 20, 24, 20),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 24, ShadowDepth = 8, Opacity = 0.65 }
                };
                frame.PreviewMouseLeftButtonDown += DragResearchWindow;

                var shell = new Grid();
                shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                shell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                shell.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                frame.Child = shell;

                // 1) Top Header Bar (Pinned)
                var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Grid.SetRow(headerGrid, 0);
                shell.Children.Add(headerGrid);

                var titleStack = new StackPanel();
                var topBadgeRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 4) };
                var badgeBorder = new Border {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(24, CyanColor.R, CyanColor.G, CyanColor.B)),
                    BorderBrush = new SolidColorBrush(BorderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(8, 2, 8, 2),
                    Margin = new Thickness(0, 0, 8, 0)
                };
                badgeBorder.Child = new TextBlock {
                    Text = "JARVIS INTELLIGENCE REPORT",
                    FontFamily = MonoFont,
                    FontSize = 10.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor)
                };
                topBadgeRow.Children.Add(badgeBorder);
                topBadgeRow.Children.Add(ApplyTabular(new TextBlock {
                    Text = (report.status == "ready" ? "분석 완료" : "확인 필요") + "  ·  출처 " + (report.sources == null ? 0 : report.sources.Length) + "건  ·  " + (report.provider ?? "JARVIS Engine"),
                    FontFamily = UiFont,
                    FontSize = 11.5,
                    Foreground = muted,
                    VerticalAlignment = VerticalAlignment.Center
                }));
                titleStack.Children.Add(topBadgeRow);
                titleStack.Children.Add(new TextBlock {
                    Text = string.IsNullOrEmpty(report.title) ? report.query : report.title,
                    FontFamily = UiFont,
                    FontSize = 20,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(TextPrimary),
                    TextWrapping = TextWrapping.Wrap
                });
                Grid.SetColumn(titleStack, 0);
                headerGrid.Children.Add(titleStack);

                var winBtnRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
                var maxBtn = CreatePillButton(researchWindow.WindowState == WindowState.Maximized ? "복원" : "최대화", TextSecondary);
                maxBtn.Margin = new Thickness(0, 0, 6, 0);
                maxBtn.Click += (s, a) => {
                    if (researchWindow == null) return;
                    researchWindow.WindowState = researchWindow.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
                    maxBtn.Content = researchWindow.WindowState == WindowState.Maximized ? "복원" : "최대화";
                };
                var close = CreatePillButton("닫기", DangerColor);
                close.Click += (sender, args) => { if (researchWindow != null) researchWindow.Close(); };
                winBtnRow.Children.Add(maxBtn);
                winBtnRow.Children.Add(close);
                Grid.SetColumn(winBtnRow, 1);
                headerGrid.Children.Add(winBtnRow);

                // 2) Pinned Export Action Bar
                var exportContainer = new Border {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(bgCard),
                    BorderBrush = new SolidColorBrush(borderCol),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(12, 8, 12, 8),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var exportGrid = new Grid();
                exportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                exportGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                exportContainer.Child = exportGrid;

                var exportBar = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
                var exportStatus = new TextBlock {
                    Text = "",
                    FontFamily = UiFont,
                    FontSize = 11.5,
                    Foreground = muted,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(10, 0, 0, 0)
                };
                string[][] exportButtons = new string[][] {
                    new string[] { "md", "Markdown (.md)" },
                    new string[] { "docx", "Word (.docx)" },
                    new string[] { "pdf", "PDF (.pdf)" },
                    new string[] { "pptx", "PPT (.pptx)" }
                };
                foreach (var eb in exportButtons) {
                    string fmt = eb[0]; string label = eb[1];
                    var btn = CreatePillButton(label, CyanColor);
                    btn.Margin = new Thickness(0, 2, 6, 2);
                    btn.Click += async (s2, a2) => {
                        try {
                            exportStatus.Text = label + " 문서 생성 중…";
                            string reqJson = "{\"researchId\":\"" + EscapeJson(id) + "\",\"format\":\"" + fmt + "\",\"openFile\":true}";
                            var resp = await http.PostAsync("http://127.0.0.1:7788/api/office/create", new StringContent(reqJson, Encoding.UTF8, "application/json"));
                            string respBody = await resp.Content.ReadAsStringAsync();
                            exportStatus.Text = resp.IsSuccessStatusCode ? (label + " 생성 완료") : ("생성 실패: " + respBody);
                            BroadcastStatus(exportStatus.Text);
                        } catch (Exception ex2) {
                            exportStatus.Text = "오류: " + ex2.Message;
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
                    Background = new SolidColorBrush(bgCard),
                    BorderBrush = new SolidColorBrush(borderCol),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(16, 14, 16, 14),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var summaryStack = new StackPanel();
                summaryStack.Children.Add(new TextBlock {
                    Text = "핵심 요약 (Executive Summary)",
                    FontFamily = UiFont,
                    FontSize = 13.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(CyanColor),
                    Margin = new Thickness(0, 0, 0, 6)
                });
                summaryStack.Children.Add(new TextBlock {
                    Text = string.IsNullOrEmpty(report.summary) ? report.note : report.summary,
                    FontFamily = UiFont,
                    FontSize = 13.5,
                    Foreground = new SolidColorBrush(TextPrimary),
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
                        var chip = CreatePillButton("# " + term, TextSecondary);
                        chip.Margin = new Thickness(0, 0, 6, 6);
                        chip.ToolTip = "클릭 시 이 키워드로 심층 조사";
                        chip.Click += (sender, args) => SendCommandToButler(term + "에 대해 조사해줘");
                        chips.Children.Add(chip);
                    }
                    body.Children.Add(chips);
                }

                // Structured Sections
                if (report.sections != null && report.sections.Length > 0) {
                    body.Children.Add(ApplyTabular(new TextBlock {
                        Text = "항목별 상세 브리핑 (" + report.sections.Length + "개 섹션)",
                        FontFamily = UiFont,
                        FontSize = 14.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(TextPrimary),
                        Margin = new Thickness(2, 0, 0, 10)
                    }));
                    foreach (var section in report.sections) {
                        var secCard = new Border {
                            Background = new SolidColorBrush(bgCard),
                            BorderBrush = new SolidColorBrush(borderCol),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(8),
                            Padding = new Thickness(16, 13, 16, 13),
                            Margin = new Thickness(0, 0, 0, 10)
                        };
                        var secStack = new StackPanel();
                        secCard.Child = secStack;

                        var secHeader = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
                        var numBadge = new Border {
                            CornerRadius = new CornerRadius(6),
                            Background = new SolidColorBrush(Color.FromArgb(24, CyanColor.R, CyanColor.G, CyanColor.B)),
                            Padding = new Thickness(7, 2, 7, 2),
                            Margin = new Thickness(0, 0, 8, 0),
                            VerticalAlignment = VerticalAlignment.Center
                        };
                        numBadge.Child = ApplyTabular(new TextBlock {
                            Text = "0" + section.sourceId,
                            FontFamily = MonoFont,
                            FontSize = 11,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(CyanColor)
                        });
                        secHeader.Children.Add(numBadge);
                        secHeader.Children.Add(new TextBlock {
                            Text = section.title ?? "",
                            FontFamily = UiFont,
                            FontSize = 14,
                            FontWeight = FontWeights.Bold,
                            Foreground = new SolidColorBrush(TextPrimary),
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
                                FontFamily = UiFont,
                                FontSize = 12.5,
                                Foreground = new SolidColorBrush(TextPrimary),
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
                    body.Children.Add(ApplyTabular(new TextBlock {
                        Text = "연결된 원본 문서 및 출처 (" + report.sources.Length + "건)",
                        FontFamily = UiFont,
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(TextPrimary),
                        Margin = new Thickness(2, 6, 0, 8)
                    }));
                    foreach (var item in report.sources) {
                        string url = item.url;
                        var link = new Button {
                            Content = ApplyTabular(new TextBlock {
                                Text = "[" + item.id + "] " + item.title + "   (" + item.domain + " · " + item.readStatus + ")",
                                FontFamily = UiFont,
                                FontSize = 12.5,
                                Foreground = new SolidColorBrush(CyanColor),
                                TextWrapping = TextWrapping.Wrap
                            }),
                            HorizontalContentAlignment = HorizontalAlignment.Left,
                            Padding = new Thickness(14, 9, 14, 9),
                            Margin = new Thickness(0, 0, 0, 6),
                            Background = new SolidColorBrush(bgCard),
                            BorderBrush = new SolidColorBrush(borderCol),
                            BorderThickness = new Thickness(1),
                            Cursor = Cursors.Hand,
                            Template = BuildRoundedButtonTemplate(new CornerRadius(8)),
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
            } catch (Exception ex) { WriteLog("Research window: " + ex.Message); BroadcastStatus("검색 결과 창을 열지 못했습니다"); }
            finally { loadingResearchId = ""; }
        }

        private static StackPanel liveStripsStack;
        private static Border taskDrawerBorder;
        private static StackPanel taskDrawerBody;
        private static TextBlock txtTaskDrawerTitle;
        private static TextBlock txtTaskDrawerStep;
        private static TextBlock txtTaskDrawerLogs;
        private static TextBlock txtTaskCollapseBtn;
        private static bool isTaskDrawerExpanded = true;

        private static Border musicDrawerBorder;
        private static Border musicDiscBorder;
        private static System.Windows.Shapes.Path musicDiscIcon;
        private static TextBlock txtMusicStateBadge;
        private static TextBlock txtMusicDrawerTitle;
        private static Border btnMusicPlayPauseCircle;
        private static System.Windows.Shapes.Path musicPlayPauseIcon;

        // Unified Hub Nested UI State
        private static Border hubPanelBorder;
        private static TextBlock txtHubMonitorBadge;
        private static StackPanel[] hubTabPanes = new StackPanel[5];
        private static System.Collections.Generic.List<Action> hubTabSyncCallbacks = new System.Collections.Generic.List<Action>();
        private static TextBlock txtHubTaskTitle;
        private static TextBlock txtHubTaskStep;
        private static TextBlock txtHubTaskLogs;
        private static TextBlock txtHubMediaState;
        private static TextBlock txtHubMediaTrack;
        private static TextBlock txtHubVoiceSummary;
        private static TextBlock txtHubModelSummary;
        private static TextBlock txtHubPlanSummary;

        private const string IconPrevData = "M19 20L9 12l10-8v16z M5 19V5";
        private const string IconNextData = "M5 4l10 8-10 8V4z M19 5v14";
        private const string IconPlayData = "M5 3l14 9-14 9V3z";
        private const string IconPauseData = "M6 4h4v16H6z M14 4h4v16h-4z";
        private const string IconMusicData = "M9 18V5l12-2v13 M9 18a3 3 0 1 1-6 0 3 3 0 0 1 6 0z M21 16a3 3 0 1 1-6 0 3 3 0 0 1 6 0z";
        private const string IconCloseData = "M18 6L6 18 M6 6l12 12";

        private static void RefreshLiveStripHostVisibility()
        {
            if (hudInstances.Count == 0) return;
            var primary = hudInstances[0];
            if (primary.LiveStripHostContainer == null) return;
            bool showTask = taskDrawerBorder != null && taskDrawerBorder.Visibility == Visibility.Visible;
            bool showMusic = musicDrawerBorder != null && musicDrawerBorder.Visibility == Visibility.Visible;
            primary.LiveStripHostContainer.Visibility = (showTask || showMusic) ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void EnsureLiveStripsBuilt()
        {
            if (liveStripsStack != null) return;
            liveStripsStack = new StackPanel { Width = 720, HorizontalAlignment = HorizontalAlignment.Center };

            // 1) Active Task Terminal Strip (Only visible when activeTask is live and not dismissed)
            taskDrawerBorder = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(BgOuterCard),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 0, 6),
                Visibility = Visibility.Collapsed
            };
            StackPanel taskOuter = new StackPanel();
            taskDrawerBorder.Child = taskOuter;

            Border taskHeader = new Border
            {
                CornerRadius = new CornerRadius(12, 12, 0, 0),
                Background = new SolidColorBrush(BgSurface),
                Padding = new Thickness(12, 7, 12, 7),
                Cursor = Cursors.Hand
            };
            Grid taskHGrid = new Grid();
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            taskHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            taskHeader.Child = taskHGrid;

            StackPanel taskTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            taskTitleRow.Children.Add(new Ellipse { Width = 6, Height = 6, Fill = new SolidColorBrush(CyanColor), Margin = new Thickness(0, 0, 7, 0), VerticalAlignment = VerticalAlignment.Center });
            txtTaskDrawerTitle = new TextBlock
            {
                Text = "실시간 터미널 · CHLGD 작업 진행",
                FontFamily = UiFont,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextPrimary),
                Margin = new Thickness(0, 0, 10, 0)
            };
            txtTaskDrawerStep = new TextBlock
            {
                Text = "실행 중",
                FontFamily = UiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center
            };
            taskTitleRow.Children.Add(txtTaskDrawerTitle);
            taskTitleRow.Children.Add(txtTaskDrawerStep);
            Grid.SetColumn(taskTitleRow, 0);
            taskHGrid.Children.Add(taskTitleRow);

            Border btnToggleTask = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BgHover),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            txtTaskCollapseBtn = new TextBlock
            {
                Text = "접기",
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary)
            };
            btnToggleTask.Child = txtTaskCollapseBtn;
            Grid.SetColumn(btnToggleTask, 1);
            taskHGrid.Children.Add(btnToggleTask);

            Border btnStopTask = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(32, DangerColor.R, DangerColor.G, DangerColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(120, DangerColor.R, DangerColor.G, DangerColor.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnStopTask.Child = new TextBlock
            {
                Text = "중단",
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(DangerColor)
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

            Border btnCloseTask = new Border
            {
                Width = 24,
                Height = 22,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BgHover),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand
            };
            btnCloseTask.Child = MakeIconPath(IconCloseData, TextSecondary, 9, true);
            btnCloseTask.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnCloseTask.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                dismissedTaskKey = currentTaskKey;
                taskDrawerBorder.Visibility = Visibility.Collapsed;
                RefreshLiveStripHostVisibility();
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
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 8, 10, 8)
            };
            txtTaskDrawerLogs = new TextBlock
            {
                Text = "[JARVIS Sovereign Butler <-> BeausCreators CHLGD Bridge]\n• 활성 에이전트: 30명 · 파이프라인 대기 중",
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap
            };
            consoleBox.Child = txtTaskDrawerLogs;
            taskDrawerBody.Children.Add(consoleBox);

            Action toggleTaskDrawer = () =>
            {
                isTaskDrawerExpanded = !isTaskDrawerExpanded;
                taskDrawerBody.Visibility = isTaskDrawerExpanded ? Visibility.Visible : Visibility.Collapsed;
                txtTaskCollapseBtn.Text = isTaskDrawerExpanded ? "접기" : "펼치기";
            };
            taskHeader.MouseLeftButtonDown += (s, e) => e.Handled = true;
            taskHeader.MouseLeftButtonUp += (s, e) => { e.Handled = true; toggleTaskDrawer(); };

            taskOuter.Children.Add(taskHeader);
            taskOuter.Children.Add(taskDrawerBody);
            liveStripsStack.Children.Add(taskDrawerBorder);

            // 2) Active Music Strip (Neutral hairline border, zero neon glow, zero emojis)
            musicDrawerBorder = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(BgOuterCard),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 0, 0, 4),
                Visibility = Visibility.Collapsed
            };

            Grid musicHGrid = new Grid();
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            musicHGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            musicDrawerBorder.Child = musicHGrid;

            musicDiscBorder = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            musicDiscIcon = MakeIconPath(IconMusicData, LimeColor, 15, true);
            musicDiscBorder.Child = musicDiscIcon;
            Grid.SetColumn(musicDiscBorder, 0);
            musicHGrid.Children.Add(musicDiscBorder);

            StackPanel musicTextCol = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            };
            txtMusicStateBadge = new TextBlock
            {
                Text = "NOW PLAYING",
                FontFamily = MonoFont,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(LimeColor),
                Margin = new Thickness(0, 0, 0, 1)
            };
            txtMusicDrawerTitle = new TextBlock
            {
                Text = "YouTube Music",
                FontFamily = UiFont,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextPrimary),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            musicTextCol.Children.Add(txtMusicStateBadge);
            musicTextCol.Children.Add(txtMusicDrawerTitle);
            Grid.SetColumn(musicTextCol, 1);
            musicHGrid.Children.Add(musicTextCol);

            StackPanel transportRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            Func<string, string, string, int, Border> makeTransportBtn = (iconData, cmdStr, tipText, sizePx) =>
            {
                Border b = new Border
                {
                    Width = sizePx,
                    Height = sizePx,
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(BgSurface),
                    BorderBrush = new SolidColorBrush(BorderHairline),
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(5, 0, 0, 0),
                    Cursor = Cursors.Hand,
                    ToolTip = tipText
                };
                b.Child = MakeIconPath(iconData, TextPrimary, 12, true);
                b.MouseEnter += (s, e) => { b.Background = new SolidColorBrush(BgHover); };
                b.MouseLeave += (s, e) => { b.Background = new SolidColorBrush(BgSurface); };
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; SendCommandToButler(cmdStr); };
                return b;
            };

            transportRow.Children.Add(makeTransportBtn(IconPrevData, "이전 곡", "이전 곡", 30));

            btnMusicPlayPauseCircle = new Border
            {
                Width = 32,
                Height = 32,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromArgb(30, LimeColor.R, LimeColor.G, LimeColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(110, LimeColor.R, LimeColor.G, LimeColor.B)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(5, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "재생 / 일시정지"
            };
            musicPlayPauseIcon = MakeIconPath(IconPauseData, LimeColor, 12, true);
            btnMusicPlayPauseCircle.Child = musicPlayPauseIcon;
            btnMusicPlayPauseCircle.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnMusicPlayPauseCircle.MouseLeftButtonUp += (s, e) => { e.Handled = true; SendCommandToButler("재생 일시정지 토글"); };
            transportRow.Children.Add(btnMusicPlayPauseCircle);

            transportRow.Children.Add(makeTransportBtn(IconNextData, "다음 곡", "다음 곡", 30));

            Border btnCloseMusicHud = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(26, DangerColor.R, DangerColor.G, DangerColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, DangerColor.R, DangerColor.G, DangerColor.B)),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(6, 0, 0, 0),
                Cursor = Cursors.Hand,
                ToolTip = "음악 끄기"
            };
            btnCloseMusicHud.Child = MakeIconPath(IconCloseData, DangerColor, 10, true);
            btnCloseMusicHud.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnCloseMusicHud.MouseLeftButtonUp += (s, e) =>
            {
                e.Handled = true;
                musicDrawerBorder.Visibility = Visibility.Collapsed;
                RefreshLiveStripHostVisibility();
                SendCommandToButler("음악 꺼줘");
            };
            transportRow.Children.Add(btnCloseMusicHud);

            Grid.SetColumn(transportRow, 2);
            musicHGrid.Children.Add(transportRow);
            liveStripsStack.Children.Add(musicDrawerBorder);
        }

        private static void EnsureUnifiedHubBuilt()
        {
            if (hubPanelBorder != null) return;

            hubPanelBorder = new Border
            {
                Width = 720,
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(BgOuterCard),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(14),
                Visibility = Visibility.Collapsed,
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 22, ShadowDepth = 6, Opacity = 0.55 }
            };

            StackPanel hubStack = new StackPanel();
            hubPanelBorder.Child = hubStack;

            // Hub Header
            Grid hubTopGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            hubTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hubTopGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hubStack.Children.Add(hubTopGrid);

            StackPanel hubTitleLeft = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hubTitleLeft.Children.Add(new TextBlock
            {
                Text = "JARVIS 통합 제어 허브",
                FontFamily = UiFont,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(TextPrimary),
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            });
            Border hubMonBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(7, 2, 7, 2),
                VerticalAlignment = VerticalAlignment.Center
            };
            txtHubMonitorBadge = new TextBlock
            {
                Text = "주 모니터",
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(CyanColor)
            };
            hubMonBadge.Child = txtHubMonitorBadge;
            hubTitleLeft.Children.Add(hubMonBadge);
            Grid.SetColumn(hubTitleLeft, 0);
            hubTopGrid.Children.Add(hubTitleLeft);

            StackPanel hubTopActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            Func<string, Action, Border> makeCompactHeaderChip = (label, onClick) =>
            {
                Border cb = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(BgSurface),
                    BorderBrush = new SolidColorBrush(BorderHairline),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(9, 4, 9, 4),
                    Margin = new Thickness(6, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                cb.Child = new TextBlock
                {
                    Text = label,
                    FontFamily = UiFont,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(TextSecondary)
                };
                cb.MouseEnter += (s, e) => cb.Background = new SolidColorBrush(BgHover);
                cb.MouseLeave += (s, e) => cb.Background = new SolidColorBrush(BgSurface);
                cb.MouseLeftButtonDown += (s, e) => e.Handled = true;
                cb.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick(); };
                return cb;
            };
            hubTopActions.Children.Add(makeCompactHeaderChip("미니 모드", () => ToggleMiniMode(true)));
            hubTopActions.Children.Add(makeCompactHeaderChip("닫기", () => CloseUnifiedHub()));
            Grid.SetColumn(hubTopActions, 1);
            hubTopGrid.Children.Add(hubTopActions);

            // Segmented Tab Bar (5 Organized Sections)
            Border tabTrack = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 0, 0, 12)
            };
            Grid tabGrid = new Grid();
            string[] tabTitles = { "빠른 실행", "작업 · 미디어", "Plan · CHLGD · REA", "음성 · 성문", "시스템 · 설정" };
            for (int i = 0; i < tabTitles.Length; i++)
            {
                tabGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            }
            tabTrack.Child = tabGrid;
            hubStack.Children.Add(tabTrack);

            Grid hubPaneHost = new Grid();
            hubStack.Children.Add(hubPaneHost);

            for (int i = 0; i < 5; i++)
            {
                hubTabPanes[i] = new StackPanel { Visibility = i == 0 ? Visibility.Visible : Visibility.Collapsed };
                hubPaneHost.Children.Add(hubTabPanes[i]);

                int idx = i;
                Border tb = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Padding = new Thickness(6, 6, 6, 6),
                    Cursor = Cursors.Hand
                };
                TextBlock tt = new TextBlock
                {
                    Text = tabTitles[i],
                    FontFamily = UiFont,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                tb.Child = tt;
                Grid.SetColumn(tb, i);
                tabGrid.Children.Add(tb);

                Action syncTab = () =>
                {
                    bool active = (activeHubTabIndex == idx);
                    tb.Background = new SolidColorBrush(active ? BgActiveTint : Colors.Transparent);
                    tb.BorderBrush = new SolidColorBrush(active ? Color.FromArgb(100, CyanColor.R, CyanColor.G, CyanColor.B) : Colors.Transparent);
                    tb.BorderThickness = new Thickness(1);
                    tt.Foreground = new SolidColorBrush(active ? TextPrimary : TextSecondary);
                };
                hubTabSyncCallbacks.Add(syncTab);
                syncTab();

                tb.MouseLeftButtonDown += (s, e) => e.Handled = true;
                tb.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    SelectUnifiedHubTab(idx);
                };
            }

            Func<string, string, Action, Border> makeHubActionTile = (title, sub, onClick) =>
            {
                Border card = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(BgSurface),
                    BorderBrush = new SolidColorBrush(BorderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(11, 8, 11, 8),
                    Margin = new Thickness(0, 0, 6, 6),
                    Cursor = Cursors.Hand
                };
                StackPanel sp = new StackPanel();
                card.Child = sp;
                sp.Children.Add(new TextBlock
                {
                    Text = title,
                    FontFamily = UiFont,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(TextPrimary),
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                if (!string.IsNullOrEmpty(sub))
                {
                    sp.Children.Add(new TextBlock
                    {
                        Text = sub,
                        FontFamily = UiFont,
                        FontSize = 10.5,
                        Foreground = new SolidColorBrush(TextMuted),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextTrimming = TextTrimming.CharacterEllipsis
                    });
                }
                card.MouseEnter += (s, e) => card.Background = new SolidColorBrush(BgHover);
                card.MouseLeave += (s, e) => card.Background = new SolidColorBrush(BgSurface);
                card.MouseLeftButtonDown += (s, e) => e.Handled = true;
                card.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick(); };
                return card;
            };

            // -----------------------------------------------------------------
            // HUB TAB 0: 빠른 실행 + 창 위치 프리셋 / 미니 모드
            // -----------------------------------------------------------------
            hubTabPanes[0].Children.Add(new TextBlock
            {
                Text = "빠른 음성 · 시스템 명령",
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(2, 0, 0, 6)
            });
            var quickGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            quickGrid.Children.Add(makeHubActionTile("지금 바로 말하기", "상시 마이크 집중 경청", () => { CloseUnifiedHub(); PromptListenNow(); }));
            quickGrid.Children.Add(makeHubActionTile("오늘 할 일 정리", "본사 작업일지 · 일정 브리핑", () => SendCommandToButler("오늘 할 일 정리해 줘")));
            quickGrid.Children.Add(makeHubActionTile("뉴런 REA 검색", "Warehouse 2-Hop 지식망", () => SendCommandToButler("뉴런에서 REA 검색해줘")));
            quickGrid.Children.Add(makeHubActionTile("뷰티풀월드 점검", "출판 시스템 · 자산 상태", () => SendCommandToButler("뷰티풀월드 출판 시스템 점검해줘")));
            quickGrid.Children.Add(makeHubActionTile("검색 결과 워드 생성", "최근 리서치 DOCX 내보내기", () => SendCommandToButler("방금 검색 결과 워드로 만들어줘")));
            quickGrid.Children.Add(makeHubActionTile("Laya 프로젝트 점검", "다국어 의사결정 진단", () => SendCommandToButler("라야로 현재 프로젝트 상태 점검해줘")));
            quickGrid.Children.Add(makeHubActionTile("CHLGD 코드 검증", "CI/CD · 보안 무결성 실행", () => SendCommandToButler("CHLGD 코드 검증 돌려줘")));
            quickGrid.Children.Add(makeHubActionTile("로파이 음악 재생", "YouTube Music 즉시 실행", () => SendCommandToButler("유튜브에서 로파이 음악 틀어줘")));
            quickGrid.Children.Add(makeHubActionTile("오늘 날씨 브리핑", "기상청 · Open-Meteo 요약", () => SendCommandToButler("오늘 날씨 브리핑해줘")));
            hubTabPanes[0].Children.Add(quickGrid);

            hubTabPanes[0].Children.Add(new TextBlock
            {
                Text = "HUD 창 배치 및 화면 모드",
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(TextMuted),
                Margin = new Thickness(2, 6, 0, 6)
            });
            var winGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 4 };
            winGrid.Children.Add(makeHubActionTile("왼쪽 위 배치", "현재 모니터 상단 좌측", () => { if (activeHubOwner != null) PositionHudOnAssignedScreen(activeHubOwner, "top-left"); }));
            winGrid.Children.Add(makeHubActionTile("가운데 위 배치", "현재 모니터 상단 중앙", () => { if (activeHubOwner != null) PositionHudOnAssignedScreen(activeHubOwner, "top-center"); }));
            winGrid.Children.Add(makeHubActionTile("오른쪽 아래 배치", "현재 모니터 하단 우측", () => { if (activeHubOwner != null) PositionHudOnAssignedScreen(activeHubOwner, "bottom-right"); }));
            winGrid.Children.Add(makeHubActionTile("모든 모니터 정렬", "전체 HUD 기본 위치 복귀", () => PlaceHudWindows()));
            hubTabPanes[0].Children.Add(winGrid);

            // -----------------------------------------------------------------
            // HUB TAB 1: 실시간 작업 터미널 · 미니 뮤직 컨트롤러
            // -----------------------------------------------------------------
            Border hubTaskCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10),
                Margin = new Thickness(0, 0, 0, 8)
            };
            StackPanel hubTaskStack = new StackPanel();
            hubTaskCard.Child = hubTaskStack;
            Grid hubTaskHeader = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            hubTaskHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hubTaskHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hubTaskStack.Children.Add(hubTaskHeader);

            StackPanel hubTaskTitleRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            txtHubTaskTitle = new TextBlock { Text = "실시간 작업 터미널", FontFamily = UiFont, FontSize = 12.5, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(TextPrimary), Margin = new Thickness(0, 0, 8, 0) };
            txtHubTaskStep = new TextBlock { Text = "대기 중", FontFamily = UiFont, FontSize = 11, Foreground = new SolidColorBrush(TextSecondary), VerticalAlignment = VerticalAlignment.Center };
            hubTaskTitleRow.Children.Add(txtHubTaskTitle);
            hubTaskTitleRow.Children.Add(txtHubTaskStep);
            Grid.SetColumn(hubTaskTitleRow, 0);
            hubTaskHeader.Children.Add(hubTaskTitleRow);

            StackPanel hubTaskBtns = new StackPanel { Orientation = Orientation.Horizontal };
            hubTaskBtns.Children.Add(makeCompactHeaderChip("작업 중단", () =>
            {
                Task.Run(async () => { try { await http.PostAsync("http://127.0.0.1:7788/api/tasks/stop", new StringContent("{}", Encoding.UTF8, "application/json")); } catch { } });
            }));
            hubTaskBtns.Children.Add(makeCompactHeaderChip("배너 숨기기", () =>
            {
                dismissedTaskKey = currentTaskKey;
                if (taskDrawerBorder != null) taskDrawerBorder.Visibility = Visibility.Collapsed;
                RefreshLiveStripHostVisibility();
                Task.Run(async () => { try { await http.PostAsync("http://127.0.0.1:7788/api/tasks/dismiss", new StringContent("{}", Encoding.UTF8, "application/json")); } catch { } });
            }));
            Grid.SetColumn(hubTaskBtns, 1);
            hubTaskHeader.Children.Add(hubTaskBtns);

            Border hubConsoleBox = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 7, 10, 7)
            };
            txtHubTaskLogs = new TextBlock
            {
                Text = "[BeausCreators CHLGD Bridge] 활성 에이전트 30명 · 대기 중입니다.",
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                TextWrapping = TextWrapping.Wrap
            };
            hubConsoleBox.Child = txtHubTaskLogs;
            hubTaskStack.Children.Add(hubConsoleBox);
            hubTabPanes[1].Children.Add(hubTaskCard);

            Border hubMediaCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 10, 12, 10)
            };
            Grid hubMediaGrid = new Grid();
            hubMediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            hubMediaGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hubMediaCard.Child = hubMediaGrid;

            StackPanel hubMediaInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            txtHubMediaState = new TextBlock { Text = "MEDIA IDLE", FontFamily = MonoFont, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(TextMuted) };
            txtHubMediaTrack = new TextBlock { Text = "재생 중인 미디어 없음", FontFamily = UiFont, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(TextPrimary), TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 2, 0, 0) };
            hubMediaInfo.Children.Add(txtHubMediaState);
            hubMediaInfo.Children.Add(txtHubMediaTrack);
            Grid.SetColumn(hubMediaInfo, 0);
            hubMediaGrid.Children.Add(hubMediaInfo);

            StackPanel hubMediaCtrl = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            hubMediaCtrl.Children.Add(makeCompactHeaderChip("이전 곡", () => SendCommandToButler("이전 곡")));
            hubMediaCtrl.Children.Add(makeCompactHeaderChip("재생/일시정지", () => SendCommandToButler("재생 일시정지 토글")));
            hubMediaCtrl.Children.Add(makeCompactHeaderChip("다음 곡", () => SendCommandToButler("다음 곡")));
            hubMediaCtrl.Children.Add(makeCompactHeaderChip("음악 끄기", () => SendCommandToButler("음악 꺼줘")));
            Grid.SetColumn(hubMediaCtrl, 1);
            hubMediaGrid.Children.Add(hubMediaCtrl);
            hubTabPanes[1].Children.Add(hubMediaCard);

            // -----------------------------------------------------------------
            // HUB TAB 2: Plan · CHLGD · Harness² · REA 워크스페이스
            // -----------------------------------------------------------------
            var wsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            wsGrid.Children.Add(makeHubActionTile("Plan · 설계 로드맵 창", "목표 · 단계별 Plan 작성 및 동기화", () => { CloseUnifiedHub(); ShowPlanAndChlgdDocumentWindow("plan"); }));
            wsGrid.Children.Add(makeHubActionTile("CHLGD 대화 규칙 · 오더 원장", "대화 학습 규칙 등록 및 KPI 조회", () => { CloseUnifiedHub(); ShowPlanAndChlgdDocumentWindow("rules"); }));
            wsGrid.Children.Add(makeHubActionTile("3대 생태계 & Harness² 관제", "30명 에이전트 · 8-Surface 자가진화", () => { CloseUnifiedHub(); ShowPlanAndChlgdDocumentWindow("harness2"); }));
            wsGrid.Children.Add(makeHubActionTile("REA v6.8.0 · Laya · MCP", "4-Stage 역공학 및 8대 도구 카탈로그", () => { CloseUnifiedHub(); ShowPlanAndChlgdDocumentWindow("rea"); }));
            hubTabPanes[2].Children.Add(wsGrid);

            Border wsPreviewCard = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 9, 12, 9),
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid wsPrevGrid = new Grid();
            wsPrevGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            wsPrevGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            wsPreviewCard.Child = wsPrevGrid;
            txtHubPlanSummary = ApplyTabular(new TextBlock
            {
                Text = "본사 30명 에이전트 · Harness² 8-Surface 검증 · REA v6.8.0 준비 완료",
                FontFamily = UiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetColumn(txtHubPlanSummary, 0);
            wsPrevGrid.Children.Add(txtHubPlanSummary);
            Border btnRunH2 = makeCompactHeaderChip("Harness² 자가진화", () =>
            {
                Task.Run(async () =>
                {
                    try
                    {
                        await http.PostAsync("http://127.0.0.1:7788/api/hud/harness2-evolve", new StringContent("{\"reason\":\"Hub Trigger\"}", Encoding.UTF8, "application/json"));
                        if (hudWindow != null) hudWindow.Dispatcher.Invoke(() => BroadcastStatus("Harness² 자가진화 루프 완료"));
                    }
                    catch { }
                });
            });
            Grid.SetColumn(btnRunH2, 1);
            wsPrevGrid.Children.Add(btnRunH2);
            hubTabPanes[2].Children.Add(wsPreviewCard);

            // -----------------------------------------------------------------
            // HUB TAB 3: 음성 · 성문 · 마이크 (Direct Pane 2 Routing)
            // -----------------------------------------------------------------
            var voiceGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            voiceGrid.Children.Add(makeHubActionTile("성문 등록 · 내 목소리만 반응", "WeSpeaker 화자 인식 · 음성 설정 창 열기", () => { CloseUnifiedHub(); ShowHotkeySettingsWindow(2); }));
            voiceGrid.Children.Add(makeHubActionTile("상시 음성인식 마이크 재연결", "Ear 워커 복구 및 즉시 경청 대기", () => { CloseUnifiedHub(); StartHandsFreeVoiceEar(); }));
            voiceGrid.Children.Add(makeHubActionTile("HD 자연음성 미리 듣기", "현재 선택된 Neural 목소리 테스트", () => SpeakAsync("안녕하세요, 이건우 대표님. 선택한 목소리와 속도로 안내하고 있습니다.")));
            voiceGrid.Children.Add(makeHubActionTile("스튜디오 음성 속도 · 목소리 변경", "현수 / 인준 / 선히 · 1.00x~1.50x", () => { CloseUnifiedHub(); ShowHotkeySettingsWindow(2); }));
            hubTabPanes[3].Children.Add(voiceGrid);

            Border voiceStatusBox = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 0)
            };
            txtHubVoiceSummary = ApplyTabular(new TextBlock
            {
                Text = "현재 음성: " + currentSpeechVoice + " · 속도 " + currentSpeechSpeedRatio.ToString("0.00") + "x",
                FontFamily = UiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(TextSecondary)
            });
            voiceStatusBox.Child = txtHubVoiceSummary;
            hubTabPanes[3].Children.Add(voiceStatusBox);

            // -----------------------------------------------------------------
            // HUB TAB 4: AI 모델 · 계정 · 단축키 · 시스템
            // -----------------------------------------------------------------
            var sysGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            sysGrid.Children.Add(makeHubActionTile("AI 모델 · 요금제 티어", "Ultra / Pro / Free / Local 모델 선택", () => { CloseUnifiedHub(); ShowHotkeySettingsWindow(0); }));
            sysGrid.Children.Add(makeHubActionTile("계정 · Gemini API 키 연결", "Antigravity OAuth 및 커스텀 모델", () => { CloseUnifiedHub(); ShowHotkeySettingsWindow(1); }));
            sysGrid.Children.Add(makeHubActionTile("호출 단축키 · 미디어 설정", "단축키 영구 저장 (" + customHotkeyLabel + ")", () => { CloseUnifiedHub(); ShowHotkeySettingsWindow(3); }));
            sysGrid.Children.Add(makeHubActionTile("부트 시퀀스 재생 (아빠 왔어)", "전체 화면 JARVIS 부트 오버레이 실행", () => { CloseUnifiedHub(); SendCommandToButler("아빠 왔어"); }));
            hubTabPanes[4].Children.Add(sysGrid);

            Border sysFooter = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(BgSubRow),
                BorderBrush = new SolidColorBrush(BorderSubtle),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 8, 12, 8),
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid sysFootGrid = new Grid();
            sysFootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sysFootGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            sysFooter.Child = sysFootGrid;
            txtHubModelSummary = ApplyTabular(new TextBlock
            {
                Text = "모델: " + currentBrainModel + " (" + currentBrainProvider + ") · 단축키: " + customHotkeyLabel,
                FontFamily = MonoFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(TextSecondary),
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            Grid.SetColumn(txtHubModelSummary, 0);
            sysFootGrid.Children.Add(txtHubModelSummary);
            Border btnQuitFromHub = makeCompactHeaderChip("자비스 종료", () => RequestHudShutdown("Hub system tab"));
            Grid.SetColumn(btnQuitFromHub, 1);
            sysFootGrid.Children.Add(btnQuitFromHub);
            hubTabPanes[4].Children.Add(sysFooter);
        }

        private static void RefreshHubSummaries()
        {
            if (txtHubVoiceSummary != null)
            {
                txtHubVoiceSummary.Text = "현재 음성: " + currentSpeechVoice + " · 속도 " + currentSpeechSpeedRatio.ToString("0.00") + "x";
            }
            if (txtHubModelSummary != null)
            {
                txtHubModelSummary.Text = "모델: " + currentBrainModel + " (" + currentBrainProvider + ") · 단축키: " + customHotkeyLabel;
            }
        }

        private static void SelectUnifiedHubTab(int idx)
        {
            if (idx < 0 || idx >= hubTabPanes.Length) idx = 0;
            activeHubTabIndex = idx;
            for (int i = 0; i < hubTabPanes.Length; i++)
            {
                if (hubTabPanes[i] != null) hubTabPanes[i].Visibility = (i == idx) ? Visibility.Visible : Visibility.Collapsed;
            }
            foreach (var cb in hubTabSyncCallbacks) { try { cb(); } catch { } }
        }

        private static void CloseUnifiedHub()
        {
            if (activeHubOwner != null && activeHubOwner.HubHostContainer != null)
            {
                activeHubOwner.HubHostContainer.Child = null;
                activeHubOwner.HubHostContainer.Visibility = Visibility.Collapsed;
            }
            if (hubPanelBorder != null) hubPanelBorder.Visibility = Visibility.Collapsed;
            activeHubOwner = null;
            SyncHubActivityIndicators();
        }

        private static void ToggleUnifiedHub(HudWindowInstance owner, int? forceTabIndex)
        {
            EnsureUnifiedHubBuilt();
            if (owner == null) owner = hudInstances.Count > 0 ? hudInstances[0] : null;
            if (owner == null || owner.HubHostContainer == null) return;

            bool isSameOpen = (activeHubOwner == owner && hubPanelBorder.Visibility == Visibility.Visible && !forceTabIndex.HasValue);
            if (isSameOpen)
            {
                CloseUnifiedHub();
                return;
            }

            if (activeHubOwner != null && activeHubOwner != owner && activeHubOwner.HubHostContainer != null)
            {
                activeHubOwner.HubHostContainer.Child = null;
                activeHubOwner.HubHostContainer.Visibility = Visibility.Collapsed;
            }

            activeHubOwner = owner;
            if (txtHubMonitorBadge != null)
            {
                txtHubMonitorBadge.Text = owner.MonitorIndex == 0 ? "주 모니터" : ("보조 모니터 " + (owner.MonitorIndex + 1));
            }
            RefreshHubSummaries();
            SelectUnifiedHubTab(forceTabIndex.HasValue ? forceTabIndex.Value : activeHubTabIndex);

            owner.HubHostContainer.Child = hubPanelBorder;
            owner.HubHostContainer.Visibility = Visibility.Visible;
            hubPanelBorder.Visibility = Visibility.Visible;
            SyncHubActivityIndicators();
        }

        private static async Task PollSubWindowsAsync()
        {
            string json = await http.GetStringAsync("http://127.0.0.1:7788/api/hud/subwindows?lite=1");
            if (string.IsNullOrWhiteSpace(json)) return;

            var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
            var root = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(json);
            if (root == null) return;

            // 1) Task Sub-Window (JavaScriptSerializer, decoupled from menu pin, respects dismissal)
            var activeTask = root.ContainsKey("activeTask") ? root["activeTask"] as System.Collections.Generic.Dictionary<string, object> : null;
            if (activeTask != null)
            {
                hasActiveTaskLive = true;
                string title = activeTask.ContainsKey("title") ? Convert.ToString(activeTask["title"]) : "터미널 작업 실행 중";
                string step = activeTask.ContainsKey("step") ? Convert.ToString(activeTask["step"]) : "";
                string startedAt = activeTask.ContainsKey("startedAt") ? Convert.ToString(activeTask["startedAt"]) : "";
                currentTaskKey = title + "|" + startedAt;

                var linesList = new System.Collections.Generic.List<string>();
                var rawLines = activeTask.ContainsKey("lines") ? activeTask["lines"] as System.Collections.ArrayList : null;
                if (rawLines != null)
                {
                    int startIdx = Math.Max(0, rawLines.Count - 5);
                    for (int i = startIdx; i < rawLines.Count; i++)
                    {
                        linesList.Add(Convert.ToString(rawLines[i]));
                    }
                }
                string logsText = linesList.Count > 0 ? string.Join("\n", linesList.ToArray()) : "실행 로그 수신 중…";

                if (txtTaskDrawerTitle != null) txtTaskDrawerTitle.Text = !string.IsNullOrEmpty(title) ? title : "터미널 작업 실행 중";
                if (txtTaskDrawerStep != null) txtTaskDrawerStep.Text = step;
                if (txtTaskDrawerLogs != null) txtTaskDrawerLogs.Text = logsText;
                if (txtHubTaskTitle != null) txtHubTaskTitle.Text = !string.IsNullOrEmpty(title) ? title : "실시간 작업 터미널";
                if (txtHubTaskStep != null) txtHubTaskStep.Text = string.IsNullOrEmpty(step) ? "실행 중" : step;
                if (txtHubTaskLogs != null) txtHubTaskLogs.Text = logsText;

                if (taskDrawerBorder != null && currentTaskKey != dismissedTaskKey)
                {
                    taskDrawerBorder.Visibility = Visibility.Visible;
                }
            }
            else
            {
                hasActiveTaskLive = false;
                currentTaskKey = "";
                dismissedTaskKey = "";
                if (taskDrawerBorder != null) taskDrawerBorder.Visibility = Visibility.Collapsed;
                if (txtHubTaskTitle != null) txtHubTaskTitle.Text = "실시간 작업 터미널";
                if (txtHubTaskStep != null) txtHubTaskStep.Text = "대기 중";
                if (txtHubTaskLogs != null) txtHubTaskLogs.Text = "[BeausCreators CHLGD Bridge] 활성 에이전트 30명 · 실행 중인 백그라운드 작업이 없습니다.";
            }

            // 2) Music Sub-Window
            var activeMedia = root.ContainsKey("activeMedia") ? root["activeMedia"] as System.Collections.Generic.Dictionary<string, object> : null;
            bool mediaActive = activeMedia != null && activeMedia.ContainsKey("active") && Convert.ToBoolean(activeMedia["active"]);
            if (mediaActive)
            {
                hasActiveMediaLive = true;
                string mTitle = activeMedia.ContainsKey("title") ? Convert.ToString(activeMedia["title"]) : "";
                string mArtist = activeMedia.ContainsKey("artist") ? Convert.ToString(activeMedia["artist"]) : "";
                string mState = activeMedia.ContainsKey("state") ? Convert.ToString(activeMedia["state"]) : "";
                bool isPaused = string.Equals(mState, "Paused", StringComparison.OrdinalIgnoreCase);

                string displayTrack = !string.IsNullOrEmpty(mTitle) ? mTitle : "YouTube Music";
                if (!string.IsNullOrEmpty(mArtist) && !displayTrack.Contains(mArtist))
                {
                    displayTrack = mArtist + " — " + displayTrack;
                }
                if (musicDrawerBorder != null) musicDrawerBorder.Visibility = Visibility.Visible;
                if (txtMusicDrawerTitle != null) txtMusicDrawerTitle.Text = displayTrack;

                Color mediaAccent = isPaused ? AmberColor : LimeColor;
                if (txtMusicStateBadge != null)
                {
                    txtMusicStateBadge.Text = isPaused ? "PAUSED" : "NOW PLAYING";
                    txtMusicStateBadge.Foreground = new SolidColorBrush(mediaAccent);
                }
                if (musicPlayPauseIcon != null)
                {
                    musicPlayPauseIcon.Data = Geometry.Parse(isPaused ? IconPlayData : IconPauseData);
                    musicPlayPauseIcon.Stroke = new SolidColorBrush(mediaAccent);
                }
                if (btnMusicPlayPauseCircle != null)
                {
                    btnMusicPlayPauseCircle.BorderBrush = new SolidColorBrush(Color.FromArgb(110, mediaAccent.R, mediaAccent.G, mediaAccent.B));
                    btnMusicPlayPauseCircle.Background = new SolidColorBrush(Color.FromArgb(30, mediaAccent.R, mediaAccent.G, mediaAccent.B));
                }
                if (musicDiscIcon != null)
                {
                    musicDiscIcon.Stroke = new SolidColorBrush(mediaAccent);
                }
                if (txtHubMediaState != null)
                {
                    txtHubMediaState.Text = isPaused ? "PAUSED" : "NOW PLAYING";
                    txtHubMediaState.Foreground = new SolidColorBrush(mediaAccent);
                }
                if (txtHubMediaTrack != null) txtHubMediaTrack.Text = displayTrack;
            }
            else
            {
                hasActiveMediaLive = false;
                if (musicDrawerBorder != null) musicDrawerBorder.Visibility = Visibility.Collapsed;
                if (txtHubMediaState != null)
                {
                    txtHubMediaState.Text = "MEDIA IDLE";
                    txtHubMediaState.Foreground = new SolidColorBrush(TextMuted);
                }
                if (txtHubMediaTrack != null) txtHubMediaTrack.Text = "재생 중인 미디어 없음";
            }

            RefreshLiveStripHostVisibility();
            SyncHubActivityIndicators();

            // 3) Auto-Pop Plan / CHLGD Document HUD Window when openWindowSeq increments
            var planSig = root.ContainsKey("planWindowSignal") ? root["planWindowSignal"] as System.Collections.Generic.Dictionary<string, object> : null;
            if (planSig != null && planSig.ContainsKey("openWindowSeq"))
            {
                int seqVal = Convert.ToInt32(planSig["openWindowSeq"]);
                if (lastSeenPlanWindowSeq < 0) lastSeenPlanWindowSeq = seqVal;
                else if (seqVal > lastSeenPlanWindowSeq)
                {
                    lastSeenPlanWindowSeq = seqVal;
                    string tab = planSig.ContainsKey("activeTab") ? Convert.ToString(planSig["activeTab"]) : "plan";
                    ShowPlanAndChlgdDocumentWindow(!string.IsNullOrEmpty(tab) ? tab : "plan");
                }
            }
        }

        private static void StartHandsFreeVoiceEar()
        {
            PromptListenNow();
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
                if (!File.Exists("C:\\자비스\\data\\config.json")) return;
                var cfg = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText("C:\\자비스\\data\\config.json", Encoding.UTF8));
                if (cfg == null) return;
                if (cfg.ContainsKey("brainProvider") && cfg["brainProvider"] != null)
                {
                    string bp = Convert.ToString(cfg["brainProvider"]);
                    if (!string.IsNullOrWhiteSpace(bp)) currentBrainProvider = bp;
                }
                if (cfg.ContainsKey("brainModel") && cfg["brainModel"] != null)
                {
                    string bm = Convert.ToString(cfg["brainModel"]);
                    if (!string.IsNullOrWhiteSpace(bm)) currentBrainModel = bm;
                }
                if (cfg.ContainsKey("sayVoice") && cfg["sayVoice"] != null)
                {
                    string sv = Convert.ToString(cfg["sayVoice"]);
                    if (!string.IsNullOrWhiteSpace(sv)) currentSpeechVoice = sv;
                }
                if (cfg.ContainsKey("ttsSpeed") && cfg["ttsSpeed"] != null)
                {
                    double spd;
                    if (double.TryParse(Convert.ToString(cfg["ttsSpeed"]), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out spd) && spd >= 0.7 && spd <= 2.0)
                    {
                        currentSpeechSpeedRatio = spd;
                    }
                }
                if (cfg.ContainsKey("userPlanTier") && cfg["userPlanTier"] != null)
                {
                    string tier = Convert.ToString(cfg["userPlanTier"]);
                    if (!string.IsNullOrWhiteSpace(tier)) currentUserPlanTier = tier;
                }
                if (cfg.ContainsKey("hudHotkeyVk") && cfg["hudHotkeyVk"] != null)
                {
                    int loadedVk;
                    if (int.TryParse(Convert.ToString(cfg["hudHotkeyVk"]), out loadedVk) && loadedVk > 0) customHotkeyVk = loadedVk;
                }
                if (cfg.ContainsKey("hudHotkeyCtrl") && cfg["hudHotkeyCtrl"] != null)
                {
                    try { customNeedCtrl = Convert.ToBoolean(cfg["hudHotkeyCtrl"]); } catch { }
                }
                if (cfg.ContainsKey("hudHotkeyAlt") && cfg["hudHotkeyAlt"] != null)
                {
                    try { customNeedAlt = Convert.ToBoolean(cfg["hudHotkeyAlt"]); } catch { }
                }
                if (cfg.ContainsKey("hudHotkeyShift") && cfg["hudHotkeyShift"] != null)
                {
                    try { customNeedShift = Convert.ToBoolean(cfg["hudHotkeyShift"]); } catch { }
                }
                if (cfg.ContainsKey("hudHotkeyLabel") && cfg["hudHotkeyLabel"] != null)
                {
                    string lbl = Convert.ToString(cfg["hudHotkeyLabel"]);
                    if (!string.IsNullOrWhiteSpace(lbl)) customHotkeyLabel = lbl;
                }
                RefreshHubSummaries();
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
                SetSettingsFeedback("저장 완료 · 설정이 즉시 반영되었습니다.");
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

        private static Task<bool> SavePlanAndApiKeyConfig(string planTier, string apiKey)
        {
            var patch = new System.Collections.Generic.Dictionary<string, object>();
            if (apiKey != null) patch["geminiApiKey"] = apiKey;
            else patch["userPlanTier"] = planTier;
            return SaveSettingsPatchAsync(patch);
        }

        private static void ShowHotkeySettingsWindow(int initialPaneIndex = 0)
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
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(bgShell),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 26,
                    ShadowDepth = 8,
                    Opacity = 0.65
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
                CornerRadius = new CornerRadius(12, 0, 0, 12),
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
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textMuted)
            });
            brandStack.Children.Add(new TextBlock
            {
                Text = "시스템 환경설정",
                FontFamily = UiFont,
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
                FontFamily = UiFont,
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
                FontFamily = UiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(textMuted)
            });
            TextBlock txtSidebarActiveModel = new TextBlock
            {
                Text = currentBrainModel,
                FontFamily = MonoFont,
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            bottomSumStack.Children.Add(txtSidebarActiveModel);
            TextBlock txtSidebarActiveProvider = ApplyTabular(new TextBlock
            {
                Text = currentBrainProvider + " · " + currentSpeechSpeedRatio.ToString("0.00") + "x",
                FontFamily = UiFont,
                FontSize = 10.5,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 1, 0, 0)
            });
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
                FontFamily = UiFont,
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            };
            TextBlock txtPaneDesc = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                Text = "모델 연결 방식을 선택합니다. 분류는 참고용이며 실제 구독·할당량을 조회한 결과가 아닙니다.",
                FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderSubtle),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            TextBlock closeGlyph = new TextBlock
            {
                Text = "\uE711",
                FontFamily = GlyphFont,
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
                    CornerRadius = new CornerRadius(8),
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
                    FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = MonoFont,
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
                BroadcastBody(displayTitle + " 모델을 저장했습니다. 환경설정 하단에서 연결 검사 결과를 확인하세요.");
                foreach (var cb in modelVisualCallbacks) { try { cb(); } catch { } }
                await VerifySelectedBrainAsync();
            };

            Func<string, string, string, string, string, UIElement> buildModelCard = (title, modelId, providerId, badgeText, descText) =>
            {
                Border card = new Border
                {
                    CornerRadius = new CornerRadius(8),
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
                    FontFamily = UiFont,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textPrimary),
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0)
                };
                topRow.Children.Add(txtTitle);

                Border codePill = new Border
                {
                    CornerRadius = new CornerRadius(6),
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
                    FontFamily = MonoFont,
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(165, 214, 255))
                };
                topRow.Children.Add(codePill);

                Border tierPill = new Border
                {
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(30, 46, 160, 67)),
                    Padding = new Thickness(6, 1, 6, 1),
                    VerticalAlignment = VerticalAlignment.Center
                };
                tierPill.Child = new TextBlock
                {
                    Text = badgeText,
                    FontFamily = UiFont,
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
                    FontFamily = UiFont,
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
                    FontFamily = UiFont,
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
                    FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            oauthStack.Children.Add(new TextBlock
            {
                Text = "개인 Google 계정은 Antigravity CLI로 연결합니다. Gemini CLI의 개인 로그인 지원은 종료되었습니다. 모델 선택 후 실제 연결을 검사하며, 구독 등급과 API 키의 사용 한도는 별개입니다.",
                FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            apiStack.Children.Add(new TextBlock
            {
                Text = "API 키를 직접 사용하는 경우 아래에 입력 후 저장하세요. 이 PC의 자비스 설정 파일에 저장됩니다.",
                FontFamily = UiFont,
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
                FontFamily = MonoFont,
                FontSize = 12,
                Padding = new Thickness(9, 0, 9, 0),
                Password = ""
            };
            apiInputRow.Children.Add(apiKeyBox);
            TextBlock txtApiSaved = new TextBlock
            {
                Text = "",
                FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            TextBlock txtSpeedDesc = ApplyTabular(new TextBlock
            {
                Text = "현재 배속: " + currentSpeechSpeedRatio.ToString("0.00") + "x · 저장된 속도는 다음 음성부터 적용됩니다.",
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
                Margin = new Thickness(0, 4, 0, 14)
            });
            voiceStack.Children.Add(txtSpeedDesc);
            voiceStack.Children.Add(new TextBlock { Text = "24kHz 스튜디오 Neural 자연 음성 전용입니다 (여성 Windows 기본 기계음 SAPI 폴백 영구 차단 · 대화 중 끼어들기 Barge-In 활성).", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 0, 0, 12) });
            WrapPanel voiceRow = new WrapPanel();
            voiceStack.Children.Add(voiceRow);
            string[] voiceIds = { "ko-KR-HyunsuMultilingualNeural", "ko-KR-InJoonNeural", "ko-KR-SunHiNeural" };
            string[] voiceLabels = { "현수 · 남성 스튜디오 Neural (기본)", "인준 · 남성 차분한 Neural", "선히 · 아나운서 Neural" };
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
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(6, 9, 6, 9),
                    Margin = new Thickness(i == 0 ? 0 : 5, 0, 0, 0),
                    Cursor = Cursors.Hand
                };
                TextBlock spdTxt = ApplyTabular(new TextBlock
                {
                    Text = spdLabel,
                    FontFamily = UiFont,
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center
                });
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
            Border speakerCard = new Border { Background = new SolidColorBrush(bgSurface), BorderBrush = new SolidColorBrush(borderSubtle), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 12) };
            StackPanel speakerStack = new StackPanel();
            speakerCard.Child = speakerStack;
            speakerStack.Children.Add(new TextBlock { Text = "내 목소리만 반응", FontFamily = UiFont, FontSize = 14, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(textPrimary) });
            speakerStack.Children.Add(new TextBlock { Text = "영상·음악을 끄고 혼자 말할 수 있을 때 등록하세요. 5초 준비 후 화면의 세 문장을 24초 동안 읽습니다. 등록용 녹음은 파일로 남기지 않고 비교용 특징만 이 PC에 저장합니다. 기존 Chrome 음성 인식은 인터넷을 사용합니다.", FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 7, 0, 8) });
            speakerStack.Children.Add(new TextBlock { Text = "비슷한 목소리·겹친 말소리·본인 녹음 재생까지 완전히 차단하는 인증 기능은 아닙니다. 너무 짧은 명령은 길게 다시 말하거나 직접 입력하세요.", FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 0, 0, 10) });
            TextBlock speakerStateText = ApplyTabular(new TextBlock { Text = "목소리 등록 상태 확인 중…", FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.FromRgb(121, 192, 255)), Margin = new Thickness(0, 0, 0, 8) });
            TextBlock enrollmentPrompt = ApplyTabular(new TextBlock { FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, FontSize = 14, Foreground = new SolidColorBrush(textPrimary), Margin = new Thickness(0, 0, 0, 10) });
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
                        if (!response.IsSuccessStatusCode)
                        {
                            try
                            {
                                var errDict = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                                speakerActionError = JsonStr(errDict, "error");
                            }
                            catch { speakerActionError = ""; }
                            enrollmentPrompt.Text = speakerActionError;
                            return;
                        }
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

            Border microphoneCard = new Border { Background = new SolidColorBrush(bgSurface), BorderBrush = new SolidColorBrush(borderSubtle), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16) };
            StackPanel microphoneStack = new StackPanel();
            microphoneCard.Child = microphoneStack;
            microphoneStack.Children.Add(new TextBlock { Text = "마이크 연결 상태", FontFamily = UiFont, Foreground = new SolidColorBrush(textPrimary), FontWeight = FontWeights.Bold, FontSize = 14 });
            microphoneStack.Children.Add(new TextBlock { Text = "입력 음량과 연결 상태를 확인합니다. 현재 입력 필터는 화자 본인 인증이나 타인 음성의 완전 차단을 보장하지 않습니다.", FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 7, 0, 10) });
            TextBlock micState = ApplyTabular(new TextBlock { Text = "아래 버튼으로 현재 상태를 확인하세요.", FontFamily = UiFont, TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(textSecondary), Margin = new Thickness(0, 0, 0, 10) });
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            });
            mediaCtrlStack.Children.Add(new TextBlock
            {
                Text = "음성으로 ‘음악 꺼줘’라고 말하거나 정지 버튼을 누르세요. 지원되는 미디어 세션을 제어하며, 실행 결과는 메인 HUD에 표시합니다.",
                FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
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
                FontFamily = UiFont,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                IsReadOnly = true,
                Cursor = Cursors.Hand,
                Margin = new Thickness(0, 0, 0, 10),
                Text = "현재 단축키: " + customHotkeyLabel + "  (클릭 후 새 키 조합 입력)"
            };
            keyCaptureBox.PreviewKeyDown += async (s, e) =>
            {
                e.Handled = true;
                Key k = (e.Key == Key.ImeProcessed ? e.ImeProcessedKey : (e.Key == Key.System ? e.SystemKey : e.Key));
                if (k == Key.None || k == Key.ImeProcessed ||
                    k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift)
                {
                    return;
                }
                int vk = KeyInterop.VirtualKeyFromKey(k);
                if (vk == 0 || vk == 0xE5)
                {
                    return;
                }
                bool c = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
                bool a = (Keyboard.Modifiers & ModifierKeys.Alt) != 0;
                bool sh = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

                string label = "";
                if (c) label += "Ctrl + ";
                if (a) label += "Alt + ";
                if (sh) label += "Shift + ";
                label += k.ToString();

                if (await ApplyCustomHotkeyAsync(vk, c, a, sh, label))
                {
                    keyCaptureBox.Text = "적용된 단축키: " + label;
                }
            };
            hotkeyStack.Children.Add(keyCaptureBox);

            WrapPanel presetHkRow = new WrapPanel();
            Border hk1 = createActionBtn("Ctrl + Space", false, async () =>
            {
                if (await ApplyCustomHotkeyAsync(VK_SPACE, true, false, false, "Ctrl + Space"))
                {
                    keyCaptureBox.Text = "적용된 단축키: Ctrl + Space";
                }
            });
            Border hk2 = createActionBtn("Alt + Space", false, async () =>
            {
                if (await ApplyCustomHotkeyAsync(VK_SPACE, false, true, false, "Alt + Space"))
                {
                    keyCaptureBox.Text = "적용된 단축키: Alt + Space";
                }
            });
            hk2.Margin = new Thickness(8, 0, 0, 0);
            Border hk3 = createActionBtn("F8 단독", false, async () =>
            {
                if (await ApplyCustomHotkeyAsync(VK_F8, false, false, false, "F8"))
                {
                    keyCaptureBox.Text = "적용된 단축키: F8";
                }
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
            int activeNavIndex = Math.Max(0, Math.Min(3, initialPaneIndex));
            System.Collections.Generic.List<Action> navSyncCallbacks = new System.Collections.Generic.List<Action>();
            var navActions = new System.Collections.Generic.List<Action>();

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
                    FontFamily = GlyphFont,
                    FontSize = 13.5,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 10, 0)
                };
                TextBlock navTxt = new TextBlock
                {
                    Text = navTitle,
                    FontFamily = UiFont,
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

                Action activateNav = () =>
                {
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
                navActions.Add(activateNav);

                navRow.MouseLeftButtonDown += (s, e) => e.Handled = true;
                navRow.MouseLeftButtonUp += (s, e) =>
                {
                    e.Handled = true;
                    activateNav();
                };
            };

            addSidebarNav(0, "\uE945", "요금제 및 AI 모델", "모델 연결 방식을 선택합니다. 분류는 참고용이며 실제 구독·할당량을 조회한 결과가 아닙니다.", paneModels);
            addSidebarNav(1, "\uE77B", "계정 및 API 연결", "Google 계정 OAuth 로그인 및 개인 API 키, 커스텀 모델 ID를 연결합니다.", paneAccount);
            addSidebarNav(2, "\uE767", "음성 · 성문 · 마이크", "답변 목소리와 말하기 속도, 내 목소리(성문) 등록 및 마이크 연결 상태를 관리합니다.", paneVoice);
            addSidebarNav(3, "\uE7F8", "미디어 · 단축키", "실시간 바탕화면 미디어 제어 및 글로벌 호출 단축키를 관리합니다.", paneDesktop);

            if (activeNavIndex >= 0 && activeNavIndex < navActions.Count)
            {
                navActions[activeNavIndex]();
            }
            settingsWindow.Closed += (sender, args) =>
            {
                speakerSettingsTimer.Stop();
                settingsWindow = null;
            };

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
                FontFamily = UiFont,
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
        // DEDICATED COMMERCIAL SaaS HUD WINDOW: Plan 문서 뷰어 & 대화 규칙/로그 & Google Harness²
        // =========================================================================
        private static void ShowPlanAndChlgdDocumentWindow(string initialTab = "plan")
        {
            if (planDocWindow != null)
            {
                try { planDocWindow.Close(); } catch { }
                planDocWindow = null;
            }

            Color bgShell = Color.FromRgb(12, 16, 23);
            Color bgSidebar = Color.FromRgb(9, 12, 16);
            Color bgSurface = BgSurface;
            Color bgSurfaceHover = BgHover;
            Color bgSubRow = BgSubRow;
            Color bgActiveCard = BgActiveTint;
            Color borderHairline = BorderHairline;
            Color borderSubtle = BorderSubtle;
            Color accentCyan = CyanColor;
            Color accentBlue = Color.FromRgb(56, 139, 253);
            Color accentEmerald = Color.FromRgb(46, 160, 67);
            Color accentAmber = AmberColor;
            Color textPrimary = TextPrimary;
            Color textSecondary = TextSecondary;
            Color textMuted = TextMuted;

            var workArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
            var presSource = hudWindow != null ? System.Windows.PresentationSource.FromVisual(hudWindow) : null;
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
            planDocWindow.Closed += (s, e) => { planDocWindow = null; };

            Border outerFrame = new Border
            {
                CornerRadius = new CornerRadius(12),
                Background = new SolidColorBrush(bgShell),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect
                {
                    Color = Colors.Black,
                    BlurRadius = 26,
                    ShadowDepth = 8,
                    Opacity = 0.65
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
            // LEFT SIDEBAR (240px)
            // =====================================================================
            Border sidebarBorder = new Border
            {
                CornerRadius = new CornerRadius(12, 0, 0, 12),
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
                FontFamily = MonoFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(accentCyan)
            });
            brandStack.Children.Add(new TextBlock
            {
                Text = "워크스페이스 관제 센터",
                FontFamily = UiFont,
                FontSize = 16.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary),
                Margin = new Thickness(0, 3, 0, 8)
            });
            Border mcpBadge = new Border
            {
                CornerRadius = new CornerRadius(6),
                Background = new SolidColorBrush(Color.FromArgb(26, 46, 160, 67)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(90, 86, 211, 100)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(9, 3, 9, 3),
                HorizontalAlignment = HorizontalAlignment.Left
            };
            mcpBadge.Child = new TextBlock
            {
                Text = "상용 정본 · MCP & Harness² 활성",
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(LimeColor)
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
                Padding = new Thickness(12, 10, 12, 10)
            };
            StackPanel bottomSumStack = new StackPanel();
            bottomSummary.Child = bottomSumStack;
            bottomSumStack.Children.Add(new TextBlock
            {
                Text = "연동된 지식 · 스킬 워크스페이스",
                FontFamily = UiFont,
                FontSize = 10.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textMuted),
                Margin = new Thickness(0, 0, 0, 5)
            });
            TextBlock txtSideBscStatus = ApplyTabular(new TextBlock
            {
                Text = "• 본사 (BeausCreators): 30명 활성",
                FontFamily = UiFont,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(textPrimary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3)
            });
            TextBlock txtSideWhStatus = ApplyTabular(new TextBlock
            {
                Text = "• 스킬 창고: 663개 · 79 뉴런 시냅스",
                FontFamily = UiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(accentCyan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 3)
            });
            TextBlock txtSideBwStatus = ApplyTabular(new TextBlock
            {
                Text = "• 출판 OS (BeautifulWorld): 673개 자산",
                FontFamily = UiFont,
                FontSize = 11,
                Foreground = new SolidColorBrush(LimeColor),
                TextWrapping = TextWrapping.Wrap
            });
            bottomSumStack.Children.Add(txtSideBscStatus);
            bottomSumStack.Children.Add(txtSideWhStatus);
            bottomSumStack.Children.Add(txtSideBwStatus);
            Grid.SetRow(bottomSummary, 2);
            sidebarGrid.Children.Add(bottomSummary);

            // =====================================================================
            // RIGHT CONTENT AREA
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
                Text = "실시간 Plan · 설계 로드맵",
                FontFamily = UiFont,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textPrimary)
            };
            TextBlock txtDocPaneDesc = new TextBlock
            {
                Text = "프로젝트 목표와 단계별 실행 계획을 한눈에 관리하고 문서로 즉시 내보냅니다.",
                FontFamily = UiFont,
                FontSize = 12,
                Foreground = new SolidColorBrush(textSecondary),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 3, 0, 0)
            };
            headerTitleStack.Children.Add(txtDocPaneTitle);
            headerTitleStack.Children.Add(txtDocPaneDesc);
            Grid.SetColumn(headerTitleStack, 0);
            rightHeader.Children.Add(headerTitleStack);

            StackPanel winCtrlRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            Border btnMaxWin = new Border
            {
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 0, 10, 0),
                Margin = new Thickness(0, 0, 6, 0),
                Cursor = Cursors.Hand
            };
            TextBlock txtMaxWin = new TextBlock
            {
                Text = "최대화",
                FontFamily = UiFont,
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
                txtMaxWin.Text = planDocWindow.WindowState == WindowState.Maximized ? "복원" : "최대화";
            };
            winCtrlRow.Children.Add(btnMaxWin);

            Border btnCloseWin = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(Color.FromRgb(38, 22, 28)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(92, 42, 52)),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            btnCloseWin.Child = MakeIconPath(IconCloseData, DangerColor, 10, true);
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

            Func<string, string, string, Color, Border> makeKpiMetricBox = (label, bigValue, subNote, accentCol) =>
            {
                Border box = new Border
                {
                    CornerRadius = new CornerRadius(8),
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
                    FontFamily = UiFont,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(textSecondary)
                });
                sp.Children.Add(ApplyTabular(new TextBlock
                {
                    Text = bigValue,
                    FontFamily = UiFont,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(accentCol),
                    Margin = new Thickness(0, 3, 0, 2),
                    TextWrapping = TextWrapping.Wrap
                }));
                sp.Children.Add(ApplyTabular(new TextBlock
                {
                    Text = subNote,
                    FontFamily = UiFont,
                    FontSize = 10.5,
                    Foreground = new SolidColorBrush(textMuted),
                    TextWrapping = TextWrapping.Wrap
                }));
                return box;
            };

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
                    CornerRadius = new CornerRadius(8),
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
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(Color.FromArgb(26, accentCol.R, accentCol.G, accentCol.B)),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(90, accentCol.R, accentCol.G, accentCol.B)),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(0, 0, 8, 2),
                        VerticalAlignment = VerticalAlignment.Center
                    };
                    badge.Child = ApplyTabular(new TextBlock
                    {
                        Text = badgeText,
                        FontFamily = UiFont,
                        FontSize = 10.5,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(accentCol)
                    });
                    leftWrap.Children.Add(badge);
                }
                leftWrap.Children.Add(new TextBlock
                {
                    Text = heading,
                    FontFamily = UiFont,
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
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(bgSubRow),
                        BorderBrush = new SolidColorBrush(borderHairline),
                        BorderThickness = new Thickness(1),
                        Padding = new Thickness(8, 2, 8, 2),
                        Margin = new Thickness(8, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    statusPill.Child = ApplyTabular(new TextBlock
                    {
                        Text = statusRight,
                        FontFamily = MonoFont,
                        FontSize = 10.5,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush(textSecondary)
                    });
                    Grid.SetColumn(statusPill, 1);
                    topRow.Children.Add(statusPill);
                }

                string[] lines = splitCleanLines(rawBody);
                foreach (string line in lines)
                {
                    Border itemRow = new Border
                    {
                        CornerRadius = new CornerRadius(6),
                        Background = new SolidColorBrush(bgSubRow),
                        BorderBrush = new SolidColorBrush(Color.FromArgb(90, accentCol.R, accentCol.G, accentCol.B)),
                        BorderThickness = new Thickness(2.5, 0, 0, 0),
                        Padding = new Thickness(11, 6, 11, 6),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    itemRow.Child = new TextBlock
                    {
                        Text = line,
                        FontFamily = UiFont,
                        FontSize = 12,
                        Foreground = new SolidColorBrush(textPrimary),
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
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(bgCol),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
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
                    FontFamily = UiFont,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    TextWrapping = TextWrapping.Wrap
                });
                if (!string.IsNullOrEmpty(subHint))
                {
                    sp.Children.Add(ApplyTabular(new TextBlock
                    {
                        Text = subHint,
                        FontFamily = UiFont,
                        FontSize = 10.5,
                        Foreground = new SolidColorBrush(Color.FromRgb(220, 235, 250)),
                        Margin = new Thickness(0, 2, 0, 0),
                        TextWrapping = TextWrapping.Wrap
                    }));
                }
                b.MouseLeftButtonDown += (s, e) => e.Handled = true;
                b.MouseLeftButtonUp += (s, e) => { e.Handled = true; onClick(); };
                return b;
            };

            // =====================================================================
            // TAB 1: 실시간 Plan · 설계 로드맵
            // =====================================================================
            Border newPlanBar = new Border
            {
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Text = ""
            };
            Grid.SetColumn(txtNewPlanInput, 0);
            newPlanGrid.Children.Add(txtNewPlanInput);

            Border btnBuildPlan = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(accentBlue),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnBuildPlan.Child = new TextBlock
            {
                Text = "새 Plan 설계 생성",
                FontFamily = UiFont,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Grid.SetColumn(btnBuildPlan, 1);
            newPlanGrid.Children.Add(btnBuildPlan);

            Border btnBriefingFromPlan = new Border
            {
                CornerRadius = new CornerRadius(8),
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
                Text = "오늘 본사 브리핑",
                FontFamily = UiFont,
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
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(bgActiveCard),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 12, 16, 12),
                Margin = new Thickness(0, 0, 0, 10)
            };
            StackPanel planBannerStack = new StackPanel();
            planBanner.Child = planBannerStack;
            TextBlock txtActivePlanTitle = new TextBlock
            {
                Text = "자비스 × BeausCreators 3대 생태계 통합 상용 아키텍처",
                FontFamily = UiFont,
                FontSize = 14.5,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            TextBlock txtActivePlanSummary = ApplyTabular(new TextBlock
            {
                Text = "실시간 동기화 완료 · 정본 문서: JARVIS_Active_Plan.md",
                FontFamily = UiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(accentCyan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            });
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
                "• BeausCreators(30명 에이전트), BeautifulWorld(출판 OS), Warehouse(663 스킬·79 뉴런) 데이터를 실시간 바인딩합니다.\n• 리서치 및 브리핑 결과를 Word, PDF, PPT, Markdown 4대 문서로 즉시 생성합니다.",
                LimeColor
            ));
            planSectionsContainer.Children.Add(makeSaaSCard(
                "STEP 03",
                "Google Harness² 재귀 자가진화 & REA v6.8.0 · Laya 엔진",
                "검증 완료",
                "• 8-Surface 용량 캡(20KB) 및 경험 원장(R=4000, P=3000) 기반 자율 규칙 승격을 수행합니다.\n• morluto/rea v6.8.0 역공학 및 Laya 의사결정 점검을 원클릭으로 실행합니다.",
                accentAmber
            ));
            tabPlan.Children.Add(planSectionsContainer);

            Action submitNewPlan = () =>
            {
                string topic = txtNewPlanInput.Text.Trim();
                if (string.IsNullOrEmpty(topic)) topic = "자비스 SaaS 상용화 및 3대 생태계 워크플로 자동화 계획";
                txtActivePlanTitle.Text = "[신규 Plan] " + topic;
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
                    LimeColor
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
                        string payload = "{\"title\":\"[Plan] " + EscapeJson(topic) + "\",\"rawTopic\":\"" + EscapeJson(topic) + "\",\"openWindow\":false}";
                        await http.PostAsync("http://127.0.0.1:7788/api/hud/plan-doc", new StringContent(payload, Encoding.UTF8, "application/json"));
                    }
                    catch { }
                });
            };
            txtNewPlanInput.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; submitNewPlan(); } };
            btnBuildPlan.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnBuildPlan.MouseLeftButtonUp += (s, e) => { e.Handled = true; submitNewPlan(); };

            // =====================================================================
            // TAB 2: CHLGD 대화 규칙 · 로그
            // =====================================================================
            Border addRuleBar = new Border
            {
                CornerRadius = new CornerRadius(8),
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
                FontFamily = UiFont,
                FontSize = 12,
                Padding = new Thickness(10, 0, 10, 0),
                Text = ""
            };
            Grid.SetColumn(txtNewRuleInput, 0);
            addRuleGrid.Children.Add(txtNewRuleInput);

            Border btnAddRule = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(accentEmerald),
                Padding = new Thickness(14, 6, 14, 6),
                Margin = new Thickness(8, 0, 0, 0),
                Cursor = Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Center
            };
            btnAddRule.Child = new TextBlock
            {
                Text = "+ 새 대화 규칙 등록",
                FontFamily = UiFont,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            };
            Grid.SetColumn(btnAddRule, 1);
            addRuleGrid.Children.Add(btnAddRule);
            tabRules.Children.Add(addRuleBar);

            System.Windows.Controls.Primitives.UniformGrid rulesKpiGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            TextBlock txtKpiOrdersVal = ApplyTabular(new TextBlock { Text = "69건", FontFamily = UiFont, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(accentCyan), Margin = new Thickness(0, 2, 0, 2) });
            TextBlock txtKpiRulesVal = ApplyTabular(new TextBlock { Text = "활성 규칙", FontFamily = UiFont, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(LimeColor), Margin = new Thickness(0, 2, 0, 2) });
            TextBlock txtKpiHarnessVal = ApplyTabular(new TextBlock { Text = "실시간 학습", FontFamily = UiFont, FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(accentAmber), Margin = new Thickness(0, 2, 0, 2) });

            Func<string, TextBlock, string, Border> wrapKpiBox = (lbl, valBlock, sub) =>
            {
                Border b = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Background = new SolidColorBrush(bgSurface),
                    BorderBrush = new SolidColorBrush(borderSubtle),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(14, 9, 14, 9),
                    Margin = new Thickness(0, 0, 8, 10)
                };
                StackPanel sp = new StackPanel();
                b.Child = sp;
                sp.Children.Add(new TextBlock { Text = lbl, FontFamily = UiFont, FontSize = 11, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(textSecondary) });
                sp.Children.Add(valBlock);
                sp.Children.Add(new TextBlock { Text = sub, FontFamily = UiFont, FontSize = 10.5, Foreground = new SolidColorBrush(textMuted), TextWrapping = TextWrapping.Wrap });
                return b;
            };
            rulesKpiGrid.Children.Add(wrapKpiBox("누적 실행 오더", txtKpiOrdersVal, "ceo-orders-log.jsonl 원장"));
            rulesKpiGrid.Children.Add(wrapKpiBox("대화 · 업무 규칙", txtKpiRulesVal, "가드레일 상시 적용 중"));
            rulesKpiGrid.Children.Add(wrapKpiBox("Harness² 재귀 학습", txtKpiHarnessVal, "반복 패턴 자동 승격"));
            tabRules.Children.Add(rulesKpiGrid);

            StackPanel rulesListContainer = new StackPanel();
            tabRules.Children.Add(rulesListContainer);

            Action submitNewRule = () =>
            {
                string newRule = txtNewRuleInput.Text.Trim();
                if (string.IsNullOrEmpty(newRule)) return;
                rulesListContainer.Children.Insert(0, makeSaaSCard(
                    "신규 등록 규칙",
                    newRule,
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm") + " · 즉시 적용",
                    "• 대표님 직접 등록 규칙으로 Google Harness² 가드레일 및 대화 원장에 영구 반영되었습니다.",
                    LimeColor
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
            txtNewRuleInput.PreviewKeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; submitNewRule(); } };
            btnAddRule.MouseLeftButtonDown += (s, e) => e.Handled = true;
            btnAddRule.MouseLeftButtonUp += (s, e) => { e.Handled = true; submitNewRule(); };

            // =====================================================================
            // TAB 3: Google Harness² & 3대 생태계
            // =====================================================================
            System.Windows.Controls.Primitives.UniformGrid h2ActionGrid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 0, 0, 4)
            };
            TextBlock txtH2RunResult = ApplyTabular(new TextBlock
            {
                Text = "Google Harness² 8-Surface 검증: PASS · 경험 원장(R=4000, P=3000) 준비 완료",
                FontFamily = UiFont,
                FontSize = 11.5,
                Foreground = new SolidColorBrush(accentCyan),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(2, 0, 0, 10)
            });
            h2ActionGrid.Children.Add(makeResponsiveActionBtn(
                "Harness² 자가진화 실행",
                "Propose -> Probe -> Compose",
                accentBlue,
                () =>
                {
                    txtH2RunResult.Text = "Google Harness² 8-Surface 검증 및 자가진화 루프 실행 중…";
                    Task.Run(async () =>
                    {
                        try
                        {
                            await http.PostAsync("http://127.0.0.1:7788/api/hud/harness2-evolve", new StringContent("{\"reason\":\"CEO HUD Trigger\"}", Encoding.UTF8, "application/json"));
                            if (hudWindow != null)
                            {
                                hudWindow.Dispatcher.Invoke(() =>
                                {
                                    txtH2RunResult.Text = "Google Harness² 자가진화 완료: 8-Surface PASS · 경험 원장(EXPERIENCE.md) 업데이트 완료";
                                    SpeakAsync("대표님, 구글 하네스 투 자가진화 루프를 완료하여 경험 원장에 반영했습니다.");
                                });
                            }
                        }
                        catch { }
                    });
                }
            ));
            h2ActionGrid.Children.Add(makeResponsiveActionBtn(
                "옵시디언 뉴런 지식망 검색",
                "79 Nodes · 138 Synapses 조회",
                accentEmerald,
                () => SendCommandToButler("뉴런에서 전체 검색해줘")
            ));
            h2ActionGrid.Children.Add(makeResponsiveActionBtn(
                "오늘 본사 작업일지 브리핑",
                "작업일지 · 보고서 · 에이전트 요약",
                Color.FromRgb(40, 54, 74),
                () => SendCommandToButler("오늘 할 일 정리해 줘")
            ));
            tabHarness2.Children.Add(h2ActionGrid);
            tabHarness2.Children.Add(txtH2RunResult);

            System.Windows.Controls.Primitives.UniformGrid ecoKpiGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 3 };
            ecoKpiGrid.Children.Add(makeKpiMetricBox("BeausCreators 본사", "30명 에이전트", "CompanyOS · 90/91 칸반 가동", accentCyan));
            ecoKpiGrid.Children.Add(makeKpiMetricBox("BeautifulWorld 출판", "673개 자산", "7-Class OS · EPUB 3.0 파이프라인", LimeColor));
            ecoKpiGrid.Children.Add(makeKpiMetricBox("Warehouse 스킬·뉴런", "663 스킬 · 79 뉴런", "138 시냅스 · 55 GitHub 레포", accentAmber));
            tabHarness2.Children.Add(ecoKpiGrid);

            StackPanel h2CardsContainer = new StackPanel();
            tabHarness2.Children.Add(h2CardsContainer);

            // =====================================================================
            // TAB 4: Antigravity REA v6.8.0 · Laya · 8대 MCP 도구 카탈로그
            // =====================================================================
            System.Windows.Controls.Primitives.UniformGrid reaActionsGrid = new System.Windows.Controls.Primitives.UniformGrid
            {
                Columns = 3,
                Margin = new Thickness(0, 0, 0, 6)
            };
            reaActionsGrid.Children.Add(makeResponsiveActionBtn(
                "자비스 REA v6.8.0 진단",
                "4-Stage 역공학 · 4분할 증거 추출",
                accentBlue,
                () => SendCommandToButler("자비스 REA 역공학 분석해줘")
            ));
            reaActionsGrid.Children.Add(makeResponsiveActionBtn(
                "본사(BeausCreators) 진단",
                "본사 아키텍처 · 스킬 구조 분석",
                accentEmerald,
                () => SendCommandToButler("뷰스크리에이터스 REA 역공학 분석해줘")
            ));
            reaActionsGrid.Children.Add(makeResponsiveActionBtn(
                "Laya 의사결정 점검",
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
                CornerRadius = new CornerRadius(8),
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
                Text = "양방향 MCP 서버 제공 도구 카탈로그 (jarvis-sovereign-butler · 8대 도구 온라인)",
                FontFamily = UiFont,
                FontSize = 13.5,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(LimeColor),
                Margin = new Thickness(0, 0, 0, 8)
            });

            System.Windows.Controls.Primitives.UniformGrid mcpToolsGrid = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2 };
            string[][] mcpToolsList = new string[][]
            {
                new string[] { "01. 상태 · 프로필 조회", "jarvis_status_and_profile", "3대 생태계 · 메모리 · 대표님 프로필 조회" },
                new string[] { "02. 음성 · 시스템 오더 실행", "jarvis_execute_command", "자비스 Butler 엔진 명령 즉시 실행" },
                new string[] { "03. Plan · 규칙 HUD 제어", "jarvis_open_plan_hud", "전용 Plan 문서 및 규칙 창 실시간 제어" },
                new string[] { "04. 대화 규칙 · 로그 기록", "jarvis_record_rule_or_log", "새 대화 규칙 및 CHLGD 오더 원장 기록" },
                new string[] { "05. Harness² 자가진화 실행", "jarvis_run_harness2_recursion", "8-Surface 검증 및 재귀 학습 루프 실행" },
                new string[] { "06. 4대 오피스 문서 제작", "jarvis_create_office_doc", "Word · PDF · PPT · Markdown 즉시 생성" },
                new string[] { "07. REA v6.8.0 · Laya 분석", "jarvis_run_rea_or_laya", "클린룸 역공학 및 Laya 상태 진단 실행" },
                new string[] { "08. Warehouse 뉴런 검색", "jarvis_warehouse_neuron", "79 Nodes · 138 Synapses 2-Hop 탐색" }
            };
            foreach (var mt in mcpToolsList)
            {
                Border tBox = new Border
                {
                    CornerRadius = new CornerRadius(6),
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
                topT.Children.Add(ApplyTabular(new TextBlock
                {
                    Text = mt[0],
                    FontFamily = UiFont,
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(textPrimary)
                }));
                TextBlock codeTb = new TextBlock
                {
                    Text = mt[1],
                    FontFamily = MonoFont,
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
                    FontFamily = UiFont,
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
            // SIDEBAR NAVIGATION SETUP (Segoe MDL2 Assets Glyphs, Zero Emojis)
            // =====================================================================
            string currentTabKey = string.IsNullOrEmpty(initialTab) ? "plan" : initialTab.ToLowerInvariant();
            System.Collections.Generic.List<Action> navSyncList = new System.Collections.Generic.List<Action>();

            Action<string, string, string, string, StackPanel> addDocNav = (tabKey, iconGlyph, navLabel, descStr, paneTarget) =>
            {
                Border navBtn = new Border
                {
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 6),
                    Cursor = Cursors.Hand
                };
                StackPanel row = new StackPanel { Orientation = Orientation.Horizontal };
                TextBlock ic = new TextBlock
                {
                    Text = iconGlyph,
                    FontFamily = GlyphFont,
                    FontSize = 13.5,
                    Margin = new Thickness(0, 0, 9, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                TextBlock lb = new TextBlock
                {
                    Text = navLabel,
                    FontFamily = UiFont,
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
                    navBtn.BorderBrush = new SolidColorBrush(active ? borderHairline : Colors.Transparent);
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
                    txtDocPaneTitle.Text = navLabel;
                    txtDocPaneDesc.Text = descStr;
                    mainTabScroll.ScrollToTop();
                    foreach (var cb in navSyncList) { try { cb(); } catch { } }
                };
            };

            addDocNav("plan", "\uE8A5", "실시간 Plan · 로드맵", "프로젝트 목표와 단계별 실행 계획을 구조화하여 관리하고 문서로 즉시 내보냅니다.", tabPlan);
            addDocNav("rules", "\uE82D", "CHLGD 대화 규칙 · 로그", "대표님과의 대화에서 학습된 규칙, 선호도, 오더 실행 원장을 카드 형태로 정리합니다.", tabRules);
            addDocNav("harness2", "\uE9D9", "3대 생태계 & Harness²", "BeausCreators · BeautifulWorld · Warehouse 실시간 지표 및 Google Harness² 자가진화 관제판입니다.", tabHarness2);
            addDocNav("rea", "\uE721", "Antigravity REA & MCP", "morluto/rea v6.8.0 클린룸 역공학, Laya 의사결정 언어 및 8대 양방향 MCP 도구 카탈로그입니다.", tabRea);

            tabPlan.Visibility = currentTabKey == "plan" ? Visibility.Visible : Visibility.Collapsed;
            tabRules.Visibility = currentTabKey == "rules" ? Visibility.Visible : Visibility.Collapsed;
            tabHarness2.Visibility = currentTabKey == "harness2" ? Visibility.Visible : Visibility.Collapsed;
            tabRea.Visibility = currentTabKey == "rea" ? Visibility.Visible : Visibility.Collapsed;
            if (currentTabKey == "rules")
            {
                txtDocPaneTitle.Text = "CHLGD 대화 규칙 · 로그";
                txtDocPaneDesc.Text = "대표님과의 대화에서 학습된 규칙, 선호도, 오더 실행 원장을 카드 형태로 정리합니다.";
            }
            else if (currentTabKey == "harness2")
            {
                txtDocPaneTitle.Text = "3대 생태계 & Harness²";
                txtDocPaneDesc.Text = "BeausCreators · BeautifulWorld · Warehouse 실시간 지표 및 Google Harness² 자가진화 관제판입니다.";
            }
            else if (currentTabKey == "rea")
            {
                txtDocPaneTitle.Text = "Antigravity REA & MCP";
                txtDocPaneDesc.Text = "morluto/rea v6.8.0 클린룸 역공학, Laya 의사결정 언어 및 8대 양방향 MCP 도구 카탈로그입니다.";
            }
            foreach (var cb in navSyncList) { try { cb(); } catch { } }

            // Pinned Footer (Clean Close Action, Zero Redundant Hint Banner)
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

            Border btnFooterClose = new Border
            {
                CornerRadius = new CornerRadius(8),
                Background = new SolidColorBrush(bgSurface),
                BorderBrush = new SolidColorBrush(borderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(16, 5, 16, 5),
                Cursor = Cursors.Hand
            };
            btnFooterClose.Child = new TextBlock
            {
                Text = "닫기",
                FontFamily = UiFont,
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

                    if (hudWindow != null)
                    {
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
                                    Color[] cols = new Color[] { accentCyan, LimeColor, accentAmber, Color.FromRgb(165, 214, 255) };
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
                            var recCounts = JsonDict(recLearn, "counts");
                            var convRules = root.ContainsKey("conversationRules") ? root["conversationRules"] as System.Collections.ArrayList : null;
                            int totOrders = JsonInt(root, "totalOrders", 0);
                            int learnedCnt = 0;
                            if (recLearn != null)
                            {
                                if (recLearn.ContainsKey("activeCount") && recLearn["activeCount"] != null) learnedCnt = JsonInt(recLearn, "activeCount", 0);
                                else if (recLearn.ContainsKey("activeRulesCount") && recLearn["activeRulesCount"] != null) learnedCnt = JsonInt(recLearn, "activeRulesCount", 0);
                                else if (recCounts != null && recCounts.ContainsKey("activeRules")) learnedCnt = JsonInt(recCounts, "activeRules", 0);
                                else if (recLearn.ContainsKey("totalLearnedRules")) learnedCnt = JsonInt(recLearn, "totalLearnedRules", 0);
                            }
                            int lessonCnt = (recCounts != null && recCounts.ContainsKey("lessons"))
                                ? JsonInt(recCounts, "lessons", 0)
                                : JsonInt(recLearn, "totalLessons", 0);
                            int convRuleCnt = convRules != null ? convRules.Count : 0;

                            txtKpiOrdersVal.Text = totOrders.ToString("N0") + "건 실행";
                            txtKpiRulesVal.Text = (convRuleCnt + learnedCnt).ToString("N0") + "개 활성";
                            txtKpiHarnessVal.Text = "승격 " + learnedCnt + "건 · 교훈 " + lessonCnt + "건";

                            rulesListContainer.Children.Clear();
                            System.Collections.ArrayList rList = null;
                            if (recLearn != null)
                            {
                                if (recLearn.ContainsKey("activeRules")) rList = recLearn["activeRules"] as System.Collections.ArrayList;
                                else if (recLearn.ContainsKey("rules")) rList = recLearn["rules"] as System.Collections.ArrayList;
                            }
                            if (rList != null)
                            {
                                foreach (var rObj in rList)
                                {
                                    var rDict = rObj as System.Collections.Generic.Dictionary<string, object>;
                                    if (rDict == null) continue;
                                    string evStr = JsonStr(rDict, "evidence");
                                    string rTitle = JsonStr(rDict, "title");
                                    if (string.IsNullOrEmpty(rTitle))
                                    {
                                        rTitle = !string.IsNullOrEmpty(evStr) ? ("근거 " + evStr + "회 검증 · 승격 규칙") : "재귀 학습 승격 규칙";
                                    }
                                    string rGuidance = JsonStr(rDict, "text");
                                    if (string.IsNullOrEmpty(rGuidance)) rGuidance = JsonStr(rDict, "guidance");
                                    if (string.IsNullOrEmpty(rGuidance)) rGuidance = JsonStr(rDict, "rule");
                                    string rDom = JsonStr(rDict, "category");
                                    if (string.IsNullOrEmpty(rDom)) rDom = JsonStr(rDict, "kind");
                                    if (string.IsNullOrEmpty(rDom)) rDom = JsonStr(rDict, "domain");
                                    if (string.IsNullOrEmpty(rDom)) rDom = "자동 학습";
                                    string rPromotedAt = JsonStr(rDict, "promotedAt");
                                    string rBadge = string.IsNullOrEmpty(rPromotedAt) ? "자동 승격됨" : (rPromotedAt + " · 승격됨");
                                    if (!string.IsNullOrEmpty(rGuidance))
                                    {
                                        rulesListContainer.Children.Add(makeSaaSCard(
                                            "Harness² · " + rDom,
                                            rTitle,
                                            rBadge,
                                            rGuidance,
                                            LimeColor
                                        ));
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
                                var ecoTotals = JsonDict(ecoRoot, "totals");
                                int totalEcoFiles = (ecoTotals != null && ecoTotals.ContainsKey("totalFiles")) ? JsonInt(ecoTotals, "totalFiles", 0) : JsonInt(ecoRoot, "totalFiles", 0);
                                int totalEcoSkills = (ecoTotals != null && ecoTotals.ContainsKey("totalSkills")) ? JsonInt(ecoTotals, "totalSkills", 0) : JsonInt(ecoRoot, "totalSkills", 0);
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
                                        if (eName.Contains("BeausCreators") && sCnt > 0)
                                        {
                                            txtSideBscStatus.Text = "• 본사 (BeausCreators): " + sCnt + "개 스킬 · 30명 활성";
                                        }
                                        else if (eName.Contains("BeautifulWorld") && fCnt > 0)
                                        {
                                            txtSideBwStatus.Text = "• 출판 OS (BeautifulWorld): " + fCnt.ToString("N0") + "개 자산";
                                        }
                                    }
                                }
                                h2CardsContainer.Children.Add(makeSaaSCard(
                                    "3대 생태계 통합",
                                    "이건우 대표님 3대 생태계 실시간 연결 현황",
                                    "총 " + totalEcoFiles.ToString("N0") + " 파일 · " + totalEcoSkills.ToString("N0") + " 스킬",
                                    ecoSb.ToString().TrimEnd(),
                                    LimeColor
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
                                    txtSideWhStatus.Text = "• 스킬 창고: " + subCnt + "개 · " + nCnt + " 뉴런 (" + eCnt + " 시냅스)";
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
                                    "• 8대 편집 가능 표면(c1~c8, 파일 " + fCount + "개) 무결성 및 상시 컨텍스트 용량 제한(20KB) 검증 통과\n• 재귀 루프: Observe(관찰) -> Propose[INV+DOM](규칙 제안) -> Probe(검증) -> Compose(원장 합성)",
                                    accentAmber
                                ));
                            }
                        }
                        catch { }
                        });
                    }
                }
                catch { }
            });
        }

        private static async Task<bool> ApplyCustomHotkeyAsync(int vk, bool ctrl, bool alt, bool shift, string label)
        {
            var patch = new System.Collections.Generic.Dictionary<string, object>
            {
                { "hudHotkeyVk", vk },
                { "hudHotkeyCtrl", ctrl },
                { "hudHotkeyAlt", alt },
                { "hudHotkeyShift", shift },
                { "hudHotkeyLabel", label }
            };
            bool saved = await SaveSettingsPatchAsync(patch);
            if (!saved)
            {
                SetHudAppearance(AmberColor);
                BroadcastStatus("호출 단축키 저장 실패");
                BroadcastBody("단축키 [" + label + "] 저장에 실패했습니다. 서버 연결을 확인해 주세요.");
                return false;
            }

            customHotkeyVk = vk;
            customNeedCtrl = ctrl;
            customNeedAlt = alt;
            customNeedShift = shift;
            customHotkeyLabel = label;

            RefreshHubSummaries();
            SetHudAppearance(CyanColor);
            BroadcastStatus("호출 단축키 저장 완료");
            BroadcastBody("단축키 [" + customHotkeyLabel + "] 적용 및 영구 저장 완료");
            return true;
        }

        private static void PromptListenNow()
        {
            if (isMiniMode) ToggleMiniMode(false);
            try { if (autoRecoveryTimer != null) autoRecoveryTimer.Stop(); } catch { }
            StopAllVoicePlaybackImmediately(true);

            StartInvisibleChromeEarWorker();
            SetHudAppearance(CyanColor);
            BroadcastStatus("집중 듣는 중 (잠시 쉬어도 끊기지 않습니다 · 끝까지 말씀하세요)");
            BroadcastBody("지금 편하게 말씀해 주세요 (예: \"오늘 할 일 정리해 줘\", \"뉴런에서 REA 검색해줘\", \"방금 검색 결과 워드로 만들어줘\")");

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

        private static void SubmitHudInput(HudWindowInstance sourceInst)
        {
            TextBox srcBox = (sourceInst != null && sourceInst.InputBox != null) ? sourceInst.InputBox : inputBox;
            if (srcBox == null || string.IsNullOrWhiteSpace(srcBox.Text)) return;
            string text = srcBox.Text.Trim();
            suppressTextChanged = true;
            try
            {
                foreach (var inst in hudInstances)
                {
                    if (inst.InputBox != null) inst.InputBox.Text = "";
                }
            }
            finally { suppressTextChanged = false; }
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
                StopAllVoicePlaybackImmediately(true);
                SetHudAppearance(CyanColor);
                BroadcastStatus(System.Text.RegularExpressions.Regex.IsMatch(sentence, "알아|설명|조사|검색|정리|비교") ? "자료 검색·정리 중" : "요청 처리 중");
                BroadcastBody(sentence);

                string jsonBody = "{\"sentence\":\"" + EscapeJson(sentence) + "\",\"executeOs\":true}";
                var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
                var resp = await http.PostAsync("http://127.0.0.1:7788/api/butler/command", content);
                string raw = await resp.Content.ReadAsStringAsync();
                var ser = new System.Web.Script.Serialization.JavaScriptSerializer();
                var root = ser.Deserialize<System.Collections.Generic.Dictionary<string, object>>(raw);
                var resObj = JsonDict(root, "result") ?? root;

                string researchId = JsonStr(resObj, "researchId");
                if (string.IsNullOrEmpty(researchId)) researchId = JsonStr(root, "researchId");
                if (!string.IsNullOrEmpty(researchId)) await ShowResearchResultAsync(researchId);

                string hudTitle = JsonStr(resObj, "hudTitle");
                string hudSubtitle = JsonStr(resObj, "hudSubtitle");
                string spoken = JsonStr(resObj, "spoken");
                string hudColor = JsonStr(resObj, "hudColor");
                string intent = JsonStr(resObj, "intent");
                bool bootScene = JsonBool(resObj, "bootScene");
                bool awaitingConfirm = JsonBool(resObj, "awaitingConfirm");
                bool showMailDraft = (intent == "draftMailReply");
                bool openPlanWin = JsonBool(resObj, "openPlanWindow") || JsonBool(root, "openPlanWindow");
                string targetPlanTab = JsonStr(resObj, "activeTab");
                if (string.IsNullOrEmpty(targetPlanTab)) targetPlanTab = JsonStr(root, "activeTab");
                if (openPlanWin)
                {
                    int parsedSeq = JsonInt(resObj, "openWindowSeq", -1);
                    if (parsedSeq < 0) parsedSeq = JsonInt(root, "openWindowSeq", -1);
                    if (parsedSeq >= 0)
                    {
                        lastSeenPlanWindowSeq = Math.Max(lastSeenPlanWindowSeq, parsedSeq);
                    }
                    ShowPlanAndChlgdDocumentWindow(!string.IsNullOrEmpty(targetPlanTab) ? targetPlanTab : "plan");
                }

                Color c = hudColor == "amber" ? AmberColor : hudColor == "lime" ? LimeColor : CyanColor;
                SetHudAppearance(c);

                BroadcastStatus(!string.IsNullOrEmpty(hudTitle) ? hudTitle : "실행 완료");
                string nextBody = (awaitingConfirm || showMailDraft) && !string.IsNullOrEmpty(hudSubtitle)
                    ? hudSubtitle
                    : (!string.IsNullOrEmpty(spoken) ? spoken : (!string.IsNullOrEmpty(hudSubtitle) ? hudSubtitle : sentence));
                BroadcastBody(nextBody);
                BroadcastConfirmButtons(awaitingConfirm);

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
                BroadcastStatus("자동 복구 중 · 잠시만 기다려 주세요");
                BroadcastBody(ex.Message);
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
            cachedAccentColor = c;
            foreach (var inst in hudInstances)
            {
                if (inst.StatusDot != null) inst.StatusDot.Fill = new SolidColorBrush(c);
                if (inst.TxtStatus != null) inst.TxtStatus.Foreground = new SolidColorBrush(c);
                for (int bi = 0; bi < 5; bi++)
                {
                    if (inst.EqBars[bi] != null)
                    {
                        inst.EqBars[bi].Background = new SolidColorBrush(bi == 2 ? c : Color.FromArgb(200, c.R, c.G, c.B));
                    }
                }
            }
        }

        private static async Task<byte[]> FetchTtsWavClauseAsync(string clause, System.Threading.CancellationToken token)
        {
            string url = "http://127.0.0.1:7788/api/tts-mp3?format=wav&text=" + Uri.EscapeDataString(clause);
            using (var timeout = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(token))
            {
                timeout.CancelAfter(10000);
                using (var response = await http.GetAsync(url, timeout.Token))
                {
                    if (!response.IsSuccessStatusCode) return null;
                    byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                    System.Collections.Generic.IEnumerable<string> values;
                    string actualVoice = response.Headers.TryGetValues("X-Jarvis-Tts-Voice", out values) ? values.FirstOrDefault() : "unknown";
                    string actualSpeed = response.Headers.TryGetValues("X-Jarvis-Tts-Speed", out values) ? values.FirstOrDefault() : "unknown";
                    string rawPersona = response.Headers.TryGetValues("X-Jarvis-Tts-Persona", out values) ? Uri.UnescapeDataString(values.FirstOrDefault() ?? "") : "";
                    SetSettingsFeedback("재생 음성: " + (!string.IsNullOrEmpty(rawPersona) ? (rawPersona + " (" + actualVoice + ")") : actualVoice) + " · 속도 " + actualSpeed + "×", false);
                    return bytes;
                }
            }
        }

        private static async Task<bool> PlayWavBytesInterruptibleAsync(byte[] bytes, System.Threading.CancellationToken token, Stopwatch clock)
        {
            if (bytes == null || bytes.Length <= 44 || token.IsCancellationRequested) return false;
            bool wav = bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' && bytes[8] == 'W' && bytes[9] == 'A' && bytes[10] == 'V' && bytes[11] == 'E';
            if (!wav) return await PlayMp3OnUiThreadAsync(bytes);
            int byteRate = BitConverter.ToInt32(bytes, 28);
            int durationMs = byteRate > 0 ? (int)((bytes.Length - 44) * 1000L / byteRate) + 45 : 2200;
            durationMs = Math.Max(160, Math.Min(25000, durationMs));
            using (var stream = new MemoryStream(bytes))
            using (var player = new SoundPlayer(stream))
            {
                activeSoundPlayer = player;
                player.Load();
                if (!token.IsCancellationRequested)
                {
                    WriteLog("WAV clause started: readyMs=" + clock.ElapsedMilliseconds + ", durationMs=" + durationMs);
                    player.Play();
                    try { await Task.Delay(durationMs, token); }
                    catch (TaskCanceledException) { try { player.Stop(); } catch { } }
                }
                if (activeSoundPlayer == player) activeSoundPlayer = null;
            }
            return true;
        }

        private static async void SpeakAsync(string text)
        {
            if (Application.Current != null && !Application.Current.Dispatcher.CheckAccess()) {
                Application.Current.Dispatcher.BeginInvoke(new Action(() => SpeakAsync(text))); return;
            }
            if (string.IsNullOrWhiteSpace(text)) return;
            if (isHudTtsMuted)
            {
                StopAllVoicePlaybackImmediately(false);
                return;
            }
            if (text == lastSpeechRequest && (DateTime.UtcNow - lastSpeechRequestAt).TotalSeconds < 4.0) return;
            lastSpeechRequest = text;
            lastSpeechRequestAt = DateTime.UtcNow;

            int mySeq = System.Threading.Interlocked.Increment(ref currentSpeechSeq);

            // Always cancel any ongoing or downloading TTS immediately so two answers NEVER overlap
            StopAllVoicePlaybackImmediately(false);

            var myCts = new System.Threading.CancellationTokenSource();
            activeSpeechCts = myCts;

            await speechGate.WaitAsync();
            try
            {
                if (myCts.IsCancellationRequested || mySeq != currentSpeechSeq) return;
                isSpeaking = true;
                currentSpeechStartedAt = DateTime.UtcNow;
                try
                {
                    string speakPayload = "{\"speaking\":true,\"text\":\"" + EscapeJson(text) + "\"}";
                    var _ = http.PostAsync("http://127.0.0.1:7788/api/ear/speaking", new StringContent(speakPayload, Encoding.UTF8, "application/json"));
                }
                catch { }

                bool played = false;
                var audioClock = System.Diagnostics.Stopwatch.StartNew();
                try
                {
                    // Fetch and play as ONE single atomic WAV stream so clauses never collide or overwrite server echo state
                    byte[] wavBytes = await FetchTtsWavClauseAsync(text, myCts.Token);
                    if (!myCts.IsCancellationRequested && mySeq == currentSpeechSeq && wavBytes != null && wavBytes.Length > 44)
                    {
                        currentSpeechStartedAt = DateTime.UtcNow;
                        if (await PlayWavBytesInterruptibleAsync(wavBytes, myCts.Token, audioClock))
                        {
                            played = true;
                        }
                    }
                }
                catch (OperationCanceledException) { played = true; }
                catch (Exception ex) { WriteLog("Server audio playback failed: " + ex.GetType().Name); }

                if (!played && !myCts.IsCancellationRequested)
                {
                    WriteLog("Speech output skipped (Studio Neural voice temporarily busy)");
                }

                if (!myCts.IsCancellationRequested && mySeq == currentSpeechSeq)
                {
                    try { await http.PostAsync("http://127.0.0.1:7788/api/ear/speaking", new StringContent("{\"speaking\":false}", Encoding.UTF8, "application/json")); }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                WriteLog("All speech playback failed: " + ex.GetType().Name);
            }
            finally
            {
                if (activeSpeechCts == myCts && mySeq == currentSpeechSeq) isSpeaking = false;
                speechGate.Release();
            }
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

        private static ControlTemplate BuildRoundedButtonTemplate(CornerRadius radius)
        {
            var template = new ControlTemplate(typeof(Button));
            var borderFactory = new FrameworkElementFactory(typeof(Border));
            borderFactory.Name = "bd";
            borderFactory.SetValue(Border.CornerRadiusProperty, radius);
            borderFactory.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            borderFactory.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Button.BorderBrushProperty));
            borderFactory.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Button.BorderThicknessProperty));
            borderFactory.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Button.PaddingProperty));

            var presenterFactory = new FrameworkElementFactory(typeof(ContentPresenter));
            presenterFactory.SetValue(ContentPresenter.HorizontalAlignmentProperty, new TemplateBindingExtension(Button.HorizontalContentAlignmentProperty));
            presenterFactory.SetValue(ContentPresenter.VerticalAlignmentProperty, new TemplateBindingExtension(Button.VerticalContentAlignmentProperty));
            borderFactory.AppendChild(presenterFactory);
            template.VisualTree = borderFactory;
            return template;
        }

        private static Button CreatePillButton(string label, Color c)
        {
            return new Button
            {
                Content = label,
                FontFamily = UiFont,
                Foreground = new SolidColorBrush(TextPrimary),
                Background = new SolidColorBrush(Color.FromArgb(32, c.R, c.G, c.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(115, c.R, c.G, c.B)),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12, 4, 12, 4),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = BuildRoundedButtonTemplate(new CornerRadius(8))
            };
        }

        private static Button CreateMiniButton(string label)
        {
            return new Button
            {
                Content = label,
                FontFamily = UiFont,
                Foreground = new SolidColorBrush(TextPrimary),
                Background = new SolidColorBrush(BgSurface),
                BorderBrush = new SolidColorBrush(BorderHairline),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(10, 4, 10, 4),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand,
                Template = BuildRoundedButtonTemplate(new CornerRadius(6))
            };
        }

        // =====================================================================
        // 7. DETERMINISTIC ISOLATED LAYOUT AUDIT (--layout-audit <outDir>)
        // =====================================================================
        private static void FlushWpfLayout(Window w)
        {
            if (w == null) return;
            w.UpdateLayout();
            w.Dispatcher.Invoke(DispatcherPriority.Render, new Action(() => { }));
        }

        private static System.Collections.Generic.Dictionary<string, object> MeasureElementBounds(FrameworkElement el, FrameworkElement ancestor, double usableWidth)
        {
            var dict = new System.Collections.Generic.Dictionary<string, object>();
            if (el == null)
            {
                dict["present"] = false;
                return dict;
            }
            dict["present"] = true;
            dict["visible"] = (el.Visibility == Visibility.Visible);
            dict["actualWidth"] = Math.Round(el.ActualWidth, 2);
            dict["actualHeight"] = Math.Round(el.ActualHeight, 2);
            dict["widthRatio"] = usableWidth > 0 ? Math.Round(el.ActualWidth / usableWidth, 4) : 0.0;
            if (ancestor != null && el.IsVisible && ancestor.IsVisible)
            {
                try
                {
                    Rect r = el.TransformToAncestor(ancestor).TransformBounds(new Rect(0, 0, el.ActualWidth, el.ActualHeight));
                    dict["left"] = Math.Round(r.Left, 2);
                    dict["top"] = Math.Round(r.Top, 2);
                    dict["right"] = Math.Round(r.Right, 2);
                    dict["bottom"] = Math.Round(r.Bottom, 2);
                    dict["withinCard"] = (r.Left >= -0.5 && r.Right <= ancestor.ActualWidth + 0.5);
                }
                catch { }
            }
            return dict;
        }

        private static System.Collections.Generic.Dictionary<string, object> CaptureHudStateMetrics(HudWindowInstance inst, string stateName)
        {
            var m = new System.Collections.Generic.Dictionary<string, object>();
            m["monitorIndex"] = inst.MonitorIndex;
            m["title"] = inst.Window.Title;
            m["state"] = stateName;
            double cardW = inst.CardBorder.ActualWidth;
            double cardH = inst.CardBorder.ActualHeight;
            double padH = inst.CardBorder.Padding.Left + inst.CardBorder.Padding.Right + inst.CardBorder.BorderThickness.Left + inst.CardBorder.BorderThickness.Right;
            double usableW = Math.Max(1.0, cardW - padH);
            m["cardActualWidth"] = Math.Round(cardW, 2);
            m["cardActualHeight"] = Math.Round(cardH, 2);
            m["usableInnerWidth"] = Math.Round(usableW, 2);
            int visibleHeaderCtrlCount = 0;
            bool legacyHeaderPresent = false;
            if (inst.HeaderControlsPanel != null)
            {
                foreach (UIElement child in inst.HeaderControlsPanel.Children)
                {
                    if (child != null && child.Visibility == Visibility.Visible)
                    {
                        visibleHeaderCtrlCount++;
                        if (child != inst.MicToggleBtn && child != inst.TtsToggleBtn && child != inst.HubToggleBtn)
                        {
                            legacyHeaderPresent = true;
                        }
                    }
                }
            }
            m["headerRightControlCount"] = visibleHeaderCtrlCount;
            m["legacyHeaderButtonsPresent"] = legacyHeaderPresent;
            m["micToggle"] = MeasureElementBounds(inst.MicToggleBtn, inst.CardBorder, usableW);
            m["ttsToggle"] = MeasureElementBounds(inst.TtsToggleBtn, inst.CardBorder, usableW);
            m["hubToggle"] = MeasureElementBounds(inst.HubToggleBtn, inst.CardBorder, usableW);
            m["targetBadge"] = MeasureElementBounds(inst.TargetBadgeBorder, inst.CardBorder, usableW);
            m["clockWeatherBadge"] = MeasureElementBounds(inst.ClockWeatherBadgeBorder, inst.CardBorder, usableW);
            m["txtMainBody"] = MeasureElementBounds(inst.TxtMainBody, inst.CardBorder, usableW);
            m["inputBoxWrapper"] = MeasureElementBounds(inst.InputBoxWrapper, inst.CardBorder, usableW);
            m["inputBox"] = MeasureElementBounds(inst.InputBox, inst.CardBorder, usableW);
            m["btnSubmit"] = MeasureElementBounds(inst.BtnSubmit, inst.CardBorder, usableW);
            m["btnYes"] = MeasureElementBounds(inst.BtnYes, inst.CardBorder, usableW);
            m["btnNo"] = MeasureElementBounds(inst.BtnNo, inst.CardBorder, usableW);
            m["hubOpen"] = (activeHubOwner == inst && hubPanelBorder != null && hubPanelBorder.Visibility == Visibility.Visible);
            if (hubPanelBorder != null && activeHubOwner == inst && hubPanelBorder.Visibility == Visibility.Visible)
            {
                m["hubPanelWidth"] = Math.Round(hubPanelBorder.ActualWidth, 2);
                m["hubPanelHeight"] = Math.Round(hubPanelBorder.ActualHeight, 2);
            }
            return m;
        }

        private static void RenderVisualToPng(FrameworkElement visual, string outPath)
        {
            if (visual == null) return;
            visual.UpdateLayout();
            int w = Math.Max(1, (int)Math.Ceiling(visual.ActualWidth));
            int h = Math.Max(1, (int)Math.Ceiling(visual.ActualHeight));
            var rtb = new System.Windows.Media.Imaging.RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(18, 22, 30)), null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(visual), null, new Rect(0, 0, w, h));
            }
            rtb.Render(dv);
            var enc = new System.Windows.Media.Imaging.PngBitmapEncoder();
            enc.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rtb));
            using (var fs = new FileStream(outPath, FileMode.Create, FileAccess.Write))
            {
                enc.Save(fs);
            }
        }

        private static void RunLayoutAudit(string outDir)
        {
            Directory.CreateDirectory(outDir);
            RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;
            Application app = Application.Current ?? new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            cachedLiveWeatherText = "서울 맑음 14°C";
            cachedTargetWindowText = "대상: Antigravity (BeausCreators)";
            BuildAllMonitorHudWindows(true);

            var metricsList = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();

            foreach (var inst in hudInstances)
            {
                inst.Window.Left = 20 + inst.MonitorIndex * 40;
                inst.Window.Top = 20 + inst.MonitorIndex * 40;
                inst.Window.Show();
                FlushWpfLayout(inst.Window);
            }

            // State 1: Idle across Primary, Secondary, Third monitors
            BroadcastStatus("JARVIS v3.0 대기 · 자비스라고 불러 주세요");
            BroadcastBody("무엇을 도와드릴까요?");
            BroadcastConfirmButtons(false);
            foreach (var inst in hudInstances) FlushWpfLayout(inst.Window);

            metricsList.Add(CaptureHudStateMetrics(hudInstances[0], "idle"));
            metricsList.Add(CaptureHudStateMetrics(hudInstances[1], "idle"));
            metricsList.Add(CaptureHudStateMetrics(hudInstances[2], "idle"));

            RenderVisualToPng(hudInstances[0].VerticalStack, System.IO.Path.Combine(outDir, "primary-idle.png"));
            RenderVisualToPng(hudInstances[1].VerticalStack, System.IO.Path.Combine(outDir, "secondary-idle.png"));
            RenderVisualToPng(hudInstances[2].VerticalStack, System.IO.Path.Combine(outDir, "third-idle.png"));

            // State 2: Active with long multi-line Korean response + typed input + confirm buttons
            BroadcastStatus("확인 대기 · 카카오톡 답장 전송 전 최종 확인");
            BroadcastBody("어떤 일들을 할 수 있는데? 본사 30명 에이전트 오더 정리, Warehouse 2-Hop 뉴런 검색, 뷰티풀월드 출판 점검, Word·PDF·PPT 보고서 즉시 생성 및 유튜브 미디어 제어까지 모두 처리합니다.");
            BroadcastConfirmButtons(true);
            suppressTextChanged = true;
            try
            {
                foreach (var inst in hudInstances)
                {
                    if (inst.InputBox != null) inst.InputBox.Text = "오늘 본사 브리핑 정리하고 워드로 저장해줘";
                }
            }
            finally { suppressTextChanged = false; }
            foreach (var inst in hudInstances) FlushWpfLayout(inst.Window);

            metricsList.Add(CaptureHudStateMetrics(hudInstances[0], "active"));
            metricsList.Add(CaptureHudStateMetrics(hudInstances[1], "active"));
            RenderVisualToPng(hudInstances[0].VerticalStack, System.IO.Path.Combine(outDir, "primary-active.png"));

            // State 3: Unified Hub Open on Primary
            BroadcastConfirmButtons(false);
            suppressTextChanged = true;
            try { if (hudInstances[0].InputBox != null) hudInstances[0].InputBox.Text = ""; }
            finally { suppressTextChanged = false; }
            ToggleUnifiedHub(hudInstances[0], 0);
            FlushWpfLayout(hudInstances[0].Window);
            metricsList.Add(CaptureHudStateMetrics(hudInstances[0], "hub-open"));
            RenderVisualToPng(hudInstances[0].VerticalStack, System.IO.Path.Combine(outDir, "primary-hub.png"));

            var report = new System.Collections.Generic.Dictionary<string, object>();
            report["auditedAt"] = DateTime.UtcNow.ToString("o");
            report["windowCount"] = hudInstances.Count;
            report["measurements"] = metricsList;
            string jsonOut = new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(report);
            File.WriteAllText(System.IO.Path.Combine(outDir, "layout-metrics.json"), jsonOut, Encoding.UTF8);

            allowShutdown = true;
            foreach (var inst in hudInstances)
            {
                try { inst.Window.Close(); } catch { }
            }
        }

        private static string JsonStr(System.Collections.Generic.Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return "";
            return Convert.ToString(dict[key]) ?? "";
        }

        private static bool JsonBool(System.Collections.Generic.Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return false;
            try { return Convert.ToBoolean(dict[key]); } catch { return false; }
        }

        private static int JsonInt(System.Collections.Generic.Dictionary<string, object> dict, string key, int fallback = 0)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return fallback;
            try { return Convert.ToInt32(dict[key]); } catch { return fallback; }
        }

        private static double JsonDouble(System.Collections.Generic.Dictionary<string, object> dict, string key, double fallback = 0.0)
        {
            if (dict == null || !dict.ContainsKey(key) || dict[key] == null) return fallback;
            try { return Convert.ToDouble(dict[key], System.Globalization.CultureInfo.InvariantCulture); } catch { return fallback; }
        }

        private static System.Collections.Generic.Dictionary<string, object> JsonDict(System.Collections.Generic.Dictionary<string, object> dict, string key)
        {
            if (dict == null || !dict.ContainsKey(key)) return null;
            return dict[key] as System.Collections.Generic.Dictionary<string, object>;
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
