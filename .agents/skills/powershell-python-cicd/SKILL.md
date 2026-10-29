---
name: powershell-python-cicd
description: "Windows 11 환경에서 한글 인코딩 깨짐 없이 PowerShell(.ps1), Python(.py), Node.js(.js) 및 Git CI/CD 파이프라인을 실행하고 검증하는 스킬입니다. Trigger words: ps1, powershell, python, py, cicd, ci/cd, git, verify, 파워쉘, 파이썬, 빌드, 배포, 검증, 테스트."
---
# PowerShell · Python · Git CI/CD Execution Skill

## 1. 핵심 인코딩 & 안전 수칙 (error-log.md #44, #46 준수)
- **PowerShell (.ps1)**: `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8` 강제 선언 및 파일 저장 시 UTF-8 with BOM 유지.
- **Python (.py)**: 실행 시 반드시 `$env:PYTHONUTF8='1'`, `$env:PYTHONIOENCODING='utf-8'` 주입.
- **Data Loss Prevention**: `rm -rf`, `Remove-Item -Recurse`, `git reset --hard`, `git clean` 등 파괴적 명령은 대표님 사전 승인 없이 절대 실행 금지.

## 2. 표준 CI/CD 파이프라인
1. `node --check` 및 `Freeze-VerifiedRuntime.js` 해시 검증
2. `test-speech-turn.js`, `test-speech-client.js`, `test-speech-replacement.js`, `test-media-control.js` 4대 자동 테스트 통과
3. `Build-VerifiedHud.ps1`로 `JarvisHud.exe` 무결성 컴파일
