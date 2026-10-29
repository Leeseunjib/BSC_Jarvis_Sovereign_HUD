---
name: antigravity-mcp-bridge
description: "자비스가 MCP 서버(Stdio & HTTP /mcp) 역할을 수행하여 Antigravity IDE와 양방향으로 작업을 공유하고, Plan/설계 문서를 바탕화면 HUD 전용 뷰어 창에 실시간 렌더링하는 스킬입니다. Trigger words: mcp, antigravity, plan, hud, 안티그래비티, 플랜, 계획, 기획서, 문서창, 연동."
---
# Antigravity 2.0 ↔ JARVIS MCP Bridge & Plan HUD Skill

## 1. MCP 서버 아키텍처
- **Stdio MCP Server**: `C:\자비스\scripts\jarvis-mcp-server.js` (`C:\Users\USER\.gemini\config\mcp_config.json` 내 `jarvis-sovereign-butler`로 등록)
- **Streamable HTTP MCP Endpoint**: `http://127.0.0.1:7788/mcp` (JSON-RPC 2.0)

## 2. 제공 MCP Tools
- `jarvis_status_and_profile`: 자비스 상태, 대표님 프로파일, 대화 규칙, BeausCreators & Warehouse 스킬 목록 조회
- `jarvis_execute_command`: 자비스 Butler/터미널/CHLGD 오더 즉시 실행
- `jarvis_open_plan_hud`: Antigravity 또는 자비스가 작성한 Plan/설계 문서를 바탕화면 **📋 Plan · 문서 프리뷰 HUD 창**에 즉시 팝업 렌더링
- `jarvis_record_rule_or_log`: 대표님과의 대화에서 도출된 새로운 규칙이나 CHLGD 학습 로그를 영구 기록
- `jarvis_run_harness2_recursion`: Google Research Harness² `Propose[INV+DOM] -> Probe -> Compose` 자가진화 루프 실행
