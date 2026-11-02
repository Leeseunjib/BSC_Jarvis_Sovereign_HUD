Set WshShell = CreateObject("WScript.Shell")
Set Fso = CreateObject("Scripting.FileSystemObject")
rootDir = Fso.GetParentFolderName(WScript.ScriptFullName)
psExe = WshShell.ExpandEnvironmentStrings("%SystemRoot%") & "\System32\WindowsPowerShell\v1.0\powershell.exe"
launcher = rootDir & "\scripts\Open-JarvisHud.ps1"
WshShell.CurrentDirectory = rootDir
WshShell.Run """" & psExe & """ -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File """ & launcher & """", 0, False