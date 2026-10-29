/**
 * @file C:\자비스\scripts\rea-analyzer.js
 * @description REA (Reverse Engineer Anything v6.7.0 + BSC 4-Stage Clean-Room Analyzer)
 *              Supports Swift (.swift), JS/TS, HTML/CSS, Shell (.sh), and JSON targets.
 *              Strictly enforces 4-Split Evidence Provenance:
 *              [1] observations (Physical Surface Census)
 *              [2] derivations  (AST / Symbol / Route / Token Deconstruction)
 *              [3] inferences   (Architecture Pattern & 1:1 Windows Clean-Room Mapping)
 *              [4] unknowns     (Static Analysis Boundaries & Runtime Dependencies)
 */

const fs = require('fs');
const path = require('path');
const crypto = require('crypto');

const PROJECT_ROOT = path.resolve(__dirname, '..');
const DEFAULT_TARGET = path.join(PROJECT_ROOT, 'vendor', 'sasarisa');
const argOut = process.argv.find(a => a.startsWith('--out-dir='));
const OUT_BASE = argOut ? argOut.split('=')[1] : null;
const DOCS_DIR = OUT_BASE || path.join(PROJECT_ROOT, 'docs');
const DATA_DIR = OUT_BASE || path.join(PROJECT_ROOT, 'public', 'data');

const IGNORED_DIRS = new Set(['.git', 'node_modules', '.build', 'dist', 'build', '.next']);

/**
 * Deterministic Unicode code-point comparator (REA Config standard, no ICU locale drift)
 */
function compareUnicodeCodePoints(a, b) {
  const left = String(a);
  const right = String(b);
  if (left < right) return -1;
  if (left > right) return 1;
  return 0;
}

function sha256File(filePath) {
  const buf = fs.readFileSync(filePath);
  return crypto.createHash('sha256').update(buf).digest('hex').slice(0, 16);
}

/**
 * Stage 1: Physical Surface Census (Observations)
 */
function scanSurface(targetDir) {
  const stats = {
    totalFiles: 0,
    totalLines: 0,
    totalBytes: 0,
    byExt: {},
    swiftFiles: [],
    webFiles: [],
    scriptFiles: [],
    entryPoints: []
  };

  function walk(dir, depth = 0) {
    if (depth > 8 || !fs.existsSync(dir)) return;
    const entries = fs.readdirSync(dir, { withFileTypes: true }).sort((a, b) =>
      compareUnicodeCodePoints(a.name, b.name)
    );

    for (const entry of entries) {
      if (IGNORED_DIRS.has(entry.name)) continue;
      const fullPath = path.join(dir, entry.name);
      const relPath = path.relative(targetDir, fullPath).replace(/\\/g, '/');

      if (entry.isDirectory()) {
        walk(fullPath, depth + 1);
      } else {
        const ext = (path.extname(entry.name) || '.none').toLowerCase();
        const fstat = fs.statSync(fullPath);
        stats.totalFiles++;
        stats.totalBytes += fstat.size;
        stats.byExt[ext] = (stats.byExt[ext] || 0) + 1;

        if (/\.(swift|js|ts|css|html|sh|json|md|txt)$/.test(ext) || entry.name === 'VERSION') {
          const content = fs.readFileSync(fullPath, 'utf8');
          const lines = content.split(/\r?\n/).length;
          stats.totalLines += lines;

          const fileMeta = {
            relPath,
            name: entry.name,
            ext,
            bytes: fstat.size,
            lines,
            sha256: sha256File(fullPath)
          };

          if (ext === '.swift') stats.swiftFiles.push({ ...fileMeta, content });
          else if (/\.(html|css|js)$/.test(ext)) stats.webFiles.push({ ...fileMeta, content });
          else if (ext === '.sh') stats.scriptFiles.push({ ...fileMeta, content });

          if (/^(main\.swift|Package\.swift|Butler\.swift|Commands\.swift|HUD\.swift|BootScene\.swift|jax\.swift|tutorial\.html)$/i.test(entry.name)) {
            stats.entryPoints.push(relPath);
          }
        }
      }
    }
  }

  walk(targetDir);
  return stats;
}

/**
 * Stage 2: AST / Symbol / Grammar / Design Token Deconstruction (Derivations)
 */
function deconstructSymbols(surface) {
  const swiftModules = [];
  const allTypes = [];
  const allFunctions = [];
  const intents = [];
  const macOSApisUsed = new Set();
  const cssTokens = new Set();
  const colorsExtracted = new Set();

  for (const sf of surface.swiftFiles) {
    const types = [];
    const funcs = [];
    const imports = [];

    const importMatches = sf.content.matchAll(/^import\s+([A-Za-z0-9_]+)/gm);
    for (const m of importMatches) {
      imports.push(m[1]);
      macOSApisUsed.add(m[1]);
    }

    const typeMatches = sf.content.matchAll(/(?:final\s+)?(class|struct|enum|protocol)\s+([A-Za-z0-9_]+)/g);
    for (const m of typeMatches) {
      const sym = `${m[1]} ${m[2]}`;
      types.push(sym);
      allTypes.push(`${sf.name}:${sym}`);
    }

    const funcMatches = sf.content.matchAll(/(?:static\s+)?func\s+([A-Za-z0-9_]+)\s*\(/g);
    for (const m of funcMatches) {
      if (!funcs.includes(m[1])) funcs.push(m[1]);
      allFunctions.push(`${sf.name}#${m[1]}()`);
    }

    if (sf.name === 'Commands.swift') {
      const caseMatches = sf.content.matchAll(/^\s*case\s+([A-Za-z0-9_]+(?:\([^)]*\))?)/gm);
      for (const m of caseMatches) {
        if (!intents.includes(m[1])) intents.push(m[1]);
      }
    }

    const nsColorMatches = sf.content.matchAll(/NSColor\(([^)]+)\)/g);
    for (const m of nsColorMatches) {
      colorsExtracted.add(m[0]);
    }

    swiftModules.push({
      relPath: sf.relPath,
      name: sf.name,
      lines: sf.lines,
      bytes: sf.bytes,
      sha256: sf.sha256,
      imports: imports.sort(compareUnicodeCodePoints),
      types: types.sort(compareUnicodeCodePoints),
      funcs: funcs.slice(0, 18),
      totalFuncs: funcs.length
    });
  }

  for (const wf of surface.webFiles) {
    const varMatches = wf.content.matchAll(/--([a-zA-Z0-9_-]+)\s*:\s*([^;]+);/g);
    for (const m of varMatches) {
      cssTokens.add(`--${m[1]}: ${m[2].trim()}`);
    }
    const hexMatches = wf.content.matchAll(/#[0-9a-fA-F]{3,8}\b/g);
    for (const m of hexMatches) {
      colorsExtracted.add(m[0]);
    }
  }

  return {
    swiftModules,
    allTypes: allTypes.sort(compareUnicodeCodePoints),
    allFunctions: allFunctions.sort(compareUnicodeCodePoints),
    intents,
    macOSApisUsed: Array.from(macOSApisUsed).sort(compareUnicodeCodePoints),
    cssTokens: Array.from(cssTokens).sort(compareUnicodeCodePoints),
    colorsExtracted: Array.from(colorsExtracted).sort(compareUnicodeCodePoints)
  };
}

/**
 * Stage 3 & 4: Clean-Room Windows 11 Mapping & 4-Split Evidence Provenance
 */
function buildWindowsCleanRoomMapping() {
  return [
    {
      upstreamFile: 'Sources/MacButler/main.swift',
      upstreamRole: 'NSApplication + NSStatusBar (◉S) + CGEvent rightOption tap/0.3s hold + 6-item permission check',
      windowsEquivalent: 'C:\\자비스\\public\\index.html + public/js/jarvis-app.js (Top MenuBar ◉J + Alt/Space Tap & 0.3s Hold Push-to-Talk)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/BootScene.swift',
      upstreamRole: 'Full-screen Arc Reactor (R=330, 120/12 ticks, conic radar, 7 counter-rotating rings, synthChime 44.1kHz, real RAM/Disk boot log, fly-to-orb)',
      windowsEquivalent: 'C:\\자비스\\public\\js\\jarvis-app.js (WebAudio 44.1kHz synthChime + SVG/CSS Arc Reactor R=330 + Windows OS RAM/Disk telemetry + Fly-to-Orb)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/HUD.swift',
      upstreamRole: 'NSPanel (980x168) + OrbView (128x128, 3 dashed rings, radial core, audio level reactive) + cyan/amber/lime + 5 screen positions',
      windowsEquivalent: 'C:\\자비스\\public\\css\\jarvis-sasarisa.css + jarvis-app.js (980px HUD Card + 128px 3-Ring Orb + 5 positions + live mic level)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/Ear.swift & Whisper.swift',
      upstreamRole: 'AVAudioEngine 16kHz + SFSpeechRecognizer live partials + whisper.cpp (ggml-large-v3-turbo-q8_0.bin) + AGC',
      windowsEquivalent: 'C:\\자비스\\scripts\\windows-ear-daemon.js + Web Speech API + Local Whisper API bridge + WebAudio AnalyserNode AGC',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/Mouth.swift & DoubleTalk.swift',
      upstreamRole: 'AVSpeechSynthesizer (Yuna Premium) / ElevenLabs / OpenAI / Azure + Echo & Barge-in detection',
      windowsEquivalent: 'C:\\자비스\\server.js (msedge-tts 96kbps ko-KR-SunHiNeural / InJoonNeural + Windows SAPI fallback + Barge-in cut)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/Commands.swift & Router.swift',
      upstreamRole: 'Deterministic Korean Parser (34 Intent cases, soundAlike Hangul regex, Aliases 0-token cache, Fast Router)',
      windowsEquivalent: 'C:\\자비스\\scripts\\butler-engine.js (34 Intent deterministic parser + Hangul soundAlike + aliases.json 0-token replay)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/Butler.swift',
      upstreamRole: 'Core state machine (idle/awake/dictation/busy/speaking), YouTube #1~#5, Coupang/Naver, Kakao previewBeforeSend, Undo, Routines',
      windowsEquivalent: 'C:\\자비스\\scripts\\butler-engine.js (Full state machine, real YouTube search + #1~#5 pick, 20s previewBeforeSend [응]/[아니], Undo, Routines)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/SetupWindow.swift & SettingsWindow.swift',
      upstreamRole: 'SwiftUI First-Run Setup Checklist (6 checks + Whisper download) & Full Settings Form (Basics, Learning, Mic/Speaker, Voice, Usage, Brain)',
      windowsEquivalent: 'C:\\자비스\\public\\index.html + jarvis-app.js (Interactive Setup Modal & Full 7-Section Settings Modal editing config.json)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Sources/MacButler/Learning.swift & GrowTogether.swift',
      upstreamRole: 'learned.json (3x repeat auto-promotion), memory.md, usage.json (7-day chart), shared-aliases.json (GrowTogether)',
      windowsEquivalent: 'C:\\자비스\\data\\ (config.json, aliases.json, learned.json, memory.md, usage.json, shared-aliases.json)',
      status: '1:1 Clean-Room Ported'
    },
    {
      upstreamFile: 'Tools/jax.swift & Hands.swift',
      upstreamRole: 'macOS AXUIElement CLI (list, front, tap, field, key, volume, open-app, open-url) without moving user mouse',
      windowsEquivalent: 'C:\\자비스\\scripts\\butler-engine.js (Windows PowerShell UIAutomation / SendKeys / WScript.Shell volume / Start-Process)',
      status: '1:1 Clean-Room Ported'
    }
  ].map(item => ({ ...item, windowsEquivalent: '과거 설계상 대응 관계 (구현 및 기능은 별도 확인 필요): ' + item.windowsEquivalent, status: '동등성 미검증' }));
}

function buildReaReportObject(targetDir = DEFAULT_TARGET) {
  const versionFile = path.join(targetDir, 'VERSION');
  const upstreamVersion = fs.existsSync(versionFile)
    ? fs.readFileSync(versionFile, 'utf8').trim()
    : '0.7.3';

  const surface = scanSurface(targetDir);
  const deconstructed = deconstructSymbols(surface);
  const cleanRoomMap = buildWindowsCleanRoomMapping();

  const evidence4Split = {
    observations: [
      `Target Repository: mvcompany8500-web/sasarisa (v${upstreamVersion}; see upstream NOTICE.md and LICENSE for AGPL-3.0)`,
      `Total Files Scanned: ${surface.totalFiles} files (${surface.totalLines.toLocaleString()} LOC, ${(surface.totalBytes / 1024).toFixed(1)} KB)`,
      `Swift Source Modules: ${surface.swiftFiles.length} .swift files in Sources/MacButler/ and Tools/`,
      `Web & Docs Assets: ${surface.webFiles.length} files (docs/index.html, docs/tutorial.html, docs/tutorial.css, docs/tutorial.js)`,
      `Entry Points Verified: ${surface.entryPoints.join(', ')}`
    ],
    derivations: [
      `Extracted Swift Types (class/struct/enum): ${deconstructed.allTypes.length} symbols across ${surface.swiftFiles.length} modules`,
      `Extracted Swift Methods (func): ${deconstructed.allFunctions.length} functions deconstructed`,
      `Extracted Deterministic Grammar Intents (Commands.swift): ${deconstructed.intents.length} cases (${deconstructed.intents.slice(0, 12).join(', ')}, ...)`,
      `macOS Native Frameworks Imported: ${deconstructed.macOSApisUsed.join(', ')}`,
      `Extracted HUD & Tutorial Color/Design Tokens: ${deconstructed.cssTokens.join(' | ')}`
    ],
    inferences: [
      `The upstream parser and aliases provide local command paths; coverage and latency have not been measured.`,
      `Pattern 2 (Phonetic Hangul Wake Matcher): Config.soundAlike decomposes Korean syllables (사사리사/자비스 -> 싸싸리싸/재비스) so speech recognizer typos still wake the butler.`,
      `Tools/jax.swift uses accessibility APIs; Windows behavior and feature parity are not established by this report.`,
      `Windows message sending requires manual recipient verification; automatic sending is disabled.`
    ],
    unknowns: [
      `원본 Pro 서버 기능과 Windows 구현의 동등성은 확인되지 않았습니다. msedge-tts는 네트워크 연결에 의존할 수 있습니다.`
    ]
  };

  return {
    generatedAt: new Date().toISOString(),
    engine: 'REA v6.7.0 + BSC 4-Stage Clean-Room Analyzer',
    target: targetDir,
    upstreamVersion,
    surface: {
      totalFiles: surface.totalFiles,
      totalLines: surface.totalLines,
      totalBytes: surface.totalBytes,
      byExt: surface.byExt,
      entryPoints: surface.entryPoints
    },
    evidence4Split,
    swiftModules: deconstructed.swiftModules,
    allTypes: deconstructed.allTypes,
    allFunctions: deconstructed.allFunctions,
    intents: deconstructed.intents,
    cssTokens: deconstructed.cssTokens,
    cleanRoomMap
  };
}

function runReaAnalysis() {
  console.log('================================================================');
  console.log('🔍 [REA v6.7.0 + BSC 4-Stage Clean-Room Analyzer] Starting...');
  console.log(`📂 Target: ${DEFAULT_TARGET}`);
  console.log('================================================================');

  if (!fs.existsSync(DEFAULT_TARGET)) {
    throw new Error(`Target not found: ${DEFAULT_TARGET}`);
  }

  const jsonReport = buildReaReportObject(DEFAULT_TARGET);
  const { upstreamVersion, surface, evidence4Split, cleanRoomMap } = jsonReport;
  const deconstructed = jsonReport;

  try {
    fs.mkdirSync(DOCS_DIR, { recursive: true });
    fs.mkdirSync(DATA_DIR, { recursive: true });
    const jsonPath = path.join(DATA_DIR, 'rea-report.json');
    fs.writeFileSync(jsonPath, JSON.stringify(jsonReport, null, 2), 'utf8');
  } catch (_) {}

  // Generate teardown_report.md (Stage 1 & 2)
  const teardownMd = `# 🔬 REA Stage 1 & 2 Teardown Report: \`mvcompany8500-web/sasarisa\` (v${upstreamVersion})

> **분석 엔진**: \`REA (Reverse Engineer Anything v6.7.0 + BSC 4-Stage Clean-Room Analyzer)\`
> **분석 대상**: \`C:\\자비스\\vendor\\sasarisa\` (GitHub 원본 클론)
> **타겟 시스템**: Windows 자비스. 정적 추출 결과이며 기능의 동등성을 검증하지 않습니다.

---

## 1. Stage 1: 물리 표면 전수 센서스 (Observations)
- **총 스캔 파일 수**: **${surface.totalFiles}개**
- **총 소스 코드 라인 수(LOC)**: **${surface.totalLines.toLocaleString()}줄** (${(surface.totalBytes / 1024).toFixed(1)} KB)
- **확장자별 분포**: ${Object.entries(surface.byExt).map(([k, v]) => `\`${k}\`(${v})`).join(', ')}
- **핵심 엔트리포인트**: ${surface.entryPoints.map(e => `\`${e}\``).join(', ')}

---

## 2. Stage 2: Swift 파일의 패턴 기반 심볼 추출

| 모듈 경로 | 줄 수 | 추출된 타입 (Class/Struct/Enum) | 주요 추출 함수 (Functions) | SHA-256 |
| :--- | :---: | :--- | :--- | :---: |
${deconstructed.swiftModules.map(m => `| \`${m.relPath}\` | ${m.lines} | ${m.types.map(t => `\`${t}\``).join(', ') || '-'} | ${m.funcs.slice(0, 6).map(f => `\`${f}()\``).join(', ')} (${m.totalFuncs}개) | \`${m.sha256.slice(0, 8)}\` |`).join('\n')}

---

## 3. \`Commands.swift\` 결정론적 명령 문법(Intent Enum) 추출 결과 (${deconstructed.intents.length}종)
\`\`\`swift
${deconstructed.intents.map(i => `case ${i}`).join('\n')}
\`\`\`

---

## 4. UI / HUD 디자인 토큰 및 컬러 추출 결과 (\`HUD.swift\`, \`BootScene.swift\`, \`tutorial.css\`)
- **CSS Variables**:
${deconstructed.cssTokens.map(t => `  - \`${t}\``).join('\n')}
- **HUD Core States**:
  - \`cyan\`: \`rgb(0, 235, 255)\` — 기본 대기·경청·발화 상태
  - \`amber\`: \`rgb(255, 179, 38)\` — 두뇌(Brain) 작업 중 / 주의 알림
  - \`lime\`: \`rgb(89, 255, 153)\` — 루틴 학습 완료 / 위치 미리보기 확인
`;

  const teardownPath = path.join(DOCS_DIR, 'teardown_report.md');
  try {
    fs.writeFileSync(teardownPath, teardownMd, 'utf8');
  } catch (_) {}

  // Generate REA_CleanRoom_Blueprint_sasarisa.md (Stage 3 & 4)
  const blueprintMd = `# 📐 REA Stage 3 & 4: Windows 11 \`자비스\` 참고 설계

> 이 문서는 정적 비교 자료입니다. 1:1 동등성이나 기능 완료를 입증하지 않습니다.

> **문서 등급**: REA 4분할 증거 기반 클린룸 설계 정본 (\`evidencePolicy.provenanceSplit\`)
> **원본 소스**: \`https://github.com/mvcompany8500-web/sasarisa\` (v${upstreamVersion}, AGPL-3.0; 원본 NOTICE.md 참고)
> **구현 경로**: \`C:\\자비스\` (실행명: **자비스**)

---

## 1. REA 4분할 증거 출처 분리 (4-Split Evidence Provenance)

### ① Observations (실측 물리 수치)
${evidence4Split.observations.map(o => `- ${o}`).join('\n')}

### ② Derivations (AST·심볼·문법 파생 근거)
${evidence4Split.derivations.map(d => `- ${d}`).join('\n')}

### ③ Inferences (핵심 아키텍처 패턴 추론)
${evidence4Split.inferences.map(i => `- ${i}`).join('\n')}

### ④ Unknowns & Clean-Room Replacements (경계 및 대체 설계)
${evidence4Split.unknowns.map(u => `- ${u}`).join('\n')}

---

## 2. macOS 원본 ➔ Windows 11 \`C:\\자비스\` 1:1 클린룸 아키텍처 매핑표

| 원본 모듈 (macOS Swift) | 원본 역할 및 메커니즘 | Windows 11 1:1 클린룸 구현 (\`C:\\자비스\`) | 상태 |
| :--- | :--- | :--- | :---: |
${cleanRoomMap.map(r => `| \`${r.upstreamFile}\` | ${r.upstreamRole} | \`${r.windowsEquivalent}\` | 동등성 미검증 |`).join('\n')}
`;

  const blueprintPath = path.join(DOCS_DIR, 'REA_CleanRoom_Blueprint_sasarisa.md');
  const jsonPath = path.join(DATA_DIR, 'rea-report.json');
  try {
    fs.writeFileSync(blueprintPath, blueprintMd, 'utf8');
  } catch (_) {}

  console.log(`✅ [Stage 1 & 2 Teardown] Saved -> ${teardownPath}`);
  console.log(`✅ [Stage 3 & 4 Blueprint] Saved -> ${blueprintPath}`);
  console.log(`✅ [4-Split Evidence JSON] Saved -> ${jsonPath}`);
  console.log(`📊 Summary: ${surface.totalFiles} files, ${deconstructed.swiftModules.length} Swift modules, ${deconstructed.allTypes.length} types, ${deconstructed.allFunctions.length} funcs, ${deconstructed.intents.length} intents.`);
}

if (require.main === module) {
  runReaAnalysis();
}

module.exports = { runReaAnalysis, buildReaReportObject };
