'use strict';

const VOICES = ['ko-KR-HyunsuMultilingualNeural', 'ko-KR-InJoonNeural', 'ko-KR-SunHiNeural', 'windows-ko'];
function speechOptions(config = {}, requested = {}) {
  const voice = requested.voice || config.sayVoice || config.ttsVoice || VOICES[0];
  const speed = Number(requested.speed ?? config.ttsSpeed ?? 1.25);
  if (!VOICES.includes(voice)) throw new Error('지원하지 않는 음성입니다. 음성 설정에서 다시 선택해 주세요.');
  if (!Number.isFinite(speed) || speed < 0.75 || speed > 1.5) throw new Error('음성 속도는 0.75~1.50 범위여야 합니다.');
  return { voice, speed };
}

async function mp3ToWav(mp3) {
  const { MPEGDecoder } = await import('mpg123-decoder');
  const decoder = new MPEGDecoder();
  await decoder.ready;
  try {
    const { channelData, samplesDecoded, sampleRate, errors } = decoder.decode(mp3);
    if (!samplesDecoded || !sampleRate || !channelData.length || errors.length) throw new Error('MP3 오디오 디코딩 실패');
    const channels = channelData.length;
    const bytes = samplesDecoded * channels * 2;
    const wav = Buffer.alloc(44 + bytes);
    wav.write('RIFF', 0); wav.writeUInt32LE(36 + bytes, 4); wav.write('WAVEfmt ', 8);
    wav.writeUInt32LE(16, 16); wav.writeUInt16LE(1, 20); wav.writeUInt16LE(channels, 22);
    wav.writeUInt32LE(sampleRate, 24); wav.writeUInt32LE(sampleRate * channels * 2, 28);
    wav.writeUInt16LE(channels * 2, 32); wav.writeUInt16LE(16, 34);
    wav.write('data', 36); wav.writeUInt32LE(bytes, 40);
    for (let i = 0; i < samplesDecoded; i++) {
      for (let c = 0; c < channels; c++) {
        const value = Math.max(-1, Math.min(1, channelData[c][i]));
        wav.writeInt16LE(Math.round(value * (value < 0 ? 32768 : 32767)), 44 + (i * channels + c) * 2);
      }
    }
    return wav;
  } finally { decoder.free(); }
}

module.exports = { VOICES, speechOptions, mp3ToWav };
