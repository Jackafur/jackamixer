@echo off
rem Builds Jackamixer.exe with the C# compiler that ships with Windows (.NET Framework 4.x). Nothing to install.
"%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe /platform:x64 /optimize+ /nowarn:1690 /win32icon:"%~dp0assets\jackamixer.ico" /out:"%~dp0Jackamixer.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "%~dp0Jackamixer.cs"
