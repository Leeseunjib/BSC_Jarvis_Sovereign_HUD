$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [System.Text.Encoding]::UTF8
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -TypeDefinition @'
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class JarvisForeground {
  [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll", CharSet=CharSet.Unicode)] public static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int capacity);
  [DllImport("user32.dll")] public static extern IntPtr SendMessageTimeout(IntPtr h, uint m, UIntPtr w, IntPtr l, uint flags, uint timeout, out UIntPtr result);
  [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
  [DllImport("user32.dll", SetLastError=true)] private static extern uint SendInput(uint count, INPUT[] inputs, int size);
  [StructLayout(LayoutKind.Sequential)] private struct INPUT { public uint type; public INPUTUNION data; }
  [StructLayout(LayoutKind.Explicit)] private struct INPUTUNION {
    [FieldOffset(0)] public KEYBDINPUT keyboard;
    [FieldOffset(0)] public MOUSEINPUT mouse;
  }
  [StructLayout(LayoutKind.Sequential)] private struct KEYBDINPUT { public ushort vk; public ushort scan; public uint flags; public uint time; public UIntPtr extra; }
  [StructLayout(LayoutKind.Sequential)] private struct MOUSEINPUT { public int x; public int y; public uint data; public uint flags; public uint time; public UIntPtr extra; }
  private static INPUT Key(ushort vk, ushort scan, uint flags) {
    return new INPUT { type=1, data=new INPUTUNION { keyboard=new KEYBDINPUT { vk=vk, scan=scan, flags=flags } } };
  }
  public static void RequireReleasedModifiers() {
    foreach (int key in new int[] { 16,17,18,91,92 }) if ((GetAsyncKeyState(key) & 0x8000) != 0) throw new Exception("보조 키가 눌려 있어 입력을 중단했습니다.");
  }
  public static void TypeText(string text) {
    RequireReleasedModifiers();
    foreach (char c in text) {
      if (c == '\r' || c == '\n') throw new Exception("카카오톡 자동 전송은 한 줄 메시지만 지원합니다.");
      INPUT[] inputs = new INPUT[] { Key(0,c,4), Key(0,c,6) };
      if (SendInput(2,inputs,Marshal.SizeOf(typeof(INPUT))) != 2) throw new Exception("Windows 키보드 입력을 완료하지 못했습니다.");
    }
  }
  public static void PressEnter() {
    RequireReleasedModifiers();
    INPUT[] inputs = new INPUT[] { Key(13,0,0), Key(13,0,2) };
    if (SendInput(2,inputs,Marshal.SizeOf(typeof(INPUT))) != 2) throw new Exception("Windows 전송 키 입력에 실패했습니다.");
  }
}
'@

function Fail([string]$message) { throw $message }
function GetWindowTitle($hwnd) {
  $buffer = New-Object System.Text.StringBuilder 512
  [void][JarvisForeground]::GetWindowText([IntPtr]([int64]$hwnd), $buffer, $buffer.Capacity)
  return $buffer.ToString()
}
function ContextForElement($element, $root) {
  if ($null -eq $element -or $null -eq $root) { Fail '현재 입력창을 확인할 수 없습니다.' }
  if ($element.Current.IsPassword) { Fail '암호 입력창에서는 사용할 수 없습니다.' }
  $pidValue = $root.Current.ProcessId
  $proc = Get-Process -Id $pidValue -ErrorAction Stop
  return @{
    hwnd = [int64]$root.Current.NativeWindowHandle
    processId = $pidValue
    process = $proc.ProcessName
    executable = $proc.Path
    title = (GetWindowTitle $root.Current.NativeWindowHandle)
    runtimeId = @($element.GetRuntimeId())
  }
}
function GetSelectionText($element) {
  $pattern = $null
  if (-not $element.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$pattern)) { return '' }
  $ranges = $pattern.GetSelection()
  if ($null -eq $ranges -or $ranges.Count -ne 1) { return '' }
  return [string]$ranges[0].GetText(12000)
}
function GetEditableValue($element, $pattern, [string]$processName) {
  # KakaoTalk exposes its empty-input placeholder as ValuePattern.Value.
  # Check the native RichEdit length rather than mistaking the hint for a draft.
  if ($processName -ieq 'KakaoTalk' -and $element.Current.ClassName -eq 'RICHEDIT50W' -and $element.Current.NativeWindowHandle -ne 0) {
    $length = [UIntPtr]::Zero
    $result = [JarvisForeground]::SendMessageTimeout([IntPtr]$element.Current.NativeWindowHandle, 14, [UIntPtr]::Zero, [IntPtr]::Zero, 2, 500, [ref]$length)
    if ($result -eq [IntPtr]::Zero) { Fail '카카오톡 입력란의 실제 내용을 확인할 수 없습니다.' }
    if ($length.ToUInt64() -eq 0) { return '' }
  }
  return [string]$pattern.Current.Value
}
function AssertTargetFocus($target, $element) {
  $focused = [System.Windows.Automation.AutomationElement]::FocusedElement
  if ([JarvisForeground]::GetForegroundWindow().ToInt64() -ne [int64]$target.hwnd -or $null -eq $focused -or ($focused.GetRuntimeId() -join ',') -ne (@($target.runtimeId) -join ',')) { Fail '대화방 입력란의 포커스가 바뀌어 입력하거나 전송하지 않았습니다.' }
}
function ResolveTarget($target) {
  $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]([int64]$target.hwnd))
  if ($null -eq $root -or $root.Current.ProcessId -ne [int]$target.processId -or (GetWindowTitle $target.hwnd) -ne [string]$target.title) { Fail '대상 창이 바뀌어 작업을 취소했습니다.' }
  $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
  $wanted = (@($target.runtimeId) -join ',')
  foreach ($candidate in $all) {
    if (($candidate.GetRuntimeId() -join ',') -eq $wanted) {
      if ($candidate.Current.IsPassword -or -not $candidate.Current.IsEnabled) { Fail '입력창을 사용할 수 없습니다.' }
      return @{ root = $root; element = $candidate }
    }
  }
  Fail '원래 입력창을 다시 찾을 수 없습니다.'
}
try {
  $request = [Console]::In.ReadToEnd() | ConvertFrom-Json
  if ($request.op -eq 'inspect') {
    $root = [System.Windows.Automation.AutomationElement]::FromHandle([IntPtr]([int64]$request.hwnd))
    if ($null -eq $root) { Fail '대화창을 찾을 수 없습니다.' }
    $proc = Get-Process -Id $root.Current.ProcessId -ErrorAction Stop
    if ($proc.ProcessName -ine 'KakaoTalk') { Fail '카카오톡 창이 아닙니다.' }
    $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
    $types = @($all | Group-Object { $_.Current.ControlType.ProgrammaticName } | ForEach-Object { @{ type = $_.Name; count = $_.Count } })
    $patterns = @($all | ForEach-Object {
      $v = $null
      $hasValue = $_.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$v)
      @{ type = $_.Current.ControlType.ProgrammaticName; patterns = @($_.GetSupportedPatterns() | ForEach-Object { $_.ProgrammaticName }); focusable = $_.Current.IsKeyboardFocusable; editable = ($hasValue -and -not $v.Current.IsReadOnly); empty = (-not $hasValue -or [string]::IsNullOrWhiteSpace([string]$v.Current.Value)) }
    })
    @{ ok = $true; total = $all.Count; types = $types; patterns = $patterns } | ConvertTo-Json -Depth 5 -Compress
  } elseif ($request.op -eq 'capture') {
    $hwnd = if ($request.hwnd) { [IntPtr]([int64]$request.hwnd) } else { [JarvisForeground]::GetForegroundWindow() }
    if ($hwnd -eq [IntPtr]::Zero) { Fail '활성 창이 없습니다.' }
    $root = [System.Windows.Automation.AutomationElement]::FromHandle($hwnd)
    $element = [System.Windows.Automation.AutomationElement]::FocusedElement
    if ($request.hwnd) {
      $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
      $candidates = @($all | Where-Object {
        $p = $null
        ($_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Edit -or $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Document) -and $_.Current.IsEnabled -and -not $_.Current.IsPassword -and $_.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$p) -and -not $p.Current.IsReadOnly
      })
      if ($candidates.Count -ne 1) { Fail '대화창에서 하나의 편집 가능한 입력란을 확인할 수 없습니다.' }
      $element = $candidates[0]
    }
    $ctx = ContextForElement $element $root
    if ($element.Current.ProcessId -ne $ctx.processId) { Fail '활성 창의 입력 영역을 찾을 수 없습니다.' }
    $blocked = @('WindowsTerminal','cmd','powershell','pwsh','Code','Cursor','Antigravity','JarvisHud','JarvisHud.validation')
    if ($ctx.process -in $blocked) { Fail '이 창에서는 자동 입력이나 글 수정을 하지 않습니다.' }
    $ctx.selection = GetSelectionText $element
    $valuePattern = $null
    $ctx.editable = $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern) -and -not $valuePattern.Current.IsReadOnly
    $ctx.value = if ($ctx.editable) { GetEditableValue $element $valuePattern $ctx.process } else { '' }
    @{ ok = $true; target = $ctx } | ConvertTo-Json -Depth 8 -Compress
  } elseif ($request.op -eq 'replace') {
    $resolved = ResolveTarget $request.target
    $element = $resolved.element
    $selection = GetSelectionText $element
    if ([string]::IsNullOrWhiteSpace($selection) -or $selection -ne [string]$request.target.selection) { Fail '선택 영역이 바뀌어 글을 수정하지 않았습니다.' }
    $originalClipboard = [System.Windows.Forms.Clipboard]::GetDataObject()
    try {
      [System.Windows.Forms.Clipboard]::SetText([string]$request.text)
      $element.SetFocus()
      Start-Sleep -Milliseconds 200
      [System.Windows.Forms.SendKeys]::SendWait('^v')
      Start-Sleep -Milliseconds 250
    } finally {
      if ($null -ne $originalClipboard) { [System.Windows.Forms.Clipboard]::SetDataObject($originalClipboard, $true) }
      else { [System.Windows.Forms.Clipboard]::Clear() }
    }
    @{ ok = $true } | ConvertTo-Json -Compress
  } elseif ($request.op -eq 'append') {
    $resolved = ResolveTarget $request.target
    $element = $resolved.element
    $valuePattern = $null
    if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern) -or $valuePattern.Current.IsReadOnly) { Fail '이 입력창은 받아쓰기를 지원하지 않습니다.' }
    $existing = [string]$valuePattern.Current.Value
    if ($existing -ne [string]$request.target.value) { Fail '입력창이 변경되어 받아쓰기를 중지했습니다.' }
    $newValue = $existing + $(if ($existing.Length -gt 0 -and -not $existing.EndsWith(' ')) { ' ' } else { '' }) + [string]$request.text
    $valuePattern.SetValue($newValue)
    @{ ok = $true; value = $newValue } | ConvertTo-Json -Depth 4 -Compress
  } elseif ($request.op -eq 'kakao') {
    $resolved = ResolveTarget $request.target
    $root = $resolved.root
    $element = $resolved.element
    $proc = Get-Process -Id $root.Current.ProcessId -ErrorAction Stop
    if ($proc.ProcessName -ine 'KakaoTalk' -or $proc.Path -ine 'C:\Program Files (x86)\Kakao\KakaoTalk\KakaoTalk.exe') { Fail '설치된 카카오톡 대화창이 아니어서 전송하지 않았습니다.' }
    $recipient = ([string]$request.recipient -replace '\s','' -replace '님$','')
    $title = ((GetWindowTitle $request.target.hwnd) -replace '\s','' -replace '님$','')
    if ($recipient -ne '현재대화방' -and $title -ine $recipient) { Fail '카카오톡 대화방 이름과 수신자가 일치하지 않습니다.' }
    $valuePattern = $null
    if (-not $element.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern) -or $valuePattern.Current.IsReadOnly) { Fail '카카오톡 입력창을 확인할 수 없습니다.' }
    $actualValue = GetEditableValue $element $valuePattern $proc.ProcessName
    if ($actualValue -ne [string]$request.target.value) { Fail '입력창이 변경되어 중단했습니다.' }
    if (-not $request.dryRun -and -not [string]::IsNullOrWhiteSpace($actualValue)) { Fail '입력창에 기존 내용이 있어 중단했습니다.' }
    if ([string]$request.text -match '[\r\n]') { Fail '카카오톡 자동 입력은 한 줄 메시지만 지원합니다.' }
    $sendPattern = $null
    if ($request.send) {
      $all = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, [System.Windows.Automation.Condition]::TrueCondition)
      $buttons = @($all | Where-Object { $_.Current.ControlType -eq [System.Windows.Automation.ControlType]::Button -and $_.Current.Name -in @('전송','보내기','Send') })
      if ($buttons.Count -gt 1) { Fail '카카오톡 전송 버튼이 여러 개여서 중단했습니다.' }
      if ($buttons.Count -eq 1) { [void]$buttons[0].TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$sendPattern) }
    }
    if ($request.dryRun) {
      @{ ok = $true; checked = $true; sendMethod = $(if ($sendPattern) { 'button' } else { 'keyboard' }) } | ConvertTo-Json -Compress
      return
    }
    AssertTargetFocus $request.target $element
    [JarvisForeground]::TypeText([string]$request.text)
    Start-Sleep -Milliseconds 250
    if ((GetEditableValue $element $valuePattern $proc.ProcessName) -ne [string]$request.text) { Fail '입력한 메시지를 확인하지 못했습니다. 전송하지 않았습니다.' }
    if ($request.send) {
      AssertTargetFocus $request.target $element
      if ($sendPattern) { $sendPattern.Invoke() } else { [JarvisForeground]::PressEnter() }
      Start-Sleep -Milliseconds 500
      if (-not [string]::IsNullOrWhiteSpace((GetEditableValue $element $valuePattern $proc.ProcessName))) { Fail '입력란이 비워지지 않아 전송 여부를 확인하지 못했습니다. 자동 재전송하지 않습니다.' }
    }
    @{ ok = $true; sendRequested = [bool]$request.send; composerCleared = [bool]$request.send; deliveryVerified = $false } | ConvertTo-Json -Compress
  } else { Fail '지원하지 않는 작업입니다.' }
} catch {
  @{ ok = $false; error = $_.Exception.Message } | ConvertTo-Json -Compress
  exit 1
}
