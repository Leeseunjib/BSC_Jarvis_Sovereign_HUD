from pathlib import Path
root=Path('C:/자비스')
p=root/'server.js';s=p.read_text(encoding='utf-8-sig')
s=s.replace("const { VoiceSessions } = require('./scripts/voice-session');", "const { VoiceSessions } = require('./scripts/voice-session');\nconst { SpeakerGate, speakerMessage } = require('./scripts/speaker-gate');\nconst speakerGate = new SpeakerGate();")
a=s.index("const VOICEPRINT_FILE =")
b=s.index('function sendJson(',a)
s=s[:a]+'''function speakerEnrollmentHud() {
  const e = speakerGate.status().enrollment;
  if (!e || !speakerGate.enrolling) return null;
  const now = Date.now();
  const step = Math.min(2, Math.max(0, Math.floor((now - e.startAt) / 8000)));
  const title = now < e.startAt ? `목소리 등록 · ${Math.ceil((e.startAt-now)/1000)}초 뒤 시작` : now < e.endAt ? `목소리 등록 · ${step+1}/3 · ${Math.ceil((e.endAt-now)/1000)}초 남음` : '목소리 등록 · 녹음 비교 중';
  return { text: e.prompts[step], hudTitle: title, hudColor: 'cyan', isFinal: true, intent: 'speakerEnrollment', ts: Math.floor(now/1000)*1000 };
}

''' +s[b:]
a=s.index("    const body = await readBody(req);",s.index("pathname === '/api/butler/command'"))
b=s.index('    const executeOs =',a)
s=s[:a]+'''    const body = await readBody(req, 2700000);
    const sentence = String(body.sentence || '').trim().slice(0, 2000);
    if (body.source === 'voice') {
      if (global.__jarvisIsSpeaking) return sendJson(res, 200, { ok: true, ignored: true, reason: 'assistant-speaking' });
      if (!voiceSessions.accept(body.voiceSession, sentence, { commit: false, ownerVerified: speakerGate.enabled })) {
        return sendJson(res, 200, { ok: true, ignored: true, reason: 'wake-required' });
      }
      const decision = await speakerGate.verify(body.speakerAudio);
      if (!decision.allowed) {
        recordEarEvent({ event: 'speaker-rejected', ts: Date.now(), reason: decision.reason, score: decision.score });
        if (!speakerGate.enrolling) global.__jarvisLatestEarItem = { text: speakerMessage(decision.reason), isFinal: true, intent: 'speakerRejected', hudTitle: '음성 실행 보류', hudColor: 'amber', ts: Date.now() };
        return sendJson(res, 200, { ok: true, ignored: true, reason: decision.reason });
      }
      if (global.__jarvisIsSpeaking || !voiceSessions.accept(body.voiceSession, sentence, { ownerVerified: decision.verified })) return sendJson(res, 200, { ok: true, ignored: true, reason: 'voice-session-expired' });
    }
''' +s[b:]
needle="  if (req.method === 'POST' && pathname === '/api/ear/listen-once') {"
s=s.replace(needle,needle+"\n    if (speakerGate.enabled || speakerGate.enrolling) return sendJson(res, 409, { ok: false, error: '내 목소리 확인이 필요합니다. 메인 HUD의 상시 마이크를 사용하거나 직접 입력해 주세요.' });")
a=s.index("    if (global.__jarvisEnrollSamples",s.index("pathname === '/api/ear/level'"))
b=s.index("  if (req.method === 'POST' && pathname === '/api/ear/wake')",a)
s=s[:a]+'''    global.__jarvisLastVoiceFeatures = { pitchHz, energy, zcr, ts: Date.now() };
    return sendJson(res, 200, {
      ok: true, isSpeaking: Boolean(global.__jarvisIsSpeaking), mediaActive: butler.hasMediaContext(),
      forceAwakeUntil: global.__jarvisForceAwakeUntil || 0, speaker: speakerGate.status()
    });
  }
  if (req.method === 'GET' && pathname === '/api/speaker/state') return sendJson(res, 200, speakerGate.status());
  if (req.method === 'GET' && pathname === '/api/ear/voiceprint') return sendJson(res, 200, { ...speakerGate.status(), legacyPitchGate: false });
  if (req.method === 'POST' && pathname === '/api/ear/enroll-voice') return sendJson(res, 409, { ok: false, error: '음높이 등록은 지원하지 않습니다. 환경설정 → 음성 · 마이크에서 내 목소리를 등록해 주세요.' });
  if (req.method === 'POST' && pathname.startsWith('/api/speaker/')) {
    try {
      const body = await readBody(req, 1200000);
      if (pathname === '/api/speaker/enroll/start') {
        const health = global.__jarvisEarHealth;
        if (!health || !health.speakerCapture || health.microphone !== 'ready' || Date.now()-health.ts > 15000) throw new Error('등록용 마이크 연결을 기다린 뒤 다시 눌러 주세요.');
        if (global.__jarvisIsSpeaking) throw new Error('자비스 음성이 끝난 뒤 등록을 시작해 주세요.');
        return sendJson(res, 200, speakerGate.start());
      }
      if (pathname === '/api/speaker/enroll/complete') {
        const result = await speakerGate.finish(body.id, body.audio, body.transcript);
        global.__jarvisLatestEarItem = { text: '자비스, 지금 시간을 알려 주세요 라고 말씀해 보세요.', hudTitle: '내 목소리 등록 완료 · 내 목소리만 반응 켜짐', hudColor: 'lime', isFinal: true, ts: Date.now() };
        voiceSessions.sessions.clear(); global.__jarvisForceAwakeUntil = 0;
        return sendJson(res, 200, result);
      }
      if (pathname === '/api/speaker/enroll/cancel') return sendJson(res, 200, speakerGate.cancel());
      if (pathname === '/api/speaker/enabled') {
        if (typeof body.enabled !== 'boolean') throw new Error('켜기 또는 끄기를 선택해 주세요.');
        voiceSessions.sessions.clear(); global.__jarvisForceAwakeUntil = 0;
        return sendJson(res, 200, speakerGate.setEnabled(body.enabled));
      }
      if (pathname === '/api/speaker/forget') return sendJson(res, 200, speakerGate.forget());
      return sendJson(res, 404, { ok: false, error: '지원하지 않는 목소리 설정입니다.' });
    } catch (error) {
      return sendJson(res, 400, { ok: false, error: speakerMessage(error.message) });
    }
  }
''' +s[b:]
s=s.replace("  if (req.method === 'POST' && pathname === '/api/ear/push') {\n    const body = await readBody(req);", "  if (req.method === 'POST' && pathname === '/api/ear/push') {\n    const body = await readBody(req);\n    if (speakerGate.enabled || speakerGate.enrolling) return sendJson(res, 200, { ok: true, suppressed: true });")
s=s.replace('      latest: global.__jarvisTimerDisplay', '      latest: speakerEnrollmentHud() || (global.__jarvisTimerDisplay')
s=s.replace('? global.__jarvisTimerDisplay : global.__jarvisLatestEarItem || null,', '? global.__jarvisTimerDisplay : global.__jarvisLatestEarItem || null),')
s=s.replace('<script src="/js/jarvis-react-hud.js"></script>', '<script src="/js/speaker-capture.js"></script>\n<script src="/js/jarvis-react-hud.js"></script>')
p.write_text(s,encoding='utf-8')

p=root/'public/js/jarvis-react-hud.js';s=p.read_text(encoding='utf-8-sig')
s=s.replace('endpoint-media-v6-shared-track','endpoint-media-v7-speaker-gate')
s=s.replace('turnRef, speechActivityRef) {', 'turnRef, speechActivityRef, captureRef) {')
s=s.replace('        src.connect(analyser);', '''        src.connect(analyser);
        if (captureRef.current) {
          try { await captureRef.current.connect(ctx, src); reportEarHealth({ speakerCapture: true }, 'speaker-capture-ready'); }
          catch (error) { reportEarHealth({ speakerCapture: false }, 'speaker-capture-unavailable'); }
        }''')
s=s.replace("                if (d && typeof d.matchScore === 'number') {\n                  voiceFeaturesRef.current.matchScore = d.matchScore;\n                  voiceFeaturesRef.current.isOwnerVoice = Boolean(d.isOwnerVoice);\n                }", "                if (d && captureRef.current) captureRef.current.sync(d.speaker, d.isSpeaking);")
s=s.replace('    const executeRef = useRef(null);', '    const executeRef = useRef(null);\n    const captureRef = useRef(window.JarvisSpeakerCapture ? new window.JarvisSpeakerCapture() : null);')
s=s.replace('turnRef, speechActivityRef);', 'turnRef, speechActivityRef, captureRef);')
a=s.index('      // Owner Voice Print & Proximity Gate:')
b=s.index('      const now = Date.now();',a)
s=s[:a]+'''      const capture = captureRef.current;
      const speakerAudio = source === 'voice' && capture ? capture.finishTurn() : null;
      if (source === 'voice' && capture && capture.enrolling) return;

''' +s[b:]
s=s.replace('source, voiceSession: voiceSessionRef.current })', 'source, voiceSession: voiceSessionRef.current, speakerAudio })')
s=s.replace("        onFailure: text => {\n          discardResultsRef.current = true;", "        onFailure: text => {\n          if (captureRef.current) captureRef.current.reset();\n          discardResultsRef.current = true;")
s=s.replace("        onPreview: text => {", "        onPreview: text => {\n          if (captureRef.current && (captureRef.current.enrolling || captureRef.current.protected)) return;")
s=s.replace('        speechActivityRef.current = true;\n        turnRef.current.audio(1);', '        speechActivityRef.current = true;\n        if (captureRef.current) captureRef.current.beginTurn();\n        turnRef.current.audio(1);')
needle="        reportEarHealth({ recognition: 'listening', error: '' });\n        networkFailures = 0;"
s=s.replace(needle,'''        if (captureRef.current && captureRef.current.enrolling) {
          for (let i = event.resultIndex || 0; i < event.results.length; i++) captureRef.current.noteText(event.results[i][0].transcript, `${generation}:${i}`);
          turnRef.current.stop();
          return;
        }
        reportEarHealth({ recognition: 'listening', error: '' });
        networkFailures = 0;''')
p.write_text(s,encoding='utf-8')
p=root/'public/js/jarvis-app.js';s=p.read_text(encoding='utf-8-sig');s=s.replace("onCommit: text => sendCommand(text, continuousMode ? 'voice' : 'manual'),", "onCommit: text => sendCommand(text, 'voice'),");p.write_text(s,encoding='utf-8')
print('Integrated speaker verification into all microphone command entry points')
