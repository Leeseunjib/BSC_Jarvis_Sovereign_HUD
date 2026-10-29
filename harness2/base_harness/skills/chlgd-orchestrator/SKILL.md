---
name: chlgd-orchestrator
description: "C:\BeausCreators 및 C:\Warehouse의 CHLGD 5단계 워크플로우(Start-Context -> Harness Gate -> Loop WorkLog -> Graph -> Dynamic Skill)를 자비스에서 실행하고 관리하는 핵심 스킬입니다. Trigger words: chlgd, context, harness, loop, graph, dynamic, 컨텍스트, 하네스, 루프, 오케스트라, 작업일지, 에러로그."
---
# CHLGD Orchestrator Skill (자비스 × BeausCreators)

## 1. 목적 (Purpose)
- 자비스(JARVIS Sovereign Butler)가 이건우 대표님의 음성/텍스트 오더를 수행할 때 **Context → Harness → Loop → Graph → Dynamic (CHLGD)** 5단계 규율을 100% 준수합니다.
- `C:\BeausCreators` 본사(26인 에이전트) 및 `C:\Warehouse`(`05.BSC_Skill_Warehouse`)의 모든 스킬 자산을 실시간 인덱싱하고 호출합니다.

## 2. 5단계 실행 프로토콜
1. **Context (문맥 핀셋 로드)**: 작업 전 `error-log.md`, `ceo-profile.json`, `jarvis-plans-and-rules.json`을 먼저 확인합니다.
2. **Harness (데이터 보호 및 검증 게이트)**: 파일 무단 삭제·덮어쓰기 차단, `.ps1` UTF-8 BOM, `.py` `PYTHONUTF8=1`, `Freeze-VerifiedRuntime.js` 무결성 검증을 통과합니다.
3. **Loop (실시간 로그 및 자가성장)**: 모든 대화 규칙과 오더를 `ceo-orders-log.jsonl`과 `jarvis-loop.md`에 영구 Append합니다.
4. **Graph (26인 에이전트 수평 협력)**: 소하(기획), 태준(QC), 거누(CTO), 서진(백엔드), 유나(프론트), 우진(AI/MCP), 수호(보안) 등과 수평 협력합니다.
5. **Dynamic (스킬 자율 생성 및 Harness² 진화)**: 반복되는 패턴이나 대표님의 새로운 규칙 지시를 즉시 스킬과 가드레일로 자산화합니다.
