'use strict';
const path = require('path');
const { spawn } = require('child_process');

function interact(request) {
  return new Promise((resolve, reject) => {
    const script = path.join(__dirname, 'windows-interaction.ps1');
    const escaped = script.replace(/'/g, "''");
    const bootstrap = `$source = [System.IO.File]::ReadAllText('${escaped}', [System.Text.Encoding]::UTF8); & ([scriptblock]::Create($source))`;
    const child = spawn('powershell.exe', ['-NoProfile', '-NonInteractive', '-STA', '-EncodedCommand', Buffer.from(bootstrap, 'utf16le').toString('base64')], { windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'] });
    let out = '', err = '';
    const timer = setTimeout(() => { child.kill(); reject(new Error('Windows 입력창 확인 시간이 초과되었습니다.')); }, 10000);
    child.stdout.on('data', d => { out = (out + d.toString('utf8')).slice(-65536); });
    child.stderr.on('data', d => { err = (err + d.toString('utf8')).slice(-4096); });
    child.on('error', e => { clearTimeout(timer); reject(e); });
    child.on('close', code => {
      clearTimeout(timer);
      try {
        const result = JSON.parse(out.trim());
        if (!result.ok) return reject(new Error(result.error || 'Windows 입력 작업에 실패했습니다.'));
        resolve(result);
      } catch (e) { reject(new Error(err.trim() || `Windows 입력 도구 오류 (종료 ${code}): ${e.message}`)); }
    });
    child.stdin.on('error', () => {});
    child.stdin.end(JSON.stringify(request), 'utf8');
  });
}
module.exports = { interact };
