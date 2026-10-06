@echo off
rem Builds Jackamixer.exe with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
rem The WinMetadata references are Windows' own "now playing" API, also already on every Windows 10/11 PC.
set FW=%WINDIR%\Microsoft.NET\Framework64\v4.0.30319
set WM=%WINDIR%\System32\WinMetadata
"%FW%\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /nowarn:1690 /win32icon:"%~dp0assets\jackamixer.ico" /out:"%~dp0Jackamixer.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:"%WM%\Windows.Media.winmd" /r:"%WM%\Windows.Foundation.winmd" /r:"%FW%\System.Runtime.dll" "%~dp0Jackamixer.cs"
