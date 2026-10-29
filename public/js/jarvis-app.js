/**
 * @file C:\자비스\public\js\jarvis-app.js
 * @description Real-Time Voice Listener & Speaker Client Runtime for Windows 11 자비스 (SasariSa v0.7.3):
 *   - Continuous Always-On SpeechRecognition (Ear.swift 1:1: Wake Word -> 8s Awake Follow-up -> Barge-in)
 *   - Real-Time WebAudio Microphone AnalyserNode driving OrbView (#orbCore scale & dB level)
 *   - Windows Native Hardware Microphone Fallback (/api/ear/listen-once via System.Speech)
 *   - 3-Tier Korean Voice Output (96kbps Neural TTS -> Windows SAPI WAV -> Browser SpeechSynthesis)
 */

(function () {
  const escapeHtml = value => String(value ?? '').replace(/[&<>"']/g, ch => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[ch]);
  const voiceSession = `web-${Date.now()}-${Math.random().toString(36).slice(2)}`;
  let speechTurn = null;
  let appState = {
    config: {},
    telemetry: {},
    tutorialStep: 0,
    listening: false,
    alwaysOn: false,
    awakeUntil: 0,
    speaking: false,
    recognition: null,
    currentAudio: null,
    micStream: null,
    micAnimId: null
  };

  const TUTORIAL_STEPS = [
    {
      title: '1단계 · 실제 음성으로 부르기 (“자비스 내 말 들려?”)',
      desc: '「🎙 마이크 듣기」 또는 상단 「🎙 상시 음성 대기 켜기」를 누르고 마이크로 “자비스 내 말 들려?”라고 말씀해 보세요. 오브(Orb)가 대표님 목소리 크기에 맞춰 실시간으로 맥동하며 한국어 음성으로 즉시 대답합니다.',
      sampleCmd: '자비스 내 말 들려?'
    },
    {
      title: '2단계 · 화려한 귀환 인사 (“아빠 왔어” · BootScene.swift)',
      desc: '「아빠 왔어」 또는 「다녀왔어」라고 말하면 44.1kHz 합성 차임벨과 함께 330px 아크 리액터, 레이더 스윕, 실시간 RAM/디스크 부트 시퀀스가 펼쳐진 뒤 상단 오브로 빨려 들어갑니다.',
      sampleCmd: '아빠 왔어'
    },
    {
      title: '3단계 · 유튜브 실시간 검색 후 1~5번 골라 틀기 (Butler.swift ytList)',
      desc: '「유튜브에서 로파이 검색해」라고 하면 상위 5개 영상을 찾아 보여주고, 이어서 「1번 틀어」라고 말하면 즉시 해당 영상을 재생합니다.',
      sampleCmd: '유튜브에서 로파이 검색해'
    },
    {
      title: '4단계 · 카카오톡 메시지 확인',
      desc: '메시지 내용을 보여드립니다. 수신자와 대화방을 확인한 뒤 카카오톡에서 직접 입력하고 보내 주세요.',
      sampleCmd: '카톡 영웅님한테 오늘도 힘내세요라고 답장해'
    },
    {
      title: '5단계 · 되돌리기 & 음성 루틴 학습 (Learning.swift & Routines.swift)',
      desc: '음량 명령은 반대 방향으로 조정할 수 있습니다. 일부 다른 명령은 자동으로 되돌릴 수 없습니다. 음성 루틴을 저장할 수 있습니다.',
      sampleCmd: '방금 뭐 했어'
    }
  ];

  // 1. Exact WebAudio Port of BootScene.synthChime() (Sources/MacButler/BootScene.swift lines 33-49)
  function playSynthChime() {
    try {
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      const ctx = new AudioCtx({ sampleRate: 44100 });
      const rate = 44100;
      const dur = 2.4;
      const len = Math.floor(rate * dur);
      const buffer = ctx.createBuffer(1, len, rate);
      const data = buffer.getChannelData(0);

      for (let i = 0; i < len; i++) {
        const t = i / rate;
        const sweepF = 90 + 520 * Math.pow(Math.min(t / 1.4, 1), 2);
        const env = t < 1.4 ? t / 1.4 : Math.max(0, 1 - (t - 1.4) / 0.4);
        let v = 0.22 * Math.sin(2 * Math.PI * sweepF * t) * env + 0.08 * Math.sin(4 * Math.PI * sweepF * t) * env;
        const pings = [
          [1.45, 1318.5],
          [1.7, 1975.5]
        ];
        for (const [start, f] of pings) {
          if (t > start) {
            const dt = t - start;
            v += 0.18 * Math.sin(2 * Math.PI * f * dt) * Math.exp(-dt * 4);
          }
        }
        data[i] = v;
      }

      const src = ctx.createBufferSource();
      src.buffer = buffer;
      src.connect(ctx.destination);
      src.start();
    } catch (e) {
      console.warn('synthChime WebAudio fallback:', e);
    }
  }

  // 2. Real-Time WebAudio Microphone Level Meter driving OrbView (#orbCore)
  async function startMicLevelVisualizer() {
    if (appState.micStream) return;
    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      appState.micStream = stream;
      const AudioCtx = window.AudioContext || window.webkitAudioContext;
      const ctx = new AudioCtx();
      const src = ctx.createMediaStreamSource(stream);
      const analyser = ctx.createAnalyser();
      analyser.fftSize = 256;
      src.connect(analyser);
      const buf = new Uint8Array(analyser.frequencyBinCount);
      const timeData = new Float32Array(analyser.fftSize);
      const orbCore = document.getElementById('orbCore');
      const indicator = document.getElementById('micLevelIndicator');

      function tick() {
        analyser.getByteFrequencyData(buf);
        let sum = 0;
        for (let i = 0; i < buf.length; i++) sum += buf[i];
        const avg = sum / buf.length;
        const pct = Math.min(100, Math.round((avg / 90) * 100));
        const scale = 1 + (pct / 100) * 0.55;
        analyser.getFloatTimeDomainData(timeData);
        const rms = Math.sqrt(timeData.reduce((sum, value) => sum + value * value, 0) / timeData.length);
        if (speechTurn) speechTurn.audio(Math.min(1, rms * 18));
        if (orbCore) orbCore.style.transform = `scale(${scale.toFixed(2)})`;
        if (indicator) indicator.textContent = `MIC: ${pct}% ${pct > 15 ? '🟢 음성 감지 중' : '대기 중'}`;
        appState.micAnimId = requestAnimationFrame(tick);
      }
      tick();
    } catch (e) {
      console.warn('Mic visualizer warning:', e.message);
    }
  }

  // 3. Play BootScene ("아빠 왔어" / "다녀왔어" -> BootScene.swift 1:1)
  function triggerBootScene(style = 'reactor', telemetry = null) {
    if (style === 'card') return;
    const overlay = document.getElementById('bootSceneOverlay');
    const reactor = document.getElementById('bootReactorWrap');
    const logPanel = document.getElementById('bootLogPanel');
    const grid = document.getElementById('bootGrid');
    const radar = document.getElementById('bootRadar');

    const sys = telemetry || appState.telemetry || { usedMemGB: 14.2, totalMemGB: 32, diskFreeGB: 142, osVersion: 'Windows 11' };
    const name = (appState.config.assistantName || '자비스').toUpperCase();
    const user = appState.config.userName || '대표님';

    const calm = style === 'calm';
    grid.style.display = calm ? 'none' : 'block';
    radar.style.display = calm ? 'none' : 'block';
    logPanel.style.display = calm ? 'none' : 'block';

    const now = new Date();
    document.getElementById('bootClockHm').textContent = now.toTimeString().slice(0, 5);
    document.getElementById('bootClockDate').textContent = now.toLocaleDateString('ko-KR', {
      year: 'numeric',
      month: '2-digit',
      day: '2-digit',
      weekday: 'long'
    });
    document.getElementById('bootTitleBig').textContent = name.split('').join(' ');
    document.getElementById('bootHelloBig').textContent = (appState.config.greetingText || '어서 오십시오, {user}.').replace('{user}', user);

    const lines = [
      `${name} · BOOT SEQUENCE`,
      '────────────────────────────────',
      '> 음성 인식 엔진 (Ear.swift · Whisper) … 준비됨',
      '> 손쉬운 사용 제어 (Tools/jax.swift) … 준비됨',
      '> 생각하는 두뇌 (Brain · Fast Router) … 온라인',
      `> 메모리 사용량 … ${sys.usedMemGB} / ${sys.totalMemGB} GB`,
      `> 디스크 여유 공간 … ${sys.diskFreeGB} GB`,
      `> ${sys.osVersion} · ${sys.cpuModel || 'x64'}`,
      '> 모든 시스템 정상 가동 (ALL SYSTEMS NOMINAL)'
    ];

    logPanel.innerHTML = '';
    reactor.classList.remove('fly-to-orb');
    overlay.classList.add('active');
    playSynthChime();

    lines.forEach((line, idx) => {
      setTimeout(() => {
        const div = document.createElement('div');
        div.style.color = idx === 0 ? '#ffffff' : 'var(--cyan)';
        div.textContent = line;
        logPanel.appendChild(div);
      }, 220 + idx * 210);
    });

    setTimeout(() => {
      reactor.classList.add('fly-to-orb');
    }, 3900);

    setTimeout(() => {
      overlay.classList.remove('active');
      reactor.classList.remove('fly-to-orb');
    }, 4650);
  }

  // 4. Speak via 3-Tier Korean Voice Output (96kbps Neural MP3 -> Windows SAPI WAV -> Web Speech)
  async function speakText(text) {
    if (!text) return;
    if (appState.currentAudio) {
      appState.currentAudio.pause();
      appState.currentAudio = null;
    }
    const orbView = document.getElementById('orbView');
    orbView.classList.add('fast');
    appState.speaking = true;

    try {
      const audio = new Audio('/api/tts-mp3?text=' + encodeURIComponent(text));
      appState.currentAudio = audio;
      audio.onended = () => {
        orbView.classList.remove('fast');
        appState.speaking = false;
        appState.awakeUntil = Date.now() + 8000;
      };
      await audio.play();
      return;
    } catch (_) {
      orbView.classList.remove('fast');
      appState.speaking = false;
      if ('speechSynthesis' in window) {
        const utterance = new SpeechSynthesisUtterance(text);
        utterance.lang = 'ko-KR';
        window.speechSynthesis.speak(utterance);
      }
    }
  }

  // 5. Update HUD Card (Image 3 & HUD.swift show() 1:1 — 2 clean lines only)
  function updateHud({ color = 'cyan', title, subtitle, heard, awaitingConfirm = false, ytList = null }) {
    const card = document.getElementById('hudCard');
    card.className = `sasarisa-hud-card color-${color}`;
    if (title) document.getElementById('hudStatus').textContent = title;
    if (subtitle) document.getElementById('hudBody').textContent = subtitle;
    else if (heard) document.getElementById('hudBody').textContent = heard;

    document.getElementById('btnConfirmYes').style.display = awaitingConfirm ? 'inline-block' : 'none';
    document.getElementById('btnConfirmNo').style.display = awaitingConfirm ? 'inline-block' : 'none';

    if (Array.isArray(ytList) && ytList.length > 0) {
      const container = document.getElementById('ytResultsContainer');
      const box = document.getElementById('ytListBox');
      container.style.display = 'block';
      box.innerHTML = ytList
        .map(
          v => `
        <div class="yt-item" data-yt-index="${escapeHtml(v.index)}">
          <span><b>${escapeHtml(v.index)}번</b> · ${escapeHtml(v.title)}</span>
          <span style="color:var(--cyan);font-weight:700;">▶ 바로 재생</span>
        </div>`
        )
        .join('');
      box.querySelectorAll('.yt-item').forEach(el => {
        el.addEventListener('click', () => {
          sendCommand(`${el.dataset.ytIndex}번 틀어`);
        });
      });
    }
  }

  function appendLog(line) {
    const box = document.getElementById('liveLogBox');
    const ts = new Date().toTimeString().slice(0, 8);
    box.textContent = `[${ts}] ${line}\n` + box.textContent;
  }

  // 6. Send Spoken/Typed Command to ButlerEngine
  async function sendCommand(sentence, source = 'manual') {
    const clean = String(sentence || '').trim();
    if (!clean) return;
    document.getElementById('hudCommandInput').value = '';
    if (source !== 'voice') updateHud({ color: 'cyan', title: '● 처리 중…', subtitle: '말씀하신 내용을 분석하고 있습니다…', heard: clean });

    try {
      const res = await fetch('/api/butler/command', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ sentence: clean, executeOs: true, source, voiceSession })
      });
      const data = await res.json();
      if (data.ignored) return;
      if (!data.ok) {
        updateHud({ color: 'amber', title: data.error || '명령 실행 실패', subtitle: data.detail || '다시 확인해 주세요.', heard: clean });
        return;
      }
      const r = data.result;

      appState.awakeUntil = Date.now() + Math.max(1, Math.min(120, Number(appState.config.followUpSec) || 30)) * 1000;

      updateHud({
        color: r.hudColor || 'cyan',
        title: r.hudTitle,
        subtitle: (r.awaitingConfirm || r.intent === 'draftMailReply') ? (r.hudSubtitle || r.spoken) : (r.spoken || r.hudSubtitle),
        heard: r.heard,
        awaitingConfirm: Boolean(r.awaitingConfirm),
        ytList: r.ytList
      });

      appendLog(`🎤 대표님: 「${clean}」\n🗣 자비스: 「${r.spoken || r.hudSubtitle}」`);

      if (r.bootScene) {
        triggerBootScene(r.bootStyle, r.telemetry);
      }
      if (r.spoken) {
        await speakText(r.spoken);
      }
      refreshStatus();
    } catch (err) {
      updateHud({ color: 'amber', title: '⚠︎ 서버 통신 오류', subtitle: err.message, heard: clean });
    }
  }

  // 7. Continuous / Single Browser Web Speech API + Windows Native Hardware Mic Fallback
  async function triggerWindowsNativeMic() {
    updateHud({
      color: 'cyan',
      title: '🎧 Windows 하드웨어 마이크 직접 청취 중 (5초간 말씀하세요)…',
      subtitle: '예: “자비스 내 말 들려?”, “아빠 왔어”, “지금 몇 시야?”, “소리 키워”',
      heard: '듣는 중…'
    });
    startMicLevelVisualizer();
    try {
      const res = await fetch('/api/ear/listen-once', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ timeoutSec: 5 })
      });
      const data = await res.json();
      if (!data.ok) {
        updateHud({
          color: 'amber',
          title: '⚠︎ 음성이 감지되지 않았습니다',
          subtitle: data.message || '마이크 가까이에서 다시 말씀해 주세요.',
          heard: ''
        });
        return;
      }
      const r = data.result;
      updateHud({
        color: r.hudColor || 'cyan',
        title: r.hudTitle,
        subtitle: r.spoken || r.hudSubtitle,
        heard: data.transcript,
        awaitingConfirm: Boolean(r.awaitingConfirm),
        ytList: r.ytList
      });
      appendLog(`🎤 [Windows 마이크] 대표님: 「${data.transcript}」\n🗣 자비스: 「${r.spoken || r.hudSubtitle}」`);
      if (r.bootScene) triggerBootScene(r.bootStyle, r.telemetry);
      if (r.spoken) await speakText(r.spoken);
    } catch (err) {
      updateHud({ color: 'amber', title: '⚠︎ 마이크 호출 오류', subtitle: err.message, heard: '' });
    }
  }

  function startBrowserSpeechRecognition(continuousMode = false) {
    startMicLevelVisualizer();
    const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
    if (!SpeechRec) {
      triggerWindowsNativeMic();
      return;
    }

    const btnListen = document.getElementById('btnMicListen');
    const btnAlways = document.getElementById('btnAlwaysOnMic');

    if (appState.listening && appState.recognition) {
      if (speechTurn) speechTurn.stop();
      appState.alwaysOn = false;
      appState.listening = false;
      appState.recognition.stop();
      btnListen.textContent = '🎙 마이크 듣기 (실시간 음성 대화)';
      btnAlways.textContent = '🎙 상시 음성 대기 켜기 (Ear.swift)';
      return;
    }

    appState.alwaysOn = continuousMode;
    const rec = new SpeechRec();
    rec.lang = 'ko-KR';
    rec.interimResults = true;
    rec.continuous = continuousMode;
    if (speechTurn) speechTurn.stop();
    let discardResults = false;
    speechTurn = new window.SpeechTurnBuffer({
      isMuted: () => appState.speaking,
      onFinalize: () => { try { rec.stop(); } catch (_) {} },
      onReset: () => { try { rec.abort(); } catch (_) {} },
      onFailure: text => {
        discardResults = true;
        const normalize = s => String(s || '').toLowerCase().replace(/[^a-z0-9가-힣]/g, '');
        const control = window.JarvisMediaIntents.classifyMediaControl(text);
        if (continuousMode && Date.now() >= appState.awakeUntil && (!control || control.intent !== 'stopMusic') && ![...(appState.config.wakeWords || []), appState.config.assistantName || '자비스'].some(w => normalize(w) && normalize(text).includes(normalize(w)))) return;
        updateHud({ color: 'amber', title: '음성 확정 실패 · 다시 말씀하거나 입력해 주세요', subtitle: text, heard: text });
        fetch('/api/ear/push', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text, status: 'recognition-error' }) }).catch(() => {});
      },
      onCommit: text => sendCommand(text, continuousMode ? 'voice' : 'manual'),
      onPreview: text => {
        const normalize = s => String(s || '').toLowerCase().replace(/[^a-z0-9가-힣]/g, '');
        const wakeWords = [...(appState.config.wakeWords || []), appState.config.assistantName || '자비스'];
        const control = window.JarvisMediaIntents.classifyMediaControl(text);
        if (continuousMode && Date.now() >= appState.awakeUntil && (!control || control.intent !== 'stopMusic') && !wakeWords.some(w => normalize(w) && normalize(text).includes(normalize(w)))) return;
        document.getElementById('hudStatus').textContent = '○ 계속 듣는 중';
        document.getElementById('hudBody').textContent = text;
        fetch('/api/ear/push', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text, isFinal: false }) }).catch(() => {});
      }
    });
    let generation = 0;
    rec.onstart = () => { generation++; discardResults = false; };
    appState.recognition = rec;
    appState.listening = true;

    btnListen.textContent = '🛑 음성 듣는 중… (클릭 시 중지)';
    if (continuousMode) {
      btnAlways.textContent = '🟢 상시 음성 대기 중 (“자비스”라고 부르세요)';
    }

    updateHud({
      color: 'cyan',
      title: '○ 받아 적는 중',
      subtitle: '클래식 음악 좀 틀어 줘',
      heard: ''
    });

    rec.onresult = ev => {
      if (!appState.listening || appState.recognition !== rec || discardResults || appState.speaking) return;
      let interim = '';
      const finals = [];
      for (let i = ev.resultIndex; i < ev.results.length; i++) {
        const t = ev.results[i][0].transcript;
        if (ev.results[i].isFinal) finals.push({ id: `${generation}:${i}`, text: t });
        else interim += `${t} `;
      }
      speechTurn.push(finals, interim.trim());
    };

    rec.onerror = err => {
      if (appState.recognition !== rec || !appState.listening) return;
      if (err.error === 'not-allowed' || err.error === 'service-not-allowed') {
        appState.alwaysOn = false;
        appState.listening = false;
        triggerWindowsNativeMic();
      }
    };

    rec.onend = () => {
      if (appState.recognition !== rec || !appState.listening) return;
      if (speechTurn) speechTurn.ended();
      if (appState.alwaysOn) {
        try {
          rec.start();
          return;
        } catch (_) {}
      }
      appState.listening = false;
      btnListen.textContent = '🎙 마이크 듣기 (실시간 음성 대화)';
      btnAlways.textContent = '🎙 상시 음성 대기 켜기 (Ear.swift)';
    };

    rec.start();
  }

  function applyHudPosition(pos) {
    const wrapper = document.getElementById('hudWrapper');
    wrapper.className = `sasarisa-hud-wrapper pos-${pos || 'top'}`;
  }

  async function refreshStatus() {
    try {
      const res = await fetch('/api/status');
      const data = await res.json();
      if (!data.ok) return;
      appState.config = data.config;
      appState.telemetry = data.telemetry;

      document.getElementById('assistantTitleBadge').textContent = `${data.config.assistantName || '자비스'} (SasariSa v${data.version})`;
      document.getElementById('voiceEngineBadge').textContent = `Neural TTS: ${data.config.sayVoice || 'ko-KR-SunHiNeural'}`;
      applyHudPosition(data.config.hudPosition || 'top');

      document.getElementById('setupAssistantName').value = data.config.assistantName || '자비스';
      document.getElementById('setupUserName').value = data.config.userName || '대표님';
      document.getElementById('setupHudPosition').value = data.config.hudPosition || 'top';
      document.getElementById('cfgWakeWords').value = (data.config.wakeWords || []).join(', ');
      document.getElementById('cfgGreetingText').value = data.config.greetingText || '';
      document.getElementById('cfgBootStyle').value = data.config.bootStyle || 'reactor';
      document.getElementById('cfgPreviewBeforeSend').checked = Boolean(data.config.previewBeforeSend);
      document.getElementById('cfgFreeMode').checked = Boolean(data.config.freeMode);
      document.getElementById('cfgStartup').checked = Boolean(data.startup?.registered);
      document.getElementById('cfgSayVoice').value = data.config.sayVoice || 'ko-KR-SunHiNeural';
      document.getElementById('cfgBrainProvider').value = data.config.brainProvider || 'claude-code';
      document.getElementById('cfgGrowTogether').value = data.config.growTogether || 'together';

      const todayUsage = data.daily?.[data.daily.length - 1] || { calls: 0, tokens: 0 };
      document.getElementById('telemetryBox').innerHTML = `
        <div>• <b>OS / CPU</b>: ${escapeHtml(data.telemetry.osVersion)} · ${escapeHtml(data.telemetry.cpuModel)}</div>
        <div>• <b>메모리 / 디스크</b>: RAM ${escapeHtml(data.telemetry.usedMemGB)} / ${escapeHtml(data.telemetry.totalMemGB)} GB 사용 중 · 여유 ${escapeHtml(data.telemetry.diskFreeGB ?? '확인 불가')} GB</div>
        <div>• <b>오늘 실행 횟수</b>: 총 <b>${todayUsage.calls}회</b> 호출 (실제 AI 토큰 사용량은 측정하지 않음)</div>
        <div>• <b>저장된 음성 루틴 (` + '`routines.json`' + `)</b>: ${Object.keys(data.routines || {}).map(k => `「${escapeHtml(k)}」`).join(', ')}</div>
      `;
    } catch (_) {}
  }

  async function openReaModal() {
    document.getElementById('reaModal').classList.add('open');
    const box = document.getElementById('reaModalContent');
    try {
      const res = await fetch('/api/rea/blueprint');
      const data = await res.json();
      if (!data.ok) {
        box.textContent = 'REA 분석 리포트를 불러올 수 없습니다.';
        return;
      }
      const rep = data.report;
      box.innerHTML = `
        <div class="form-section">
          <h4>1. REA 4분할 증거 추적 (4-Split Evidence Provenance · rea.config.json)</h4>
          <p>이 보고서는 과거 정적 분석 자료이며 기능의 동등성이나 구현 완료를 보증하지 않습니다.</p>
          <div style="margin-bottom:8px;"><b style="color:var(--cyan);">[Observations — 실측 물리 수치]</b><br/>${rep.evidence4Split.observations.map(x => `• ${escapeHtml(x)}`).join('<br/>')}</div>
          <div style="margin-bottom:8px;"><b style="color:var(--lime);">[Derivations — 심볼 추출]</b><br/>${rep.evidence4Split.derivations.map(x => `• ${escapeHtml(x)}`).join('<br/>')}</div>
          <div style="margin-bottom:8px;"><b style="color:var(--amber);">[Inferences — 미검증 추정]</b><br/>${rep.evidence4Split.inferences.map(x => `• ${escapeHtml(x)}`).join('<br/>')}</div>
          <div><b style="color:#cbd5e1;">[Unknowns]</b><br/>${rep.evidence4Split.unknowns.map(x => `• ${escapeHtml(x)}`).join('<br/>')}</div>
        </div>

        <div class="form-section">
          <h4>2. macOS 원본과 Windows 구현 비교 (동등성 미검증)</h4>
          <table class="rea-table">
            <thead>
              <tr><th>원본 Swift 모듈</th><th>원본 핵심 메커니즘</th><th>Windows 11 C:\\자비스 구현 파일</th><th>상태</th></tr>
            </thead>
            <tbody>
              ${rep.cleanRoomMap.map(m => `<tr><td><code>${escapeHtml(m.upstreamFile)}</code></td><td>${escapeHtml(m.upstreamRole)}</td><td><code>${escapeHtml(m.windowsEquivalent)}</code></td><td>동등성 미검증</td></tr>`).join('')}
            </tbody>
          </table>
        </div>
      `;
    } catch (e) {
      box.textContent = 'REA 로드 실패: ' + e.message;
    }
  }

  let altDownAt = 0;
  window.addEventListener('keydown', e => {
    if (e.key === 'Alt' && !altDownAt) {
      altDownAt = Date.now();
    }
  });
  window.addEventListener('keyup', e => {
    if (e.key === 'Alt' && altDownAt) {
      const heldMs = Date.now() - altDownAt;
      altDownAt = 0;
      if (heldMs < 350) {
        startBrowserSpeechRecognition(false);
      }
    }
  });

  function renderTutorialStep() {
    const s = TUTORIAL_STEPS[appState.tutorialStep];
    document.getElementById('tutStepBadge').textContent = `${appState.tutorialStep + 1} / ${TUTORIAL_STEPS.length} 단계`;
    document.getElementById('tutTitle').textContent = s.title;
    document.getElementById('tutDesc').textContent = s.desc;
  }

  document.addEventListener('DOMContentLoaded', () => {
    refreshStatus();
    renderTutorialStep();
    appendLog('자비스 실시간 음성 인식 & Neural TTS 대화 시스템이 준비되었습니다.');

    document.getElementById('btnSendCmd').addEventListener('click', () => {
      sendCommand(document.getElementById('hudCommandInput').value);
    });
    document.getElementById('hudCommandInput').addEventListener('keydown', e => {
      if (e.key === 'Enter') sendCommand(e.target.value);
    });

    document.getElementById('btnMicListen').addEventListener('click', () => startBrowserSpeechRecognition(false));
    document.getElementById('btnAlwaysOnMic').addEventListener('click', () => startBrowserSpeechRecognition(true));
    document.getElementById('btnWinNativeMic').addEventListener('click', triggerWindowsNativeMic);
    document.getElementById('orbView').addEventListener('click', () => startBrowserSpeechRecognition(false));
    document.getElementById('btnWakeToggle').addEventListener('click', () => sendCommand('자비스 내 말 들려?'));

    document.getElementById('btnConfirmYes').addEventListener('click', () => sendCommand('응'));
    document.getElementById('btnConfirmNo').addEventListener('click', () => sendCommand('아니'));

    document.querySelectorAll('.cmd-chip').forEach(btn => {
      btn.addEventListener('click', () => sendCommand(btn.dataset.cmd));
    });

    document.getElementById('btnGreet').addEventListener('click', () => sendCommand('아빠 왔어'));
    document.getElementById('btnOpenSetup').addEventListener('click', () => {
      document.getElementById('setupModal').classList.add('open');
    });
    document.getElementById('btnOpenSettings').addEventListener('click', () => {
      document.getElementById('settingsModal').classList.add('open');
    });
    document.getElementById('btnOpenRea').addEventListener('click', openReaModal);

    document.getElementById('btnForgetLearned').addEventListener('click', async () => {
      await fetch('/api/forget-learned', { method: 'POST' });
      updateHud({ color: 'lime', title: '✓ 배운 말 기록 초기화 완료', subtitle: 'learned.json 기록을 지웠습니다.' });
    });

    document.querySelectorAll('[data-close]').forEach(btn => {
      btn.addEventListener('click', () => {
        document.getElementById(btn.dataset.close).classList.remove('open');
      });
    });

    document.getElementById('setupHudPosition').addEventListener('change', e => {
      applyHudPosition(e.target.value);
      updateHud({
        color: 'lime',
        title: '✓ 여기에 떠요',
        subtitle: `「${document.getElementById('setupAssistantName').value || '자비스'}」 하고 부르면 이 자리에 이 카드가 나와요`
      });
    });

    document.getElementById('btnPreviewVoiceSetup').addEventListener('click', () => {
      const name = document.getElementById('setupAssistantName').value || '자비스';
      const user = document.getElementById('setupUserName').value || '대표님';
      speakText(`안녕하세요, ${user}. ${name}입니다. 이 목소리로 읽어 드릴게요.`);
    });

    document.getElementById('btnPreviewVoiceSettings').addEventListener('click', () => {
      const voice = document.getElementById('cfgSayVoice').value;
      appState.config.sayVoice = voice;
      speakText(`안녕하세요, ${appState.config.userName || '대표님'}. ${appState.config.assistantName || '자비스'}입니다. 이 목소리로 읽어 드릴게요.`);
    });

    document.getElementById('btnSaveSetup').addEventListener('click', async () => {
      const patch = {
        assistantName: document.getElementById('setupAssistantName').value.trim() || '자비스',
        userName: document.getElementById('setupUserName').value.trim() || '대표님',
        hudPosition: document.getElementById('setupHudPosition').value
      };
      await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(patch)
      });
      document.getElementById('setupModal').classList.remove('open');
      await refreshStatus();
      updateHud({ color: 'lime', title: `✓ ${patch.assistantName} 준비 완료`, subtitle: `${patch.userName}, 이제 언제든 불러 주세요.` });
    });

    document.getElementById('btnSaveSettings').addEventListener('click', async () => {
      const patch = {
        wakeWords: document
          .getElementById('cfgWakeWords')
          .value.split(',')
          .map(s => s.trim())
          .filter(Boolean),
        greetingText: document.getElementById('cfgGreetingText').value.trim(),
        bootStyle: document.getElementById('cfgBootStyle').value,
        previewBeforeSend: document.getElementById('cfgPreviewBeforeSend').checked,
        freeMode: document.getElementById('cfgFreeMode').checked,
        sayVoice: document.getElementById('cfgSayVoice').value,
        brainProvider: document.getElementById('cfgBrainProvider').value,
        growTogether: document.getElementById('cfgGrowTogether').value
      };
      await fetch('/api/config', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(patch)
      });
      const startupResponse = await fetch('/api/startup', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ enable: document.getElementById('cfgStartup').checked })
      });
      const startupResult = await startupResponse.json();
      if (!startupResult.ok) {
        updateHud({ color: 'amber', title: '⚠ 자동 시작 설정 실패', subtitle: startupResult.startup?.error || '설정을 확인해 주세요.' });
        return;
      }
      document.getElementById('settingsModal').classList.remove('open');
      await refreshStatus();
      updateHud({ color: 'lime', title: '✓ 설정이 저장되어 즉시 적용되었습니다', subtitle: 'config.json 업데이트 완료' });
    });

    document.getElementById('btnTutPrev').addEventListener('click', () => {
      appState.tutorialStep = (appState.tutorialStep - 1 + TUTORIAL_STEPS.length) % TUTORIAL_STEPS.length;
      renderTutorialStep();
    });
    document.getElementById('btnTutNext').addEventListener('click', () => {
      appState.tutorialStep = (appState.tutorialStep + 1) % TUTORIAL_STEPS.length;
      renderTutorialStep();
    });
    document.getElementById('btnTutTry').addEventListener('click', () => {
      sendCommand(TUTORIAL_STEPS[appState.tutorialStep].sampleCmd);
    });

    let lastTimerEvent = '';
    setInterval(async () => {
      try {
        const data = await (await fetch('/api/ear/latest')).json();
        const event = data.latest;
        if (!event || event.kind !== 'timer' || event.text === lastTimerEvent) return;
        lastTimerEvent = event.text;
        updateHud({ color: 'lime', title: event.hudTitle, subtitle: event.spoken });
        appendLog(event.spoken);
        await speakText(event.spoken);
      } catch (_) {}
    }, 500);

    // Auto-start continuous Korean voice wake on load
    setTimeout(() => {
      try {
        startBrowserSpeechRecognition(true);
      } catch (_) {}
    }, 600);
  });
})();
