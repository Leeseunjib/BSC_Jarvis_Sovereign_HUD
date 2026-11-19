'use strict';
(async () => {
  await fetch('http://127.0.0.1:7788/api/tasks/run', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      title: '자비스 서버 핫리스타트',
      command: "Start-Process powershell.exe -WindowStyle Hidden -ArgumentList '-NoProfile -File C:\\자비스\\data\\hot-restart.ps1'"
    })
  });
  await new Promise(r => setTimeout(r, 2800));
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
  console.log('FIXED_HEADERS:', {
    status: res.status,
    voice: res.headers.get('x-jarvis-tts-voice'),
    pitch: res.headers.get('x-jarvis-tts-pitch'),
    speed: res.headers.get('x-jarvis-tts-speed'),
    gender: res.headers.get('x-jarvis-tts-gender'),
    persona: decodeURIComponent(res.headers.get('x-jarvis-tts-persona') || '')
  });
  await fetch('http://127.0.0.1:7788/api/persona/speak', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({
      agent: 'soha',
      text: '소하: 이건우 대표님, 수석비서 소하입니다. 남성 목소리로 출력되던 버그를 해결하여 이제 성숙한 여성 중저음 보이스로 답변드립니다.'
    })
  });
})();
