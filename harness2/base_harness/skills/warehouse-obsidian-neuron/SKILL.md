---
name: warehouse-obsidian-neuron
description: "C:\Warehouse (05.BSC_Skill_Warehouse)의 7대 도메인, 18대 BSC 정본 스킬, 217개 세부 스킬, 55개 GitHub 오픈소스, 32인 에이전트 무장 맵을 옵시디언 뉴런 그래프(79 Nodes / 138 Synapses)로 2-Hop 검색·시각화·검증하는 스킬입니다. Trigger words: 뉴런, 옵시디언, 시냅스, 웨어하우스, warehouse, 스킬창고, 창고뉴런, bsc warehouse, 2-hop, 7대도메인."
---
# Warehouse Obsidian Neuron & 2-Hop Synapse Skill

## 1. 5-Layer 옵시디언 뉴런 볼트 (`C:\Warehouse\3.Obsidian_Vault`)
- **Layer 0 (Master MOC)**: `00_Master_Neuron_MOC.md`
- **Layer 1 (7대 도메인 허브)**: `01_Domains/DOM-01..DOM-07.md`
- **Layer 2 (18대 BSC 정본 스킬)**: `02_Originals/*.md`
- **Layer 3 (19대 외부 패키지 & 55대 GitHub 레포)**: `03_Imported_Packs/*.md`
- **Layer 4 (32인 전사 에이전트 무장 맵)**: `04_Agents_Skill_Map/Agent_*.md`
- **Layer 5 (REA Clean-Room 블루프린트)**: `05_REA_Blueprints/REA_Blueprints_Hub.md`

## 2. 자비스 실행 및 API 연동
- **음성/텍스트 오더**: *"자비스, 뉴런에서 REA 검색해줘"*, *"창고 뉴런 보여줘"*, *"창고 감사 돌려줘"*
- **REST API**: `GET /api/warehouse/neurons?q=...`, `POST /api/warehouse/neurons`
- **CLI**: `bsc warehouse --neuron`, `bsc warehouse --audit`, `bsc warehouse --search <keyword>`
