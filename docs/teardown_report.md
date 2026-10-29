# 🔬 REA Stage 1 & 2 Teardown Report: `mvcompany8500-web/sasarisa` (v0.7.3)

> **분석 엔진**: `REA (Reverse Engineer Anything v6.7.0 + BSC 4-Stage Clean-Room Analyzer)`
> **분석 대상**: `C:\자비스\vendor\sasarisa` (GitHub 원본 클론)
> **타겟 시스템**: Windows 자비스. 정적 추출 결과이며 기능의 동등성을 검증하지 않습니다.

---

## 1. Stage 1: 물리 표면 전수 센서스 (Observations)
- **총 스캔 파일 수**: **92개**
- **총 소스 코드 라인 수(LOC)**: **11,261줄** (13364.8 KB)
- **확장자별 분포**: `.yml`(4), `.md`(10), `.none`(6), `.icns`(1), `.entitlements`(1), `.swift`(44), `.sh`(6), `.png`(8), `.gif`(2), `.html`(2), `.webp`(4), `.jpg`(1), `.mp4`(1), `.css`(1), `.js`(1)
- **핵심 엔트리포인트**: `Sources/MacButler/BootScene.swift`, `Sources/MacButler/Butler.swift`, `Sources/MacButler/Commands.swift`, `Sources/MacButler/HUD.swift`, `Sources/MacButler/main.swift`, `Tools/butler.swift`, `Tools/jax.swift`

---

## 2. Stage 2: Swift 파일의 패턴 기반 심볼 추출

| 모듈 경로 | 줄 수 | 추출된 타입 (Class/Struct/Enum) | 주요 추출 함수 (Functions) | SHA-256 |
| :--- | :---: | :--- | :--- | :---: |
| `Sources/MacButler/AIUsage.swift` | 85 | `enum AIUsage`, `struct Plan`, `struct Window` | `load()`, `save()`, `noteClaude()`, `refreshCodex()`, `findRateLimits()`, `daily()` (6개) | `453f71f7` |
| `Sources/MacButler/APIBrain.swift` | 379 | `class APIBrain`, `class ToolBoxClient`, `enum Brains`, `enum Keys`, `protocol Thinker`, `struct Provider`, `struct Reply` | `warm()`, `ask()`, `stop()`, `cancel()`, `make()`, `ready()` (24개) | `41093abf` |
| `Sources/MacButler/AsideBrain.swift` | 149 | `class AsideBrain` | `warm()`, `ask()`, `feed()`, `finish()`, `answer()`, `clean()` (9개) | `a42500d5` |
| `Sources/MacButler/AudioDevices.swift` | 105 | `enum AudioDevices`, `enum Sound`, `struct AudioDevice` | `hasStreams()`, `all()`, `defaultInputUID()`, `defaultOutputName()`, `outputBusy()`, `micUsers()` (14개) | `75695d50` |
| `Sources/MacButler/BootScene.swift` | 250 | `class BootScene`, `enum SysInfo`, `struct Text` | `memory()`, `model()`, `diskFreeGB()`, `synthChime()`, `ring()`, `ticks()` (9개) | `3264f331` |
| `Sources/MacButler/Brain.swift` | 233 | `class Brain` | `systemPrompt()`, `permissionArgs()`, `mcpConfig()`, `commandLine()`, `q()`, `stepLabel()` (15개) | `0ae94c7e` |
| `Sources/MacButler/Butler.swift` | 1618 | `class Butler`, `enum Mode` | `canBargeIn()`, `start()`, `listenForSignals()`, `signal()`, `holdBegan()`, `holdEnded()` (74개) | `1a1de470` |
| `Sources/MacButler/ClaudeRelay.swift` | 141 | `enum ClaudeRelay` | `score()`, `clearBest()`, `ask()`, `pick()`, `compose()`, `directOrder()` (9개) | `aabd844d` |
| `Sources/MacButler/CodexBrain.swift` | 223 | `class CodexBrain`, `struct Caps` | `model()`, `listedModels()`, `capabilities()`, `run()`, `arguments()`, `q()` (13개) | `ad1c4f10` |
| `Sources/MacButler/ComboBrain.swift` | 60 | `class ComboBrain` | `pick()`, `brain()`, `warm()`, `ask()`, `stop()`, `cancel()` (6개) | `923a51ae` |
| `Sources/MacButler/Commands.swift` | 686 | `enum Intent`, `struct Parser` | `app()`, `match()`, `kakaoReply()`, `squash()`, `clean()`, `wakeRange()` (41개) | `154bac3b` |
| `Sources/MacButler/Config.swift` | 306 | `enum Paths`, `struct Config`, `struct ElevenLabsConfig` | `soundAlike()`, `hangulReading()`, `fill()`, `handyVocabulary()`, `set()`, `load()` (10개) | `b737c975` |
| `Sources/MacButler/ControlSocket.swift` | 69 | `class ControlSocket` | `start()`, `stop()` (2개) | `5147bb4a` |
| `Sources/MacButler/DemoFrames.swift` | 109 | `enum DemoFrames`, `struct Scene` | `run()`, `frame()`, `compose()`, `displayAll()` (4개) | `ca42ff06` |
| `Sources/MacButler/DoubleTalk.swift` | 90 | `class DoubleTalk` | `listen()`, `begin()`, `end()`, `played()`, `isUser()`, `periodicity()` (6개) | `b17fdc69` |
| `Sources/MacButler/Ear.swift` | 339 | `class Ear` | `segment()`, `start()`, `observeEngine()`, `restartEngine()`, `startEngine()`, `process()` (16개) | `13ff8839` |
| `Sources/MacButler/EchoTest.swift` | 44 | `enum EchoTest` | `run()` (1개) | `bff623c2` |
| `Sources/MacButler/GrowTogether.swift` | 132 | `enum GrowTogether`, `struct State` | `saveState()`, `isShareable()`, `cleanSaid()`, `lookup()`, `veto()`, `askIfNeeded()` (10개) | `2eec007a` |
| `Sources/MacButler/HUD.swift` | 175 | `class HUD`, `class OrbView` | `setColor()`, `setSpeed()`, `level()`, `layout()`, `positions()`, `paint()` (12개) | `2559a5cd` |
| `Sources/MacButler/Hands.swift` | 140 | `enum AX`, `enum Hands` | `key()`, `playPause()`, `mediaKey()`, `paste()`, `typeVisible()`, `attr()` (18개) | `62e01533` |
| `Sources/MacButler/Higgsfield.swift` | 192 | `class Higgsfield`, `struct HFError`, `struct Plan` | `request()`, `stop()`, `quote()`, `generate()`, `run()`, `setGuard()` (12개) | `bfb354a0` |
| `Sources/MacButler/KoreanReader.swift` | 84 | `enum KoreanReader` | `sino()`, `under10000()`, `native()`, `read()`, `latinModels()` (5개) | `2917b58e` |
| `Sources/MacButler/Learning.swift` | 228 | `enum Growth`, `enum Learning`, `enum Memory`, `enum Usage`, `struct Entry`, `struct State` | `save()`, `key()`, `tokens()`, `replayable()`, `remember()`, `forget()` (21개) | `c016f7b3` |
| `Sources/MacButler/MicCapture.swift` | 40 | `class MicCapture` | `start()`, `stop()`, `captureOutput()` (3개) | `055c596e` |
| `Sources/MacButler/Mouth.swift` | 232 | `class Mouth` | `speak()`, `complete()`, `bestVoice()`, `rank()`, `say()`, `sayCommand()` (16개) | `dc286bd7` |
| `Sources/MacButler/NameModel.swift` | 55 | `enum NameModel` | `phrases()`, `prepare()` (2개) | `eb03290c` |
| `Sources/MacButler/NameTraining.swift` | 155 | `class NameTrainer`, `struct Result`, `struct Sample` | `command()`, `prompt()`, `take()`, `parser()`, `hits()`, `result()` (14개) | `ff28949a` |
| `Sources/MacButler/Polish.swift` | 85 | `enum Polish` | `handyPrompt()`, `run()` (2개) | `2ba52889` |
| `Sources/MacButler/ProAccount.swift` | 130 | `enum ProAccount`, `struct Status` | `authed()`, `call()`, `link()`, `unlink()`, `refresh()`, `speechRequest()` (8개) | `50f92730` |
| `Sources/MacButler/Router.swift` | 124 | `class Router`, `enum Aliases` | `prompt()`, `warm()`, `ensure()`, `feed()`, `reply()`, `route()` (10개) | `32e78f5a` |
| `Sources/MacButler/Routines.swift` | 32 | `enum Routines` | `load()`, `save()`, `remove()`, `match()`, `write()` (5개) | `8ee10f07` |
| `Sources/MacButler/SelfTest.swift` | 374 | `enum SelfTest` | `run()`, `check()`, `checkBool()` (3개) | `7ed679f4` |
| `Sources/MacButler/SettingsWindow.swift` | 653 | `class Closer`, `class NameTrainingModel`, `class SettingsModel`, `enum SettingsWindow`, `struct Choice`, `struct EngineMenu`, `struct NameTrainingRow`, `struct Note`, `struct SettingsView`, `struct UsageBar` | `refreshUsage()`, `preview()`, `refreshPro()`, `linkPro()`, `unlinkPro()`, `keyHint()` (16개) | `f771e83b` |
| `Sources/MacButler/SetupWindow.swift` | 290 | `class SetupModel`, `enum SetupWindow`, `struct SetupView` | `previewVoice()`, `save()`, `refresh()`, `openPane()`, `download()`, `urlSession()` (11개) | `798b712a` |
| `Sources/MacButler/Strings.swift` | 63 | `struct Strings` | `t()`, `menuToggle()`, `menuGreet()`, `menuFolder()`, `menuQuit()`, `hotkeyHint()` (6개) | `32a1505d` |
| `Sources/MacButler/Timers.swift` | 47 | `enum Timers`, `struct Item` | `add()`, `restore()`, `cancelLast()`, `cancelAll()`, `schedule()`, `save()` (6개) | `d9265e7e` |
| `Sources/MacButler/Utterances.swift` | 24 | `enum Utterances` | `keep()` (1개) | `fb55384c` |
| `Sources/MacButler/Whisper.swift` | 221 | `class Whisper` | `warm()`, `stop()`, `transcribe()`, `next()`, `saidTwice()`, `attempt()` (18개) | `3cc8ded8` |
| `Sources/MacButler/Writer.swift` | 73 | `enum Writer` | `instruction()`, `selection()`, `rewrite()` (3개) | `02079161` |
| `Sources/MacButler/main.swift` | 284 | `class AppDelegate` | `applicationDidFinishLaunching()`, `menuWillOpen()`, `fixPermission()`, `installHotkey()`, `toggle()`, `greet()` (13개) | `f9d8de9e` |
| `Tools/butler.swift` | 61 | - | `butlerSend()`, `hfGuard()`, `block()` (3개) | `811d0a82` |
| `Tools/jax.swift` | 325 | - | `butlerSend()`, `attr()`, `frame()`, `label()`, `guardApp()`, `record()` (16개) | `58203916` |
| `Tools/mcp.swift` | 132 | `struct Tool` | `s()`, `i()`, `run()`, `str()`, `call()`, `text()` (8개) | `54dfc610` |
| `docs/make-gif.swift` | 27 | - |  (0개) | `b180556f` |

---

## 3. `Commands.swift` 결정론적 명령 문법(Intent Enum) 추출 결과 (34종)
```swift
case greet
case type(String)
case claude(target: String, message: String, newSession: Bool)
case youtube(String)
case youtubeNth(Int)
case seek(Int)
case mailNth(Int)
case higgsfield(String)
case search(String)
case web(label: String, query: String, url: String)
case remember(String)
case media(play: Bool)
case closeTabs(label: String, host: String)
case mediaNext
case displayOff
case closeOpened
case browser(String)
case volume(set: Int?, delta: Int, mute: Bool)
case clock(date: Bool)
case timer(seconds: Int, label: String)
case cancelTimers
case map(String)
case multi([String])
case routine(String)
case rewrite(String)
case undo
case whatDid
case makeRoutine(name: String, steps: [String])
case badRoutine(String)
case forgetRoutine(String)
case openApp(spoken: String, app: String)
case kakaoReply(to: String, text: String)
case kakaoType(to: String, text: String)
case none
```

---

## 4. UI / HUD 디자인 토큰 및 컬러 추출 결과 (`HUD.swift`, `BootScene.swift`, `tutorial.css`)
- **CSS Variables**:
  - `--blue: #2563eb`
  - `--c: #4dd9ff`
  - `--c: #ffb840}.tut .hud.lime{--c:#73ff99}
.tut .hud.pop{transform:translateY(-3px)}
.tut .htxt{flex:1`
  - `--card: #ffffff`
  - `--green: #16a34a`
  - `--ink: #0b0d12`
  - `--line: #eceef2`
  - `--max: 1180px`
  - `--mute: #9aa1ad`
  - `--pad: 24px`
  - `--sky1: #9fe3ff`
  - `--sky2: #cdeffd`
  - `--sky3: #eef9ff`
  - `--soft: #f7f8fa`
  - `--sub: #6b7280`
- **HUD Core States**:
  - `cyan`: `rgb(0, 235, 255)` — 기본 대기·경청·발화 상태
  - `amber`: `rgb(255, 179, 38)` — 두뇌(Brain) 작업 중 / 주의 알림
  - `lime`: `rgb(89, 255, 153)` — 루틴 학습 완료 / 위치 미리보기 확인
