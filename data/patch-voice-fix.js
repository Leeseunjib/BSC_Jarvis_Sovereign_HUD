'use strict';
const fs = require('fs');

for (const p of ['C:\\자비스\\server.js', 'C:\\자비스\\server.verified.js']) {
  let s = fs.readFileSync(p, 'utf8');
  s = s.replace(
    /const explicitVoiceOverride = Boolean\(body\.voice\);\r?\n\s*const autoProfile = !explicitVoiceOverride \? resolvePersonaVoice\(body\.agent \|\| body\.persona \|\| rawInputText\) : null;\r?\n\s*const effectiveVoice = autoProfile \? autoProfile\.voice : voice;\r?\n\s*const effectiveSpeed = \(!body\.speed && autoProfile && autoProfile\.speed\) \? autoProfile\.speed : speed;/,
    "const freshTts = (() => { try { delete require.cache[require.resolve('./scripts/tts-audio')]; return require('./scripts/tts-audio'); } catch (_) { return { resolvePersonaVoice }; } })();\r\n" +
    "    const autoProfile = freshTts.resolvePersonaVoice(body.agent || body.persona || rawInputText);\r\n" +
    "    const explicitVoiceOverride = Boolean(body.voice) && !autoProfile;\r\n" +
    "    const effectiveVoice = autoProfile ? autoProfile.voice : voice;\r\n" +
    "    const effectiveSpeed = (autoProfile && autoProfile.speed) ? autoProfile.speed : speed;"
  );
  fs.writeFileSync(p, s, 'utf8');
  console.log('PATCHED:', p, s.includes('freshTts.resolvePersonaVoice'));
}

const { resolvePersonaVoice } = require('C:\\자비스\\scripts\\tts-audio');
console.log('TEST_RESOLVE_1:', resolvePersonaVoice('"소하: 네, 대표님! 수석비서 소하입니다. 무엇을 도와드릴까요?"'));
console.log('TEST_RESOLVE_2:', resolvePersonaVoice('소하야'));
