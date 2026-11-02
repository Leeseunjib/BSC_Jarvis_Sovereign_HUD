from pathlib import Path

root = Path('C:/자비스')
p = root / 'server.js'
s = p.read_text(encoding='utf-8-sig')
s = s.replace("const { VoiceSessions } = require('./scripts/voice-session');", "const { VoiceSessions } = require('./scripts/voice-session');\nconst { VOICES, speechOptions, mp3ToWav } = require('./scripts/tts-audio');\nlet lastTtsStatus = null;")
s = s.replace("async function synthesizeStudioNeuralMp3Buffer(rawText, voice = 'ko-KR-HyunsuMultilingualNeural')", "async function synthesizeStudioNeuralMp3Buffer(rawText, voice, userSpeed)")
s = s.replace("  const cfg = butler ? butler.getConfig() : {};\n  const userSpeed = Number(cfg.ttsSpeed) || 1.25;\n", '')
s = s.replace("const candidates = [voice, 'ko-KR-HyunsuMultilingualNeural', 'ko-KR-SunHiNeural', 'ko-KR-InJoonNeural'];", "const candidates = [voice]; // Never silently substitute a different speaker.")
s = s.replace("setTimeout(() => reject(new Error('timeout')), 5000)", "setTimeout(() => reject(new Error('timeout')), 8000)")
a = s.index('  // Tier 2: Fast Google Neural Korean MP3 fallback', s.index('async function synthesizeStudio'))
b = s.index('  return Buffer.alloc(0);', a)
s = s[:a] + s[b:]
s = s.replace('function synthesizeWindowsNativeWav(text, alsoSpeakOnHost = false)', 'function synthesizeWindowsNativeWav(text, speed = 1.25)')
s = s.replace("    const safe = String(text || '').replace(/'/g, \"''\").replace(/\\r?\\n/g, ' ');", "    const safe = String(text || '').replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/'/g, \"''\").replace(/\\r?\\n/g, ' ');\n    const rate = `${speed >= 1 ? '+' : ''}${Math.round((speed - 1) * 100)}%`;")
s = s.replace("if ($ko) { $synth.SelectVoice($ko.VoiceInfo.Name) }\n$synth.Rate = 1\n${alsoSpeakOnHost ? `$synth.SetOutputToDefaultAudioDevice(); $synth.Speak('${safe}');` : ''}", "if (!$ko) { throw 'No Korean Windows voice installed' }\n$synth.SelectVoice($ko.VoiceInfo.Name)\n$synth.Rate = 0")
s = s.replace("$synth.Speak('${safe}')", "$synth.SpeakSsml('<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"ko-KR\"><prosody rate=\"${rate}\">${safe}</prosody></speak>')")
a = s.index('  // 8-A) GET /api/tts-mp3')
b = s.index('  // 9) Static files', a)
s = s[:a] + '''  if (req.method === 'GET' && pathname === '/api/tts/status') {
    const cfg = butler.getConfig();
    return sendJson(res, 200, { ok: true, voices: VOICES, selectedVoice: cfg.sayVoice, speed: cfg.ttsSpeed, last: lastTtsStatus });
  }
  if ((req.method === 'GET' && pathname === '/api/tts-mp3') || (req.method === 'POST' && pathname === '/api/tts')) {
    const body = req.method === 'POST' ? await readBody(req) : Object.fromEntries(urlObj.searchParams);
    const text = humanizeKoreanSpeechText(body.text);
    if (!text) { res.writeHead(204); return res.end(); }
    let options;
    try { options = speechOptions(butler.getConfig(), body); }
    catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
    const { voice, speed } = options;
    let bytes = null, provider = 'edge-neural', actualVoice = voice, contentType = 'audio/mpeg';
    let error = null;
    if (voice !== 'windows-ko') {
      try {
        bytes = await synthesizeStudioNeuralMp3Buffer(text, voice, speed);
        if (bytes.length <= 256) throw new Error('선택한 Neural 음성을 생성하지 못했습니다.');
        if (body.format === 'wav') { bytes = await mp3ToWav(bytes); contentType = 'audio/wav'; }
      } catch (err) { error = err.message; bytes = null; }
    }
    if (!bytes || bytes.length <= 256) {
      bytes = await synthesizeWindowsNativeWav(text, speed);
      provider = 'windows-sapi'; actualVoice = 'windows-ko'; contentType = 'audio/wav';
    }
    const fallback = voice !== actualVoice;
    const ok = !!(bytes && bytes.length > 44);
    lastTtsStatus = { at: new Date().toISOString(), ok, requestedVoice: voice, actualVoice, speed, provider, fallback, error, bytes: bytes?.length || 0, contentType };
    recordEarEvent({ event: 'tts-generated', ts: Date.now(), ...lastTtsStatus });
    if (!ok) return sendJson(res, 503, { ok: false, error: '음성을 생성하지 못했습니다.' });
    res.writeHead(200, { ...CORS_HEADERS, 'Content-Type': contentType, 'Content-Length': bytes.length, 'Cache-Control': 'no-store',
      'X-Jarvis-Tts-Voice': actualVoice, 'X-Jarvis-Tts-Speed': String(speed), 'X-Jarvis-Tts-Provider': provider, 'X-Jarvis-Tts-Fallback': fallback ? '1' : '0' });
    return res.end(bytes);
  }

''' + s[b:]
old = '''    const updated = butler.saveConfig(body);
    return sendJson(res, 200, { ok: true, config: updated });'''
new = '''    if ('sayVoice' in body || 'ttsVoice' in body || 'ttsSpeed' in body) {
      try { speechOptions({ ...butler.getConfig(), ...body }); }
      catch (error) { return sendJson(res, 400, { ok: false, error: error.message }); }
      if ('sayVoice' in body) body.ttsVoice = body.sayVoice;
      else if ('ttsVoice' in body) body.sayVoice = body.ttsVoice;
    }
    const updated = butler.saveConfig(body);
    return sendJson(res, 200, { ok: true, config: updated });'''
assert old in s
s = s.replace(old, new)
p.write_text(s, encoding='utf-8')
print('Patched server audio and settings validation')
