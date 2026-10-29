'use strict';
const path = require('path');
const { spawn } = require('child_process');
function runMediaScript(request, scriptName) {
  return new Promise((resolve, reject) => {
    const script = path.join(__dirname, scriptName).replace(/'/g, "''");
    const bootstrap = `$source = [IO.File]::ReadAllText('${script}', [Text.Encoding]::UTF8); & ([scriptblock]::Create($source))`;
    const child = spawn('powershell.exe', ['-NoProfile','-NonInteractive','-EncodedCommand',Buffer.from(bootstrap,'utf16le').toString('base64')], { windowsHide:true, stdio:['pipe','pipe','pipe'] });
    let out='',err='';
    const timer=setTimeout(()=>{child.kill();reject(new Error('Windows 미디어 제어 시간이 초과되었습니다.'));},22000);
    child.stdout.on('data',d=>{out=(out+d.toString('utf8')).slice(-32768);});
    child.stderr.on('data',d=>{err=(err+d.toString('utf8')).slice(-2048);});
    child.on('error',e=>{clearTimeout(timer);reject(e);});
    child.on('close',()=>{clearTimeout(timer);try{const result=JSON.parse(out.trim());if(!result.ok)throw new Error(result.error||'미디어 요청이 거부되었습니다.');resolve(result);}catch(e){reject(new Error(err.trim()||e.message));}});
    child.stdin.on('error',()=>{}); child.stdin.end(JSON.stringify(request),'utf8');
  });
}
function createMediaController(native, browser) {
  return async request => {
    let nativeError;
    try {
      const result = await native(request);
      if (request.action === 'status' || result.state !== 'none') return result;
    } catch (error) { nativeError = error; }
    if (request.action === 'status') throw nativeError;
    try {
      const result = await browser(request);
      if (nativeError && result.state === 'none') throw nativeError;
      return result;
    } catch (error) { throw new Error(`미디어 제어 실패: ${error.message}`); }
  };
}
const controlBrowserMedia = request => runMediaScript(request, 'windows-browser-media.ps1');
const controlMedia = createMediaController(request => runMediaScript(request, 'windows-media.ps1'), controlBrowserMedia);
function inspectBrowserMedia() { return runMediaScript({action:'status'}, 'windows-browser-media.ps1'); }
module.exports={controlMedia,inspectBrowserMedia,controlBrowserMedia,createMediaController};
