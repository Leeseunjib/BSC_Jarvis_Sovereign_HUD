(function (root, factory) {
  const SpeechTurnBuffer = factory();
  if (typeof module === 'object' && module.exports) module.exports = { SpeechTurnBuffer };
  else root.SpeechTurnBuffer = SpeechTurnBuffer;
})(typeof window === 'object' ? window : globalThis, function () {
  'use strict';
  // A recognizer's `isFinal` ends a segment, not necessarily the speaker's turn.
  return class SpeechTurnBuffer {
    constructor({ onCommit, onPreview = () => {}, onFinalize = () => {}, onFailure = () => {}, onReset = () => {}, isMuted = () => false, pauseMs = 2000, shortPauseMs = null, now = () => Date.now(), schedule = fn => setTimeout(fn, 100), cancel = id => clearTimeout(id) }) {
      this.onCommit = onCommit; this.onPreview = onPreview; this.isMuted = isMuted;
      this.onFinalize = onFinalize; this.onFailure = onFailure; this.onReset = onReset;
      this.pauseMs = Math.max(800, pauseMs); this.now = now; this.schedule = schedule; this.cancel = cancel;
      this.shortPauseMs = shortPauseMs;
      this.pending = new Map(); this.consumed = new Set(); this.interim = ''; this.timer = null;
      this.lastResultAt = 0; this.lastSpeechAt = 0;
      this.closingAt = null;
    }
    audio(level) {
      if (!this.isMuted() && level >= 0.12) this.lastSpeechAt = this.now();
    }
    push(finals, interim = '') {
      if (this.isMuted()) return;
      const previous = [...this.pending.values(), this.interim].filter(Boolean).join(' ').replace(/[\s.,!?。！？]/g, '');
      let changed = this.interim !== interim;
      for (const { id, text } of finals) {
        const clean = String(text || '').trim();
        if (!clean || this.consumed.has(id)) continue;
        if (this.pending.get(id) !== clean) changed = true;
        this.pending.set(id, clean);
      }
      this.interim = String(interim || '').trim();
      const text = [...this.pending.values(), this.interim].filter(Boolean).join(' ');
      // Finalizing unchanged words must not restart a silence period already observed.
      if (changed && text.replace(/[\s.,!?。！？]/g, '') !== previous) this.lastResultAt = this.now();
      if (text) this.onPreview(text);
      this.arm();
    }
    arm() {
      if (this.timer === null && (this.pending.size || this.interim)) this.timer = this.schedule(() => { this.timer = null; this.tick(); });
    }
    tick() {
      const phrase = [...this.pending.values(), this.interim].join(' ').replace(/[\s,.!?]/g, '').replace(/^(?:헤이)?(?:자비스|사사리사)/, '');
      const shortCommand = /^(?:(?:지금|현재)?(?:몇시야|몇시예요|시간알려줘|시간알려주세요)|(?:음악|노래|유튜브음악)(?:꺼줘|멈춰줘|정지해줘))$/.test(phrase);
      const pause = shortCommand && this.shortPauseMs ? Math.max(1100, this.shortPauseMs) : this.pauseMs;
      const quiet = this.now() - Math.max(this.lastResultAt, this.lastSpeechAt) >= pause;
      // A long recognition segment is not a timeout while speech continues.
      if (!this.isMuted() && this.interim && this.closingAt === null && quiet) {
        this.closingAt = this.now();
        this.onFinalize();
      }
      if (this.closingAt !== null && this.now() - this.closingAt >= 3000 && this.interim) {
        this.fail();
        this.onReset();
      }
      if (!this.isMuted() && this.pending.size && !this.interim && quiet) {
        const text = [...this.pending.values()].join(' ');
        for (const id of this.pending.keys()) this.consumed.add(id);
        while (this.consumed.size > 1000) this.consumed.delete(this.consumed.values().next().value);
        this.pending.clear();
        this.closingAt = null;
        this.onCommit(text, { committedAt: this.now(), silenceMs: this.now() - Math.max(this.lastResultAt, this.lastSpeechAt), pauseMs: pause });
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
