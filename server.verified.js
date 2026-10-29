/**
 * @file C:\자비스\server.js
 * @description 자비스 (SasariSa v0.7.3 1:1 Windows 11 Clean-Room Host Server)
 *   - Serves the 1:1 HUD, BootScene, SetupWindow, SettingsWindow, Tutorial & REA Inspector
 *   - 3-Tier Korean Voice Output (Mouth.swift 1:1):
 *       1) msedge-tts 96kbps Neural (ko-KR-SunHiNeural / ko-KR-InJoonNeural)
 *       2) Windows 11 Native System.Speech.Synthesis WAV generator (100% offline fallback)
 *       3) Client Web Speech Synthesis fallback
 *   - 2-Tier Korean Voice Input (Ear.swift 1:1):
 *       1) Browser Continuous Web Speech API + WebAudio AnalyserNode real-time mic level
 *       2) Windows 11 Native System.Speech.Recognition hardware microphone bridge (/api/ear/listen-once)
 */

const http = require('http');
const https = require('https');
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const { ButlerEngine, FILES } = require('./scripts/butler-engine.verified');
const { sharedTerminalManager, sharedBscBridge } = require('./scripts/bsc-collaboration-bridge');
const { VoiceSessions } = require('./scripts/voice-session');
const { registerWindowsStartup, getStartupStatus } = require('./scripts/windows-ear-daemon');
const { runReaAnalysis, buildReaReportObject } = require('./scripts/rea-analyzer');
const {
  getChlgdAndPlanDashboard,
  addConversationRule,
  createOrUpdatePlanDocument,
  triggerHudPlanWindowOpen,
  runJarvisHarness2EvolutionCycle
} = require('./scripts/jarvis-harness2-engine');
const { handleMcpJsonRpcRequest, MCP_SERVER_INFO, MCP_TOOLS } = require('./scripts/jarvis-mcp-server');

let MsEdgeTTS = null;
let OUTPUT_FORMAT = null;
try {
  if (process.env.JARVIS_DISABLE_NEURAL_TTS === '1') throw new Error('disabled');
  ({ MsEdgeTTS, OUTPUT_FORMAT } = require('C:\\BSC_Javis_OS\\node_modules\\msedge-tts'));
} catch (_) {
  try {
    if (process.env.JARVIS_DISABLE_NEURAL_TTS === '1') throw new Error('disabled');
    ({ MsEdgeTTS, OUTPUT_FORMAT } = require('msedge-tts'));
  } catch (__) {
    MsEdgeTTS = null;
  }
}

let edgeClockSynced = false;
async function ensureEdgeTtsClockSync() {
  if (!MsEdgeTTS) return;
  if (edgeClockSynced && MsEdgeTTS._clockOffsetMs !== 0) return;
  for (const probeUrl of ['https://www.microsoft.com', 'https://www.google.com']) {
    try {
      const ctrl = new AbortController();
      const timer = setTimeout(() => ctrl.abort(), 2500);
      const res = await fetch(probeUrl, { method: 'HEAD', signal: ctrl.signal }).catch(() => null);
      clearTimeout(timer);
      const dateHdr = res?.headers?.get('date');
      if (dateHdr) {
        const srvMs = Date.parse(dateHdr);
        if (Number.isFinite(srvMs)) {
          MsEdgeTTS._clockOffsetMs = srvMs - Date.now();
          edgeClockSynced = true;
          return;
        }
      }
    } catch (_) {}
  }
}

function humanizeKoreanSpeechText(rawText) {
  return String(rawText || '')
    .replace(/```[\s\S]*?```/g, ' ')
    .replace(/`([^`]+)`/g, '$1')
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
    .replace(/https?:\/\/[^\s)]+/g, ' ')
    .replace(/[\u{1F300}-\u{1FAFF}\u{2600}-\u{27BF}]/gu, '')
    .replace(/(\d),(\d{3})/g, '$1$2')
    .replace(/\bJ\.?A\.?R\.?V\.?I\.?S\.?\b/gi, '자비스')
    .replace(/\bAPI\b/gi, '에이피아이')
    .replace(/\bTTS\b/gi, '티티에스')
    .replace(/\bSTT\b/gi, '에스티티')
    .replace(/\bLLM\b/gi, '엘엘엠')
    .replace(/[→➔➜]/g, ', ')
    .replace(/[·•/]/g, ', ')
    .replace(/[*#_~>|『』「」《》〈〉【】\[\]()]/g, ' ')
    .replace(/\s+/g, ' ')
    .trim()
    .slice(0, 1500);
}

function stripLeadingId3(buf) {
  if (!buf || buf.length < 10) return buf;
  if (buf[0] !== 0x49 || buf[1] !== 0x44 || buf[2] !== 0x33) return buf;
  const size = ((buf[6] & 0x7f) << 21) | ((buf[7] & 0x7f) << 14) | ((buf[8] & 0x7f) << 7) | (buf[9] & 0x7f);
  const start = 10 + size;
  return start < buf.length ? buf.subarray(start) : buf;
}

function splitTextIntoSentenceChunks(text, maxChunkLen = 165) {
  const sentences = text
    .split(/(?<=[.?!。]|다\.|요\.|죠\.|까\?|네\.|니다\.)\s+/)
    .map(s => s.trim())
    .filter(Boolean);
  const chunks = [];
  let current = '';
  for (const s of sentences) {
    if (s.length > maxChunkLen) {
      if (current) { chunks.push(current); current = ''; }
      for (let i = 0; i < s.length; i += maxChunkLen) {
        chunks.push(s.slice(i, i + maxChunkLen));
      }
    } else if ((current ? `${current} ${s}` : s).length <= maxChunkLen) {
      current = current ? `${current} ${s}` : s;
    } else {
      if (current) chunks.push(current);
      current = s;
    }
  }
  if (current) chunks.push(current);
  return chunks.length > 0 ? chunks : [text];
}

function fetchGoogleNeuralTtsChunkMp3(clauseText = '', speed = 1.2) {
  return new Promise(resolve => {
    const q = encodeURIComponent(String(clauseText || '').trim().slice(0, 180));
    if (!q) return resolve(Buffer.alloc(0));
    const url = `https://translate.google.com/translate_tts?ie=UTF-8&tl=ko&client=tw-ob&ttsspeed=${Number(speed) || 1.2}&q=${q}`;
    const req = https.get(
      url,
      {
        headers: {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36',
          Referer: 'https://translate.google.com/'
        },
        timeout: 6000
      },
      res => {
        if (res.statusCode !== 200) {
          res.resume();
          return resolve(Buffer.alloc(0));
        }
        const chunks = [];
        res.on('data', d => chunks.push(d));
        res.on('end', () => resolve(Buffer.concat(chunks)));
        res.on('error', () => resolve(Buffer.alloc(0)));
      }
    );
    req.on('timeout', () => {
      try { req.destroy(); } catch (_) {}
      resolve(Buffer.alloc(0));
    });
    req.on('error', () => resolve(Buffer.alloc(0)));
  });
}

async function synthesizeGoogleNeuralKoreanMp3(cleanText = '', speed = 1.2) {
  try {
    const clauses = splitTextIntoSentenceChunks(String(cleanText || '네, 이건우 대표님.'), 165);
    const mp3Parts = [];
    for (const clause of clauses) {
      if (!clause.trim()) continue;
      const buf = await fetchGoogleNeuralTtsChunkMp3(clause, speed);
      if (buf.length > 256) {
        mp3Parts.push(mp3Parts.length === 0 ? buf : stripLeadingId3(buf));
      }
    }
    const merged = Buffer.concat(mp3Parts);
    if (merged.length > 256) return merged;
  } catch (_) {}
  return Buffer.alloc(0);
}

async function synthesizeStudioNeuralMp3Buffer(rawText, voice = 'ko-KR-HyunsuMultilingualNeural') {
  const clean = humanizeKoreanSpeechText(rawText);
  if (!clean) return Buffer.alloc(0);
  const cfg = butler ? butler.getConfig() : {};
  const userSpeed = Number(cfg.ttsSpeed) || 1.25;

  // Tier 1: Fast Google Neural Korean MP3 (0ms handshake delay, 100% natural human voice)
  const googleMp3 = await synthesizeGoogleNeuralKoreanMp3(clean, Math.min(1.5, Math.max(0.9, userSpeed)));
  if (googleMp3 && googleMp3.length > 256) {
    return googleMp3;
  }

  // Tier 2: Microsoft Edge Studio Neural MP3
  if (MsEdgeTTS) {
    await ensureEdgeTtsClockSync();
    const ratePct = `${userSpeed >= 1 ? '+' : ''}${Math.round((userSpeed - 1) * 100)}%`;
    const candidates = [voice, 'ko-KR-HyunsuMultilingualNeural', 'ko-KR-SunHiNeural', 'ko-KR-InJoonNeural'];
    for (const v of candidates) {
      if (!v) continue;
      const tts = new MsEdgeTTS();
      try {
        const fmt = OUTPUT_FORMAT.AUDIO_24KHZ_96KBITRATE_MONO_MP3 || OUTPUT_FORMAT.AUDIO_24KHZ_48KBITRATE_MONO_MP3;
        await tts.setMetadata(v, fmt);
        const { audioStream } = tts.toStream(clean, { rate: ratePct, pitch: '+0Hz' });
        const parts = [];
        await new Promise((resolve, reject) => {
          const t = setTimeout(() => reject(new Error('timeout')), 6000);
          audioStream.on('data', d => parts.push(d));
          audioStream.on('end', () => { clearTimeout(t); resolve(); });
          audioStream.on('error', err => { clearTimeout(t); reject(err); });
        });
        try { tts.close(); } catch (_) {}
        const buf = Buffer.concat(parts);
        if (buf.length > 256) return buf;
      } catch (err) {
        try { tts.close(); } catch (__) {}
      }
    }
  }
  return Buffer.alloc(0);
}

const PORT = Number(process.env.JARVIS_PORT || 7788);
const SERVER_STARTED_AT = Date.now();
const PUBLIC_DIR = path.join(__dirname, 'public');
const butler = new ButlerEngine();
const voiceSessions = new VoiceSessions(butler);
butler.codingJobs.onUpdate = job => {
  if (job.status === 'running') return;
  const ok = job.status === 'completed';
  const spoken = ok ? '코덱스 코딩 작업이 끝났습니다. 결과를 확인해 주세요.' : '코덱스 코딩 작업이 실패했습니다. 코딩 상태를 확인해 주세요.';
  const item = { text: `코딩 작업 ${job.id}`, intent: 'codingStatus', isFinal: true, hudColor: ok ? 'lime' : 'amber', hudTitle: ok ? '코덱스 작업 완료' : '코덱스 작업 실패', spoken, ts: Date.now() };
  global.__jarvisLatestEarItem = item;
  if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
  global.__jarvisEarQueue.push(item);
  if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
};

const reaJsonPath = path.join(PUBLIC_DIR, 'data', 'rea-report.json');
if (!fs.existsSync(reaJsonPath)) {
  try {
    runReaAnalysis();
  } catch (_) {}
}

const MIME_TYPES = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'application/javascript; charset=utf-8',
  '.json': 'application/json; charset=utf-8',
  '.png': 'image/png',
  '.svg': 'image/svg+xml',
  '.mp3': 'audio/mpeg',
  '.wav': 'audio/wav'
};

// Same-origin browser access only. Native local clients do not send Origin.
const CORS_HEADERS = { 'X-Content-Type-Options': 'nosniff' };

function readBody(req, maxBytes = 65536) {
  return new Promise(resolve => {
    let data = '';
    req.on('data', chunk => {
      data += chunk;
      if (data.length > maxBytes) req.destroy();
    });
    req.on('end', () => {
      try {
        resolve(data ? JSON.parse(data) : {});
      } catch (_) {
        resolve({});
      }
    });
  });
}

function resolveGeminiApiKey() {
  try {
    const cfg = butler.getConfig();
    if (cfg && cfg.geminiApiKey && String(cfg.geminiApiKey).trim()) {
      return String(cfg.geminiApiKey).trim();
    }
  } catch (_) {}
  if (process.env.GEMINI_API_KEY) return String(process.env.GEMINI_API_KEY).trim();
  if (process.env.GOOGLE_AI_STUDIO_API_KEY) return String(process.env.GOOGLE_AI_STUDIO_API_KEY).trim();
  try {
    const envFiles = ['C:\\자비스\\.env', 'C:\\BeausCreators\\.env', 'C:\\BeausCreators\\.env.dev'];
    for (const f of envFiles) {
      if (fs.existsSync(f)) {
        const raw = fs.readFileSync(f, 'utf8');
        const m = raw.match(/^(?:GEMINI_API_KEY|GOOGLE_AI_STUDIO_API_KEY)=(.+)$/m);
        if (m && m[1]) {
          return m[1].trim().replace(/^["']|["']$/g, '');
        }
      }
    }
  } catch (_) {}
  return '';
}

function callGeminiAudioSttOnce(apiKey, modelName, audioBase64, mimeType) {
  return new Promise(resolve => {
    const payload = JSON.stringify({
      contents: [
        {
          parts: [
            {
              inlineData: {
                mimeType: mimeType || 'audio/webm',
                data: audioBase64
              }
            },
            {
              text: '이 한국어 음성 오디오에서 화자가 말한 한국어 문장을 들리는 그대로 정확히 한 줄로 받아적으세요. 부연 설명이나 따옴표 없이 오직 발화된 한국어 문장만 출력하세요. 아무 말도 없거나 숨소리/배경잡음뿐이면 빈 문자열만 출력하세요.'
            }
          ]
        }
      ],
      generationConfig: {
        temperature: 0.0,
        maxOutputTokens: 120
      }
    });
    const req = https.request(
      {
        hostname: 'generativelanguage.googleapis.com',
        path: `/v1beta/models/${encodeURIComponent(modelName)}:generateContent?key=${encodeURIComponent(apiKey)}`,
        method: 'POST',
        timeout: 6500,
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload)
        }
      },
      res => {
        let out = '';
        res.on('data', d => (out += d.toString('utf8')));
        res.on('end', () => {
          if (res.statusCode !== 200) return resolve('');
          try {
            const parsed = JSON.parse(out);
            const text =
              parsed &&
              parsed.candidates &&
              parsed.candidates[0] &&
              parsed.candidates[0].content &&
              parsed.candidates[0].content.parts &&
              parsed.candidates[0].content.parts[0] &&
              parsed.candidates[0].content.parts[0].text;
            if (text) {
              const cleaned = String(text)
                .replace(/^["'“”‘’]+|["'“”‘’]+$/g, '')
                .replace(/\r?\n+/g, ' ')
                .trim();
              if (
                cleaned &&
                !/^(없음|무음|빈 문자열|none|silence|null|기침|음\.?|아!?|어\.?)$/i.test(cleaned) &&
                !/아무\s*말도\s*없거나|숨소리|배경\s*잡음|타이핑하는\s*소리|\d{2}:\d{2}|^[ㅎㅋㄹ0ㅁ\s]+$/i.test(cleaned)
              ) {
                return resolve(cleaned);
              }
            }
          } catch (_) {}
          resolve('');
        });
      }
    );
    req.on('error', () => resolve(''));
    req.on('timeout', () => {
      req.destroy();
      resolve('');
    });
    req.write(payload);
    req.end();
  });
}

async function transcribeAudioBufferWithGemini(audioBase64, mimeType = 'audio/webm') {
  const apiKey = resolveGeminiApiKey();
  if (!apiKey || !audioBase64 || audioBase64.length < 400) return '';
  const models = ['gemini-3.8-flash', 'gemini-3.5-flash-lite', 'gemini-2.5-flash'];
  for (const m of models) {
    const text = await callGeminiAudioSttOnce(apiKey, m, audioBase64, mimeType);
    if (text) return text;
  }
  return '';
}

const VOICEPRINT_FILE = path.join(__dirname, 'data', 'owner-voiceprint.json');
function loadOwnerVoicePrint() {
  try {
    if (fs.existsSync(VOICEPRINT_FILE)) {
      return JSON.parse(fs.readFileSync(VOICEPRINT_FILE, 'utf8'));
    }
  } catch (_) {}
  return {
    enrolled: true,
    ownerName: '이건우 대표님',
    pitchHz: 132,
    pitchRange: [82, 235],
    minEnergy: 0.055,
    strictOwnerGate: true,
    updatedAt: new Date().toISOString()
  };
}
function saveOwnerVoicePrint(vp) {
  try {
    fs.mkdirSync(path.dirname(VOICEPRINT_FILE), { recursive: true });
    fs.writeFileSync(VOICEPRINT_FILE, JSON.stringify(vp, null, 2), 'utf8');
  } catch (_) {}
  return vp;
}
function evaluateOwnerVoiceMatch(features, vp) {
  if (!vp || !vp.enrolled) return { matchScore: 100, isOwnerVoice: true };
  const pitch = Number(features && features.pitchHz) || 0;
  const energy = Number(features && features.energy) || 0;
  const minE = Number(vp.minEnergy) || 0.05;
  if (energy > 0 && energy < minE * 0.72) {
    return { matchScore: 32, isOwnerVoice: false, reason: 'low-energy-outdoor' };
  }
  if (pitch > 0 && vp.pitchHz > 0) {
    const diff = Math.abs(pitch - vp.pitchHz);
    const score = Math.max(25, Math.min(99, Math.round(100 - diff * 0.65)));
    const isOwner = vp.strictOwnerGate ? score >= 52 && energy >= minE * 0.75 : true;
    return { matchScore: score, isOwnerVoice: isOwner };
  }
  return { matchScore: 92, isOwnerVoice: true };
}
let ownerVoicePrint = loadOwnerVoicePrint();
if (!fs.existsSync(VOICEPRINT_FILE)) saveOwnerVoicePrint(ownerVoicePrint);

function sendJson(res, status, obj) {
  const payload = JSON.stringify(obj);
  res.writeHead(status, {
    ...CORS_HEADERS,
    'Content-Type': 'application/json; charset=utf-8',
    'Cache-Control': 'no-store'
  });
  res.end(payload);
}

/**
 * Tier-2 100% Offline Windows Native SAPI WAV Synthesizer + Host Speaker Playback
 */
function synthesizeWindowsNativeWav(text, alsoSpeakOnHost = false) {
  return new Promise(resolve => {
    const safe = String(text || '').replace(/'/g, "''").replace(/\r?\n/g, ' ');
    const ps = `
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$ko = $synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'ko-KR' } | Select-Object -First 1
if ($ko) { $synth.SelectVoice($ko.VoiceInfo.Name) }
$synth.Rate = 1
${alsoSpeakOnHost ? `$synth.SetOutputToDefaultAudioDevice(); $synth.Speak('${safe}');` : ''}
$ms = New-Object System.IO.MemoryStream
$synth.SetOutputToWaveStream($ms)
$synth.Speak('${safe}')
$synth.SetOutputToNull()
[Convert]::ToBase64String($ms.ToArray())
$synth.Dispose()
`;
    const child = spawn('powershell.exe', ['-NoProfile', '-Command', ps], { windowsHide: true });
    const timer = setTimeout(() => { child.kill(); resolve(null); }, 12000);
    let out = '';
    child.stdout.on('data', d => (out += d.toString('utf8')));
    child.on('close', () => {
      clearTimeout(timer);
      const b64 = out.trim();
      if (b64.length > 100) {
        try {
          return resolve(Buffer.from(b64, 'base64'));
        } catch (_) {}
      }
      resolve(null);
    });
    child.on('error', () => { clearTimeout(timer); resolve(null); });
  });
}

/**
 * Windows 11 Native Hardware Microphone Listener (System.Speech.Recognition)
 */
function listenWindowsNativeMicOnce(timeoutSec = 5) {
  return new Promise(resolve => {
    const ps = `
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try {
  Add-Type -AssemblyName System.Speech
  $rec = New-Object System.Speech.Recognition.SpeechRecognitionEngine
  $choices = New-Object System.Speech.Recognition.Choices
  $choices.Add(@(
    "자비스", "헤이 자비스", "사사리사", "아빠 왔어", "다녀왔어",
    "자비스 내 말 들려", "내 말 들려", "자비스 넌 누구야", "넌 누구야", "안녕", "반가워",
    "유튜브에서 로파이 검색해", "1번 틀어", "2번 틀어", "3번 틀어",
    "쿠팡에서 맥미니 찾아", "강남역 가는 길", "오늘 날씨", "뉴스 보여줘",
    "카톡 영웅님한테 오늘도 힘내세요라고 답장해", "응", "아니",
    "소리 키워", "소리 줄여", "음소거", "되돌려", "방금 뭐 했어", "창 다 꺼",
    "몇 시야", "오늘 며칠이야", "라면 3분 타이머", "메모장 열어", "계산기 열어", "크롬 열어"
  ))
  $gb = New-Object System.Speech.Recognition.GrammarBuilder($choices)
  $rec.LoadGrammar((New-Object System.Speech.Recognition.Grammar($gb)))
  try { $rec.LoadGrammar((New-Object System.Speech.Recognition.DictationGrammar)) } catch {}
  $rec.SetInputToDefaultAudioDevice()
  $res = $rec.Recognize([TimeSpan]::FromSeconds(${Number(timeoutSec) || 5}))
  if ($res -ne $null -and $res.Text -ne "") {
    Write-Output ("HEARD:" + $res.Text)
  } else {
    Write-Output "SILENCE"
  }
} catch {
  Write-Output ("ERR:" + $_.Exception.Message)
}
`;
    const child = spawn('powershell.exe', ['-NoProfile', '-Command', ps], { windowsHide: true });
    let out = '';
    child.stdout.on('data', d => (out += d.toString('utf8')));
    child.on('close', () => {
      const text = out.trim();
      if (text.startsWith('HEARD:')) {
        return resolve({ ok: true, transcript: text.slice(6).trim() });
      }
      if (text === 'SILENCE') {
        return resolve({ ok: false, reason: 'silence', message: '마이크에 음성이 감지되지 않았습니다. 다시 말씀해 주세요.' });
      }
      resolve({ ok: false, reason: 'error', message: text.replace(/^ERR:/, '') });
    });
    child.on('error', err => resolve({ ok: false, reason: 'error', message: err.message }));
  });
}

const server = http.createServer(async (req, res) => {
  const allowedHosts = new Set([`127.0.0.1:${PORT}`, `localhost:${PORT}`]);
  if (!allowedHosts.has(String(req.headers.host || '').toLowerCase())) {
    return sendJson(res, 403, { ok: false, error: 'Invalid host' });
  }
  const origin = req.headers.origin;
  if (origin && origin !== `http://${req.headers.host}`) {
    return sendJson(res, 403, { ok: false, error: 'Cross-origin request denied' });
  }
  if (req.method === 'POST' && !/^application\/json(?:\s*;|$)/i.test(req.headers['content-type'] || '')) {
    return sendJson(res, 415, { ok: false, error: 'JSON required' });
  }
  if (req.method === 'OPTIONS') {
    res.writeHead(204, CORS_HEADERS);
    return res.end();
  }

  const urlObj = new URL(req.url, `http://127.0.0.1:${PORT}`);
  const pathname = urlObj.pathname;

  // 1) GET /api/status or /api/butler/state
  if (req.method === 'GET' && (pathname === '/api/status' || pathname === '/api/butler/state')) {
    const cfg = butler.getConfig();
    const telemetry = butler.getSystemTelemetry();
    const daily = butler.getDailyUsage(7);
    const startup = getStartupStatus();
    let routines = {};
    let memory = '';
    try {
      routines = JSON.parse(fs.readFileSync(FILES.routines, 'utf8'));
      memory = fs.readFileSync(FILES.memory, 'utf8');
    } catch (_) {}
    return sendJson(res, 200, {
      ok: true,
      version: '0.7.3',
      runtime: { entry: path.basename(__filename), pid: process.pid },
      config: cfg,
      telemetry,
      daily,
      startup,
      routines,
      memory,
      neuralTtsAvailable: Boolean(MsEdgeTTS),
      ytList: butler.ytList,
      subwindows: butler.getSubwindowsState()
    });
  }

  // 1-B) GET /api/hud/subwindows (Real-time Collapsible Task & Music Sub-Windows State)
  if (req.method === 'GET' && pathname === '/api/hud/subwindows') {
    return sendJson(res, 200, {
      ok: true,
      ...butler.getSubwindowsState()
    });
  }

  // 1-D) GET /api/hud/chlgd-dashboard (CHLGD Rules, Logs, Google Harness² & Active Plan Document)
  if (req.method === 'GET' && pathname === '/api/hud/chlgd-dashboard') {
    return sendJson(res, 200, getChlgdAndPlanDashboard(sharedBscBridge));
  }

  // 1-E) GET & POST /api/hud/plan-doc (Plan & Document HUD Viewer Window)
  if (req.method === 'GET' && pathname === '/api/hud/plan-doc') {
    const dash = getChlgdAndPlanDashboard(sharedBscBridge);
    return sendJson(res, 200, {
      ok: true,
      openWindowSeq: dash.openWindowSeq,
      activeTab: dash.activeTab,
      activePlanDoc: dash.activePlanDoc
    });
  }
  if (req.method === 'POST' && pathname === '/api/hud/plan-doc') {
    const body = await readBody(req);
    if (body && body.onlyOpenWindow) {
      const st = triggerHudPlanWindowOpen(body.targetTab || 'plan');
      return sendJson(res, 200, { ok: true, openWindowSeq: st.openWindowSeq, activeTab: st.activeTab, activePlanDoc: st.activePlanDoc });
    }
    const st = createOrUpdatePlanDocument({
      title: body.title || '📋 자비스 Plan · 설계 문서',
      category: body.category || 'CHLGD Plan & Architecture',
      summary: body.summary || '',
      sections: Array.isArray(body.sections) ? body.sections : null,
      rawTopic: body.rawTopic || body.title || '',
      triggerOpenWindow: body.openWindow !== false,
      targetTab: body.targetTab || 'plan'
    });
    return sendJson(res, 200, { ok: true, openWindowSeq: st.openWindowSeq, activeTab: st.activeTab, activePlanDoc: st.activePlanDoc });
  }

  // 1-F) POST /api/hud/rules (Add new CEO Conversation Rule & CHLGD Guardrail)
  if (req.method === 'POST' && pathname === '/api/hud/rules') {
    const body = await readBody(req);
    const updated = addConversationRule({
      ruleText: body.ruleText || '',
      category: body.category || '대화 학습 규칙',
      source: body.source || 'HUD 직접 등록'
    });
    return sendJson(res, 200, {
      ok: true,
      conversationRules: updated ? updated.conversationRules : []
    });
  }

  // 1-G) POST /api/hud/harness2-evolve (Run Google Harness² 8-Surface Validation & Recursion Pass)
  if (req.method === 'POST' && pathname === '/api/hud/harness2-evolve') {
    const body = await readBody(req);
    const resObj = runJarvisHarness2EvolutionCycle((body && body.reason) || 'HUD Manual Evolution Trigger');
    return sendJson(res, 200, resObj);
  }

  // 1-H) GET & POST /mcp (Model Context Protocol JSON-RPC 2.0 Endpoint for Antigravity IDE)
  if (pathname === '/mcp') {
    if (req.method === 'GET') {
      return sendJson(res, 200, {
        ok: true,
        serverInfo: MCP_SERVER_INFO,
        tools: MCP_TOOLS
      });
    }
    if (req.method === 'POST') {
      const rpcReq = await readBody(req, 512 * 1024);
      const rpcResp = await handleMcpJsonRpcRequest(rpcReq, butler);
      return sendJson(res, 200, rpcResp || { jsonrpc: '2.0', result: {} });
    }
  }

  // 1-C) POST /api/tasks/run | /api/tasks/stop | /api/tasks/dismiss
  if (req.method === 'POST' && pathname === '/api/tasks/run') {
    try {
      const body = await readBody(req);
      const taskState = sharedTerminalManager.runTask({
        title: body.title || '터미널 작업 실행',
        command: body.command,
        cwd: body.cwd || 'C:\\자비스',
        chlgdPhase: body.chlgdPhase || 'Execute',
        partnerAgent: body.partnerAgent || null
      });
      return sendJson(res, 200, { ok: true, taskState });
    } catch (err) {
      return sendJson(res, 400, { ok: false, error: err.message });
    }
  }
  if (req.method === 'POST' && pathname === '/api/tasks/stop') {
    const stopped = sharedTerminalManager.stopActiveTask();
    return sendJson(res, 200, { ok: true, stopped, activeTask: sharedTerminalManager.getActiveState() });
  }
  if (req.method === 'POST' && pathname === '/api/tasks/dismiss') {
    const dismissed = sharedTerminalManager.dismissActiveTask();
    return sendJson(res, 200, { ok: true, dismissed });
  }

  // 2) GET /api/rea/blueprint
  if (req.method === 'GET' && pathname === '/api/rea/blueprint') {
    try {
      if (fs.existsSync(reaJsonPath)) {
        const report = JSON.parse(fs.readFileSync(reaJsonPath, 'utf8'));
        return sendJson(res, 200, { ok: true, report });
      }
      const report = buildReaReportObject();
      return sendJson(res, 200, { ok: true, report });
    } catch (err) {
      return sendJson(res, 500, { ok: false, error: err.message });
    }
  }

  // 3) POST /api/butler/command
  if (req.method === 'POST' && pathname === '/api/butler/command') {
    const body = await readBody(req);
    const sentence = String(body.sentence || '').trim();
    if (body.source === 'voice' && !voiceSessions.accept(body.voiceSession, sentence)) {
      if (global.__jarvisLatestEarItem && global.__jarvisLatestEarItem.isFinal === false && global.__jarvisLatestEarItem.text === sentence) {
        global.__jarvisLatestEarItem = { text: sentence, isFinal: true, intent: 'wakeRequired', hudTitle: '자비스라고 부른 뒤 명령해 주세요', hudColor: 'amber', ts: Date.now() };
      }
      return sendJson(res, 200, { ok: true, ignored: true });
    }
    const executeOs = body.executeOs !== false;
    global.__jarvisLatestEarItem = { text: sentence, isFinal: true, intent: 'processing', hudTitle: '○ 명령 처리 중', hudColor: 'cyan', ts: Date.now() };
    let result;
    try {
      result = await butler.execute(sentence, executeOs);
    } catch (err) {
      const failure = { text: sentence, isFinal: true, intent: 'error', hudColor: 'amber', hudTitle: '⚠ 명령 실행 실패', hudSubtitle: err.message, spoken: `명령을 실행하지 못했습니다. ${err.message}`, ts: Date.now() };
      global.__jarvisLatestEarItem = failure;
      if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
      global.__jarvisEarQueue.push(failure);
      if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
      return sendJson(res, 500, { ok: false, error: '명령 실행 실패', detail: err.message,
        result: { intent: 'error', hudColor: 'amber', hudTitle: '⚠ 명령 실행 실패', hudSubtitle: err.message, spoken: '명령을 실행하지 못했습니다.' } });
    }
    if (body.source === 'voice' && result && result.pendingSend) {
      voiceSessions.extend(body.voiceSession, result.pendingSend.expiresAt);
    }
    if (body.source === 'voice' && result && result.intent === 'awake') {
      const wakeUntil = Date.now() + 45000;
      global.__jarvisForceAwakeUntil = wakeUntil;
      voiceSessions.wakeAll(wakeUntil);
    }
    if (body.source === 'voice' && result && (result.intent === 'hush' || result.intent === 'bye')) {
      global.__jarvisForceAwakeUntil = 0;
    }
    if (body.source === 'voice' && result && result.intent === 'dictation') {
      voiceSessions.extend(body.voiceSession, Date.now() + 600000);
    }
    try {
      sharedBscBridge.recordOrderAndGrow({
        sentence,
        intent: result && result.intent,
        hudTitle: result && result.hudTitle,
        hudColor: result && result.hudColor,
        source: body.source || 'manual',
        partnerAgent: result && result.taskState && result.taskState.partnerAgent
      });
    } catch (_) {}
    if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
    const syncItem = {
      text: sentence,
      intent: (result && result.intent) || '',
      isFinal: true,
      hudColor: (result && result.hudColor) || 'lime',
      hudTitle: (result && result.hudTitle) || '✓ 실행 완료',
      hudSubtitle: (result && result.hudSubtitle) || '',
      awaitingConfirm: Boolean(result && result.awaitingConfirm),
      spoken: (result && (result.spoken || result.hudSubtitle)) || sentence,
      ts: Date.now()
    };
    global.__jarvisLatestEarItem = syncItem;
    global.__jarvisEarQueue.push(syncItem);
    if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
    return sendJson(res, 200, { ok: true, result });
  }

  // 4) POST /api/ear/listen-once (Windows Native Hardware Mic STT + Immediate Execution)
  if (req.method === 'POST' && pathname === '/api/ear/listen-once') {
    const body = await readBody(req);
    const heard = await listenWindowsNativeMicOnce(body.timeoutSec || 5);
    if (!heard.ok) {
      return sendJson(res, 200, { ok: false, ...heard });
    }
    let result;
    try { result = await butler.execute(heard.transcript, true); }
    catch (err) { return sendJson(res, 500, { ok: false, error: '명령 실행 실패', message: err.message }); }
    return sendJson(res, 200, { ok: true, transcript: heard.transcript, result });
  }

  // 5) POST /api/config
  if (req.method === 'POST' && pathname === '/api/config') {
    const body = await readBody(req);
    const updated = butler.saveConfig(body);
    return sendJson(res, 200, { ok: true, config: updated });
  }

  // 6) POST /api/startup
  if (req.method === 'POST' && pathname === '/api/startup') {
    const body = await readBody(req);
    const status = registerWindowsStartup(Boolean(body.enable));
    return sendJson(res, status.error ? 500 : 200, { ok: !status.error, startup: status });
  }

  // 7) POST /api/forget-learned
  if (req.method === 'POST' && pathname === '/api/forget-learned') {
    try {
      fs.writeFileSync(FILES.learned, JSON.stringify({ entries: {} }, null, 2), 'utf8');
    } catch (_) {}
    return sendJson(res, 200, { ok: true, message: '배운 명령 기록을 초기화했습니다.' });
  }

  // 7-A) Live Korean Speech Queue & Cross-Window Sync (/api/ear/push, /api/ear/level, /api/ear/speaking, /api/ear/poll, /api/ear/latest, /ear)
  if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
  if (req.method === 'GET' && pathname === '/api/ear/health') {
    const health = global.__jarvisEarHealth;
    const connected = Boolean(health && health.microphone === 'ready' && health.recognition === 'listening' && Date.now() - health.ts < 15000);
    const recoverable = Date.now() - SERVER_STARTED_AT > 8000 && (!health || Date.now() - health.ts > 20000);
    return sendJson(res, 200, { ok: true, connected, recoverable });
  }
  if (req.method === 'POST' && pathname === '/api/ear/health') {
    const body = await readBody(req);
    global.__jarvisEarHealth = {
      microphone: ['starting', 'ready', 'error'].includes(body.microphone) ? body.microphone : 'starting',
      recognition: ['starting', 'listening', 'error'].includes(body.recognition) ? body.recognition : 'starting',
      speechBuffer: ['pause-2000-v1', 'endpoint-v2', 'endpoint-media-v3', 'endpoint-media-v4'].includes(body.speechBuffer) ? body.speechBuffer : '',
      turn: body.turn ? { finalSegments: Math.max(0, Math.min(1000, Number(body.turn.finalSegments) || 0)), interimChars: Math.max(0, Math.min(12000, Number(body.turn.interimChars) || 0)), finalizing: Boolean(body.turn.finalizing) } : null,
      error: String(body.error || '').slice(0, 120), ts: Date.now()
    };
    return sendJson(res, 200, { ok: true });
  }
  if (req.method === 'POST' && pathname === '/api/ear/level') {
    const body = await readBody(req);
    if (body && typeof body.level === 'number') {
      global.__jarvisMicLevel = Math.max(0, Math.min(1, Number(body.level) || 0));
    }
    const pitchHz = Number(body && body.pitchHz) || 0;
    const energy = Number(body && body.energy) || global.__jarvisMicLevel || 0;
    const zcr = Number(body && body.zcr) || 0;
    if (global.__jarvisEnrollSamples && Array.isArray(global.__jarvisEnrollSamples)) {
      if (energy >= 0.035 && pitchHz >= 75 && pitchHz <= 300) {
        global.__jarvisEnrollSamples.push({ pitchHz, energy, zcr });
      }
    }
    const matchInfo = evaluateOwnerVoiceMatch({ pitchHz, energy, zcr }, ownerVoicePrint);
    global.__jarvisLastVoiceFeatures = {
      pitchHz,
      energy,
      zcr,
      matchScore: matchInfo.matchScore,
      isOwnerVoice: matchInfo.isOwnerVoice,
      ts: Date.now()
    };
    return sendJson(res, 200, {
      ok: true,
      isSpeaking: Boolean(global.__jarvisIsSpeaking),
      mediaActive: butler.hasMediaContext(),
      forceAwakeUntil: global.__jarvisForceAwakeUntil || 0,
      matchScore: matchInfo.matchScore,
      isOwnerVoice: matchInfo.isOwnerVoice
    });
  }
  if (req.method === 'GET' && pathname === '/api/ear/voiceprint') {
    return sendJson(res, 200, {
      ok: true,
      voicePrint: ownerVoicePrint,
      live: global.__jarvisLastVoiceFeatures || { pitchHz: 0, energy: 0, matchScore: 100, isOwnerVoice: true }
    });
  }
  if (req.method === 'POST' && pathname === '/api/ear/enroll-voice') {
    const body = await readBody(req);
    if (body && typeof body.strictOwnerGate === 'boolean' && !body.enrollNow) {
      ownerVoicePrint.strictOwnerGate = body.strictOwnerGate;
      if (typeof body.minEnergy === 'number') ownerVoicePrint.minEnergy = body.minEnergy;
      ownerVoicePrint.updatedAt = new Date().toISOString();
      saveOwnerVoicePrint(ownerVoicePrint);
      return sendJson(res, 200, { ok: true, voicePrint: ownerVoicePrint });
    }
    global.__jarvisEnrollSamples = [];
    const durationMs = Math.max(2500, Math.min(7000, Number((body && body.durationMs) || 4000)));
    await new Promise(r => setTimeout(r, durationMs));
    const samples = global.__jarvisEnrollSamples || [];
    global.__jarvisEnrollSamples = null;
    if (samples.length > 0) {
      const avgPitch = Math.round(samples.reduce((a, b) => a + b.pitchHz, 0) / samples.length);
      const avgEnergy = Number((samples.reduce((a, b) => a + b.energy, 0) / samples.length).toFixed(3));
      ownerVoicePrint = {
        enrolled: true,
        ownerName: '이건우 대표님',
        pitchHz: avgPitch,
        pitchRange: [Math.max(75, avgPitch - 42), Math.min(300, avgPitch + 48)],
        minEnergy: Math.max(0.042, Number((avgEnergy * 0.65).toFixed(3))),
        strictOwnerGate: true,
        sampleCount: samples.length,
        updatedAt: new Date().toISOString()
      };
    } else {
      const lastP = (global.__jarvisLastVoiceFeatures && global.__jarvisLastVoiceFeatures.pitchHz) || 132;
      ownerVoicePrint = {
        ...ownerVoicePrint,
        enrolled: true,
        ownerName: '이건우 대표님',
        pitchHz: lastP > 75 ? lastP : 132,
        strictOwnerGate: true,
        updatedAt: new Date().toISOString()
      };
    }
    saveOwnerVoicePrint(ownerVoicePrint);
    return sendJson(res, 200, {
      ok: true,
      voicePrint: ownerVoicePrint,
      message: `이건우 대표님 성문 등록 완료 (${ownerVoicePrint.pitchHz}Hz · 근접 음성 필터 가동)`
    });
  }
  if (req.method === 'POST' && pathname === '/api/ear/wake') {
    const body = await readBody(req);
    const sec = Math.max(10, Math.min(180, Number((body && body.durationSec) || 60)));
    global.__jarvisForceAwakeUntil = Date.now() + sec * 1000;
    global.__jarvisIsSpeaking = false;
    try { voiceSessions.wakeAll(global.__jarvisForceAwakeUntil); } catch (_) {}
    const item = {
      text: '지금 편하게 말씀해 주세요 (예: “유튜브에서 로파이 음악 틀어줘”, “웹브라우저 켜서 네이버 열어줘”)',
      isFinal: false,
      hudTitle: '● 집중 듣는 중 (호출어 없이 바로 말씀하세요)',
      hudColor: 'cyan',
      ts: Date.now()
    };
    global.__jarvisLatestEarItem = item;
    if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
    global.__jarvisEarQueue.push(item);
    if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
    return sendJson(res, 200, { ok: true, forceAwakeUntil: global.__jarvisForceAwakeUntil });
  }
  if (req.method === 'POST' && pathname === '/api/ear/transcribe-audio') {
    const body = await readBody(req, 3 * 1024 * 1024);
    if (global.__jarvisIsSpeaking) {
      return sendJson(res, 200, { ok: true, transcript: '', skipped: 'speaking' });
    }
    const audioBase64 = String((body && body.audioBase64) || '').trim();
    const mimeType = String((body && body.mimeType) || 'audio/webm').trim();
    const transcript = await transcribeAudioBufferWithGemini(audioBase64, mimeType);
    if (transcript) {
      global.__jarvisForceAwakeUntil = Math.max(global.__jarvisForceAwakeUntil || 0, Date.now() + 120000);
      try { voiceSessions.wakeAll(global.__jarvisForceAwakeUntil); } catch (_) {}
      const item = {
        text: transcript,
        isFinal: false,
        hudTitle: '○ 받아 적는 중',
        hudColor: 'cyan',
        ts: Date.now()
      };
      global.__jarvisLatestEarItem = item;
      if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
      global.__jarvisEarQueue.push(item);
      if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
    }
    return sendJson(res, 200, { ok: true, transcript });
  }
  if (req.method === 'POST' && pathname === '/api/ear/speaking') {
    const body = await readBody(req);
    global.__jarvisIsSpeaking = Boolean(body && body.speaking);
    return sendJson(res, 200, { ok: true });
  }
  if (req.method === 'POST' && pathname === '/api/ear/push') {
    const body = await readBody(req);
    if (body && typeof body.micLevel === 'number') {
      global.__jarvisMicLevel = Math.max(0, Math.min(1, Number(body.micLevel) || 0));
    }
    if (body && body.text && !global.__jarvisIsSpeaking) {
      const item = {
        text: String(body.text).trim(),
        isFinal: Boolean(body.isFinal),
        ts: Date.now()
      };
      if (body.status === 'recognition-error') Object.assign(item, { isFinal: true, intent: 'speechError', hudTitle: '음성 확정 실패 · 다시 말씀하거나 입력해 주세요', hudColor: 'amber' });
      if (body.status === 'processing') Object.assign(item, { isFinal: true, intent: 'processing', hudTitle: '○ 명령 처리 중', hudColor: 'cyan' });
      global.__jarvisLatestEarItem = item;
      global.__jarvisEarQueue.push(item);
      if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
    }
    return sendJson(res, 200, { ok: true, isSpeaking: Boolean(global.__jarvisIsSpeaking) });
  }
  if (req.method === 'GET' && pathname === '/api/ear/poll') {
    const items = global.__jarvisEarQueue.splice(0, global.__jarvisEarQueue.length);
    return sendJson(res, 200, { ok: true, items, micLevel: global.__jarvisMicLevel || 0 });
  }
  if (req.method === 'GET' && pathname === '/api/ear/latest') {
    return sendJson(res, 200, {
      ok: true,
      latest: global.__jarvisTimerDisplay && Date.now() < global.__jarvisTimerDisplayUntil
        ? global.__jarvisTimerDisplay : global.__jarvisLatestEarItem || null,
      earHealth: global.__jarvisEarHealth || null,
      micLevel: global.__jarvisMicLevel || 0,
      isSpeaking: Boolean(global.__jarvisIsSpeaking)
    });
  }
  if (req.method === 'POST' && pathname === '/api/ear/launch-chrome') {
    const chromeCandidates = [
      'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
      'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe'
    ];
    const chromeExe = chromeCandidates.find(p => fs.existsSync(p));
    const appUrl = `http://127.0.0.1:${PORT}/ear`;
    if (chromeExe) {
      spawn(
        chromeExe,
        [
          `--app=${appUrl}`,
          '--use-fake-ui-for-media-stream',
          '--autoplay-policy=no-user-gesture-required',
          '--window-position=180,42',
          '--window-size=710,255'
        ],
        { detached: true, stdio: 'ignore', windowsHide: false }
      ).unref();
    }
    return sendJson(res, 200, { ok: true, launched: Boolean(chromeExe), url: appUrl });
  }
  if (req.method === 'GET' && (pathname === '/ear' || pathname === '/react')) {
    res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' });
    return res.end(`<!DOCTYPE html><html lang="ko"><head><meta charset="utf-8"><title>자비스 — React 18 소형 HUD (SasariSa)</title>
<style>
  * { box-sizing: border-box; user-select: none; }
  body {
    margin: 0;
    background: #03070e;
    color: #fff;
    font-family: 'Malgun Gothic', -apple-system, BlinkMacSystemFont, sans-serif;
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    min-height: 100vh;
    padding: 12px 16px;
  }
  .jarvis-react-root {
    width: min(680px, 96vw);
    display: flex;
    flex-direction: column;
    gap: 10px;
  }
  .jarvis-hud-pill {
    width: 100%;
    min-height: 116px;
    background: rgba(5, 11, 22, 0.97);
    border: 2px solid #4dd9ff;
    border-radius: 24px;
    padding: 14px 24px 14px 18px;
    display: flex;
    align-items: center;
    gap: 18px;
    position: relative;
    transition: border-color 0.2s, box-shadow 0.2s;
  }
  .jarvis-orb-wrap {
    width: 86px;
    height: 86px;
    flex: 0 0 86px;
    position: relative;
    display: flex;
    align-items: center;
    justify-content: center;
    cursor: pointer;
  }
  .jarvis-ring {
    position: absolute;
    border-radius: 50%;
    border: 2px dashed #4dd9ff;
  }
  .jarvis-ring.r1 { width: 82px; height: 82px; animation: spin 9s linear infinite; }
  .jarvis-ring.r2 { width: 64px; height: 64px; border-width: 1.5px; opacity: 0.8; animation: spinRev 6s linear infinite; }
  .jarvis-ring.r3 { width: 46px; height: 46px; border-style: dotted; border-width: 2.5px; animation: spin 4s linear infinite; }
  .jarvis-core {
    width: 22px;
    height: 22px;
    border-radius: 50%;
    background: radial-gradient(circle, #ffffff 0%, #4dd9ff 55%, transparent 100%);
    transition: transform 0.08s ease-out;
  }
  @keyframes spin { to { transform: rotate(360deg); } }
  @keyframes spinRev { to { transform: rotate(-360deg); } }
  .jarvis-txt-col { flex: 1; display: flex; flex-direction: column; gap: 6px; overflow: hidden; }
  .jarvis-st-line {
    font-size: 14.5px;
    font-weight: 800;
    color: #4dd9ff;
    display: flex;
    align-items: center;
    gap: 10px;
  }
  .jarvis-mic-meter-wrap {
    width: 68px;
    height: 7px;
    background: rgba(255, 255, 255, 0.12);
    border-radius: 99px;
    overflow: hidden;
    display: inline-flex;
  }
  .jarvis-mic-meter-fill {
    height: 100%;
    border-radius: 99px;
    transition: width 0.08s linear;
  }
  .jarvis-react-badge {
    margin-left: auto;
    font-size: 11px;
    font-weight: 700;
    color: rgba(77, 217, 255, 0.85);
    border: 1px solid rgba(77, 217, 255, 0.35);
    padding: 2px 8px;
    border-radius: 99px;
  }
  .jarvis-bd-line {
    font-size: 23px;
    font-weight: 800;
    color: #ffffff;
    line-height: 1.28;
    word-break: keep-all;
  }
  .jarvis-ctrl-strip {
    display: flex;
    gap: 8px;
    flex-wrap: wrap;
  }
  .jarvis-btn {
    background: rgba(77, 217, 255, 0.14);
    border: 1.5px solid rgba(77, 217, 255, 0.65);
    color: #fff;
    padding: 7px 14px;
    border-radius: 99px;
    font-size: 12.5px;
    font-weight: 800;
    cursor: pointer;
    transition: 0.15s;
  }
  .jarvis-btn:hover { background: rgba(77, 217, 255, 0.28); }
  .jarvis-btn.active {
    background: rgba(115, 255, 153, 0.2);
    border-color: #73ff99;
    color: #73ff99;
  }
  .jarvis-btn.send {
    background: rgba(77, 217, 255, 0.26);
    border-color: #4dd9ff;
    padding: 8px 18px;
  }
  .jarvis-input-row {
    display: flex;
    gap: 8px;
    width: 100%;
  }
  .jarvis-quick-input {
    flex: 1;
    background: rgba(255, 255, 255, 0.07);
    border: 1px solid rgba(77, 217, 255, 0.45);
    border-radius: 99px;
    padding: 8px 16px;
    color: #fff;
    font-size: 13.5px;
    outline: none;
    user-select: text;
  }
  .jarvis-quick-input:focus {
    border-color: #4dd9ff;
    box-shadow: 0 0 12px rgba(77, 217, 255, 0.35);
  }
  .jarvis-preset-row {
    display: flex;
    gap: 6px;
    flex-wrap: wrap;
  }
  .jarvis-preset-chip {
    background: rgba(255, 255, 255, 0.05);
    border: 1px solid rgba(255, 255, 255, 0.16);
    color: #d8ecff;
    padding: 5px 12px;
    border-radius: 99px;
    font-size: 12px;
    font-weight: 700;
    cursor: pointer;
  }
  .jarvis-preset-chip:hover {
    border-color: #4dd9ff;
    color: #fff;
  }
</style>
<script src="/js/react18-runtime.js"></script>
<script src="/js/speech-turn-buffer.js"></script>
<script src="/js/media-intents.js"></script>
<script src="/js/jarvis-react-hud.js"></script>
</head>
<body>
  <div id="jarvis-react-root"></div>
</body></html>`);
  }

  // 8-A) GET /api/tts-mp3?text=... (24kHz 96kbps Studio HD Neural Voice with Clock Sync)
  if (req.method === 'GET' && pathname === '/api/tts-mp3') {
    const text = String(urlObj.searchParams.get('text') || '').trim();
    const voice = urlObj.searchParams.get('voice') || butler.getConfig().sayVoice || 'ko-KR-HyunsuMultilingualNeural';
    if (!text) {
      res.writeHead(204);
      return res.end();
    }
    const mp3Buf = await synthesizeStudioNeuralMp3Buffer(text, voice);
    if (mp3Buf && mp3Buf.length > 256) {
      res.writeHead(200, {
        ...CORS_HEADERS,
        'Content-Type': 'audio/mpeg',
        'Content-Length': mp3Buf.length,
        'Cache-Control': 'no-store'
      });
      return res.end(mp3Buf);
    }
    const wav = await synthesizeWindowsNativeWav(text);
    if (wav && wav.length > 44 && wav.subarray(0, 4).toString() === 'RIFF') {
      res.writeHead(200, { ...CORS_HEADERS, 'Content-Type': 'audio/wav', 'Content-Length': wav.length, 'Cache-Control': 'no-store' });
      return res.end(wav);
    }
    return sendJson(res, 503, { ok: false, error: 'Windows 음성 생성 실패' });
  }

  // 8) POST /api/tts (Mouth.swift 1:1 Studio HD Neural Voice Stream)
  if (req.method === 'POST' && pathname === '/api/tts') {
    const body = await readBody(req);
    const text = String(body.text || '').trim();
    const voice = body.voice || butler.getConfig().sayVoice || 'ko-KR-HyunsuMultilingualNeural';
    if (!text) {
      return sendJson(res, 200, { ok: false });
    }
    const mp3Buf = await synthesizeStudioNeuralMp3Buffer(text, voice);
    if (mp3Buf && mp3Buf.length > 256) {
      res.writeHead(200, {
        ...CORS_HEADERS,
        'Content-Type': 'audio/mpeg',
        'Content-Length': mp3Buf.length,
        'Cache-Control': 'no-store'
      });
      return res.end(mp3Buf);
    }
    const wav = await synthesizeWindowsNativeWav(text);
    if (wav && wav.length > 44 && wav.subarray(0, 4).toString() === 'RIFF') {
      res.writeHead(200, { ...CORS_HEADERS, 'Content-Type': 'audio/wav', 'Content-Length': wav.length, 'Cache-Control': 'no-store' });
      return res.end(wav);
    }
    return sendJson(res, 503, { ok: false, error: 'Windows 음성 생성 실패' });
  }

  // 9) Static files
  let safeRel = pathname === '/' ? '/index.html' : pathname;
  const filePath = path.join(PUBLIC_DIR, safeRel);
  if (filePath.startsWith(PUBLIC_DIR) && fs.existsSync(filePath) && fs.statSync(filePath).isFile()) {
    const ext = path.extname(filePath).toLowerCase();
    res.writeHead(200, {
      ...CORS_HEADERS,
      'Content-Type': MIME_TYPES[ext] || 'application/octet-stream'
    });
    fs.createReadStream(filePath).pipe(res);
    return;
  }

  sendJson(res, 404, { ok: false, error: 'Not Found' });
});

let timerCheckInterval;
server.on('listening', () => {
  const pendingNotifications = [];
  const checkTimers = () => {
    for (const timer of butler.takeDueTimers()) {
      const spoken = `${timer.label || '타이머'} 시간이 끝났습니다.`;
      const item = { text: `${spoken} (${timer.fireAt})`, spoken, hudTitle: '⏰ 타이머 종료', hudColor: 'lime', isFinal: true, kind: 'timer', ts: Date.now() };
      pendingNotifications.push(item);
    }
    if (pendingNotifications.length && Date.now() >= (global.__jarvisTimerDisplayUntil || 0)) {
      const item = pendingNotifications.shift();
      global.__jarvisTimerDisplay = item;
      global.__jarvisTimerDisplayUntil = Date.now() + 2000;
      if (!global.__jarvisEarQueue) global.__jarvisEarQueue = [];
      global.__jarvisEarQueue.push(item);
      if (global.__jarvisEarQueue.length > 20) global.__jarvisEarQueue.shift();
    }
  };
  checkTimers();
  timerCheckInterval = setInterval(checkTimers, 500);
});
server.on('close', () => clearInterval(timerCheckInterval));

if (require.main === module) {
  server.on('error', error => {
    console.error(error.code === 'EADDRINUSE' ? 'Jarvis port is already in use; existing processes were left running.' : error);
    process.exit(1);
  });
  server.listen(PORT, '127.0.0.1', () => console.log('Jarvis server listening on 127.0.0.1:' + PORT));
}

module.exports = { server, butler, PORT };
