'use strict';
const dns = require('node:dns/promises');
const net = require('node:net');
const crypto = require('node:crypto');
const { searchEcosystems } = require('./ecosystem-index');
const STOP = new Set('기술 정보 검색 검색해 알려줘 알아 대해 대한 무엇 뭐야 개념 설명 방법 관련 이것 어떻게 그리고 있는 있습니다 합니다 위한 통해 동작을 함께 정리했습니다 무엇인지 살펴본다 최근 the and for with from that this are you your not into more about'.split(' '));
function clean(html = '') {
  return String(html).replace(/<(script|style|noscript)\b[^>]*>[\s\S]*?<\/\1>/gi, ' ').replace(/<[^>]+>/g, ' ')
    .replace(/&#(x[\da-f]+|\d+);/gi, (_, x) => { const n=x[0].toLowerCase()==='x'?parseInt(x.slice(1),16):Number(x);return n<=0x10ffff?String.fromCodePoint(n):''; })
    .replace(/&quot;/g, '"').replace(/&#39;|&apos;/g, "'").replace(/&amp;/g, '&').replace(/&lt;/g, '<').replace(/&gt;/g, '>').replace(/&nbsp;/g, ' ').replace(/&ldquo;|&rdquo;/g,'"').replace(/&lsquo;|&rsquo;/g,"'").replace(/&mdash;|&ndash;/g,'—').replace(/&hellip;/g,'…').replace(/\s+/g, ' ').trim();
}
function queryFor(sentence) {
  return String(sentence).replace(/^(?:자비스[,\s]*|(?:구글|네이버|우리\s*시스템|사내\s*문서|전체\s*시스템)(?:에서)?\s*)/i, '').replace(/(?:에?\s*대해(?:서)?\s*)?(?:알고\s*있어|알아|뭐야|무엇이야|설명해|알려줘|정리해|조사해|검색해서\s*보여|찾아서\s*보여|검색해|찾아봐|찾아|보여)(?:\s*줘|주세요|봐)?[?？.!\s]*$/i, '').replace(/[?？]+$/,'').trim().slice(0,180);
}
function isResearchQuestion(sentence) {
  return /(?:알아|알고\s*있어|뭐야|무엇(?:이|인)|어떤\s*기술|설명해|에\s*대해|차이|비교해|장단점|조사해|정리해|최신\s*소식|어떻게\s*작동)/i.test(sentence) || /[?？]$/.test(sentence.trim());
}
function tokens(text) {return [...new Set((clean(text).match(/[A-Za-z][A-Za-z0-9.+-]{1,30}|[가-힣]{2,16}/g)||[]).filter(t=>!STOP.has(t.toLowerCase())))];}
function safeUrl(value) {
  try { const u=new URL(value); if(!['http:','https:'].includes(u.protocol)||u.username||u.password||u.port&&!['80','443'].includes(u.port))return null;
    const host=u.hostname.toLowerCase().replace(/^\[|\]$/g,'');
    if(host==='localhost'||host.endsWith('.local')||host.endsWith('.internal')||!host.includes('.')||net.isIP(host))return null;
    return u.href;
  } catch {return null;}
}
function unwrap(value) {try {const u=new URL(clean(value),'https://duckduckgo.com');return safeUrl(u.searchParams.get('uddg')||u.href);}catch{return null;}}
function parseDuck(html) {
  const links=[...html.matchAll(/<a\b(?=[^>]*class=["'][^"']*result__a\b)[^>]*href=["']([^"']+)["'][^>]*>([\s\S]*?)<\/a>/gi)];
  return links.map((m,i)=>{const part=html.slice(m.index+m[0].length,links[i+1]?.index||html.length);const snippet=part.match(/<(?:a|div)\b[^>]*class=["'][^"']*result__snippet\b[^"']*["'][^>]*>([\s\S]*?)<\/(?:a|div)>/i);return {title:clean(m[2]),url:unwrap(m[1]),snippet:clean(snippet?.[1]||'')};}).filter(s=>s.url&&s.title);
}
function parseBing(xml) {return [...xml.matchAll(/<item>([\s\S]*?)<\/item>/g)].map(m=>{const field=t=>clean((m[1].match(new RegExp('<'+t+'>([\\s\\S]*?)<\\/'+t+'>'))||[])[1]||'');return {title:field('title'),url:safeUrl(field('link')),snippet:field('description')};}).filter(s=>s.url&&s.title);}
async function boundedFetch(url, timeout=6500) {
  const response=await fetch(url,{redirect:'manual',signal:AbortSignal.timeout(timeout),headers:{'User-Agent':'Mozilla/5.0 (compatible; JarvisResearch/1.0)','Accept-Language':'ko,en;q=0.8'}});
  if(response.status>=300&&response.status<400)return {redirect:new URL(response.headers.get('location'),url).href};
  if(!response.ok)throw Error('HTTP '+response.status);
  const reader=response.body.getReader();const chunks=[];let size=0;
  try {for(;;){const {done,value}=await reader.read();if(done)break;size+=value.length;if(size>600000){await reader.cancel();break;}chunks.push(Buffer.from(value));}}finally{reader.releaseLock();}
  return {text:Buffer.concat(chunks).toString('utf8')};
}
function publicAddress(ip) {return net.isIP(ip)===4 ? !/^(?:0|10|127|169\.254|172\.(?:1[6-9]|2\d|3[01])|192\.168|100\.(?:6[4-9]|[7-9]\d|1[01]\d|12[0-7])|22[4-9]|23\d|24\d|25\d)\./.test(ip) : net.isIP(ip)===6 && !/^(?:fc|fd|fe[89ab]|ff|::)/i.test(ip);}
async function articleText(url, terms) {
  for(let hop=0;hop<3;hop++){
    if(!safeUrl(url))throw Error('Non-public source');
    const addresses=await dns.lookup(new URL(url).hostname,{all:true});
    if(!addresses.length||addresses.some(a=>!publicAddress(a.address)))throw Error('Non-public address');
    const fetched=await boundedFetch(url,4500);
    if(fetched.redirect){url=fetched.redirect;continue;}
    const html=fetched.text;
    const paragraphs=[...html.matchAll(/<p\b[^>]*>([\s\S]*?)<\/p>/gi)].filter(m=>(m[1].match(/<a\b/g)||[]).length<3).map(m=>clean(m[1])).filter(t=>t.length>=50&&t.length<=1300&&!/cookie|쿠키|개인정보처리|javascript|로그인|무단.*전재|Quick start.*FAQ/i.test(t));
    const relevant=paragraphs.map(text=>({text,score:terms.filter(t=>text.toLowerCase().includes(t.toLowerCase())).length})).filter(p=>p.score>0).sort((a,b)=>b.score-a.score);
    return relevant.slice(0,2).map(p=>p.text).join('\n').slice(0,1600);
  }
  return '';
}
async function searchWeb(query, get=boundedFetch) {
  let results=[],provider='',errors=[];
  const terms=tokens(query);
  for(const [name,url,parse] of [['DuckDuckGo','https://html.duckduckgo.com/html/?q='+encodeURIComponent(query),parseDuck],['Bing','https://www.bing.com/search?format=rss&q='+encodeURIComponent(query),parseBing]]){
    try{const response=await get(url);results=parse(response.text||'').map(s=>({...s,score:terms.filter(t=>(s.title+' '+s.snippet).toLowerCase().includes(t.toLowerCase())).length})).filter(s=>!terms.length||s.score>0);if(results.length){provider=name;break;}errors.push(name+': 관련 검색 결과 없음');}catch(e){errors.push(name+': '+e.message);}
  }
  const seen=new Set();
  results=results.filter(s=>{if(seen.has(s.url))return false;seen.add(s.url);return true;}).sort((a,b)=>b.score-a.score).slice(0,6);
  return {results,provider,errors};
}
async function research(sentence, options={}) {
  const query=queryFor(sentence)||String(sentence).slice(0,180),terms=tokens(query);
  let localHits = [];
  if (options.includeLocal !== false) {
    try {
      const localRes = searchEcosystems(query, { limit: 4 });
      localHits = Array.isArray(localRes.hits) ? localRes.hits : [];
    } catch (_) {}
  }
  const {results,provider,errors}=await (options.search||searchWeb)(query);
  const rank=s=>{const host=new URL(s.url).hostname;return (/^(?:github\.com|learn\.microsoft\.com|developer\.|docs\.)/.test(host)?3:0)+(terms.some(t=>host===t.toLowerCase()+'.tools'||host===t.toLowerCase()+'.org')?4:0);};
  const webSources=results.slice().sort((a,b)=>rank(b)-rank(a)).map((s,i)=>({...s,id:localHits.length+i+1,domain:new URL(s.url).hostname,readStatus:'검색 미리보기'}));
  if(options.readPages!==false)await Promise.all(webSources.slice(0,3).map(async s=>{try{const text=await articleText(s.url,[...new Set([...terms,...tokens(s.title)])]);if(text){s.excerpt=text;s.readStatus='원문 확인';}}catch{s.readStatus='미리보기 · 원문 접근 불가';}}));
  const localSources=localHits.map((h,i)=>({
    id:i+1,
    title:`[${h.ecosystem}] ${h.title}`,
    url:'file:///'+String(h.path||'').replace(/\\/g,'/'),
    domain:`C:\\${h.ecosystem}`,
    readStatus:`로컬 ${h.kind||'문서'}`,
    excerpt:`경로: ${h.path}\n${h.snippet||''}`
  }));
  const sources=[...localSources,...webSources];
  const sections=sources.map(s=>({title:s.title,text:(s.excerpt||s.snippet||'검색 결과에 설명이 제공되지 않았습니다. 출처를 열어 확인해 주세요.').slice(0,950),sourceId:s.id}));
  const counts=new Map();for(const s of sources)for(const word of tokens(s.title+' '+(s.snippet||s.excerpt||''))){counts.set(word,(counts.get(word)||0)+1);}
  const keywordSeen=new Set();const keywords=[...terms,...[...counts].sort((a,b)=>b[1]-a[1]).map(e=>e[0])].filter(x=>{const k=x.toLowerCase();if(keywordSeen.has(k))return false;keywordSeen.add(k);return true;}).slice(0,12);
  const acronym=/\b[A-Z]{2,5}\b/.exec(query);
  const note=sources.length ? (acronym ? `${acronym[0]} 같은 약어는 분야별 뜻이 다를 수 있습니다. 출처의 분야를 확인해 주세요. ` : '')+(localSources.length ? `로컬 3대 생태계(BeausCreators·BeautifulWorld·Warehouse) 문서 ${localSources.length}건과 웹 검색 ${webSources.length}건을 함께 정리했습니다. 상단 버튼으로 Word(.docx), Markdown(.md), PDF, PPT(.pptx) 문서로 즉시 저장하실 수 있습니다.` : '원문과 검색 미리보기에서 핵심 문장을 발췌했습니다. 상단 버튼으로 Word(.docx), Markdown(.md), PDF, PPT(.pptx) 문서로 저장할 수 있습니다.') : '검색 결과를 확보하지 못했습니다. 검색어를 구체적으로 바꾸거나 다시 시도해 주세요.';
  const primary=webSources.filter(s=>rank(s)>0&&s.excerpt);
  const overview=localSources.length
    ? [...localSources.slice(0,2).map(s=>s.excerpt.slice(0,450)+` [${s.id}]`), ...(primary.length?primary.slice(0,1).map(s=>s.excerpt.slice(0,450)+` [${s.id}]`):[])]
    : (primary.length?primary.slice(0,2).map(s=>s.excerpt.slice(0,650)+` [${s.id}]`):sections.slice(0,3).map(s=>s.text.split(/(?<=[.!?。])\s+/)[0].slice(0,260)+` [${s.sourceId}]`));
  const combinedProvider = [localSources.length ? `로컬 생태계(${localSources.length}건)` : '', provider].filter(Boolean).join(' + ') || '통합 검색';
  return {id:crypto.randomUUID(),query,title:query+' · 조사 결과',status:sources.length?'ready':'unavailable',createdAt:new Date().toISOString(),provider:combinedProvider,note,keywords,summary:overview.join('\n\n'),sections,sources: sources.map(({id,title,url,domain,readStatus})=>({id,title,url,domain,readStatus})),errors};
}
module.exports={research,searchWeb,queryFor,isResearchQuestion,parseDuck,parseBing,safeUrl,publicAddress};
