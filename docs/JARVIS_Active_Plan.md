# 🧠 C:\Warehouse 옵시디언 뉴런 (79 Nodes · 138 Synapses) × JARVIS 완전 직결 설계서

- **분류**: JARVIS × Warehouse Obsidian Neuron & CHLGD CI/CD
- **작성**: JARVIS Sovereign Butler (CHLGD × Harness²)
- **업데이트**: 2026-11-03 03:12:27

> C:\Warehouse (05.BSC_Skill_Warehouse)의 5계층 옵시디언 뉴런 볼트(79 Nodes / 138 Synapses), 7대 도메인, 18대 BSC 정본 스킬, 217개 세부 스킬, 55대 GitHub 레포, 32인 에이전트 무장 맵, 그리고 bsc warehouse CI/CD 파이프라인을 C:\자비스(포트 7788 · JarvisHud.exe · MCP Server)에 100% 직결 완료했습니다.

## 1. Warehouse 5계층 옵시디언 뉴런 ↔ 자비스 엔진 직결
• 그래프 정본: C:\Warehouse\00.운영\warehouse_neuron_graph.json (79 Nodes / 138 Synapses / 7 Domains / 18 Originals / 19 Imported Packs / 32 Agent Maps)
• 자비스 인덱스 모듈: C:\자비스\scripts\ecosystem-index.js (getWarehouseNeuronGraph, searchWarehouseNeurons, neuronToResearchReport 탑재)
• 2-Hop 시냅스 탐색 속도: 4ms ~ 13ms 이내 (1-Hop 직접 뉴런 + 2-Hop 도메인·에이전트·원본팩·REA 블루프린트 동시 추출)

## 2. 자비스 음성/텍스트 오더 & 전용 UI 창(Research HUD / Plan HUD) 연동
• 「자비스, 옵시디언 뉴런에서 REA 검색해줘」 / 「창고 뉴런 보여줘」 / 「뉴런에서 유니티 찾아줘」 ➔ 전용 검색 결과 창(ShowResearchResultAsync)에 1-Hop 직접 뉴런과 2-Hop 연결 에이전트·도메인·시냅스 엣지 즉시 팝업 및 1클릭 Word(.docx)·MD(.md)·PDF(.pdf)·PPT(.pptx) 변환 지원
• 「자비스, 창고 감사 돌려줘」 / 「뉴런 재구축해줘」 ➔ 실시간 터미널 미니 창에서 18대 정본 스킬 6-Gate 마스터 감사(Test-MasterAuditAndVerification.ps1) 및 뉴런 그래프 재구축 실행

## 3. REST API & Antigravity 2.0 MCP 양방향 도구 확장
• GET /api/warehouse/neurons (& ?q=키워드) : 79 Nodes / 138 Synapses 전체 현황 및 2-Hop 시냅스 검색
• POST /api/warehouse/neurons : 뉴런 볼트 실시간 재구축(rebuild) 및 Plan HUD 동기화
• MCP Tool 추가: jarvis-sovereign-butler 내 jarvis_warehouse_neuron 도구 및 warehouse-obsidian-neuron 코어 스킬 탑재
• 런타임 무결성 검증: Freeze-VerifiedRuntime.js 통과 (server.verified.js & butler-engine.verified.js SHA-256 해시 잠금 완료)
