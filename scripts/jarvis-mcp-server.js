#!/usr/bin/env node
/**
 * @file C:\자비스\scripts\jarvis-mcp-server.js
 * @description 자비스(JARVIS Sovereign Butler) ↔ Antigravity 2.0 IDE 양방향 Model Context Protocol (MCP) 서버
 *
 * 지원 전송 계층 (Dual-Transport JSON-RPC 2.0):
 *   1. Stdio MCP Server (`C:\Users\USER\.gemini\config\mcp_config.json` -> `jarvis-sovereign-butler`)
 *   2. Streamable HTTP MCP Handler (`POST http://127.0.0.1:7788/mcp` in `server.js`)
 *
 * 제공 MCP Tools:
 *   - `jarvis_status_and_profile`: 자비스 상태, 대표님 대화 규칙 원장, CHLGD 로그, C:\BeausCreators & C:\Warehouse 스킬 목록 조회
 *   - `jarvis_execute_command`: 자비스 Butler / 터미널 / CHLGD / 미디어 오더 실행
 *   - `jarvis_open_plan_hud`: Plan / 설계 문서 / 아키텍처 블루프린트를 자비스 바탕화면 `📋 Plan · 문서 프리뷰 HUD 창`에 즉시 렌더링
 *   - `jarvis_record_rule_or_log`: 대표님과의 대화에서 도출된 새로운 규칙이나 학습 선호도를 CHLGD + Google Harness²에 영구 기록
 *   - `jarvis_run_harness2_recursion`: Google Research Harness² (arXiv:2610.14224) 8-Surface 검증 및 자가진화(Propose-Probe-Compose) 실행
 */

'use strict';

const http = require('http');
const {
  getChlgdAndPlanDashboard,
  addConversationRule,
  createOrUpdatePlanDocument,
  triggerHudPlanWindowOpen,
  runJarvisHarness2EvolutionCycle
} = require('./jarvis-harness2-engine');
const { sharedBscBridge } = require('./bsc-collaboration-bridge');

const MCP_SERVER_INFO = {
  name: 'jarvis-sovereign-butler',
  version: '2.0.0'
};

const MCP_TOOLS = [
  {
    name: 'jarvis_status_and_profile',
    description: '이건우 대표님의 자비스(JARVIS) 시스템 상태, 대화 규칙 원장, CHLGD 학습 로그, Google Harness² 8-Surface 상태, 그리고 C:\\BeausCreators 및 C:\\Warehouse 스킬 인벤토리를 조회합니다.',
    inputSchema: {
      type: 'object',
      properties: {
        includeLogs: {
          type: 'boolean',
          description: '최근 CHLGD 오더 로그 포함 여부 (기본 true)'
        }
      }
    }
  },
  {
    name: 'jarvis_execute_command',
    description: '자비스 Butler 엔진 또는 실시간 터미널(PowerShell/Python/Git/CHLGD)에 명령을 전달하여 실행합니다.',
    inputSchema: {
      type: 'object',
      properties: {
        sentence: {
          type: 'string',
          description: '자비스에게 실행시킬 한국어 오더 또는 CLI 명령어 (예: "깃 상태 확인해줘", "CHLGD 코드 검증 돌려줘")'
        }
      },
      required: ['sentence']
    }
  },
  {
    name: 'jarvis_open_plan_hud',
    description: '대표님과 Plan(기획/설계/아키텍처/규칙 정리) 작업을 진행할 때 자비스의 바탕화면 전용 UI HUD 문서 뷰어 창(📋 Plan & CHLGD 뷰어)에 문서를 띄웁니다.',
    inputSchema: {
      type: 'object',
      properties: {
        title: {
          type: 'string',
          description: 'Plan 또는 문서 제목'
        },
        category: {
          type: 'string',
          description: '문서 분류 (예: System Architecture, CHLGD Plan, REA Analysis)'
        },
        summary: {
          type: 'string',
          description: '핵심 요약 문장'
        },
        sections: {
          type: 'array',
          description: '문서 섹션 목록 ([{ heading, body }])',
          items: {
            type: 'object',
            properties: {
              heading: { type: 'string' },
              body: { type: 'string' }
            }
          }
        },
        targetTab: {
          type: 'string',
          description: '열릴 탭 (plan | rules | harness2 | rea)'
        }
      },
      required: ['title']
    }
  },
  {
    name: 'jarvis_record_rule_or_log',
    description: '대표님과 대화하며 새로 정의된 규칙, 금지 사항, 선호도를 자비스 CHLGD 규칙 원장 및 Google Harness² guardrails/EXPERIENCE에 영구 기록합니다.',
    inputSchema: {
      type: 'object',
      properties: {
        ruleText: {
          type: 'string',
          description: '새로 기록할 대화 규칙 또는 대표님 지침'
        },
        category: {
          type: 'string',
          description: '규칙 카테고리 (예: 대화 규칙, CHLGD 작업 규칙, 음성/경청 규칙, 코딩/CI-CD 규칙)'
        }
      },
      required: ['ruleText']
    }
  },
  {
    name: 'jarvis_run_harness2_recursion',
    description: '자비스에 내장된 Google Research Harness² (arXiv:2610.14224) 8-Component Surface 무결성 검증 및 Propose[INV+DOM] -> Probe -> Compose (R=4k, P=3k) 자가진화 루프를 실행합니다.',
    inputSchema: {
      type: 'object',
      properties: {
        reason: {
          type: 'string',
          description: '자가진화 실행 사유'
        }
      }
    }
  }
];

function postLocalJarvisApi(pathname, payloadObj) {
  return new Promise(resolve => {
    const body = JSON.stringify(payloadObj || {});
    const req = http.request(
      {
        hostname: '127.0.0.1',
        port: Number(process.env.JARVIS_PORT || 7788),
        path: pathname,
        method: 'POST',
        timeout: 8000,
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(body)
        }
      },
      res => {
        let out = '';
        res.on('data', d => (out += d.toString('utf8')));
        res.on('end', () => {
          try {
            resolve(JSON.parse(out));
          } catch (_) {
            resolve({ ok: false, raw: out });
          }
        });
      }
    );
    req.on('error', err => resolve({ ok: false, error: err.message }));
    req.on('timeout', () => {
      req.destroy();
      resolve({ ok: false, error: 'timeout' });
    });
    req.write(body);
    req.end();
  });
}

/**
 * Core JSON-RPC 2.0 MCP Request Dispatcher (used by both Stdio and HTTP `/mcp`)
 */
async function handleMcpJsonRpcRequest(rpcReq, butlerInstance = null) {
  if (!rpcReq || typeof rpcReq !== 'object') {
    return {
      jsonrpc: '2.0',
      id: null,
      error: { code: -32600, message: 'Invalid Request' }
    };
  }

  const { id, method, params } = rpcReq;

  // Notifications have no id and expect no response
  if (id === undefined || id === null) {
    return null;
  }

  try {
    if (method === 'initialize') {
      return {
        jsonrpc: '2.0',
        id,
        result: {
          protocolVersion: (params && params.protocolVersion) || '2024-11-05',
          capabilities: {
            tools: { listChanged: false },
            resources: { listChanged: false }
          },
          serverInfo: MCP_SERVER_INFO
        }
      };
    }

    if (method === 'ping') {
      return { jsonrpc: '2.0', id, result: {} };
    }

    if (method === 'tools/list') {
      return {
        jsonrpc: '2.0',
        id,
        result: {
          tools: MCP_TOOLS
        }
      };
    }

    if (method === 'resources/list') {
      return {
        jsonrpc: '2.0',
        id,
        result: {
          resources: [
            {
              uri: 'jarvis://chlgd/dashboard',
              name: 'JARVIS CHLGD · 대화 규칙 · Google Harness² 통합 대시보드',
              mimeType: 'application/json'
            },
            {
              uri: 'jarvis://plan/active',
              name: 'JARVIS 현재 활성 Plan 문서 (HUD 뷰어 연동)',
              mimeType: 'text/markdown'
            }
          ]
        }
      };
    }

    if (method === 'resources/read') {
      const uri = params && params.uri;
      const dash = getChlgdAndPlanDashboard(sharedBscBridge);
      if (uri === 'jarvis://plan/active') {
        const doc = dash.activePlanDoc || {};
        const text = `# ${doc.title || 'Plan'}\n\n${doc.summary || ''}\n\n` +
          ((doc.sections || []).map(s => `## ${s.heading}\n${s.body}`).join('\n\n'));
        return {
          jsonrpc: '2.0',
          id,
          result: {
            contents: [{ uri, mimeType: 'text/markdown', text }]
          }
        };
      }
      return {
        jsonrpc: '2.0',
        id,
        result: {
          contents: [{ uri: uri || 'jarvis://chlgd/dashboard', mimeType: 'application/json', text: JSON.stringify(dash, null, 2) }]
        }
      };
    }

    if (method === 'tools/call') {
      const toolName = params && params.name;
      const args = (params && params.arguments) || {};

      if (toolName === 'jarvis_status_and_profile') {
        const dash = getChlgdAndPlanDashboard(sharedBscBridge);
        return {
          jsonrpc: '2.0',
          id,
          result: {
            content: [
              {
                type: 'text',
                text: JSON.stringify(dash, null, 2)
              }
            ]
          }
        };
      }

      if (toolName === 'jarvis_execute_command') {
        const sentence = String(args.sentence || '').trim();
        let execResult;
        if (butlerInstance && typeof butlerInstance.execute === 'function') {
          execResult = await butlerInstance.execute(sentence, true);
        } else {
          execResult = await postLocalJarvisApi('/api/butler/command', { sentence, executeOs: true, source: 'mcp-antigravity' });
        }
        return {
          jsonrpc: '2.0',
          id,
          result: {
            content: [
              {
                type: 'text',
                text: JSON.stringify(execResult, null, 2)
              }
            ]
          }
        };
      }

      if (toolName === 'jarvis_open_plan_hud') {
        const updatedState = createOrUpdatePlanDocument({
          title: args.title || '📋 Antigravity × JARVIS 공동 작업 Plan',
          category: args.category || 'Antigravity MCP Synced Plan',
          summary: args.summary || '',
          sections: Array.isArray(args.sections) ? args.sections : null,
          rawTopic: args.title || '',
          triggerOpenWindow: true,
          targetTab: args.targetTab || 'plan'
        });
        // Also notify running Jarvis HTTP server if called via Stdio process
        if (!butlerInstance) {
          await postLocalJarvisApi('/api/hud/plan-doc', {
            title: args.title,
            category: args.category,
            summary: args.summary,
            sections: args.sections,
            targetTab: args.targetTab || 'plan',
            openWindow: true
          }).catch(() => {});
        }
        return {
          jsonrpc: '2.0',
          id,
          result: {
            content: [
              {
                type: 'text',
                text: JSON.stringify(
                  {
                    ok: true,
                    message: '자비스 바탕화면 📋 Plan · 문서 HUD 창에 문서를 표시했습니다.',
                    openWindowSeq: updatedState.openWindowSeq,
                    activePlanDoc: updatedState.activePlanDoc
                  },
                  null,
                  2
                )
              }
            ]
          }
        };
      }

      if (toolName === 'jarvis_record_rule_or_log') {
        const updated = addConversationRule({
          ruleText: args.ruleText,
          category: args.category || 'Antigravity × 자비스 대화 규칙',
          source: 'Antigravity MCP Bridge'
        });
        if (!butlerInstance) {
          await postLocalJarvisApi('/api/hud/rules', {
            ruleText: args.ruleText,
            category: args.category || 'Antigravity × 자비스 대화 규칙'
          }).catch(() => {});
        }
        return {
          jsonrpc: '2.0',
          id,
          result: {
            content: [
              {
                type: 'text',
                text: JSON.stringify(
                  {
                    ok: true,
                    message: '대표님 대화 규칙 원장 및 Google Harness² guardrails에 영구 반영했습니다.',
                    totalRules: updated ? updated.conversationRules.length : 0
                  },
                  null,
                  2
                )
              }
            ]
          }
        };
      }

      if (toolName === 'jarvis_run_harness2_recursion') {
        const res = runJarvisHarness2EvolutionCycle(args.reason || 'Antigravity MCP Trigger');
        return {
          jsonrpc: '2.0',
          id,
          result: {
            content: [
              {
                type: 'text',
                text: JSON.stringify(res, null, 2)
              }
            ]
          }
        };
      }

      return {
        jsonrpc: '2.0',
        id,
        error: { code: -32601, message: `Unknown tool: ${toolName}` }
      };
    }

    return {
      jsonrpc: '2.0',
      id,
      error: { code: -32601, message: `Method not found: ${method}` }
    };
  } catch (err) {
    return {
      jsonrpc: '2.0',
      id,
      error: { code: -32603, message: err.message }
    };
  }
}

// Stdio loop when invoked directly by Antigravity IDE (`node C:\자비스\scripts\jarvis-mcp-server.js`)
if (require.main === module) {
  let buffer = '';
  process.stdin.setEncoding('utf8');
  process.stdin.on('data', async chunk => {
    buffer += chunk;
    const lines = buffer.split(/\r?\n/);
    buffer = lines.pop() || '';
    for (const line of lines) {
      const trimmed = line.trim();
      if (!trimmed) continue;
      try {
        const reqObj = JSON.parse(trimmed);
        const respObj = await handleMcpJsonRpcRequest(reqObj, null);
        if (respObj) {
          process.stdout.write(JSON.stringify(respObj) + '\n');
        }
      } catch (_) {}
    }
  });
}

module.exports = {
  MCP_SERVER_INFO,
  MCP_TOOLS,
  handleMcpJsonRpcRequest
};
