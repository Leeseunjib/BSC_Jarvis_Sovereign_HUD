# AGENTS.md — C:\자비스 (BSC JARVIS Sovereign HUD · 정본 메인 & 상용 프로덕션 SSOT)

> **[2026-11-03 이건우 대표님 공식 지정 LOCK]**
> - **정본(Main SSOT) 및 상용화(Commercial Production) 기준 자비스**: **`C:\자비스`** (`https://github.com/Leeseunjib/BSC_Jarvis_Sovereign_HUD.git`, 포트 `127.0.0.1:7788`, 실행기 `C:\자비스\bin\JarvisHud.exe` + `C:\자비스\server.verified.js`)
> - **샌드박스(Sandbox) 격리**: `C:\BSC_Javis_OS` 등 다른 자비스 폴더는 모두 **실험용 샌드박스(Sandbox)**이며, 앞으로 모든 자비스 고도화·실무 연동·상용 제품화는 오직 본 프로젝트(**`C:\자비스`**)를 유일한 메인으로 진행한다.

---

## 0. 핵심 의사소통 및 태도 (Cold Objectivity)
- **사용자 호칭**: 반드시 **"대표님"** 또는 **"이건우 대표님"**(BeausCreators CEO)으로 지칭한다.
- **실측 보고 원칙**: 추측성 수치나 과장을 배제하고, 실제 검증된 물리 지표(`Bytes`, `Lines`, `ms`, `Pass/Fail`)와 작동 한계만 보고한다.
- **데이터 유실 방지 (Zero Data Loss)**: 사전 승인 없는 파일 삭제 금지, 기존 문서/로그 통째 덮어쓰기(Blind Overwrite) 금지(반드시 Append).

## 1. 정본 아키텍처 및 핵심 모듈 (`C:\자비스`)
| 구분 | 정본 파일 경로 | 역할 및 상용 기준 스펙 |
| :--- | :--- | :--- |
| **데스크톱 HUD 앱** | `scripts/JarvisMonitorOverlay.cs` → `bin/JarvisHud.exe` | Windows 11 주 모니터 상시 HUD, 검색 결과 전용 창(`ShowResearchResultAsync`), Plan·규칙 뷰어, 미니 뮤직 컨트롤러(재생 시에만 표시), 설정 창 |
| **메인 백엔드 서버** | `server.js` → `server.verified.js` | `127.0.0.1:7788` 단독 점유 (`GET /api/health` → `service: "jarvis"`), 음성/텍스트 오더 실행, TTS/STT, MCP(`POST /mcp`) |
| **집사 명령 엔진** | `scripts/butler-engine.js` → `scripts/butler-engine.verified.js` | 음성/텍스트 인텐트 라우팅, 앱/미디어/브라우저 제어, 3대 생태계 검색, 오피스 문서, REA/Laya, 재귀 학습 |
| **3대 생태계 인덱스** | `scripts/ecosystem-index.js` | `C:\BeausCreators`, `C:\BeautifulWorld`, `C:\Warehouse` 전수 인덱싱(`24,693+` 파일, `665+` 스킬) 및 통합 검색 |
| **4대 오피스 문서 엔진** | `scripts/office-doc-engine.js` | 외부 의존성 없는 순수 Node.js 기반 `.md`, `.docx`, `.pptx`, `.pdf` 생성 및 무결성 검증(`verifyDocument`) |
| **REA · Laya 브릿지** | `scripts/rea-laya-bridge.js` | `Invoke-ReaAnalyzer.js` 4-Stage 클린룸 역공학 & `laya-multilingual` / System-1 구조화 의사결정 |
| **Google Harness² 재귀 학습** | `scripts/recursive-learning.js` | 매 대화 턴 `Observe → Propose → Probe → Compose → Apply` 재귀 루프로 규칙 자동 추출·검증·승격 및 프롬프트 주입(`P=1,500자`) |
| **런타임 승격·설치기** | `scripts/Build-VerifiedHud.ps1`, `scripts/Install-VerifiedRuntime.ps1` | 소스 해시 검증 후 `server.verified.js`, `butler-engine.verified.js`, `bin/JarvisHud.exe` 무중단 승격 |

## 2. 에이전트 아바타 네임플레이트 및 워룸 회의 규칙 (2026-10-19 LOCK)
- 일반 채팅 발화 시 첫 줄에 빈 대괄호(`![]()`)로 가로형 네임플레이트 출력:
  `![](/C:/BeausCreators/.cursor/agent_images/{id}/avatar_card.png)`
- 다중 에이전트 워룸 회의 시 각 담당 에이전트 배너와 인용구(`> "..."`)로 발언한다.
