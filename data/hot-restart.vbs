Set WshShell = CreateObject("WScript.Shell")
WshShell.Run "cmd.exe /c taskkill /F /IM node.exe /FI ""WINDOWTITLE eq *"" & timeout /t 1 /nobreak >nul", 0, False
