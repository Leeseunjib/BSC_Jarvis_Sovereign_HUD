'use strict';
const assert = require('node:assert/strict');
const { speechOptions, mp3ToWav } = require('./tts-audio');
(async () => {
  assert.deepEqual(speechOptions({ sayVoice: 'ko-KR-InJoonNeural', ttsVoice: 'ko-KR-SunHiNeural', ttsSpeed: 1.5 }), { voice: 'ko-KR-InJoonNeural', speed: 1.5 });
  assert.deepEqual(speechOptions({ sayVoice: 'ko-KR-InJoonNeural', ttsSpeed: 1.5 }, { voice: 'windows-ko', speed: 1 }), { voice: 'windows-ko', speed: 1 });
  for (const speed of [NaN, Infinity, -1, 0, 2, 'fast']) assert.throws(() => speechOptions({ ttsSpeed: speed }));
  assert.throws(() => speechOptions({ sayVoice: 'unavailable-voice' }));
  await assert.rejects(() => mp3ToWav(Buffer.from('<html>not audio</html>')));
  console.log('PASS: saved voice, rate, explicit preview overrides, invalid input and audio rejection');
})().catch(error => { console.error(error); process.exitCode = 1; });
