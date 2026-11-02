(function (root) {
  'use strict';
  class SpeakerCapture {
    constructor({ now = Date.now, send = (...args) => fetch(...args) } = {}) {
      this.now = now; this.send = send; this.frames = []; this.turnAt = null; this.enrollment = null;
      this.enrollmentText = ''; this.enrollmentSegments = new Map(); this.submitted = ''; this.ready = false; this.protected = false; this.speaking = false; this.samplesSeen = 0;
    }
    async connect(ctx, source) {
      await ctx.audioWorklet.addModule('/js/speaker-capture-worklet.js');
      this.node = new AudioWorkletNode(ctx, 'jarvis-speaker-capture');
      this.mute = ctx.createGain(); this.mute.gain.value = 0;
      source.connect(this.node); this.node.connect(this.mute); this.mute.connect(ctx.destination);
      this.node.port.onmessage = event => this.append(event.data);
      this.ready = true;
    }
    append(samples) {
      this.samplesSeen += samples.length;
      const at = this.now(); this.frames.push({ at, samples });
      while (this.frames.length > 620 || (this.frames[0] && at - this.frames[0].at > 62000)) this.frames.shift();
      const e = this.enrollment;
      if (e && at >= e.startAt && at <= e.endAt && this.speaking) this.contaminated = true;
      if (e && e.phase === 'recording' && at >= e.endAt + 250 && this.submitted !== e.id) {
        this.submitted = e.id;
        const audio = this.encode(e.startAt, e.endAt);
        const transcript = this.contaminated ? '' : this.enrollmentText;
        this.send('/api/speaker/enroll/complete', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ id: e.id, audio, transcript }) }).catch(() => {});
        this.turnAt = null;
      }
    }
    sync(state, speaking) {
      if (speaking || this.speaking !== !!speaking) this.turnAt = null;
      if (this.speaking && !speaking) this.lastPlaybackEnd = this.now();
      this.speaking = !!speaking;
      if (!state) return;
      this.protected = !!state.enabled;
      const next = state.enrollment && ['recording', 'processing'].includes(state.enrollment.phase) ? state.enrollment : null;
      if (next && (!this.enrollment || this.enrollment.id !== next.id)) { this.enrollmentText = ''; this.enrollmentSegments.clear(); this.contaminated = false; this.turnAt = null; }
      if (!!next !== !!this.enrollment) this.turnAt = null;
      this.enrollment = next;
    }
    get enrolling() { return !!this.enrollment; }
    noteText(text, id) {
      if (!this.enrollment || this.now() < this.enrollment.startAt || this.now() > this.enrollment.endAt) return;
      this.enrollmentSegments.set(id, text);
      this.enrollmentText = [...this.enrollmentSegments.values()].join(' ').slice(-5000);
    }
    beginTurn(preRoll = 400) { if (this.turnAt === null && !this.enrolling && !this.speaking) this.turnAt = Math.max(this.now() - preRoll, this.lastPlaybackEnd || 0); }
    encode(startAt, endAt) {
      const frames = this.frames.filter(f => f.at > startAt && f.at <= endAt);
      const count = frames.reduce((n, f) => n + f.samples.length, 0);
      const bytes = new Uint8Array(count * 2), view = new DataView(bytes.buffer);
      let offset = 0;
      for (const f of frames) for (const x of f.samples) { const value = Math.max(-1, Math.min(1, x)); view.setInt16(offset, Math.round(value * (value < 0 ? 32768 : 32767)), true); offset += 2; }
      let binary = '';
      for (let i = 0; i < bytes.length; i += 8192) binary += String.fromCharCode(...bytes.subarray(i, i + 8192));
      return { sampleRate: 16000, pcm: btoa(binary) };
    }
    finishTurn() {
      const start = this.turnAt; this.turnAt = null;
      if (!this.ready || start === null || this.now() - start > 60000) return null;
      return this.encode(start, this.now());
    }
    reset() { this.turnAt = null; }
  }
  root.JarvisSpeakerCapture = SpeakerCapture;
  if (typeof module === 'object' && module.exports) module.exports = SpeakerCapture;
})(typeof window === 'object' ? window : globalThis);
