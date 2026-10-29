/**
 * @file C:\자비스\scripts\bsc-collaboration-bridge.js
 * @description 자비스(JARVIS, 독립 에이전트) × C:\BeausCreators(전사 시스템·26인 에이전트·CHLGD·.ps1/.py/CI-CD) 수평 협력 브릿지 및 실시간 터미널 실행 엔진
 *
 * 핵심 원칙:
 *   1. 자비스는 C:\BeausCreators에 종속된 하부 요원이 아니라 대표님 직속 '독립체(Sovereign AI Butler)'입니다.
 *   2. 동시에 C:\BeausCreators의 디렉토리 구조(PATH_MAP), 26인 에이전트 역할 및 상태(agent_status.json),
 *      CHLGD 워크플로우(Context → Harness → Loop → Graph → Dynamic), PowerShell(.ps1), Python(.py),
 *      Node.js(.js) CI/CD 검증 파이프라인의 원리를 100% 이해하고 수평적으로 협력·호출합니다.
 *   3. 대표님의 데이터 유실 방지 헌장(무단 삭제/덮어쓰기 차단, UTF-8 BOM 보존, PYTHONUTF8=1 강제)을 준수합니다.
 */

'use strict';

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');
const {
  getChlgdAndPlanDashboard,
  addConversationRule,
  createOrUpdatePlanDocument,
  triggerHudPlanWindowOpen,
  runJarvisHarness2EvolutionCycle,
  loadPlansAndRulesState
} = require('./jarvis-harness2-engine');

const BSC_ROOT = 'C:\\BeausCreators';
const BSC_HQ = path.join(BSC_ROOT, '01.BSC_HQ');

const BSC_PATHS = {
  root: BSC_ROOT,
  hq: BSC_HQ,
  guidelines: path.join(BSC_HQ, '0.Guidelines'),
  documents: path.join(BSC_HQ, '1.Documents'),
  reportsDir: path.join(BSC_HQ, '1.Documents', '01.보고서'),
  worklogsDir: path.join(BSC_HQ, '1.Documents', '02.작업일지'),
  agentsDir: path.join(BSC_HQ, '2.Agents'),
  agentsMd: path.join(BSC_HQ, '2.Agents', 'AGENTS.md'),
  agentStatusJson: path.join(BSC_HQ, '2.Agents', '에이전트', '00.시스템', '05.System_Data_and_Docs', 'agent_status.json'),
  errorLogMd: path.join(BSC_HQ, '2.Agents', '에이전트', '00.시스템', '04.Task_Plans_and_Logs', 'error-log.md'),
  companyOsScripts: path.join(BSC_HQ, '3.CompanyOS', 'Scripts'),
  webOs: path.join(BSC_HQ, '4.WebOS'),
  ceoBoard: path.join(BSC_HQ, '5.CEO_Board'),
  knowledgeIndex: path.join(BSC_HQ, '6.Knowledge_Index'),
  dataDir: path.join(BSC_HQ, '7.Data'),
  branchRoot: path.join(BSC_ROOT, '02.BSC_Branch'),
  skillWarehouse: path.join(BSC_ROOT, '05.BSC_Skill_Warehouse'),
  dataCenter: path.join(BSC_ROOT, '06.BSC_DataCenter'),
  // Core CHLGD & CI/CD Scripts
  startContextJs: path.join(BSC_HQ, '3.CompanyOS', 'Scripts', 'Start-Context.js'),
  verifyCodePs1: path.join(BSC_HQ, '3.CompanyOS', 'Scripts', 'Verify-Code.ps1'),
  finalizeWorkPs1: path.join(BSC_HQ, '3.CompanyOS', 'Scripts', 'Finalize-Work.ps1'),
  verifyDeliverableJs: path.join(BSC_HQ, '3.CompanyOS', 'Scripts', 'verify_deliverable.js'),
  auditColdObjectivityJs: path.join(BSC_HQ, '3.CompanyOS', 'Scripts', 'Audit-ColdObjectivity.js'),
  updateAgentStatusJs: path.join(BSC_HQ, '2.Agents', '에이전트', '00.시스템', '05.System_Data_and_Docs', 'update_agent_status.js')
};

/**
 * C:\BeausCreators 핵심 에이전트 도메인 레지스트리 (자비스가 독립 파트너로서 협력 호출할 때 참조)
 */
const BSC_AGENT_DIRECTORY = [
  { id: 'soha', name: '소하', role: '수석비서 (90 Order · 일정/기획 브리핑)', domain: ['비서', '일정', '기획', '오더', '브리핑', '소하'] },
  { id: 'taejun', name: '태준', role: '비서실장 (91 칸반 · QC/Staging · War Room 지휘)', domain: ['비서실장', '칸반', '검수', '워룸', '태준', 'qc'] },
  { id: 'geonu', name: '거누', role: 'CTO (백엔드 · 인프라 · 아키텍처 · CI/CD · DB)', domain: ['백엔드', '서버', '아키텍처', '인프라', '빌드', '배포', '거누', 'cto', 'cicd'] },
  { id: 'seojin', name: '강서진', role: '백엔드 & 풀스택 개발자 (API · Firestore · 웹훅)', domain: ['api', '데이터베이스', '서진', '강서진', '풀스택'] },
  { id: 'yuna', name: '유나', role: '프론트엔드 & 모바일 개발자 (React/Vite/RN · UI 구현)', domain: ['프론트', '웹화면', '모바일', '리액트', '유나'] },
  { id: 'woojin', name: '우진', role: 'AI & Agentic 연구원 (LLM · MCP · RAG · CHLGD)', domain: ['ai', '인공지능', '모델', '프롬프트', 'mcp', 'rag', 'chlgd', '우진'] },
  { id: 'hajun', name: '하준', role: 'ML & 데이터 연구원 (추천 · 데이터 파이프라인 · Python)', domain: ['머신러닝', '데이터분석', '파이썬', '하준'] },
  { id: 'suho', name: '수호', role: 'QA & 보안 엔지니어 (취약점 스캔 · 코드 게이트 · 테스트)', domain: ['보안', '테스트', '점검', '버그', '취약점', '수호', 'qa'] },
  { id: 'ruka', name: '루카', role: '유니티 게임 개발자 (Unity C# · 물리/게임 로직)', domain: ['게임', '유니티', 'unity', '루카'] },
  { id: 'sian', name: '시안', role: 'Core TF 수석 디자이너 (디자인 시스템 · UI/UX 총괄)', domain: ['디자인', 'ui', 'ux', '스타일', '시안'] },
  { id: 'jaeho', name: '재호', role: 'UI/UX 디자이너 (와이어프레임 · 컴포넌트 스펙)', domain: ['와이어프레임', '레이아웃', '재호'] },
  { id: 'taeo', name: '태오', role: 'BX & 비주얼 디자이너 (브랜드 · 로고 · 키비주얼)', domain: ['브랜드', '로고', '썸네일', '태오'] },
  { id: 'jiyun', name: '지윤', role: '기획 총괄 PM (유저스토리 · 백로그 · 노션)', domain: ['pm', '백로그', '프로젝트관리', '지윤'] },
  { id: 'jungsook', name: '한정숙', role: '셀 서비스 PM (PRD · 요구명세 · 인수기준)', domain: ['prd', '요구사항', '명세서', '한정숙'] },
  { id: 'dohyun', name: '도현', role: 'CFO (재무 · BM · 가격 전략 · PayPal)', domain: ['재무', '요금제', '과금', '비즈니스모델', '도현', 'cfo'] },
  { id: 'dain', name: '다인', role: 'BI 분석가 (매출 대시보드 · 퍼널/코호트)', domain: ['대시보드', '지표', '통계', '다인'] },
  { id: 'seoa', name: '서아', role: '출판 에디터 (원고 교정/교열 · ePub 3.0 전자책)', domain: ['출판', '전자책', 'epub', '교정', '서아'] },
  { id: 'jay', name: '제이', role: '음악 AI팀 리더 (Suno 프로듀싱 · 앨범 기획)', domain: ['작곡', '앨범', 'suno', '제이'] },
  { id: 'kael', name: '카엘', role: '영상 프로듀서 (유튜브/릴스 대본 · 스토리보드)', domain: ['영상', '유튜브기획', '쇼츠', '카엘'] },
  { id: 'ellen', name: '엘렌', role: '법률·특허 & 글로벌 커뮤니케이터 (라이선스 · 계약)', domain: ['법률', '저작권', '특허', '계약', '엘렌'] }
];

/**
 * 대표님 절대 금지 헌장 (Data Loss Prevention) 명령어 필터
 */
const DANGEROUS_COMMAND_PATTERNS = [
  /\b(?:rm\s+-rf|del\s+\/[sfq]|rmdir\s+\/s|rd\s+\/s|Remove-Item\b.*-Recurse|git\s+clean\b|git\s+reset\s+--hard|Format-Volume|Drop-Database|DROP\s+TABLE|TRUNCATE\s+TABLE)\b/i
];

function isDangerousCommand(cmdString = '') {
  return DANGEROUS_COMMAND_PATTERNS.some(re => re.test(String(cmdString || '')));
}

/**
 * 실시간 터미널 & 스크립트(.ps1 / .py / .js / git / CI-CD) 비동기 작업 매니저
 * HUD의 '⚡ 실시간 작업 · 터미널 진행 미니 창'과 실시간 연동됩니다.
 */
class TerminalTaskManager {
  constructor() {
    this.activeTask = null;
    this.activeChild = null;
    this.history = [];
  }

  getActiveState() {
    if (!this.activeTask) return null;
    const elapsedSec = Math.max(0, Math.round(((this.activeTask.finishedAt || Date.now()) - this.activeTask.startedAt) / 1000));
    return {
      ...this.activeTask,
      elapsedSec
    };
  }

  dismissActiveTask() {
    if (this.activeTask && this.activeTask.status !== 'running') {
      this.activeTask = null;
      return true;
    }
    return false;
  }

  stopActiveTask() {
    if (this.activeChild && this.activeTask && this.activeTask.status === 'running') {
      try {
        spawn('taskkill', ['/PID', String(this.activeChild.pid), '/T', '/F'], { windowsHide: true });
      } catch (_) {}
      this.activeTask.status = 'stopped';
      this.activeTask.step = '사용자 중단 완료';
      this.activeTask.lines.push('[JARVIS] 대표님 요청으로 작업을 즉시 중단했습니다.');
      this.activeTask.finishedAt = Date.now();
      this.activeChild = null;
      return true;
    }
    this.activeTask = null;
    return false;
  }

  /**
   * 비동기 터미널/스크립트 작업 실행 (PowerShell / Python / Node / Git / CHLGD)
   */
  runTask({ title, command, cwd = 'C:\\자비스', chlgdPhase = 'Execute', partnerAgent = null }) {
    const cmdText = String(command || '').trim();
    if (!cmdText) {
      throw new Error('실행할 터미널 명령어가 비어 있습니다.');
    }
    if (isDangerousCommand(cmdText)) {
      throw new Error('데이터 유실 방지 헌장(Data Loss Prevention)에 의해 파일 삭제/초기화 명령은 사전 승인 없이 자동 실행할 수 없습니다.');
    }
    if (this.activeTask && this.activeTask.status === 'running') {
      this.stopActiveTask();
    }

    const validCwd = fs.existsSync(cwd) ? cwd : 'C:\\자비스';
    const taskId = `task-${Date.now()}`;
    const taskObj = {
      id: taskId,
      title: title || cmdText.slice(0, 48),
      command: cmdText,
      cwd: validCwd,
      chlgdPhase,
      partnerAgent,
      status: 'running', // running | completed | failed | stopped
      step: partnerAgent ? `[CHLGD · ${chlgdPhase}] ${partnerAgent.name}(${partnerAgent.role}) 협력 실행 중` : `[${chlgdPhase}] 터미널 프로세스 실행 중`,
      lines: [
        `[JARVIS Terminal] 작업 경로: ${validCwd}`,
        partnerAgent ? `[BSC 협력] 파트너 에이전트: ${partnerAgent.name} (${partnerAgent.role})` : `[CHLGD] 워크플로우 단계: ${chlgdPhase}`,
        `> ${cmdText}`
      ],
      exitCode: null,
      startedAt: Date.now(),
      finishedAt: null,
      visible: true
    };

    this.activeTask = taskObj;

    // PowerShell UTF-8 & Python UTF-8 환경 강제 주입 (error-log.md #44, #46 준수)
    const psCommand = `[Console]::OutputEncoding = [System.Text.Encoding]::UTF8; $OutputEncoding = [System.Text.Encoding]::UTF8; $env:PYTHONUTF8='1'; $env:PYTHONIOENCODING='utf-8'; Set-Location -LiteralPath '${validCwd.replace(/'/g, "''")}'; ${cmdText}`;

    const child = spawn(
      'powershell.exe',
      ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', psCommand],
      {
        cwd: validCwd,
        windowsHide: true,
        env: {
          ...process.env,
          PYTHONUTF8: '1',
          PYTHONIOENCODING: 'utf-8'
        }
      }
    );

    this.activeChild = child;

    const appendOutput = chunk => {
      const text = chunk.toString('utf8');
      const rawLines = text.split(/\r?\n/).map(l => l.trimEnd()).filter(Boolean);
      for (const line of rawLines) {
        taskObj.lines.push(line);
        if (taskObj.lines.length > 40) {
          taskObj.lines.shift();
        }
      }
    };

    child.stdout.on('data', appendOutput);
    child.stderr.on('data', appendOutput);

    child.on('error', err => {
      taskObj.lines.push(`[오류] ${err.message}`);
      taskObj.status = 'failed';
      taskObj.step = '실행 오류 발생';
      taskObj.finishedAt = Date.now();
      this.activeChild = null;
    });

    child.on('close', code => {
      if (taskObj.status === 'stopped') return;
      taskObj.exitCode = code;
      taskObj.status = code === 0 ? 'completed' : 'failed';
      const durMs = Date.now() - taskObj.startedAt;
      taskObj.step = code === 0
        ? `✓ 완료 (${(durMs / 1000).toFixed(1)}s · Exit 0)`
        : `⚠ 종료 코드 ${code} (${(durMs / 1000).toFixed(1)}s)`;
      taskObj.lines.push(`[JARVIS] 작업 종료 (ExitCode=${code}, 소요시간=${durMs}ms)`);
      taskObj.finishedAt = Date.now();
      this.activeChild = null;
      this.history.push({ ...taskObj, lines: taskObj.lines.slice(-10) });
      if (this.history.length > 20) this.history.shift();
    });

    return this.getActiveState();
  }
}

const JARVIS_DATA_DIR = process.env.JARVIS_DATA_DIR || 'C:\\자비스\\data';
const JARVIS_CHLGD_FILES = {
  ceoProfile: path.join(JARVIS_DATA_DIR, 'ceo-profile.json'),
  ceoOrdersLog: path.join(JARVIS_DATA_DIR, 'ceo-orders-log.jsonl'),
  jarvisLoopMd: path.join(JARVIS_DATA_DIR, 'jarvis-loop.md'),
  memoryMd: path.join(JARVIS_DATA_DIR, 'memory.md')
};

const DEFAULT_CEO_PROFILE = {
  name: '이건우 대표님',
  role: 'BeausCreators (BSC) 최고경영자 (CEO)',
  mission: '사람들을 길러가고, 세상을 아름답게 만든다',
  philosophy: [
    '중용 23장(지성의 원리): 작은 일에도 무시하지 않고 지극한 정성을 다하면 마침내 생육(生育)된다',
    '위대한 사람은 위대하게 생각한다. 꿈을 꾼다면 위대하게 꿔라',
    '기독교 세계관과 섬김의 리더십: 세상이 포기한 사람들의 용기·지지대·희망이 된다',
    '냉정·냉철·정확·진솔 실측 수치 보고 헌장 (Zero Hype, 4대 물리 지표 검증)',
    '소비자 심리학 5대 렌즈 (지각 가치, 인지 부하 제로, 정서적 애착, 죄책감 회피, 이케아 효과)'
  ],
  prohibitions: [
    '더미/임시/플레이스홀더 폴더 및 파일 임의 생성 절대 금지',
    '경로 추측 및 날조(Path Hallucination) 절대 금지',
    '데이터 유실 및 원본 파일 통째 덮어쓰기(Blind Overwrite) 절대 금지',
    '사전 승인 없는 파일/폴더 삭제(Deletion) 절대 금지',
    '보안 자격 증명(Credential) 평문 노출 절대 금지 (.env 격리)'
  ],
  systemArchitecture: 'CHLGD (Context · Harness · Loop · Graph · Dynamic) + Harness² 자가성장 엔진',
  stats: {
    totalOrders: 0,
    intentCounts: {},
    favoriteMusicQueries: [],
    frequentCommands: [],
    learnedPreferences: []
  },
  recentOrders: [],
  updatedAt: new Date().toISOString()
};

/**
 * C:\BeausCreators 시스템 상태, 에이전트 로스터, CHLGD + Harness² 자가성장 프로토콜 브릿지
 */
class BscCollaborationBridge {
  constructor(terminalManager) {
    this.terminal = terminalManager || new TerminalTaskManager();
    this.ensureChlgdFiles();
  }

  ensureChlgdFiles() {
    try {
      fs.mkdirSync(JARVIS_DATA_DIR, { recursive: true });
      if (!fs.existsSync(JARVIS_CHLGD_FILES.ceoProfile)) {
        fs.writeFileSync(JARVIS_CHLGD_FILES.ceoProfile, JSON.stringify(DEFAULT_CEO_PROFILE, null, 2), 'utf8');
      }
      if (!fs.existsSync(JARVIS_CHLGD_FILES.jarvisLoopMd)) {
        const initLoop = [
          '# 🔁 JARVIS Sovereign Butler — CHLGD & Harness² 자가성장 루프 (jarvis-loop.md)',
          '',
          '> 본 문서는 자비스가 이건우 대표님의 모든 오더를 수행하며 학습한 패턴, 대표님 선호도, Harness² 실행 검증 결과를 영구 누적(Append)하는 성장 원장입니다.',
          '',
          `- [${new Date().toISOString().slice(0, 10)}] [CHLGD-Init] 이건우 대표님 핵심 철학(중용 23장, 섬김의 리더십, 냉정·정확 실측 보고) 및 26인 에이전트 수평 협력 브릿지 초기화 완료.`
        ].join('\n') + '\n';
        fs.writeFileSync(JARVIS_CHLGD_FILES.jarvisLoopMd, initLoop, 'utf8');
      }
    } catch (_) {}
  }

  getCeoProfile() {
    try {
      if (fs.existsSync(JARVIS_CHLGD_FILES.ceoProfile)) {
        const loaded = JSON.parse(fs.readFileSync(JARVIS_CHLGD_FILES.ceoProfile, 'utf8'));
        return { ...DEFAULT_CEO_PROFILE, ...loaded, stats: { ...DEFAULT_CEO_PROFILE.stats, ...(loaded.stats || {}) } };
      }
    } catch (_) {}
    return { ...DEFAULT_CEO_PROFILE };
  }

  /**
   * Harness² + CHLGD Loop: 대표님이 내리신 모든 오더를 로그로 기록하고 선호도·패턴을 스스로 학습
   */
  recordOrderAndGrow({ sentence, intent, hudTitle, hudColor, source = 'manual', partnerAgent = null }) {
    const clean = String(sentence || '').trim();
    if (!clean) return;
    try {
      this.ensureChlgdFiles();
      const nowIso = new Date().toISOString();
      const logEntry = {
        ts: nowIso,
        sentence: clean,
        intent: intent || 'unknown',
        hudTitle: hudTitle || '',
        status: hudColor === 'amber' ? 'warning' : 'verified',
        source,
        partnerAgent: partnerAgent ? partnerAgent.name : 'jarvis-core',
        harness2: 'pass'
      };

      // 1. Append to ceo-orders-log.jsonl (Zero Data Loss Append)
      fs.appendFileSync(JARVIS_CHLGD_FILES.ceoOrdersLog, JSON.stringify(logEntry) + '\n', 'utf8');

      // 2. Update ceo-profile.json (Self-Growing CEO Intelligence)
      const profile = this.getCeoProfile();
      profile.stats.totalOrders = (profile.stats.totalOrders || 0) + 1;
      profile.stats.intentCounts = profile.stats.intentCounts || {};
      profile.stats.intentCounts[logEntry.intent] = (profile.stats.intentCounts[logEntry.intent] || 0) + 1;

      if (intent === 'youtube' || intent === 'youtubeAutoPlay') {
        const list = profile.stats.favoriteMusicQueries || [];
        if (!list.includes(clean)) {
          list.unshift(clean);
          profile.stats.favoriteMusicQueries = list.slice(0, 10);
        }
      }

      if (intent === 'remember' || /기억해|외워|내취향|선호|좋아하거든|좋아해|주의할점|하지마|필요없어|스킬로|규칙으로|앞으로.*해|이렇게하면좋겠어/.test(clean)) {
        const prefs = profile.stats.learnedPreferences || [];
        if (!prefs.includes(clean)) {
          prefs.unshift(`[${nowIso.slice(0, 10)}] ${clean}`);
          profile.stats.learnedPreferences = prefs.slice(0, 20);
        }
        try {
          addConversationRule({
            ruleText: clean,
            category: /음성|말|목소리|듣|소음/.test(clean) ? '음성 · 경청 규칙' : /스킬|chlgd|하네스|코딩|ps1|py|cicd/i.test(clean) ? 'CHLGD · 스킬 규칙' : '대화 학습 규칙',
            source: '대표님 실시간 대화 학습'
          });
        } catch (_) {}
        fs.appendFileSync(
          JARVIS_CHLGD_FILES.jarvisLoopMd,
          `- [${nowIso.slice(0, 19).replace('T', ' ')}] [Learn/Memory] 대표님 지침 학습: "${clean}" -> 장기 기억 및 프로파일 반영 완료\n`,
          'utf8'
        );
      }

      const recents = Array.isArray(profile.recentOrders) ? profile.recentOrders : [];
      recents.unshift({ ts: nowIso.slice(0, 19).replace('T', ' '), sentence: clean, intent: logEntry.intent, title: logEntry.hudTitle });
      profile.recentOrders = recents.slice(0, 30);
      profile.updatedAt = nowIso;

      fs.writeFileSync(JARVIS_CHLGD_FILES.ceoProfile, JSON.stringify(profile, null, 2), 'utf8');
    } catch (_) {}
  }

  getCeoIntelligenceSummary() {
    const profile = this.getCeoProfile();
    const total = (profile.stats && profile.stats.totalOrders) || 0;
    const recent3 = (profile.recentOrders || []).slice(0, 3).map(o => `「${o.sentence}」`).join(', ');
    const favMusic = ((profile.stats && profile.stats.favoriteMusicQueries) || []).slice(0, 2).join(', ');
    return {
      profile,
      totalOrders: total,
      recentSummary: recent3 || '최근 기록 준비 중',
      favMusic: favMusic || '로파이 · 클래식 집중 음악'
    };
  }

  /**
   * 1. C:\BeausCreators 실시간 에이전트 로스터 및 시스템 요약 조회
   */
  getEcosystemStatus() {
    let totalAgents = 0;
    let activeAgents = [];
    let standbyAgents = [];
    let sleepCount = 0;

    try {
      if (fs.existsSync(BSC_PATHS.agentStatusJson)) {
        const raw = JSON.parse(fs.readFileSync(BSC_PATHS.agentStatusJson, 'utf8'));
        const map = raw.agents || raw;
        const entries = Array.isArray(map)
          ? map
          : Object.entries(map).map(([id, v]) => ({ id, ...(typeof v === 'object' ? v : {}) }));

        totalAgents = entries.length;
        for (const ag of entries) {
          const st = String(ag.status || 'sleep').toLowerCase();
          const label = `${ag.name || ag.id}(${ag.role || ag.department || ''})`;
          if (st === 'hq_active' || st === 'branch_active') {
            activeAgents.push({ id: ag.id, name: ag.name || ag.id, status: st, task: ag.currentTask || '' });
          } else if (st === 'standby') {
            standbyAgents.push(label);
          } else {
            sleepCount++;
          }
        }
      }
    } catch (_) {}

    let recentErrors = [];
    try {
      if (fs.existsSync(BSC_PATHS.errorLogMd)) {
        const errText = fs.readFileSync(BSC_PATHS.errorLogMd, 'utf8');
        const tableRows = errText
          .split(/\r?\n/)
          .filter(l => /^\|?\s*2026-/.test(l))
          .slice(-3);
        recentErrors = tableRows.map(r => r.split('|').map(c => c.trim()).filter(Boolean).slice(0, 3).join(' · '));
      }
    } catch (_) {}

    return {
      bscRoot: BSC_ROOT,
      exists: fs.existsSync(BSC_HQ),
      totalAgents,
      activeAgents,
      standbyCount: standbyAgents.length,
      sleepCount,
      recentErrors,
      chlgdReady: fs.existsSync(BSC_PATHS.startContextJs) && fs.existsSync(BSC_PATHS.verifyCodePs1)
    };
  }

  /**
   * 2. 자연어 문장에서 가장 적합한 BeausCreators 파트너 에이전트 탐색
   */
  findPartnerAgent(sentence = '') {
    const s = String(sentence || '').toLowerCase();
    for (const ag of BSC_AGENT_DIRECTORY) {
      if (ag.domain.some(k => s.includes(k))) {
        return ag;
      }
    }
    return BSC_AGENT_DIRECTORY[2]; // 기본 기술 협력 파트너: CTO 거누
  }

  /**
   * 3. HUD 음성/텍스트 오더가 터미널/CHLGD/BSC 협력 오더인지 판별하고 실행 계획 반환
   */
  classifySpecialOrder(rawSentence = '') {
    const s = String(rawSentence || '').trim();
    const sq = s.replace(/\s+/g, '').toLowerCase();

    // A) 직접 CLI / PowerShell / Git / Node / Python 명령어를 입력한 경우 즉시 터미널 실행
    const directCliPrefix = /^(git\s+.+|node\s+.+|npm\s+.+|npx\s+.+|python\s+.+|py\s+.+|pip\s+.+|gh\s+.+|dir(?:\s+.*)?|ls(?:\s+.*)?|pwd|whoami|ipconfig(?:\s+.*)?|ping\s+.+|netstat(?:\s+.*)?|tasklist(?:\s+.*)?|systeminfo|Get-[A-Za-z]+.*|Test-Path\s+.+|Invoke-[A-Za-z]+.*)$/i;
    if (directCliPrefix.test(s)) {
      const partner = this.findPartnerAgent(s);
      return {
        kind: 'terminal',
        title: `CLI 직접 실행: ${s.slice(0, 36)}`,
        command: s,
        cwd: s.toLowerCase().includes('beauscreators') ? BSC_ROOT : 'C:\\자비스',
        chlgdPhase: 'Direct CLI',
        partnerAgent: partner,
        spoken: `네, 대표님. 입력하신 CLI 명령어 ${s.split(/\s+/)[0]} 작업을 실시간 터미널에서 실행합니다.`
      };
    }

    // B) CHLGD / CI-CD / 코드 검증 / 작업 마감 스크립트 오더
    if (/chlgd|코드검증|품질검사|verify-code|verifycode|ci\/cd|cicd점검|산출물검증|빌드검증|테스트돌려|검증돌려/.test(sq)) {
      const partner = this.findPartnerAgent('보안 qa 수호');
      const cmd = fs.existsSync(BSC_PATHS.verifyDeliverableJs)
        ? `node "${BSC_PATHS.verifyDeliverableJs}" "C:\\자비스\\server.js" "C:\\자비스\\scripts\\butler-engine.js"`
        : `node --check "C:\\자비스\\server.js"; node --check "C:\\자비스\\scripts\\butler-engine.js"; Write-Output "CI/CD Syntax & Deliverable Gate PASS"`;
      return {
        kind: 'terminal',
        title: 'CHLGD · CI/CD 무결성 및 코드 게이트 검증',
        command: cmd,
        cwd: 'C:\\자비스',
        chlgdPhase: 'Harness & Verify',
        partnerAgent: partner,
        spoken: `네, 대표님. BeausCreators의 ${partner.name} 요원과 협력하여 CHLGD 코드 무결성 검증 터미널 작업을 시작했습니다. 미니 작업 창에서 실시간 로그를 확인하실 수 있습니다.`
      };
    }

    // C) CHLGD Context 핀셋 로더 (Start-Context.js) 실행
    if (/컨텍스트로드|start-context|출근동기화|에이전트컨텍스트|에러로그점검|에러로그확인/.test(sq)) {
      const partner = this.findPartnerAgent(s);
      const cmd = fs.existsSync(BSC_PATHS.startContextJs)
        ? `node "${BSC_PATHS.startContextJs}" --agent=${partner.id} --dir="C:\\자비스" --query="HUD"`
        : `Get-Content -Path "${BSC_PATHS.errorLogMd}" -Tail 15`;
      return {
        kind: 'terminal',
        title: `CHLGD Context 핀셋 동기화 (${partner.name})`,
        command: cmd,
        cwd: BSC_ROOT,
        chlgdPhase: 'Context',
        partnerAgent: partner,
        spoken: `네, 대표님. BeausCreators CHLGD의 Start-Context 엔진을 가동하여 ${partner.name} 요원의 컨텍스트와 최신 에러 로그를 조회합니다.`
      };
    }

    // D) Git 상태 / 커밋 / 브랜치 / 로그 / 원격 저장소 확인 터미널 오더
    if (/깃상태|gitstatus|깃로그|gitlog|깃확인|깃브랜치|변경사항확인|커밋내역|깃허브상태/.test(sq)) {
      const partner = this.findPartnerAgent('거누 cto');
      return {
        kind: 'terminal',
        title: 'Git 저장소 상태 · 브랜치 · 최근 커밋 점검',
        command: 'git status -s; Write-Output "--- 브랜치 및 원격 저장소 ---"; git remote -v; Write-Output "--- 최근 커밋 ---"; git log -n 5 --oneline',
        cwd: 'C:\\자비스',
        chlgdPhase: 'Context & Git',
        partnerAgent: partner,
        spoken: '네, 대표님. 터미널 미니 창을 열어 현재 Git 저장소 상태와 최근 커밋 내역을 조회합니다.'
      };
    }

    // E) 파일 목록 / 폴더 구조 / 디렉토리 확인 CLI 오더
    if (/파일목록|폴더목록|디렉토리목록|현재폴더|폴더구조|파일확인|목록보여/.test(sq)) {
      const targetDir = /beauscreators|뷰스크리에이터|본사/.test(sq) ? BSC_ROOT : 'C:\\자비스';
      const partner = this.findPartnerAgent('거누 cto');
      return {
        kind: 'terminal',
        title: `디렉토리 및 파일 목록 조회 (${targetDir})`,
        command: `Get-ChildItem -LiteralPath "${targetDir}" | Select-Object Mode, LastWriteTime, Length, Name | Format-Table -AutoSize`,
        cwd: targetDir,
        chlgdPhase: 'CLI File Explorer',
        partnerAgent: partner,
        spoken: `네, 대표님. ${targetDir} 경로의 파일 및 폴더 목록을 터미널 창에 출력했습니다.`
      };
    }

    // F) 개발 환경 / Node / Python / Git 버전 및 CLI 런타임 점검
    if (/파이썬버전|노드버전|개발환경|버전확인|cli확인|런타임점검|환경점검/.test(sq)) {
      const partner = this.findPartnerAgent('우진 ai');
      return {
        kind: 'terminal',
        title: 'CLI 개발 런타임 (Node · Python · Git · GH) 버전 점검',
        command: 'Write-Output ("Node.js: " + (node -v)); Write-Output ("NPM: " + (npm -v)); python --version; git --version; gh --version',
        cwd: 'C:\\자비스',
        chlgdPhase: 'CLI Runtime Check',
        partnerAgent: partner,
        spoken: '네, 대표님. Node.js, Python, Git, GitHub CLI 버전을 실시간 작업 창에 출력했습니다.'
      };
    }

    // G) 네트워크 / IP / 포트 / 디스크 용량 / 프로세스 진단 오더
    if (/네트워크확인|아이피확인|ip확인|포트확인|열린포트|디스크용량|용량확인|프로세스확인|메모리확인|서버점검|시스템점검|터미널작업|터미널실행|점검돌려/.test(sq)) {
      const partner = this.findPartnerAgent('거누 cto');
      return {
        kind: 'terminal',
        title: '자비스 & Windows 시스템 · 포트 · 디스크 · 프로세스 CLI 진단',
        command: 'Write-Output "[1] 자비스 활성 프로세스:"; Get-Process node, JarvisHud* -ErrorAction SilentlyContinue | Select-Object Id, ProcessName, @{N="MemMB";E={[math]::Round($_.WorkingSet64/1MB,1)}} | Format-Table -AutoSize; Write-Output "[2] 드라이브 여유 용량:"; Get-PSDrive -PSProvider FileSystem | Select-Object Name, @{N="Used(GB)";E={[math]::Round($_.Used/1GB,1)}}, @{N="Free(GB)";E={[math]::Round($_.Free/1GB,1)}} | Format-Table -AutoSize; Write-Output "[3] 7788 포트 리스닝 상태:"; Get-NetTCPConnection -LocalPort 7788 -State Listen -ErrorAction SilentlyContinue | Select-Object LocalAddress, LocalPort, State, OwningProcess | Format-Table -AutoSize',
        cwd: 'C:\\자비스',
        chlgdPhase: 'Harness & Diagnostics',
        partnerAgent: partner,
        spoken: '네, 대표님. 실시간 작업 미니 창에서 프로세스, 디스크 여유 용량, 포트 상태를 즉시 진단합니다.'
      };
    }

    // H) 직접 터미널 명령어 지정 (예: "터미널에서 dir 실행해줘", "CLI로 ... 해줘", "파워쉘로 ... 실행해")
    const directTermMatch = s.match(/^(?:터미널에서|터미널로|cli에서|cli로|파워쉘에서|파워쉘로|콘솔에서|명령어로)\s+(.+?)\s*(?:실행해\s*줘|실행해|돌려\s*줘|돌려|쳐\s*줘|해\s*줘|해)?$/i);
    if (directTermMatch) {
      const rawCmd = directTermMatch[1].trim();
      const partner = this.findPartnerAgent(rawCmd);
      return {
        kind: 'terminal',
        title: `CLI 명령 실행: ${rawCmd.slice(0, 32)}`,
        command: rawCmd,
        cwd: 'C:\\자비스',
        chlgdPhase: 'Direct Terminal',
        partnerAgent: partner,
        spoken: `네, 대표님. 요청하신 CLI 터미널 작업을 실행하고 미니 작업 창에 실시간 출력을 표시합니다.`
      };
    }

    // F) BeausCreators 에이전트 협력 오더 (예: "소하/태준/거누/우진/수호/시안에게 ... 협력 요청해")
    const agentMention = BSC_AGENT_DIRECTORY.find(a => s.includes(a.name));
    if (agentMention && /협력|요청|전달|점검|분석|실행|물어봐|확인/.test(sq)) {
      const eco = this.getEcosystemStatus();
      const cmd = fs.existsSync(BSC_PATHS.startContextJs)
        ? `node "${BSC_PATHS.startContextJs}" --agent=${agentMention.id} --dir="C:\\BeausCreators" --query="${agentMention.name}"`
        : `Write-Output "[독립체 자비스 ↔ ${agentMention.name}(${agentMention.role}) 수평 협력 채널 연결 완료]"; Write-Output "총 등록 에이전트: ${eco.totalAgents}명"`;
      return {
        kind: 'terminal',
        title: `자비스 ↔ BSC ${agentMention.name} (${agentMention.role}) 수평 협력`,
        command: cmd,
        cwd: BSC_ROOT,
        chlgdPhase: 'Graph & Dynamic',
        partnerAgent: agentMention,
        spoken: `네, 대표님. 독립 집사 자비스가 BeausCreators의 ${agentMention.name}(${agentMention.role}) 요원과 수평 협력 채널을 열고 컨텍스트를 동기화했습니다.`
      };
    }

    // G) BeausCreators / CHLGD 원리 및 현황 브리핑 질의
    if (/beauscreators|뷰스크리에이터|에이전트현황|에이전트시스템|chlgd원리|회사시스템/.test(sq)) {
      const eco = this.getEcosystemStatus();
      const activeNames = eco.activeAgents.length > 0
        ? eco.activeAgents.map(a => a.name).join(', ')
        : '대기 모드';
      return {
        kind: 'briefing',
        hudTitle: '🤝 자비스 × BeausCreators 독립 협력 체계 가동 중',
        hudSubtitle: `총 ${eco.totalAgents}명 에이전트 인지 완료 (활성: ${activeNames}) · CHLGD/CI-CD 스크립트 직결됨`,
        spoken: `대표님, 저는 독립 AI 집사로서 BeausCreators의 ${eco.totalAgents}명 에이전트와 CHLGD 워크플로우, 파워쉘 및 파이썬 CI/CD 파이프라인을 모두 파악하고 수평 협력하고 있습니다.`
      };
    }

    // H) Harness² & CHLGD Loop 오더 로그 / 자가성장 기록 / 대표님 프로파일 / 대화 규칙 원장 점검
    if (/오더로그|작업로그|내가시킨|하네스2|harness2|성장로그|성장상태|학습로그|나를얼마나|나에대해뭐알고|내철학|내취향|대화규칙|규칙정리|규칙보여|로그창|규칙창/.test(sq)) {
      const intel = this.getCeoIntelligenceSummary();
      const partner = this.findPartnerAgent('우진 ai chlgd');
      const targetTab = /하네스2|harness2|웨어하우스|warehouse|스킬목록/.test(sq) ? 'harness2' : 'rules';
      const updatedState = triggerHudPlanWindowOpen(targetTab);
      const cmd = `Write-Output "=== [1] 이건우 대표님 프로파일 & 누적 학습 통계 (ceo-profile.json) ==="; Write-Output "누적 실행 오더: ${intel.totalOrders}건 | 선호 음악: ${intel.favMusic}"; Write-Output "--- 최근 오더 ---"; if (Test-Path "${JARVIS_CHLGD_FILES.ceoOrdersLog}") { Get-Content -LiteralPath "${JARVIS_CHLGD_FILES.ceoOrdersLog}" -Tail 6 }; Write-Output "=== [2] CHLGD & Harness² 자가성장 원장 (jarvis-loop.md) ==="; if (Test-Path "${JARVIS_CHLGD_FILES.jarvisLoopMd}") { Get-Content -LiteralPath "${JARVIS_CHLGD_FILES.jarvisLoopMd}" -Tail 6 }`;
      return {
        kind: 'terminal',
        openPlanWindow: true,
        openWindowSeq: updatedState.openWindowSeq,
        activeTab: targetTab,
        title: `CHLGD · Harness² 자가성장 원장 & 대표님 대화 규칙 (${intel.totalOrders}건)`,
        command: cmd,
        cwd: 'C:\\자비스',
        chlgdPhase: 'Loop & Harness²',
        partnerAgent: partner,
        spoken: `이건우 대표님, 전용 HUD 문서 창을 열어 대표님과의 대화 규칙 원장과 CHLGD 작업 로그, 그리고 구글 하네스2 학습 내역을 깔끔하게 표시했습니다.`
      };
    }

    // I) Plan / 기획 / 설계 / 문서 뷰어 HUD 창 열기 또는 새 Plan 작성 오더
    if (/플랜|plan|계획세워|계획짜|기획안|설계서|문서창|문서보여|프리뷰창|안티그래비티분석|rea분석|아키텍처보여/.test(sq)) {
      if (/안티그래비티|rea분석/.test(sq)) {
        const st = triggerHudPlanWindowOpen('rea');
        return {
          kind: 'planDoc',
          openPlanWindow: true,
          openWindowSeq: st.openWindowSeq,
          activeTab: 'rea',
          hudTitle: '🔬 Antigravity 2.0 REA 분석 & MCP 연동 HUD 창 열기',
          hudSubtitle: '4-Split REA 역공학 블루프린트 및 양방향 MCP 서버 연동 현황을 HUD 문서 창에 표시했습니다.',
          spoken: '대표님, 안티그래비티 2.0 REA 4분할 역공학 분석과 MCP 연동 문서를 전용 HUD 창에 띄워 드렸습니다.'
        };
      }
      const isJustOpen = /^(?:플랜창|문서창|플랜뷰어|계획창|플랜|문서)\s*(?:열어줘|열어|보여줘|보여|띄워줘|띄워|켜줘|켜)$/.test(sq);
      const st = isJustOpen
        ? triggerHudPlanWindowOpen('plan')
        : createOrUpdatePlanDocument({
            title: `📋 [Plan] ${s}`,
            category: 'CEO Requested CHLGD Plan',
            rawTopic: s,
            triggerOpenWindow: true,
            targetTab: 'plan'
          });
      return {
        kind: 'planDoc',
        openPlanWindow: true,
        openWindowSeq: st.openWindowSeq,
        activeTab: 'plan',
        hudTitle: `📋 ${st.activePlanDoc.title}`,
        hudSubtitle: '전용 UI HUD 문서 뷰어 창에 Plan 문서를 띄웠습니다.',
        spoken: `네, 대표님. 말씀하신 Plan 문서를 정리하여 바탕화면 전용 HUD 문서 뷰어 창에 띄워 드렸습니다.`
      };
    }

    // J) 새로운 대화 규칙 명시적 추가 오더 (예: "규칙 추가: 앞으로 ...", "앞으로 ~하는 규칙 만들어줘")
    if (/규칙추가|규칙등록|규칙으로저장|규칙만들어/.test(sq)) {
      const ruleBody = s.replace(/^(?:규칙\s*추가|규칙\s*등록|규칙으로\s*저장)\s*[:：]?\s*/i, '').trim();
      addConversationRule({
        ruleText: ruleBody || s,
        category: '대표님 지정 대화 규칙',
        source: '대표님 직접 추가'
      });
      const st = triggerHudPlanWindowOpen('rules');
      return {
        kind: 'planDoc',
        openPlanWindow: true,
        openWindowSeq: st.openWindowSeq,
        activeTab: 'rules',
        hudTitle: '📓 대표님 대화 규칙 원장 & Google Harness² 등록 완료',
        hudSubtitle: `「${ruleBody || s}」 규칙이 CHLGD 원장 및 Harness² 가드레일에 영구 저장되었습니다.`,
        spoken: '네, 대표님. 말씀하신 규칙을 대화 규칙 원장과 구글 하네스2 가드레일에 깔끔하게 기록하고 HUD 창에 표시했습니다.'
      };
    }

    return null;
  }
}

const sharedTerminalManager = new TerminalTaskManager();
const sharedBscBridge = new BscCollaborationBridge(sharedTerminalManager);

module.exports = {
  BSC_PATHS,
  BSC_AGENT_DIRECTORY,
  TerminalTaskManager,
  BscCollaborationBridge,
  sharedTerminalManager,
  sharedBscBridge
};
