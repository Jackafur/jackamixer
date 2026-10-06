# Jackamixer

A small volume mixer for Windows 10/11 with live level meters. One row per app: slider, mute, and a meter that shows what is actually making noise.

- Press **Win+\\** to open it, press it again (or Esc, or click away) to close.
- No tray icon. A hidden listener holds the hotkey (about 24 MB RAM, 0% CPU while idle); the mixer window only exists while it is open.
- Apps with several processes (browsers, Discord) get one row.
- Follows your Windows accent color and light/dark mode.
- Optional background picture.

## Install

1. Download or clone this folder to where you want it to live.
2. Right-click `install.ps1` > **Run with PowerShell**.

That builds `Jackamixer.exe` with the C# compiler that already ships with Windows (nothing to download), adds **Jackamixer** to the Start menu, and starts the hotkey listener now and at every login.

To remove it, run `uninstall.ps1`, then delete the folder.

## Using it

| Do this | To |
| --- | --- |
| Drag the slider, or scroll on a row | change volume (scroll = 2% per notch) |
| Click the app icon, or middle-click the row | mute / unmute |
| **Change hotkey** (bottom left) | press the keys you want; it tells you if Windows already uses them |
| **Background** (bottom right) | pick a picture, make it darker or lighter, or remove it |

The meter runs from -60 dB to 0 dB, turns yellow above -6 dB and red above -1 dB; the small tick holds the recent peak.

## Settings file

Everything is saved in `Jackamixer.ini` next to the exe. You normally never touch it, but you can:

```ini
hotkey=Win+\
background=C:\Pictures\something.png
background_dim=55
```

After editing it by hand, run `Jackamixer.exe --reload`.

## Command line

| Command | Does |
| --- | --- |
| `Jackamixer.exe` | open the mixer (starts the listener if it is not running) |
| `Jackamixer.exe --background` | start only the hotkey listener |
| `Jackamixer.exe --reload` | re-read the hotkey from the ini |
| `Jackamixer.exe --quit` | stop the listener |
| `Jackamixer.exe --snapshot out.png` | render the mixer to a picture (for screenshots) |

## Building

`build.cmd` compiles `Jackamixer.cs` (the whole app, one file) with `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. The icon is `assets\jackamixer.ico`.
