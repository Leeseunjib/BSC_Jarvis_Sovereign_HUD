'use strict';
const fs = require('fs');
const path = require('path');
const crypto = require('crypto');
const { fork } = require('child_process');
const MODEL = 'wespeaker_en_voxceleb_resnet34_LM';
const THRESHOLD = .85; // Cosine score, not a probability; validated with the installed model's held-out fixtures.
const PROMPTS = [
  '자비스, 내 목소리를 등록해 주세요. 지금은 첫 번째 문장을 천천히 읽고 있습니다.',
  '다른 사람이나 영상 소리에는 반응하지 말고, 제가 부를 때 제 말을 들어 주세요.',
  '이제 마지막 문장입니다. 오늘 할 일을 차근차근 정리하고 필요한 정보를 확인하겠습니다.'
];
function speakerMessage(reason) {
  const messages = {
    'different-or-uncertain-speaker': '등록한 목소리로 확인되지 않아 실행하지 않았습니다. 가까이에서 다시 말씀해 주세요.',
    'speaker-audio-required': '목소리 비교용 녹음이 없습니다. 메인 HUD의 마이크를 사용하거나 직접 입력해 주세요.',
    'speech-too-short-or-long': '명령이 너무 짧거나 깁니다. “자비스, 지금 시간을 알려 주세요”처럼 말씀해 주세요.',
    'speech-too-short': '목소리를 비교하기에 음성이 짧습니다. 조금 길게 말씀해 주세요.',
    'insufficient-speech': '목소리가 충분히 녹음되지 않았습니다. 마이크 입력과 음량을 확인해 주세요.',
    'audio-clipped': '마이크 입력이 너무 큽니다. 소리가 찌그러지지 않도록 입력 게인을 낮춰 주세요.',
    'speaker-profile-invalid': '목소리 등록 정보에 오류가 있습니다. 다시 등록할 때까지 음성 실행을 보류합니다.',
    'speaker-profile-changed': '목소리 설정이 바뀌었습니다. 다시 말씀해 주세요.',
    'enrolling': '목소리를 등록하는 동안 음성 명령은 실행하지 않습니다.'
  };
  return messages[reason] || (/[가-힣]/.test(reason || '') ? reason : '목소리 비교를 완료하지 못했습니다. 다시 시도하거나 직접 입력해 주세요.');
}
function cosine(a, b) {
  if (!a || !b || a.length !== b.length) return -1;
  let dot = 0, aa = 0, bb = 0;
  for (let i = 0; i < a.length; i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
  return aa && bb ? dot / Math.sqrt(aa * bb) : -1;
}
class SpeakerWorker {
  constructor() { this.child = null; this.pending = new Map(); }
  stop() {
    const child = this.child; this.child = null;
    for (const p of this.pending.values()) { clearTimeout(p.timer); p.reject(new Error('speaker-worker-unavailable')); }
    this.pending.clear();
    if (child) { try { child.kill(); } catch (_) {} }
  }
  extract(pcm, enroll = false) {
    if (this.pending.size >= 2) return Promise.reject(new Error('speaker-worker-busy'));
    if (!this.child) {
      const child = fork(path.join(__dirname, 'speaker-worker.js'), [], { windowsHide: true, stdio: ['ignore', 'ignore', 'ignore', 'ipc'] });
      this.child = child;
      child.on('message', data => {
        const p = this.pending.get(data.id); if (!p) return;
        clearTimeout(p.timer); this.pending.delete(data.id);
        if (data.ok) p.resolve(data.vectors); else p.reject(new Error(data.error));
      });
      for (const event of ['error', 'exit']) child.on(event, () => { if (this.child === child) this.stop(); });
    }
    return new Promise((resolve, reject) => {
      const id = crypto.randomUUID();
      const timer = setTimeout(() => this.stop(), 12000);
      this.pending.set(id, { resolve, reject, timer });
      this.child.send({ id, pcm, enroll }, error => { if (error) this.stop(); });
    });
  }
}
class SpeakerGate {
  constructor({ file = path.join(process.env.JARVIS_DATA_DIR || path.join(__dirname, '../data'), 'owner-speaker-profile.json'), worker = new SpeakerWorker(), now = Date.now } = {}) {
    this.file = file; this.worker = worker; this.now = now; this.profile = null; this.enrollment = null; this.last = null; this.loadError = false;
    try {
      if (fs.existsSync(file)) {
        const p = JSON.parse(fs.readFileSync(file, 'utf8'));
        if (p.version !== 1 || p.model !== MODEL || p.vectors.length !== 3 || !p.vectors.every(v => Array.isArray(v) && v.length === 256 && v.every(Number.isFinite))) throw new Error('invalid-profile');
        this.profile = p;
      }
    } catch (_) { this.loadError = true; }
  }
  get enabled() { return this.loadError || !!this.profile?.enabled; }
  get enrolling() { return !!(this.enrollment && this.enrollment.expiresAt > this.now() && ['recording', 'processing'].includes(this.enrollment.phase)); }
  status() {
    const e = this.enrollment;
    if (e && this.now() > e.expiresAt && ['recording', 'processing'].includes(e.phase)) { e.phase = 'error'; e.message = '등록 시간이 만료됐습니다. 마이크 연결을 확인하고 다시 시작해 주세요.'; }
    return { ok: true, enrolled: !!this.profile, enabled: this.enabled, profileError: this.loadError, model: MODEL,
      threshold: THRESHOLD, updatedAt: this.profile?.updatedAt || null, last: this.last,
      enrollment: e ? { id: e.id, phase: e.phase, message: e.message || '', startAt: e.startAt, endAt: e.endAt, expiresAt: e.expiresAt, prompts: PROMPTS } : null };
  }
  start() {
    if (this.enrolling) throw new Error('이미 목소리를 등록하고 있습니다.');
    const now = this.now();
    this.enrollment = { id: crypto.randomUUID(), phase: 'recording', startAt: now + 5000, endAt: now + 29000, expiresAt: now + 60000 };
    return this.status();
  }
  cancel() { this.enrollment = null; return this.status(); }
  persist() {
    fs.mkdirSync(path.dirname(this.file), { recursive: true });
    const tmp = this.file + '.tmp'; fs.writeFileSync(tmp, JSON.stringify(this.profile), { encoding: 'utf8', mode: 0o600 }); fs.renameSync(tmp, this.file);
  }
  setEnabled(enabled) {
    if (!this.profile || this.loadError) throw new Error('먼저 내 목소리를 등록해 주세요.');
    const old = this.profile.enabled; this.profile.enabled = enabled === true;
    try { this.persist(); } catch (error) { this.profile.enabled = old; throw error; }
    return this.status();
  }
  forget() { if (fs.existsSync(this.file)) fs.unlinkSync(this.file); this.profile = null; this.loadError = false; this.enrollment = null; this.last = null; return this.status(); }
  validateAudio(audio, minSeconds = 1, maxSeconds = 60) {
    if (!audio || audio.sampleRate !== 16000 || typeof audio.pcm !== 'string' || audio.pcm.length > Math.ceil(maxSeconds * 32000 / 3) * 4 + 4 || !/^[A-Za-z0-9+/]*={0,2}$/.test(audio.pcm)) throw new Error('speaker-audio-required');
    const bytes = Buffer.from(audio.pcm, 'base64').length;
    if (bytes % 2 || bytes < minSeconds * 32000 || bytes > maxSeconds * 32000) throw new Error('speech-too-short-or-long');
  }
  async finish(id, audio, transcript) {
    const e = this.enrollment;
    if (!e || id !== e.id || e.phase !== 'recording' || this.now() < e.endAt - 1000 || this.now() > e.expiresAt) throw new Error('등록 세션이 없거나 만료됐습니다.');
    e.phase = 'processing';
    try {
      this.validateAudio(audio, 22, 26);
      if (!String(transcript || '').includes('등록') || String(transcript || '').replace(/[^가-힣a-z]/gi, '').length < 30) throw new Error('등록 문장이 인식되지 않았습니다. 조용한 곳에서 화면의 문장을 읽어 주세요.');
      const vectors = await this.worker.extract(audio.pcm, true);
      if (vectors.length !== 3 || cosine(vectors[0], vectors[1]) < THRESHOLD || cosine(vectors[0], vectors[2]) < THRESHOLD || cosine(vectors[1], vectors[2]) < THRESHOLD) throw new Error('세 구간의 목소리가 충분히 일치하지 않습니다. 영상과 음악을 끄고 다시 등록해 주세요.');
      if (this.enrollment !== e || e.phase !== 'processing') throw new Error('등록이 취소됐습니다.');
      const old = this.profile;
      this.profile = { version: 1, model: MODEL, enabled: true, updatedAt: new Date(this.now()).toISOString(), vectors };
      try { this.persist(); } catch (error) { this.profile = old; throw error; }
      this.loadError = false; e.phase = 'complete'; e.message = '내 목소리 등록 완료 · 내 목소리만 반응 켜짐';
      return this.status();
    } catch (error) {
      if (this.enrollment === e) { e.phase = 'error'; e.message = speakerMessage(error.message); }
      throw error;
    }
  }
  async verify(audio) {
    if (this.enrolling) return { allowed: false, reason: 'enrolling' };
    if (!this.enabled) return { allowed: true, verified: false, reason: 'disabled' };
    try {
      if (this.loadError) throw new Error('speaker-profile-invalid');
      const profile = this.profile;
      this.validateAudio(audio);
      const vectors = await this.worker.extract(audio.pcm);
      if (this.profile !== profile || this.enrolling) throw new Error('speaker-profile-changed');
      const scores = vectors.map(v => Math.max(...this.profile.vectors.map(p => cosine(v, p))));
      const score = Math.min(...scores);
      const allowed = Number.isFinite(score) && score >= THRESHOLD;
      this.last = { at: new Date(this.now()).toISOString(), allowed, score: Math.round(score * 1000) / 1000, reason: allowed ? 'owner-match' : 'different-or-uncertain-speaker' };
      return { ...this.last, verified: allowed };
    } catch (error) {
      this.last = { at: new Date(this.now()).toISOString(), allowed: false, reason: error.message };
      return { ...this.last, verified: false };
    }
  }
}
module.exports = { SpeakerGate, SpeakerWorker, cosine, MODEL, PROMPTS, THRESHOLD, speakerMessage };
