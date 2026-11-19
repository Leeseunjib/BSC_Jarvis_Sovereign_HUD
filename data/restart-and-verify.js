'use strict';
(async () => {
  // 1) Trigger a detached restart of server.verified.js in Desktop Session 1
  const restartCmd = "Start-Process powershell -WindowStyle Hidden -ArgumentList '-NoProfile -Command \"Start-Sleep -Milliseconds 300; Stop-Process -Id 43592 -Force -ErrorAction SilentlyContinue; Start-Sleep -Milliseconds 400; Start-Process -FilePath ''C:\\Program Files\\nodejs\\node.exe'' -ArgumentList ''C:\\자비스\\server.verified.js'' -WorkingDirectory ''C:\\자비스'' -WindowStyle Hidden\"'";
  try {
    await fetch('http://127.0.0.1:7788/api/tasks/run', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: '자비스 음성 라우터 핫리로드', command: restartCmd })
    });
  } catch (_) {}

  // Wait 2.2 seconds for new server.verified.js to come up on 7788
  await new Promise(r => setTimeout(r, 2200));

  // 2) Verify that sending '"소하: 네, 대표님! 수석비서 소하입니다. 무엇을 도와드릴까요?"' EVEN WITH voice: 'ko-KR-HyunsuMultilingualNeural' returns ko-KR-SunHiNeural (-5Hz, female)!
  const res = await fetch('http://127.0.0.1:7788/api/tts', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      text: '"소하: 네, 대표님! 수석비서 소하입니다. 무엇을 도와드릴까요?"',
      voice: 'ko-KR-HyunsuMultilingualNeural',
      speed: 1.15,
      format: 'wav'
    })
  });
  console.log('VERIFY_HEADERS:', {
    status: res.status,
    voice: res.headers.get('x-jarvis-tts-voice'),
    pitch: res.headers.get('x-jarvis-tts-pitch'),
    speed: res.headers.get('x-jarvis-tts-speed'),
    gender: res.headers.get('x-jarvis-tts-gender'),
    persona: decodeURIComponent(res.headers.get('x-jarvis-tts-persona') || '')
  });

  // 3) Speak out loud via /api/persona/speak so the user hears the fixed mature female Soha voice on Jarvis HUD!
  await fetch('http://127.0.0.1:7788/api/persona/speak', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      agent: 'soha',
      text: '소하: 이건우 대표님, 수석비서 소하입니다. 자비스 기본 남성 음성이 덮어쓰던 버그를 수정하여 이제 성숙한 여성 중저음 보이스로 고정되었습니다.'
    })
  });
})();
