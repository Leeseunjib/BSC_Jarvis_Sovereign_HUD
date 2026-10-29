(function (root, factory) {
  const api = factory();
  if (typeof module === 'object' && module.exports) module.exports = api;
  else root.JarvisMediaIntents = api;
})(typeof window === 'object' ? window : globalThis, function () {
  const normalize = text => String(text || '').toLowerCase().replace(/[^a-z0-9가-힣]/g, '');
  function classifyMediaControl(text, hasContext = false) {
    const phrase = normalize(text);
    const stop = phrase.match(/^(?:(유튜브)(?:에서)?)?(음악|노래|영상|동영상|미디어|유튜브|재생)(?:을|를)?(?:좀)?(?:꺼|끄자|멈춰|중지|정지|종료|그만틀어|그만재생해|닫아)(?:줘|주세요|해줘|해)?$/);
    if (stop) return { intent: 'stopMusic', scope: (stop[1] || stop[2] === '유튜브') ? 'browser' : 'any', explicit: true };
    if (hasContext && /^(꺼|꺼줘|꺼주세요|멈춰|멈춰줘|멈춰주세요|그만틀어|그만틀어줘|정지|중지|중지해줘|stop)$/.test(phrase)) return { intent: 'stopMusic', scope: 'browser', explicit: false };
    if (/^(?:유튜브|음악|노래|영상|미디어)?(?:다시틀어|다시재생해|다시켜|재생계속해|계속재생해)(?:줘|주세요)?$/.test(phrase) && (hasContext || /^(유튜브|음악|노래|영상|미디어)/.test(phrase))) return { intent: 'resumeMusic', scope: 'browser' };
    if (hasContext && /^(다음곡|다음노래|다음영상)(?:틀어|재생해)?(?:줘|주세요)?$/.test(phrase)) return { intent: 'mediaNext', scope: 'browser' };
    if (hasContext && /^(이전곡|이전노래|이전영상)(?:틀어|재생해)?(?:줘|주세요)?$/.test(phrase)) return { intent: 'mediaPrev', scope: 'browser' };
    return null;
  }
  return { classifyMediaControl };
});
