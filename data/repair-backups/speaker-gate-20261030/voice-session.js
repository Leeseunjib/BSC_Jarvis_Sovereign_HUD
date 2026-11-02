'use strict';

// Each microphone surface has a separate wake/follow-up window.
class VoiceSessions {
  constructor(engine, clock = () => Date.now()) {
    this.engine = engine;
    this.clock = clock;
    this.sessions = new Map();
  }

  accept(sessionId, sentence) {
    const text = String(sentence || '').trim().slice(0, 2000);
    if (!text) return false;
    const now = this.clock();
    for (const [id, expires] of this.sessions) if (expires <= now) this.sessions.delete(id);
    const id = String(sessionId || 'default').slice(0, 80);
    const config = this.engine.getConfig();
    const normalized = s => String(s || '').toLowerCase().replace(/[^a-z0-9가-힣]/g, '');
    const phrase = normalized(text);
    if (!phrase) return false;
    // Block room-noise / timestamp / prompt-echo hallucinations & 1-word ambient fillers
    if (
      /^(?:000\d|0시0분|ㅎㅎ+|ㅋㅋ+|ㄹ0ㅁ|기침|아|어|음|이거|거기|그냥|찰랑|이런|아이고|아맞다|아진짜|부러워요|저장하겠습니다)$/.test(phrase) ||
      /아무말도없거나|숨소리|배경잡음|타이핑하는소리|\d{2}:\d{2}/.test(text)
    ) {
      return false;
    }
    const isGreeting = (config.greetings || []).some(g => phrase === normalized(g));
    const wasAwake = (this.sessions.get(id) || 0) > now || (global.__jarvisForceAwakeUntil || 0) > now;
    const isWake = config.activation !== 'hold' && (this.engine.hasWakeWord(text) || isGreeting);
    const isMediaStop = config.activation !== 'hold' && typeof this.engine.isMediaStopRequest === 'function' && this.engine.isMediaStopRequest(text);
    if (!wasAwake && !isWake && !isMediaStop) return false;
    const baseFollowSec = Math.max(1, Math.min(60, Number(config.followUpSec) || 15));
    const stripped = typeof this.engine.stripWake === 'function' ? this.engine.stripWake(text) : text;
    const isPureWake = isWake && !String(stripped || '').trim();
    const seconds = this.engine.mode === 'dictation' ? 600 : (isPureWake ? 45 : baseFollowSec);
    if (this.sessions.size >= 100 && !this.sessions.has(id)) this.sessions.delete(this.sessions.keys().next().value);
    this.sessions.set(id, now + seconds * 1000);
    if (/^(그만|됐어|끝|잘자|쉬어|조용히해|조용|하지마|아니하지마|bye)$/i.test(phrase)) {
      this.sessions.delete(id);
      global.__jarvisForceAwakeUntil = 0;
    }
    return true;
  }

  wakeAll(expiresAt) {
    if (!Number.isFinite(expiresAt)) return;
    global.__jarvisForceAwakeUntil = expiresAt;
    for (const id of this.sessions.keys()) {
      this.sessions.set(id, Math.max(this.sessions.get(id) || 0, expiresAt));
    }
  }

  extend(sessionId, expiresAt) {
    const id = String(sessionId || 'default').slice(0, 80);
    if (Number.isFinite(expiresAt) && this.sessions.has(id)) {
      this.sessions.set(id, Math.max(this.sessions.get(id), expiresAt));
    }
  }
}

module.exports = { VoiceSessions };
