# Experience Library R — Domain: `voice_and_hud`

## [2026-10-30 응답 지연 수정]
- **현재 기준**: 음성 종료 대기는 일반 2초, 완결된 시간·음악 정지 명령은 1.2초를 기본으로 한다. 계속 말하는 동안 실행하지 않고, 같은 인식 결과의 최종 확정으로 대기를 반복하지 않는다. 내 목소리 확인은 설정에서 실제 등록한 뒤 활성화하며, 타인·영상 소리를 완벽하게 차단한다고 보장하지 않는다. 이전의 3.8초 대기·F0 기반 완벽 차단 설명은 폐기한다.
- **Surface Lever**: `c1:systemprompt.md`, `c6:guardrails.md`, `c6:skills/chlgd-orchestrator/SKILL.md`
