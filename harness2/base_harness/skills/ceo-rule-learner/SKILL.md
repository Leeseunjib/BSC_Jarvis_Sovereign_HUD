---
name: ceo-rule-learner
description: "대표님과 대화할 때 말씀하시는 새로운 규칙, 금지 사항, 업무 스타일, 선호 음악/작업 방식을 자동으로 감지하여 구조화된 규칙 원장과 Google Harness² guardrails/EXPERIENCE에 즉시 반영합니다. Trigger words: 규칙, 헌장, 앞으로, 하지마, 기억해, 취향, 스타일, 로그, 정리, 학습, 나를가장잘아는."
---
# CEO Rule & Preference Real-Time Learner Skill

## 1. 자동 규칙 감지 패턴
- 대표님 발화 중 `"앞으로 ~해"`, `"~하지 마"`, `"규칙으로 추가해"`, `"기억해"`, `"스킬로 만들어"`, `"이렇게 하면 좋겠어"` 패턴을 실시간 추출합니다.
- 추출된 규칙은 **① 대표님 대화 규칙 원장(`jarvis-plans-and-rules.json`)**, **② Google Harness² `guardrails.md`**, **③ 도메인 경험 원장(`EXPERIENCE.md`)**, **④ `jarvis-loop.md`** 4곳에 동시 기록됩니다.

## 2. HUD 메뉴 실시간 조회
- 바탕화면 자비스 HUD의 **`☰ 메뉴` → `📓 CHLGD · 대화 규칙 & 로그 창`**에서 대표님께서 언제든 깔끔하게 분류된 규칙·로그·스킬 현황을 한눈에 열람하실 수 있습니다.
