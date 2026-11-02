'use strict';
// Explicitly promote reviewed sources; never run this from normal app startup.
const fs=require('fs'),path=require('path'),crypto=require('crypto');
const root=path.resolve(__dirname,'..');
const server=fs.readFileSync(path.join(root,'server.js'),'utf8');
const engine=fs.readFileSync(path.join(__dirname,'butler-engine.js'),'utf8');
const launcher=fs.readFileSync(path.join(__dirname,'Open-JarvisHud.ps1'),'utf8');
if (/taskkill|CloseMainWindow|Stop-Process|JarvisHud_v\d/.test(server)) throw Error('Unsafe process or legacy HUD startup code found.');
if (/taskkill|CloseMainWindow|Stop-Process|launchOnVisibleDesktopRaw/.test(engine)) throw Error('Unverified process termination or raw launch code found in the engine.');
if (/Stop-Process|Copy-Item|JarvisHud_v\d|latestScratch/.test(launcher)) throw Error('The launcher must not terminate or replace HUD builds.');
const entrypoints=['Start_자비스.cmd','Start_자비스.vbs','자비스_실행.bat'];
for(const file of entrypoints){
  const text=fs.readFileSync(path.join(root,file),'utf8');
  if(!text.includes('Open-JarvisHud.ps1') || /taskkill|Stop-Process|RunOverlay|Add-Type|server\.js|JarvisHud\.next|copy\s+\/Y/i.test(text)) throw Error('Unsafe legacy startup path: '+file);
}
const startupCheck=fs.readFileSync(path.join(__dirname,'Verify-HudStartup.ps1'),'utf8');
if(/Stop-Process|Copy-Item|JarvisHud\.next/.test(startupCheck))throw Error('Startup verification must not terminate or replace the HUD.');
const importLine="require('./scripts/butler-engine')";
if(!server.includes(importLine))throw Error('Server engine import not found.');
fs.writeFileSync(path.join(__dirname,'butler-engine.verified.js'),engine,'utf8');
fs.writeFileSync(path.join(root,'server.verified.js'),server.replace(importLine,"require('./scripts/butler-engine.verified')"),'utf8');
const files=['server.js','server.verified.js','scripts/butler-engine.js','scripts/butler-engine.verified.js','scripts/voice-session.js','scripts/media-control.js','scripts/windows-media.ps1','scripts/windows-browser-media.ps1','public/js/media-intents.js','public/js/speech-turn-buffer.js','public/js/jarvis-react-hud.js','scripts/Open-JarvisHud.ps1','scripts/JarvisMonitorOverlay.cs','scripts/verified-hud/MonitorOverlayApp.cs','bin/JarvisHud.exe','bin/hud-build.json','scripts/Build-VerifiedHud.ps1','scripts/Install-VerifiedRuntime.ps1','scripts/jarvis-harness2-engine.js','scripts/bsc-collaboration-bridge.js'];
files.push('scripts/Watch-JarvisHud.ps1','scripts/Verify-HudStartup.ps1','scripts/Check-HudWindows.ps1','scripts/Measure-HudStability.ps1','scripts/JarvisLegacyLauncher.cs','scripts/Build-LegacyLauncher.ps1','JarvisHud_v20.exe',...entrypoints);
files.push('scripts/ear-health.js','scripts/Observe-SpeechHealth.ps1');
files.push('scripts/web-research.js','scripts/Get-AudioDevices.ps1');
files.push('scripts/tts-audio.js','package.json','package-lock.json');
files.push('scripts/voice-brain.js');
files.push('scripts/antigravity-brain.js','config/jarvis-voice-agent.md');
files.push('scripts/speaker-gate.js','scripts/speaker-worker.js','public/js/speaker-capture.js','public/js/speaker-capture-worklet.js','public/js/jarvis-app.js');
files.push('scripts/ecosystem-index.js','scripts/office-doc-engine.js','scripts/rea-laya-bridge.js','scripts/recursive-learning.js');
const hashes=Object.fromEntries(files.map(file=>[file,crypto.createHash('sha256').update(fs.readFileSync(path.join(root,file))).digest('hex')]));
fs.writeFileSync(path.join(root,'data','verified-runtime.json'),JSON.stringify({createdAt:new Date().toISOString(),hashes},null,2));
console.log('Reviewed server and engine snapshots saved.');
