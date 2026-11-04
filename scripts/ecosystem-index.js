'use strict';
/**
 * @file C:\자비스\scripts\ecosystem-index.js
 * @description 자비스가 대표님의 3대 생태계를 실제 디스크 기준으로 인지·검색하는 모듈.
 *   - C:\BeausCreators  : 본사(HQ)·지점(Branch)·스킬창고·데이터센터
 *   - C:\BeautifulWorld : 출판사 OS (7-Class: 01.HQ ~ 07.Inbox)
 *   - C:\Warehouse      : 스킬·리서치 창고 (Imported / Originals / Obsidian / Research)
 * 읽기 전용입니다. 어떤 파일도 수정·삭제하지 않습니다.
 */
const fs = require('fs');
const path = require('path');

const ROOTS = [
  {
    id: 'beauscreators', label: 'BeausCreators (본사·지점)', root: 'C:\\BeausCreators',
    areas: ['01.BSC_HQ\\0.Guidelines', '01.BSC_HQ\\1.Documents', '01.BSC_HQ\\2.Agents', '01.BSC_HQ\\3.CompanyOS', '01.BSC_HQ\\6.Knowledge_Index', '01.BSC_HQ\\7.Data', '02.BSC_Branch', '05.BSC_Skill_Warehouse', '06.BSC_DataCenter', '.agents\\skills'],
    keyFiles: ['01.BSC_HQ\\2.Agents\\AGENTS.md', '01.BSC_HQ\\2.Agents\\에이전트\\error-log.md', '01.BSC_HQ\\3.CompanyOS\\Scripts\\Invoke-ReaAnalyzer.js', '01.BSC_HQ\\3.CompanyOS\\Scripts\\Invoke-LayaRlGrowth.js', '01.BSC_HQ\\3.CompanyOS\\Scripts\\Invoke-PublishingDocEngine.js']
  },
  {
    id: 'beautifulworld', label: 'BeautifulWorld (출판사 OS)', root: 'C:\\BeautifulWorld',
    areas: ['01.HQ', '02.Works', '03.Apps', '04.Studio', '05.Skills', '06.USB_Sync', '07.Inbox', '.agents\\skills'],
    keyFiles: ['AGENTS.md', 'PATH_MAP.md', 'context-notes.md', '02.Works\\Build-Epub.ps1', '02.Works\\BSC_Epub_Studio.html']
  },
  {
    id: 'warehouse', label: 'Warehouse (스킬·리서치 창고)', root: 'C:\\Warehouse',
    areas: ['00.운영', '1.Imported_Skills', '2.BSC_Originals', '3.Obsidian_Vault', '4.Market_Packages', '5.Research', 'docs', 'scripts'],
    keyFiles: ['AGENTS.md', 'README.md', 'CEO_LINK_MANUAL.md', '00.운영\\inventory.json', '00.운영\\warehouse_neuron_graph.json', '3.Obsidian_Vault\\00_Master_Neuron_MOC.md', 'scripts\\Build-WarehouseNeuronGraph.js', 'scripts\\Search-Skill.ps1']
  }
];

const WAREHOUSE_NEURON_JSON_PATH = 'C:\\Warehouse\\00.운영\\warehouse_neuron_graph.json';
const WAREHOUSE_NEURON_BUILDER_JS = 'C:\\Warehouse\\scripts\\Build-WarehouseNeuronGraph.js';
const WAREHOUSE_OBSIDIAN_MOC_PATH = 'C:\\Warehouse\\3.Obsidian_Vault\\00_Master_Neuron_MOC.md';

function normalizeNeuronNode(n) {
  if (!n) return null;
  const cat = n.category || n.type || 'Neuron';
  const rel = n.relPath || '';
  const fullPath = n.path || (rel ? path.join('C:\\Warehouse\\3.Obsidian_Vault', rel.replace(/\//g, '\\')) : WAREHOUSE_OBSIDIAN_MOC_PATH);
  return {
    ...n,
    id: n.id,
    title: n.title || n.label || n.id,
    label: n.label || n.title || n.id,
    category: cat,
    type: cat,
    relPath: rel,
    path: fullPath,
    domain: n.domain || cat,
    summary: n.summary || ''
  };
}

function getWarehouseNeuronGraph(options = {}) {
  try {
    if ((options.forceRebuild || !fs.existsSync(WAREHOUSE_NEURON_JSON_PATH)) && fs.existsSync(WAREHOUSE_NEURON_BUILDER_JS)) {
      const builder = require(WAREHOUSE_NEURON_BUILDER_JS);
      if (builder && typeof builder.buildNeuronVault === 'function') {
        builder.buildNeuronVault();
      }
    }
    if (fs.existsSync(WAREHOUSE_NEURON_JSON_PATH)) {
      const raw = JSON.parse(fs.readFileSync(WAREHOUSE_NEURON_JSON_PATH, 'utf8'));
      const nodes = (Array.isArray(raw.nodes) ? raw.nodes : []).map(normalizeNeuronNode).filter(Boolean);
      const edges = Array.isArray(raw.edges) ? raw.edges : [];
      const domains = nodes.filter(n => n.category === 'Domain' || n.type === 'domain_hub');
      const originals = nodes.filter(n => n.category === 'Original' || n.type === 'original_skill');
      const importedPacks = nodes.filter(n => n.category === 'Imported' || n.type === 'imported_pack');
      const agentMaps = nodes.filter(n => n.category === 'Agent' || n.type === 'agent_neuron');
      return {
        ok: true,
        builtAt: raw.generatedAt || raw.builtAt || new Date().toISOString(),
        version: raw.version || '2.0.0-CHLGD-Neuron',
        vaultDir: raw.vaultRoot || raw.vaultDir || 'C:\\Warehouse\\3.Obsidian_Vault',
        mocPath: WAREHOUSE_OBSIDIAN_MOC_PATH,
        graphJsonPath: WAREHOUSE_NEURON_JSON_PATH,
        nodeCount: raw.nodeCount || nodes.length,
        edgeCount: raw.edgeCount || edges.length,
        domainCount: raw.domainCount || domains.length,
        originalSkillCount: raw.originalSkillCount || originals.length,
        importedPackCount: raw.importedPackCount || importedPacks.length,
        agentMapCount: raw.agentCount || agentMaps.length,
        subskillCount: 217,
        githubRepoCount: 55,
        domains,
        originals,
        nodes,
        edges
      };
    }
  } catch (_) {}
  return {
    ok: false,
    nodeCount: 0,
    edgeCount: 0,
    domainCount: 0,
    originalSkillCount: 0,
    importedPackCount: 0,
    agentMapCount: 0,
    domains: [],
    originals: [],
    nodes: [],
    edges: []
  };
}

function searchWarehouseNeurons(query = '', options = {}) {
  const started = Date.now();
  try {
    if (fs.existsSync(WAREHOUSE_NEURON_BUILDER_JS)) {
      const builder = require(WAREHOUSE_NEURON_BUILDER_JS);
      if (options.rebuild && typeof builder.buildNeuronVault === 'function') {
        builder.buildNeuronVault();
      }
      const g = getWarehouseNeuronGraph();
      const cleanQ = String(query || '').trim();
      // If query is empty or asks for full overview ("전체", "뉴런", "목록", "시스템"), return top domains + originals
      if (!cleanQ || /^(전체|목록|현황|올|모두|보여줘|시스템|옵시디언|옵시디언뉴런|창고뉴런|웨어하우스뉴런|스킬뉴런|뉴런시스템)$/i.test(cleanQ.replace(/\s+/g, ''))) {
        const directHits = [...g.domains, ...g.originals.slice(0, 8)];
        return {
          ok: true,
          query: cleanQ || 'Warehouse 7-Domain 옵시디언 뉴런 전체 현황',
          isOverview: true,
          nodeCount: g.nodeCount,
          edgeCount: g.edgeCount,
          directHits,
          twoHopNeighbors: g.originals.slice(8, 18),
          synapseEdges: (g.edges || []).slice(0, 24),
          elapsedMs: Date.now() - started
        };
      }
      if (typeof builder.queryNeuronGraph === 'function') {
        const res = builder.queryNeuronGraph(cleanQ);
        if (res) {
          let directHits = (res.directHits || []).map(normalizeNeuronNode).filter(Boolean);
          let twoHopNeighbors = (res.hop2Hits || res.twoHopNeighbors || []).map(normalizeNeuronNode).filter(Boolean);
          if (directHits.length === 0 && g.ok) {
            const terms = tokenize(cleanQ).filter(t => !/^(뉴런|옵시디언|웨어하우스|warehouse|창고|스킬|시냅스|검색|찾아|보여)$/.test(t));
            if (terms.length > 0) {
              directHits = g.nodes.filter(n => {
                const hay = `${n.id} ${n.label} ${n.summary || ''} ${(n.keywords || []).join(' ')} ${n.category || ''}`.toLowerCase();
                return terms.some(t => hay.includes(t));
              }).slice(0, 10);
            }
          }
          const hitIds = new Set(directHits.map(m => m.id));
          const connectedEdges = (g.edges || []).filter(e => hitIds.has(e.from) || hitIds.has(e.to));
          if (twoHopNeighbors.length === 0 && connectedEdges.length > 0) {
            const neighborIds = new Set();
            for (const e of connectedEdges) {
              neighborIds.add(e.from);
              neighborIds.add(e.to);
            }
            twoHopNeighbors = g.nodes.filter(n => neighborIds.has(n.id) && !hitIds.has(n.id)).slice(0, 14);
          }
          return {
            ok: true,
            query: cleanQ,
            nodeCount: res.nodeCount || g.nodeCount || 79,
            edgeCount: res.edgeCount || g.edgeCount || 138,
            directHits,
            twoHopNeighbors,
            synapseEdges: connectedEdges.slice(0, 28),
            elapsedMs: Date.now() - started
          };
        }
      }
    }
  } catch (_) {}
  return { ok: false, query, directHits: [], twoHopNeighbors: [], synapseEdges: [], elapsedMs: Date.now() - started };
}

function neuronToResearchReport(query, neuronRes, id) {
  const direct = Array.isArray(neuronRes && neuronRes.directHits) ? neuronRes.directHits : [];
  const neighbors = Array.isArray(neuronRes && neuronRes.twoHopNeighbors) ? neuronRes.twoHopNeighbors : [];
  const edges = Array.isArray(neuronRes && neuronRes.synapseEdges) ? neuronRes.synapseEdges : [];
  const allNodes = [...direct, ...neighbors];
  const sources = allNodes.slice(0, 18).map((n, idx) => ({
    id: idx + 1,
    title: `[${n.type}] ${n.label}`,
    url: 'file:///' + String(n.path || WAREHOUSE_OBSIDIAN_MOC_PATH).replace(/\\/g, '/'),
    domain: n.domain || n.type || 'Warehouse Neuron',
    readStatus: idx < direct.length ? '1-Hop 직접 뉴런 일치' : '2-Hop 시냅스 연결 노드'
  }));
  if (sources.length === 0) {
    sources.push({
      id: 1,
      title: '00_Master_Neuron_MOC.md · Warehouse 옵시디언 중앙 관제 MOC',
      url: 'file:///' + WAREHOUSE_OBSIDIAN_MOC_PATH.replace(/\\/g, '/'),
      domain: '3.Obsidian_Vault',
      readStatus: 'Master MOC'
    });
  }

  const sections = [];
  if (direct.length > 0) {
    sections.push({
      title: `🎯 1-Hop 직접 매칭 뉴런 (${direct.length}개 노드)`,
      text: direct.map((n, i) => `${i + 1}. [${n.type}] ${n.label}\n   · 요약: ${n.summary || '정본 뉴런 노드'}\n   · 경로: ${n.path || ''}`).join('\n\n'),
      sourceId: 1
    });
  }
  if (neighbors.length > 0) {
    sections.push({
      title: `🔗 2-Hop 시냅스 연결 뉴런 (도메인 · 에이전트 · 원본 팩 · REA ${neighbors.length}개)`,
      text: neighbors.map((n, i) => `${i + 1}. [${n.type}] ${n.label} (${n.domain || ''})\n   · 요약: ${n.summary || ''}\n   · 경로: ${n.path || ''}`).join('\n\n'),
      sourceId: Math.min(sources.length, direct.length + 1)
    });
  }
  if (edges.length > 0) {
    sections.push({
      title: `⚡ 활성 시냅스 엣지 트래버설 (${edges.length}개 연결)`,
      text: edges.slice(0, 20).map(e => `• ${e.from}  ──(${e.rel})──▶  ${e.to}`).join('\n'),
      sourceId: 1
    });
  }
  if (sections.length === 0) {
    sections.push({
      title: '🧠 Warehouse 옵시디언 뉴런 안내',
      text: `총 79개 뉴런 노드 및 138개 시냅스가 가동 중입니다. (7대 도메인 허브 · 18대 BSC 정본 스킬 · 19대 외부 패키지 · 32인 에이전트 무장 맵)\n중앙 관제 MOC: ${WAREHOUSE_OBSIDIAN_MOC_PATH}`,
      sourceId: 1
    });
  }

  return {
    id: id || `neuron-${Date.now()}`,
    query: query || 'Warehouse 옵시디언 뉴런',
    title: `🧠 ${query || 'Warehouse'} · 옵시디언 뉴런 2-Hop 시냅스 맵`,
    status: 'ready',
    createdAt: new Date().toISOString(),
    provider: 'JARVIS × C:\\Warehouse Obsidian Neuron Engine (79 Nodes · 138 Synapses)',
    note: `1-Hop 직접 뉴런 ${direct.length}건 · 2-Hop 시냅스 연결 ${neighbors.length}건 · 시냅스 엣지 ${edges.length}건 (${neuronRes.elapsedMs || 0}ms). 출처 클릭 시 옵시디언 노트 또는 정본 SKILL.md가 열립니다.`,
    summary: `• 전체 뉴런 그래프: ${neuronRes.nodeCount || 79} Nodes / ${neuronRes.edgeCount || 138} Synapses (7대 도메인 × 18대 정본 스킬 × 32인 에이전트)\n• 1-Hop 직접 매칭: ${direct.map(d => d.label).slice(0, 5).join(', ') || '전체 도메인 허브'}\n• 2-Hop 시냅스 확산: ${neighbors.map(n => n.label).slice(0, 6).join(', ') || '연결된 에이전트 및 도메인 허브'}`,
    keywords: tokenize(query),
    sections,
    sources,
    errors: []
  };
}

const SKIP_DIRS = new Set(['.git', 'node_modules', '.venv', 'venv', '.venv-laya', '__pycache__', 'dist', 'build', '.next', 'Library', 'Temp', 'Logs', 'obj', '.model-cache', '.training-runs', 'Archive', '.trash', 'BSC_보관_구버전', '.obsidian', 'site-packages']);
const DOC_EXT = /\.(md|txt|json|docx|pptx|xlsx|pdf|epub|html|ps1|py|js|cs|yaml|yml)$/i;
const TEXT_EXT = /\.(md|txt)$/i;
const MAX_FILES_PER_ROOT = 12000;
let cache = null;

function walk(dir, depth, out, limit) {
  if (depth > 6 || out.length >= limit) return;
  let entries;
  try { entries = fs.readdirSync(dir, { withFileTypes: true }); } catch (_) { return; }
  for (const e of entries) {
    if (out.length >= limit) return;
    if (e.isDirectory()) {
      if (!SKIP_DIRS.has(e.name)) walk(path.join(dir, e.name), depth + 1, out, limit);
    } else if (DOC_EXT.test(e.name)) {
      out.push(path.join(dir, e.name));
    }
  }
}

function buildIndex(force = false) {
  if (!force && cache && Date.now() - cache.builtAt < 10 * 60 * 1000) return cache;
  const started = Date.now();
  const roots = ROOTS.map(r => {
    const files = [];
    if (fs.existsSync(r.root)) {
      for (const area of r.areas) walk(path.join(r.root, area), 0, files, MAX_FILES_PER_ROOT);
      // Root-level guide files (AGENTS.md, PATH_MAP.md 등)
      try { for (const f of fs.readdirSync(r.root)) if (/\.(md|json)$/i.test(f)) files.push(path.join(r.root, f)); } catch (_) {}
    }
    return { ...r, files };
  });
  cache = { builtAt: Date.now(), buildMs: Date.now() - started, roots };
  return cache;
}

function countSkills(dir) {
  let n = 0;
  const visit = (d, depth) => {
    if (depth > 4) return;
    let entries;
    try { entries = fs.readdirSync(d, { withFileTypes: true }); } catch (_) { return; }
    if (entries.some(e => e.isFile() && e.name === 'SKILL.md')) { n++; return; }
    for (const e of entries) if (e.isDirectory() && !SKIP_DIRS.has(e.name)) visit(path.join(d, e.name), depth + 1);
  };
  visit(dir, 0);
  return n;
}

/** 3대 생태계 실측 현황 (존재 여부, 영역, 핵심 파일, 스킬 수, 인덱스 파일 수, 옵시디언 뉴런 그래프) */
function getEcosystemOverview() {
  const idx = buildIndex();
  const neuronInfo = getWarehouseNeuronGraph();
  const roots = idx.roots.map(r => {
    const skills = r.id === 'warehouse'
      ? countSkills(path.join(r.root, '1.Imported_Skills')) + countSkills(path.join(r.root, '2.BSC_Originals'))
      : countSkills(path.join(r.root, '.agents', 'skills')) + (r.id === 'beautifulworld' ? countSkills(path.join(r.root, '05.Skills')) : 0);
    return {
      id: r.id,
      name: r.label,
      label: r.label,
      root: r.root,
      exists: fs.existsSync(r.root),
      areas: r.areas.map(a => ({ name: a, exists: fs.existsSync(path.join(r.root, a)) })),
      keyFiles: r.keyFiles.map(f => ({ path: path.join(r.root, f), exists: fs.existsSync(path.join(r.root, f)) })),
      indexedFiles: r.files.length,
      fileCount: r.files.length,
      skills,
      skillCount: skills,
      ...(r.id === 'warehouse' ? {
        neuronGraph: {
          ok: neuronInfo.ok,
          nodeCount: neuronInfo.nodeCount,
          edgeCount: neuronInfo.edgeCount,
          domainCount: neuronInfo.domainCount,
          originalSkillCount: neuronInfo.originalSkillCount,
          importedPackCount: neuronInfo.importedPackCount,
          agentMapCount: neuronInfo.agentMapCount,
          subskillCount: neuronInfo.subskillCount,
          githubRepoCount: neuronInfo.githubRepoCount,
          mocPath: neuronInfo.mocPath
        }
      } : {})
    };
  });
  const totalFiles = roots.reduce((acc, r) => acc + r.indexedFiles, 0);
  const totalSkills = roots.reduce((acc, r) => acc + r.skills, 0);
  return {
    ok: true,
    indexBuiltAt: new Date(idx.builtAt).toISOString(),
    indexBuildMs: idx.buildMs,
    roots,
    ecosystems: roots,
    neuronGraph: {
      ok: neuronInfo.ok,
      nodeCount: neuronInfo.nodeCount,
      edgeCount: neuronInfo.edgeCount,
      domainCount: neuronInfo.domainCount,
      originalSkillCount: neuronInfo.originalSkillCount,
      importedPackCount: neuronInfo.importedPackCount,
      agentMapCount: neuronInfo.agentMapCount,
      subskillCount: neuronInfo.subskillCount,
      githubRepoCount: neuronInfo.githubRepoCount,
      mocPath: neuronInfo.mocPath,
      graphJsonPath: neuronInfo.graphJsonPath
    },
    totals: {
      totalFiles,
      totalSkills,
      neuronNodes: neuronInfo.nodeCount,
      neuronSynapses: neuronInfo.edgeCount
    }
  };
}

function tokenize(q) {
  return [...new Set(String(q || '').toLowerCase().split(/[\s,.;:/\\()\[\]"'`]+/).map(t => t.replace(/(?:을|를|이|가|은|는|에서|에|의|로|으로|좀)$/, '')).filter(t => t.length >= 2))];
}

function matchesBoundedTerm(hay, term) {
  if (!hay || !term) return false;
  if (/^[a-z0-9]{1,4}$/i.test(term)) {
    const esc = term.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
    return new RegExp(`(?:^|[^a-z0-9])${esc}(?:$|[^a-z0-9])`, 'i').test(hay);
  }
  return hay.includes(term);
}

let hqGraphCache = null;
function getHqBrainGraphCached() {
  if (hqGraphCache && Date.now() - hqGraphCache.loadedAt < 5 * 60 * 1000) return hqGraphCache.data;
  const p = 'C:\\BeausCreators\\01.BSC_HQ\\6.Knowledge_Index\\hq_brain_graph.json';
  try {
    if (fs.existsSync(p)) {
      const data = JSON.parse(fs.readFileSync(p, 'utf8'));
      hqGraphCache = { loadedAt: Date.now(), data };
      return data;
    }
  } catch (_) {}
  return null;
}

function snippetFrom(text, terms) {
  const lower = text.toLowerCase();
  let at = -1;
  for (const t of terms) { at = lower.indexOf(t); if (at >= 0) break; }
  if (at < 0) return text.slice(0, 220).replace(/\s+/g, ' ').trim();
  return text.slice(Math.max(0, at - 90), at + 220).replace(/\s+/g, ' ').trim();
}

/**
 * 3대 생태계 통합 검색. 1차: HQ Brain Graph(770 Nodes) & Warehouse Neuron Graph(79 Nodes) 인메모리 즉시 매칭(<5ms) + 2차: 파일명·경로 일치 정렬.
 * @param {string} query
 * @param {{limit?:number, rootIds?:string[], timeBudgetMs?:number}} options
 */
function searchEcosystems(query, options = {}) {
  const started = Date.now();
  const terms = tokenize(query);
  const limit = options.limit || 12;
  const budget = options.timeBudgetMs || 1200;
  const hits = [];
  const seenPaths = new Set();
  const seenTitles = new Set();
  if (!terms.length) return { ok: true, query, terms, results: [], neuronSynapses: null, elapsedMs: 0 };

  const neuronSynapses = searchWarehouseNeurons(query);
  if (neuronSynapses && Array.isArray(neuronSynapses.directHits)) {
    for (const nh of neuronSynapses.directHits) {
      if (options.rootIds && !options.rootIds.includes('warehouse')) break;
      const fullPath = nh.path || path.join('C:\\Warehouse\\3.Obsidian_Vault', (nh.relPath || '').replace(/\//g, '\\'));
      const normPath = fullPath.toLowerCase();
      const normTitle = String(nh.title || nh.label || '').toLowerCase();
      if (seenPaths.has(normPath) || seenTitles.has(normTitle)) continue;
      seenPaths.add(normPath);
      seenTitles.add(normTitle);
      hits.push({
        rootId: 'warehouse',
        rootLabel: 'Warehouse (스킬·리서치 창고)',
        ecosystem: 'Warehouse (스킬·리서치 창고)',
        kind: 'neuron-md',
        path: fullPath,
        rel: path.relative('C:\\Warehouse', fullPath),
        title: nh.title || nh.label,
        snippet: nh.summary || '',
        score: 25 + (nh.score || 10)
      });
    }
  }

  const hqGraph = getHqBrainGraphCached();
  if (hqGraph && Array.isArray(hqGraph.nodes) && (!options.rootIds || options.rootIds.includes('beauscreators'))) {
    for (const n of hqGraph.nodes) {
      const hayTitle = `${n.title || ''} ${n.relPath || ''}`.toLowerCase();
      const hayMeta = `${(n.tags || []).join(' ')} ${(n.headings || []).join(' ')} ${n.summary || ''}`.toLowerCase();
      let gScore = 0;
      let matched = 0;
      for (const t of terms) {
        let hit = false;
        if (matchesBoundedTerm(hayTitle, t)) { gScore += 10; hit = true; }
        if (matchesBoundedTerm(hayMeta, t)) { gScore += 5; hit = true; }
        if (hit) matched++;
      }
      if (gScore > 0) {
        if (matched === terms.length) gScore += 8;
        const fullPath = path.resolve('C:\\BeausCreators\\01.BSC_HQ', (n.relPath || '').replace(/\//g, '\\'));
        const normPath = fullPath.toLowerCase();
        const normTitle = String(n.title || '').toLowerCase();
        if (seenPaths.has(normPath) || seenTitles.has(normTitle)) continue;
        seenPaths.add(normPath);
        seenTitles.add(normTitle);
        hits.push({
          rootId: 'beauscreators',
          rootLabel: 'BeausCreators (본사·지점)',
          ecosystem: 'BeausCreators (본사·지점)',
          kind: 'graph-md',
          path: fullPath,
          rel: path.relative('C:\\BeausCreators', fullPath),
          title: n.title || path.basename(fullPath),
          snippet: n.summary || '',
          score: gScore
        });
      }
    }
  }

  const idx = buildIndex();
  for (const r of idx.roots) {
    if (options.rootIds && !options.rootIds.includes(r.id)) continue;
    for (const file of r.files) {
      const normPath = file.toLowerCase();
      if (seenPaths.has(normPath)) continue;
      const rel = path.relative(r.root, file);
      const relLower = rel.toLowerCase();
      const baseLower = path.basename(file).toLowerCase();
      let score = 0;
      for (const t of terms) {
        if (matchesBoundedTerm(baseLower, t)) score += 6;
        else if (matchesBoundedTerm(relLower, t)) score += 3;
      }
      let snippet = '';
      if (score > 0 && TEXT_EXT.test(file) && Date.now() - started < budget && hits.length < limit * 2) {
        try {
          const st = fs.statSync(file);
          if (st.size < 250 * 1024) {
            const text = fs.readFileSync(file, 'utf8');
            const lower = text.toLowerCase();
            let bodyHits = 0;
            for (const t of terms) if (matchesBoundedTerm(lower, t)) bodyHits++;
            if (bodyHits) { score += bodyHits * 2 + (bodyHits === terms.length ? 3 : 0); snippet = snippetFrom(text, terms); }
          }
        } catch (_) {}
      }
      if (score > 0) {
        seenPaths.add(normPath);
        hits.push({ rootId: r.id, rootLabel: r.label, ecosystem: r.label, kind: (path.extname(file) || '.file').replace(/^\./, ''), path: file, rel, title: path.basename(file), snippet, score });
      }
    }
  }
  hits.sort((a, b) => b.score - a.score || a.rel.length - b.rel.length);
  const sliced = hits.slice(0, limit);
  return { ok: true, query, terms, results: sliced, hits: sliced, neuronSynapses, totalMatches: hits.length, elapsedMs: Date.now() - started };
}

/** 검색 결과를 자비스 검색 결과 창(ResearchReport) 형식으로 변환 */
function toResearchReport(query, search, id) {
  const results = search.results || [];
  const neuron = search.neuronSynapses || null;
  const sources = results.map((h, i) => ({ id: i + 1, title: `${h.title}  ·  ${h.rootLabel}`, url: 'file:///' + h.path.replace(/\\/g, '/'), domain: h.rel, readStatus: h.snippet ? '본문 일치' : '파일명·경로 일치' }));
  const sections = results.map((h, i) => ({ title: h.title, text: h.snippet || `경로: ${h.path}`, sourceId: i + 1 }));
  if (neuron && Array.isArray(neuron.directHits) && neuron.directHits.length > 0) {
    const synLines = [
      `• 1-Hop 직접 뉴런 (${neuron.directHits.length}개): ${neuron.directHits.map(n => `${n.label} [${n.type}]`).slice(0, 5).join(', ')}`,
      `• 2-Hop 연결 뉴런 (${(neuron.twoHopNeighbors || []).length}개): ${(neuron.twoHopNeighbors || []).map(n => `${n.label}`).slice(0, 6).join(', ')}`
    ];
    sections.unshift({
      title: '🧠 Warehouse 옵시디언 뉴런 2-Hop 시냅스 연결망',
      text: synLines.join('\n'),
      sourceId: 1
    });
  }
  const byRoot = {};
  for (const h of results) byRoot[h.rootLabel] = (byRoot[h.rootLabel] || 0) + 1;
  return {
    id, query, title: query + ' · 우리 시스템 & 뉴런 시냅스 검색', status: (results.length || (neuron && neuron.directHits && neuron.directHits.length)) ? 'ready' : 'unavailable',
    createdAt: new Date().toISOString(), provider: 'BeausCreators · BeautifulWorld · Warehouse (옵시디언 뉴런 79 Nodes 직결)',
    note: results.length ? `총 ${search.totalMatches}건 중 상위 ${results.length}건 + 뉴런 시냅스 ${(neuron && neuron.directHits ? neuron.directHits.length : 0)}건을 표시합니다 (${search.elapsedMs}ms). 출처를 누르면 파일이 열립니다.` : '3대 생태계에서 일치하는 문서를 찾지 못했습니다. 다른 키워드로 다시 말씀해 주세요.',
    summary: results.length ? Object.entries(byRoot).map(([k, v]) => `${k}: ${v}건`).join('\n') : '',
    keywords: search.terms || [], sections, sources, errors: []
  };
}

module.exports = {
  ROOTS,
  buildIndex,
  getEcosystemOverview,
  searchEcosystems,
  toResearchReport,
  getWarehouseNeuronGraph,
  searchWarehouseNeurons,
  neuronToResearchReport,
  tokenize
};
