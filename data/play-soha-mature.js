'use strict';
const fs = require('fs');
const { MsEdgeTTS, OUTPUT_FORMAT } = require('C:\\BSC_Javis_OS\\node_modules\\msedge-tts');
const { mp3ToWav } = require('C:\\자비스\\scripts\\tts-audio');

(async () => {
  try {
    const res = await fetch('https://www.microsoft.com', { method: 'HEAD' });
    const d = res.headers.get('date');
    if (d) MsEdgeTTS._clockOffsetMs = Date.parse(d) - Date.now();
  } catch (_) {}

  async function synth(voice, text, rate, pitch) {
    const tts = new MsEdgeTTS();
    await tts.setMetadata(voice, OUTPUT_FORMAT.AUDIO_24KHZ_96KBITRATE_MONO_MP3);
    const { audioStream } = tts.toStream(text, { rate, pitch });
    const parts = [];
    await new Promise((res, rej) => {
      audioStream.on('data', d => parts.push(d));
      audioStream.on('end', res);
      audioStream.on('error', rej);
    });
    try { tts.close(); } catch (_) {}
    return Buffer.concat(parts);
  }

  // 1) Mature Soha Option A: SunHiNeural at -6Hz, +10% (calm, mature low-mid female tone)
  const b1 = await synth('ko-KR-SunHiNeural', '첫 번째, 중저음으로 낮춘 성숙한 소하 목소리입니다. 이건우 대표님, 오늘 일정과 오더를 차분하게 보좌하겠습니다.', '+10%', '-6Hz');
  // 2) Mature Soha Option B: EmmaMultilingualNeural at -2Hz, +8% (warm, adult secretary tone)
  const b2 = await synth('en-US-EmmaMultilingualNeural', '두 번째, 엠마 멀티링구얼 성우 목소리입니다. 이건우 대표님, 차분하고 부드러운 성인 비서 톤입니다.', '+8%', '-2Hz');

  const wav = await mp3ToWav(Buffer.concat([b1, b2]));
  const outWav = 'C:\\자비스\\data\\soha_mature.wav';
  fs.writeFileSync(outWav, wav);
  console.log('SAVED_MATURE_WAV:', wav.length);

  // Trigger playback in Desktop Session 1 via /api/tasks/run on 127.0.0.1:7788
  const r = await fetch('http://127.0.0.1:7788/api/tasks/run', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: '소하 성숙한 중저음 보이스 재생',
      command: "powershell -NoProfile -Command \"(New-Object System.Media.SoundPlayer 'C:\\자비스\\data\\soha_mature.wav').PlaySync()\""
    })
  });
  console.log('TASK_RUN:', await r.json());
})();
