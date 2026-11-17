'use strict';

/**
 * @file C:\자비스\scripts\jarvis-ide-cli-mcp-tools.js
 * @description JARVIS v3.0 IDE File Operations, Live CLI Executor, Outbound MCP Client,
 *   and 100% Free ($0 GPL) Local Blender 5.1 `bpy` 3D Modeling & ArchViz Engine.
 */

const fs = require('fs');
const path = require('path');
const net = require('net');
const { spawn } = require('child_process');
const { sharedTerminalManager, isDangerousCommand } = require('./bsc-collaboration-bridge');

const ALLOWED_ROOTS = [
  'C:\\자비스',
  'C:\\BeausCreators',
  'C:\\BeautifulWorld',
  'C:\\Warehouse',
  path.join(process.env.USERPROFILE || 'C:\\Users\\USER', 'Desktop'),
  path.join(process.env.USERPROFILE || 'C:\\Users\\USER', 'Documents')
];

function assertAllowedPath(targetPath) {
  const resolved = path.resolve(String(targetPath || ''));
  const ok = ALLOWED_ROOTS.some(r => resolved.toLowerCase().startsWith(r.toLowerCase()));
  if (!ok) {
    throw new Error(`허용되지 않은 경로입니다: ${resolved}`);
  }
  return resolved;
}

function ideReadFile({ filePath, startLine = 1, endLine = 200 }) {
  const resolved = assertAllowedPath(filePath);
  if (!fs.existsSync(resolved)) throw new Error(`파일이 존재하지 않습니다: ${resolved}`);
  const lines = fs.readFileSync(resolved, 'utf8').split(/\r?\n/);
  const s = Math.max(1, Number(startLine) || 1);
  const e = Math.min(lines.length, Number(endLine) || s + 199);
  return {
    ok: true,
    path: resolved,
    totalLines: lines.length,
    content: lines.slice(s - 1, e).join('\n')
  };
}

function ideWriteFile({ filePath, content = '', append = false }) {
  const resolved = assertAllowedPath(filePath);
  fs.mkdirSync(path.dirname(resolved), { recursive: true });
  if (append) {
    fs.appendFileSync(resolved, String(content), 'utf8');
  } else {
    fs.writeFileSync(resolved, String(content), 'utf8');
  }
  return {
    ok: true,
    path: resolved,
    bytes: Buffer.byteLength(String(content), 'utf8'),
    mode: append ? 'append' : 'write'
  };
}

function ideEditFile({ filePath, target, replacement }) {
  const resolved = assertAllowedPath(filePath);
  if (!fs.existsSync(resolved)) throw new Error(`수정할 파일이 존재하지 않습니다: ${resolved}`);
  const raw = fs.readFileSync(resolved, 'utf8');
  if (!raw.includes(target)) {
    throw new Error('파일 내에서 수정할 대상 문자열(target)을 찾지 못했습니다.');
  }
  const updated = raw.split(target).join(replacement);
  fs.writeFileSync(resolved, updated, 'utf8');
  return { ok: true, path: resolved, bytes: Buffer.byteLength(updated, 'utf8') };
}

function ideListDir({ dirPath = 'C:\\자비스' }) {
  const resolved = assertAllowedPath(dirPath);
  if (!fs.existsSync(resolved)) throw new Error(`폴더가 존재하지 않습니다: ${resolved}`);
  const entries = fs.readdirSync(resolved, { withFileTypes: true }).slice(0, 80).map(d => ({
    name: d.name,
    type: d.isDirectory() ? 'dir' : 'file'
  }));
  return { ok: true, path: resolved, count: entries.length, entries };
}

/**
 * Execute a CLI command synchronously for the ReAct loop while also streaming to HUD Terminal Drawer
 */
function cliExecute({ command, cwd = 'C:\\자비스', timeoutMs = 15000 }) {
  const cmd = String(command || '').trim();
  if (!cmd) throw new Error('실행할 CLI 명령어가 비어 있습니다.');
  if (isDangerousCommand(cmd)) {
    return Promise.resolve({
      ok: false,
      requiresApproval: true,
      error: '파일 삭제 또는 초기화 명령은 대표님 사전 승인 후에만 실행할 수 있습니다.',
      command: cmd
    });
  }
  const safeCwd = fs.existsSync(cwd) ? cwd : 'C:\\자비스';
  // Mirror to HUD Terminal Drawer
  try {
    sharedTerminalManager.runTask({
      title: `JARVIS Agent CLI: ${cmd.slice(0, 42)}`,
      command: cmd,
      cwd: safeCwd,
      chlgdPhase: 'Agentic CLI'
    });
  } catch (_) {}

  return new Promise(resolve => {
    const psExe = path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe');
    const wrapped = `[Console]::OutputEncoding=[System.Text.Encoding]::UTF8; $OutputEncoding=[System.Text.Encoding]::UTF8; $env:PYTHONUTF8='1'; Set-Location -LiteralPath '${safeCwd.replace(/'/g, "''")}'; ${cmd}`;
    const child = spawn(psExe, ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', wrapped], {
      cwd: safeCwd,
      windowsHide: true,
      env: { ...process.env, PYTHONUTF8: '1', PYTHONIOENCODING: 'utf-8' }
    });
    let stdout = '';
    let stderr = '';
    const timer = setTimeout(() => {
      try { child.kill(); } catch (_) {}
      resolve({ ok: false, timedOut: true, stdout: stdout.slice(-3000), stderr: stderr.slice(-1500) });
    }, timeoutMs);
    child.stdout.on('data', d => { stdout = (stdout + d.toString('utf8')).slice(-6000); });
    child.stderr.on('data', d => { stderr = (stderr + d.toString('utf8')).slice(-3000); });
    child.on('close', code => {
      clearTimeout(timer);
      resolve({ ok: code === 0, exitCode: code, stdout: stdout.trim(), stderr: stderr.trim() });
    });
    child.on('error', err => {
      clearTimeout(timer);
      resolve({ ok: false, error: err.message });
    });
  });
}

/**
 * Universal Outbound MCP Client (Stdio JSON-RPC 2.0 via ~/.gemini/config/mcp_config.json)
 */
async function mcpCallExternal({ serverName, toolName, toolArgs = {} }) {
  const cfgPath = path.join(process.env.USERPROFILE || 'C:\\Users\\USER', '.gemini', 'config', 'mcp_config.json');
  if (!fs.existsSync(cfgPath)) throw new Error('mcp_config.json 파일을 찾을 수 없습니다.');
  const mcpCfg = JSON.parse(fs.readFileSync(cfgPath, 'utf8'));
  const servers = mcpCfg.mcpServers || {};
  if (!serverName || serverName === 'list') {
    return { ok: true, availableServers: Object.keys(servers) };
  }
  const entry = servers[serverName];
  if (!entry || !entry.command) {
    // Auto-route ArtCraft 14 MCP-capable apps even if not individually listed in mcp_config.json
    const acApp = resolveArtCraftApp(serverName);
    if (acApp && acApp.cliExe && fs.existsSync(acApp.cliExe)) {
      return artcraftExecute({
        appName: acApp.id,
        mode: 'mcp',
        mcpTool: toolName || 'tools/list',
        mcpArgs: toolArgs || {}
      });
    }
    return { ok: false, error: `등록되지 않았거나 원격 전용인 MCP 서버입니다: ${serverName}`, availableServers: [...Object.keys(servers), ...Object.keys(ARTCRAFT_APPS).filter(k => ARTCRAFT_APPS[k].mcp)] };
  }

  return new Promise(resolve => {
    const child = spawn(entry.command, entry.args || [], {
      windowsHide: true,
      shell: entry.command === 'npx',
      env: { ...process.env, ...(entry.env || {}) },
      stdio: ['pipe', 'pipe', 'pipe']
    });
    let buf = '';
    let settled = false;
    const finish = val => {
      if (settled) return;
      settled = true;
      clearTimeout(timer);
      try { child.kill(); } catch (_) {}
      resolve(val);
    };
    const timer = setTimeout(() => finish({ ok: false, error: `MCP 서버(${serverName}) 응답 시간 초과` }), 12000);

    child.stdout.on('data', chunk => {
      buf += chunk.toString('utf8');
      const lines = buf.split(/\r?\n/);
      buf = lines.pop() || '';
      for (const line of lines) {
        if (!line.trim()) continue;
        try {
          const msg = JSON.parse(line);
          if (msg.id === 1) {
            child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
            if (!toolName || toolName === 'tools/list') {
              child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} }) + '\n');
            } else {
              child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 2, method: 'tools/call', params: { name: toolName, arguments: toolArgs } }) + '\n');
            }
          } else if (msg.id === 2) {
            finish({ ok: !msg.error, serverName, toolName: toolName || 'tools/list', result: msg.result || msg.error });
          }
        } catch (_) {}
      }
    });
    child.on('error', err => finish({ ok: false, error: err.message }));
    child.stdin.write(JSON.stringify({
      jsonrpc: '2.0',
      id: 1,
      method: 'initialize',
      params: { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'jarvis-v3', version: '3.0.0' } }
    }) + '\n');
  });
}

/**
 * 100% Free ($0 GPL Open-Source) Local Blender 3D Modeling & ArchViz Engine
 *   - Connects to live Blender MCP TCP socket (127.0.0.1:9876) if Blender GUI addon is active
 *   - Otherwise executes local `blender.exe --background --python` or opens Blender GUI with generated `.blend`!
 */
function findInstalledBlenderExe() {
  const candidates = [
    'C:\\Program Files\\Blender Foundation\\Blender 5.1\\blender.exe',
    'C:\\Program Files\\Blender Foundation\\Blender 5.0\\blender.exe',
    'C:\\Program Files\\Blender Foundation\\Blender 4.5\\blender.exe',
    'C:\\Program Files\\Blender Foundation\\Blender 4.2\\blender.exe'
  ];
  return candidates.find(p => fs.existsSync(p)) || null;
}

function tryBlenderLiveSocket(pythonCode, timeoutMs = 4500) {
  return new Promise(resolve => {
    const socket = new net.Socket();
    let data = '';
    let done = false;
    const finish = val => {
      if (done) return;
      done = true;
      try { socket.destroy(); } catch (_) {}
      resolve(val);
    };
    socket.setTimeout(timeoutMs);
    socket.connect(9876, '127.0.0.1', () => {
      socket.write(JSON.stringify({ type: 'execute_code', params: { code: pythonCode } }));
    });
    socket.on('data', chunk => {
      data += chunk.toString('utf8');
      try {
        const parsed = JSON.parse(data);
        finish({ ok: parsed.status !== 'error', liveViewport: true, response: parsed });
      } catch (_) {}
    });
    socket.on('timeout', () => finish(null));
    socket.on('error', () => finish(null));
  });
}

async function blenderExecuteBpy({ pythonCode, sceneName = 'jarvis_3d_scene', openGui = true }) {
  const code = String(pythonCode || '').trim();
  if (!code) throw new Error('실행할 Blender Python(bpy) 스크립트가 비어 있습니다.');

  // 1. Try live Blender MCP viewport socket (127.0.0.1:9876) first — $0 instant viewport update!
  const liveRes = await tryBlenderLiveSocket(code);
  if (liveRes && liveRes.ok) {
    return {
      ok: true,
      mode: 'live-blender-mcp-socket',
      cost: '$0 (100% 로컬 오픈소스 무료)',
      detail: liveRes.response
    };
  }

  // 2. Otherwise run local Blender 5.1/5.0/4.5 executable ($0 local GPL)
  const blenderExe = findInstalledBlenderExe();
  if (!blenderExe) {
    return { ok: false, error: '설치된 Blender 실행 파일(blender.exe)을 찾지 못했습니다.' };
  }

  const outDir = path.join('C:\\자비스\\data', 'blender_projects');
  fs.mkdirSync(outDir, { recursive: true });
  const safeName = String(sceneName || 'scene').replace(/[^a-zA-Z0-9가-힣_-]/g, '_');
  const scriptPath = path.join(outDir, `${safeName}.py`);
  const blendPath = path.join(outDir, `${safeName}.blend`);

  const wrappedPy = [
    'import bpy, math',
    code,
    `bpy.ops.wm.save_as_mainfile(filepath=r"${blendPath}")`,
    `print("JARVIS_BLENDER_SAVED:", r"${blendPath}")`
  ].join('\n');
  fs.writeFileSync(scriptPath, wrappedPy, 'utf8');

  return new Promise(resolve => {
    const child = spawn(blenderExe, ['--background', '--python', scriptPath], { windowsHide: true });
    let stdout = '';
    let stderr = '';
    const timer = setTimeout(() => {
      try { child.kill(); } catch (_) {}
      resolve({ ok: false, error: 'Blender 스크립트 실행 시간 초과' });
    }, 25000);
    child.stdout.on('data', d => { stdout += d.toString('utf8'); });
    child.stderr.on('data', d => { stderr += d.toString('utf8'); });
    child.on('close', codeExit => {
      clearTimeout(timer);
      const saved = fs.existsSync(blendPath);
      if (saved && openGui) {
        const launcher = path.join('C:\\자비스\\bin\\JarvisDesktopLaunch.exe');
        if (fs.existsSync(launcher)) {
          spawn(launcher, [blenderExe, blendPath], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
        } else {
          spawn(blenderExe, [blendPath], { detached: true, stdio: 'ignore' }).unref();
        }
      }
      resolve({
        ok: saved && codeExit === 0,
        mode: 'local-blender-cli',
        blenderExe,
        blendPath,
        scriptPath,
        cost: '$0 (100% 로컬 오픈소스 무료)',
        stdout: stdout.slice(-1500)
      });
    });
  });
}

async function notebooklmAction({ action = 'list', notebookId = '', query = '', title = '', url = '' } = {}) {
  const act = String(action || 'list').toLowerCase();
  if (act === 'list') {
    return mcpCallExternal({ serverName: 'notebooklm', toolName: 'notebook_list', toolArgs: {} });
  }
  if (act === 'query' && notebookId && query) {
    return mcpCallExternal({ serverName: 'notebooklm', toolName: 'notebook_query', toolArgs: { notebook_id: notebookId, query } });
  }
  if (act === 'create' && title) {
    return mcpCallExternal({ serverName: 'notebooklm', toolName: 'notebook_create', toolArgs: { title } });
  }
  if (act === 'add_source' && notebookId && url) {
    return mcpCallExternal({ serverName: 'notebooklm', toolName: 'source_add', toolArgs: { notebook_id: notebookId, url } });
  }
  return mcpCallExternal({ serverName: 'notebooklm', toolName: 'notebook_list', toolArgs: {} });
}

const ARTCRAFT_SUITE_ROOT = 'C:\\BeausCreators\\05.BSC_Skill_Warehouse\\1.Imported_Skills\\2026-10-19_TechPack\\artcraft-suite';
const ARTCRAFT_LAUNCHER_DIR = path.join(ARTCRAFT_SUITE_ROOT, 'artcraft-launcher-portable', 'artcraft-launcher-0.5.1-windows-x64-portable');
const ARTCRAFT_DATA_APPS_DIR = path.join(ARTCRAFT_LAUNCHER_DIR, 'ArtCraftLauncherData', 'data', 'apps');

const ARTCRAFT_APPS = {
  launcher: {
    id: 'launcher',
    label: 'ArtCraft 통합 런처 (13종 전체관리)',
    role: '통합 앱 관리 및 AI 모델 바인딩',
    aliases: ['아트크래프트', '아트크래프트런처', 'artcraft', 'artcraft-launcher', 'launcher'],
    guiExe: path.join(ARTCRAFT_LAUNCHER_DIR, 'artcraft-launcher.exe'),
    cliExe: null,
    mcp: false
  },
  photocraft: {
    id: 'photocraft',
    label: 'PhotoCraft (포토샵 대체 · 사진/PSD 편집)',
    role: 'PSD 레이어 편집, 포맷 변환, 배치 액션',
    aliases: ['포토크래프트', 'photocraft', '포토샵크래프트'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'photocraft', 'photocraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'photocraft', 'photocraft-cli.exe'),
    mcp: true
  },
  vectorcraft: {
    id: 'vectorcraft',
    label: 'VectorCraft (일러스트 대체 · SVG/AI 벡터)',
    role: 'SVG/AI/EPS/DXF 벡터 그래픽 및 로고 제어',
    aliases: ['벡터크래프트', 'vectorcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'vectorcraft', 'vectorcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'vectorcraft', 'vectorcraft-cli.exe'),
    mcp: true
  },
  filmcraft: {
    id: 'filmcraft',
    label: 'FilmCraft (프리미어 대체 · 비선형 영상 편집)',
    role: '영상 컷 편집, 타임라인, LUT 컬러 그레이딩',
    aliases: ['필름크래프트', 'filmcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'filmcraft', 'filmcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'filmcraft', 'filmcraft-cli.exe'),
    mcp: true
  },
  effectcraft: {
    id: 'effectcraft',
    label: 'EffectCraft (애프터이펙트 대체 · 모션/VFX)',
    role: '모션 그래픽, 키프레임 합성, VFX 시각효과',
    aliases: ['이펙트크래프트', 'effectcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'effectcraft', 'effectcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'effectcraft', 'effectcraft-cli.exe'),
    mcp: true
  },
  designcraft: {
    id: 'designcraft',
    label: 'DesignCraft (인디자인 대체 · 출판/조판)',
    role: '출판 페이지 레이아웃, INDD/IDML/PDF 조판',
    aliases: ['디자인크래프트', 'designcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'designcraft', 'designcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'designcraft', 'designcraft-cli.exe'),
    mcp: true
  },
  figcraft: {
    id: 'figcraft',
    label: 'FigCraft (피그마 대체 · UI/UX 프로토타이핑)',
    role: 'UI/UX 화면 설계, 오토레이아웃, 컴포넌트',
    aliases: ['피그크래프트', 'figcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'figcraft', 'figcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'figcraft', 'figcraft-cli.exe'),
    mcp: true
  },
  lightcraft: {
    id: 'lightcraft',
    label: 'LightCraft (라이트룸 대체 · RAW 사진 현상)',
    role: 'RAW 사진 색보정, 카탈로그 현상, 프리셋',
    aliases: ['라이트크래프트', 'lightcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'lightcraft', 'lightcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'lightcraft', 'lightcraft-cli.exe'),
    mcp: true
  },
  pdfcraft: {
    id: 'pdfcraft',
    label: 'PdfCraft (아크로뱃 대체 · PDF 편집/보안)',
    role: 'PDF 병합/분할, 텍스트 편집, 전자서명/보안',
    aliases: ['피디에프크래프트', 'pdfcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'pdfcraft', 'pdfcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'pdfcraft', 'pdfcraft-cli.exe'),
    mcp: true
  },
  wordcraft: {
    id: 'wordcraft',
    label: 'WordCraft (MS 워드 대체 · DOCX 문서/변경추적)',
    role: 'DOCX/ODT/PDF 문서 작성, 스타일, Track Changes',
    aliases: ['워드크래프트', 'wordcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'wordcraft', 'wordcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'wordcraft', 'wordcraft-cli.exe'),
    mcp: true
  },
  deckcraft: {
    id: 'deckcraft',
    label: 'DeckCraft (파워포인트 대체 · 프레젠테이션)',
    role: '슬라이드 덱 생성, 발표자료 디자인, PDF 변환',
    aliases: ['덱크래프트', '데크크래프트', 'deckcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'deckcraft', 'deckcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'deckcraft', 'deckcraft-cli.exe'),
    mcp: true
  },
  gridcraft: {
    id: 'gridcraft',
    label: 'GridCraft (엑셀 대체 · 스프레드시트/데이터)',
    role: 'XLSX/CSV 수식 계산, 피벗, 데이터 시트 자동화',
    aliases: ['그리드크래프트', 'gridcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'gridcraft', 'gridcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'gridcraft', 'gridcraft-cli.exe'),
    mcp: true
  },
  soundcraft: {
    id: 'soundcraft',
    label: 'SoundCraft (오디션 대체 · 오디오 녹음/편집)',
    role: 'WAV/MP3 파형 편집, 노이즈 제거, 마스터링',
    aliases: ['사운드크래프트', 'soundcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'soundcraft', 'soundcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'soundcraft', 'soundcraft-cli.exe'),
    mcp: true
  },
  cadcraft: {
    id: 'cadcraft',
    label: 'CADCraft (오토캐드 대체 · 2D/3D CAD 도면)',
    role: 'DXF/DWG 건축·인테리어·기계 2D/3D 도면 설계',
    aliases: ['캐드크래프트', 'cadcraft'],
    guiExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'cadcraft', 'cadcraft.exe'),
    cliExe: path.join(ARTCRAFT_DATA_APPS_DIR, 'cadcraft', 'cadcraft-cli.exe'),
    mcp: true
  },
  subcraft: {
    id: 'subcraft',
    label: 'SubCraft 3D Painter (서브스턴스 3D 페인터 대체 · PBR 텍스처/베이킹)',
    role: '3D 메쉬 PBR 텍스처 페인팅, 9종 맵 베이킹, Unity/UE 익스포트',
    aliases: ['서브크래프트', '서브크래프트3d', 'subcraft', 'subcraft3d'],
    guiExe: path.join(ARTCRAFT_SUITE_ROOT, 'SubCraft-0.2.0-windows-x64', 'SubCraft-0.2.0-windows-x64', 'SubCraft.exe'),
    cliExe: null,
    mcp: false
  },
  'craft-anything': {
    id: 'craft-anything',
    label: 'Craft Anything v0.6.0 (역공학 & 클린룸 재구축 워크벤치)',
    role: '320개 정적/동적 분석 MCP 도구 및 클린룸 UI/앱 재구축',
    aliases: ['크래프트애니씽', '크래프트애니띵', 'craft-anything', 'craftanything'],
    guiExe: path.join(ARTCRAFT_SUITE_ROOT, 'craft-anything-0.6.0-windows-x64-portable', 'craft-anything.exe'),
    cliExe: path.join(ARTCRAFT_SUITE_ROOT, 'craft-anything-0.6.0-windows-x64-portable', 'craft-anything-cli.exe'),
    mcp: true
  }
};

function resolveArtCraftApp(rawName = '') {
  const norm = String(rawName || '').toLowerCase().replace(/[\s_-]+/g, '');
  if (!norm) return null;
  for (const app of Object.values(ARTCRAFT_APPS)) {
    if (app.id.replace(/-/g, '') === norm) return app;
    if (app.aliases.some(a => a.toLowerCase().replace(/[\s_-]+/g, '') === norm)) return app;
  }
  for (const app of Object.values(ARTCRAFT_APPS)) {
    if (app.aliases.some(a => norm.includes(a.toLowerCase().replace(/[\s_-]+/g, '')))) return app;
  }
  return null;
}

function tokenizeCliArgs(cliArgs) {
  if (Array.isArray(cliArgs)) return cliArgs.map(String);
  const str = String(cliArgs || '').trim();
  if (!str) return ['--help'];
  const tokens = [];
  const re = /"([^"]*)"|'([^']*)'|(\S+)/g;
  let m;
  while ((m = re.exec(str)) !== null) {
    tokens.push(m[1] !== undefined ? m[1] : m[2] !== undefined ? m[2] : m[3]);
  }
  return tokens;
}

/**
 * Unified ArtCraft 16-App Executor (List / GUI Launch / Headless CLI / Stdio MCP)
 */
async function artcraftExecute({
  appName = 'list',
  mode = 'list',
  cliArgs = [],
  mcpTool = 'tools/list',
  mcpArgs = {},
  openFile = '',
  controlPort = 0
} = {}) {
  const actMode = String(mode || 'list').toLowerCase();
  if (actMode === 'list' || String(appName).toLowerCase() === 'list') {
    const inventory = Object.values(ARTCRAFT_APPS).map(a => ({
      id: a.id,
      label: a.label,
      role: a.role,
      guiReady: Boolean(a.guiExe && fs.existsSync(a.guiExe)),
      cliReady: Boolean(a.cliExe && fs.existsSync(a.cliExe)),
      mcpReady: Boolean(a.mcp && a.cliExe && fs.existsSync(a.cliExe))
    }));
    return {
      ok: true,
      totalApps: inventory.length,
      guiInstalledCount: inventory.filter(x => x.guiReady).length,
      cliAndMcpCount: inventory.filter(x => x.mcpReady).length,
      apps: inventory
    };
  }

  const app = resolveArtCraftApp(appName);
  if (!app) {
    return {
      ok: false,
      error: `알 수 없는 ArtCraft 앱입니다: ${appName}`,
      availableApps: Object.keys(ARTCRAFT_APPS)
    };
  }

  if (actMode === 'launch' || actMode === 'gui' || actMode === 'open') {
    if (!app.guiExe || !fs.existsSync(app.guiExe)) {
      return { ok: false, error: `${app.label} GUI 실행 파일을 찾을 수 없습니다: ${app.guiExe}` };
    }
    const launchArgs = [];
    if (Number(controlPort) > 0) launchArgs.push('--control', String(Number(controlPort)));
    if (openFile && String(openFile).trim()) launchArgs.push(String(openFile).trim());

    const desktopLauncher = 'C:\\자비스\\bin\\JarvisDesktopLaunch.exe';
    if (fs.existsSync(desktopLauncher)) {
      spawn(desktopLauncher, [app.guiExe, ...launchArgs], { detached: true, stdio: 'ignore', windowsHide: true }).unref();
    } else {
      spawn(app.guiExe, launchArgs, { detached: true, stdio: 'ignore' }).unref();
    }
    return {
      ok: true,
      mode: 'launch',
      app: app.id,
      label: app.label,
      exe: app.guiExe,
      args: launchArgs
    };
  }

  if (actMode === 'cli') {
    if (!app.cliExe || !fs.existsSync(app.cliExe)) {
      return {
        ok: false,
        error: `${app.label}는 전용 Headless CLI 바이너리가 없으며 GUI 모드(mode="launch")로 실행할 수 있습니다.`,
        guiExe: app.guiExe
      };
    }
    const argv = tokenizeCliArgs(cliArgs);
    return new Promise(resolve => {
      const child = spawn(app.cliExe, argv, { windowsHide: true });
      let stdout = '';
      let stderr = '';
      const timer = setTimeout(() => {
        try { child.kill(); } catch (_) {}
        resolve({ ok: false, timedOut: true, app: app.id, stdout: stdout.slice(-4000), stderr: stderr.slice(-2000) });
      }, 20000);
      child.stdout.on('data', d => { stdout = (stdout + d.toString('utf8')).slice(-8000); });
      child.stderr.on('data', d => { stderr = (stderr + d.toString('utf8')).slice(-4000); });
      child.on('close', exitCode => {
        clearTimeout(timer);
        resolve({
          ok: exitCode === 0,
          mode: 'cli',
          app: app.id,
          cliExe: app.cliExe,
          args: argv,
          exitCode,
          stdout: stdout.trim(),
          stderr: stderr.trim()
        });
      });
      child.on('error', err => {
        clearTimeout(timer);
        resolve({ ok: false, app: app.id, error: err.message });
      });
    });
  }

  if (actMode === 'mcp') {
    if (!app.mcp || !app.cliExe || !fs.existsSync(app.cliExe)) {
      return {
        ok: false,
        error: `${app.label}는 Stdio MCP 모드를 지원하지 않습니다 (지원 앱: PhotoCraft~CADCraft 13종 및 Craft-Anything).`
      };
    }
    const mcpSpawnArgs = ['mcp'];
    return new Promise(resolve => {
      const child = spawn(app.cliExe, mcpSpawnArgs, {
        windowsHide: true,
        stdio: ['pipe', 'pipe', 'pipe']
      });
      let buf = '';
      let settled = false;
      const finish = val => {
        if (settled) return;
        settled = true;
        clearTimeout(timer);
        try { child.kill(); } catch (_) {}
        resolve(val);
      };
      const timer = setTimeout(() => finish({ ok: false, app: app.id, error: `ArtCraft MCP(${app.id}) 응답 시간 초과` }), 12000);

      child.stdout.on('data', chunk => {
        buf += chunk.toString('utf8');
        const lines = buf.split(/\r?\n/);
        buf = lines.pop() || '';
        for (const line of lines) {
          if (!line.trim()) continue;
          try {
            const msg = JSON.parse(line);
            if (msg.id === 1) {
              child.stdin.write(JSON.stringify({ jsonrpc: '2.0', method: 'notifications/initialized' }) + '\n');
              if (!mcpTool || mcpTool === 'tools/list' || mcpTool === 'list') {
                child.stdin.write(JSON.stringify({ jsonrpc: '2.0', id: 2, method: 'tools/list', params: {} }) + '\n');
              } else {
                child.stdin.write(JSON.stringify({
                  jsonrpc: '2.0',
                  id: 2,
                  method: 'tools/call',
                  params: { name: mcpTool, arguments: mcpArgs || {} }
                }) + '\n');
              }
            } else if (msg.id === 2) {
              finish({
                ok: !msg.error,
                mode: 'mcp',
                app: app.id,
                mcpTool: mcpTool || 'tools/list',
                result: msg.result || msg.error
              });
            }
          } catch (_) {}
        }
      });
      child.on('error', err => finish({ ok: false, app: app.id, error: err.message }));
      child.stdin.write(JSON.stringify({
        jsonrpc: '2.0',
        id: 1,
        method: 'initialize',
        params: { protocolVersion: '2024-11-05', capabilities: {}, clientInfo: { name: 'jarvis-bsc-artcraft-bridge', version: '3.0.0' } }
      }) + '\n');
    });
  }

  return { ok: false, error: `지원하지 않는 ArtCraft 실행 모드입니다: ${mode} (list | launch | cli | mcp 사용)` };
}

module.exports = {
  ideReadFile,
  ideWriteFile,
  ideEditFile,
  ideListDir,
  cliExecute,
  mcpCallExternal,
  blenderExecuteBpy,
  findInstalledBlenderExe,
  notebooklmAction,
  ARTCRAFT_APPS,
  resolveArtCraftApp,
  artcraftExecute
};
