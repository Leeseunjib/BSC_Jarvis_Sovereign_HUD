---
name: office-doc-studio
description: "검색 결과나 대표님 지시 주제를 바탕으로 Word(.docx), Markdown(.md), PDF(.pdf), PowerPoint(.pptx) 문서를 외부 유료 라이브러리 없이 즉시 생성하고 바탕화면에 열어주는 스킬입니다. Trigger words: word, docx, md, markdown, pdf, ppt, pptx, 워드, 마크다운, 피디에프, 파워포인트, 발표자료, 보고서작성, 문서만들어."
---
# Office Document Studio Skill (.docx / .md / .pdf / .pptx)

## 1. 지원 포맷 (Zero-Dependency OpenXML & Edge Headless)
- **Markdown (`.md`)**: UTF-8 구조화 마크다운 문서
- **Word (`.docx`)**: OpenXML WordprocessingML 정품 패키지 (표지·요약·섹션·출처 자동 조판, Microsoft Word 2016+ 호환)
- **PowerPoint (`.pptx`)**: OpenXML PresentationML 16:9 와이드 슬라이드 덱 (표지 + 섹션별 핵심 불릿 슬라이드 자동 생성)
- **PDF (`.pdf`)**: HTML 프리뷰 조판 후 Microsoft Edge/Chrome Headless `--print-to-pdf` 엔진으로 한글 폰트(맑은 고딕) 무결성 PDF 출력
