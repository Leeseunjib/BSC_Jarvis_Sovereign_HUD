/**
 * @file C:\자비스\scripts\windows-ear-daemon.js
 * @description 1:1 Windows 11 Clean-Room Port of SasariSa's Always-On Wake Daemon:
 *   - Sources/MacButler/Ear.swift (Continuous wake word listener + AGC)
 *   - scripts/install.sh (LaunchAgent io.macbutler.app.plist -> Windows Startup Folder .vbs registration)
 */

const fs = require('fs');
const path = require('path');
const os = require('os');
const http = require('http');
const { spawn } = require('child_process');

const PORT = process.env.JARVIS_PORT || 7788;

function registerWindowsStartup(enable = true) {
  const startupDir = process.env.JARVIS_STARTUP_DIR || path.join(
    os.homedir(),
    'AppData',
    'Roaming',
    'Microsoft',
    'Windows',
    'Start Menu',
    'Programs',
    'Startup'
  );
  const vbsPath = path.join(startupDir, 'Jarvis_SasariSa_AutoStart.vbs');
  const launcherCmd = path.join(__dirname, '..', 'Start_자비스.cmd');

  if (!enable) {
    try {
      if (fs.existsSync(vbsPath)) fs.unlinkSync(vbsPath);
      return { enabled: false, path: vbsPath };
    } catch (err) { return { enabled: true, path: vbsPath, error: err.message }; }
  }

  const content = [
    'Set WshShell = CreateObject("WScript.Shell")',
    `WshShell.Run """${launcherCmd.replace(/"/g, '""')}""", 0, False`
  ].join('\r\n');

  try {
    fs.writeFileSync(vbsPath, '\uFEFF' + content, 'utf16le');
    return { enabled: true, path: vbsPath, launcherCmd };
  } catch (err) {
    return { enabled: false, error: err.message };
  }
}

function createDesktopShortcut() {
  try {
    const { execFileSync } = require('child_process');
    const shortcutPath = execFileSync('powershell.exe', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', path.join(__dirname, 'Create-DesktopShortcut.ps1')], { windowsHide: true, encoding: 'utf8' }).trim();
    return { created: true, shortcutPath };
  } catch (err) {
    return { created: false, error: err.message };
  }
}

function getStartupStatus() {
  const startupDir = process.env.JARVIS_STARTUP_DIR || path.join(
    os.homedir(),
    'AppData',
    'Roaming',
    'Microsoft',
    'Windows',
    'Start Menu',
    'Programs',
    'Startup'
  );
  const vbsPath = path.join(startupDir, 'Jarvis_SasariSa_AutoStart.vbs');
  return {
    registered: fs.existsSync(vbsPath),
    path: vbsPath
  };
}

/**
 * Starts Windows Native System.Speech.Recognition background wake listener
 */
function startNativeWakeListener() {
  const psScript = `
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
try {
  Add-Type -AssemblyName System.Speech
  $rec = New-Object System.Speech.Recognition.SpeechRecognitionEngine
  $choices = New-Object System.Speech.Recognition.Choices
  $choices.Add(@("자비스", "헤이 자비스", "사사리사", "헤이 사사리사", "아빠 왔어", "다녀왔어", "창 다 꺼", "되돌려"))
  $gb = New-Object System.Speech.Recognition.GrammarBuilder($choices)
  $g = New-Object System.Speech.Recognition.Grammar($gb)
  $rec.LoadGrammar($g)
  $rec.SetInputToDefaultAudioDevice()
  Write-Output "EAR_READY"
  while ($true) {
    $res = $rec.Recognize()
    if ($res -ne $null -and $res.Text -ne "") {
      Write-Output ("WAKE:" + $res.Text)
    }
  }
} catch {
  Write-Output ("EAR_WEB_FALLBACK:" + $_.Exception.Message)
}
`;
  const child = spawn('powershell.exe', ['-NoProfile', '-Command', psScript], {
    windowsHide: true
  });

  child.stdout.on('data', buf => {
    const line = buf.toString('utf8').trim();
    if (line.startsWith('WAKE:')) {
      const phrase = line.slice(5).trim();
      const body = JSON.stringify({ sentence: phrase, source: 'windows-ear-daemon' });
      const req = http.request({
        hostname: '127.0.0.1',
        port: PORT,
        path: '/api/butler/command',
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          'Content-Length': Buffer.byteLength(body)
        }
      });
      req.on('error', () => {});
      req.write(body);
      req.end();
    }
  });

  return child;
}

if (require.main === module) {
  console.log('🎙 [자비스 Windows Ear Daemon] Starting native wake-word listener...');
  startNativeWakeListener();
}

module.exports = {
  registerWindowsStartup,
  createDesktopShortcut,
  getStartupStatus,
  startNativeWakeListener
};
