'use strict';
const assert = require('assert/strict'), fs = require('fs'), path = require('path'), os = require('os');
const { SpeakerGate, SpeakerWorker, MODEL, THRESHOLD, cosine } = require('./speaker-gate');
const { VoiceSessions } = require('./voice-session');
const temp = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-speaker-test-'));
const a = Array(256).fill(0), b = Array(256).fill(0); a[0] = 1; b[1] = 1;
const audio = { sampleRate: 16000, pcm: Buffer.alloc(64000).toString('base64') };
const file = path.join(temp, 'profile.json');
(async () => {
  let time = 1000, results = [a];
  const gate = new SpeakerGate({ file, now: () => time, worker: { extract: async () => results } });
  assert.equal(gate.status().enrolled, false);
  assert.throws(() => gate.setEnabled(true));
  gate.start(); assert.equal((await gate.verify(audio)).allowed, false);
  time += 30000;
  await assert.rejects(gate.finish(gate.enrollment.id, audio, '등록'));
  assert.equal(gate.status().enrolled, false, '실패한 녹음을 등록된 목소리로 만들면 안 됩니다');
  const id = gate.start().enrollment.id; time += 30000; results = [a, a, b];
  const enrollmentAudio = { sampleRate: 16000, pcm: Buffer.alloc(24 * 32000).toString('base64') };
  const phrase = '자비스 내 목소리를 등록해 주세요 오늘 할 일을 천천히 정리하고 필요한 정보를 확인합니다';
  await assert.rejects(gate.finish(id, enrollmentAudio, phrase), /일치/);
  assert.equal(fs.existsSync(file), false);
  const id2 = gate.start().enrollment.id; time += 30000; results = [a, a, a];
  await gate.finish(id2, enrollmentAudio, phrase);
  assert.equal(gate.enabled, true);
  assert.equal((await gate.verify(null)).allowed, false, '녹음 없이 통과하면 안 됩니다');
  results = [a]; assert.equal((await gate.verify(audio)).allowed, true);
  results = [b]; assert.equal((await gate.verify(audio)).allowed, false);
  results = [a, b]; assert.equal((await gate.verify(audio)).allowed, false, '앞 화자만 맞는 혼합 발화를 허용하면 안 됩니다');
  gate.worker.extract = async () => { throw Error('offline'); };
  assert.equal((await gate.verify(audio)).allowed, false, '모델 장애 시 실행 금지');
  const loaded = new SpeakerGate({ file }); assert.equal(loaded.enabled, true);
  gate.forget(); assert.equal(fs.existsSync(file), false); assert.equal(gate.enabled, false);
  fs.writeFileSync(file, 'broken'); const broken = new SpeakerGate({ file }); assert.equal(broken.enabled, true); assert.equal((await broken.verify(audio)).allowed, false);
  const engine = { getConfig: () => ({ voiceRequireWake: true, greetings: ['안녕'] }), hasWakeWord: t => t.includes('자비스'), stripWake: t => t.replace('자비스', ''), isMediaStopRequest: t => t === '음악 꺼줘' };
  const sessions = new VoiceSessions(engine, () => time);
  assert.equal(sessions.accept('room', '안녕'), false);
  assert.equal(sessions.accept('room', '자비스 몇 시야', { commit: false }), true);
  assert.equal(sessions.accept('room', '메일 보내'), false, '미확인 화자의 호출어는 이어 듣기를 열면 안 됩니다');
  assert.equal(sessions.accept('room', '음악 꺼줘'), false);
  assert.equal(sessions.accept('room', '음악 꺼줘', { ownerVerified: true }), true);
  console.log('PASS: 등록 실패·혼합 화자·무음/누락·모델 장애 차단, 저장/삭제, 호출어/이어 듣기');
  if (process.argv.includes('--real')) {
    const sherpa = require('sherpa-onnx-node'), worker = new SpeakerWorker();
    const names = ['fangjun-test-sr-1.wav', 'fangjun-test-sr-2.wav', 'leijun-test-sr-1.wav', 'leijun-test-sr-2.wav', 'liudehua-test-sr-1.wav', 'liudehua-test-sr-2.wav'];
    const rows = [], vectors = [];
    try {
      for (const name of names) {
        const wave = sherpa.readWave(path.join(__dirname, '../data/verification/speaker-fixtures', name));
        assert.equal(wave.sampleRate, 16000);
        const pcm = Buffer.alloc(wave.samples.length * 2);
        wave.samples.forEach((x, i) => pcm.writeInt16LE(Math.round(Math.max(-1, Math.min(1, x)) * 32767), i * 2));
        const start = Date.now(), vv = await worker.extract(pcm.toString('base64'));
        vectors.push(vv);
        const scores = vv.map(v => Math.max(...vectors[0].map(p => cosine(v, p))));
        const score = Math.min(...scores), allowed = score >= THRESHOLD;
        rows.push({ name, scores, allowed, milliseconds: Date.now() - start });
        if (name.startsWith('fangjun')) assert.equal(allowed, true, name);
        else assert.equal(allowed, false, name);
      }
      fs.writeFileSync(path.join(__dirname, '../data/verification/speaker-model-measurements-20261030.json'), JSON.stringify({ model: MODEL, threshold: THRESHOLD, rows }, null, 2));
      console.log(JSON.stringify(rows));
      console.log('PASS: 실제 모델로 동일 화자 2건 통과 / 다른 화자 4건 거부 (개인 실환경 정확도는 별도 확인 필요)');
    } finally { worker.stop(); }
  }
})().catch(error => { console.error(error); process.exitCode = 1; });
