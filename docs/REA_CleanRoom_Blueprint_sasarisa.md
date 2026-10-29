# 📐 REA Stage 3 & 4: Windows 11 `자비스` 참고 설계

> 이 문서는 정적 비교 자료입니다. 1:1 동등성이나 기능 완료를 입증하지 않습니다.

> **문서 등급**: REA 4분할 증거 기반 클린룸 설계 정본 (`evidencePolicy.provenanceSplit`)
> **원본 소스**: `https://github.com/mvcompany8500-web/sasarisa` (v0.7.3, AGPL-3.0; 원본 NOTICE.md 참고)
> **구현 경로**: `C:\자비스` (실행명: **자비스**)

---

## 1. REA 4분할 증거 출처 분리 (4-Split Evidence Provenance)

### ① Observations (실측 물리 수치)
- Target Repository: mvcompany8500-web/sasarisa (v0.7.3; see upstream NOTICE.md and LICENSE for AGPL-3.0)
- Total Files Scanned: 92 files (11,261 LOC, 13364.8 KB)
- Swift Source Modules: 44 .swift files in Sources/MacButler/ and Tools/
- Web & Docs Assets: 4 files (docs/index.html, docs/tutorial.html, docs/tutorial.css, docs/tutorial.js)
- Entry Points Verified: Sources/MacButler/BootScene.swift, Sources/MacButler/Butler.swift, Sources/MacButler/Commands.swift, Sources/MacButler/HUD.swift, Sources/MacButler/main.swift, Tools/butler.swift, Tools/jax.swift

### ② Derivations (AST·심볼·문법 파생 근거)
- Extracted Swift Types (class/struct/enum): 85 symbols across 44 modules
- Extracted Swift Methods (func): 516 functions deconstructed
- Extracted Deterministic Grammar Intents (Commands.swift): 34 cases (greet, type(String), claude(target: String, message: String, newSession: Bool), youtube(String), youtubeNth(Int), seek(Int), mailNth(Int), higgsfield(String), search(String), web(label: String, query: String, url: String), remember(String), media(play: Bool), ...)
- macOS Native Frameworks Imported: AVFoundation, AppKit, ApplicationServices, AudioToolbox, Charts, CoreAudio, CoreGraphics, CoreMedia, CryptoKit, Foundation, FoundationModels, ImageIO, QuartzCore, ScreenCaptureKit, Security, Speech, SwiftUI, UniformTypeIdentifiers
- Extracted HUD & Tutorial Color/Design Tokens: --blue: #2563eb | --c: #4dd9ff | --c: #ffb840}.tut .hud.lime{--c:#73ff99}
.tut .hud.pop{transform:translateY(-3px)}
.tut .htxt{flex:1 | --card: #ffffff | --green: #16a34a | --ink: #0b0d12 | --line: #eceef2 | --max: 1180px | --mute: #9aa1ad | --pad: 24px | --sky1: #9fe3ff | --sky2: #cdeffd | --sky3: #eef9ff | --soft: #f7f8fa | --sub: #6b7280

### ③ Inferences (핵심 아키텍처 패턴 추론)
- The upstream parser and aliases provide local command paths; coverage and latency have not been measured.
- Pattern 2 (Phonetic Hangul Wake Matcher): Config.soundAlike decomposes Korean syllables (사사리사/자비스 -> 싸싸리싸/재비스) so speech recognizer typos still wake the butler.
- Tools/jax.swift uses accessibility APIs; Windows behavior and feature parity are not established by this report.
- Windows message sending requires manual recipient verification; automatic sending is disabled.

### ④ Unknowns & Clean-Room Replacements (경계 및 대체 설계)
- 원본 Pro 서버 기능과 Windows 구현의 동등성은 확인되지 않았습니다. msedge-tts는 네트워크 연결에 의존할 수 있습니다.

---

## 2. macOS 원본 ➔ Windows 11 `C:\자비스` 1:1 클린룸 아키텍처 매핑표

| 원본 모듈 (macOS Swift) | 원본 역할 및 메커니즘 | Windows 11 1:1 클린룸 구현 (`C:\자비스`) | 상태 |
| :--- | :--- | :--- | :---: |
| `Sources/MacButler/main.swift` | NSApplication + NSStatusBar (◉S) + CGEvent rightOption tap/0.3s hold + 6-item permission check | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\public\index.html + public/js/jarvis-app.js (Top MenuBar ◉J + Alt/Space Tap & 0.3s Hold Push-to-Talk)` | 동등성 미검증 |
| `Sources/MacButler/BootScene.swift` | Full-screen Arc Reactor (R=330, 120/12 ticks, conic radar, 7 counter-rotating rings, synthChime 44.1kHz, real RAM/Disk boot log, fly-to-orb) | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\public\js\jarvis-app.js (WebAudio 44.1kHz synthChime + SVG/CSS Arc Reactor R=330 + Windows OS RAM/Disk telemetry + Fly-to-Orb)` | 동등성 미검증 |
| `Sources/MacButler/HUD.swift` | NSPanel (980x168) + OrbView (128x128, 3 dashed rings, radial core, audio level reactive) + cyan/amber/lime + 5 screen positions | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\public\css\jarvis-sasarisa.css + jarvis-app.js (980px HUD Card + 128px 3-Ring Orb + 5 positions + live mic level)` | 동등성 미검증 |
| `Sources/MacButler/Ear.swift & Whisper.swift` | AVAudioEngine 16kHz + SFSpeechRecognizer live partials + whisper.cpp (ggml-large-v3-turbo-q8_0.bin) + AGC | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\scripts\windows-ear-daemon.js + Web Speech API + Local Whisper API bridge + WebAudio AnalyserNode AGC` | 동등성 미검증 |
| `Sources/MacButler/Mouth.swift & DoubleTalk.swift` | AVSpeechSynthesizer (Yuna Premium) / ElevenLabs / OpenAI / Azure + Echo & Barge-in detection | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\server.js (msedge-tts 96kbps ko-KR-SunHiNeural / InJoonNeural + Windows SAPI fallback + Barge-in cut)` | 동등성 미검증 |
| `Sources/MacButler/Commands.swift & Router.swift` | Deterministic Korean Parser (34 Intent cases, soundAlike Hangul regex, Aliases 0-token cache, Fast Router) | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\scripts\butler-engine.js (34 Intent deterministic parser + Hangul soundAlike + aliases.json 0-token replay)` | 동등성 미검증 |
| `Sources/MacButler/Butler.swift` | Core state machine (idle/awake/dictation/busy/speaking), YouTube #1~#5, Coupang/Naver, Kakao previewBeforeSend, Undo, Routines | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\scripts\butler-engine.js (Full state machine, real YouTube search + #1~#5 pick, 20s previewBeforeSend [응]/[아니], Undo, Routines)` | 동등성 미검증 |
| `Sources/MacButler/SetupWindow.swift & SettingsWindow.swift` | SwiftUI First-Run Setup Checklist (6 checks + Whisper download) & Full Settings Form (Basics, Learning, Mic/Speaker, Voice, Usage, Brain) | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\public\index.html + jarvis-app.js (Interactive Setup Modal & Full 7-Section Settings Modal editing config.json)` | 동등성 미검증 |
| `Sources/MacButler/Learning.swift & GrowTogether.swift` | learned.json (3x repeat auto-promotion), memory.md, usage.json (7-day chart), shared-aliases.json (GrowTogether) | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\data\ (config.json, aliases.json, learned.json, memory.md, usage.json, shared-aliases.json)` | 동등성 미검증 |
| `Tools/jax.swift & Hands.swift` | macOS AXUIElement CLI (list, front, tap, field, key, volume, open-app, open-url) without moving user mouse | `과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): C:\자비스\scripts\butler-engine.js (Windows PowerShell UIAutomation / SendKeys / WScript.Shell volume / Start-Process)` | 동등성 미검증 |
