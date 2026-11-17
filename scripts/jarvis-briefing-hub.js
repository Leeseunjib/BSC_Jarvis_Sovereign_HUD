'use strict';

/**
 * @file C:\자비스\scripts\jarvis-briefing-hub.js
 * @description JARVIS v3.0 Daily Executive Newsletter (Live Korean & Tech/Business News RSS)
 *   + Gmail & Google Calendar Schedule/Digest Connector
 */

const fs = require('fs');
const path = require('path');
const https = require('https');
const crypto = require('crypto');

function fetchUrlText(url, timeoutMs = 4500) {
  return new Promise(resolve => {
    const req = https.get(
      url,
      {
        headers: {
          'User-Agent': 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) JARVIS-Newsletter/3.0',
          'Accept-Language': 'ko-KR,ko;q=0.9'
        },
        timeout: timeoutMs
      },
      res => {
        if (res.statusCode >= 300 && res.statusCode < 400 && res.headers.location) {
          res.resume();
          return fetchUrlText(res.headers.location, timeoutMs).then(resolve);
        }
        if (res.statusCode !== 200) {
          res.resume();
          return resolve('');
        }
        let body = '';
        res.on('data', d => {
          body += d.toString('utf8');
          if (body.length > 350000) res.destroy();
        });
        res.on('end', () => resolve(body));
        res.on('error', () => resolve(''));
      }
    );
    req.on('timeout', () => { req.destroy(); resolve(''); });
    req.on('error', () => resolve(''));
  });
}

function decodeXmlEntities(str) {
  return String(str || '')
    .replace(/<!\[CDATA\[([\s\S]*?)\]\]>/g, '$1')
    .replace(/&quot;/g, '"')
    .replace(/&apos;/g, "'")
    .replace(/&#39;/g, "'")
    .replace(/&lt;/g, '<')
    .replace(/&gt;/g, '>')
    .replace(/&amp;/g, '&')
    .trim();
}

async function fetchLiveKoreanNews(topic = '', limit = 7) {
  const cleanTopic = String(topic || '')
    .replace(/(?:오늘의?|최신|주요|아침|뉴스레터|뉴스|소식|헤드라인|알려줘|보여줘|브리핑|정리해줘)/g, '')
    .trim();
  const rssUrl = cleanTopic
    ? `https://news.google.com/rss/search?q=${encodeURIComponent(cleanTopic)}&hl=ko&gl=KR&ceid=KR:ko`
    : 'https://news.google.com/rss?hl=ko&gl=KR&ceid=KR:ko';

  const xml = await fetchUrlText(rssUrl, 4500);
  const items = [];
  const itemRegex = /<item>([\s\S]*?)<\/item>/g;
  let m;
  while ((m = itemRegex.exec(xml)) !== null && items.length < limit) {
    const block = m[1];
    const titleMatch = block.match(/<title>([\s\S]*?)<\/title>/);
    const linkMatch = block.match(/<link>([\s\S]*?)<\/link>/);
    const pubMatch = block.match(/<pubDate>([\s\S]*?)<\/pubDate>/);
    const sourceMatch = block.match(/<source[^>]*>([\s\S]*?)<\/source>/);
    if (titleMatch) {
      items.push({
        index: items.length + 1,
        title: decodeXmlEntities(titleMatch[1]),
        link: linkMatch ? decodeXmlEntities(linkMatch[1]) : 'https://news.google.com',
        source: sourceMatch ? decodeXmlEntities(sourceMatch[1]) : 'Google News Korea',
        pubDate: pubMatch ? decodeXmlEntities(pubMatch[1]).slice(0, 22) : ''
      });
    }
  }
  return { ok: items.length > 0, topic: cleanTopic || '오늘의 주요 헤드라인', items };
}

/**
 * Parse optional Google Calendar ICS URL (if configured in config.json / .env) + local reminders/schedules
 */
async function fetchCalendarAndMailSummary(cfg = {}) {
  const icsUrl = cfg.googleCalendarIcsUrl || process.env.GOOGLE_CALENDAR_ICS_URL || '';
  const calendarEvents = [];

  if (icsUrl && /^https?:\/\//i.test(icsUrl)) {
    const icsRaw = await fetchUrlText(icsUrl, 4000);
    const todayPrefix = new Date().toISOString().slice(0, 10).replace(/-/g, '');
    const vevents = icsRaw.split('BEGIN:VEVENT').slice(1);
    for (const ev of vevents) {
      const dtMatch = ev.match(/DTSTART[^:]*:(\d{8}(?:T\d{6}Z?)?)/);
      const sumMatch = ev.match(/SUMMARY:(.+)/);
      if (dtMatch && sumMatch && dtMatch[1].startsWith(todayPrefix.slice(0, 6))) {
        calendarEvents.push({
          date: dtMatch[1],
          summary: sumMatch[1].trim()
        });
      }
    }
  }

  // Also check local timers & memory schedule notes
  const dataDir = process.env.JARVIS_DATA_DIR || path.join(__dirname, '..', 'data');
  let savedSchedules = [];
  try {
    const memPath = path.join(dataDir, 'memory.md');
    if (fs.existsSync(memPath)) {
      savedSchedules = fs.readFileSync(memPath, 'utf8')
        .split(/\r?\n/)
        .filter(l => /일정|미팅|회의|약속|마감|캘린더|메일/.test(l))
        .slice(-5);
    }
  } catch (_) {}

  return {
    calendarConnected: Boolean(icsUrl),
    gmailConnected: Boolean(cfg.gmailConnected || process.env.GMAIL_APP_PASSWORD),
    calendarEvents: calendarEvents.slice(0, 6),
    localScheduleNotes: savedSchedules
  };
}

/**
 * Build a full Morning Executive Newsletter Report for HUD Research/Newsletter Window + Spoken Summary
 */
async function buildExecutiveNewsletter({ topic = '', cfg = {} } = {}) {
  const [news, aiNews, schedule] = await Promise.all([
    fetchLiveKoreanNews(topic, 6),
    topic ? Promise.resolve({ items: [] }) : fetchLiveKoreanNews('AI 인공지능 테크 경제', 4),
    fetchCalendarAndMailSummary(cfg)
  ]);

  const id = crypto.randomUUID();
  const todayKo = new Date().toLocaleDateString('ko-KR', { year: 'numeric', month: 'long', day: 'numeric', weekday: 'long' });

  const sections = [];
  if (news.items.length > 0) {
    sections.push({
      sourceId: 1,
      title: `1. 📰 ${news.topic} 실시간 브리핑 (${todayKo})`,
      text: news.items.map(n => `• [${n.source}] ${n.title}`).join('\n')
    });
  }
  if (aiNews.items && aiNews.items.length > 0) {
    sections.push({
      sourceId: 2,
      title: '2. 🤖 AI · 테크 · 비즈니스 핵심 동향',
      text: aiNews.items.map(n => `• [${n.source}] ${n.title}`).join('\n')
    });
  }

  const schedLines = [];
  if (schedule.calendarEvents.length > 0) {
    schedLines.push(...schedule.calendarEvents.map(e => `• [Google 캘린더] ${e.date}: ${e.summary}`));
  } else {
    schedLines.push('• [Google 캘린더 · Gmail 연동 안내] Google 계정 OAuth(Gmail · Google Calendar 플러그인 또는 캘린더 비공개 ICS 주소)를 1회 연결하시면 읽지 않은 중요 메일과 오늘 일정을 이 영역에 자동 통합합니다.');
  }
  if (schedule.localScheduleNotes.length > 0) {
    schedLines.push(...schedule.localScheduleNotes.map(n => `• [자비스 기억 일정] ${n.replace(/^-\s*/, '')}`));
  }
  sections.push({
    sourceId: 3,
    title: '3. 📅 오늘의 일정 · Gmail & Google 캘린더 소식',
    text: schedLines.join('\n')
  });

  const sources = news.items.map((n, idx) => ({
    id: idx + 1,
    title: n.title,
    url: n.link,
    domain: n.source,
    readStatus: '실시간 뉴스'
  }));

  const topHeadlinesSpoken = news.items.length > 0
    ? `오늘의 주요 소식입니다. 첫째, ${news.items[0].title.replace(/\s*-\s*[^-]+$/, '')}. 둘째, ${news.items[1] ? news.items[1].title.replace(/\s*-\s*[^-]+$/, '') : '상세 뉴스레터를 화면에 띄웠습니다'}. 전체 기사와 일정은 전용 뉴스레터 창에서 확인하실 수 있습니다.`
    : '현재 실시간 뉴스 피드에 연결하지 못했습니다. 인터넷 상태를 확인해 주세요.';

  return {
    report: {
      id,
      query: topic || '이건우 대표님 모닝 뉴스레터 & 일정 브리핑',
      title: `JARVIS Executive Newsletter — ${todayKo}`,
      status: 'ready',
      createdAt: new Date().toISOString(),
      provider: 'JARVIS Live News & Schedule Hub',
      note: '실시간 국내·글로벌 헤드라인과 대표님의 일정·메일 브리핑을 통합한 맞춤형 뉴스레터입니다.',
      summary: `주요 헤드라인 ${news.items.length}건, AI·비즈니스 소식 ${(aiNews.items || []).length}건, 일정 브리핑을 정리했습니다.`,
      keywords: ['오늘의 뉴스', 'AI 트렌드', '경제 브리핑', '구글 캘린더 열기', '지메일 열기'],
      sections,
      sources
    },
    spoken: topHeadlinesSpoken
  };
}

const STOCK_TICKER_MAP = {
  '삼성전자': '005930.KS',
  'sk하이닉스': '000660.KS',
  '하이닉스': '000660.KS',
  '네이버': '035420.KS',
  '카카오': '035720.KS',
  '현대차': '005380.KS',
  '기아': '000270.KS',
  '셀트리온': '068270.KS',
  '엔비디아': 'NVDA',
  'nvidia': 'NVDA',
  '테슬라': 'TSLA',
  'tesla': 'TSLA',
  '애플': 'AAPL',
  'apple': 'AAPL',
  '마이크로소프트': 'MSFT',
  '구글': 'GOOGL',
  '알파벳': 'GOOGL',
  '아마존': 'AMZN',
  '메타': 'META',
  '브로드컴': 'AVGO',
  '팔란티어': 'PLTR',
  '코스피': '^KS11',
  '코스닥': '^KQ11',
  '나스닥': '^IXIC',
  '에스앤피': '^GSPC',
  's&p500': '^GSPC',
  '다우존스': '^DJI',
  '환율': 'KRW=X',
  '원달러': 'KRW=X',
  '비트코인': 'BTC-USD'
};

async function fetchYahooQuote(symbol) {
  const url = `https://query1.finance.yahoo.com/v8/finance/chart/${encodeURIComponent(symbol)}?interval=1d&range=5d`;
  const raw = await fetchUrlText(url, 4000);
  if (!raw) return null;
  try {
    const parsed = JSON.parse(raw);
    const meta = parsed?.chart?.result?.[0]?.meta;
    if (!meta || typeof meta.regularMarketPrice !== 'number') return null;
    const price = meta.regularMarketPrice;
    const prev = meta.chartPreviousClose || meta.previousClose || price;
    const diff = price - prev;
    const pct = prev ? (diff / prev) * 100 : 0;
    return {
      symbol: meta.symbol || symbol,
      shortName: meta.shortName || meta.symbol || symbol,
      currency: meta.currency || '',
      price,
      prevClose: prev,
      change: Number(diff.toFixed(2)),
      changePct: Number(pct.toFixed(2))
    };
  } catch (_) {
    return null;
  }
}

/**
 * Real-time Korean & Global Stock Market Intelligence + Vibe-Trading Analysis Report
 */
async function fetchStockMarketIntelligence({ query = '' } = {}) {
  const q = String(query || '').trim();
  const sq = q.toLowerCase().replace(/\s+/g, '');

  // Determine requested symbols or default market overview
  const matchedEntries = Object.entries(STOCK_TICKER_MAP).filter(([k]) => sq.includes(k));
  const targetSymbols = matchedEntries.length > 0
    ? [...new Set(matchedEntries.map(([, v]) => v))]
    : ['^KS11', '^KQ11', '^IXIC', '^GSPC', 'KRW=X', '005930.KS', 'NVDA'];

  // Also accept direct uppercase tickers in query (e.g. AAPL, TSLA, 005930.KS)
  const tickerMatch = q.match(/\b([A-Z]{2,5}|\d{6}\.K[SQ])\b/g);
  if (tickerMatch) {
    for (const t of tickerMatch) {
      if (!targetSymbols.includes(t)) targetSymbols.unshift(t);
    }
  }

  const [quotesRaw, stockNews] = await Promise.all([
    Promise.all(targetSymbols.slice(0, 7).map(sym => fetchYahooQuote(sym))),
    fetchLiveKoreanNews(matchedEntries.length > 0 ? `${matchedEntries[0][0]} 주가 전망 실적` : '증시 코스피 나스닥 주식 마감 전망', 6)
  ]);

  const quotes = quotesRaw.filter(Boolean);
  const id = crypto.randomUUID();
  const sections = [];

  if (quotes.length > 0) {
    sections.push({
      sourceId: 1,
      title: '1. 📈 실시간 시세 · 지수 · 등락률 (Yahoo Finance Live)',
      text: quotes.map(qt => {
        const sign = qt.change >= 0 ? '▲ +' : '▼ ';
        return `• ${qt.shortName} (${qt.symbol}): ${qt.price.toLocaleString()} ${qt.currency} (${sign}${qt.change} / ${qt.change >= 0 ? '+' : ''}${qt.changePct}%)`;
      }).join('\n')
    });
  }

  if (stockNews.items && stockNews.items.length > 0) {
    sections.push({
      sourceId: 2,
      title: '2. 📰 종목 · 글로벌 증시 실시간 주요 뉴스 및 모멘텀',
      text: stockNews.items.map(n => `• [${n.source}] ${n.title}`).join('\n')
    });
  }

  sections.push({
    sourceId: 3,
    title: '3. 🧮 퀀트 & 리스크 체크포인트 (Vibe-Trading / Financial Analysis)',
    text: '• 단기 변동성·수급 모멘텀과 거시 지표(원/달러 환율, 미 국채 금리, 반도체/AI 섹터 동조화)를 함께 대조하세요.\n• 특정 종목 심층 재무제표(PER/PBR/실적 컨센서스)나 메모장 정리가 필요하시면 이어서 바로 지시해 주세요.'
  });

  const topQuote = quotes[0];
  const spoken = topQuote
    ? `실시간 증시 브리핑입니다. ${topQuote.shortName} 현재가는 ${topQuote.price.toLocaleString()} ${topQuote.currency}, 전일 대비 ${topQuote.change >= 0 ? '플러스 ' : '마이너스 '}${Math.abs(topQuote.changePct)} 퍼센트입니다. 전체 종목 지표와 관련 증시 뉴스는 전용 창에 띄워 드렸습니다.`
    : '증시 관련 최신 뉴스를 조회하여 화면에 정리했습니다.';

  return {
    report: {
      id,
      query: q || '실시간 국내·미국 증시 & 종목 인텔리전스',
      title: `JARVIS Stock & Quant Intelligence — ${q || '글로벌·국내 증시 종합'}`,
      status: 'ready',
      createdAt: new Date().toISOString(),
      provider: 'JARVIS Market & Quant Engine',
      note: '실시간 국내(KOSPI/KOSDAQ)·미국(NASDAQ/S&P500) 시세, 환율 및 종목별 뉴스를 통합 조회한 결과입니다.',
      summary: `실시간 시세 ${quotes.length}개 지표 및 증시 속보 ${(stockNews.items || []).length}건을 확보했습니다.`,
      keywords: ['삼성전자 주가', '엔비디아 주가', '코스피·나스닥 시황', '원달러 환율', '메모장에 시황 정리'],
      sections,
      sources: (stockNews.items || []).map((n, idx) => ({
        id: idx + 1,
        title: n.title,
        url: n.link,
        domain: n.source,
        readStatus: '증시 속보'
      }))
    },
    quotes,
    spoken
  };
}

const CITY_COORDS = {
  '서울': { name: '서울', lat: 37.5665, lon: 126.9780 },
  '강남': { name: '서울 강남', lat: 37.4979, lon: 127.0276 },
  '판교': { name: '성남 판교', lat: 37.3947, lon: 127.1112 },
  '성남': { name: '성남', lat: 37.4200, lon: 127.1267 },
  '인천': { name: '인천', lat: 37.4563, lon: 126.7052 },
  '수원': { name: '수원', lat: 37.2636, lon: 127.0286 },
  '대전': { name: '대전', lat: 36.3504, lon: 127.3845 },
  '대구': { name: '대구', lat: 35.8714, lon: 128.6014 },
  '부산': { name: '부산', lat: 35.1796, lon: 129.0756 },
  '광주': { name: '광주', lat: 35.1595, lon: 126.8526 },
  '제주': { name: '제주', lat: 33.4996, lon: 126.5312 }
};

function describeWmoCode(code) {
  const c = Number(code) || 0;
  if (c === 0) return { icon: '☀', text: '맑음' };
  if (c === 1 || c === 2) return { icon: '⛅', text: '구름 조금' };
  if (c === 3) return { icon: '☁', text: '흐림' };
  if (c === 45 || c === 48) return { icon: '🌫', text: '안개' };
  if (c >= 51 && c <= 67) return { icon: '🌧', text: '비' };
  if (c >= 71 && c <= 77) return { icon: '❄', text: '눈' };
  if (c >= 80 && c <= 82) return { icon: '🌦', text: '소나기' };
  if (c >= 95) return { icon: '⛈', text: '뇌우' };
  return { icon: '🌤', text: '대체로 맑음' };
}

const weatherCache = new Map();

/**
 * 100% Free ($0, No API Key Required) Live Weather Engine via Open-Meteo KMA/ECMWF Forecast
 */
async function fetchLiveWeatherBriefing({ query = '', forceRefresh = false } = {}) {
  const q = String(query || '').trim();
  const matchedCity = Object.entries(CITY_COORDS).find(([k]) => q.includes(k));
  const loc = matchedCity ? matchedCity[1] : CITY_COORDS['서울'];
  const cacheKey = loc.name;
  const cached = weatherCache.get(cacheKey);
  if (!forceRefresh && cached && Date.now() - cached.ts < 10 * 60 * 1000) {
    return cached.data;
  }

  const url = `https://api.open-meteo.com/v1/forecast?latitude=${loc.lat}&longitude=${loc.lon}&current=temperature_2m,relative_humidity_2m,apparent_temperature,weather_code,wind_speed_10m&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&timezone=Asia%2FSeoul&forecast_days=3`;
  const raw = await fetchUrlText(url, 4500);
  if (!raw) {
    if (cached) return cached.data;
    return {
      ok: false,
      city: loc.name,
      badgeText: `${loc.name} 날씨 연결 대기`,
      spoken: `현재 ${loc.name} 날씨 서버 응답을 확인하고 있습니다.`
    };
  }

  try {
    const parsed = JSON.parse(raw);
    const cur = parsed.current || {};
    const daily = parsed.daily || {};
    const tempC = Number(cur.temperature_2m ?? 0).toFixed(1);
    const feelsLikeC = Number(cur.apparent_temperature ?? tempC).toFixed(1);
    const humidity = Math.round(Number(cur.relative_humidity_2m ?? 0));
    const windKmh = Number(cur.wind_speed_10m ?? 0).toFixed(1);
    const wmo = describeWmoCode(cur.weather_code);
    const maxC = Array.isArray(daily.temperature_2m_max) ? Number(daily.temperature_2m_max[0]).toFixed(1) : tempC;
    const minC = Array.isArray(daily.temperature_2m_min) ? Number(daily.temperature_2m_min[0]).toFixed(1) : tempC;
    const rainProb = Array.isArray(daily.precipitation_probability_max) ? Number(daily.precipitation_probability_max[0] || 0) : 0;

    const badgeText = `${wmo.icon} ${loc.name} ${tempC}°C (${wmo.text}·습도 ${humidity}%)`;
    const spoken = `현재 ${loc.name} 날씨는 ${wmo.text}, 기온은 섭씨 ${tempC}도이며 체감온도는 ${feelsLikeC}도입니다. 오늘 최고 기온은 ${maxC}도, 최저 기온은 ${minC}도, 강수 확률은 ${rainProb} 퍼센트입니다.`;

    const result = {
      ok: true,
      cost: '$0 (Open-Meteo 무료 기상 API)',
      city: loc.name,
      tempC: Number(tempC),
      feelsLikeC: Number(feelsLikeC),
      humidity,
      windKmh: Number(windKmh),
      icon: wmo.icon,
      condition: wmo.text,
      maxC: Number(maxC),
      minC: Number(minC),
      rainProb,
      badgeText,
      spoken
    };
    weatherCache.set(cacheKey, { ts: Date.now(), data: result });
    return result;
  } catch (_) {
    return {
      ok: false,
      city: loc.name,
      badgeText: `${loc.name} 날씨 확인 중`,
      spoken: `${loc.name} 날씨 데이터를 파싱하지 못했습니다.`
    };
  }
}

module.exports = {
  fetchLiveKoreanNews,
  fetchCalendarAndMailSummary,
  buildExecutiveNewsletter,
  fetchStockMarketIntelligence,
  fetchLiveWeatherBriefing
};
