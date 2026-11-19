'use strict';

/**
 * @file C:\자비스\scripts\jarvis-agent-core.js
 * @description JARVIS v3.0 Multi-Turn ReAct Tool-Calling Brain
 *   - 12-Turn rolling conversation & tool memory
 *   - Up to 4 autonomous ReAct tool steps per turn (Search -> Verify -> Write to Notepad / CLI / Blender / MCP)
 *   - Native Gemini Function Calling (gemini-3.5-flash-lite / gemini-3.8-flash) + Local Ollama JSON Tool Router
 *   - Zero hardcoded Warehouse/parrot fallback templates
 */

const crypto = require('crypto');
const fs = require('fs');
const http = require('http');
const https = require('https');
const {
  getLastExternalWindow,
  listOpenWindows,
  writeToWindow,
  readWindowText,
  webSearchAndExtract,
  saveMemoFile
} = require('./computer-use-hands');
const {
  ideReadFile,
  ideWriteFile,
  ideEditFile,
  ideListDir,
  cliExecute,
  mcpCallExternal,
  blenderExecuteBpy,
  notebooklmAction,
  artcraftExecute
} = require('./jarvis-ide-cli-mcp-tools');
const { buildExecutiveNewsletter, fetchStockMarketIntelligence } = require('./jarvis-briefing-hub');
const { createReportBundle } = require('./office-doc-engine');

const conversationHistory = []; // [{ role: 'user'|'assistant', text, toolSummary, ts }]

function pushHistory(role, text, toolSummary = '') {
  conversationHistory.push({
    role,
    text: String(text || '').slice(0, 1200),
    toolSummary: String(toolSummary || '').slice(0, 400),
    ts: Date.now()
  });
  while (conversationHistory.length > 12) conversationHistory.shift();
}

function getConversationHistory() {
  return [...conversationHistory];
}

function resolveGeminiKey(cfg = {}) {
  if (cfg && cfg.geminiApiKey && String(cfg.geminiApiKey).trim()) return String(cfg.geminiApiKey).trim();
  if (process.env.GEMINI_API_KEY) return String(process.env.GEMINI_API_KEY).trim();
  if (process.env.GOOGLE_AI_STUDIO_API_KEY) return String(process.env.GOOGLE_AI_STUDIO_API_KEY).trim();
  for (const f of ['C:\\자비스\\.env', 'C:\\BeausCreators\\.env', 'C:\\BeausCreators\\.env.dev']) {
    try {
      if (fs.existsSync(f)) {
        const raw = fs.readFileSync(f, 'utf8');
        const m = raw.match(/^(?:GEMINI_API_KEY|GOOGLE_AI_STUDIO_API_KEY)=(.+)$/m);
        if (m && m[1]) return m[1].trim().replace(/^["']|["']$/g, '');
      }
    } catch (_) {}
  }
  return '';
}

const FUNCTION_DECLARATIONS = [
  {
    name: 'web_search_and_extract',
    description: '인터넷 웹 검색을 수행하여 정확한 원문·사실·최신 정보와 클릭 가능한 실제 웹 링크(URL) 목록을 가져옵니다. 링크 목록은 자동으로 대표님 화면의 클릭 가능한 링크 창에 표시됩니다. 사용자가 웹 브라우저를 직접 열어보길 원하면 openBrowser=true로 설정하세요.',
    parameters: {
      type: 'OBJECT',
      properties: {
        query: { type: 'STRING', description: '검색할 키워드 또는 문장' },
        openBrowser: { type: 'BOOLEAN', description: '실제 크롬 웹 브라우저 검색 창도 함께 띄울지 여부' }
      },
      required: ['query']
    }
  },
  {
    name: 'open_web_url',
    description: '특정 웹사이트 URL(https://...) 또는 구글/네이버 검색 페이지를 대표님의 기본 크롬 웹 브라우저 창으로 즉시 열어 줍니다. 사용자가 "웹 링크 열어줘", "사이트 열어줘", "브라우저로 띄워줘"라고 요청할 때 호출하세요.',
    parameters: {
      type: 'OBJECT',
      properties: {
        url: { type: 'STRING', description: '열어줄 전체 https:// URL (모르면 query를 사용)' },
        query: { type: 'STRING', description: 'URL 대신 구글 검색 페이지를 바로 열 때 사용할 검색어' }
      }
    }
  },
  {
    name: 'computer_write_to_window',
    description: '사용자의 컴퓨터에 열려 있는 메모장(notepad) 또는 사용자가 마우스로 클릭한 활성 창에 텍스트를 직접 작성(타이핑/붙여넣기)합니다. 메모장이 닫혀 있으면 자동으로 실행한 뒤 작성합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        targetApp: { type: 'STRING', description: '대상 프로그램 (예: "notepad" 또는 "active")' },
        content: { type: 'STRING', description: '창에 실제로 작성할 전체 본문 텍스트' },
        mode: { type: 'STRING', description: '"append" (이어 쓰기, 기본값) 또는 "replace" (전체 덮어쓰기)' }
      },
      required: ['targetApp', 'content']
    }
  },
  {
    name: 'computer_read_window',
    description: '현재 열려 있는 메모장(notepad)이나 사용자가 클릭한 활성 창에 적혀 있는 텍스트 내용을 읽어옵니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        targetApp: { type: 'STRING', description: '"notepad" 또는 "active"' }
      }
    }
  },
  {
    name: 'computer_list_windows',
    description: '현재 바탕화면에 열려 있는 모든 프로그램 창 목록과 마지막으로 클릭된 외부 창 정보를 조회합니다.',
    parameters: { type: 'OBJECT', properties: {} }
  },
  {
    name: 'memo_save_file',
    description: '메모 내용을 텍스트 파일(C:\\자비스\\data\\memos)로 저장하고 필요 시 메모장으로 엽니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        title: { type: 'STRING', description: '메모 제목' },
        content: { type: 'STRING', description: '저장할 본문 내용' },
        openInNotepad: { type: 'BOOLEAN', description: '저장 후 메모장으로 열지 여부' }
      },
      required: ['title', 'content']
    }
  },
  {
    name: 'ide_read_file',
    description: '로컬 프로젝트의 코드나 문서 파일을 읽습니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        filePath: { type: 'STRING', description: '읽을 파일 절대 경로' },
        startLine: { type: 'INTEGER', description: '시작 줄 번호' },
        endLine: { type: 'INTEGER', description: '끝 줄 번호' }
      },
      required: ['filePath']
    }
  },
  {
    name: 'ide_write_file',
    description: '새 코드/스크립트/문서 파일을 생성하거나 작성합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        filePath: { type: 'STRING', description: '생성/저장할 파일 절대 경로' },
        content: { type: 'STRING', description: '파일 전체 내용' },
        append: { type: 'BOOLEAN', description: '기존 파일 끝에 추가할지 여부' }
      },
      required: ['filePath', 'content']
    }
  },
  {
    name: 'ide_edit_file',
    description: '기존 파일의 특정 문자열(target)을 새 내용(replacement)으로 정밀 수정합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        filePath: { type: 'STRING', description: '수정할 파일 절대 경로' },
        target: { type: 'STRING', description: '교체할 기존 문자열' },
        replacement: { type: 'STRING', description: '새 문자열' }
      },
      required: ['filePath', 'target', 'replacement']
    }
  },
  {
    name: 'ide_list_dir',
    description: '지정한 폴더의 파일 및 하위 디렉토리 목록을 조회합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        dirPath: { type: 'STRING', description: '조회할 폴더 절대 경로' }
      }
    }
  },
  {
    name: 'cli_execute',
    description: 'PowerShell, Python, Node.js, Git 등 터미널 CLI 명령어를 실제로 실행하고 stdout/stderr 결과를 확인합니다. (삭제/초기화 명령은 자동 차단됨)',
    parameters: {
      type: 'OBJECT',
      properties: {
        command: { type: 'STRING', description: '실행할 PowerShell/CLI 명령어' },
        cwd: { type: 'STRING', description: '작업 폴더 경로 (기본 C:\\자비스)' }
      },
      required: ['command']
    }
  },
  {
    name: 'blender_execute_bpy',
    description: '100% 무료($0 오픈소스) 로컬 Blender 5.1/5.0 Python(bpy) API 또는 Blender MCP 소켓을 통해 3D 게임 모델링, 건물 외관, 인테리어(ArchViz), 재질·조명을 생성하고 .blend 파일로 저장·오픈합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        pythonCode: { type: 'STRING', description: '실행할 순수 Blender bpy 파이썬 스크립트 코드' },
        sceneName: { type: 'STRING', description: '저장할 씬 이름 (영문/한글)' },
        openGui: { type: 'BOOLEAN', description: '생성 후 Blender 창을 화면에 띄울지 여부 (기본 true)' }
      },
      required: ['pythonCode']
    }
  },
  {
    name: 'artcraft_execute',
    description: '바탕화면 및 로컬에 설치된 ArtCraft 크리에이티브 스위트 16종(통합런처, PhotoCraft, VectorCraft, FilmCraft, EffectCraft, DesignCraft, FigCraft, LightCraft, PdfCraft, WordCraft, DeckCraft, GridCraft, SoundCraft, CADCraft, SubCraft 3D Painter, Craft-Anything)의 앱 실행(launch), Headless CLI 작업(cli), 또는 내장 Stdio MCP 도구 호출(mcp)을 수행합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        appName: { type: 'STRING', description: '앱 이름 (예: "list", "photocraft", "vectorcraft", "filmcraft", "effectcraft", "designcraft", "figcraft", "lightcraft", "pdfcraft", "wordcraft", "deckcraft", "gridcraft", "soundcraft", "cadcraft", "subcraft", "craft-anything", "launcher")' },
        mode: { type: 'STRING', description: '"list" (전체 목록 확인) | "launch" (GUI 창 실행) | "cli" (CLI 명령어 실행) | "mcp" (내장 MCP 도구 호출)' },
        cliArgs: { type: 'ARRAY', items: { type: 'STRING' }, description: 'mode="cli"일 때 전달할 인자 배열 (예: ["commands"], ["convert", "in.svg", "out.png"])' },
        mcpTool: { type: 'STRING', description: 'mode="mcp"일 때 호출할 도구명 (목록 조회는 "tools/list", 실행은 "execute", "new_document", "save_document" 등)' },
        mcpArgs: { type: 'OBJECT', description: 'mode="mcp"일 때 전달할 JSON 인자 객체' },
        openFile: { type: 'STRING', description: 'mode="launch"일 때 함께 열 파일 경로 (선택)' },
        controlPort: { type: 'INTEGER', description: 'mode="launch"일 때 실시간 원격 제어 포트 (예: 7981, 선택)' }
      },
      required: ['appName', 'mode']
    }
  },
  {
    name: 'notebooklm_action',
    description: 'Google NotebookLM MCP 서버와 직접 연동하여 대표님의 노트북 목록 조회(list), 특정 노트북 질의(query), 새 리서치 노트북 생성(create), 웹 소스 추가(add_source)를 수행합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        action: { type: 'STRING', description: '"list" | "query" | "create" | "add_source"' },
        notebookId: { type: 'STRING', description: '대상 노트북 ID (query / add_source 시)' },
        query: { type: 'STRING', description: '노트북에 물어볼 질문 (query 시)' },
        title: { type: 'STRING', description: '새 노트북 제목 (create 시)' },
        url: { type: 'STRING', description: '추가할 URL 소스 (add_source 시)' }
      },
      required: ['action']
    }
  },
  {
    name: 'mcp_call_external',
    description: '설치된 외부 MCP 서버(notebooklm, notion-mcp-server, firebase-mcp-server, StitchMCP, comfy-mcp, blender, craft-anything)의 도구를 조회하거나 호출합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        serverName: { type: 'STRING', description: 'MCP 서버 이름 (목록 조회 시 "list")' },
        toolName: { type: 'STRING', description: '호출할 도구 이름 (도구 목록 조회 시 "tools/list")' },
        toolArgs: { type: 'OBJECT', description: '도구 인자 객체' }
      },
      required: ['serverName']
    }
  },
  {
    name: 'executive_newsletter',
    description: '오늘의 실시간 주요 뉴스 헤드라인, AI·비즈니스 동향, 일정 및 Gmail/구글 캘린더 브리핑을 뉴스레터 창으로 띄우고 요약해 줍니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        topic: { type: 'STRING', description: '특정 뉴스 주제 (비워두면 오늘의 종합 헤드라인)' }
      }
    }
  },
  {
    name: 'stock_market_intelligence',
    description: '국내(코스피·코스닥·삼성전자·SK하이닉스 등) 및 미국(나스닥·S&P500·엔비디아·테슬라·애플 등) 실시간 주가, 지수, 환율, 종목별 최신 증시 뉴스와 퀀트 시황을 조회하여 전용 창에 띄우고 브리핑합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        query: { type: 'STRING', description: '종목명, 티커, 또는 시황 질문 (예: "삼성전자 엔비디아 주가", "오늘 증시 시황")' }
      }
    }
  },
  {
    name: 'create_report_bundle',
    description: '조사·트렌드 분석·알고리즘 분석 결과를 Word(.docx) 파일과 인포그래픽 HTML(.html) 파일 2종으로 동시에 생성하여 바탕화면과 문서 폴더에 저장합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        title: { type: 'STRING', description: '보고서 제목' },
        summary: { type: 'STRING', description: '핵심 요약 (Executive Summary)' },
        sections: {
          type: 'ARRAY',
          description: '분석 섹션 배열 [{ heading, body }]',
          items: {
            type: 'OBJECT',
            properties: {
              heading: { type: 'STRING' },
              body: { type: 'STRING' }
            }
          }
        },
        sources: {
          type: 'ARRAY',
          description: '검증 출처 배열 [{ title, url }]',
          items: {
            type: 'OBJECT',
            properties: {
              title: { type: 'STRING' },
              url: { type: 'STRING' }
            }
          }
        }
      },
      required: ['title', 'summary']
    }
  },
  {
    name: 'present_dynamic_window',
    description: '자비스가 수집한 정보, 표, 코드, 요약 결과물을 상황에 맞는 크기(compact / standard / wide)의 유기적 결과 창으로 대표님 화면에 띄웁니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        title: { type: 'STRING', description: '결과 창 제목' },
        layoutSize: { type: 'STRING', description: '"compact" (간결한 요약 카드), "standard" (표준 리포트), "wide" (코드·상세 비교 분석)' },
        summary: { type: 'STRING', description: '상단 핵심 요약 문장' },
        sections: {
          type: 'ARRAY',
          description: '본문 섹션 배열 [{ title, text }]',
          items: {
            type: 'OBJECT',
            properties: {
              title: { type: 'STRING' },
              text: { type: 'STRING' }
            }
          }
        }
      },
      required: ['title', 'summary']
    }
  },
  {
    name: 'request_control_permission',
    description: '기존 중요 파일 덮어쓰기, 민감한 시스템 설정 변경, 대량 수정 등 마음대로 바꾸면 안 되는 중요한 작업을 수행하기 전에 대표님께 실시간 제어 승인을 요청합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        permissionSummary: { type: 'STRING', description: '무엇을 어떻게 변경하려는지 대표님께 보고할 설명' },
        deferredToolName: { type: 'STRING', description: '승인 시 실행할 도구 이름 (예: ide_edit_file, cli_execute, computer_write_to_window)' },
        deferredToolArgs: { type: 'OBJECT', description: '승인 시 전달할 도구 인자 객체' }
      },
      required: ['permissionSummary', 'deferredToolName']
    }
  },
  {
    name: 'ask_user_clarification',
    description: '사용자의 지시 대상이 불분명하거나 모르는 내용이어서 실행 전에 대표님께 한 문장으로 짧게 되물어야 할 때 호출합니다.',
    parameters: {
      type: 'OBJECT',
      properties: {
        question: { type: 'STRING', description: '대표님께 여쭤볼 짧고 명확한 질문 한 문장' }
      },
      required: ['question']
    }
  }
];

async function executeAgentTool(name, args = {}, cfg = {}, contextArtifacts = {}) {
  switch (name) {
    case 'web_search_and_extract': {
      const searchRes = await webSearchAndExtract(args);
      if (searchRes && searchRes.ok && Array.isArray(searchRes.results) && searchRes.results.length > 0) {
        const id = crypto.randomUUID();
        const sources = searchRes.results.map((r, i) => ({
          id: i + 1,
          title: r.title || r.url,
          url: r.url,
          domain: (() => { try { return new URL(r.url).hostname; } catch (_) { return 'web'; } })(),
          readStatus: '웹 링크 (클릭 시 브라우저 열림)'
        }));
        const sections = searchRes.results.map((r, i) => ({
          sourceId: i + 1,
          title: r.title || `웹 결과 ${i + 1}`,
          text: `${r.snippet || ''}\n링크: ${r.url}`.trim()
        }));
        contextArtifacts.research = {
          id,
          query: args.query || '웹 검색',
          title: `${args.query || '웹 검색'} · 웹 링크 및 요약`,
          layoutSize: 'standard',
          status: 'ready',
          createdAt: new Date().toISOString(),
          provider: searchRes.provider || 'Web Search',
          note: '하단 [연결된 원본 문서 및 출처] 링크를 클릭하시면 크롬 브라우저로 즉시 열립니다.',
          summary: sources.slice(0, 3).map(s => `[${s.id}] ${s.title} — ${s.url}`).join('\n'),
          keywords: [],
          sections,
          sources
        };
        contextArtifacts.researchId = id;
      }
      return searchRes;
    }
    case 'open_web_url': {
      const targetUrl = (args.url && /^https?:\/\//i.test(String(args.url).trim()))
        ? String(args.url).trim()
        : `https://www.google.com/search?q=${encodeURIComponent(String(args.query || '구글'))}`;
      const { execFile } = require('child_process');
      const launcherExe = 'C:\\자비스\\bin\\JarvisDesktopLaunch.exe';
      await new Promise(resolve => {
        if (fs.existsSync(launcherExe)) {
          execFile(launcherExe, [targetUrl], { windowsHide: true, timeout: 6000 }, () => resolve());
        } else {
          execFile('cmd.exe', ['/c', 'start', '', targetUrl], { windowsHide: true, timeout: 6000 }, () => resolve());
        }
      });
      return { ok: true, openedUrl: targetUrl };
    }
    case 'computer_write_to_window':
      return await writeToWindow(args);
    case 'computer_read_window':
      return await readWindowText(args);
    case 'computer_list_windows':
      return await listOpenWindows();
    case 'memo_save_file':
      return await saveMemoFile(args);
    case 'ide_read_file':
      return ideReadFile(args);
    case 'ide_write_file':
      return ideWriteFile(args);
    case 'ide_edit_file':
      return ideEditFile(args);
    case 'ide_list_dir':
      return ideListDir(args);
    case 'cli_execute':
      return await cliExecute(args);
    case 'blender_execute_bpy':
      return await blenderExecuteBpy(args);
    case 'artcraft_execute':
      return await artcraftExecute(args);
    case 'notebooklm_action':
      return await notebooklmAction(args);
    case 'mcp_call_external':
      return await mcpCallExternal(args);
    case 'executive_newsletter': {
      const nl = await buildExecutiveNewsletter({ topic: args.topic || '', cfg });
      contextArtifacts.research = nl.report;
      contextArtifacts.researchId = nl.report.id;
      return { ok: true, summary: nl.report.summary, spokenHint: nl.spoken };
    }
    case 'stock_market_intelligence': {
      const st = await fetchStockMarketIntelligence({ query: args.query || '' });
      contextArtifacts.research = st.report;
      contextArtifacts.researchId = st.report.id;
      return { ok: true, quotes: st.quotes, summary: st.report.summary, spokenHint: st.spoken };
    }
    case 'create_report_bundle': {
      const bundle = await createReportBundle({
        title: args.title || '자비스 조사 보고서',
        summary: args.summary || '',
        sections: Array.isArray(args.sections) ? args.sections : [],
        sources: Array.isArray(args.sources) ? args.sources : []
      }, { copyToDesktop: true });
      return { ok: true, docxPath: bundle.docx.path, htmlPath: bundle.html.path, desktopCopies: bundle.desktopCopies };
    }
    case 'present_dynamic_window': {
      const id = crypto.randomUUID();
      const secs = Array.isArray(args.sections) ? args.sections.map((s, idx) => ({
        sourceId: idx + 1,
        title: s.title || `섹션 ${idx + 1}`,
        text: s.text || ''
      })) : [{ sourceId: 1, title: args.title || '요약', text: args.summary || '' }];
      const report = {
        id,
        query: args.title || '자비스 동적 브리핑',
        title: args.title || 'JARVIS Dynamic Insight',
        layoutSize: ['compact', 'standard', 'wide'].includes(args.layoutSize) ? args.layoutSize : 'standard',
        status: 'ready',
        createdAt: new Date().toISOString(),
        provider: 'JARVIS Autonomous UI Presenter',
        note: `콘텐츠 분량과 성격에 맞춰 ${args.layoutSize || 'standard'} 레이아웃으로 유기적 생성된 창입니다.`,
        summary: args.summary || '',
        keywords: [],
        sections: secs,
        sources: []
      };
      contextArtifacts.research = report;
      contextArtifacts.researchId = id;
      return { ok: true, windowOpened: true, layoutSize: report.layoutSize, id };
    }
    case 'request_control_permission': {
      contextArtifacts.pendingPermission = {
        summary: String(args.permissionSummary || '중요 제어 작업'),
        toolName: String(args.deferredToolName || ''),
        toolArgs: args.deferredToolArgs || {}
      };
      return { ok: true, awaitingUserApproval: true };
    }
    case 'ask_user_clarification':
      contextArtifacts.clarificationQuestion = String(args.question || '어떤 방식으로 진행할지 말씀해 주세요.');
      return { ok: true, asked: contextArtifacts.clarificationQuestion };
    default:
      return { ok: false, error: `알 수 없는 도구: ${name}` };
  }
}

function callGeminiTurn(apiKey, model, systemText, contents, timeoutMs = 8500) {
  return new Promise(resolve => {
    const generationConfig = {
      temperature: 0.2,
      maxOutputTokens: 900,
      ...(model.startsWith('gemini-3') ? { thinkingConfig: { thinkingLevel: 'LOW' } } : {})
    };
    const payload = JSON.stringify({
      systemInstruction: { parts: [{ text: systemText }] },
      contents,
      tools: [{ functionDeclarations: FUNCTION_DECLARATIONS }],
      generationConfig
    });
    const req = https.request(
      {
        hostname: 'generativelanguage.googleapis.com',
        path: `/v1beta/models/${encodeURIComponent(model)}:generateContent?key=${encodeURIComponent(apiKey)}`,
        method: 'POST',
        timeout: timeoutMs,
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload)
        }
      },
      res => {
        let out = '';
        res.on('data', d => (out += d.toString('utf8')));
        res.on('end', () => {
          if (res.statusCode !== 200) return resolve({ ok: false, status: res.statusCode, raw: out.slice(0, 300) });
          try {
            const parsed = JSON.parse(out);
            const candidateContent = parsed?.candidates?.[0]?.content;
            if (!candidateContent || !Array.isArray(candidateContent.parts)) {
              return resolve({ ok: false, error: 'empty-candidate' });
            }
            return resolve({ ok: true, model, content: candidateContent });
          } catch (e) {
            return resolve({ ok: false, error: e.message });
          }
        });
      }
    );
    req.on('timeout', () => { req.destroy(); resolve({ ok: false, error: 'timeout' }); });
    req.on('error', e => resolve({ ok: false, error: e.message }));
    req.write(payload);
    req.end();
  });
}

function callOllamaAgentFallback(systemText, promptText, timeoutMs = 9500) {
  return new Promise(resolve => {
    const toolGuide = `사용 가능한 도구 JSON 형식:
도구가 필요하면 반드시 첫 줄에 {"tool":"도구명","args":{...}} 만 출력하고, 일반 대화 답변이면 자연스러운 한국어 1~2문장만 출력하세요.
지원 도구:
- {"tool":"web_search_and_extract","args":{"query":"검색어","openBrowser":false}}
- {"tool":"computer_write_to_window","args":{"targetApp":"notepad","content":"작성할 본문","mode":"append"}}
- {"tool":"computer_read_window","args":{"targetApp":"notepad"}}
- {"tool":"cli_execute","args":{"command":"명령어","cwd":"C:\\\\자비스"}}
- {"tool":"blender_execute_bpy","args":{"pythonCode":"bpy 코드","sceneName":"scene","openGui":true}}
- {"tool":"executive_newsletter","args":{"topic":""}}`;

    const payload = JSON.stringify({
      model: 'gemma4:e4b',
      stream: false,
      think: false,
      keep_alive: '15m',
      messages: [
        { role: 'system', content: `${systemText}\n\n${toolGuide}` },
        { role: 'user', content: promptText }
      ],
      options: { num_predict: 350, temperature: 0.2 }
    });
    const req = http.request(
      {
        hostname: '127.0.0.1',
        port: 11434,
        path: '/api/chat',
        method: 'POST',
        timeout: timeoutMs,
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(payload)
        }
      },
      res => {
        let b = '';
        res.on('data', d => (b += d.toString('utf8')));
        res.on('end', () => {
          try {
            const p = JSON.parse(b);
            const txt = String(p?.message?.content || '').trim();
            if (txt) return resolve({ ok: true, text: txt });
          } catch (_) {}
          resolve({ ok: false });
        });
      }
    );
    req.on('timeout', () => { req.destroy(); resolve({ ok: false }); });
    req.on('error', () => resolve({ ok: false }));
    req.write(payload);
    req.end();
  });
}

/**
 * Run the JARVIS v3.0 Autonomous ReAct Loop
 */
async function runJarvisAgentTurn(userSentence, cfg = {}, executeOsActions = true, onAsyncProgress = null) {
  const startedAt = Date.now();
  const user = cfg.userName || '이건우 대표님';
  const lastWin = getLastExternalWindow();
  const recentCtx = conversationHistory
    .slice(-6)
    .map(h => `${h.role === 'user' ? '대표님' : '자비스'}: ${h.text}${h.toolSummary ? ` [실행됨: ${h.toolSummary}]` : ''}`)
    .join('\n');

  const systemText = [
    `당신은 ${user}의 전담 AI 에이전트 '자비스(JARVIS v3.0)'입니다.`,
    `단순 챗봇이 아니라 사용자의 Windows 11 컴퓨터(메모장 작성/읽기, 창 제어), 웹 검색, IDE 파일 읽기/쓰기/수정, 실시간 CLI 터미널, Blender 3D(bpy 무료 오픈소스), 외부 MCP 도구를 직접 실행하는 진짜 에이전트입니다.`,
    `핵심 행동 원칙:`,
    `1. 절대 사내 Warehouse나 특정 문서를 강제로 언급하지 마세요. 오직 대표님이 지금 지시하신 의도에만 집중하세요.`,
    `2. 메모장(notepad)이나 클릭한 창에 특정 문헌·원문·자료를 작성하라는 지시(예: "메모장에 중용 23장 써줘")를 받으면, 기억에 의존해 지어내지 말고 먼저 web_search_and_extract 도구로 정확한 원문과 풀이를 확보한 뒤 computer_write_to_window 도구로 창에 실제로 작성하세요.`,
    `3. 기존 중요 파일 덮어쓰기, 중요 설정 변경 등 마음대로 바꾸면 안 되는 민감 작업은 실행 전 request_control_permission 도구로 대표님께 "이런 부분을 제어해도 되겠습니까?"라고 승인을 요청하세요.`,
    `4. 수집한 정보나 작업 결과를 시각적으로 보여드리면 좋을 때는 present_dynamic_window 도구로 분량에 맞는 크기(compact/standard/wide)의 창을 유기적으로 띄워 드리세요.`,
    `5. 지시 대상이나 의미가 정말로 모호할 때만 ask_user_clarification으로 짧게 질문하세요.`,
    `6. 실행하지 않은 작업을 완료했다고 거짓 보고하지 마세요. 도구 실행이 성공한 뒤에만 1~2문장으로 간결하고 자연스럽게 결과를 보고하세요.`,
    `7. 대표님이 BeausCreators 에이전트(예: 수석비서 소하, 비서실장 태준, CTO 거누, 수석디자이너 시안, 프론트엔드 유나, 출판에디터 서아, CFO 도현, COO 시온, CMO 나린, QA 수호 등)를 호출하거나 대화를 요청하시면 해당 페르소나의 역할과 말투로 응답하되, 문장 앞에 "소하: ..." 또는 "태준: ..." 형식으로 화자를 명시하세요(자비스 TTS 엔진이 자동으로 여성 에이전트는 ko-KR-SunHiNeural 여성 보이스, 남성 에이전트는 ko-KR-InJoonNeural/Hyunsu 남성 보이스로 분리 발화합니다).`,
    lastWin.process ? `현재 대표님이 직전에 클릭/사용하신 외부 창: 프로세스=${lastWin.process}, 제목="${lastWin.title}", HWND=${lastWin.hwnd}` : '',
    recentCtx ? `최근 대화 맥락:\n${recentCtx}` : ''
  ].filter(Boolean).join('\n');

  const contextArtifacts = {};
  const executedTools = [];
  const apiKey = resolveGeminiKey(cfg);

  if (apiKey && process.env.JARVIS_DISABLE_EXTERNAL_HUB !== '1') {
    const contents = [{ role: 'user', parts: [{ text: userSentence }] }];
    const modelsToTry = ['gemini-3.5-flash-lite', 'gemini-3.8-flash'];

    for (let step = 0; step < 4; step++) {
      let turnRes = null;
      for (const m of modelsToTry) {
        turnRes = await callGeminiTurn(apiKey, m, systemText, contents, 8500);
        if (turnRes && turnRes.ok) break;
      }
      if (!turnRes || !turnRes.ok) break;

      contents.push(turnRes.content);
      const parts = turnRes.content.parts || [];
      const fnCalls = parts.filter(p => p.functionCall);

      if (fnCalls.length === 0) {
        const finalText = parts.filter(p => !p.thought && p.text).map(p => p.text).join('').trim();
        if (finalText) {
          pushHistory('user', userSentence);
          pushHistory('assistant', finalText, executedTools.join(', '));
          return {
            ok: true,
            text: finalText,
            executedTools,
            provider: 'jarvis-agent-gemini',
            model: turnRes.model,
            elapsedMs: Date.now() - startedAt,
            ...contextArtifacts
          };
        }
        break;
      }

      // Execute requested tool calls
      const responseParts = [];
      for (const part of fnCalls) {
        const { name, args } = part.functionCall;
        if (onAsyncProgress && step === 0) {
          onAsyncProgress(name, args);
        }
        let toolOutput;
        if (!executeOsActions && ['computer_write_to_window', 'cli_execute', 'blender_execute_bpy', 'ide_write_file', 'ide_edit_file'].includes(name)) {
          toolOutput = { ok: true, dryRun: true, note: '시험 모드(executeOs=false)이므로 실제 변경은 생략했습니다.' };
        } else {
          try {
            toolOutput = await executeAgentTool(name, args || {}, cfg, contextArtifacts);
          } catch (err) {
            toolOutput = { ok: false, error: err.message };
          }
        }
        executedTools.push(`${name}(${toolOutput && toolOutput.ok ? 'OK' : 'ERR'})`);
        if (toolOutput && toolOutput.requiresApproval) {
          contextArtifacts.requiresApproval = toolOutput;
        }
        responseParts.push({
          functionResponse: {
            name,
            response: { result: toolOutput }
          }
        });
      }
      contents.push({ role: 'user', parts: responseParts });

      if (contextArtifacts.pendingPermission) {
        const perm = contextArtifacts.pendingPermission;
        const askMsg = `대표님, ${perm.summary} 작업을 제어·실행해도 되겠습니까?`;
        pushHistory('user', userSentence);
        pushHistory('assistant', askMsg, 'request_control_permission');
        return {
          ok: true,
          text: askMsg,
          pendingPermission: perm,
          executedTools,
          provider: 'jarvis-agent-gemini',
          model: turnRes.model,
          elapsedMs: Date.now() - startedAt
        };
      }

      if (contextArtifacts.clarificationQuestion) {
        const qText = contextArtifacts.clarificationQuestion;
        pushHistory('user', userSentence);
        pushHistory('assistant', qText, 'ask_user_clarification');
        return {
          ok: true,
          text: qText,
          isClarification: true,
          executedTools,
          provider: 'jarvis-agent-gemini',
          model: turnRes.model,
          elapsedMs: Date.now() - startedAt
        };
      }
    }
  }

  // Offline / Secondary Fallback: Local Ollama JSON Tool Router (Zero Warehouse Parrot!)
  if (process.env.JARVIS_DISABLE_EXTERNAL_HUB !== '1') {
    const ollamaRes = await callOllamaAgentFallback(systemText, userSentence, 9500);
    if (ollamaRes && ollamaRes.ok && ollamaRes.text) {
      const firstLine = ollamaRes.text.split(/\r?\n/)[0].trim();
      if (firstLine.startsWith('{') && firstLine.includes('"tool"')) {
        try {
          const parsedTool = JSON.parse(firstLine);
          if (parsedTool.tool) {
            const out = executeOsActions
              ? await executeAgentTool(parsedTool.tool, parsedTool.args || {}, cfg, contextArtifacts)
              : { ok: true, dryRun: true };
            executedTools.push(`${parsedTool.tool}(${out && out.ok ? 'OK' : 'ERR'})`);
            const doneMsg = out && out.ok
              ? `네, 대표님. 요청하신 작업을 ${parsedTool.tool} 도구로 실행 완료했습니다.`
              : `작업 실행 중 확인이 필요합니다: ${(out && out.error) || '상태 점검 필요'}`;
            pushHistory('user', userSentence);
            pushHistory('assistant', doneMsg, executedTools.join(', '));
            return {
              ok: true,
              text: doneMsg,
              executedTools,
              provider: 'jarvis-agent-ollama',
              model: 'gemma4:e4b',
              fallback: true,
              elapsedMs: Date.now() - startedAt,
              ...contextArtifacts
            };
          }
        } catch (_) {}
      }
      pushHistory('user', userSentence);
      pushHistory('assistant', ollamaRes.text);
      return {
        ok: true,
        text: ollamaRes.text,
        executedTools,
        provider: 'jarvis-agent-ollama',
        model: 'gemma4:e4b',
        fallback: true,
        elapsedMs: Date.now() - startedAt
      };
    }
  }

  return null;
}

module.exports = {
  runJarvisAgentTurn,
  executeAgentTool,
  getConversationHistory,
  FUNCTION_DECLARATIONS
};
