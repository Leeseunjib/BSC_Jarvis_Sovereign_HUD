(function (root, factory) {
  const SpeechTurnBuffer = factory();
  if (typeof module === 'object' && module.exports) module.exports = { SpeechTurnBuffer };
  else root.SpeechTurnBuffer = SpeechTurnBuffer;
})(typeof window === 'object' ? window : globalThis, function () {
  'use strict';
  // A recognizer's `isFinal` ends a segment, not necessarily the speaker's turn.
  return class SpeechTurnBuffer {
    constructor({ onCommit, onPreview = () => {}, onFinalize = () => {}, onFailure = () => {}, onReset = () => {}, isMuted = () => false, pauseMs = 2000, now = () => Date.now(), schedule = fn => setTimeout(fn, 100), cancel = id => clearTimeout(id) }) {
      this.onCommit = onCommit; this.onPreview = onPreview; this.isMuted = isMuted;
      this.onFinalize = onFinalize; this.onFailure = onFailure; this.onReset = onReset;
      this.pauseMs = Math.max(800, pauseMs); this.now = now; this.schedule = schedule; this.cancel = cancel;
      this.pending = new Map(); this.consumed = new Set(); this.interim = ''; this.timer = null;
      this.lastResultAt = 0; this.lastSpeechAt = 0;
      this.closingAt = null;
    }
    audio(level) {
      if (!this.isMuted() && level >= 0.12) this.lastSpeechAt = this.now();
    }
    push(finals, interim = '') {
      if (this.isMuted()) return;
      let changed = this.interim !== interim;
      for (const { id, text } of finals) {
        const clean = String(text || '').trim();
        if (!clean || this.consumed.has(id)) continue;
        if (this.pending.get(id) !== clean) changed = true;
        this.pending.set(id, clean);
      }
      this.interim = String(interim || '').trim();
      if (changed) this.lastResultAt = this.now();
      const text = [...this.pending.values(), this.interim].filter(Boolean).join(' ');
      if (text) this.onPreview(text);
      this.arm();
    }
    arm() {
      if (this.timer === null && (this.pending.size || this.interim)) this.timer = this.schedule(() => { this.timer = null; this.tick(); });
    }
    tick() {
      const quiet = this.now() - Math.max(this.lastResultAt, this.lastSpeechAt) >= this.pauseMs;
      const stalled = this.now() - this.lastResultAt >= 10000;
      if (!this.isMuted() && this.interim && this.closingAt === null && (quiet || stalled)) {
        this.closingAt = this.now();
        this.onFinalize();
      }
      if (this.closingAt !== null && this.now() - this.closingAt >= 3000 && this.interim) {
        this.fail();
        this.onReset();
      }
      if (!this.isMuted() && this.pending.size && !this.interim && stalled && !quiet) {
        this.fail();
        this.onReset();
      }
      if (!this.isMuted() && this.pending.size && !this.interim && this.now() - Math.max(this.lastResultAt, this.lastSpeechAt) >= this.pauseMs) {
        const text = [...this.pending.values()].join(' ');
        for (const id of this.pending.keys()) this.consumed.add(id);
        while (this.consumed.size > 1000) this.consumed.delete(this.consumed.values().next().value);
        this.pending.clear();
        this.closingAt = null;
        this.onCommit(text);
      }
      this.arm();
    }
    ended() {
      if (this.interim) this.fail();
      else this.closingAt = null;
    }
    fail() {
      const text = [...this.pending.values(), this.interim].filter(Boolean).join(' ');
      this.stop();
      if (text) this.onFailure(text);
    }
    state() {
      return { finalSegments: this.pending.size, interimChars: this.interim.length, finalizing: this.closingAt !== null };
    }
    stop() {
      if (this.timer !== null) this.cancel(this.timer);
      this.timer = null; this.pending.clear(); this.interim = '';
      this.closingAt = null;
    }
  };
});
