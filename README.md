# 🛡 BSC JARVIS Sovereign HUD (`C:\자비스`)

> **이건우 대표님 공식 지정 정본(Main SSOT) 및 상용(Commercial Production) 자비스**
> - **정본 워크스페이스**: `C:\자비스`
> - **공식 GitHub 저장소**: `https://github.com/Leeseunjib/BSC_Jarvis_Sovereign_HUD.git` (`main`)
> - **정본 포트**: `http://127.0.0.1:7788` (`service: "jarvis"`)
> - **샌드박스 구분**: `C:\BSC_Javis_OS` 등 타 폴더는 실험용 샌드박스(Sandbox)이며, 실제 운영 및 추후 상용화(Commercialization)는 본 프로젝트(`C:\자비스`)를 유일한 메인으로 진행합니다.

---

## 1. 핵심 기능 아키텍처 (Commercial Main Spec)

1. **Windows 11 네이티브 데스크톱 HUD (`bin/JarvisHud.exe` & `scripts/JarvisMonitorOverlay.cs`)**:
   - 주 모니터 상시 대기 HUD, 음성 호출어(`자비스`, `헤이 자비스`, `네 자비스`) 및 끝까지 경청하는 턴 버퍼(`SpeechTurnBuffer`), 화자 성문(`WeSpeaker ONNX` + `SpeakerGate`) 검증.
   - 실제 미디어 재생 중일 때만 자동으로 나타나는 슬림 단일 바 뮤직 HUD (`⏮` · `⏯` · `⏭` · `✕`).
   - 설정 창의 모델 요금제 구분 (`Free 무료 티어`: `Gemini 3.8 Flash` Google 로그인 기본 쿼터 / `Gemini 3.5 Flash-Lite` AI Studio `$0` 무료 티어).
2. **3대 생태계 100% 통합 인지 및 검색 (`scripts/ecosystem-index.js`)**:
   - `C:\BeausCreators` (본사·지점·데이터센터), `C:\BeautifulWorld` (출판사 7-Class OS), `C:\Warehouse` (스킬·리서치 창고) 총 **`24,693+`개 파일, `665+`개 스킬** 실시간 인덱싱 및 로컬+웹 통합 검색.
3. **검색 결과 전용 UI 창 & 4대 오피스 문서 스튜디오 (`scripts/office-doc-engine.js`)**:
   - 검색 명령 시 바탕화면에 전용 `자비스 검색 결과` 창 오픈.
   - 창 상단 원클릭 버튼(`📝 Markdown (.md)`, `📘 Word (.docx)`, `📕 PDF (.pdf)`, `📙 PPT (.pptx)`) 또는 음성 명령으로 즉시 문서 생성·검증·열기 (`docs/exports/YYYY-MM-DD/`).
4. **REA 4-Stage 클린룸 역공학 & Laya 구조화 언어 실행 (`scripts/rea-laya-bridge.js`)**:
   - `Invoke-ReaAnalyzer.js` 기반 4분할 증거 추적(`Observations` / `Derivations` / `Inferences` / `Unknowns`) 분석 및 블루프린트 생성.
   - `convaiinnovations/laya-multilingual` 구조화 상태·질문(`{ state, questions }`) 의사결정 및 System-1 라우팅.
5. **Google Harness² + BeausCreators 재귀 자가학습 시스템 (`scripts/recursive-learning.js`)**:
   - 매 대화 턴마다 **`Observe → Propose → Probe → Compose → Apply`** 5단계 재귀 루프 수행.
   - 대표님과의 대화에서 규칙·취향·교정·반복 명령·실패 교훈을 자동 추출하여 헌장 충돌 검사(`Probe`) 후 `data/learning/learned-rules.json`, `harness2/base_harness/guardrails.md`, `data/jarvis-loop.md`에 승격하고 매 답변 프롬프트(`P=1,500자` 예산)에 실시간 주입.
6. **Antigravity IDE 연동 MCP 서버 (`scripts/jarvis-mcp-server.js`)**:
   - `POST /mcp` JSON-RPC 2.0 및 stdio MCP 서버를 통해 Antigravity IDE와 실시간 협업 (9대 도구 지원).

---

## 2. 빌드 · 설치 · 검증 명령어

```powershell
# 1. C# HUD 컴파일 및 검증된 런타임(server.verified.js, butler-engine.verified.js, JarvisHud.exe) 설치
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\자비스\scripts\Build-VerifiedHud.ps1"
powershell -NoProfile -ExecutionPolicy Bypass -File "C:\자비스\scripts\Install-VerifiedRuntime.ps1"

# 2. 자동화 테스트 스위트 실행
node C:\자비스\scripts\test-recursive-learning.js
npm run test:speech
npm run test:media
npm test
```
