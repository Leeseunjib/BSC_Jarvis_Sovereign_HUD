'use strict';
/**
 * @file C:\자비스\scripts\office-doc-engine.js
 * @description 자비스 문서 엔진 — 외부 패키지 없이 Markdown(.md), Word(.docx), PowerPoint(.pptx), PDF(.pdf)를 생성합니다.
 *   - .docx / .pptx : OpenXML 패키지를 직접 구성 (ZIP Store 방식)
 *   - .pdf          : 한글 폰트가 정확히 들어가도록 HTML을 Edge/Chrome 헤드리스로 인쇄
 * 출력 위치: C:\자비스\docs\exports\YYYY-MM-DD\  (기존 파일은 덮어쓰지 않고 시각 접미사를 붙입니다)
 */
const fs = require('fs');
const path = require('path');
const { execFile } = require('child_process');

const EXPORT_ROOT = process.env.JARVIS_EXPORT_DIR || path.join(__dirname, '..', 'docs', 'exports');
const FORMATS = ['md', 'docx', 'pptx', 'pdf'];

// ---------------------------------------------------------------- ZIP (store)
const CRC_TABLE = (() => { const t = new Uint32Array(256); for (let n = 0; n < 256; n++) { let c = n; for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1; t[n] = c >>> 0; } return t; })();
function crc32(buf) { let c = -1; for (let i = 0; i < buf.length; i++) c = (c >>> 8) ^ CRC_TABLE[(c ^ buf[i]) & 0xff]; return (c ^ -1) >>> 0; }
function zip(entries) {
  const locals = [], centrals = []; let offset = 0;
  for (const e of entries) {
    const name = Buffer.from(e.name, 'utf8'), data = Buffer.isBuffer(e.data) ? e.data : Buffer.from(e.data, 'utf8'), crc = crc32(data);
    const lh = Buffer.alloc(30);
    lh.writeUInt32LE(0x04034b50, 0); lh.writeUInt16LE(20, 4); lh.writeUInt16LE(0x0800, 6); lh.writeUInt16LE(0, 8);
    lh.writeUInt32LE(0, 10); lh.writeUInt32LE(crc, 14); lh.writeUInt32LE(data.length, 18); lh.writeUInt32LE(data.length, 22);
    lh.writeUInt16LE(name.length, 26); lh.writeUInt16LE(0, 28);
    const ch = Buffer.alloc(46);
    ch.writeUInt32LE(0x02014b50, 0); ch.writeUInt16LE(20, 4); ch.writeUInt16LE(20, 6); ch.writeUInt16LE(0x0800, 8); ch.writeUInt16LE(0, 10);
    ch.writeUInt32LE(0, 12); ch.writeUInt32LE(crc, 16); ch.writeUInt32LE(data.length, 20); ch.writeUInt32LE(data.length, 24);
    ch.writeUInt16LE(name.length, 28); ch.writeUInt32LE(0, 30); ch.writeUInt32LE(0, 34); ch.writeUInt32LE(0, 38); ch.writeUInt32LE(offset, 42);
    locals.push(lh, name, data); centrals.push(ch, name); offset += 30 + name.length + data.length;
  }
  const central = Buffer.concat(centrals), end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0); end.writeUInt16LE(entries.length, 8); end.writeUInt16LE(entries.length, 10);
  end.writeUInt32LE(central.length, 12); end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, central, end]);
}
const esc = s => String(s == null ? '' : s).replace(/[\u0000-\u0008\u000b\u000c\u000e-\u001f]/g, '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
const XML = '<?xml version="1.0" encoding="UTF-8" standalone="yes"?>\n';

// ---------------------------------------------------------------- Document model
/** doc = { title, subtitle, summary, sections:[{heading, body}], sources:[{title,url}] } */
function normalizeDoc(input) {
  const doc = input || {};
  return {
    title: String(doc.title || '자비스 문서').slice(0, 200),
    subtitle: String(doc.subtitle || `작성: 자비스 · ${new Date().toLocaleString('ko-KR')}`),
    summary: String(doc.summary || ''),
    sections: (Array.isArray(doc.sections) ? doc.sections : []).filter(s => s && (s.heading || s.body)).map(s => ({ heading: String(s.heading || ''), body: String(s.body || '') })),
    sources: (Array.isArray(doc.sources) ? doc.sources : []).filter(s => s && (s.title || s.url)).map(s => ({ title: String(s.title || s.url), url: String(s.url || '') }))
  };
}

/** 검색 결과(ResearchReport)를 문서 모델로 변환 */
function docFromResearch(report) {
  return normalizeDoc({
    title: report.title || report.query,
    subtitle: `${report.provider || '자비스 검색'} · ${new Date(report.createdAt || Date.now()).toLocaleString('ko-KR')}`,
    summary: report.summary || report.note || '',
    sections: [
      ...(report.sections || []).map(s => ({ heading: `[${s.sourceId}] ${s.title}`, body: s.text })),
      ...(report.note ? [{ heading: '참고', body: report.note }] : [])
    ],
    sources: (report.sources || []).map(s => ({ title: `[${s.id}] ${s.title}`, url: s.url }))
  });
}

// ---------------------------------------------------------------- Markdown
function toMarkdown(doc) {
  const lines = [`# ${doc.title}`, '', `> ${doc.subtitle}`, ''];
  if (doc.summary) lines.push('## 핵심 요약', '', doc.summary, '');
  for (const s of doc.sections) lines.push(`## ${s.heading}`, '', s.body, '');
  if (doc.sources.length) { lines.push('## 출처', ''); for (const s of doc.sources) lines.push(s.url ? `- [${s.title}](${s.url})` : `- ${s.title}`); lines.push(''); }
  return lines.join('\n');
}

// ---------------------------------------------------------------- DOCX
function docxParagraph(text, { style, bold, size, color } = {}) {
  const runs = String(text).split('\n').map((line, i) => `${i ? '<w:r><w:br/></w:r>' : ''}<w:r><w:rPr><w:rFonts w:ascii="Malgun Gothic" w:hAnsi="Malgun Gothic" w:eastAsia="Malgun Gothic"/>${bold ? '<w:b/>' : ''}${color ? `<w:color w:val="${color}"/>` : ''}${size ? `<w:sz w:val="${size}"/><w:szCs w:val="${size}"/>` : ''}</w:rPr><w:t xml:space="preserve">${esc(line)}</w:t></w:r>`).join('');
  return `<w:p>${style ? `<w:pPr><w:pStyle w:val="${style}"/></w:pPr>` : '<w:pPr><w:spacing w:after="120" w:line="300" w:lineRule="auto"/></w:pPr>'}${runs}</w:p>`;
}
function toDocx(doc) {
  const body = [
    docxParagraph(doc.title, { style: 'Title' }),
    docxParagraph(doc.subtitle, { color: '666666', size: 20 }),
    ...(doc.summary ? [docxParagraph('핵심 요약', { style: 'Heading1' }), docxParagraph(doc.summary)] : []),
    ...doc.sections.flatMap(s => [docxParagraph(s.heading, { style: 'Heading1' }), docxParagraph(s.body)]),
    ...(doc.sources.length ? [docxParagraph('출처', { style: 'Heading1' }), ...doc.sources.map(s => docxParagraph(`${s.title}${s.url ? '  ' + s.url : ''}`, { size: 18, color: '1F4E79' }))] : [])
  ].join('');
  const styles = `${XML}<w:styles xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii="Malgun Gothic" w:hAnsi="Malgun Gothic" w:eastAsia="Malgun Gothic" w:cs="Malgun Gothic"/><w:sz w:val="21"/><w:szCs w:val="21"/><w:lang w:val="en-US" w:eastAsia="ko-KR"/></w:rPr></w:rPrDefault></w:docDefaults>
<w:style w:type="paragraph" w:default="1" w:styleId="Normal"><w:name w:val="Normal"/></w:style>
<w:style w:type="paragraph" w:styleId="Title"><w:name w:val="Title"/><w:basedOn w:val="Normal"/><w:pPr><w:spacing w:after="160"/></w:pPr><w:rPr><w:b/><w:color w:val="0B2545"/><w:sz w:val="40"/><w:szCs w:val="40"/></w:rPr></w:style>
<w:style w:type="paragraph" w:styleId="Heading1"><w:name w:val="heading 1"/><w:basedOn w:val="Normal"/><w:pPr><w:keepNext/><w:spacing w:before="280" w:after="100"/><w:outlineLvl w:val="0"/></w:pPr><w:rPr><w:b/><w:color w:val="1F4E79"/><w:sz w:val="28"/><w:szCs w:val="28"/></w:rPr></w:style>
</w:styles>`;
  return zip([
    { name: '[Content_Types].xml', data: `${XML}<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/word/document.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml"/><Override PartName="/word/styles.xml" ContentType="application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"/><Override PartName="/docProps/core.xml" ContentType="application/vnd.openxmlformats-package.core-properties+xml"/></Types>` },
    { name: '_rels/.rels', data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="word/document.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties" Target="docProps/core.xml"/></Relationships>` },
    { name: 'docProps/core.xml', data: `${XML}<cp:coreProperties xmlns:cp="http://schemas.openxmlformats.org/package/2006/metadata/core-properties" xmlns:dc="http://purl.org/dc/elements/1.1/" xmlns:dcterms="http://purl.org/dc/terms/" xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance"><dc:title>${esc(doc.title)}</dc:title><dc:creator>JARVIS</dc:creator><dcterms:created xsi:type="dcterms:W3CDTF">${new Date().toISOString().slice(0, 19)}Z</dcterms:created></cp:coreProperties>` },
    { name: 'word/_rels/document.xml.rels', data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles" Target="styles.xml"/></Relationships>` },
    { name: 'word/styles.xml', data: styles },
    { name: 'word/document.xml', data: `${XML}<w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main"><w:body>${body}<w:sectPr><w:pgSz w:w="11906" w:h="16838"/><w:pgMar w:top="1440" w:right="1200" w:bottom="1440" w:left="1200" w:header="708" w:footer="708" w:gutter="0"/></w:sectPr></w:body></w:document>` }
  ]);
}

// ---------------------------------------------------------------- PPTX
const NS_P = 'xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" xmlns:p="http://schemas.openxmlformats.org/presentationml/2006/main"';
const SLIDE_W = 12192000, SLIDE_H = 6858000;
function pptRuns(text, size, bold, color) {
  return String(text).split('\n').filter((l, i, arr) => l.trim() || arr.length === 1).map(l => `<a:p><a:r><a:rPr lang="ko-KR" altLang="en-US" sz="${size}"${bold ? ' b="1"' : ''} dirty="0"><a:solidFill><a:srgbClr val="${color}"/></a:solidFill><a:latin typeface="Malgun Gothic"/><a:ea typeface="Malgun Gothic"/></a:rPr><a:t>${esc(l)}</a:t></a:r></a:p>`).join('') || '<a:p><a:endParaRPr lang="ko-KR"/></a:p>';
}
function pptShape(id, name, x, y, cx, cy, paragraphs) {
  return `<p:sp><p:nvSpPr><p:cNvPr id="${id}" name="${name}"/><p:cNvSpPr txBox="1"/><p:nvPr/></p:nvSpPr><p:spPr><a:xfrm><a:off x="${x}" y="${y}"/><a:ext cx="${cx}" cy="${cy}"/></a:xfrm><a:prstGeom prst="rect"><a:avLst/></a:prstGeom><a:noFill/></p:spPr><p:txBody><a:bodyPr wrap="square" lIns="91440" tIns="45720" rIns="91440" bIns="45720"><a:normAutofit/></a:bodyPr><a:lstStyle/>${paragraphs}</p:txBody></p:sp>`;
}
function pptSlide(title, body, isCover) {
  const bg = `<p:bg><p:bgPr><a:solidFill><a:srgbClr val="${isCover ? '0B2545' : 'FFFFFF'}"/></a:solidFill><a:effectLst/></p:bgPr></p:bg>`;
  const shapes = isCover
    ? pptShape(2, 'Title', 685800, 2286000, SLIDE_W - 1371600, 1371600, pptRuns(title, 4000, true, 'FFFFFF')) + pptShape(3, 'Subtitle', 685800, 3810000, SLIDE_W - 1371600, 914400, pptRuns(body, 1800, false, 'A9C7E8'))
    : pptShape(2, 'Title', 609600, 381000, SLIDE_W - 1219200, 914400, pptRuns(title, 2800, true, '0B2545')) + pptShape(3, 'Body', 609600, 1371600, SLIDE_W - 1219200, SLIDE_H - 1905000, pptRuns(body, 1600, false, '333333'));
  return `${XML}<p:sld ${NS_P}><p:cSld>${bg}<p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr>${shapes}</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>`;
}
const THEME = `${XML}<a:theme xmlns:a="http://schemas.openxmlformats.org/drawingml/2006/main" name="Jarvis"><a:themeElements><a:clrScheme name="Jarvis"><a:dk1><a:sysClr val="windowText" lastClr="000000"/></a:dk1><a:lt1><a:sysClr val="window" lastClr="FFFFFF"/></a:lt1><a:dk2><a:srgbClr val="0B2545"/></a:dk2><a:lt2><a:srgbClr val="EEF2F7"/></a:lt2><a:accent1><a:srgbClr val="1F4E79"/></a:accent1><a:accent2><a:srgbClr val="2E9F6B"/></a:accent2><a:accent3><a:srgbClr val="F2A33A"/></a:accent3><a:accent4><a:srgbClr val="4DD9FF"/></a:accent4><a:accent5><a:srgbClr val="7B61FF"/></a:accent5><a:accent6><a:srgbClr val="E05C5C"/></a:accent6><a:hlink><a:srgbClr val="0563C1"/></a:hlink><a:folHlink><a:srgbClr val="954F72"/></a:folHlink></a:clrScheme><a:fontScheme name="Jarvis"><a:majorFont><a:latin typeface="Malgun Gothic"/><a:ea typeface="Malgun Gothic"/><a:cs typeface=""/></a:majorFont><a:minorFont><a:latin typeface="Malgun Gothic"/><a:ea typeface="Malgun Gothic"/><a:cs typeface=""/></a:minorFont></a:fontScheme><a:fmtScheme name="Jarvis"><a:fillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:fillStyleLst><a:lnStyleLst><a:ln w="6350"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="12700"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln><a:ln w="19050"><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:ln></a:lnStyleLst><a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle></a:effectStyleLst><a:bgFillStyleLst><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill><a:solidFill><a:schemeClr val="phClr"/></a:solidFill></a:bgFillStyleLst></a:fmtScheme></a:themeElements></a:theme>`;
const EMPTY_TREE = '<p:spTree><p:nvGrpSpPr><p:cNvPr id="1" name=""/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr><p:grpSpPr><a:xfrm><a:off x="0" y="0"/><a:ext cx="0" cy="0"/><a:chOff x="0" y="0"/><a:chExt cx="0" cy="0"/></a:xfrm></p:grpSpPr></p:spTree>';

function chunkText(text, maxLines = 9, maxChars = 620) {
  const lines = String(text || '').split(/\n+/).flatMap(l => l.length > 110 ? l.match(/.{1,110}(\s|$)|.{1,110}/g) : [l]).map(l => l.trim()).filter(Boolean);
  const pages = []; let cur = [], len = 0;
  for (const l of lines) { if (cur.length >= maxLines || len + l.length > maxChars) { pages.push(cur.join('\n')); cur = []; len = 0; } cur.push(l); len += l.length; }
  if (cur.length) pages.push(cur.join('\n'));
  return pages.length ? pages : [''];
}
function toPptx(doc) {
  const slides = [[doc.title, doc.subtitle, true]];
  if (doc.summary) chunkText(doc.summary).forEach((p, i) => slides.push([i ? '핵심 요약 (계속)' : '핵심 요약', p, false]));
  for (const s of doc.sections) chunkText(s.body).forEach((p, i) => slides.push([i ? `${s.heading} (계속)` : s.heading, p, false]));
  if (doc.sources.length) chunkText(doc.sources.map(s => `${s.title}${s.url ? ' — ' + s.url : ''}`).join('\n'), 10, 900).forEach((p, i) => slides.push([i ? '출처 (계속)' : '출처', p, false]));
  const capped = slides.slice(0, 40);
  const entries = [
    { name: '[Content_Types].xml', data: `${XML}<Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types"><Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml"/><Default Extension="xml" ContentType="application/xml"/><Override PartName="/ppt/presentation.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml"/><Override PartName="/ppt/slideMasters/slideMaster1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml"/><Override PartName="/ppt/slideLayouts/slideLayout1.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml"/><Override PartName="/ppt/theme/theme1.xml" ContentType="application/vnd.openxmlformats-officedocument.theme+xml"/>${capped.map((_, i) => `<Override PartName="/ppt/slides/slide${i + 1}.xml" ContentType="application/vnd.openxmlformats-officedocument.presentationml.slide+xml"/>`).join('')}</Types>` },
    { name: '_rels/.rels', data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument" Target="ppt/presentation.xml"/></Relationships>` },
    { name: 'ppt/presentation.xml', data: `${XML}<p:presentation ${NS_P} saveSubsetFonts="1"><p:sldMasterIdLst><p:sldMasterId id="2147483648" r:id="rId1"/></p:sldMasterIdLst><p:sldIdLst>${capped.map((_, i) => `<p:sldId id="${256 + i}" r:id="rId${i + 3}"/>`).join('')}</p:sldIdLst><p:sldSz cx="${SLIDE_W}" cy="${SLIDE_H}"/><p:notesSz cx="6858000" cy="9144000"/></p:presentation>` },
    { name: 'ppt/_rels/presentation.xml.rels', data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="slideMasters/slideMaster1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="theme/theme1.xml"/>${capped.map((_, i) => `<Relationship Id="rId${i + 3}" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide" Target="slides/slide${i + 1}.xml"/>`).join('')}</Relationships>` },
    { name: 'ppt/slideMasters/slideMaster1.xml', data: `${XML}<p:sldMaster ${NS_P}><p:cSld>${EMPTY_TREE}</p:cSld><p:clrMap bg1="lt1" tx1="dk1" bg2="lt2" tx2="dk2" accent1="accent1" accent2="accent2" accent3="accent3" accent4="accent4" accent5="accent5" accent6="accent6" hlink="hlink" folHlink="folHlink"/><p:sldLayoutIdLst><p:sldLayoutId id="2147483649" r:id="rId1"/></p:sldLayoutIdLst></p:sldMaster>` },
    { name: 'ppt/slideMasters/_rels/slideMaster1.xml.rels', data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/><Relationship Id="rId2" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme" Target="../theme/theme1.xml"/></Relationships>` },
    { name: 'ppt/slideLayouts/slideLayout1.xml', data: `${XML}<p:sldLayout ${NS_P} type="blank" preserve="1"><p:cSld name="Blank">${EMPTY_TREE}</p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>` },
    { name: 'ppt/slideLayouts/_rels/slideLayout1.xml.rels', data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster" Target="../slideMasters/slideMaster1.xml"/></Relationships>` },
    { name: 'ppt/theme/theme1.xml', data: THEME }
  ];
  capped.forEach(([t, b, cover], i) => {
    entries.push({ name: `ppt/slides/slide${i + 1}.xml`, data: pptSlide(t, b, cover) });
    entries.push({ name: `ppt/slides/_rels/slide${i + 1}.xml.rels`, data: `${XML}<Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships"><Relationship Id="rId1" Type="http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout" Target="../slideLayouts/slideLayout1.xml"/></Relationships>` });
  });
  return { buffer: zip(entries), slideCount: capped.length };
}

// ---------------------------------------------------------------- PDF (HTML → headless browser)
function toHtml(doc) {
  const p = t => esc(t).replace(/\n/g, '<br>');
  return `<!doctype html><html lang="ko"><head><meta charset="utf-8"><title>${esc(doc.title)}</title><style>
@page{size:A4;margin:18mm 16mm}body{font-family:'Malgun Gothic','맑은 고딕',sans-serif;color:#1d2733;font-size:11pt;line-height:1.65}
h1{font-size:22pt;color:#0b2545;margin:0 0 4px}.sub{color:#6b7785;font-size:9.5pt;margin-bottom:18px;border-bottom:2px solid #1f4e79;padding-bottom:10px}
h2{font-size:14pt;color:#1f4e79;margin:20px 0 6px;page-break-after:avoid}.sum{background:#eef4fa;border-left:4px solid #1f4e79;padding:10px 14px;border-radius:4px}
.src{font-size:9pt;color:#1f4e79;word-break:break-all}</style></head><body><h1>${esc(doc.title)}</h1><div class="sub">${esc(doc.subtitle)}</div>
${doc.summary ? `<h2>핵심 요약</h2><div class="sum">${p(doc.summary)}</div>` : ''}${doc.sections.map(s => `<h2>${esc(s.heading)}</h2><div>${p(s.body)}</div>`).join('')}
${doc.sources.length ? `<h2>출처</h2>${doc.sources.map(s => `<div class="src">${esc(s.title)}${s.url ? ' — ' + esc(s.url) : ''}</div>`).join('')}` : ''}</body></html>`;
}
function findBrowser() {
  return ['C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe', 'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe', 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe'].find(p => fs.existsSync(p)) || null;
}
function htmlToPdf(htmlPath, pdfPath) {
  const browser = findBrowser();
  if (!browser) return Promise.reject(new Error('PDF 변환용 Edge/Chrome을 찾지 못했습니다.'));
  const profile = path.join(require('os').tmpdir(), 'jarvis-pdf-profile');
  return new Promise((resolve, reject) => {
    execFile(browser, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-pdf-header-footer', `--user-data-dir=${profile}`, `--print-to-pdf=${pdfPath}`, 'file:///' + htmlPath.replace(/\\/g, '/')], { windowsHide: true, timeout: 45000 }, err => {
      if (fs.existsSync(pdfPath) && fs.statSync(pdfPath).size > 800) return resolve(pdfPath);
      reject(new Error('PDF를 만들지 못했습니다' + (err ? ': ' + err.message : '.')));
    });
  });
}

// ---------------------------------------------------------------- Public API
function uniquePath(dir, base, ext) {
  const safe = String(base).replace(/[\\/:*?"<>|\r\n]+/g, ' ').replace(/\s+/g, ' ').trim().slice(0, 60) || '자비스_문서';
  let p = path.join(dir, `${safe}.${ext}`);
  if (fs.existsSync(p)) p = path.join(dir, `${safe}_${new Date().toTimeString().slice(0, 8).replace(/:/g, '')}.${ext}`);
  return p;
}

/**
 * 문서 생성. 기존 파일은 덮어쓰지 않습니다.
 * @returns {Promise<{ok, format, path, bytes, slideCount?}>}
 */
async function createDocument(format, input, options = {}) {
  const fmt = String(format || '').toLowerCase().replace(/^\./, '').replace('word', 'docx').replace('ppt', 'pptx').replace('pptxx', 'pptx').replace('markdown', 'md');
  if (!FORMATS.includes(fmt)) throw new Error('지원하는 형식은 md, docx, pptx, pdf입니다.');
  const doc = normalizeDoc(input);
  const opts = typeof options === 'string' ? { outDir: options } : (options || {});
  const dir = opts.outDir || path.join(EXPORT_ROOT, new Date().toISOString().slice(0, 10));
  fs.mkdirSync(dir, { recursive: true });
  const out = uniquePath(dir, doc.title, fmt);
  let extra = {};
  if (fmt === 'md') fs.writeFileSync(out, '\ufeff' + toMarkdown(doc), 'utf8');
  else if (fmt === 'docx') fs.writeFileSync(out, toDocx(doc));
  else if (fmt === 'pptx') { const r = toPptx(doc); fs.writeFileSync(out, r.buffer); extra.slideCount = r.slideCount; }
  else {
    const htmlPath = out.replace(/\.pdf$/, '.html');
    fs.writeFileSync(htmlPath, toHtml(doc), 'utf8');
    await htmlToPdf(htmlPath, out);
    extra.htmlPath = htmlPath;
  }
  const bytes = fs.statSync(out).size;
  return { ok: true, format: fmt, path: out, bytes, ...extra };
}

/** 생성된 파일 무결성 검사 (ZIP/PDF 헤더 + 필수 파트) */
function verifyDocument(filePath) {
  const buf = fs.readFileSync(filePath);
  const ext = path.extname(filePath).toLowerCase();
  if (ext === '.pdf') return { ok: buf.slice(0, 5).toString() === '%PDF-', bytes: buf.length };
  if (ext === '.md') return { ok: buf.length > 10, bytes: buf.length };
  const zipOk = buf.readUInt32LE(0) === 0x04034b50;
  const text = buf.toString('latin1');
  const need = ext === '.docx' ? ['word/document.xml', '[Content_Types].xml'] : ['ppt/presentation.xml', 'ppt/slides/slide1.xml', 'ppt/theme/theme1.xml'];
  return { ok: zipOk && need.every(n => text.includes(n)), bytes: buf.length };
}

module.exports = { FORMATS, EXPORT_ROOT, createDocument, verifyDocument, docFromResearch, normalizeDoc, normalizeDocSpec: normalizeDoc, toMarkdown };
