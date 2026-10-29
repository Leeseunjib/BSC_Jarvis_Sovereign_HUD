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
    const isGreeting = (config.greetings || []).some(g => phrase === normalized(g));
    const wasAwake = (this.sessions.get(id) || 0) > now;
    const isWake = config.activation !== 'hold' && (this.engine.hasWakeWord(text) || isGreeting);
    const isMediaStop = config.activation !== 'hold' && typeof this.engine.isMediaStopRequest === 'function' && this.engine.isMediaStopRequest(text);
    if (!wasAwake && !isWake && !isMediaStop) return false;
    const seconds = this.engine.mode === 'dictation' ? 600 : Math.max(1, Math.min(120, Number(config.followUpSec) || 30));
    if (this.sessions.size >= 100 && !this.sessions.has(id)) this.sessions.delete(this.sessions.keys().next().value);
    this.sessions.set(id, now + seconds * 1000);
    if (/^(그만|됐어|끝|잘자|bye)$/i.test(phrase)) this.sessions.delete(id);
    return true;
  }

  extend(sessionId, expiresAt) {
    const id = String(sessionId || 'default').slice(0, 80);
    if (Number.isFinite(expiresAt) && this.sessions.has(id)) {
      this.sessions.set(id, Math.max(this.sessions.get(id), expiresAt));
    }
  }
}

module.exports = { VoiceSessions };
