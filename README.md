# Jackamixer

A small volume mixer for Windows 10/11 with live level meters. One row per app: slider, mute, and a meter that shows what is actually making noise.

![Jackamixer](assets/screenshot.png)

- Press **Win+\\** to open it, press it again (or Esc, or click away) to close.
- No tray icon. A hidden listener holds the hotkey (about 24 MB RAM, 0% CPU while idle); the mixer window only exists while it is open.
- Apps with several processes (browsers, Discord) get one row.
- Follows your Windows accent color and light/dark mode.
- Optional background picture, with drag-to-position and zoom.

## Why I made it

The Windows 11 volume mixer gives every app a slider, but it doesn't show how loud each app actually is, so when something starts making noise you're stuck guessing which one it is. If there's an option for that somewhere, I couldn't find it. The old mixer (sndvol) does have meters, but it looks like it's from 2009 and it's buried. So I made my own: one hotkey, a live meter on every app, and nothing sitting in the tray.

## Install

1. Download or clone this folder to where you want it to live.
2. Right-click `install.ps1` > **Run with PowerShell**.

That builds `Jackamixer.exe` with the C# compiler that already ships with Windows (nothing to download), adds **Jackamixer** and **Jackamixer Options** to the Start menu, and starts the hotkey listener now and at every login.

To remove it, run `uninstall.ps1`, then delete the folder.

## Using it

| Do this | To |
| --- | --- |
| Drag the slider, or scroll on a row | change volume (scroll = 2% per notch) |
| Click the app icon, or middle-click the row | mute / unmute |
| Right-click an app > **Hide** | take it out of the mixer (bring it back in Options) |
| Right-click > **Options...**, or Start menu > **Jackamixer Options** | hotkey, background picture, darkness and crop, hidden apps, mixer behavior |

The meter runs from -60 dB to 0 dB, turns yellow above -6 dB and red above -1 dB; the small tick holds the recent peak.

In Options you can also choose whether the hotkey **opens** the mixer (clicking away closes it, the default) or **toggles** it (it stays open on top until you press the hotkey again), turn off the right-click menu, or turn on a small gear button on the mixer.

Why is iCUE (or Discord, OBS, Wallpaper Engine) bouncing when it isn't making sound? Apps with audio-reactive features listen to your speakers, Windows lists that as a session on your speakers, and its meter shows everything it hears. Hide it if it bugs you.

## Settings file

Everything is saved in `Jackamixer.ini` next to the exe. You normally never touch it, but you can (after editing it by hand, run `Jackamixer.exe --reload`).

```ini
hotkey=Win+\
background=C:\Pictures\something.png
background_dim=55
background_zoom=100
background_x=50
background_y=50
show_gear=0
right_click_options=1
hidden_apps=icue.exe
hotkey_mode=close
```

## Command line

| Command | Does |
| --- | --- |
| `Jackamixer.exe` | open the mixer (starts the listener if it is not running) |
| `Jackamixer.exe --options` | open the options window |
| `Jackamixer.exe --background` | start only the hotkey listener |
| `Jackamixer.exe --reload` | re-read the hotkey from the ini |
| `Jackamixer.exe --quit` | stop the listener |
| `Jackamixer.exe --snapshot out.png` | render the mixer to a picture (`--snapshot-options` for the options window) |

## Building

`build.cmd` compiles `Jackamixer.cs` (the whole app, one file) with `%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`. The icon is `assets\jackamixer.ico`.

## License

MIT
