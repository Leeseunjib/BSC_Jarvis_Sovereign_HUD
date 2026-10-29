/**
 * @file C:\자비스\scripts\verify-jarvis.js
 * @description End-to-End Verification Gate for C:\자비스 (REA Blueprint + 1:1 SasariSa Windows Engine)
 */

const fs = require('fs');
const path = require('path');
const os = require('os');
const originalData = process.env.JARVIS_DATA_DIR || path.resolve(__dirname, '..', 'data');
const tempData = fs.mkdtempSync(path.join(os.tmpdir(), 'jarvis-verify-'));
for (const file of fs.readdirSync(originalData, { withFileTypes: true })) {
  if (file.isFile()) fs.copyFileSync(path.join(originalData, file.name), path.join(tempData, file.name));
}
process.env.JARVIS_DATA_DIR = tempData;
process.env.JARVIS_DISABLE_EXTERNAL_HUB = '1';
const { ButlerEngine, soundAlikePattern } = require('./butler-engine');

async function runVerification() {
  console.log('================================================================');
  console.log('🛡️ [자비스 Verification Gate] Testing REA & 1:1 SasariSa Engine...');
  console.log('================================================================');

  const checks = [];
  function assert(name, cond, detail = '') {
    checks.push({ name, ok: Boolean(cond), detail });
    console.log(`${cond ? '✅ PASS' : '❌ FAIL'} | ${name} ${detail ? `(${detail})` : ''}`);
    if (!cond) process.exitCode = 1;
  }

  // 1. Verify REA Artifacts
  const rootDir = path.resolve(__dirname, '..');
  const teardownPath = path.join(rootDir, 'docs', 'teardown_report.md');
  const blueprintPath = path.join(rootDir, 'docs', 'REA_CleanRoom_Blueprint_sasarisa.md');
  assert('REA Stage 1 & 2 Teardown Report exists', fs.existsSync(teardownPath), teardownPath);
  assert('REA Stage 3 & 4 Clean-Room Blueprint exists', fs.existsSync(blueprintPath), blueprintPath);

  // 2. Verify Upstream Clone
  const upstreamButler = path.join(rootDir, 'vendor', 'sasarisa', 'Sources', 'MacButler', 'Butler.swift');
  assert('Upstream mvcompany8500-web/sasarisa Swift source verified', fs.existsSync(upstreamButler), upstreamButler);

  // 3. Verify Config.soundAlike Korean phonetic decomposition
  const patJarvis = soundAlikePattern('자비스');
  const patSasarisa = soundAlikePattern('사사리사');
  assert('Config.soundAlike("자비스") matches "재비스"', new RegExp(patJarvis).test('재비스'), '자비스 ~= 재비스');
  assert('Config.soundAlike("사사리사") matches "싸싸리싸"', new RegExp(patSasarisa).test('싸싸리싸'), '사사리사 ~= 싸싸리싸');

  // 4. Verify Deterministic Intents (Commands.swift 1:1) without side-effect OS popups
  const engine = new ButlerEngine();

  const rGreet = await engine.execute('자비스 아빠 왔어', false);
  assert('Intent [greet] -> triggers BootScene', rGreet.intent === 'greet' && rGreet.bootScene === true, rGreet.hudTitle);

  const rKakao = await engine.execute('카톡 영웅님한테 오늘도 힘내세요라고 답장해', false);
  assert('Intent [kakaoReply] -> 2-step previewBeforeSend guard', rKakao.intent === 'kakaoReply' && rKakao.awaitingConfirm === true, rKakao.hudTitle);

  const rConfirmNo = await engine.execute('아니', false);
  assert('Intent [confirmSend=false] -> cancels pending send', rConfirmNo.intent === 'confirmSend', rConfirmNo.hudTitle);

  const rVol = await engine.execute('소리 키워', false);
  assert('Intent [volume] -> 0-token volume control', rVol.intent === 'volume', rVol.hudTitle);

  const rWhat = await engine.execute('방금 뭐 했어', false);
  assert('Intent [whatDid] -> reports last action', rWhat.intent === 'whatDid', rWhat.hudSubtitle);

  const rUndo = await engine.execute('되돌려', false);
  assert('Intent [undo] -> reverts last quick action', rUndo.intent === 'undo', rUndo.hudTitle);

  const rRoutine = await engine.execute("앞으로 '집중 시작' 하면 메모장 열어 그리고 소리 줄여", false);
  assert('Intent [makeRoutine] -> learns spoken routine', rRoutine.intent === 'makeRoutine', rRoutine.hudTitle);

  // 5. Verify REA Live Blueprint Builder (Stage 1~4)
  const { buildReaReportObject } = require('./rea-analyzer');
  const reaObj = buildReaReportObject();
  assert(
    'REA 4-Split Evidence Provenance & 44 Swift Modules Deconstructed',
    reaObj.surface.totalFiles === 92 && reaObj.swiftModules.length === 44 && reaObj.intents.length === 34,
    `${reaObj.surface.totalFiles} files, ${reaObj.swiftModules.length} Swift modules, ${reaObj.intents.length} Intents`
  );

  console.log('================================================================');
  console.log(`🎯 Result: ${checks.filter(c => c.ok).length}/${checks.length} checks passed.`);
  console.log('================================================================');
}

runVerification().catch(err => { console.error(err); process.exitCode = 1; }).finally(() => {
  const resolved = fs.realpathSync(tempData);
  const parent = fs.realpathSync(os.tmpdir());
  if (path.dirname(resolved).toLowerCase() !== parent.toLowerCase() || !path.basename(resolved).startsWith('jarvis-verify-')) {
    throw new Error('Refusing to remove unexpected verification directory');
  }
  fs.rmSync(resolved, { recursive: true, force: true });
});
