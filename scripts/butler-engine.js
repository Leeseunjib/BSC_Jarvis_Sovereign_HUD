/**
 * @file C:\자비스\scripts\butler-engine.js
 * @description 1:1 Windows 11 Clean-Room Port of SasariSa (v0.7.3) with Real Voice Conversation & Brain Q&A:
 *   - Sources/MacButler/Config.swift (soundAlike Hangul matcher, config.json)
 *   - Sources/MacButler/Commands.swift (34 Intent deterministic grammar parser)
 *   - Sources/MacButler/Router.swift & GrowTogether.swift (aliases.json & shared-aliases.json 0-token cache)
 *   - Sources/MacButler/Butler.swift & Brain.swift (Real-time voice Q&A + Ollama/LM Studio/API Brain + Windows Hands)
 */

const fs = require('fs');
const path = require('path');
const os = require('os');
const http = require('http');
const https = require('https');
const { exec, execFile, spawn } = require('child_process');
const { CodingJobs } = require('./coding-jobs');
const { interact } = require('./windows-interaction');
const { controlMedia } = require('./media-control');
const { classifyMediaControl } = require('../public/js/media-intents');
const { sharedTerminalManager, sharedBscBridge } = require('./bsc-collaboration-bridge');

function runPowerShell(script) {
  const wrapped = `$ProgressPreference='SilentlyContinue'; [Console]::OutputEncoding=[Text.Encoding]::UTF8; try {\n${script}\n} catch { [Console]::Error.WriteLine($_.Exception.Message); exit 1 }`;
  const encoded = Buffer.from(wrapped, 'utf16le').toString('base64');
  return new Promise((resolve, reject) => {
    const child = execFile(path.join(process.env.SystemRoot || 'C:\\Windows', 'System32', 'WindowsPowerShell', 'v1.0', 'powershell.exe'), ['-NoProfile', '-NonInteractive', '-EncodedCommand', encoded], { windowsHide: true, timeout: 12000 }, (err, stdout, stderr) => {
      if (err) return reject(new Error(err.killed ? 'Windows 작업 응답 시간이 초과되었습니다.' : String(stderr || err.message).trim().slice(0, 300)));
      resolve(stdout);
    });
    child.stdin.end();
  });
}

function launchOnVisibleDesktop(targetPathOrUrl) {
  const launcherExe = path.join(__dirname, '..', 'bin', 'JarvisDesktopLaunch.exe');
  if (fs.existsSync(launcherExe)) {
    return new Promise((resolve, reject) => {
      execFile(launcherExe, [String(targetPathOrUrl)], { windowsHide: true, timeout: 8000 }, (err, stdout) => {
        if (err) {
          return runPowerShell(`$ErrorActionPreference='Stop'; Start-Process -FilePath ${psLiteral(String(targetPathOrUrl))} -WindowStyle Normal`).then(resolve, reject);
        }
        resolve(stdout);
      });
    });
  }
  return runPowerShell(`$ErrorActionPreference='Stop'; Start-Process -FilePath ${psLiteral(String(targetPathOrUrl))} -WindowStyle Normal`);
}
function psLiteral(value) { return "'" + String(value).replace(/'/g, "''") + "'"; }

const ROOT_DIR = path.resolve(__dirname, '..');
const DATA_DIR = process.env.JARVIS_DATA_DIR || path.join(ROOT_DIR, 'data');

const FILES = {
  config: path.join(DATA_DIR, 'config.json'),
  aliases: path.join(DATA_DIR, 'aliases.json'),
  sharedAliases: path.join(DATA_DIR, 'shared-aliases.json'),
  learned: path.join(DATA_DIR, 'learned.json'),
  routines: path.join(DATA_DIR, 'routines.json'),
  timers: path.join(DATA_DIR, 'timers.json'),
  usage: path.join(DATA_DIR, 'usage.json'),
  memory: path.join(DATA_DIR, 'memory.md'),
  convLog: path.join(DATA_DIR, 'conversation.log'),
  media: path.join(DATA_DIR, 'media-context.json')
};

const DEFAULT_CONFIG = {
  language: 'ko',
  activation: 'voice',
  assistantName: '자비스',
  userName: '대표님',
  wakeWords: ['자비스', '헤이 자비스', '사사리사', '헤이 사사리사', 'jarvis', 'hey jarvis'],
  learnedWakes: [],
  greetings: [
    '아빠 왔어',
    '대표님 왔어',
    '나 왔어',
    '다녀왔어',
    '출근했어',
    '자비스 출근',
    '자비스 켜',
    '자비스 켜져',
    '자비스 깨어나',
    '부트 화면',
    '부트화면 켜',
    '사사리사 켜',
    "daddy's home",
    "i'm home"
  ],
  greetingText: '어서 오십시오, {user}. 자비스 온라인입니다.',
  bootStyle: 'reactor',
  hotkey: 'rightOption',
  tts: 'neural',
  sayVoice: 'ko-KR-HyunsuMultilingualNeural',
  ttsVoice: 'ko-KR-HyunsuMultilingualNeural',
  ttsSpeed: 1.25,
  claudeModel: 'sonnet',
  brainProvider: 'gemini-cli',
  brainModel: 'gemini-2.5-flash',
  brainEffort: 'low',
  bargeIn: 'auto',
  hudPosition: 'top',
  confirmDelegate: 'never',
  freeMode: false,
  growTogether: 'together',
  previewBeforeSend: true,
  polishDictation: true,
  showIdleOrb: true,
  followUpSec: 8,
  silenceSec: 1.0,
  customWords: ['클로드', '엔터', '받아써', '그만', '유튜브', '쿠팡', '네이버', '구글', '창 다 꺼', '되돌려'],
  apps: {}
};

const SEED_ALIASES = {
  '불꺼': '화면 꺼',
  '불꺼줘': '화면 꺼',
  '조용히해': '음소거',
  '닥쳐': '음소거',
  '소리켜': '음소거 해제',
  '소리키워': '소리 올려',
  '소리줄여': '소리 줄여',
  '방금거취소': '되돌려',
  '원래대로': '되돌려',
  '다꺼': '창 다 꺼',
  '열어둔거다꺼': '창 다 꺼'
};

const MEMORY_STORE = {};

function ensureDataFiles() {
  try {
    fs.mkdirSync(DATA_DIR, { recursive: true });
    if (!fs.existsSync(FILES.config)) {
      fs.writeFileSync(FILES.config, JSON.stringify(DEFAULT_CONFIG, null, '  '), 'utf8');
    }
    if (!fs.existsSync(FILES.aliases)) {
      fs.writeFileSync(FILES.aliases, JSON.stringify(SEED_ALIASES, null, '  '), 'utf8');
    }
    if (!fs.existsSync(FILES.sharedAliases)) {
      fs.writeFileSync(FILES.sharedAliases, JSON.stringify(SEED_ALIASES, null, '  '), 'utf8');
    }
    if (!fs.existsSync(FILES.learned)) {
      fs.writeFileSync(FILES.learned, JSON.stringify({ entries: {} }, null, '  '), 'utf8');
    }
    if (!fs.existsSync(FILES.routines)) {
      fs.writeFileSync(
        FILES.routines,
        JSON.stringify(
          {
            '작업 시작': ['메모장 열어', '유튜브에서 로파이 검색해'],
            '퇴근 준비': ['창 다 꺼', '음소거']
          },
          null,
          '  '
        ),
        'utf8'
      );
    }
    if (!fs.existsSync(FILES.timers)) {
      fs.writeFileSync(FILES.timers, JSON.stringify([], null, '  '), 'utf8');
    }
    if (!fs.existsSync(FILES.usage)) {
      const today = new Date().toISOString().slice(0, 10);
      fs.writeFileSync(FILES.usage, JSON.stringify({ [today]: { calls: 0, tokens: 0 } }, null, '  '), 'utf8');
    }
    if (!fs.existsSync(FILES.memory)) {
      fs.writeFileSync(
        FILES.memory,
        '# 자비스 (SasariSa Windows 11) 장기 기억 장부\n- 호칭: 대표님 (이건우 대표님)\n- 비서 이름: 자비스 (사사리사 1:1 호환)\n',
        'utf8'
      );
    }
  } catch (_) {}
}

function readJson(filePath, fallback) {
  if (MEMORY_STORE[filePath] !== undefined) return MEMORY_STORE[filePath];
  try {
    return JSON.parse(fs.readFileSync(filePath, 'utf8'));
  } catch (_) {
    return fallback;
  }
}

function writeJson(filePath, data) {
  MEMORY_STORE[filePath] = data;
  try {
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    fs.writeFileSync(filePath, JSON.stringify(data, null, 2), 'utf8');
  } catch (_) {}
}

/**
 * Port of Config.soundAlike(_ name: String) from Sources/MacButler/Config.swift (lines 118-137)
 */
function soundAlikePattern(name) {
  const lead = [
    ['ㄱ', 'ㄲ', 'ㅋ'], ['ㄲ', 'ㄱ', 'ㅋ'], ['ㄴ'], ['ㄷ', 'ㄸ', 'ㅌ'], ['ㄸ', 'ㄷ', 'ㅌ'],
    ['ㄹ', 'ㄴ'], ['ㅁ'], ['ㅂ', 'ㅃ', 'ㅍ'], ['ㅃ', 'ㅂ', 'ㅍ'], ['ㅅ', 'ㅆ'], ['ㅆ', 'ㅅ'],
    ['ㅇ'], ['ㅈ', 'ㅉ', 'ㅊ'], ['ㅉ', 'ㅈ', 'ㅊ'], ['ㅊ', 'ㅈ', 'ㅉ'], ['ㅋ', 'ㄱ', 'ㄲ'],
    ['ㅌ', 'ㄷ', 'ㄸ'], ['ㅍ', 'ㅂ', 'ㅃ'], ['ㅎ', 'ㅇ']
  ];
  const vowel = [
    [0, 4, 1, 5], [1, 5, 0, 2], [2, 0, 6], [3, 7, 1], [4, 0, 5, 1], [5, 1, 4, 7],
    [6, 4, 2], [7, 5, 1], [8, 13, 4], [9, 10, 14, 0], [10, 9, 15, 1], [11, 10, 15, 18],
    [12, 8], [13, 8, 18], [14, 9, 4], [15, 10, 11, 5], [16, 11, 20], [17, 13],
    [18, 13, 20, 4], [19, 18, 20], [20, 18, 5, 1]
  ];
  const tail = [0, 1, 1, 1, 4, 4, 4, 7, 8, 8, 8, 8, 8, 8, 8, 8, 16, 17, 17, 19, 19, 21, 19, 19, 1, 7, 17, 0];
  const L = ['ㄱ','ㄲ','ㄴ','ㄷ','ㄸ','ㄹ','ㅁ','ㅂ','ㅃ','ㅅ','ㅆ','ㅇ','ㅈ','ㅉ','ㅊ','ㅋ','ㅌ','ㅍ','ㅎ'];

  const chars = Array.from(name);
  if (chars.length < 2) return null;
  const parts = [];
  for (const ch of chars) {
    const code = ch.charCodeAt(0);
    if (code < 0xac00 || code >= 0xd7a4) return null;
    const u = code - 0xac00;
    const l = Math.floor(u / 588);
    const v = Math.floor((u % 588) / 28);
    const t = u % 28;
    const ls = lead[l].map(c => L.indexOf(c)).filter(idx => idx >= 0);
    const vs = vowel[v] || [v];
    const ts = [];
    for (let ti = 0; ti < 28; ti++) {
      if (tail[ti] === tail[t] || (t === 0 && [4, 8, 16, 21].includes(ti)) || ti === 0) {
        ts.push(ti);
      }
    }
    const set = new Set();
    for (const li of ls) {
      for (const vi of vs) {
        for (const ti of ts) {
          set.add(String.fromCharCode(0xac00 + li * 588 + vi * 28 + ti));
        }
      }
    }
    parts.push(`[${Array.from(set).sort().join('')}]`);
  }
  return parts.join('\\s*');
}

function squash(s) {
  return String(s || '')
    .toLowerCase()
    .replace(/[^a-z0-9가-힣]/g, '');
}

function cleanLocalDraft(value) {
  return String(value || '').trim().replace(/\s*(?:자비스|assistant)\s*:\s*$/i, '').trim();
}

const WindowsHands = {
  APP_MAP: {
    '크롬': 'chrome',
    '구글 크롬': 'chrome',
    '구글크롬': 'chrome',
    '웹브라우저': 'chrome',
    '웹 브라우저': 'chrome',
    '브라우저': 'chrome',
    '인터넷': 'chrome',
    '인터넷창': 'chrome',
    '인터넷 창': 'chrome',
    '사파리': 'msedge',
    '엣지': 'msedge',
    '메모': 'notepad',
    '메모장': 'notepad',
    '카톡': 'KakaoTalk',
    '카카오톡': 'KakaoTalk',
    '클로드': 'Claude',
    '터미널': 'wt',
    '터미널창': 'wt',
    '파워쉘': 'powershell',
    '파워쉘창': 'powershell',
    '명령프롬프트': 'cmd',
    'cmd': 'cmd',
    '파인더': 'explorer',
    '탐색기': 'explorer',
    '파일탐색기': 'explorer',
    '계산기': 'calc',
    '설정': 'ms-settings:',
    '코드': 'code',
    '노션': 'Notion',
    '슬랙': 'slack',
    '엑셀': 'excel',
    '워드': 'winword',
    '파워포인트': 'powerpnt',
    '안티그래비티': 'antigravity',
    '커서': 'cursor',
    '옵시디언': 'obsidian',
    '디스코드': 'discord',
    '올라마': 'ollama',
    '블렌더': 'blender',
    '뷰스크리에이터스': 'C:\\BeausCreators',
    '본사폴더': 'C:\\BeausCreators\\01.BSC_HQ',
    '에이전트로스터': 'C:\\BeausCreators\\01.BSC_HQ\\2.Agents\\에이전트\\AI_Agent_Roster.html',
    '에이전트관제판': 'C:\\BeausCreators\\01.BSC_HQ\\2.Agents\\에이전트\\AI_Agent_Roster.html'
  },

  findDesktopShortcut(spokenName) {
    const q = squash(spokenName);
    const desktopDirs = [
      path.join(os.homedir(), 'Desktop'),
      path.join(os.homedir(), 'OneDrive', 'Desktop'),
      path.join(os.homedir(), 'OneDrive', '바탕 화면'),
      'C:\\Users\\Public\\Desktop'
    ];
    const keywordAliases = {
      '엑셀': 'excel',
      '워드': 'word',
      '파워포인트': 'powerpoint',
      '구글독스': 'googledocs',
      '시길': 'sigil',
      '노션': 'notion',
      '헤르메스': 'hermes',
      '그록': 'grok',
      '커서': 'cursor',
      '안티그래비티': 'antigravity',
      '제미나이': 'gemini',
      '오르카': 'orca',
      '디스코드': 'discord',
      '바이오닉': 'bionic',
      '올라마': 'ollama',
      '옵시디언': 'obsidian',
      '프레이머': 'framer',
      '어사이드': 'aside',
      '에프엘스튜디오': 'flstudio',
      '에프엘': 'flstudio',
      '펜타블렛': 'pentablet',
      '에이스스튜디오': 'acestudio',
      '뮤시아': 'musia'
    };
    const matchKey = keywordAliases[q] || q;
    for (const dir of desktopDirs) {
      try {
        if (!fs.existsSync(dir)) continue;
        const files = fs.readdirSync(dir);
        for (const f of files) {
          const sqFile = squash(f.replace(/\.(lnk|url|cmd|exe)$/i, ''));
          if (sqFile.includes(matchKey) || matchKey.includes(sqFile)) {
            return path.join(dir, f);
          }
        }
      } catch (_) {}
    }
    return null;
  },

  async openUrl(url) {
    const target = new URL(url);
    if (!['http:', 'https:'].includes(target.protocol)) throw new Error('Unsupported URL protocol');
    await launchOnVisibleDesktop(target.href);
  },

  async openMediaUrl(url) {
    const target = new URL(url);
    if (target.protocol !== 'https:' || !/(^|\.)youtube\.com$/i.test(target.hostname)) throw new Error('유튜브 재생 주소가 아닙니다.');
    const browser = ['C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', 'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe', 'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe'].find(file => fs.existsSync(file));
    if (!browser) {
      await launchOnVisibleDesktop(target.href);
      return { processId: 0, browser: 'default', opened: true };
    }
    const profile = path.join(ROOT_DIR, 'data', 'chrome-media-live');
    const launcherExe = path.join(ROOT_DIR, 'bin', 'JarvisDesktopLaunch.exe');
    const args = ['--new-window', '--no-first-run', '--no-default-browser-check', '--force-renderer-accessibility', '--autoplay-policy=no-user-gesture-required', `--user-data-dir=${profile}`, target.href];

    // Pause any previously playing YouTube browser session before opening the new track
    try {
      await controlMedia({ action: 'pause', scope: 'browser' });
    } catch (_) {}

    if (fs.existsSync(launcherExe)) {
      await new Promise((resolve, reject) => {
        execFile(launcherExe, [browser, ...args], { windowsHide: true, timeout: 8000 }, err => {
          if (err) reject(err);
          else resolve();
        });
      }).catch(async () => {
        await launchOnVisibleDesktop(target.href);
      });
    } else {
      await new Promise((resolve, reject) => {
        const child = spawn(browser, args, { detached: true, stdio: 'ignore', windowsHide: false });
        child.once('error', reject);
        child.once('spawn', () => { child.unref(); resolve(); });
      });
    }

    const output = await runPowerShell(`
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=[Text.Encoding]::UTF8
$found=$null
for($i=0;$i -lt 25;$i++) {
  $roots=@(Get-WmiObject Win32_Process -Filter ${psLiteral('Name = "' + path.basename(browser) + '"')} | Where-Object { $_.CommandLine -and $_.CommandLine.Contains(${psLiteral(profile)}) -and $_.CommandLine -notmatch '--type=' })
  foreach($root in $roots) {
    $process=Get-Process -Id $root.ProcessId -ErrorAction SilentlyContinue
    if($process -and $process.MainWindowHandle -ne 0){$found=$process;break}
  }
  if($found){break}
  Start-Sleep -Milliseconds 200
}
if(-not $found){
  @{processId=0;browser=${psLiteral(path.basename(browser))};opened=$true}|ConvertTo-Json -Compress
} else {
  @{processId=$found.Id;browser=${psLiteral(path.basename(browser))};opened=$true}|ConvertTo-Json -Compress
}
`);
    const parsedOut = JSON.parse(output.trim());
    // Ensure YouTube video actually starts playing if opened in Paused state
    setTimeout(() => {
      controlMedia({ action: 'play', scope: 'browser', targetProcessId: parsedOut.processId || null }).catch(() => {});
    }, 2400);
    return parsedOut;
  },

  async openApp(spokenName, customApps = {}) {
    const key = String(spokenName || '').trim();
    const sqKey = squash(key);
    const shortcut = this.findDesktopShortcut(key);
    if (shortcut) {
      await launchOnVisibleDesktop(shortcut);
      return path.basename(shortcut);
    }
    const target = customApps[key] || this.APP_MAP[key] || this.APP_MAP[sqKey] || this.APP_MAP[key.toLowerCase()] || key;
    await launchOnVisibleDesktop(target);
    return target;
  },

  async stopMusicAndCloseYouTube() {
    return controlMedia({ action: 'pause', scope: 'browser' });
  },

  async volume(action) {
    const vk = action === 'mute' ? 173 : action === 'down' ? 174 : 175;
    const loops = action === 'mute' ? 1 : 5;
    const ps = `$ErrorActionPreference='Stop'; Add-Type -TypeDefinition 'using System; using System.Runtime.InteropServices; public static class JarvisKey { [DllImport("user32.dll")] public static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra); }'; 1..${loops} | ForEach-Object { [JarvisKey]::keybd_event(${vk},0,0,[UIntPtr]::Zero); [JarvisKey]::keybd_event(${vk},0,2,[UIntPtr]::Zero) }`;
    await runPowerShell(ps);
  },

  async mediaKey(action) {
    const mapped = { next: 'next', stop: 'pause', pause: 'pause', play: 'play' }[action];
    if (!mapped) throw new Error('재생과 정지를 명확하게 지정해 주세요.');
    return controlMedia({ action: mapped, scope: 'any' });
  },

  async typeText(text, pressEnter = false) {
    const safe = String(text || '').replace(/'/g, "''");
    const enterCmd = pressEnter ? '; Start-Sleep -Milliseconds 120; $ws.SendKeys("{ENTER}")' : '';
    const ps = `Set-Clipboard -Value '${safe}'; $ws = New-Object -ComObject WScript.Shell; $ws.SendKeys('^v')${enterCmd}`;
    await runPowerShell(`$ErrorActionPreference='Stop'; ${ps}`);
  }
};

function searchYouTubeTop5(query) {
  return new Promise(resolve => {
    const url = `https://www.youtube.com/results?search_query=${encodeURIComponent(query)}`;
    const req = https
      .get(
        url,
        {
          headers: {
            'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
            'Accept-Language': 'ko-KR,ko;q=0.9'
          }
        },
        res => {
          let html = '';
          const parse = () => {
            const list = [];
            const regex = /"videoRenderer":\{"videoId":"([a-zA-Z0-9_-]{11})".*?"title":\{(?:"runs":\[\{"text":"([^"]+)"|"simpleText":"([^"]+)")/g;
            let match;
            const seen = new Set();
            while ((match = regex.exec(html)) !== null && list.length < 5) {
              const titleText = match[2] || match[3] || 'YouTube 미디어';
              if (!seen.has(match[1])) {
                seen.add(match[1]);
                list.push({ index: list.length + 1, id: match[1], title: titleText.replace(/\\u0026/g, '&'), url: `https://www.youtube.com/watch?v=${match[1]}` });
              }
            }
            resolve(list);
          };
          res.on('data', chunk => {
            html += chunk;
            if (html.length > 2500000) { parse(); res.destroy(); }
          });
          res.on('end', parse);
          res.on('error', () => resolve([]));
        }
      )
      .on('error', () => resolve([]));
    req.setTimeout(5000, () => { req.destroy(); resolve([]); });
  });
}

/**
 * Local Ollama / Conversational Brain Responder (Brain.swift + APIBrain.swift 1:1)
 * Attempts local Ollama first if running, otherwise uses built-in intelligent Korean Butler conversational brain.
 */
function askLocalOllama(prompt, systemPrompt, model = 'gemma4:e4b', timeoutMs = 1800) {
  return new Promise(resolve => {
    const payload = JSON.stringify({
      model,
      prompt: `${systemPrompt}\n\n사용자: ${prompt}\n자비스:`,
      stream: false
    });
    const req = http.request(
      {
        hostname: '127.0.0.1',
        port: 11434,
        path: '/api/generate',
        method: 'POST',
        timeout: timeoutMs,
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload)
        }
      },
      res => {
        let body = '';
        res.on('data', c => (body += c));
        res.on('end', () => {
          try {
            const parsed = JSON.parse(body);
            if (parsed && parsed.response) return resolve(parsed.response.trim());
          } catch (_) {}
          resolve(null);
        });
      }
    );
    req.on('error', () => resolve(null));
    req.on('timeout', () => {
      req.destroy();
      resolve(null);
    });
    req.write(payload);
    req.end();
  });
}

function askGeminiCli(prompt, model = 'gemini-3-flash-preview') {
  return new Promise(resolve => {
    const safePrompt = String(prompt || '').replace(/"/g, '\\"');
    const sysInstruction = `당신은 이건우 대표님을 보좌하는 뷰스크리에이터스(C:\\BeausCreators)의 AI 집사 자비스입니다. 2문장 이내로 간결하고 자연스러운 한국어 구어체로 답변하세요. 질문: ${safePrompt}`;
    const modelArg = model ? `-m "${model}"` : '';
    const cmd = `gemini ${modelArg} -p "${sysInstruction}"`;
    exec(cmd, { timeout: 9500, windowsHide: true }, (err, stdout) => {
      if (!err && stdout && stdout.trim()) {
        return resolve(stdout.trim().replace(/\r?\n+/g, ' ').slice(0, 320));
      }
      // Fallback to default gemini-cli model if preview model flag is not supported by installed CLI version
      exec(`gemini -p "${sysInstruction}"`, { timeout: 9000, windowsHide: true }, (err2, stdout2) => {
        if (!err2 && stdout2 && stdout2.trim()) {
          return resolve(stdout2.trim().replace(/\r?\n+/g, ' ').slice(0, 320));
        }
        resolve(null);
      });
    });
  });
}

let unifiedLlmHub = null;
try {
  const hubPath = 'C:\\BSC_Javis_OS\\scripts\\multi-llm-provider-hub.js';
  if (process.env.JARVIS_DISABLE_EXTERNAL_HUB !== '1' && fs.existsSync(hubPath)) {
    unifiedLlmHub = require(hubPath);
  }
} catch (_) {}

function getBscEcosystemSummary() {
  const rosterPath = 'C:\\BeausCreators\\01.BSC_HQ\\2.Agents\\에이전트\\00.시스템\\05.System_Data_and_Docs\\agent_status.json';
  const worklogDir = 'C:\\BeausCreators\\01.BSC_HQ\\1.Documents\\02.작업일지';
  let activeCount = 0;
  let totalAgents = 0;
  let recentLogs = [];
  try {
    if (fs.existsSync(rosterPath)) {
      const roster = JSON.parse(fs.readFileSync(rosterPath, 'utf8'));
      const agents = roster.agents || roster;
      const list = Array.isArray(agents) ? agents : Object.values(agents);
      totalAgents = list.length;
      activeCount = list.filter(a => a && (a.status === 'hq_active' || a.status === 'branch_active' || a.status === 'standby')).length;
    }
  } catch (_) {}
  try {
    if (fs.existsSync(worklogDir)) {
      recentLogs = fs.readdirSync(worklogDir)
        .filter(f => f.endsWith('.md'))
        .sort()
        .slice(-3)
        .map(f => f.replace(/\.md$/, ''));
    }
  } catch (_) {}
  return { totalAgents, activeCount, recentLogs };
}

async function generateSmartButlerReply(prompt, cfg, sys, memoryText) {
  const user = cfg.userName || '대표님';
  const name = cfg.assistantName || '자비스';
  const sq = squash(prompt);

  // 1) Immediate natural Korean conversational responses
  if (/내말들려|들려|잘들려|소리가들려|마이크테스트|아아|들리니|보이니/.test(sq)) {
    return `네, ${user}! 목소리가 아주 또렷하게 잘 들립니다. 무엇이든 편하게 말씀해 주세요.`;
  }
  if (/넌누구|누구야|이름이뭐|자기소개|정체가뭐/.test(sq)) {
    return `저는 ${user}의 Windows 음성 비서 ${name}입니다.`;
  }
  if (/내가누구|나에대해|나를얼마나|내취향|내철학|내정보/.test(sq)) {
    const intel = sharedBscBridge.getCeoIntelligenceSummary();
    return `${user}은 BeausCreators(BSC)의 최고경영자이신 이건우 대표님이십니다. '사람들을 길러가고 세상을 아름답게 만든다'는 미션과 중용 23장의 정성, 냉정·정확 실측 보고 원칙을 지키고 계시며, 지금까지 자비스에게 총 ${intel.totalOrders}건의 오더를 내리셨습니다.`;
  }
  if (/안녕|반가워|좋은아침|굿모닝|하이|헬로/.test(sq)) {
    return `안녕하세요, ${user}! 오늘 컨디션은 어떠신가요? 필요하신 작업이 있으시면 언제든 말씀해 주세요.`;
  }
  if (/뭐할수있|기능이뭐|어떤일|도와줄수/.test(sq)) {
    return `앱과 검색 열기, 소리 조절, 메모, 루틴과 타이머를 지원합니다. 메시지는 전송 전에 직접 확인해 주세요.`;
  }

  // 2) C:\BeausCreators Ecosystem & 26 Agents Live Status / Independent Collaboration
  if (/뷰스크리에이터|beauscreators|에이전트현황|에이전트상태|회사현황|작업일지|보고서현황|어떻게돌아가/.test(sq)) {
    const bsc = getBscEcosystemSummary();
    const logInfo = bsc.recentLogs.length > 0 ? `최근 작업일지는 ${bsc.recentLogs.slice(-2).join(', ')}입니다.` : '';
    return bsc.totalAgents ? `${user}, 독립 집사 자비스가 파악한 BeausCreators 에이전트는 총 ${bsc.totalAgents}명이며 현재 ${bsc.activeCount}명이 활성 또는 대기 상태입니다. ${logInfo}` : `${user}, 에이전트 상태 자료를 확인 중입니다.`;
  }
  if (/소하|태준|거누|유나|수호|우진|하준|다인|도현|지윤|한정숙|시안|재호|태오|서아|제이|카엘|클레어/.test(sq)) {
    const partner = sharedBscBridge.findPartnerAgent(prompt);
    return `${user}, 독립체 자비스가 BeausCreators의 ${partner.name}(${partner.role}) 요원과 수평 협력 채널로 연결되어 있습니다. CHLGD 스크립트나 터미널 실행이 필요하시면 말씀해 주세요.`;
  }

  // 3) MCP (Model Context Protocol) Voice Operations
  if (/mcp|엠씨피|노션mcp|블렌더mcp|컴피|스티치|파이어베이스|노트북엘엠|notebooklm/.test(sq)) {
    return `${user}, 이 비서에서는 MCP 연결 상태를 확인하거나 해당 작업을 실행할 수 없습니다.`;
  }

  if (/상태어때|시스템상태|메모리얼마|컴퓨터상태/.test(sq)) {
    return `현재 ${sys.osVersion} 시스템의 메모리는 전체 ${sys.totalMemGB}기가 중 ${sys.usedMemGB}기가를 사용 중입니다.${sys.diskFreeGB == null ? ' 디스크 여유 공간은 확인하지 못했습니다.' : ` 디스크 여유 공간은 ${sys.diskFreeGB}기가입니다.`}`;
  }
  if (/기억하고있|내가뭐좋아|메모보여|기억해둔거/.test(sq)) {
    const lines = String(memoryText || '')
      .split(/\r?\n/)
      .filter(l => l.startsWith('- '))
      .slice(-3)
      .join(' ');
    return `현재 장기 기억 장부에 기록된 내용은 다음과 같습니다. ${lines || '아직 추가된 메모가 없습니다.'}`;
  }
  if (/날씨/.test(sq)) {
    return `${user}, 날씨는 네이버에서 검색해 확인해 주세요.`;
  }
  if (/뉴스|속보/.test(sq)) {
    return `${user}, 최신 뉴스는 뉴스 사이트에서 확인해 주세요.`;
  }

  // Math expression check (e.g. "125 곱하기 4는 뭐야")
  const mathMatch = prompt.match(/(\d+)\s*(더하기|빼기|곱하기|나누기|\+|\-|\*|\/)\s*(\d+)/);
  if (mathMatch) {
    const a = Number(mathMatch[1]);
    const op = mathMatch[2];
    const b = Number(mathMatch[3]);
    let ans = 0;
    if (op === '더하기' || op === '+') ans = a + b;
    else if (op === '빼기' || op === '-') ans = a - b;
    else if (op === '곱하기' || op === '*') ans = a * b;
    else if (op === '나누기' || op === '/') ans = b !== 0 ? Number((a / b).toFixed(2)) : 0;
    return `${a} ${op} ${b}의 계산 결과는 ${ans}입니다, ${user}.`;
  }

  // Use the configured provider (gemini-cli, gemini, ollama, lmstudio, openai)
  const provider = String(cfg.brainProvider || 'gemini-cli').toLowerCase();
  const selectedModel = String(cfg.brainModel || 'gemini-2.5-flash').trim();

  if (provider === 'gemini-cli' || provider === 'geminicli') {
    const cliAns = await askGeminiCli(prompt, selectedModel || 'gemini-2.5-flash');
    if (cliAns) return cliAns;
  }

  const mappedProvider = provider === 'gemini-cli' ? 'gemini' : provider;
  if (['gemini', 'openai', 'ollama', 'lmstudio'].includes(mappedProvider) && unifiedLlmHub && typeof unifiedLlmHub.chatWithUnifiedLlm === 'function') {
    try {
      const hubState = typeof unifiedLlmHub.loadHubState === 'function' ? unifiedLlmHub.loadHubState() : {};
      const providerModel = selectedModel || hubState[`${mappedProvider}Model`] || undefined;
      const llmRes = await unifiedLlmHub.chatWithUnifiedLlm({
        prompt, provider: mappedProvider, ...(providerModel ? { model: providerModel } : {}),
        systemPrompt: `당신은 이건우 대표님을 보좌하는 뷰스크리에이터스(C:\\BeausCreators)의 AI 집사 자비스입니다. 2문장 이내로 간결하고 자연스러운 한국어 구어체로 답변하세요.`
      });
      if (llmRes && llmRes.ok && llmRes.text) {
        return String(llmRes.text);
      }
    } catch (_) {}
  }

  const localOllamaModel = provider === 'ollama' && selectedModel ? selectedModel : 'gemma4:e4b';
  const ollamaReply = await askLocalOllama(
    prompt,
    `당신은 ${user}을 보좌하는 한국어 음성 비서 ${name}입니다. 1~2문장으로 간결하고 정중하게 한국어로 답변하세요.`,
    localOllamaModel
  );
  if (ollamaReply) return ollamaReply;

  // Smart fallback: answer politely and offer to search if it's an information query
  return `네, ${user}. 말씀하신 「${prompt}」 내용을 확인했습니다. 웹 검색이 필요하시면 「구글에서 ${prompt} 검색해」라고 말씀해 주세요.`;
}

class ButlerEngine {
  constructor() {
    ensureDataFiles();
    this.mode = 'idle';
    this.ytList = [];
    this.openedUrls = [];
    this.openedApps = [];
    this.lastDone = null;
    this.pendingSend = null;
    this.lastSite = null;
    this.codingJobs = new CodingJobs();
    this.interact = interact;
    this.rewriteLocal = askLocalOllama;
    this.dictationTarget = null;
    this.mailDraft = null;
    this.mediaControl = controlMedia;
    this.searchYouTube = searchYouTubeTop5;
    this.mediaContext = readJson(FILES.media, null);
  }

  getConfig() {
    return { ...DEFAULT_CONFIG, ...readJson(FILES.config, DEFAULT_CONFIG) };
  }

  hasMediaContext() {
    return Boolean(this.mediaContext && this.mediaContext.url && !['none','Closed'].includes(this.mediaContext.state) && Date.now() - this.mediaContext.updatedAt < 12 * 3600000);
  }

  rememberMedia(url, title, state = 'opened', browser = {}) {
    this.mediaContext = { url, title, state, processId: browser.processId || null, updatedAt: Date.now() };
    writeJson(FILES.media, this.mediaContext);
  }

  isMediaStopRequest(text) {
    if (this.mode === 'dictation') return false;
    const command = classifyMediaControl(this.stripWake(text), this.hasMediaContext());
    return Boolean(command && command.intent === 'stopMusic');
  }

  getSubwindowsState() {
    const activeTask = sharedTerminalManager.getActiveState();
    const hasMedia = this.hasMediaContext() && !['none', 'Closed', 'Stopped'].includes((this.mediaContext && this.mediaContext.state) || '');
    const activeMedia = hasMedia
      ? {
          active: true,
          title: (this.mediaContext && this.mediaContext.title) || 'YouTube 미디어 재생 중',
          url: (this.mediaContext && this.mediaContext.url) || '',
          state: (this.mediaContext && this.mediaContext.state) || 'Playing',
          updatedAt: (this.mediaContext && this.mediaContext.updatedAt) || Date.now()
        }
      : { active: false, title: '', url: '', state: 'none', updatedAt: 0 };

    return {
      activeTask,
      activeMedia,
      bscStatus: sharedBscBridge.getEcosystemStatus()
    };
  }

  saveConfig(patch) {
    const next = { ...this.getConfig(), ...patch };
    writeJson(FILES.config, next);
    return next;
  }

  getSystemTelemetry() {
    const totalMemGB = os.totalmem() / 1073741824;
    const freeMemGB = os.freemem() / 1073741824;
    const usedMemGB = totalMemGB - freeMemGB;
    const cpus = os.cpus();
    return {
      usedMemGB: Number(usedMemGB.toFixed(1)),
      totalMemGB: Number(totalMemGB.toFixed(0)),
      cpuModel: cpus[0] ? cpus[0].model.trim() : 'Windows x64 Processor',
      osVersion: `Windows 11 (${os.release()})`,
      diskFreeGB: (() => { try { const s = fs.statfsSync(ROOT_DIR); return Number((s.bavail * s.bsize / 1073741824).toFixed(1)); } catch (_) { return null; } })()
    };
  }

  hasWakeWord(rawText) {
    const cfg = this.getConfig();
    const t = String(rawText || '').trim();
    const wakes = [...(cfg.wakeWords || []), ...(cfg.learnedWakes || []), cfg.assistantName, '자비스', '사사리사', '재비스', '싸싸리싸'];
    for (const w of wakes.filter(Boolean)) {
      if (t.toLowerCase().includes(w.toLowerCase())) return true;
    }
    const phonetic = soundAlikePattern(cfg.assistantName || '자비스');
    if (phonetic && new RegExp(phonetic, 'i').test(t)) return true;
    return false;
  }

  stripWake(rawText) {
    const cfg = this.getConfig();
    let t = String(rawText || '').trim();
    const wakes = [...(cfg.wakeWords || []), ...(cfg.learnedWakes || []), cfg.assistantName, '자비스', '사사리사', '재비스', '싸싸리싸'];
    for (const w of wakes.filter(Boolean).sort((a, b) => b.length - a.length)) {
      const escaped = w.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const re = new RegExp(`^(?:헤이|하이|hey|야|어)?\\s*${escaped}(?:님|야|아(?=\\s|$))?[ ,~!?.\\s]*`, 'i');
      if (re.test(t)) {
        t = t.replace(re, '').trim();
      }
    }
    const phonetic = soundAlikePattern(cfg.assistantName || '자비스');
    if (phonetic) {
      const re = new RegExp(`^(?:헤이|하이|hey)?\\s*${phonetic}(?:님|야|아(?=\\s|$))?[ ,~!?.\\s]*`, 'i');
      if (re.test(t)) t = t.replace(re, '').trim();
    }
    return t;
  }

  recordUsage(tokens = 0) {
    const usage = readJson(FILES.usage, {});
    const day = new Date().toISOString().slice(0, 10);
    if (!usage[day]) usage[day] = { calls: 0, tokens: 0 };
    usage[day].calls += 1;
    usage[day].tokens += tokens;
    writeJson(FILES.usage, usage);
  }

  getDailyUsage(days = 7) {
    const usage = readJson(FILES.usage, {});
    const out = [];
    for (let i = days - 1; i >= 0; i--) {
      const d = new Date(Date.now() - i * 86400000).toISOString().slice(0, 10);
      out.push({
        day: d.slice(5),
        calls: usage[d]?.calls || 0,
        tokens: usage[d]?.tokens || 0
      });
    }
    return out;
  }

  takeDueTimers(now = Date.now()) {
    const timers = readJson(FILES.timers, []);
    if (!Array.isArray(timers)) return [];
    const due = timers.filter(t => Number.isFinite(Date.parse(t.fireAt)) && Date.parse(t.fireAt) <= now);
    if (due.length) writeJson(FILES.timers, timers.filter(t => !due.includes(t)));
    return due;
  }

  parseIntent(rawSentence) {
    const cfg = this.getConfig();
    let s = this.stripWake(rawSentence);
    const sq = squash(s);
    if (!s) return { intent: 'awake', title: '● 네, 듣고 있어요', subtitle: '지금 말씀하세요', color: 'cyan' };

    if (this.pendingSend && Date.now() < this.pendingSend.expiresAt) {
      if (/^(응|네|예|어|그래|보내|전송|확인|yes|ok|send)$/.test(sq)) {
        return { intent: 'confirmSend', decision: true };
      }
      if (/^(아니|취소|지워|보내지마|안보내|no|cancel)$/.test(sq)) {
        return { intent: 'confirmSend', decision: false };
      }
    }

    if (this.mode === 'dictation') {
      if (/^(그만|끝|종료|받아쓰기끝|stop)$/.test(sq)) return { intent: 'stopDictation' };
      return { intent: 'dictationText', text: s };
    }

    const aliases = { ...readJson(FILES.sharedAliases, {}), ...readJson(FILES.aliases, {}) };
    if (aliases[sq]) {
      s = aliases[sq];
    }

    // Strip compound browser prefixes ("웹브라우저 켜서 네이버 열어줘" -> "네이버 열어줘")
    const compoundBrowserMatch = s.match(/^(?:웹\s*브라우저|브라우저|크롬\s*브라우저|구글\s*크롬|크롬|엣지|인터넷\s*창|인터넷)(?:을|를)?\s*(?:켜서|열어서|실행해서|띄워서|켜고|열고|에서)\s+(.+)$/i);
    if (compoundBrowserMatch && compoundBrowserMatch[1].trim()) {
      s = compoundBrowserMatch[1].trim();
    }

    const sq2 = squash(s);

    const mediaCommand = classifyMediaControl(s, this.hasMediaContext());
    if (mediaCommand) return mediaCommand;

    if (
      (cfg.greetings || []).some(g => sq2.includes(squash(g))) ||
      /^(아빠왔어|대표님왔어|나왔어|다녀왔어|출근했어|출근|켜|켜져|깨어나|깨어나라|시작|부트화면|부트화면켜|온라인)$/.test(sq2)
    ) {
      return { intent: 'greet' };
    }

    if (/^(방금거취소|방금건취소|되돌려|되돌려줘|원래대로|실행취소|undo)$/.test(sq2)) {
      return { intent: 'undo' };
    }
    if (/^(방금뭐했어|뭐했어|방금뭐한거야|whatdidyoudo)$/.test(sq2)) {
      return { intent: 'whatDid' };
    }

    if (/^(멈춰|스톱|조용|조용히해|시끄러|잠깐|하지마|아니하지마|stop|quiet|hush)$/.test(sq2) || /자비.*없.*조용|아무것도하지말고.*대답하지말|스스로말하거나그러지마/.test(sq2)) {
      return { intent: 'hush' };
    }
    if (/^(됐어|그만|고마워|끝|잘자|쉬어|수고했어|thanks|bye)$/.test(sq2)) {
      return { intent: 'bye' };
    }
    if (/^(할말있어|내말잘들어|궁금한게있는데|물어볼게있는데)$/.test(sq2)) {
      return { intent: 'awake', title: '● 경청 중 · 네, 대표님 말씀하세요', subtitle: '끝까지 편하게 말씀해 주세요', color: 'cyan' };
    }

    if (/받아써|받아적어|받아쓰기|타이핑모드|dictation/.test(sq2)) {
      return { intent: 'dictation' };
    }

    if (/^(코딩상태|코덱스상태|코딩작업상태)$/.test(sq2)) return { intent: 'codingStatus' };
    const codingMatch = s.match(/^(?:코덱스|codex)(?:에게|한테|로)\s*(.+)$/i);
    if (codingMatch) return { intent: 'codingTask', task: codingMatch[1].trim() };

    if (/^(?:선택한|선택된)?메일(?:을)?읽어(?:줘)?$/.test(sq2)) return { intent: 'readSelectedMail' };
    if (/^(?:선택한|선택된)?메일(?:의)?답장(?:을)?(?:작성해|써|써줘|만들어줘)$/.test(sq2)) return { intent: 'draftMailReply' };
    if (/^메일답장(?:을)?입력해(?:줘)?$/.test(sq2)) return { intent: 'insertMailReply' };

    const makeRoutineMatch = s.match(/^(?:앞으로|이제부터)?\s*['"「]?(.+?)['"」]?\s*(?:이?라고\s*하면|하면)\s*(.+)$/);
    if (makeRoutineMatch && (s.startsWith('앞으로') || s.startsWith('이제부터') || s.includes('라고 하면'))) {
      const rName = makeRoutineMatch[1].trim();
      const steps = makeRoutineMatch[2].split(/\s*(?:하고|그리고|한\s*다음에|,)\s*/).filter(Boolean);
      return { intent: 'makeRoutine', name: rName, steps };
    }

    const routines = readJson(FILES.routines, {});
    for (const [rName, steps] of Object.entries(routines)) {
      if (sq2 === squash(rName) || sq2 === squash(rName + '해') || sq2 === squash(rName + '루틴')) {
        return { intent: 'routine', name: rName, steps };
      }
    }

    const kakaoMatch = s.match(/카(?:카오)?톡\s*(?:(\S+?)\s*(?:한테|에게|께)\s+)?(.+?)\s*(?:이?라고|하고)\s*(답장|보내|전송|입력|써)/);
    if (kakaoMatch) {
      const to = (kakaoMatch[1] || '현재 대화방').replace(/님$/, '');
      const text = kakaoMatch[2].trim();
      const sendNow = !['입력', '써'].includes(kakaoMatch[3]);
      return { intent: sendNow ? 'kakaoReply' : 'kakaoType', to, text };
    }

    const nthMap = { '1': 1, '일': 1, '첫': 1, '2': 2, '이': 2, '두': 2, '3': 3, '삼': 3, '세': 3, '4': 4, '사': 4, '네': 4, '5': 5, '오': 5, '다섯': 5 };
    const ytNthMatch = s.match(/^(1|2|3|4|5|일|이|삼|사|오|첫|두|세|네|다섯)\s*(?:번|번째)?\s*(?:영상|거)?\s*(?:틀어|재생|열어|보여|봐)?$/);
    if (ytNthMatch && nthMap[ytNthMatch[1]]) {
      return { intent: 'youtubeNth', index: nthMap[ytNthMatch[1]] };
    }

    // 9-B) Daily Priority Briefing (Image 2: "오늘 할 일 정리해 줘" / "오늘 일정 알려줘")
    if (/오늘.*(할일|해야할일|일정|우선순위|업무).*?(정리|알려|보여|브리핑)/.test(sq2) || /^(오늘할일|오늘일정|업무브리핑)$/.test(sq2)) {
      return { intent: 'dailyBriefing' };
    }

    // 10) YouTube search / Music play ("유튜브에서 로파이 검색해", "클래식 음악 좀 틀어 줘", "음악 틀어줘", "로파이 틀어줘", "버즈라는 가수의 노래를 틀어 달라고")
    if (/^(?:음악|노래|브금|플레이리스트|플리)(?:좀|를|을)?(?:틀어|재생|켜|들려)(?:줘|봐|주세요|달라고|줄래)?$/.test(sq2)) {
      return { intent: 'youtubeAutoPlay', query: '집중하기 좋은 로파이 음악 플레이리스트', spokenLabel: '로파이 음악' };
    }
    const genreMusicMatch = s.match(/^(로파이|클래식|재즈|피아노|팝송|잔잔한|신나는|집중|카페|공부)\s*(?:음악|노래|브금|플리|플레이리스트)?(?:를|을)?\s*(?:좀\s*)?(?:틀어|재생해|켜|들려)(?:\s*줘|\s*봐|\s*주세요|\s*달라고|\s*줄래)?$/i);
    if (genreMusicMatch) {
      const genre = genreMusicMatch[1].trim();
      return { intent: 'youtubeAutoPlay', query: `${genre} 음악 플레이리스트`, spokenLabel: `${genre} 음악` };
    }
    const musicPlayMatch = s.match(/^(?:유튜브(?:에서)?\s*)?(.+?)\s*(?:음악|노래|곡|브금|플리|플레이리스트)(?:를|을)?\s*(?:좀\s*)?(?:틀어|재생해|재생|켜|들려)(?:\s*줘|\s*봐|\s*주세요|\s*달라고|\s*줄래)?$/);
    if (musicPlayMatch) {
      const artistOrGenre = musicPlayMatch[1]
        .replace(/(?:이?라는\s*가수의|이?라는\s*가수|가수의|가수)\s*/g, ' ')
        .replace(/\s+/g, ' ')
        .trim();
      const q = `${artistOrGenre} 노래 모음 플레이리스트`;
      return { intent: 'youtubeAutoPlay', query: q, spokenLabel: `${artistOrGenre} 노래` };
    }

    const ytMatch = s.match(/(?:유튜브(?:에서)?\s*(.+?)\s*(?:검색|찾아|틀어|재생|보여|들려).*|(.+?)\s*(?:유튜브에서\s*찾아|노래(?:를|을)?\s*틀어|영상(?:을|를)?\s*틀어).*)/);
    if (ytMatch) {
      const q = (ytMatch[1] || ytMatch[2] || '')
        .replace(/(?:이?라는\s*가수의|이?라는\s*가수)\s*/g, ' ')
        .replace(/(?:를|을|해줘|해봐|줘|좀|달라고|줄래)$/g, '')
        .trim();
      if (/틀어|재생|켜|들려/.test(s)) {
        return { intent: 'youtubeAutoPlay', query: q, spokenLabel: q };
      }
      return { intent: 'youtube', query: q };
    }

    const shopMatch = s.match(/^(쿠팡|네이버|구글|다나와|아마존)(?:에서)?\s*(.+?)\s*(?:찾아|검색|보여|열어|최저가).*$/);
    if (shopMatch) {
      const site = shopMatch[1];
      const q = shopMatch[2].trim();
      const urls = {
        '쿠팡': `https://www.coupang.com/np/search?q=${encodeURIComponent(q)}`,
        '네이버': `https://search.naver.com/search.naver?query=${encodeURIComponent(q)}`,
        '구글': `https://www.google.com/search?q=${encodeURIComponent(q)}`,
        '다나와': `https://search.danawa.com/dsearch.php?k1=${encodeURIComponent(q)}`,
        '아마존': `https://www.amazon.com/s?k=${encodeURIComponent(q)}`
      };
      return { intent: 'web', label: site, query: q, url: urls[site] };
    }

    // Generic Web Search ("파이썬 강좌 검색해줘", "오늘 주요 뉴스 검색해")
    const genericSearchMatch = s.match(/^(.+?)\s*(?:검색해|웹검색해|구글링해|찾아봐)(?:줘|봐|주세요)?$/);
    if (genericSearchMatch && !/유튜브|파일|폴더|터미널|깃|git/i.test(genericSearchMatch[1])) {
      const q = genericSearchMatch[1].replace(/(?:좀|를|을)$/g, '').trim();
      if (q) {
        return { intent: 'web', label: '구글', query: q, url: `https://www.google.com/search?q=${encodeURIComponent(q)}` };
      }
    }

    const mapMatch = s.match(/^(.+?)\s*(?:가는\s*길|길\s*찾기|지도\s*보여).*$/);
    if (mapMatch) {
      return { intent: 'map', place: mapMatch[1].trim() };
    }


    if (/창다꺼|열어둔거다꺼|다닫아|모두닫아/.test(sq2)) {
      return { intent: 'closeOpened' };
    }

    if (/음소거|뮤트|mute/.test(sq2)) {
      return { intent: 'volume', action: 'mute' };
    }
    if (/소리키워|소리올려|볼륨올려|크게/.test(sq2)) {
      return { intent: 'volume', action: 'up' };
    }
    if (/소리줄여|소리낮춰|볼륨줄여|작게/.test(sq2)) {
      return { intent: 'volume', action: 'down' };
    }
    if (/다음영상|다음곡|스킵|next/.test(sq2)) {
      return { intent: 'mediaNext' };
    }
    if (/^(재생일시정지|일시정지재생|재생일시정지토글|음악토글|플레이포즈)$/.test(sq2)) {
      return { intent: 'togglePlayPause' };
    }
    if (/일시정지|영상멈춰|다시틀어|재생해/.test(sq2)) {
      return { intent: 'media', play: /다시|재생/.test(sq2) };
    }

    if (/몇시|지금시간|현재시간|whattime/.test(sq2)) {
      return { intent: 'clock', date: false };
    }
    if (/며칠|무슨요일|오늘날짜|whatdate/.test(sq2)) {
      return { intent: 'clock', date: true };
    }

    const timerMatch = s.match(/^(?:(.+?)\s*)?(\d+)\s*(분|초)\s*(?:타이머|뒤에\s*알려|알람).*$/);
    if (timerMatch) {
      const label = (timerMatch[1] || '타이머').trim();
      const num = parseInt(timerMatch[2], 10);
      const sec = timerMatch[3] === '분' ? num * 60 : num;
      return { intent: 'timer', seconds: sec, label };
    }

    const remMatch = s.match(/^(?:기억해|메모해|외워)\s*[:：]?\s*(.+)$/);
    if (remMatch) {
      return { intent: 'remember', note: remMatch[1].trim() };
    }

    if (/존댓말로바꿔|정중하게바꿔|영어로바꿔|영어로번역해|다듬어줘/.test(sq2)) {
      return { intent: 'rewrite', style: s };
    }

    // Check BeausCreators CHLGD / Terminal / Agent Collaboration Orders before generic app match
    const bscOrder = sharedBscBridge.classifySpecialOrder(s);
    if (bscOrder) {
      return { intent: 'bscSpecialOrder', bscOrder };
    }

    const SITE_MAP = {
      '유튜브': { label: '유튜브', url: 'https://www.youtube.com' },
      'youtube': { label: '유튜브', url: 'https://www.youtube.com' },
      '네이버': { label: '네이버', url: 'https://www.naver.com' },
      'naver': { label: '네이버', url: 'https://www.naver.com' },
      '구글': { label: '구글', url: 'https://www.google.com' },
      'google': { label: '구글', url: 'https://www.google.com' },
      '깃허브': { label: 'GitHub', url: 'https://github.com/Leeseunjib/BSC_Jarvis_Sovereign_HUD' },
      'github': { label: 'GitHub', url: 'https://github.com/Leeseunjib/BSC_Jarvis_Sovereign_HUD' },
      '쿠팡': { label: '쿠팡', url: 'https://www.coupang.com' },
      '챗지피티': { label: 'ChatGPT', url: 'https://chatgpt.com' },
      '제미나이웹': { label: 'Gemini', url: 'https://gemini.google.com' }
    };

    const appMatch = s.match(/^(.+?)\s*(?:를|을|좀)?\s*(?:열어|켜|실행해|틀어|보여|접속해)(?:줘|봐|주세요)?$/);
    if (appMatch) {
      const targetApp = appMatch[1].replace(/(?:를|을|좀|사이트|페이지|창)$/g, '').trim();
      const sqTarget = squash(targetApp);
      const siteHit = SITE_MAP[targetApp] || SITE_MAP[sqTarget];
      if (siteHit) {
        return { intent: 'web', label: siteHit.label, query: '메인 화면', url: siteHit.url };
      }
      if (/^https?:\/\//i.test(targetApp)) {
        return { intent: 'web', label: '웹사이트', query: targetApp, url: targetApp };
      }
      return { intent: 'openApp', spoken: targetApp };
    }

    if (/^(유튜브|유튜브를|유튜브좀)$/.test(sq2)) {
      return { intent: 'web', label: '유튜브', query: '홈', url: 'https://www.youtube.com' };
    }

    const typeMatch = s.match(/^(.+?)\s*(?:이?라고)\s*(?:써|쳐|입력해|타이핑해|적어)(?:줘)?$/);
    if (typeMatch) {
      return { intent: 'type', text: typeMatch[1].trim() };
    }

    return { intent: 'brain', prompt: s };
  }

  async execute(rawSentence, executeOsActions = true) {
    const cfg = this.getConfig();
    const parsed = this.parseIntent(rawSentence);
    this.recordUsage(0);

    switch (parsed.intent) {
      case 'bscSpecialOrder': {
        const ord = parsed.bscOrder;
        if (ord.kind === 'terminal') {
          const taskState = executeOsActions
            ? sharedTerminalManager.runTask({
                title: ord.title,
                command: ord.command,
                cwd: ord.cwd,
                chlgdPhase: ord.chlgdPhase,
                partnerAgent: ord.partnerAgent
              })
            : { id: 'dry-run', title: ord.title, status: 'running' };
          return {
            intent: 'bscSpecialOrder',
            taskState,
            hudColor: 'lime',
            hudTitle: `⚡ ${ord.title}`,
            hudSubtitle: ord.spoken,
            heard: rawSentence,
            spoken: ord.spoken
          };
        }
        return {
          intent: 'bscSpecialOrder',
          hudColor: 'cyan',
          hudTitle: ord.hudTitle,
          hudSubtitle: ord.hudSubtitle,
          heard: rawSentence,
          spoken: ord.spoken
        };
      }

      case 'awake':
        return {
          intent: 'awake',
          hudColor: 'cyan',
          hudTitle: `● 경청 중 · 네, ${cfg.userName || '대표님'} 말씀하세요`,
          hudSubtitle: '지금 이어서 끝까지 편하게 말씀하세요 (45초간 경청 모드 유지)',
          heard: rawSentence,
          spoken: `네, ${cfg.userName || '대표님'}. 말씀하세요.`
        };

      case 'greet': {
        const sys = this.getSystemTelemetry();
        const greetLine = (cfg.greetingText || '어서 오십시오, {user}.').replace('{user}', cfg.userName || '대표님');
        return {
          intent: 'greet',
          bootScene: true,
          bootStyle: cfg.bootStyle || 'reactor',
          telemetry: sys,
          hudColor: 'cyan',
          hudTitle: `◉ ${(cfg.assistantName || '자비스').toUpperCase()} · ONLINE`,
          hudSubtitle: `${greetLine} (RAM ${sys.usedMemGB}/${sys.totalMemGB}GB · Disk ${sys.diskFreeGB}GB 여유)`,
          heard: rawSentence,
          spoken: greetLine
        };
      }

      case 'confirmSend': {
        const pending = this.pendingSend;
        this.pendingSend = null;
        if (pending.kind === 'coding') {
          if (!parsed.decision) return { intent: 'confirmSend', hudColor: 'amber', hudTitle: '코딩 작업 취소', heard: rawSentence, spoken: '코딩 작업을 시작하지 않았습니다.' };
          if (!executeOsActions) return { intent: 'confirmSend', hudColor: 'lime', hudTitle: '코딩 작업 확인됨 (시험)', heard: rawSentence, spoken: '시험 모드에서는 코딩 작업을 실행하지 않습니다.' };
          const job = this.codingJobs.start({ task: pending.text, workspace: pending.workspace });
          return { intent: 'confirmSend', hudColor: 'cyan', hudTitle: '코덱스 작업 시작', hudSubtitle: `작업 폴더: ${job.workspace}`, heard: rawSentence, spoken: '코덱스 작업을 시작했습니다. 코딩 상태라고 물어보시면 진행 상황을 알려 드리겠습니다.' };
        }
        if (pending.kind === 'rewrite') {
          if (!parsed.decision) return { intent: 'confirmSend', hudColor: 'amber', hudTitle: '글 수정 취소', heard: rawSentence, spoken: '선택한 글을 변경하지 않았습니다.' };
          if (executeOsActions) await this.interact({ op: 'replace', target: pending.target, text: pending.text });
          return { intent: 'confirmSend', hudColor: 'lime', hudTitle: '선택한 글 바꾸기 요청 완료', heard: rawSentence, spoken: '선택한 글을 바꾸었습니다. 결과를 확인해 주세요.' };
        }
        if (pending.kind === 'kakao') {
          if (!parsed.decision) return { intent: 'confirmSend', hudColor: 'amber', hudTitle: '메시지 전송 취소', heard: rawSentence, spoken: '메시지를 보내지 않았습니다.' };
          if (!executeOsActions) return { intent: 'confirmSend', hudColor: 'cyan', hudTitle: '메시지 전송 시험', heard: rawSentence, spoken: '시험 모드에서는 전송하지 않습니다.' };
          await this.interact({ op: 'kakao', target: pending.target, recipient: pending.to, text: pending.text, send: true });
          return { intent: 'confirmSend', hudColor: 'cyan', hudTitle: '카카오톡 전송 요청 · 수신 확인 필요', heard: rawSentence, spoken: '카카오톡 전송을 요청하고 입력란이 비워진 것을 확인했습니다. 수신 여부는 대화방에서 확인해 주세요.' };
        }
        if (parsed.decision) {
          return {
            intent: 'confirmSend',
            hudColor: 'amber',
            hudTitle: '직접 대화방과 수신자를 확인해 주세요',
            hudSubtitle: `「${pending.text}」 — 자동 전송은 지원하지 않습니다.`,
            heard: rawSentence,
            spoken: '수신자를 확인할 수 없어 자동 전송하지 않았습니다. 카카오톡에서 직접 보내 주세요.'
          };
        }
        return {
          intent: 'confirmSend',
          hudColor: 'amber',
          hudTitle: '✕ 전송 취소됨 (보내기 전 미리 보기 보호)',
          hudSubtitle: '대기 중인 메시지를 취소했습니다. 입력하거나 전송하지 않았습니다.',
          heard: rawSentence,
          spoken: '전송을 취소했습니다.'
        };
      }

      case 'codingTask': {
        const workspace = cfg.codingWorkspace || ROOT_DIR;
        this.pendingSend = { kind: 'coding', text: parsed.task, workspace, expiresAt: Date.now() + 30000 };
        return { intent: 'codingTask', awaitingConfirm: true, pendingSend: this.pendingSend, hudColor: 'amber', hudTitle: '코덱스 코딩 작업 확인', hudSubtitle: `${workspace} · ${parsed.task}`, heard: rawSentence, spoken: `코덱스에 ${parsed.task} 작업을 맡길까요? 확인하면 ${workspace} 폴더에서 실행합니다.` };
      }

      case 'codingStatus': {
        const job = this.codingJobs.latest();
        const msg = !job ? '아직 코딩 작업이 없습니다.' : job.status === 'running' ? '코덱스 작업이 진행 중입니다.' : job.status === 'completed' ? '코덱스 작업이 끝났습니다. 결과를 확인해 주세요.' : '코덱스 작업이 실패했습니다. 오류 내용을 확인해 주세요.';
        return { intent: 'codingStatus', hudColor: job && job.status === 'failed' ? 'amber' : 'cyan', hudTitle: msg, hudSubtitle: job ? (job.status === 'failed' ? job.error.slice(-1200) : job.output.slice(-1200)) : '', heard: rawSentence, spoken: msg };
      }

      case 'readSelectedMail': {
        if (!executeOsActions) return { intent: 'readSelectedMail', hudColor: 'cyan', hudTitle: '선택한 메일 읽기 시험', heard: rawSentence, spoken: '시험 모드입니다.' };
        const { target } = await this.interact({ op: 'capture' });
        if (!/^(chrome|msedge|firefox|outlook|olk|thunderbird)$/i.test(target.process) || !/(gmail|outlook|메일|mail)/i.test(target.title)) throw new Error('메일 화면에서 읽을 내용을 선택한 뒤 다시 요청해 주세요.');
        if (!target.selection) throw new Error('읽을 메일 내용을 선택해 주세요.');
        return { intent: 'readSelectedMail', hudColor: 'cyan', hudTitle: '선택한 메일 내용', hudSubtitle: target.selection.slice(0, 1200), heard: rawSentence, spoken: target.selection.slice(0, 1000) };
      }

      case 'draftMailReply': {
        if (!executeOsActions) return { intent: 'draftMailReply', hudColor: 'amber', hudTitle: '메일 선택 필요', heard: rawSentence, spoken: '메일 내용을 선택해 주세요.' };
        const { target } = await this.interact({ op: 'capture' });
        if (!/^(chrome|msedge|firefox|outlook|olk|thunderbird)$/i.test(target.process) || !/(gmail|outlook|메일|mail)/i.test(target.title) || !target.selection) throw new Error('메일 화면에서 답장할 메일 내용을 선택해 주세요.');
        const draft = cleanLocalDraft(await this.rewriteLocal(`다음 메일에 대한 간결하고 정중한 한국어 답장 초안을 작성해 주세요. 사실을 지어내지 마세요.\n\n${target.selection}`, '답장 내용만 출력하세요. 보내기, 약속이나 첨부는 실제로 수행하지 않습니다.', cfg.localRewriteModel || 'gemma4:e4b', 60000));
        if (!draft) throw new Error('로컬 모델을 사용할 수 없어 메일 초안을 만들지 못했습니다.');
        this.mailDraft = draft;
        return { intent: 'draftMailReply', hudColor: 'cyan', hudTitle: '메일 답장 초안', hudSubtitle: draft, heard: rawSentence, spoken: '답장 초안을 만들었습니다. 답장 입력란을 선택한 뒤 메일 답장 입력해라고 말씀해 주세요.' };
      }

      case 'insertMailReply': {
        if (!this.mailDraft) throw new Error('먼저 선택한 메일의 답장 초안을 만들어 주세요.');
        if (executeOsActions) {
          const { target } = await this.interact({ op: 'capture' });
          if (!/^(chrome|msedge|firefox|outlook|olk|thunderbird)$/i.test(target.process) || !/(gmail|outlook|메일|mail)/i.test(target.title) || !target.editable || String(target.value || '').trim()) throw new Error('메일 화면의 빈 답장 입력란을 선택해 주세요.');
          await this.interact({ op: 'append', target, text: this.mailDraft });
        }
        this.mailDraft = null;
        return { intent: 'insertMailReply', hudColor: 'cyan', hudTitle: '메일 답장 초안 입력', heard: rawSentence, spoken: '답장 초안을 입력했습니다. 내용을 확인한 뒤 직접 보내 주세요.' };
      }

      case 'kakaoReply':
      case 'kakaoType': {
        let target = null;
        if (executeOsActions) {
          ({ target } = await this.interact({ op: 'capture' }));
          const normalize = s => String(s || '').replace(/\s/g, '').replace(/님$/, '').toLowerCase();
          if (String(target.process).toLowerCase() !== 'kakaotalk' || !target.editable) throw new Error('카카오톡 대화창의 빈 입력란을 선택해 주세요.');
          if (parsed.to !== '현재 대화방' && normalize(target.title) !== normalize(parsed.to)) throw new Error('현재 대화방 이름이 요청한 수신자와 일치하지 않습니다.');
          if (String(target.value || '').trim()) throw new Error('기존 입력 내용이 있어 메시지를 쓰지 않았습니다.');
        }
        if (parsed.intent === 'kakaoReply') {
          this.pendingSend = {
            kind: 'kakao',
            to: target && parsed.to === '현재 대화방' ? target.title : parsed.to,
            text: parsed.text,
            target,
            expiresAt: Date.now() + 60000
          };
          return {
            intent: 'kakaoReply',
            awaitingConfirm: true,
            pendingSend: { kind: 'kakao', to: this.pendingSend.to, text: parsed.text, expiresAt: this.pendingSend.expiresAt },
            hudColor: 'amber',
            hudTitle: `💬 [${this.pendingSend.to}] 메시지 확인`,
            hudSubtitle: `「${parsed.text}」 — 대화방을 확인한 뒤 전송할까요?`,
            heard: rawSentence,
            spoken: `${this.pendingSend.to} 대화방에 「${parsed.text}」라고 보낼까요?`
          };
        }
        if (executeOsActions) await this.interact({ op: 'kakao', target, recipient: parsed.to, text: parsed.text, send: false });
        return {
          intent: parsed.intent,
          hudColor: 'cyan',
          hudTitle: `💬 카카오톡 ${parsed.to} 입력`,
          hudSubtitle: `「${parsed.text}」`,
          heard: rawSentence,
          spoken: '대화방 입력란에 메시지를 준비했습니다. 전송하지 않았습니다.'
        };
      }

      case 'dailyBriefing': {
        const promptText = '오늘 제가 해야 할 일을 기존 일정과 작업 기록에서 확인할 수 있는 내용을 바탕으로 정리해, 우선순위 순서대로 알려 주세요.';
        const claudeUrl = `https://claude.ai/new?q=${encodeURIComponent(promptText)}`;
        if (executeOsActions) await WindowsHands.openUrl(claudeUrl);
        this.openedUrls.push(claudeUrl);
        this.lastDone = {
          said: rawSentence,
          description: '오늘 해야 할 일 우선순위 브리핑 (Claude 연동)',
          undoType: 'url',
          undoTarget: claudeUrl
        };
        return {
          intent: 'dailyBriefing',
          hudColor: 'cyan',
          hudTitle: '○ 받아 적는 중 · 우선순위 업무 브리핑',
          hudSubtitle: promptText,
          heard: rawSentence,
          spoken: `${cfg.userName || '대표님'}, 오늘 우선순위 일정을 정리해 화면에 띄워 드렸습니다.`
        };
      }

      case 'youtubeAutoPlay': {
        const list = await this.searchYouTube(parsed.query);
        this.ytList = list;
        const firstUrl = list[0] ? list[0].url : `https://www.youtube.com/results?search_query=${encodeURIComponent(parsed.query)}`;
        const firstTitle = list[0] ? list[0].title : parsed.spokenLabel;
        if (executeOsActions && list[0]) {
          const browser = await WindowsHands.openMediaUrl(firstUrl);
          this.rememberMedia(firstUrl, firstTitle, 'opened', browser);
        } else if (executeOsActions) await WindowsHands.openUrl(firstUrl);
        this.openedUrls.push(firstUrl);
        this.lastDone = {
          said: rawSentence,
          description: `${parsed.spokenLabel} 재생 (${firstTitle})`,
          undoType: 'url',
          undoTarget: firstUrl
        };
        return {
          intent: 'youtubeAutoPlay',
          ytList: list,
          hudColor: 'cyan',
          hudTitle: `○ 받아 적는 중 · 실행 완료`,
          hudSubtitle: `${rawSentence} ➔ ▶ ${firstTitle}`,
          heard: rawSentence,
          spoken: list[0] ? `${parsed.spokenLabel} 영상을 열었습니다. 음악 꺼줘라고 하시면 멈춥니다.` : '유튜브 검색 화면을 열었습니다. 검색된 영상의 재생 버튼을 눌러 주세요.'
        };
      }

      case 'youtube': {
        const list = await this.searchYouTube(parsed.query);
        this.ytList = list;
        const searchPageUrl = `https://www.youtube.com/results?search_query=${encodeURIComponent(parsed.query)}`;
        const wantsDirectPlay = /틀어|재생|켜|들려/.test(rawSentence);
        const targetUrl = (wantsDirectPlay && list[0]) ? list[0].url : (list[0] ? list[0].url : searchPageUrl);
        if (executeOsActions && list[0]) {
          const browser = await WindowsHands.openMediaUrl(targetUrl);
          this.rememberMedia(targetUrl, list[0].title, 'opened', browser);
        } else if (executeOsActions) await WindowsHands.openUrl(targetUrl);
        this.openedUrls.push(targetUrl);
        if (list.length === 0) {
          return {
            intent: 'youtube',
            hudColor: 'cyan',
            hudTitle: `▶ 유튜브 화면 열기: ${parsed.query}`,
            hudSubtitle: '바탕화면 브라우저에서 유튜브 화면을 열었습니다.',
            heard: rawSentence,
            spoken: `바탕화면에 유튜브 ${parsed.query} 화면을 띄워 드렸습니다.`
          };
        }
        const previewLines = list.map(v => `${v.index}번. ${v.title}`).join(' · ');
        this.lastDone = {
          said: rawSentence,
          description: `유튜브에서 「${parsed.query}」 재생 (${list[0].title})`,
          undoType: 'url',
          undoTarget: targetUrl
        };
        return {
          intent: 'youtube',
          ytList: list,
          hudColor: 'lime',
          hudTitle: `▶ 바탕화면 유튜브 실행: ${list[0].title}`,
          hudSubtitle: `다른 영상을 원하시면 「2번 틀어」~「5번 틀어」, 끄시려면 「음악 꺼」라고 말씀하세요.`,
          heard: rawSentence,
          spoken: `유튜브 ${parsed.query} 영상을 열었습니다. 끄시려면 음악 꺼줘라고 말씀해 주세요.`
        };
      }

      case 'stopMusic':
      case 'resumeMusic': {
        const pause = parsed.intent === 'stopMusic';
        if (!executeOsActions) return { intent: parsed.intent, hudColor: 'cyan', hudTitle: pause ? '음악 정지 시험' : '음악 재생 시험', heard: rawSentence, spoken: '시험 모드에서는 재생 상태를 변경하지 않습니다.' };
        const outcome = await this.mediaControl({ action: pause ? 'pause' : 'play', scope: parsed.scope || 'any', preferredSource: this.mediaContext && this.mediaContext.source, targetProcessId: this.hasMediaContext() ? this.mediaContext.processId : null });
        if (outcome.state === 'none') {
          if (this.mediaContext) {
            this.mediaContext.state = 'none';
            this.mediaContext.updatedAt = Date.now();
            writeJson(FILES.media, this.mediaContext);
          }
          return { intent: parsed.intent, hudColor: 'amber', hudTitle: '제어할 재생 항목을 찾지 못했습니다', hudSubtitle: '유튜브 영상이나 음악 앱이 열려 있는지 확인해 주세요.', heard: rawSentence, spoken: '제어할 재생 항목을 찾지 못했습니다.', media: outcome };
        }
        const expected = pause ? ['Paused', 'Stopped', 'Closed'] : ['Playing'];
        if (!outcome.verified || !expected.includes(outcome.state)) throw new Error('요청한 재생 상태를 확인하지 못했습니다.');
        if (this.mediaContext) {
          Object.assign(this.mediaContext, { state: outcome.state, source: outcome.source, updatedAt: Date.now() });
          writeJson(FILES.media, this.mediaContext);
        }
        return {
          intent: parsed.intent,
          hudColor: 'lime',
          hudTitle: pause ? '⏸ 음악 정지 확인' : '▶ 음악 재생 확인',
          hudSubtitle: pause ? '재생 앱이 멈춘 것을 확인했습니다.' : '재생 앱이 다시 재생 중인 것을 확인했습니다.',
          heard: rawSentence,
          spoken: pause ? '음악을 멈췄습니다.' : '음악을 다시 재생했습니다.',
          media: outcome
        };
      }

      case 'youtubeNth': {
        const pick = this.ytList[parsed.index - 1];
        if (!pick) {
          return {
            intent: 'youtubeNth',
            hudColor: 'amber',
            hudTitle: '⚠︎ 먼저 유튜브 검색을 말씀해 주세요',
            hudSubtitle: '예: 「유튜브에서 로파이 검색해」 한 뒤 「1번 틀어」',
            heard: rawSentence,
            spoken: '먼저 유튜브에서 찾으실 영상을 말씀해 주세요.'
          };
        }
        if (executeOsActions) {
          const browser = await WindowsHands.openMediaUrl(pick.url);
          this.rememberMedia(pick.url, pick.title, 'opened', browser);
        }
        this.openedUrls.push(pick.url);
        this.lastDone = {
          said: rawSentence,
          description: `유튜브 ${parsed.index}번 「${pick.title}」 재생`,
          undoType: 'url',
          undoTarget: pick.url
        };
        return {
          intent: 'youtubeNth',
          pickedVideo: pick,
          hudColor: 'lime',
          hudTitle: `▶ ${parsed.index}번 재생: ${pick.title}`,
          hudSubtitle: pick.url,
          heard: rawSentence,
          spoken: `${parsed.index}번 영상을 재생합니다.`
        };
      }

      case 'web': {
        if (executeOsActions) await WindowsHands.openUrl(parsed.url);
        this.openedUrls.push(parsed.url);
        this.lastSite = { label: parsed.label, url: parsed.url };
        this.lastDone = {
          said: rawSentence,
          description: `${parsed.label}에서 「${parsed.query}」 검색`,
          undoType: 'url',
          undoTarget: parsed.url
        };
        return {
          intent: 'web',
          hudColor: 'cyan',
          hudTitle: `🌐 ${parsed.label} 검색: ${parsed.query}`,
          hudSubtitle: parsed.url,
          heard: rawSentence,
          spoken: `${parsed.label}에서 ${parsed.query} 검색 결과를 열었습니다.`
        };
      }

      case 'map': {
        const url = `https://map.naver.com/p/search/${encodeURIComponent(parsed.place)}`;
        if (executeOsActions) await WindowsHands.openUrl(url);
        this.openedUrls.push(url);
        this.lastDone = {
          said: rawSentence,
          description: `네이버 지도 「${parsed.place}」 길찾기`,
          undoType: 'url',
          undoTarget: url
        };
        return {
          intent: 'map',
          hudColor: 'cyan',
          hudTitle: `🗺 네이버 지도: ${parsed.place}`,
          hudSubtitle: url,
          heard: rawSentence,
          spoken: `네이버 지도에서 ${parsed.place} 가는 길을 열었습니다.`
        };
      }

      case 'volume': {
        if (executeOsActions) await WindowsHands.volume(parsed.action);
        const label = parsed.action === 'mute' ? '음소거 전환' : parsed.action === 'up' ? '볼륨 높임' : '볼륨 낮춤';
        const inverse = parsed.action === 'mute' ? 'mute' : parsed.action === 'up' ? 'down' : 'up';
        this.lastDone = {
          said: rawSentence,
          description: `시스템 소리 ${label}`,
          undoType: 'volume',
          undoTarget: inverse
        };
        return {
          intent: 'volume',
          hudColor: 'cyan',
          hudTitle: `🔊 ${label}`,
          hudSubtitle: 'Windows 시스템 오디오 볼륨을 즉시 조절했습니다 (0 토큰).',
          heard: rawSentence,
          spoken: `${label} 완료했습니다.`
        };
      }

      case 'togglePlayPause': {
        if (!this.hasMediaContext()) {
          return await this.execute('유튜브에서 로파이 음악 틀어줘', executeOsActions);
        }
        if (!executeOsActions) {
          return { intent: 'togglePlayPause', hudColor: 'cyan', hudTitle: '⏯ 재생/일시정지 전환 시험', heard: rawSentence, spoken: '시험 모드에서는 재생 상태를 변경하지 않습니다.' };
        }
        const wantPlay = this.mediaContext && this.mediaContext.state === 'Paused';
        try {
          const outcome = await this.mediaControl({
            action: wantPlay ? 'play' : 'pause',
            scope: 'browser',
            preferredSource: this.mediaContext && this.mediaContext.source,
            targetProcessId: this.mediaContext && this.mediaContext.processId
          });
          if (outcome.state === 'none') {
            const reopenUrl = (this.mediaContext && this.mediaContext.url) || '';
            if (reopenUrl) {
              const browser = await WindowsHands.openMediaUrl(reopenUrl);
              const title = (this.mediaContext && this.mediaContext.title) || 'YouTube 음악';
              this.rememberMedia(reopenUrl, title, 'Playing', browser);
              return {
                intent: 'togglePlayPause',
                hudColor: 'lime',
                hudTitle: `▶ 음악 다시 재생: ${title}`,
                hudSubtitle: reopenUrl,
                heard: rawSentence,
                spoken: `${title} 음악을 다시 재생합니다.`
              };
            }
            return await this.execute('유튜브에서 로파이 음악 틀어줘', executeOsActions);
          }
          if (this.mediaContext) {
            this.mediaContext.state = wantPlay ? 'Playing' : 'Paused';
            this.mediaContext.updatedAt = Date.now();
            writeJson(FILES.media, this.mediaContext);
          }
          return {
            intent: 'togglePlayPause',
            hudColor: 'lime',
            hudTitle: wantPlay ? '▶ 음악 재생 확인' : '⏸ 음악 일시정지 확인',
            hudSubtitle: wantPlay ? '미디어를 다시 재생했습니다.' : '미디어를 일시정지했습니다. 다시 누르시면 이어서 재생됩니다.',
            heard: rawSentence,
            spoken: wantPlay ? '음악을 다시 재생했습니다.' : '음악을 일시정지했습니다.'
          };
        } catch (_) {
          if (wantPlay && this.mediaContext && this.mediaContext.url) {
            const browser = await WindowsHands.openMediaUrl(this.mediaContext.url);
            this.rememberMedia(this.mediaContext.url, this.mediaContext.title || 'YouTube 음악', 'Playing', browser);
            return {
              intent: 'togglePlayPause',
              hudColor: 'lime',
              hudTitle: `▶ 음악 다시 재생: ${this.mediaContext.title || 'YouTube 음악'}`,
              hudSubtitle: this.mediaContext.url,
              heard: rawSentence,
              spoken: '음악을 다시 재생했습니다.'
            };
          }
          return await this.execute('유튜브에서 로파이 음악 틀어줘', executeOsActions);
        }
      }

      case 'media':
      case 'mediaNext': {
        if (!executeOsActions) return { intent: parsed.intent, hudColor: 'cyan', hudTitle: '미디어 제어 시험', heard: rawSentence, spoken: '시험 모드에서는 재생 상태를 변경하지 않습니다.' };
        const next = parsed.intent === 'mediaNext';
        if (next) {
          if (!this.ytList || this.ytList.length === 0) {
            this.ytList = await this.searchYouTube('집중하기 좋은 로파이 음악 플레이리스트');
          }
          if (this.ytList && this.ytList.length > 0) {
            this.ytCursor = ((typeof this.ytCursor === 'number' ? this.ytCursor : 0) + 1) % this.ytList.length;
            const pick = this.ytList[this.ytCursor] || this.ytList[0];
            const browser = await WindowsHands.openMediaUrl(pick.url);
            this.rememberMedia(pick.url, pick.title, 'Playing', browser);
            return {
              intent: 'mediaNext',
              pickedVideo: pick,
              hudColor: 'lime',
              hudTitle: `⏭ 다음 곡 재생: ${pick.title}`,
              hudSubtitle: pick.url,
              heard: rawSentence,
              spoken: `다음 곡으로 ${pick.title} 영상을 재생합니다.`
            };
          }
        }
        const outcome = await this.mediaControl({ action: next ? 'next' : parsed.play ? 'play' : 'pause', scope: this.hasMediaContext() ? 'browser' : 'any', preferredSource: this.mediaContext && this.mediaContext.source });
        if (!next && !outcome.verified) throw new Error('요청한 재생 상태를 확인하지 못했습니다.');
        return {
          intent: parsed.intent,
          hudColor: 'cyan',
          hudTitle: next ? '⏭ 다음 영상 요청 · 전환 확인 필요' : outcome.state === 'none' ? '재생 항목 확인 필요' : parsed.play ? '▶ 재생 확인' : '⏸ 일시정지 확인',
          hudSubtitle: 'Windows 미디어 세션 상태 확인',
          heard: rawSentence,
          spoken: outcome.state === 'none' ? 'Windows에서 제어할 수 있는 재생 항목을 찾지 못했습니다.' : next ? '다음 영상을 요청했습니다. 실제로 바뀌었는지 확인해 주세요.' : parsed.play ? '재생 중인 것을 확인했습니다.' : '미디어가 멈춘 것을 확인했습니다.'
        };
      }

      case 'clock': {
        const now = new Date();
        const koTime = now.toLocaleTimeString('ko-KR', { hour: '2-digit', minute: '2-digit' });
        const koDate = now.toLocaleDateString('ko-KR', { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' });
        const msg = parsed.date ? `오늘은 ${koDate}입니다.` : `지금은 ${koTime}입니다.`;
        return {
          intent: 'clock',
          hudColor: 'cyan',
          hudTitle: `🕒 ${msg}`,
          hudSubtitle: `${koDate} · ${koTime}`,
          heard: rawSentence,
          spoken: msg
        };
      }

      case 'timer': {
        if (!Number.isFinite(parsed.seconds) || parsed.seconds <= 0 || parsed.seconds > 31536000) {
          return { intent: 'timer', hudColor: 'amber', hudTitle: '타이머 시간을 확인해 주세요', heard: rawSentence, spoken: '1초에서 1년 사이의 시간을 지정해 주세요.' };
        }
        const timers = readJson(FILES.timers, []);
        const fireAt = new Date(Date.now() + parsed.seconds * 1000).toISOString();
        timers.push({ label: parsed.label, seconds: parsed.seconds, fireAt });
        writeJson(FILES.timers, timers);
        const minText = parsed.seconds >= 60 ? `${Math.round(parsed.seconds / 60)}분` : `${parsed.seconds}초`;
        return {
          intent: 'timer',
          timerSeconds: parsed.seconds,
          timerLabel: parsed.label,
          hudColor: 'lime',
          hudTitle: `⏱ ${parsed.label} (${minText}) 타이머 시작`,
          hudSubtitle: `${minText} 뒤에 음성으로 알려 드립니다.`,
          heard: rawSentence,
          spoken: `${parsed.label} ${minText} 타이머를 맞췄습니다.`
        };
      }

      case 'remember': {
        try {
          fs.appendFileSync(FILES.memory, `- [${new Date().toISOString().slice(0, 10)}] ${parsed.note}\n`, 'utf8');
        } catch (_) {}
        return {
          intent: 'remember',
          hudColor: 'lime',
          hudTitle: '🧠 장기 기억(memory.md)에 저장했습니다',
          hudSubtitle: `「${parsed.note}」`,
          heard: rawSentence,
          spoken: '기억해 두었습니다.'
        };
      }

      case 'makeRoutine': {
        const routines = readJson(FILES.routines, {});
        routines[parsed.name] = parsed.steps;
        writeJson(FILES.routines, routines);
        return {
          intent: 'makeRoutine',
          hudColor: 'lime',
          hudTitle: `✓ 새 루틴 학습 완료: 「${parsed.name}」`,
          hudSubtitle: `실행 단계: ${parsed.steps.join(' → ')}`,
          heard: rawSentence,
          spoken: `앞으로 ${parsed.name}이라고 하시면 ${parsed.steps.join(', ')} 순서대로 실행할게요.`
        };
      }

      case 'routine': {
        const results = [];
        for (const step of parsed.steps) {
          const r = await this.execute(step, executeOsActions);
          results.push(r.hudTitle);
        }
        return {
          intent: 'routine',
          hudColor: 'lime',
          hudTitle: `⚡ 루틴 실행 완료: 「${parsed.name}」`,
          hudSubtitle: results.join(' · '),
          heard: rawSentence,
          spoken: `${parsed.name} 루틴을 모두 실행했습니다.`
        };
      }

      case 'openApp': {
        const target = executeOsActions
          ? await WindowsHands.openApp(parsed.spoken, cfg.apps)
          : WindowsHands.APP_MAP[parsed.spoken] || parsed.spoken;
        this.openedApps.push(target);
        this.lastDone = {
          said: rawSentence,
          description: `${parsed.spoken} (${target}) 실행`,
          undoType: 'app',
          undoTarget: target
        };
        return {
          intent: 'openApp',
          hudColor: 'cyan',
          hudTitle: `🚀 ${parsed.spoken} 실행`,
          hudSubtitle: `Windows 앱(${target})을 열었습니다.`,
          heard: rawSentence,
          spoken: `${parsed.spoken}을 열었습니다.`
        };
      }

      case 'closeOpened': {
        return {
          intent: 'closeOpened',
          hudColor: 'amber',
          hudTitle: '⚠ 창 자동 닫기 미지원',
          hudSubtitle: '열린 창의 소유자를 안전하게 확인할 수 없어 닫지 않았습니다.',
          heard: rawSentence,
          spoken: '안전하게 구분할 수 없어 창을 닫지 않았습니다.'
        };
      }

      case 'undo': {
        if (!this.lastDone) {
          return {
            intent: 'undo',
            hudColor: 'amber',
            hudTitle: '↩ 되돌릴 방금 작업이 없습니다',
            hudSubtitle: '아직 실행된 빠른 명령이 없습니다.',
            heard: rawSentence,
            spoken: '되돌릴 방금 작업이 없습니다.'
          };
        }
        const prev = this.lastDone;
        if (prev.undoType !== 'volume') return {
          intent: 'undo', hudColor: 'amber', hudTitle: '⚠ 이 작업은 자동으로 되돌릴 수 없습니다',
          hudSubtitle: prev.description, heard: rawSentence, spoken: '이 작업은 자동으로 되돌릴 수 없습니다.'
        };
        this.lastDone = null;
        if (executeOsActions && prev.undoType === 'volume') {
          await WindowsHands.volume(prev.undoTarget);
        }
        return {
          intent: 'undo',
          hudColor: 'lime',
          hudTitle: `↩ 되돌리기 완료: ${prev.description}`,
          hudSubtitle: `방금 명령(「${prev.said}」)을 원래대로 되돌렸습니다.`,
          heard: rawSentence,
          spoken: `방금 실행한 ${prev.description} 작업을 되돌렸습니다.`
        };
      }

      case 'whatDid': {
        const desc = this.lastDone
          ? `방금 「${this.lastDone.said}」 말씀에 따라 ${this.lastDone.description} 작업을 수행했습니다.`
          : '이번 세션에서 아직 수행한 빠른 작업이 없습니다.';
        return {
          intent: 'whatDid',
          hudColor: 'cyan',
          hudTitle: '📋 방금 수행한 작업 내역',
          hudSubtitle: desc,
          heard: rawSentence,
          spoken: desc
        };
      }

      case 'type': {
        if (executeOsActions) await WindowsHands.typeText(parsed.text, false);
        this.lastDone = {
          said: rawSentence,
          description: `「${parsed.text}」 텍스트 입력`,
          undoType: 'type'
        };
        return {
          intent: 'type',
          hudColor: 'cyan',
          hudTitle: `⌨ 텍스트 입력: 「${parsed.text}」`,
          hudSubtitle: '현재 포커스된 Windows 입력창에 붙여 넣었습니다.',
          heard: rawSentence,
          spoken: '입력했습니다.'
        };
      }

      case 'dictation': {
        if (executeOsActions) {
          const { target } = await this.interact({ op: 'capture' });
          if (!target.editable) throw new Error('받아쓸 입력창을 선택하고 다시 말씀해 주세요.');
          this.dictationTarget = target;
        }
        this.mode = 'dictation';
        return { intent: 'dictation', hudColor: 'cyan', hudTitle: '✍ 받아쓰기 시작', hudSubtitle: '입력창을 유지한 채 말씀하세요. 끝내려면 “그만”이라고 말씀하세요.', heard: rawSentence, spoken: '받아쓰기를 시작합니다.' };
      }

      case 'dictationText': {
        if (executeOsActions) {
          if (!this.dictationTarget) throw new Error('받아쓰기 입력창을 다시 선택해 주세요.');
          try {
            const result = await this.interact({ op: 'append', target: this.dictationTarget, text: parsed.text });
            this.dictationTarget.value = result.value;
          } catch (err) { this.mode = 'idle'; this.dictationTarget = null; throw err; }
        }
        return { intent: 'dictationText', hudColor: 'cyan', hudTitle: '✍ 받아쓰기', hudSubtitle: parsed.text, heard: rawSentence, spoken: '' };
      }

      case 'stopDictation':
        this.mode = 'idle';
        this.dictationTarget = null;
        return { intent: 'stopDictation', hudColor: 'cyan', hudTitle: '받아쓰기 종료', heard: rawSentence, spoken: '받아쓰기를 마쳤습니다.' };

      case 'rewrite': {
        if (!executeOsActions) return { intent: 'rewrite', hudColor: 'amber', hudTitle: '선택한 글 확인 필요', heard: rawSentence, spoken: '시험 모드에서는 글을 변경하지 않습니다.' };
        const { target } = await this.interact({ op: 'capture' });
        if (!target.selection) throw new Error('수정할 글을 선택한 뒤 다시 요청해 주세요.');
        const revised = cleanLocalDraft(await this.rewriteLocal(`요청: ${parsed.style}\n\n선택한 글:\n${target.selection}`, '요청에 맞게 선택한 글만 수정하세요. 설명이나 따옴표 없이 수정 결과만 출력하세요.', cfg.localRewriteModel || 'gemma4:e4b', 60000));
        if (!revised) throw new Error('로컬 글 수정 모델에 연결할 수 없습니다. 선택한 글은 변경되지 않았습니다.');
        this.pendingSend = { kind: 'rewrite', text: revised, target, expiresAt: Date.now() + 60000 };
        return { intent: 'rewrite', awaitingConfirm: true, pendingSend: { kind: 'rewrite', expiresAt: this.pendingSend.expiresAt }, hudColor: 'amber', hudTitle: '수정할 문장 확인', hudSubtitle: revised, heard: rawSentence, spoken: '수정된 문장을 표시했습니다. 선택한 글을 바꿀까요?' };
      }

      case 'hush':
      case 'bye':
        this.mode = 'idle';
        return {
          intent: parsed.intent,
          hudColor: 'cyan',
          hudTitle: '● 대기 모드 전환',
          hudSubtitle: `언제든 「${cfg.assistantName || '자비스'}」라고 부르시거나 Alt 키를 누르세요.`,
          heard: rawSentence,
          spoken: parsed.intent === 'bye' ? '네, 필요하실 때 언제든 불러 주세요.' : ''
        };

      default: {
        const sys = this.getSystemTelemetry();
        let memText = '';
        try {
          memText = fs.readFileSync(FILES.memory, 'utf8');
        } catch (_) {}
        const replyText = await generateSmartButlerReply(parsed.prompt, cfg, sys, memText);
        return {
          intent: 'brain',
          hudColor: 'cyan',
          hudTitle: `🗣 ${cfg.assistantName || '자비스'} 답변`,
          hudSubtitle: replyText,
          heard: rawSentence,
          spoken: replyText
        };
      }
    }
  }
}

module.exports = {
  ButlerEngine,
  WindowsHands,
  soundAlikePattern,
  FILES,
  DEFAULT_CONFIG
};
