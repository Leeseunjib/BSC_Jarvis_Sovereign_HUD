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
const { ButlerEngine, WindowsHands, FILES } = require('./scripts/butler-engine.verified');
const { getEcosystemOverview, searchEcosystems } = require('./scripts/ecosystem-index');
const { createDocument, verifyDocument, normalizeDocSpec } = require('./scripts/office-doc-engine');
const { runRea, runLaya, startLayaService, getLayaStatus } = require('./scripts/rea-laya-bridge');
const { getLearningStatus, runRecursionCycle } = require('./scripts/recursive-learning');
const { sharedTerminalManager, sharedBscBridge } = require('./scripts/bsc-collaboration-bridge');
const { VoiceSessions } = require('./scripts/voice-session');
const { SpeakerGate, speakerMessage } = require('./scripts/speaker-gate');
const speakerGate = new SpeakerGate();
const { VOICES, speechOptions, mp3ToWav } = require('./scripts/tts-audio');
let lastTtsStatus = null;
const { normalizeEarHealth, getEarStatus } = require('./scripts/ear-health');
const { askVoiceBrain, getBrainStatus, verifyBrain, stopAntigravity } = require('./scripts/voice-brain');
const earEvents = [];
const researchReports = new Map();
let lastEarEventKey = '';
function recordEarEvent(event) {
  earEvents.push(event);
  if (earEvents.length > 200) earEvents.shift();
  const file = path.join(path.dirname(FILES.config), 'speech-events-' + new Date().toISOString().slice(0, 10) + '.jsonl');
  fs.appendFile(file, JSON.stringify(event) + '\n', () => {});
}
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

const ttsAudioCache = new Map();

async function synthesizeStudioNeuralMp3Buffer(rawText, voice, userSpeed) {
  const clean = humanizeKoreanSpeechText(rawText);
  if (!clean) return Buffer.alloc(0);
  const cacheKey = `${voice}|${userSpeed}|${clean}`;
  if (ttsAudioCache.has(cacheKey)) {
    return ttsAudioCache.get(cacheKey);
  }

  // Tier 1: Microsoft Edge Studio Neural MP3 (Single unbroken 24kHz 96kbps studio MP3 stream)
  if (MsEdgeTTS) {
    await ensureEdgeTtsClockSync();
    const ratePct = `${userSpeed >= 1 ? '+' : ''}${Math.round((userSpeed - 1) * 100)}%`;
    const candidates = [voice]; // Never silently substitute a different speaker.
    for (const v of candidates) {
      if (!v) continue;
      const tts = new MsEdgeTTS();
      try {
        const fmt = OUTPUT_FORMAT.AUDIO_24KHZ_96KBITRATE_MONO_MP3 || OUTPUT_FORMAT.AUDIO_24KHZ_48KBITRATE_MONO_MP3;
        await tts.setMetadata(v, fmt);
        const { audioStream } = tts.toStream(clean, { rate: ratePct, pitch: '+0Hz' });
        const parts = [];
        await new Promise((resolve, reject) => {
          const t = setTimeout(() => reject(new Error('timeout')), 8000);
          audioStream.on('data', d => parts.push(d));
          audioStream.on('end', () => { clearTimeout(t); resolve(); });
          audioStream.on('error', err => { clearTimeout(t); reject(err); });
        });
        try { tts.close(); } catch (_) {}
        const buf = Buffer.concat(parts);
        if (buf.length > 256) {
          if (ttsAudioCache.size > 120) ttsAudioCache.delete(ttsAudioCache.keys().next().value);
          ttsAudioCache.set(cacheKey, buf);
          return buf;
        }
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

function speakerEnrollmentHud() {
  const e = speakerGate.status().enrollment;
  if (!e || !speakerGate.enrolling) return null;
  const now = Date.now();
  const step = Math.min(2, Math.max(0, Math.floor((now - e.startAt) / 8000)));
  const title = now < e.startAt ? `목소리 등록 · ${Math.ceil((e.startAt-now)/1000)}초 뒤 시작` : now < e.endAt ? `목소리 등록 · ${step+1}/3 · ${Math.ceil((e.endAt-now)/1000)}초 남음` : '목소리 등록 · 녹음 비교 중';
  return { text: e.prompts[step], hudTitle: title, hudColor: 'cyan', isFinal: true, intent: 'speakerEnrollment', ts: Math.floor(now/1000)*1000 };
}

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
function synthesizeWindowsNativeWav(text, speed = 1.25) {
  return new Promise(resolve => {
    const safe = String(text || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, "''").replace(/\r?\n/g, ' ');
    const rate = `${speed >= 1 ? '+' : ''}${Math.round((speed - 1) * 100)}%`;
    const ps = `
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
$ko = $synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -eq 'ko-KR' } | Select-Object -First 1
if (!$ko) { throw 'No Korean Windows voice installed' }
$synth.SelectVoice($ko.VoiceInfo.Name)
$synth.Rate = 0
$ms = New-Object System.IO.MemoryStream
$synth.SetOutputToWaveStream($ms)
$synth.SpeakSsml('<speak version="1.0" xmlns="http://www.w3.org/2001/10/synthesis" xml:lang="ko-KR"><prosody rate="${rate}">${safe}</prosody></speak>')
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
  try {
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

  if (req.method === 'GET' && pathname === '/api/health') {
    return sendJson(res, 200, { ok: true, service: 'jarvis', pid: process.pid, entry: path.basename(__filename), uptimeMs: Date.now() - SERVER_STARTED_AT });
  }

  if (req.method === 'GET' && pathname === '/api/brain/status') return sendJson(res, 200, getBrainStatus(butler.getConfig()));
  if (req.method === 'POST' && pathname === '/api/brain/verify') {
    await readBody(req);
    const config = butler.getConfig();
    await verifyBrain(config);
    const status = getBrainStatus(butler.getConfig());
    return sendJson(res, 200, { ...status, ok: !!status.last?.ok });
  }
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

  if (req.method === 'GET' && pathname.startsWith('/api/research/')) {
    const report = researchReports.get(pathname.slice('/api/research/'.length));
    return sendJson(res, report ? 200 : 404, report || { error: '검색 결과가 없거나 만료됐습니다. 다시 검색해 주세요.' });
  }
  if (req.method === 'GET' && pathname === '/api/ecosystem/all') {
    const q = urlObj.searchParams.get('q');
    if (q) return sendJson(res, 200, searchEcosystems(q, { limit: Number(urlObj.searchParams.get('limit')) || 12 }));
    return sendJson(res, 200, getEcosystemOverview());
  }
  if (req.method === 'GET' && (pathname === '/api/learning/status' || pathname === '/api/recursion/status')) {
    const st = getLearningStatus();
    return sendJson(res, 200, { ...st, activeRulesCount: st.activeCount, totalRulesCount: st.totalCount, promptInjectionChars: st.promptChars });
  }
  if (req.method === 'POST' && (pathname === '/api/learning/cycle' || pathname === '/api/recursion/cycle')) {
    const body = await readBody(req);
    return sendJson(res, 200, runRecursionCycle((body && body.reason) || 'api'));
  }
  if (req.method === 'POST' && (pathname === '/api/office/create' || pathname === '/api/docs/create')) {
    try {
      const body = await readBody(req, 512 * 1024);
      const fmt = String(body.format || 'docx').toLowerCase();
      const refReport = (body.researchId && researchReports.get(body.researchId)) || butler.lastResearchReport;
      const spec = (refReport && !body.sections)
        ? normalizeDocSpec({
            title: body.title || refReport.title || refReport.query || '자비스 조사 보고서',
            subtitle: body.subtitle || `질의: ${refReport.query || ''} · 제공: ${refReport.provider || 'JARVIS'}`,
            summary: body.summary || refReport.summary || refReport.note || '',
            sections: Array.isArray(refReport.sections) ? refReport.sections.map(s => ({ heading: s.title, body: s.text })) : [],
            sources: refReport.sources || []
          })
        : normalizeDocSpec({
            title: body.title || '자비스 오피스 문서',
            subtitle: body.subtitle || 'JARVIS Office Document Engine',
            summary: body.summary || '',
            sections: Array.isArray(body.sections) ? body.sections : [{ heading: '본문', body: String(body.body || body.content || '') }],
            sources: Array.isArray(body.sources) ? body.sources : []
          });
      const created = await createDocument(fmt, spec, body.outDir);
      const verify = created && created.path ? verifyDocument(created.path) : { ok: false, bytes: 0 };
      if (body.openFile !== false && created && created.ok && created.path) {
        await WindowsHands.openApp(created.path).catch(() => {});
      }
      return sendJson(res, 200, { ...created, verify });
    } catch (err) {
      return sendJson(res, 400, { ok: false, error: err.message });
    }
  }
  if (req.method === 'POST' && pathname === '/api/rea/analyze') {
    try {
      const body = await readBody(req);
      return sendJson(res, 200, await runRea(body.target || 'C:\\자비스', body.query || ''));
    } catch (err) {
      return sendJson(res, 500, { ok: false, error: err.message });
    }
  }
  if (req.method === 'POST' && pathname === '/api/laya/execute') {
    try {
      const body = await readBody(req);
      if (body && body.startServer) {
        const srv = await startLayaService();
        if (!body.prompt) return sendJson(res, 200, srv);
      }
      const layaRes = await runLaya((body && body.prompt) || '현재 프로젝트 상태 점검', { mode: (body && body.mode) || 'auto' });
      return sendJson(res, 200, { ...layaRes, serviceStatus: await getLayaStatus() });
    } catch (err) {
      return sendJson(res, 500, { ok: false, error: err.message });
    }
  }
  if (false) {
  }
  // 3) POST /api/butler/command
  if (req.method === 'POST' && pathname === '/api/butler/command') {
    const commandStartedAt = Date.now();
    const body = await readBody(req, 2700000);
    const sentence = String(body.sentence || body.text || '').trim().slice(0, 2000);
    if (body.source === 'voice') {
      if (global.__jarvisIsSpeaking) return sendJson(res, 200, { ok: true, ignored: true, reason: 'assistant-speaking' });
      if (!voiceSessions.accept(body.voiceSession, sentence, { commit: false, ownerVerified: speakerGate.enabled })) {
        return sendJson(res, 200, { ok: true, ignored: true, reason: 'wake-required' });
      }
      const decision = await speakerGate.verify(body.speakerAudio);
      if (!decision.allowed) {
        recordEarEvent({ event: 'speaker-rejected', ts: Date.now(), reason: decision.reason, score: decision.score });
        if (!speakerGate.enrolling) global.__jarvisLatestEarItem = { text: speakerMessage(decision.reason), isFinal: true, intent: 'speakerRejected', hudTitle: '음성 실행 보류', hudColor: 'amber', ts: Date.now() };
        return sendJson(res, 200, { ok: true, ignored: true, reason: decision.reason });
      }
      if (global.__jarvisIsSpeaking || !voiceSessions.accept(body.voiceSession, sentence, { ownerVerified: decision.verified })) return sendJson(res, 200, { ok: true, ignored: true, reason: 'voice-session-expired' });
    }
    const executeOs = body.executeOs !== false;
    const executionStartedAt = Date.now();
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
    if (result && result.research) {
      researchReports.set(result.research.id, result.research);
      while(researchReports.size > 10) researchReports.delete(researchReports.keys().next().value);
    }
    if (body.source === 'voice' && result && result.pendingSend) {
      voiceSessions.extend(body.voiceSession, result.pendingSend.expiresAt);
    }
    if (body.source === 'voice' && result && result.intent === 'awake') {
      voiceSessions.extend(body.voiceSession, Date.now() + 45000);
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
      researchId: (result && result.researchId) || '',
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
    const timing = { totalMs: Date.now() - commandStartedAt, executionMs: Date.now() - executionStartedAt, inputMs: executionStartedAt - commandStartedAt };
    recordEarEvent({ event: 'command-timing', ts: Date.now(), intent: result && result.intent, source: body.source || 'manual', ...timing,
      ...(body.source === 'voice' && Number.isFinite(body.voiceTiming?.silenceMs) ? { silenceMs: Math.max(0, Math.min(300000, body.voiceTiming.silenceMs)) } : {}),
      ...(result && result.llm ? { llm: result.llm } : {}) });
    return sendJson(res, 200, { ok: !(result && result.error), result, timing });
  }

  // 4) POST /api/ear/listen-once (Windows Native Hardware Mic STT + Immediate Execution)
  if (req.method === 'POST' && pathname === '/api/ear/listen-once') {
    if (speakerGate.enabled || speakerGate.enrolling) return sendJson(res, 409, { ok: false, error: '내 목소리 확인이 필요합니다. 메인 HUD의 상시 마이크를 사용하거나 직접 입력해 주세요.' });
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
    if ('sayVoice' in body || 'ttsVoice' in body || 'ttsSpeed' in body) {
      if ('sayVoice' in body) body.ttsVoice = body.sayVoice;
      else if ('ttsVoice' in body) body.sayVoice = body.ttsVoice;
      try { speechOptions({ ...butler.getConfig(), ...body }); }
      catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    }
    const updated = butler.saveConfig(body);
    if ('brainModel' in body || 'brainProvider' in body || 'geminiApiKey' in body) stopAntigravity();
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
    return sendJson(res, 200, getEarStatus(health, SERVER_STARTED_AT));
  }
  if (req.method === 'GET' && pathname === '/api/ear/events') {
    return sendJson(res, 200, { ok: true, events: earEvents });
  }
  if (req.method === 'POST' && pathname === '/api/ear/health') {
    const body = await readBody(req);
    global.__jarvisEarHealth = normalizeEarHealth(body);
    const health = global.__jarvisEarHealth;
    const eventKey = health.workerSession + ':' + health.sequence;
    if (health.event && eventKey !== lastEarEventKey) { lastEarEventKey = eventKey; recordEarEvent(health); }
    return sendJson(res, 200, { ok: true });
  }
  if (req.method === 'POST' && pathname === '/api/ear/level') {
    const body = await readBody(req);
    global.__jarvisSpeakerSamples = Math.max(0, Math.min(1e12, Number(body.speakerSamples) || 0));
    if (body && typeof body.level === 'number') {
      global.__jarvisMicLevel = Math.max(0, Math.min(1, Number(body.level) || 0));
    }
    const pitchHz = Number(body && body.pitchHz) || 0;
    const energy = Number(body && body.energy) || global.__jarvisMicLevel || 0;
    const zcr = Number(body && body.zcr) || 0;
    global.__jarvisLastVoiceFeatures = { pitchHz, energy, zcr, ts: Date.now() };
    return sendJson(res, 200, {
      ok: true, isSpeaking: Boolean(global.__jarvisIsSpeaking), mediaActive: butler.hasMediaContext(),
      forceAwakeUntil: global.__jarvisForceAwakeUntil || 0, speaker: speakerGate.status()
    });
  }
  if (req.method === 'GET' && pathname === '/api/speaker/state') return sendJson(res, 200, { ...speakerGate.status(), captureReady: !!global.__jarvisEarHealth?.speakerCapture, captureSamples: global.__jarvisSpeakerSamples || 0 });
  if (req.method === 'GET' && pathname === '/api/ear/voiceprint') return sendJson(res, 200, { ...speakerGate.status(), legacyPitchGate: false });
  if (req.method === 'POST' && pathname === '/api/ear/enroll-voice') return sendJson(res, 409, { ok: false, error: '음높이 등록은 지원하지 않습니다. 환경설정 → 음성 · 마이크에서 내 목소리를 등록해 주세요.' });
  if (req.method === 'POST' && pathname.startsWith('/api/speaker/')) {
    try {
      const body = await readBody(req, 1200000);
      if (pathname === '/api/speaker/enroll/start') {
        const health = global.__jarvisEarHealth;
        if (!health || !health.speakerCapture || health.microphone !== 'ready' || Date.now()-health.ts > 15000) throw new Error('등록용 마이크 연결을 기다린 뒤 다시 눌러 주세요.');
        if (global.__jarvisIsSpeaking) throw new Error('자비스 음성이 끝난 뒤 등록을 시작해 주세요.');
        return sendJson(res, 200, speakerGate.start());
      }
      if (pathname === '/api/speaker/enroll/complete') {
        const result = await speakerGate.finish(body.id, body.audio, body.transcript);
        global.__jarvisLatestEarItem = { text: '자비스, 지금 시간을 알려 주세요 라고 말씀해 보세요.', hudTitle: '내 목소리 등록 완료 · 내 목소리만 반응 켜짐', hudColor: 'lime', isFinal: true, ts: Date.now() };
        voiceSessions.sessions.clear(); global.__jarvisForceAwakeUntil = 0;
        return sendJson(res, 200, result);
      }
      if (pathname === '/api/speaker/enroll/cancel') return sendJson(res, 200, speakerGate.cancel());
      if (pathname === '/api/speaker/enabled') {
        if (typeof body.enabled !== 'boolean') throw new Error('켜기 또는 끄기를 선택해 주세요.');
        voiceSessions.sessions.clear(); global.__jarvisForceAwakeUntil = 0;
        return sendJson(res, 200, speakerGate.setEnabled(body.enabled));
      }
      if (pathname === '/api/speaker/forget') return sendJson(res, 200, speakerGate.forget());
      return sendJson(res, 404, { ok: false, error: '지원하지 않는 목소리 설정입니다.' });
    } catch (error) {
      if (pathname === '/api/speaker/enroll/complete') global.__jarvisLatestEarItem = { text: speakerMessage(error.message), hudTitle: '목소리 등록 실패 · 다시 시도해 주세요', hudColor: 'amber', isFinal: true, ts: Date.now() };
      return sendJson(res, 400, { ok: false, error: speakerMessage(error.message) });
    }
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
    recordEarEvent({ event: global.__jarvisIsSpeaking ? 'tts-start' : 'tts-end', ts: Date.now() });
    clearTimeout(global.__jarvisSpeakingLease);
    if (global.__jarvisIsSpeaking) {
      global.__jarvisSpeakingLease = setTimeout(() => { global.__jarvisIsSpeaking = false; }, 45000);
      global.__jarvisSpeakingLease.unref();
    }
    return sendJson(res, 200, { ok: true });
  }
  if (req.method === 'POST' && pathname === '/api/ear/push') {
    const body = await readBody(req);
    if (speakerGate.enabled || speakerGate.enrolling) return sendJson(res, 200, { ok: true, suppressed: true });
    if (body && typeof body.micLevel === 'number') {
      global.__jarvisMicLevel = Math.max(0, Math.min(1, Number(body.micLevel) || 0));
    }
    if (body && body.text && !global.__jarvisIsSpeaking) {
      const cleanText = String(body.text).trim();
      const item = {
        text: cleanText,
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
      latest: speakerEnrollmentHud() || (global.__jarvisTimerDisplay && Date.now() < global.__jarvisTimerDisplayUntil
        ? global.__jarvisTimerDisplay : global.__jarvisLatestEarItem || null),
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
<script src="/js/speaker-capture.js"></script>
<script src="/js/jarvis-react-hud.js"></script>
</head>
<body>
  <div id="jarvis-react-root"></div>
</body></html>`);
  }

  if (req.method === 'GET' && pathname === '/api/tts/status') {
    const cfg = butler.getConfig();
    return sendJson(res, 200, { ok: true, voices: VOICES, selectedVoice: cfg.sayVoice, speed: cfg.ttsSpeed, last: lastTtsStatus });
  }
  if ((req.method === 'GET' && pathname === '/api/tts-mp3') || (req.method === 'POST' && pathname === '/api/tts')) {
    const ttsStartedAt = Date.now();
    const body = req.method === 'POST' ? await readBody(req) : Object.fromEntries(urlObj.searchParams);
    const text = humanizeKoreanSpeechText(body.text);
    if (!text) { res.writeHead(204); return res.end(); }
    let options;
    try { options = speechOptions(butler.getConfig(), body); }
    catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    const { voice, speed } = options;
    let bytes = null, provider = 'edge-neural', actualVoice = voice, contentType = 'audio/mpeg';
    let error = null;
    if (voice !== 'windows-ko') {
      try {
        bytes = await synthesizeStudioNeuralMp3Buffer(text, voice, speed);
        if (bytes.length <= 256) throw new Error('선택한 Neural 음성을 생성하지 못했습니다.');
        if (body.format === 'wav') { bytes = await mp3ToWav(bytes); contentType = 'audio/wav'; }
      } catch (err) { error = err.message; bytes = null; }
    }
    if (!bytes || bytes.length <= 256) {
      bytes = await synthesizeWindowsNativeWav(text, speed);
      provider = 'windows-sapi'; actualVoice = 'windows-ko'; contentType = 'audio/wav';
    }
    const fallback = voice !== actualVoice;
    const ok = !!(bytes && bytes.length > 44);
    lastTtsStatus = { at: new Date().toISOString(), ok, requestedVoice: voice, actualVoice, speed, provider, fallback, error, bytes: bytes?.length || 0, contentType, elapsedMs: Date.now() - ttsStartedAt };
    recordEarEvent({ event: 'tts-generated', ts: Date.now(), ...lastTtsStatus });
    if (!ok) return sendJson(res, 503, { ok: false, error: '음성을 생성하지 못했습니다.' });
    res.writeHead(200, { ...CORS_HEADERS, 'Content-Type': contentType, 'Content-Length': bytes.length, 'Cache-Control': 'no-store',
      'X-Jarvis-Tts-Voice': actualVoice, 'X-Jarvis-Tts-Speed': String(speed), 'X-Jarvis-Tts-Provider': provider, 'X-Jarvis-Tts-Fallback': fallback ? '1' : '0' });
    return res.end(bytes);
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
  } catch (error) {
    console.error('Request failed:', error.message);
    if (!res.headersSent) sendJson(res, error instanceof SyntaxError ? 400 : 500, { ok: false, error: error instanceof SyntaxError ? 'Invalid JSON' : '요청 처리 실패' });
    else res.end();
  }
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
server.on('close', () => { clearInterval(timerCheckInterval); stopAntigravity(); if (speakerGate.worker.stop) speakerGate.worker.stop(); });

if (require.main === module) {
  server.on('error', error => {
    console.error(error.code === 'EADDRINUSE' ? 'Jarvis port is already in use; existing processes were left running.' : error);
    process.exit(1);
  });
  server.listen(PORT, '127.0.0.1', () => {
    console.log('Jarvis server listening on 127.0.0.1:' + PORT);
    if (butler.getConfig().brainProvider === 'antigravity-cli') verifyBrain(butler.getConfig()).catch(() => {});
  });
}

module.exports = { server, butler, PORT, speakerGate };
