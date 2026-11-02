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
    keyFiles: ['AGENTS.md', 'README.md', 'CEO_LINK_MANUAL.md', '00.운영\\inventory.json', 'scripts\\Search-Skill.ps1']
  }
];

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

/** 3대 생태계 실측 현황 (존재 여부, 영역, 핵심 파일, 스킬 수, 인덱스 파일 수) */
function getEcosystemOverview() {
  const idx = buildIndex();
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
      skillCount: skills
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
    totals: { totalFiles, totalSkills }
  };
}

function tokenize(q) {
  return [...new Set(String(q || '').toLowerCase().split(/[\s,.;:/\\()\[\]"'`]+/).map(t => t.replace(/(?:을|를|이|가|은|는|에서|에|의|로|으로|좀)$/, '')).filter(t => t.length >= 2))];
}

function snippetFrom(text, terms) {
  const lower = text.toLowerCase();
  let at = -1;
  for (const t of terms) { at = lower.indexOf(t); if (at >= 0) break; }
  if (at < 0) return text.slice(0, 220).replace(/\s+/g, ' ').trim();
  return text.slice(Math.max(0, at - 90), at + 220).replace(/\s+/g, ' ').trim();
}

/**
 * 3대 생태계 통합 검색. 파일명·경로 일치 + .md/.txt 본문 일치 점수로 정렬합니다.
 * @param {string} query
 * @param {{limit?:number, rootIds?:string[], timeBudgetMs?:number}} options
 */
function searchEcosystems(query, options = {}) {
  const started = Date.now();
  const terms = tokenize(query);
  const limit = options.limit || 12;
  const budget = options.timeBudgetMs || 2500;
  const idx = buildIndex();
  const hits = [];
  if (!terms.length) return { ok: true, query, terms, results: [], elapsedMs: 0 };
  for (const r of idx.roots) {
    if (options.rootIds && !options.rootIds.includes(r.id)) continue;
    for (const file of r.files) {
      const rel = path.relative(r.root, file);
      const relLower = rel.toLowerCase();
      let score = 0;
      for (const t of terms) if (relLower.includes(t)) score += path.basename(file).toLowerCase().includes(t) ? 4 : 2;
      let snippet = '';
      if (TEXT_EXT.test(file) && Date.now() - started < budget) {
        try {
          const st = fs.statSync(file);
          if (st.size < 400 * 1024) {
            const text = fs.readFileSync(file, 'utf8');
            const lower = text.toLowerCase();
            let bodyHits = 0;
            for (const t of terms) if (lower.includes(t)) bodyHits++;
            if (bodyHits) { score += bodyHits * 1.5 + (bodyHits === terms.length ? 3 : 0); snippet = snippetFrom(text, terms); }
          }
        } catch (_) {}
      }
      if (score > 0) hits.push({ rootId: r.id, rootLabel: r.label, ecosystem: r.label, kind: (path.extname(file) || '.file').replace(/^\./, ''), path: file, rel, title: path.basename(file), snippet, score });
    }
  }
  hits.sort((a, b) => b.score - a.score || a.rel.length - b.rel.length);
  const sliced = hits.slice(0, limit);
  return { ok: true, query, terms, results: sliced, hits: sliced, totalMatches: hits.length, elapsedMs: Date.now() - started };
}

/** 검색 결과를 자비스 검색 결과 창(ResearchReport) 형식으로 변환 */
function toResearchReport(query, search, id) {
  const results = search.results || [];
  const sources = results.map((h, i) => ({ id: i + 1, title: `${h.title}  ·  ${h.rootLabel}`, url: 'file:///' + h.path.replace(/\\/g, '/'), domain: h.rel, readStatus: h.snippet ? '본문 일치' : '파일명·경로 일치' }));
  const sections = results.map((h, i) => ({ title: h.title, text: h.snippet || `경로: ${h.path}`, sourceId: i + 1 }));
  const byRoot = {};
  for (const h of results) byRoot[h.rootLabel] = (byRoot[h.rootLabel] || 0) + 1;
  return {
    id, query, title: query + ' · 우리 시스템 검색', status: results.length ? 'ready' : 'unavailable',
    createdAt: new Date().toISOString(), provider: 'BeausCreators · BeautifulWorld · Warehouse 로컬 검색',
    note: results.length ? `총 ${search.totalMatches}건 중 상위 ${results.length}건을 표시합니다 (${search.elapsedMs}ms). 출처를 누르면 파일이 열립니다.` : '3대 생태계에서 일치하는 문서를 찾지 못했습니다. 다른 키워드로 다시 말씀해 주세요.',
    summary: results.length ? Object.entries(byRoot).map(([k, v]) => `${k}: ${v}건`).join('\n') : '',
    keywords: search.terms || [], sections, sources, errors: []
  };
}

module.exports = { ROOTS, buildIndex, getEcosystemOverview, searchEcosystems, toResearchReport, tokenize };
