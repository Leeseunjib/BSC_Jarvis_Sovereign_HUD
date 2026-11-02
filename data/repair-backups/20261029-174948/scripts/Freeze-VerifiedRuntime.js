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
const importLine="require('./scripts/butler-engine')";
if(!server.includes(importLine))throw Error('Server engine import not found.');
fs.writeFileSync(path.join(__dirname,'butler-engine.verified.js'),engine,'utf8');
fs.writeFileSync(path.join(root,'server.verified.js'),server.replace(importLine,"require('./scripts/butler-engine.verified')"),'utf8');
const files=['server.verified.js','scripts/butler-engine.verified.js','scripts/voice-session.js','scripts/media-control.js','scripts/windows-media.ps1','scripts/windows-browser-media.ps1','public/js/media-intents.js','public/js/speech-turn-buffer.js','public/js/jarvis-react-hud.js','scripts/Open-JarvisHud.ps1'];
const hashes=Object.fromEntries(files.map(file=>[file,crypto.createHash('sha256').update(fs.readFileSync(path.join(root,file))).digest('hex')]));
fs.writeFileSync(path.join(root,'data','verified-runtime.json'),JSON.stringify({createdAt:new Date().toISOString(),hashes},null,2));
console.log('Reviewed server and engine snapshots saved.');
