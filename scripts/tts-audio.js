'use strict';

const VOICES = ['ko-KR-HyunsuMultilingualNeural', 'ko-KR-InJoonNeural', 'ko-KR-SunHiNeural', 'windows-ko'];

// 30 BSC Agents (C:\BeausCreators\01.BSC_HQ\2.Agents) Gender-Matched & Persona-Calibrated Neural Voice Profiles
const PERSONA_VOICE_MAP = {
  // === 15 Female Personas (ko-KR-SunHiNeural + Individual Pitch/Rate) ===
  soha: { id: 'soha', name: '수석비서 소하', aliases: ['소하', '수석비서 소하', '수석비서', 'bsc-soha'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+2Hz', speed: 1.22 },
  sian: { id: 'sian', name: '수석디자이너 시안', aliases: ['시안', '수석디자이너 시안', '수석 디자이너 시안', 'bsc-sian'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '-2Hz', speed: 1.20 },
  yuna: { id: 'yuna', name: '프론트엔드 유나', aliases: ['유나', '프론트엔드 유나', 'bsc-yuna'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+6Hz', speed: 1.28 },
  seoa: { id: 'seoa', name: '출판에디터 서아', aliases: ['서아', '출판에디터 서아', '에디터 서아', 'bsc-seoa'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '-4Hz', speed: 1.16 },
  narin: { id: 'narin', name: 'CMO 나린', aliases: ['나린', 'cmo 나린', '최고마케팅책임자 나린', 'bsc-narin'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+0Hz', speed: 1.25 },
  jiyoon: { id: 'jiyoon', name: '기획총괄 지윤', aliases: ['지윤', 'pm 지윤', '기획총괄 지윤', 'bsc-jiyoon'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+1Hz', speed: 1.24 },
  jungsook: { id: 'jungsook', name: '서비스PM 한정숙', aliases: ['한정숙', '정숙', 'pm 한정숙', 'bsc-jungsook'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '-3Hz', speed: 1.20 },
  dain: { id: 'dain', name: 'BI분석가 다인', aliases: ['다인', 'bi 다인', '분석가 다인', 'bsc-dain'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '-1Hz', speed: 1.22 },
  seah: { id: 'seah', name: '그로스해커 세아', aliases: ['세아', '마케터 세아', '그로스해커 세아', 'bsc-seah'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+5Hz', speed: 1.28 },
  harin: { id: 'harin', name: '마케터 하린', aliases: ['하린', '마케터 하린', '그로스 마케터 하린', 'bsc-harin'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+4Hz', speed: 1.26 },
  claire: { id: 'claire', name: '글로벌마케터 클레어', aliases: ['클레어', '글로벌마케터 클레어', 'bsc-claire'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+0Hz', speed: 1.24 },
  lina: { id: 'lina', name: '툴스엔지니어 리나', aliases: ['리나', '유니티 리나', '툴스 리나', 'bsc-lina'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+3Hz', speed: 1.25 },
  roa: { id: 'roa', name: '그래픽스TA 로아', aliases: ['로아', '그래픽스 로아', '비주얼 로아', 'bsc-roa'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '-2Hz', speed: 1.21 },
  arin: { id: 'arin', name: 'CX디렉터 아린', aliases: ['아린', 'cx 아린', '디렉터 아린', 'bsc-arin'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '+3Hz', speed: 1.22 },
  ellen: { id: 'ellen', name: '법률커뮤니케이터 엘렌', aliases: ['엘렌', '법률 엘렌', 'bsc-ellen'], gender: 'female', voice: 'ko-KR-SunHiNeural', pitch: '-3Hz', speed: 1.18 },

  // === 15 Male Personas (ko-KR-InJoonNeural / ko-KR-HyunsuMultilingualNeural + Individual Pitch/Rate) ===
  taejun: { id: 'taejun', name: '비서실장 태준', aliases: ['태준', '비서실장 태준', '비서실장', 'bsc-taejun'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '-4Hz', speed: 1.22 },
  geonu: { id: 'geonu', name: 'CTO 거누', aliases: ['거누', 'cto 거누', '최고기술책임자 거누', 'bsc-geonu'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '+0Hz', speed: 1.25 },
  dohyun: { id: 'dohyun', name: 'CFO 도현', aliases: ['도현', 'cfo 도현', '최고재무책임자 도현', 'bsc-dohyun'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '-6Hz', speed: 1.18 },
  sion: { id: 'sion', name: 'COO 시온', aliases: ['시온', 'coo 시온', '최고운영책임자 시온', 'bsc-sion'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '-3Hz', speed: 1.20 },
  seojin: { id: 'seojin', name: '백엔드 강서진', aliases: ['강서진', '서진', '백엔드 서진', 'bsc-seojin'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '-2Hz', speed: 1.24 },
  woojin: { id: 'woojin', name: 'AI연구원 우진', aliases: ['우진', 'ai연구원 우진', '연구원 우진', 'bsc-woojin'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '+2Hz', speed: 1.26 },
  hajun: { id: 'hajun', name: 'ML연구원 하준', aliases: ['하준', 'ml연구원 하준', '연구원 하준', 'bsc-hajun'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '-1Hz', speed: 1.22 },
  suho: { id: 'suho', name: 'QA 수호', aliases: ['수호', 'qa 수호', '보안 수호', 'bsc-suho'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '+3Hz', speed: 1.28 },
  ruka: { id: 'ruka', name: '게임개발자 루카', aliases: ['루카', '게임개발자 루카', 'bsc-ruka'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '+4Hz', speed: 1.28 },
  jaeho: { id: 'jaeho', name: 'UI/UX디자이너 재호', aliases: ['재호', '디자이너 재호', 'uiux 재호', 'bsc-jaeho'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '+1Hz', speed: 1.24 },
  taeo: { id: 'taeo', name: 'BX디자이너 태오', aliases: ['태오', 'bx 태오', '디자이너 태오', 'bsc-taeo'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '+2Hz', speed: 1.25 },
  marcus: { id: 'marcus', name: '테크PM 마커스', aliases: ['마커스', '테크pm 마커스', 'bsc-marcus'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '-2Hz', speed: 1.23 },
  kael: { id: 'kael', name: '영상프로듀서 카엘', aliases: ['카엘', '영상 카엘', '프로듀서 카엘', 'bsc-kael'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '+4Hz', speed: 1.28 },
  jay: { id: 'jay', name: '코어시스템 제이', aliases: ['제이', '코어 제이', '프로듀서 제이', 'bsc-jay'], gender: 'male', voice: 'ko-KR-InJoonNeural', pitch: '-3Hz', speed: 1.22 },
  eunwoo: { id: 'eunwoo', name: '게임플레이 은우', aliases: ['은우', '게임플레이 은우', '작사가 은우', 'bsc-eunwoo'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '+3Hz', speed: 1.25 },

  // Default JARVIS Butler Voice
  jarvis: { id: 'jarvis', name: '자비스', aliases: ['자비스', 'jarvis', '사사리사'], gender: 'male', voice: 'ko-KR-HyunsuMultilingualNeural', pitch: '+0Hz', speed: 1.25 }
};

function resolvePersonaVoice(agentOrText) {
  if (!agentOrText) return null;
  const raw = String(agentOrText).trim();
  if (!raw) return null;
  const lower = raw.toLowerCase().replace(/^bsc-/, '');
  if (PERSONA_VOICE_MAP[lower]) return PERSONA_VOICE_MAP[lower];

  for (const profile of Object.values(PERSONA_VOICE_MAP)) {
    if (profile.id === 'jarvis') continue;
    for (const alias of profile.aliases) {
      if (lower === alias.toLowerCase()) return profile;
    }
  }
  // Check leading speaker tag or self-intro in text, e.g., "[수석비서 소하]", "소하:", "수석비서 소하입니다", "비서실장 태준입니다"
  for (const profile of Object.values(PERSONA_VOICE_MAP)) {
    if (profile.id === 'jarvis') continue;
    for (const alias of profile.aliases) {
      const esc = alias.replace(/[.*+?^${}()|[\]\\]/g, '\\$&');
      const leadRe = new RegExp(`^(?:\\[\\s*${esc}\\s*\\]|${esc}\\s*[:：]|(?:안녕하세요[,\\s]*|반갑습니다[,\\s]*|네[,\\s]*)*(?:대표님[,\\s]*)?(?:프론트오피스\\s*|백오피스\\s*|전사\\s*)?${esc}(?:입니다|예요|입니다만|드리겠습니다|보고드립니다))`, 'i');
      if (leadRe.test(raw)) return profile;
    }
  }
  return null;
}

function splitMultiPersonaSegments(rawText, defaultOptions = {}) {
  const text = String(rawText || '').trim();
  const baseVoice = defaultOptions.voice || VOICES[0];
  const baseSpeed = Number(defaultOptions.speed) || 1.25;
  const basePitch = defaultOptions.pitch || '+0Hz';
  const explicitProfile = resolvePersonaVoice(defaultOptions.agent || defaultOptions.persona);

  if (!text) return [];

  // Check if text contains multiple speaker turns separated by newlines or speaker labels like "소하: ... / 태준: ..."
  const speakerNamesPattern = Object.values(PERSONA_VOICE_MAP)
    .filter(p => p.id !== 'jarvis')
    .flatMap(p => p.aliases)
    .sort((a, b) => b.length - a.length)
    .map(a => a.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
    .join('|');

  const turnRegex = new RegExp(`(?:^|\\n|\\s+)(?:\\[\\s*(${speakerNamesPattern})\\s*\\]|(${speakerNamesPattern})\\s*[:：])\\s*`, 'gi');
  const matches = [];
  let match;
  while ((match = turnRegex.exec(text)) !== null) {
    const speakerLabel = match[1] || match[2];
    const profile = resolvePersonaVoice(speakerLabel);
    if (profile) {
      matches.push({
        index: match.index,
        contentStart: match.index + match[0].length,
        profile
      });
    }
  }

  if (matches.length >= 1) {
    const segments = [];
    if (matches[0].index > 3) {
      const leadText = text.slice(0, matches[0].index).trim();
      if (leadText) {
        const p0 = explicitProfile || resolvePersonaVoice(leadText);
        segments.push({
          text: leadText,
          voice: p0 ? p0.voice : baseVoice,
          speed: p0 ? p0.speed : baseSpeed,
          pitch: p0 ? p0.pitch : basePitch,
          personaId: p0 ? p0.id : 'jarvis',
          personaName: p0 ? p0.name : '자비스',
          gender: p0 ? p0.gender : 'male'
        });
      }
    }
    for (let i = 0; i < matches.length; i++) {
      const cur = matches[i];
      const nextIdx = i + 1 < matches.length ? matches[i + 1].index : text.length;
      const segText = text.slice(cur.contentStart, nextIdx).replace(/^["'「]|["'」]$/g, '').trim();
      if (segText) {
        segments.push({
          text: segText,
          voice: cur.profile.voice,
          speed: cur.profile.speed,
          pitch: cur.profile.pitch,
          personaId: cur.profile.id,
          personaName: cur.profile.name,
          gender: cur.profile.gender
        });
      }
    }
    if (segments.length > 0) return segments;
  }

  const singleProfile = explicitProfile || resolvePersonaVoice(text);
  if (singleProfile && !defaultOptions.explicitVoiceOverride) {
    return [{
      text,
      voice: singleProfile.voice,
      speed: singleProfile.speed,
      pitch: singleProfile.pitch,
      personaId: singleProfile.id,
      personaName: singleProfile.name,
      gender: singleProfile.gender
    }];
  }

  return [{
    text,
    voice: baseVoice,
    speed: baseSpeed,
    pitch: basePitch,
    personaId: singleProfile ? singleProfile.id : 'jarvis',
    personaName: singleProfile ? singleProfile.name : '자비스',
    gender: baseVoice === 'ko-KR-SunHiNeural' ? 'female' : 'male'
  }];
}

function speechOptions(config = {}, requested = {}) {
  const personaProfile = (!requested.voice && (requested.agent || requested.persona))
    ? resolvePersonaVoice(requested.agent || requested.persona)
    : null;
  const voice = requested.voice || (personaProfile && personaProfile.voice) || config.sayVoice || config.ttsVoice || VOICES[0];
  const speed = Number(requested.speed ?? (personaProfile && personaProfile.speed) ?? config.ttsSpeed ?? 1.25);
  if (!VOICES.includes(voice)) throw new Error('지원하지 않는 음성입니다. 음성 설정에서 다시 선택해 주세요.');
  if (!Number.isFinite(speed) || speed < 0.75 || speed > 1.5) throw new Error('음성 속도는 0.75~1.50 범위여야 합니다.');
  return {
    voice,
    speed,
    ...(personaProfile ? { pitch: personaProfile.pitch, personaId: personaProfile.id, personaName: personaProfile.name, gender: personaProfile.gender } : {})
  };
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

module.exports = { VOICES, PERSONA_VOICE_MAP, resolvePersonaVoice, splitMultiPersonaSegments, speechOptions, mp3ToWav };
