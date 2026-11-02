from pathlib import Path
import re
p = Path('C:/자비스/scripts/JarvisMonitorOverlay.cs')
s = p.read_text(encoding='utf-8-sig')
def replace(old,new):
    global s
    assert old in s, old[:100]
    s=s.replace(old,new)

replace('private static double currentSpeechSpeedRatio = 1.25;', '''private static double currentSpeechSpeedRatio = 1.25;
        private static string currentSpeechVoice = "ko-KR-HyunsuMultilingualNeural";
        private static TextBlock settingsFeedback;
        private static readonly System.Threading.SemaphoreSlim settingsSaveGate = new System.Threading.SemaphoreSlim(1, 1);''')
a=s.index('        private static void SaveBrainAndSpeedConfig(')
b=s.index('        private static FrameworkElement CreateTopRightGearIcon()',a)
s=s[:a]+'''        private static void SetSettingsFeedback(string message, bool error = false)
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
                var cfg = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string, object>>(File.ReadAllText("C:\\\\자비스\\\\data\\\\config.json", Encoding.UTF8));
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

''' +s[b:]
a=s.index('        private static void SavePlanAndApiKeyConfig(')
b=s.index('        private static void ShowHotkeySettingsWindow()',a)
s=s[:a]+'''        private static Task<bool> SavePlanAndApiKeyConfig(string planTier, string apiKey)
        {
            var patch = new System.Collections.Generic.Dictionary<string, object>();
            if (apiKey != null) patch["geminiApiKey"] = apiKey;
            else patch["userPlanTier"] = planTier;
            return SaveSettingsPatchAsync(patch);
        }

''' +s[b:]
replace('private static void ShowHotkeySettingsWindow()\n        {','private static void ShowHotkeySettingsWindow()\n        {\n            RefreshStoredSettings();')
replace('Width = 760,\n                Height = 490,\n                Left = (SystemParameters.PrimaryScreenWidth - 760) / 2.0,\n                Top = Math.Max(48, (SystemParameters.PrimaryScreenHeight - 490) / 2.0),', '''Width = Math.Min(1000, SystemParameters.WorkArea.Width - 32),
                Height = Math.Min(720, SystemParameters.WorkArea.Height - 48),
                MinWidth = 680,
                MinHeight = 420,
                ResizeMode = ResizeMode.CanResizeWithGrip,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,''')
replace('Grid.SetRow(paneHost, 1);\n            rightGrid.Children.Add(paneHost);','''ScrollViewer settingsScroll = new ScrollViewer { Content = paneHost, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 10, 0) };
            Grid.SetRow(settingsScroll, 1);
            rightGrid.Children.Add(settingsScroll);''')
replace('targetPane.Visibility = Visibility.Visible;', 'targetPane.Visibility = Visibility.Visible;\n                    settingsScroll.ScrollToTop();')
replace('StackPanel topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 3) };','WrapPanel topRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 5) };')
replace('Text = descText,','Text = descText,\n                    TextWrapping = TextWrapping.Wrap,')
replace('Text = title,\n                    FontFamily', 'Text = title,\n                    TextWrapping = TextWrapping.Wrap,\n                    FontFamily')
replace('statusTxt.Text = "적용됨";', 'statusTxt.Text = "저장됨";')
replace('Action<string, string, string> selectModelAndSave = (prov, mod, displayTitle) =>','Action<string, string, string> selectModelAndSave = async (prov, mod, displayTitle) =>')
replace('SaveBrainAndSpeedConfig(prov, mod, currentSpeechSpeedRatio);', 'if (!await SaveModelConfig(prov, mod)) return;')
replace('모델이 적용되었습니다', '모델이 저장되었습니다 · 연결 권한은 별도 확인')
replace('tabBtn.MouseLeftButtonUp += (s, e) =>','tabBtn.MouseLeftButtonUp += async (s, e) =>')
replace('SavePlanAndApiKeyConfig(tKey, null);', 'if (!await SavePlanAndApiKeyConfig(tKey, null)) return;')
replace('createActionBtn("API 키 저장", false, () =>','createActionBtn("API 키 저장", false, async () =>')
replace('SavePlanAndApiKeyConfig(currentUserPlanTier, apiKeyBox.Text.Trim());\n                txtApiSaved.Text = "저장됨";', 'bool saved = await SavePlanAndApiKeyConfig(currentUserPlanTier, apiKeyBox.Password.Trim());\n                txtApiSaved.Text = saved ? "저장됨 · 연결 미확인" : "저장 실패";\n                if (saved) apiKeyBox.Clear();')
replace('TextBox apiKeyBox = new TextBox', 'PasswordBox apiKeyBox = new PasswordBox')
# PasswordBox has no Text property.
a=s.index('PasswordBox apiKeyBox')
b=s.index('apiInputRow.Children.Add(apiKeyBox)',a)
s=s[:a]+s[a:b].replace('Text = ""','Password = ""')+s[b:]
for name in ['apiInputRow','customInputRow','mediaBtnRow','presetHkRow']:
    replace('StackPanel '+name+' = new StackPanel { Orientation = Orientation.Horizontal };','WrapPanel '+name+' = new WrapPanel();')
replace('spdBtn.MouseLeftButtonUp += (s, e) =>','spdBtn.MouseLeftButtonUp += async (s, e) =>')
replace('SaveBrainAndSpeedConfig(currentBrainProvider, currentBrainModel, spdVal);','if (!await SaveSettingsPatchAsync(new System.Collections.Generic.Dictionary<string, object> { { "ttsSpeed", spdVal } })) return;')
replace('string[] speedNames = new string[] { "1.00x 차분함", "1.15x 자연스러움", "1.25x 권장", "1.35x 빠름", "1.50x 초고속" };', 'string[] speedNames = new string[] { "1.00×", "1.15×", "1.25×", "1.35×", "1.50×" };')
replace('Text = "한국어 Neural 자연음성 말하기 속도",','Text = "답변 목소리와 말하기 속도",')
replace('x — 원하는 단계를 선택하면 즉시 음성으로 안내합니다.', 'x · 저장된 속도는 다음 음성부터 적용됩니다.')
replace('voiceStack.Children.Add(txtSpeedDesc);', '''voiceStack.Children.Add(txtSpeedDesc);
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
            voiceStack.Children.Add(previewVoice);''')
# Replace unverified biometric claims and dangerous hard-coded energy gate with read-only microphone status.
a=s.index('            Border voicePrintCard = new Border')
b=s.index('            paneVoice.Children.Add(voicePrintCard);',a)+len('            paneVoice.Children.Add(voicePrintCard);')
s=s[:a]+'''            Border microphoneCard = new Border { Background = new SolidColorBrush(bgSurface), CornerRadius = new CornerRadius(10), Padding = new Thickness(16) };
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
                    string status = health != null && health.ContainsKey("status") ? Convert.ToString(health["status"]) : "상태 미확인";
                    string input = health != null && health.ContainsKey("audioInputLabel") ? Convert.ToString(health["audioInputLabel"]) : "입력 장치 미확인";
                    double level = state.ContainsKey("micLevel") ? Convert.ToDouble(state["micLevel"]) : 0;
                    micState.Text = "인식 상태: " + status + " · 입력 레벨: " + level.ToString("0.0000") + "\\n" + input;
                }
                catch { micState.Text = "마이크 상태를 가져오지 못했습니다. 서버 연결을 확인해 주세요."; }
            }));
            paneVoice.Children.Add(microphoneCard);''' +s[b:]
# Bounded layouts, honest status text.
replace('"모든 변경사항은 선택 즉시 실시간으로 자비스 엔진에 반영됩니다."','"선택값을 저장합니다. 모델 접근 권한·요금은 계정과 공급자에서 확인하세요."')
replace('Grid.SetColumn(footerStatus, 0);','settingsFeedback = footerStatus;\n            footerStatus.TextWrapping = TextWrapping.Wrap;\n            footerStatus.Margin = new Thickness(0, 0, 12, 0);\n            Grid.SetColumn(footerStatus, 0);')
replace('TextBlock txtPaneDesc = new TextBlock\n            {','TextBlock txtPaneDesc = new TextBlock\n            {\n                TextWrapping = TextWrapping.Wrap,')
replace('Text = "현재 활성 모델",','Text = "저장된 모델 · 연결 별도 확인",')
replace('"Google AI Ultra 요금제"','"선택한 분류: Ultra"')
replace('"Pro · 유료 API 요금제"','"선택한 분류: Pro / API"')
replace('"Local 온디바이스 ($0)"','"선택한 분류: 로컬"')
replace('"Free 무료 티어 ($0)"','"선택한 분류: 무료"')
replace('"사용 중인 구글 요금제 티어(Ultra / Pro / Free / Local)에 맞춰 최적의 모델을 선택합니다."','"모델 연결 방식을 선택합니다. 분류는 참고용이며 실제 구독·할당량을 조회한 결과가 아닙니다."')
replace('"내 목소리 인식 · 음성"','"음성 · 마이크"')
replace('"바탕화면 · 단축키 제어"','"미디어 · 단축키"')
replace('"대표님 전용 성문(Voice Print) 등록으로 외부 소음을 차단하고 자연음성 배속을 조정합니다."','"답변 목소리와 말하기 속도를 선택하고 마이크 연결 상태를 확인합니다."')
replace('"Google 계정 OAuth 연동 (Ultra · Pro · Free 요금제 자동 적용)"','"Google 계정 로그인 · Gemini CLI"')
replace('"대표님의 Google AI Ultra 계정이나 사용자 본인의 구글 계정으로 로그인하면 별도 API 과금 없이 본인 요금제 한도로 즉시 연동됩니다."','"Gemini CLI 설치와 Google 로그인이 필요합니다. 이 화면은 구독 등급이나 남은 할당량을 자동 조회하지 않습니다. Google 로그인과 API 키의 과금·제한은 서로 다릅니다."')
replace('"개인 API 키 등록 (Google AI Studio / OpenAI)"','"Google Gemini API 키 등록"')
replace('"재생 중인 유튜브 · 음악 즉시 종료"','"음악 정지 요청"')
replace('"자비스가 실행한 모든 브라우저와 앱은 대표님의 실제 바탕화면(Winsta0\\\\Default)에 표시되며, 음성(\\"음악 꺼줘\\")이나 아래 버튼으로 즉시 제어할 수 있습니다."','"음성으로 ‘음악 꺼줘’라고 말하거나 정지 버튼을 누르세요. 지원되는 미디어 세션을 제어하며, 실행 결과는 메인 HUD에 표시합니다."')
# Model descriptions: keep official IDs, remove promises about entitlements and Live support.
a=s.index('            Action renderSelectedTierModels = () =>')
b=s.index('            for (int idx',a)
part=s[a:b]
def card(m):
    title,mid,provider,badge,desc=m.groups()
    names={'gemini-4-argon':'Gemini 4 Argon','gemini-3.8-flash':'Gemini 3.8 Flash','gemini-3.1-pro-preview':'Gemini 3.1 Pro Preview','gemini-3-flash-preview':'Gemini 3 Flash Preview','gemini-3.5-flash-lite':'Gemini 3.5 Flash-Lite','gemini-2.5-flash':'Gemini 2.5 Flash','gemini-2.5-pro':'Gemini 2.5 Pro'}
    title=names.get(mid,title)
    badges={'gemini-cli':'Google 로그인 · CLI','gemini':'Gemini API 키','openai':'OpenAI API 키 · 별도 설정','ollama':'로컬 · Ollama 필요','lmstudio':'로컬 · LM Studio 필요'}
    badge=badges.get(provider,'연결 설정 필요')
    descs={'gemini-4-argon':'추론 모델. 실제 접근 권한과 CLI 지원 여부는 로그인 계정에서 확인해야 합니다.','gemini-3.8-flash':'텍스트 응답·도구 호출용 모델입니다. 실시간 음성용 Gemini Live와는 별도 모델입니다.','gemini-3.1-pro-preview':'추론·분석용 프리뷰 모델. 사용 가능 여부와 제한은 계정 및 인증 방식에 따라 다릅니다.','gemini-3-flash-preview':'범용 Flash 프리뷰 모델. 무료 사용 가능 여부와 할당량은 로그인 계정에서 확인하세요.','gemini-3.5-flash-lite':'경량 텍스트 작업용 모델. API 프로젝트의 사용 가능 여부·요금·한도를 확인하세요.','gemini-2.5-flash':'이전 세대 모델. 기존 프로젝트의 사용 가능 여부와 API 한도를 확인하세요.','gemini-2.5-pro':'이전 세대 추론 모델. API 프로젝트의 사용 가능 여부·과금·한도를 확인하세요.'}
    desc=descs.get(mid,desc.replace('플래그십','응답'))
    return 'buildModelCard(\n                        '+',\n                        '.join('"'+x+'"' for x in [title,mid,provider,badge,desc])+'\n                    )'
part=re.sub(r'buildModelCard\(\s*"([^"]*)",\s*"([^"]*)",\s*"([^"]*)",\s*"([^"]*)",\s*"([^"]*)"\s*\)',card,part)
s=s[:a]+part+s[b:]
# Correct preview and playback to use the server's saved voice; decoded PCM preserves voice and prosody.
s=s.replace('스튜디오 에이치디 현수 자연음성으로 정상 작동 중입니다.', '선택한 목소리와 속도로 안내하고 있습니다.')
s=s.replace('HD 자연음성 미리 듣기 (현수 Neural)', '설정한 음성 미리 듣기')
replace('/api/tts-mp3?voice=ko-KR-HyunsuMultilingualNeural&text=', '/api/tts-mp3?format=wav&text=')
replace('new System.Threading.CancellationTokenSource(12000)', 'new System.Threading.CancellationTokenSource(20000)')
needle='''                        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                        if (response.IsSuccessStatusCode && bytes.Length > 44)'''
replace(needle,'''                        byte[] bytes = await response.Content.ReadAsByteArrayAsync();
                        System.Collections.Generic.IEnumerable<string> values;
                        string actualVoice = response.Headers.TryGetValues("X-Jarvis-Tts-Voice", out values) ? values.FirstOrDefault() : "unknown";
                        string actualSpeed = response.Headers.TryGetValues("X-Jarvis-Tts-Speed", out values) ? values.FirstOrDefault() : "unknown";
                        bool fallbackVoice = response.Headers.TryGetValues("X-Jarvis-Tts-Fallback", out values) && values.FirstOrDefault() == "1";
                        WriteLog("TTS response voice=" + actualVoice + ", speed=" + actualSpeed + ", fallback=" + fallbackVoice);
                        SetSettingsFeedback(fallbackVoice ? "선택한 Neural 음성 사용 불가 · Windows 음성으로 대체합니다. 속도는 유지됩니다." : "재생 음성: " + actualVoice + " · 속도 " + actualSpeed + "×", fallbackVoice);
                        if (response.IsSuccessStatusCode && bytes.Length > 44)''')
# WPF MediaFailed details help diagnose older MP3 playback attempts.
s=s.replace('EventHandler<ExceptionEventArgs> failed = (s, e) => finished.TrySetResult(false);','EventHandler<ExceptionEventArgs> failed = (s, e) => { WriteLog("MP3 MediaFailed: " + e.ErrorException.Message); finished.TrySetResult(false); };')
p.write_text(s,encoding='utf-8-sig')
print('Patched native settings UI and speech settings flow')
