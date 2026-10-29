/**
 * ⚛️ 자비스 (SasariSa v0.7.3 Windows 11) — React 18 Speech Sensor & HUD Engine
 * Path: C:\자비스\public\js\jarvis-react-hud.js
 *
 * Implements `Sources/MacButler/Ear.swift` + `HUD.swift` 1:1:
 *  - Real-time WebAudio RMS + Automatic Gain Control (`Ear.swift` lines 177-237)
 *  - Streams `micLevel` (0.0 ~ 1.0) to `/api/ear/level` so the pure desktop HUD's `OrbView`
 *    pulses with the exact formula `s = 1 + min(v * 18, 0.6)` (`HUD.swift` line 52)
 *  - Continuous `webkitSpeechRecognition` (`ko-KR`) with `Ear.swift` 2s Watchdog auto-restart
 *  - Echo gate (`ear.muted`): pauses recognition while Studio HD voice is speaking
 */

(function () {
  const React = window.React;
  const ReactDOM = window.ReactDOM;
  const h = React.createElement;
  const { useState, useEffect, useRef, useCallback } = React;

  const IS_BACKGROUND_WORKER = window.location.search.includes('worker=1');
  const earHealth = { microphone: 'starting', recognition: 'starting', error: '', speechBuffer: 'endpoint-media-v4' };
  function reportEarHealth(patch) {
    Object.assign(earHealth, patch);
    fetch('/api/ear/health', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(earHealth) }).catch(() => {});
  }

  function useStudioHdVoice() {
    const audioRef = useRef(null);
    const [isSpeaking, setIsSpeaking] = useState(false);

    const speakHd = useCallback(async (text, voice = 'ko-KR-HyunsuMultilingualNeural') => {
      if (IS_BACKGROUND_WORKER) return; // Native Desktop HUD plays Studio HD audio directly
      const clean = String(text || '').trim();
      if (!clean) return;
      try {
        if (audioRef.current) {
          audioRef.current.pause();
          audioRef.current = null;
        }
        setIsSpeaking(true);
        const url = `/api/tts-mp3?voice=${encodeURIComponent(voice)}&text=${encodeURIComponent(clean)}&t=${Date.now()}`;
        const audio = new Audio(url);
        audioRef.current = audio;
        audio.onended = () => setIsSpeaking(false);
        audio.onerror = () => setIsSpeaking(false);
        await audio.play();
      } catch (_) {
        setIsSpeaking(false);
      }
    }, []);

    return { isSpeaking, speakHd };
  }

  function useMicrophoneMeter(serverSpeakingRef, audioActivityRef, mediaContextRef, awakeUntilRef, turnRef) {
    const [micLevel, setMicLevel] = useState(0);
    const [micActive, setMicActive] = useState(false);
    const [micDeviceLabel, setMicDeviceLabel] = useState('');
    const streamRef = useRef(null);
    const rafRef = useRef(null);
    const lastPushRef = useRef(0);
    const voiceFeaturesRef = useRef({ pitchHz: 0, energy: 0, zcr: 0, matchScore: 100, isOwnerVoice: true });
    const pitchHistoryRef = useRef([]);
    const energyHistoryRef = useRef([]);
    const zcrHistoryRef = useRef([]);

    const startMeter = useCallback(async () => {
      try {
        if (streamRef.current) return true;
        const stream = await navigator.mediaDevices.getUserMedia({
          audio: {
            echoCancellation: true,
            noiseSuppression: true,
            autoGainControl: true
          }
        });
        streamRef.current = stream;
        const track = stream.getAudioTracks()[0];
        if (track && track.label) setMicDeviceLabel(track.label);
        setMicActive(true);
        reportEarHealth({ microphone: 'ready', error: '' });

        const AudioCtx = window.AudioContext || window.webkitAudioContext;
        const ctx = new AudioCtx();
        const src = ctx.createMediaStreamSource(stream);
        const analyser = ctx.createAnalyser();
        analyser.fftSize = 256;
        src.connect(analyser);
        const data = new Float32Array(analyser.fftSize);
        const sampleRate = ctx.sampleRate || 48000;

        const estimateVoiceCharacteristics = (buf, rmsVal) => {
          let zeroCrossings = 0;
          for (let i = 1; i < buf.length; i++) {
            if ((buf[i - 1] >= 0 && buf[i] < 0) || (buf[i - 1] < 0 && buf[i] >= 0)) zeroCrossings++;
          }
          const zcr = zeroCrossings / buf.length;
          if (rmsVal < 0.008) return { pitchHz: 0, zcr };
          // Autocorrelation fundamental frequency (F0: 75Hz ~ 320Hz human vocal range)
          const minLag = Math.max(2, Math.floor(sampleRate / 320));
          const maxLag = Math.min(buf.length - 2, Math.floor(sampleRate / 75));
          let bestLag = 0;
          let bestCorr = 0;
          for (let lag = minLag; lag <= maxLag; lag++) {
            let corr = 0;
            for (let i = 0; i < buf.length - lag; i++) {
              corr += buf[i] * buf[i + lag];
            }
            if (corr > bestCorr) {
              bestCorr = corr;
              bestLag = lag;
            }
          }
          const pitchHz = bestLag > 0 ? Math.round(sampleRate / bestLag) : 0;
          return { pitchHz, zcr: Number(zcr.toFixed(3)) };
        };

        const tick = () => {
          analyser.getFloatTimeDomainData(data);
          let sum = 0;
          for (let i = 0; i < data.length; i++) {
            sum += data[i] * data[i];
          }
          // Ear.swift line 221: rms = sqrt(sum / n)
          const rms = Math.sqrt(sum / data.length);
          // HUD.swift line 52: min(v * 18, 0.6) normalized to 0..1
          const scaled = Math.min(1, rms * 18);
          setMicLevel(scaled);

          const { pitchHz, zcr } = estimateVoiceCharacteristics(data, rms);
          if (scaled >= 0.045 && pitchHz >= 75 && pitchHz <= 310) {
            pitchHistoryRef.current.push(pitchHz);
            if (pitchHistoryRef.current.length > 18) pitchHistoryRef.current.shift();
            energyHistoryRef.current.push(scaled);
            if (energyHistoryRef.current.length > 18) energyHistoryRef.current.shift();
            zcrHistoryRef.current.push(zcr);
            if (zcrHistoryRef.current.length > 18) zcrHistoryRef.current.shift();
          }

          const avgPitch = pitchHistoryRef.current.length
            ? Math.round(pitchHistoryRef.current.reduce((a, b) => a + b, 0) / pitchHistoryRef.current.length)
            : pitchHz;
          const avgEnergy = energyHistoryRef.current.length
            ? Number((energyHistoryRef.current.reduce((a, b) => a + b, 0) / energyHistoryRef.current.length).toFixed(3))
            : Number(scaled.toFixed(3));
          const avgZcr = zcrHistoryRef.current.length
            ? Number((zcrHistoryRef.current.reduce((a, b) => a + b, 0) / zcrHistoryRef.current.length).toFixed(3))
            : zcr;

          voiceFeaturesRef.current = {
            pitchHz: avgPitch,
            energy: avgEnergy,
            zcr: avgZcr,
            liveEnergy: Number(scaled.toFixed(3)),
            matchScore: voiceFeaturesRef.current.matchScore || 100,
            isOwnerVoice: voiceFeaturesRef.current.isOwnerVoice !== false
          };

          // Do not feed continuous background music / speaker bleed into SpeechTurnBuffer
          if (audioActivityRef.current) {
            audioActivityRef.current(0);
          }

          const now = Date.now();
          if (now - lastPushRef.current >= 120) {
            lastPushRef.current = now;
            fetch('/api/ear/level', {
              method: 'POST',
              headers: { 'Content-Type': 'application/json' },
              body: JSON.stringify({
                level: scaled,
                pitchHz: avgPitch,
                energy: avgEnergy,
                zcr: avgZcr
              })
            })
              .then((r) => r.json())
              .then((d) => {
                if (d && typeof d.mediaActive === 'boolean') mediaContextRef.current = d.mediaActive;
                if (d && typeof d.isSpeaking === 'boolean' && serverSpeakingRef) {
                  serverSpeakingRef.current = d.isSpeaking;
                }
                if (d && typeof d.matchScore === 'number') {
                  voiceFeaturesRef.current.matchScore = d.matchScore;
                  voiceFeaturesRef.current.isOwnerVoice = Boolean(d.isOwnerVoice);
                }
                if (d && typeof d.forceAwakeUntil === 'number' && d.forceAwakeUntil > Date.now() && awakeUntilRef) {
                  awakeUntilRef.current = Math.max(awakeUntilRef.current || 0, d.forceAwakeUntil);
                }
              })
              .catch(() => {});
          }
          rafRef.current = setTimeout(tick, 120);
        };
        rafRef.current = requestAnimationFrame(tick);
        return true;
      } catch (err) {
        setMicActive(false);
        reportEarHealth({ microphone: 'error', error: err.name || '마이크 연결 실패' });
        return false;
      }
    }, [serverSpeakingRef, awakeUntilRef, turnRef]);

    useEffect(() => {
      startMeter();
      return () => {
        if (rafRef.current) clearTimeout(rafRef.current);
      };
    }, [startMeter]);

    return { micLevel, micActive, micDeviceLabel, voiceFeaturesRef, startMeter };
  }

  function JarvisOrbView({ hudColor, micLevel, onClick }) {
    const borderHex =
      hudColor === 'lime' ? '#73ff99' :
      hudColor === 'amber' ? '#ffb840' : '#4dd9ff';
    const coreScale = (1 + Math.min((micLevel || 0) * 0.6, 0.6)).toFixed(2);

    return h(
      'div',
      { className: 'jarvis-orb-wrap', onClick },
      h('div', { className: 'jarvis-ring r1', style: { borderColor: borderHex } }),
      h('div', { className: 'jarvis-ring r2', style: { borderColor: borderHex } }),
      h('div', { className: 'jarvis-ring r3', style: { borderColor: borderHex } }),
      h('div', {
        className: 'jarvis-core',
        style: {
          transform: `scale(${coreScale})`,
          boxShadow: `0 0 ${16 + Math.round((micLevel || 0) * 20)}px ${borderHex}`
        }
      })
    );
  }

  function JarvisReactApp() {
    const [hudStatus, setHudStatus] = useState('○ 호출어 대기');
    const [hudText, setHudText] = useState('“자비스”라고 부르거나 단축키로 말씀하세요.');
    const [hudColor, setHudColor] = useState('cyan');
    const [sttState, setSttState] = useState('STARTING');
    const [sttDetail, setSttDetail] = useState('한국어 실시간 음성인식 연결 중...');
    const [manualInput, setManualInput] = useState('');
    const [config, setConfig] = useState({
      name: '자비스',
      sayVoice: 'ko-KR-HyunsuMultilingualNeural'
    });

    const serverSpeakingRef = useRef(false);
    const { isSpeaking, speakHd } = useStudioHdVoice();
    const audioActivityRef = useRef(null);
    const mediaContextRef = useRef(false);
    const awakeUntilRef = useRef(0);
    const turnRef = useRef(null);
    const isSpeakingRef = useRef(false);
    isSpeakingRef.current = isSpeaking;

    const recRef = useRef(null);
    const recRunningRef = useRef(false);
    const lastExecutedRef = useRef({ text: '', ts: 0 });
    const voiceSessionRef = useRef(`worker-${Date.now()}-${Math.random().toString(36).slice(2)}`);
    const executeRef = useRef(null);
    const { micLevel, micActive, micDeviceLabel, voiceFeaturesRef, startMeter } = useMicrophoneMeter(serverSpeakingRef, audioActivityRef, mediaContextRef, awakeUntilRef, turnRef);
    const configRef = useRef(config);
    configRef.current = config;
    const generationRef = useRef(0);
    const discardResultsRef = useRef(false);

    const executeCommand = useCallback(async (rawSentence, source = 'manual') => {
      const sentence = String(rawSentence || '').trim();
      if (!sentence) return;

      // Owner Voice Print & Proximity Gate: reject distant / outdoor / non-owner background speech
      if (source === 'voice' && voiceFeaturesRef && voiceFeaturesRef.current) {
        const vf = voiceFeaturesRef.current;
        if (vf.isOwnerVoice === false && vf.energy > 0 && vf.energy < 0.045) {
          return;
        }
      }

      const now = Date.now();
      if (lastExecutedRef.current.text === sentence && now - lastExecutedRef.current.ts < 2500) {
        return;
      }
      lastExecutedRef.current = { text: sentence, ts: now };

      if (source !== 'voice') {
        setHudColor('cyan');
        setHudStatus('○ 처리 중');
        setHudText(sentence);
      }

      try {
        const res = await fetch('/api/butler/command', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ sentence, executeOs: true, source, voiceSession: voiceSessionRef.current })
        });
        const data = await res.json();
        if (data.ignored) return;
        if (!data.ok) {
          setHudColor('amber');
          setHudStatus(data.error || '명령 실행 실패');
          setHudText(data.detail || '다시 확인해 주세요.');
          return;
        }
        if (data && data.ok && data.result) {
          const r = data.result;
          if (source === 'voice') {
            const followSec = r.intent === 'awake' ? 45 : Math.max(1, Math.min(60, Number(config.followUpSec) || 15));
            awakeUntilRef.current = Date.now() + followSec * 1000;
          }
          setHudColor(r.hudColor || 'lime');
          setHudStatus(r.hudTitle || '✓ 실행 완료');
          setHudText((r.awaitingConfirm || r.intent === 'draftMailReply') ? (r.hudSubtitle || r.spoken || sentence) : (r.spoken || r.hudSubtitle || sentence));
          if (r.spoken && !IS_BACKGROUND_WORKER) {
            await speakHd(r.spoken, config.sayVoice);
          }
        }
      } catch (err) {
        setHudColor('amber');
        setHudStatus('⚠ 서버 연결 확인');
        setHudText(err.message || '명령 처리 중 오류');
      }
    }, [speakHd, config.sayVoice, config.followUpSec]);

    executeRef.current = executeCommand;
    if (!turnRef.current) {
      turnRef.current = new window.SpeechTurnBuffer({
        pauseMs: 2000,
        onCommit: text => executeRef.current(text, 'voice'),
        onFinalize: () => { try { if (recRef.current && recRunningRef.current) recRef.current.stop(); } catch (_) {} },
        onReset: () => { try { if (recRef.current) recRef.current.abort(); } catch (_) {} },
        onFailure: text => {
          discardResultsRef.current = true;
          const normalize = s => String(s || '').toLowerCase().replace(/[^a-z0-9가-힣]/g, '');
          const cfg = configRef.current;
          const clean = String(text || '').trim();
          const control = window.JarvisMediaIntents.classifyMediaControl(clean, mediaContextRef.current);
          const hasWake = [...(cfg.wakeWords || []), cfg.assistantName || '자비스', '헤이자비스'].some(w => normalize(w) && normalize(clean).includes(normalize(w)));
          if (Date.now() >= awakeUntilRef.current && (!control || control.intent !== 'stopMusic') && !hasWake) return;
          setHudColor('amber'); setHudStatus('음성 확정 실패 · 다시 말씀하거나 입력해 주세요'); setHudText(clean);
          fetch('/api/ear/push', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text: clean, status: 'recognition-error' }) })
            .then(r => (r && typeof r.json === 'function') ? r.json() : null)
            .then(d => {
              if (d && d.rescued && d.result) {
                const r = d.result;
                awakeUntilRef.current = Date.now() + (r.intent === 'awake' ? 45000 : 25000);
                setHudColor(r.hudColor || 'cyan');
                setHudStatus(r.hudTitle || '✓ 실행 완료');
                setHudText(r.spoken || r.hudSubtitle || clean);
                if (r.spoken && !IS_BACKGROUND_WORKER) speakHd(r.spoken, configRef.current.sayVoice);
              }
            })
            .catch(() => {});
        },
        isMuted: () => isSpeakingRef.current || serverSpeakingRef.current,
        onPreview: text => {
          const normalize = s => String(s || '').toLowerCase().replace(/[^a-z0-9가-힣]/g, '');
          const cfg = configRef.current;
          const wakeWords = [...(cfg.wakeWords || []), cfg.assistantName || '자비스', '헤이자비스'];
          const control = window.JarvisMediaIntents.classifyMediaControl(text, mediaContextRef.current);
          const hasWake = wakeWords.some(w => normalize(w) && normalize(text).includes(normalize(w)));
          if (hasWake) {
            awakeUntilRef.current = Math.max(awakeUntilRef.current || 0, Date.now() + 45000);
          }
          if (Date.now() >= awakeUntilRef.current && (!control || control.intent !== 'stopMusic') && !hasWake) return;
          setHudColor('cyan'); setHudStatus('● 경청 중 · 끝까지 편하게 말씀하세요'); setHudText(text);
          fetch('/api/ear/push', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({ text, isFinal: false }) }).catch(() => {});
        }
      });
    }
    audioActivityRef.current = level => turnRef.current.audio(level);
    useEffect(() => () => turnRef.current.stop(), []);

    const startSpeechRecognition = useCallback(() => {
      startMeter();
      const SR = window.SpeechRecognition || window.webkitSpeechRecognition;
      if (!SR) {
        setSttState('ERROR');
        setSttDetail('Chrome 음성인식 엔진이 필요합니다.');
        return;
      }

      if (recRef.current) {
        const previous = recRef.current;
        recRef.current = null;
        turnRef.current.stop();
        try { previous.onend = null; previous.stop(); } catch (_) {}
      }

      const rec = new SR();
      recRef.current = rec;
      rec.lang = 'ko-KR';
      rec.continuous = true;
      rec.interimResults = true;
      rec.maxAlternatives = 1;
      let generation = 0;

      rec.onstart = () => {
        if (recRef.current !== rec) return;
        discardResultsRef.current = false;
        generation = ++generationRef.current;
        recRunningRef.current = true;
        setSttState('LISTENING');
        setSttDetail('🟢 실시간 한국어 음성 인식 작동 중');
        reportEarHealth({ recognition: 'listening' });
      };

      rec.onresult = (event) => {
        if (recRef.current !== rec || discardResultsRef.current || isSpeakingRef.current || serverSpeakingRef.current) return;
        reportEarHealth({ recognition: 'listening', error: '' });
        let interim = '';
        const finals = [];
        for (let i = event.resultIndex; i < event.results.length; i++) {
          const t = event.results[i][0].transcript.trim();
          if (event.results[i].isFinal) finals.push({ id: `${generation}:${i}`, text: t });
          else interim += `${t} `;
        }
        if (Date.now() < awakeUntilRef.current) {
          awakeUntilRef.current = Math.max(awakeUntilRef.current, Date.now() + 30000);
        }
        turnRef.current.push(finals, interim.trim());
      };

      rec.onerror = event => {
        if (recRef.current !== rec) return;
        recRunningRef.current = false;
        if (event.error === 'no-speech' || event.error === 'aborted') return;
        if (event.error === 'not-allowed' || event.error === 'service-not-allowed') recRef.current = null;
        setSttState('ERROR');
        setSttDetail(`음성 인식 오류: ${event.error || '알 수 없음'}`);
        reportEarHealth({ recognition: 'error', error: event.error || '음성 인식 실패' });
        setHudColor('amber');
        setHudStatus('⚠ 음성 인식 확인');
        setHudText('마이크 권한과 인터넷 연결을 확인하세요.');
      };

      rec.onend = () => {
        if (recRef.current !== rec) return;
        recRunningRef.current = false;
        turnRef.current.ended();
        reportEarHealth({ recognition: 'starting' });
        setTimeout(() => {
          if (recRef.current === rec) {
            try { rec.start(); } catch (_) {}
          }
        }, 180);
      };

      try {
        rec.start();
      } catch (_) {}
    }, [executeCommand, startMeter, config]);

    // Ear.swift Watchdog (lines 72-79): ensure recognizer stays alive 24/7
    useEffect(() => {
      startSpeechRecognition();
      const watchdog = setInterval(() => {
        if (!recRunningRef.current && recRef.current) {
          try { recRef.current.start(); } catch (_) {}
        }
      }, 2000);
      return () => clearInterval(watchdog);
    }, [startSpeechRecognition]);

    useEffect(() => {
      const heartbeat = setInterval(() => reportEarHealth({ turn: turnRef.current.state() }), 5000);
      return () => clearInterval(heartbeat);
    }, []);

    useEffect(() => {
      fetch('/api/butler/state')
        .then((r) => r.json())
        .then((d) => {
          if (d && d.ok && d.config) {
            setConfig(d.config);
            if (d.config.speechPauseMs && turnRef.current) {
              turnRef.current.pauseMs = Math.max(2400, Number(d.config.speechPauseMs) || 3800);
            }
          }
        })
        .catch(() => {});
    }, []);

    const pillBorderColor =
      hudColor === 'lime' ? '#73ff99' :
      hudColor === 'amber' ? '#ffb840' : '#4dd9ff';

    return h(
      'div',
      { className: 'jarvis-react-root' },
      h(
        'div',
        {
          className: `jarvis-hud-pill ${hudColor}`,
          style: {
            borderColor: pillBorderColor,
            boxShadow: `0 0 30px ${pillBorderColor}55, inset 0 0 18px ${pillBorderColor}22`
          }
        },
        h(JarvisOrbView, {
          hudColor,
          micLevel,
          onClick: () => startSpeechRecognition()
        }),
        h(
          'div',
          { className: 'jarvis-txt-col' },
          h(
            'div',
            { className: 'jarvis-st-line', style: { color: pillBorderColor } },
            h('span', null, hudStatus),
            h(
              'span',
              { className: 'jarvis-mic-meter-wrap', title: micDeviceLabel || '마이크 볼륨' },
              h('span', {
                className: 'jarvis-mic-meter-fill',
                style: {
                  width: `${Math.max(8, Math.min(100, Math.round(micLevel * 100)))}%`,
                  background: micActive ? '#73ff99' : '#ffb840'
                }
              })
            )
          ),
          h('div', { className: 'jarvis-bd-line' }, hudText)
        )
      )
    );
  }

  window.addEventListener('DOMContentLoaded', () => {
    const container = document.getElementById('jarvis-react-root');
    if (container && ReactDOM) {
      const mount = () => ReactDOM.createRoot(container).render(h(JarvisReactApp));
      if (!IS_BACKGROUND_WORKER || !navigator.locks) { mount(); return; }
      navigator.locks.request('jarvis-background-microphone', { ifAvailable: true }, async lock => {
        if (!lock) { window.close(); return; }
        mount();
        await new Promise(resolve => window.addEventListener('pagehide', resolve, { once: true }));
      }).catch(error => reportEarHealth({ microphone: 'error', error: error.message }));
    }
  });
})();
