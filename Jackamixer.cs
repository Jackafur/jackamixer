// Jackamixer: a small volume mixer flyout with live level meters.
// No tray icon. A hidden listener waits for the hotkey (Win+\ by default, set in Jackamixer.ini); the flyout
// only exists while it is open and closes when it loses focus or on Esc.
// Build with build.cmd (uses the C# compiler that ships with Windows).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Jackamixer
{
    // ---------------- Core Audio COM interop ----------------

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorCo { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IntPtr devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int clsCtx, IntPtr activationParams, [MarshalAs(UnmanagedType.IUnknown)] out object iface);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid fmtid; public int pid; }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    struct PropVariant { [FieldOffset(0)] public ushort vt; [FieldOffset(8)] public IntPtr ptr; }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PropertyKey key);
        [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
    }

    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float db, ref Guid ctx);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolumeLevel(out float db);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint ch, float db, ref Guid ctx);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint ch, float level, ref Guid ctx);
        [PreserveSig] int GetChannelVolumeLevel(uint ch, out float db);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint ch, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("C02216F6-8C67-4B5B-9D00-D008E73E0064"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioMeterInformation
    {
        [PreserveSig] int GetPeakValue(out float peak);
    }

    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr guid, int flags, out IntPtr ctl);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr guid, int flags, out IntPtr vol);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator e);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, [MarshalAs(UnmanagedType.IUnknown)] out object session);
    }

    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid ctx);
        [PreserveSig] int GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        [PreserveSig] int SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid ctx);
        [PreserveSig] int GetGroupingParam(out Guid g);
        [PreserveSig] int SetGroupingParam(ref Guid g, ref Guid ctx);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr n);
        [PreserveSig] int GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        [PreserveSig] int GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid ctx);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid ctx);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // ---------------- Win32 helpers ----------------

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetProcessDPIAware();
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h, int msg, IntPtr w, IntPtr l);
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] public static extern bool AllowSetForegroundWindow(int pid);
        [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h, int id, int mods, int vk);
        [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h, int id);
        [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr h);
        [DllImport("dwmapi.dll")] public static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int val, int size);
        [DllImport("kernel32.dll")] public static extern IntPtr OpenProcess(int access, bool inherit, uint pid);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] public static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder sb, ref int size);
        [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr h);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int SHDefExtractIcon(string file, int index, uint flags, out IntPtr large, out IntPtr small, uint size);
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] public static extern int ExtractIconEx(string file, int index, IntPtr[] large, IntPtr[] small, int n);
        [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)] public static extern int SHLoadIndirectString(string src, StringBuilder outBuf, int cch, IntPtr reserved);
        [DllImport("ole32.dll")] public static extern int PropVariantClear(ref PropVariant pv);

        public static string Indirect(string s)
        {
            var sb = new StringBuilder(512);
            return SHLoadIndirectString(Environment.ExpandEnvironmentVariables(s), sb, sb.Capacity, IntPtr.Zero) == 0 ? sb.ToString() : null;
        }

        public static string ProcessPath(uint pid)
        {
            if (pid == 0) return null;
            IntPtr h = OpenProcess(0x1000, false, pid); // PROCESS_QUERY_LIMITED_INFORMATION
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : null;
            }
            finally { CloseHandle(h); }
        }

        // Accepts "C:\app.exe", "@%SystemRoot%\x.dll,-203" and similar icon specs.
        public static Bitmap LoadIconBitmap(string spec, int px)
        {
            if (string.IsNullOrEmpty(spec)) return null;
            string file = Environment.ExpandEnvironmentVariables(spec.TrimStart('@').Trim('"'));
            int index = 0;
            int comma = file.LastIndexOf(',');
            if (comma > 0)
            {
                int idx;
                if (int.TryParse(file.Substring(comma + 1).Trim(), out idx)) { index = idx; file = file.Substring(0, comma); }
            }
            if (!File.Exists(file)) return null;
            IntPtr large, small;
            if (SHDefExtractIcon(file, index, 0, out large, out small, (uint)px) != 0 || large == IntPtr.Zero)
            {
                var arr = new IntPtr[1];
                if (ExtractIconEx(file, index, arr, null, 1) < 1 || arr[0] == IntPtr.Zero) return null;
                large = arr[0];
                small = IntPtr.Zero;
            }
            try { using (var ic = Icon.FromHandle(large)) return ic.ToBitmap(); }
            catch { return null; }
            finally
            {
                DestroyIcon(large);
                if (small != IntPtr.Zero) DestroyIcon(small);
            }
        }
    }

    static class Util
    {
        public static float Clamp01(float v) { return v < 0f ? 0f : (v > 1f ? 1f : v); }
    }

    // ---------------- Audio model ----------------

    abstract class Channel
    {
        public static Guid Ctx = Guid.NewGuid();
        public string Key = "";
        public string Name = "";
        public Bitmap Icon;
        public bool IsMaster;
        public abstract float Volume { get; set; }
        public abstract bool Muted { get; set; }
        public abstract float Peak { get; }
    }

    class MasterChannel : Channel
    {
        readonly IAudioEndpointVolume vol;
        readonly IAudioMeterInformation meter;

        public MasterChannel(IAudioEndpointVolume v, IAudioMeterInformation m, string name)
        {
            vol = v; meter = m; Name = name; Key = "#master"; IsMaster = true;
        }

        public override float Volume
        {
            get { float f; return vol.GetMasterVolumeLevelScalar(out f) >= 0 ? f : 0f; }
            set { vol.SetMasterVolumeLevelScalar(Util.Clamp01(value), ref Ctx); }
        }

        public override bool Muted
        {
            get { bool m; return vol.GetMute(out m) >= 0 && m; }
            set { vol.SetMute(value, ref Ctx); }
        }

        public override float Peak
        {
            get { float p; return meter.GetPeakValue(out p) >= 0 ? p : 0f; }
        }
    }

    class SessionRef
    {
        public string Id;
        public ISimpleAudioVolume Vol;
        public IAudioMeterInformation Meter;
    }

    // One row per app: every session from the same exe is grouped (Chrome, Discord, ...).
    class AppChannel : Channel
    {
        public bool IsSystem;
        public readonly List<SessionRef> Sessions = new List<SessionRef>();

        public override float Volume
        {
            get
            {
                foreach (var r in Sessions) { float f; if (r.Vol != null && r.Vol.GetMasterVolume(out f) >= 0) return f; }
                return 0f;
            }
            set
            {
                float v = Util.Clamp01(value);
                foreach (var r in Sessions) if (r.Vol != null) r.Vol.SetMasterVolume(v, ref Ctx);
            }
        }

        public override bool Muted
        {
            get
            {
                bool any = false;
                foreach (var r in Sessions)
                {
                    bool m;
                    if (r.Vol != null && r.Vol.GetMute(out m) >= 0) { if (!m) return false; any = true; }
                }
                return any;
            }
            set { foreach (var r in Sessions) if (r.Vol != null) r.Vol.SetMute(value, ref Ctx); }
        }

        public override float Peak
        {
            get
            {
                float best = 0f;
                foreach (var r in Sessions) { float p; if (r.Meter != null && r.Meter.GetPeakValue(out p) >= 0 && p > best) best = p; }
                return best;
            }
        }
    }

    class AudioEngine
    {
        readonly IMMDeviceEnumerator en = (IMMDeviceEnumerator)new MMDeviceEnumeratorCo();
        readonly Dictionary<string, string> nameCache = new Dictionary<string, string>();
        readonly Dictionary<string, Bitmap> iconCache = new Dictionary<string, Bitmap>();
        readonly int iconPx;
        IAudioSessionManager2 mgr;
        public string DeviceId;
        public MasterChannel Master;

        public AudioEngine(int iconPx) { this.iconPx = iconPx; }

        public string CurrentDefaultId()
        {
            IMMDevice d;
            if (en.GetDefaultAudioEndpoint(0, 0, out d) < 0 || d == null) return null;
            string id;
            d.GetId(out id);
            return id;
        }

        public bool Open()
        {
            Master = null; mgr = null; DeviceId = null;
            IMMDevice dev;
            if (en.GetDefaultAudioEndpoint(0, 0, out dev) < 0 || dev == null) return false;
            string id;
            dev.GetId(out id);
            DeviceId = id;
            var ev = Activate(dev, typeof(IAudioEndpointVolume).GUID) as IAudioEndpointVolume;
            var em = Activate(dev, typeof(IAudioMeterInformation).GUID) as IAudioMeterInformation;
            mgr = Activate(dev, typeof(IAudioSessionManager2).GUID) as IAudioSessionManager2;
            if (ev == null || em == null) return false;
            Master = new MasterChannel(ev, em, DeviceName(dev));
            return true;
        }

        static object Activate(IMMDevice d, Guid iid)
        {
            object o;
            return d.Activate(ref iid, 23, IntPtr.Zero, out o) >= 0 ? o : null; // CLSCTX_ALL
        }

        static string DeviceName(IMMDevice d)
        {
            IPropertyStore ps;
            if (d.OpenPropertyStore(0, out ps) < 0 || ps == null) return "Speakers";
            var key = new PropertyKey { fmtid = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), pid = 14 }; // PKEY_Device_FriendlyName
            PropVariant pv;
            if (ps.GetValue(ref key, out pv) < 0) return "Speakers";
            string s = pv.vt == 31 ? Marshal.PtrToStringUni(pv.ptr) : null; // VT_LPWSTR
            Native.PropVariantClear(ref pv);
            return string.IsNullOrEmpty(s) ? "Speakers" : s;
        }

        public List<AppChannel> ReadApps()
        {
            var list = new List<AppChannel>();
            if (mgr == null) return list;
            var map = new Dictionary<string, AppChannel>();
            IAudioSessionEnumerator e;
            if (mgr.GetSessionEnumerator(out e) < 0 || e == null) return list;
            int n;
            e.GetCount(out n);
            for (int i = 0; i < n; i++)
            {
                object o;
                if (e.GetSession(i, out o) < 0 || o == null) continue;
                var c = o as IAudioSessionControl2;
                if (c == null) continue;
                int state;
                c.GetState(out state);
                if (state == 2) continue; // expired
                bool sys = c.IsSystemSoundsSession() == 0;
                uint pid;
                c.GetProcessId(out pid);
                string path = sys ? null : Native.ProcessPath(pid);
                string key = sys ? "#system" : (path != null ? path.ToLowerInvariant() : "pid:" + pid);
                AppChannel ch;
                if (!map.TryGetValue(key, out ch))
                {
                    ch = new AppChannel { Key = key, IsSystem = sys };
                    ch.Name = ResolveName(c, key, path, sys);
                    ch.Icon = ResolveIcon(c, key, path);
                    map[key] = ch;
                    list.Add(ch);
                }
                string inst;
                c.GetSessionInstanceIdentifier(out inst);
                ch.Sessions.Add(new SessionRef { Id = inst ?? "", Vol = o as ISimpleAudioVolume, Meter = o as IAudioMeterInformation });
            }
            list.Sort((a, b) =>
            {
                if (a.IsSystem != b.IsSystem) return a.IsSystem ? -1 : 1;
                return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            });
            return list;
        }

        string ResolveName(IAudioSessionControl2 c, string key, string path, bool sys)
        {
            string n;
            if (nameCache.TryGetValue(key, out n)) return n;
            string dn;
            c.GetDisplayName(out dn);
            if (!string.IsNullOrEmpty(dn) && dn.StartsWith("@")) dn = Native.Indirect(dn);
            if (string.IsNullOrEmpty(dn) && sys) dn = "System sounds";
            if (string.IsNullOrEmpty(dn) && path != null) { try { dn = FileVersionInfo.GetVersionInfo(path).FileDescription; } catch { } }
            if (string.IsNullOrEmpty(dn) && path != null) dn = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrEmpty(dn)) dn = "Unknown app";
            dn = dn.Trim();
            nameCache[key] = dn;
            return dn;
        }

        Bitmap ResolveIcon(IAudioSessionControl2 c, string key, string path)
        {
            Bitmap b;
            if (iconCache.TryGetValue(key, out b)) return b;
            string ip;
            c.GetIconPath(out ip);
            b = Native.LoadIconBitmap(ip, iconPx);
            if (b == null && path != null) b = Native.LoadIconBitmap(path, iconPx);
            iconCache[key] = b;
            return b;
        }
    }

    // ---------------- Look ----------------

    static class Theme
    {
        public static bool Dark = true;
        public static Color Bg, RowHover, Text, SubText, Track, Thumb, ThumbEdge, Line, Accent, Meter, MeterHot, MeterClip, Hold;
        public static Font NameFont, GlyphFont, SmallGlyph;

        public static void Load()
        {
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    if (v is int) Dark = (int)v == 0;
                }
            }
            catch { }

            // Windows 11 default blue; replaced by the user's accent when available.
            Color accForDark = Color.FromArgb(0x60, 0xCD, 0xFF), accForLight = Color.FromArgb(0x00, 0x5F, 0xB8);
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent"))
                {
                    var pal = k == null ? null : k.GetValue("AccentPalette") as byte[];
                    if (pal != null && pal.Length >= 32)
                    {
                        accForDark = Color.FromArgb(pal[4], pal[5], pal[6]);     // Light2
                        accForLight = Color.FromArgb(pal[16], pal[17], pal[18]); // Dark1
                    }
                }
            }
            catch { }

            if (Dark)
            {
                Bg = Hex(0x242424); RowHover = Hex(0x2E2E2E); Text = Hex(0xFFFFFF); SubText = Hex(0xA0A0A0);
                Track = Hex(0x4A4A4A); Thumb = Hex(0x454545); ThumbEdge = Hex(0x555555); Line = Hex(0x3A3A3A);
                Accent = accForDark; Meter = Hex(0x6CCB5F); MeterHot = Hex(0xFCE100); MeterClip = Hex(0xFF6B6B); Hold = Hex(0xE0E0E0);
            }
            else
            {
                Bg = Hex(0xF3F3F3); RowHover = Hex(0xEAEAEA); Text = Hex(0x1A1A1A); SubText = Hex(0x5F5F5F);
                Track = Hex(0xC8C8C8); Thumb = Hex(0xFFFFFF); ThumbEdge = Hex(0xCFCFCF); Line = Hex(0xE0E0E0);
                Accent = accForLight; Meter = Hex(0x0F7B0F); MeterHot = Hex(0xC29C00); MeterClip = Hex(0xC42B1C); Hold = Hex(0x404040);
            }

            string ui = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            string ic = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            NameFont = new Font(ui, 9.75f);
            GlyphFont = new Font(ic, 13f);
            SmallGlyph = new Font(ic, 7f);
        }

        static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        static bool FontExists(string name)
        {
            using (var f = new Font(name, 10f)) return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
        }
    }


    // ---------------- Settings ----------------

    // Jackamixer.ini next to the exe. Created with defaults on first run.
    static class Config
    {
        public const string DefaultHotkey = @"Win+\";
        static readonly string[] Order = { "hotkey", "background", "background_dim" };

        static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Jackamixer.ini"); }
        }

        static string Default(string key)
        {
            if (key == "hotkey") return DefaultHotkey;
            if (key == "background_dim") return "55";
            return "";
        }

        static Dictionary<string, string> Load()
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string raw in File.ReadAllLines(FilePath))
                    {
                        string line = raw.Trim();
                        if (line.Length == 0 || line.StartsWith(";") || line.StartsWith("#")) continue;
                        int eq = line.IndexOf('=');
                        if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                }
            }
            catch { }
            return d;
        }

        static void Save(Dictionary<string, string> d)
        {
            var sb = new StringBuilder();
            sb.Append("; Jackamixer settings. Easiest way to change these: open Jackamixer and use the links at the bottom.\r\n");
            sb.Append("; If you edit this file by hand, run  Jackamixer.exe --reload  afterwards.\r\n");
            sb.Append(";\r\n");
            sb.Append("; hotkey          Win, Ctrl, Alt, Shift joined with + and then one key. Examples: Win+\\  Ctrl+Alt+M  F13\r\n");
            sb.Append("; background      full path to a picture (png, jpg, bmp, gif). Empty = plain.\r\n");
            sb.Append("; background_dim  0 to 90, how much the picture is darkened so the text stays readable.\r\n");
            foreach (string k in Order)
            {
                string v;
                if (!d.TryGetValue(k, out v)) v = Default(k);
                sb.Append(k).Append('=').Append(v).Append("\r\n");
            }
            try { File.WriteAllText(FilePath, sb.ToString()); } catch { }
        }

        public static string Get(string key)
        {
            string v;
            return Load().TryGetValue(key, out v) && v.Length > 0 ? v : Default(key);
        }

        public static int GetInt(string key, int min, int max)
        {
            int v;
            if (!int.TryParse(Get(key), out v)) int.TryParse(Default(key), out v);
            return Math.Max(min, Math.Min(max, v));
        }

        public static void Set(string key, string value)
        {
            var d = Load();
            d[key] = value;
            Save(d);
        }

        public static string ReadHotkey()
        {
            if (!File.Exists(FilePath)) Save(Load());
            return Get("hotkey");
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern short VkKeyScan(char c);
        [DllImport("user32.dll")] static extern uint MapVirtualKey(uint code, uint type);

        // "Win+Shift+V" -> MOD_WIN|MOD_SHIFT, 'V'. Single characters go through the keyboard layout.
        public static bool ParseHotkey(string text, out int mods, out int vk)
        {
            mods = 0; vk = 0;
            if (string.IsNullOrEmpty(text)) return false;
            string keyPart, modPart;
            if (text.EndsWith("+")) { keyPart = "+"; modPart = text.Substring(0, text.Length - 1); }
            else
            {
                int cut = text.LastIndexOf('+');
                keyPart = text.Substring(cut + 1).Trim();
                modPart = cut > 0 ? text.Substring(0, cut) : "";
            }
            foreach (string raw in modPart.Split('+'))
            {
                string m = raw.Trim().ToLowerInvariant();
                if (m == "") continue;
                if (m == "win" || m == "windows") mods |= 0x0008;
                else if (m == "ctrl" || m == "control") mods |= 0x0002;
                else if (m == "alt") mods |= 0x0001;
                else if (m == "shift") mods |= 0x0004;
                else return false;
            }
            if (keyPart.Length == 1)
            {
                short r = VkKeyScan(keyPart[0]);
                if (r == -1) return false;
                vk = r & 0xFF;
            }
            else
            {
                Keys k;
                if (!Enum.TryParse(keyPart, true, out k)) return false;
                vk = (int)(k & Keys.KeyCode);
            }
            return vk != 0;
        }

        // MOD_* flags + virtual key -> "Win+Shift+V". Inverse of ParseHotkey.
        public static string Describe(int mods, int vk)
        {
            var parts = new List<string>();
            if ((mods & 0x0008) != 0) parts.Add("Win");
            if ((mods & 0x0002) != 0) parts.Add("Ctrl");
            if ((mods & 0x0001) != 0) parts.Add("Alt");
            if ((mods & 0x0004) != 0) parts.Add("Shift");
            string key = null;
            if (vk < 0x60 || vk > 0x6F) // numpad keys keep their names
            {
                uint c = MapVirtualKey((uint)vk, 2) & 0xFFFF; // MAPVK_VK_TO_CHAR
                if (c > 32 && c != 0x7F) key = char.ToUpperInvariant((char)c).ToString();
            }
            parts.Add(key ?? ((Keys)vk).ToString());
            return string.Join("+", parts.ToArray());
        }
    }

    // ---------------- Background picture ----------------

    // Optional picture behind the whole flyout, darkened so text stays readable.
    // Every control paints its own slice of one form-sized frame.
    static class Backdrop
    {
        static Bitmap frame;
        static Bitmap source;
        static string sourcePath;

        public static bool Active { get { return frame != null; } }

        public static void Build(Size size)
        {
            Release();
            string path = Config.Get("background");
            if (string.IsNullOrEmpty(path) || size.Width <= 0 || size.Height <= 0) return;
            if (path != sourcePath)
            {
                if (source != null) source.Dispose();
                source = null;
                sourcePath = path;
                try
                {
                    // Keep a small copy only, so a 4K wallpaper doesn't sit in memory.
                    using (var img = Image.FromFile(path))
                    {
                        float k = Math.Min(1f, 1200f / Math.Max(img.Width, img.Height));
                        source = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)));
                        using (var g = Graphics.FromImage(source))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                            g.DrawImage(img, 0, 0, source.Width, source.Height);
                        }
                    }
                }
                catch { source = null; }
            }
            if (source == null) return;
            int dim = Config.GetInt("background_dim", 0, 90);
            frame = new Bitmap(size.Width, size.Height);
            using (var g = Graphics.FromImage(frame))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                float scale = Math.Max(size.Width / (float)source.Width, size.Height / (float)source.Height);
                float w = source.Width * scale, h = source.Height * scale;
                g.DrawImage(source, (size.Width - w) / 2f, (size.Height - h) / 2f, w, h);
                using (var b = new SolidBrush(Color.FromArgb((int)(dim * 2.55f), Theme.Bg))) g.FillRectangle(b, 0, 0, size.Width, size.Height);
            }
        }

        public static void Release()
        {
            if (frame != null) { frame.Dispose(); frame = null; }
        }

        // Fill a control with its slice of the picture, or the plain theme color.
        public static void Paint(Graphics g, Control c, bool hover)
        {
            if (frame == null) { g.Clear(hover ? Theme.RowHover : Theme.Bg); return; }
            Form f = c.FindForm();
            Point at = f == null ? Point.Empty : f.PointToClient(c.PointToScreen(Point.Empty));
            g.DrawImage(frame, new Rectangle(0, 0, c.Width, c.Height), new Rectangle(at.X, at.Y, c.Width, c.Height), GraphicsUnit.Pixel);
            if (hover)
            {
                using (var b = new SolidBrush(Theme.Dark ? Color.FromArgb(28, Color.White) : Color.FromArgb(22, Color.Black)))
                    g.FillRectangle(b, 0, 0, c.Width, c.Height);
            }
        }
    }

    // ---------------- Controls ----------------

    abstract class PaintedControl : Control
    {
        protected readonly float s;
        protected PaintedControl(float scale)
        {
            s = scale;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
        }
        protected int P(float v) { return (int)Math.Round(v * s); }
    }

    class ChannelRow : PaintedControl
    {
        public readonly Channel Ch;
        bool hover, dragging;
        float vol, shown, hold;
        bool muted;
        int holdTicks;

        public ChannelRow(Channel ch, float scale) : base(scale)
        {
            Ch = ch;
            SetStyle(ControlStyles.Selectable, true);
            vol = ch.Volume;
            muted = ch.Muted;
        }

        int X0 { get { return P(52); } }
        int X1 { get { return Width - P(18); } }
        Rectangle IconRect { get { return new Rectangle(P(14), (Height - P(26)) / 2, P(26), P(26)); } }

        // -60 dB .. 0 dB mapped onto the bar, so quiet audio still shows.
        static float MeterPos(float p)
        {
            if (p <= 0.001f) return 0f;
            return Util.Clamp01((20f * (float)Math.Log10(p) + 60f) / 60f);
        }

        public void Tick()
        {
            float v = Ch.Volume;
            bool m = Ch.Muted;
            float target = m ? 0f : MeterPos(Ch.Peak);
            float ns = target >= shown ? target : Math.Max(target, shown - 0.035f);
            float nh = hold;
            int nht = holdTicks;
            if (target >= nh) { nh = target; nht = 24; }
            else if (nht > 0) nht--;
            else nh = Math.Max(target, nh - 0.02f);
            bool changed = Math.Abs(v - vol) > 0.0005f || m != muted || Math.Abs(ns - shown) > 0.001f || Math.Abs(nh - hold) > 0.001f;
            vol = v; muted = m; shown = ns; hold = nh; holdTicks = nht;
            if (changed) Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Backdrop.Paint(g, this, hover);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;

            // icon
            Rectangle ir = IconRect;
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            if (Ch.IsMaster)
            {
                TextRenderer.DrawText(g, muted ? "" : "", Theme.GlyphFont, ir, muted ? Theme.SubText : Theme.Text, center);
            }
            else if (Ch.Icon != null)
            {
                if (muted)
                {
                    using (var ia = new ImageAttributes())
                    {
                        var cm = new ColorMatrix();
                        cm.Matrix33 = 0.35f;
                        ia.SetColorMatrix(cm);
                        g.DrawImage(Ch.Icon, ir, 0, 0, Ch.Icon.Width, Ch.Icon.Height, GraphicsUnit.Pixel, ia);
                    }
                }
                else g.DrawImage(Ch.Icon, ir);
            }
            else
            {
                using (var b = new SolidBrush(Theme.Track)) g.FillEllipse(b, ir);
                string initial = Ch.Name.Length > 0 ? Ch.Name.Substring(0, 1).ToUpperInvariant() : "?";
                TextRenderer.DrawText(g, initial, Theme.NameFont, ir, Theme.Text, center);
            }
            if (muted && !Ch.IsMaster)
            {
                var br = new Rectangle(ir.Right - P(11), ir.Bottom - P(11), P(14), P(14));
                using (var b = new SolidBrush(Theme.Bg)) g.FillEllipse(b, br);
                TextRenderer.DrawText(g, "", Theme.SmallGlyph, br, Theme.Text, center);
            }

            int x0 = X0, x1 = X1;

            // name + percent
            var left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, Ch.Name, Theme.NameFont, new Rectangle(x0 - P(1), P(8), x1 - x0 - P(56), P(20)), muted ? Theme.SubText : Theme.Text, left);
            string pct = muted ? "Muted" : ((int)Math.Round(vol * 100)).ToString();
            TextRenderer.DrawText(g, pct, Theme.NameFont, new Rectangle(x1 - P(56), P(8), P(58), P(20)), Theme.SubText, right);

            // volume slider
            int sy = P(34);
            int tx = x0 + (int)Math.Round((x1 - x0) * vol);
            using (var p = new Pen(Theme.Track, P(4)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, x0, sy, x1, sy);
            }
            if (tx > x0)
            {
                using (var p = new Pen(muted ? Theme.SubText : Theme.Accent, P(4)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLine(p, x0, sy, tx, sy);
                }
            }
            int ro = P(9), ri = (hover || dragging) ? P(6) : P(5);
            using (var b = new SolidBrush(Theme.Thumb)) g.FillEllipse(b, tx - ro, sy - ro, ro * 2, ro * 2);
            using (var p = new Pen(Theme.ThumbEdge, 1f)) g.DrawEllipse(p, tx - ro, sy - ro, ro * 2, ro * 2);
            using (var b = new SolidBrush(muted ? Theme.SubText : Theme.Accent)) g.FillEllipse(b, tx - ri, sy - ri, ri * 2, ri * 2);

            // level meter
            g.SmoothingMode = SmoothingMode.None;
            int my = P(47), mh = Math.Max(3, P(4)), mw = x1 - x0;
            using (var b = new SolidBrush(Theme.Track)) g.FillRectangle(b, x0, my, mw, mh);
            int fill = (int)(mw * shown);
            int hot = (int)(mw * 0.9f), clip = (int)(mw * 0.983f); // -6 dB, -1 dB
            if (fill > 0)
            {
                using (var b = new SolidBrush(Theme.Meter)) g.FillRectangle(b, x0, my, Math.Min(fill, hot), mh);
                if (fill > hot) using (var b = new SolidBrush(Theme.MeterHot)) g.FillRectangle(b, x0 + hot, my, Math.Min(fill, clip) - hot, mh);
                if (fill > clip) using (var b = new SolidBrush(Theme.MeterClip)) g.FillRectangle(b, x0 + clip, my, fill - clip, mh);
            }
            if (hold > 0.01f)
            {
                int hx = x0 + (int)(mw * hold);
                using (var b = new SolidBrush(Theme.Hold)) g.FillRectangle(b, Math.Min(hx, x1 - P(2)), my, Math.Max(2, P(2)), mh);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && IconRect.Contains(e.Location))) { ToggleMute(); return; }
            if (e.Button == MouseButtons.Left && e.X >= X0 - P(12) && e.Y >= P(24)) { dragging = true; SetFromX(e.X); }
        }

        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromX(e.X); }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Invalidate(); }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            float v = Util.Clamp01(vol + (e.Delta / 120f) * 0.02f);
            Ch.Volume = (float)Math.Round(v * 100f) / 100f;
            Tick();
        }

        void SetFromX(int x)
        {
            Ch.Volume = Util.Clamp01((x - X0) / (float)(X1 - X0));
            Tick();
        }

        void ToggleMute()
        {
            Ch.Muted = !muted;
            Tick();
        }
    }

    class Separator : PaintedControl
    {
        public Separator(float scale) : base(scale) { }
        protected override void OnPaint(PaintEventArgs e)
        {
            Backdrop.Paint(e.Graphics, this, false);
            using (var b = new SolidBrush(Theme.Line)) e.Graphics.FillRectangle(b, P(14), Height / 2, Width - P(28), 1);
        }
    }

    class Note : PaintedControl
    {
        public Note(float scale, string text) : base(scale) { Text = text; }
        protected override void OnPaint(PaintEventArgs e)
        {
            Backdrop.Paint(e.Graphics, this, false);
            TextRenderer.DrawText(e.Graphics, Text, Theme.NameFont, ClientRectangle, Theme.SubText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    // Scroll area that shows the picture behind its rows.
    class BackPanel : Panel
    {
        public BackPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
        protected override void OnPaintBackground(PaintEventArgs e) { Backdrop.Paint(e.Graphics, this, false); }
        protected override void OnScroll(ScrollEventArgs se) { base.OnScroll(se); Invalidate(true); }
        protected override void OnMouseWheel(MouseEventArgs e) { base.OnMouseWheel(e); Invalidate(true); }
    }

    // Bottom strip: "Change hotkey (Win+\)" on the left, "Background" on the right.
    class Footer : PaintedControl
    {
        readonly string hotkey;
        int hoverPart; // 0 none, 1 hotkey, 2 background
        public event EventHandler HotkeyClicked;
        public event EventHandler BackgroundClicked;

        public Footer(float scale, string hotkeyText) : base(scale)
        {
            hotkey = hotkeyText;
            Cursor = Cursors.Hand;
        }

        int PartAt(int x) { return x < Width / 2 ? 1 : 2; }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Backdrop.Paint(g, this, false);
            using (var b = new SolidBrush(Theme.Line)) g.FillRectangle(b, 0, 0, Width, 1);
            var r = new Rectangle(P(16), 1, Width - P(32), Height - 1);
            var flags = TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            TextRenderer.DrawText(g, "Change hotkey (" + hotkey + ")", Theme.NameFont, r, hoverPart == 1 ? Theme.Text : Theme.Accent, flags | TextFormatFlags.Left);
            TextRenderer.DrawText(g, "Background", Theme.NameFont, r, hoverPart == 2 ? Theme.Text : Theme.Accent, flags | TextFormatFlags.Right);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int p = PartAt(e.X);
            if (p != hoverPart) { hoverPart = p; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e) { hoverPart = 0; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (e.Button != MouseButtons.Left) return;
            EventHandler h = PartAt(e.X) == 1 ? HotkeyClicked : BackgroundClicked;
            if (h != null) h(this, EventArgs.Empty);
        }
    }

    // Dark menu for the Background link.
    class MenuRenderer : ToolStripProfessionalRenderer
    {
        protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Bg)) e.Graphics.FillRectangle(b, e.AffectedBounds);
        }
        protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
        {
            using (var p = new Pen(Theme.Line)) e.Graphics.DrawRectangle(p, 0, 0, e.AffectedBounds.Width - 1, e.AffectedBounds.Height - 1);
        }
        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            using (var b = new SolidBrush(e.Item.Selected && e.Item.Enabled ? Theme.RowHover : Theme.Bg))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.SubText;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Line)) e.Graphics.FillRectangle(b, 4, e.Item.Height / 2, e.Item.Width - 8, 1);
        }
    }

    // "Press the keys you want" window. Only accepts combos Windows lets us register.
    class HotkeyDialog : Form
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        readonly Host host;
        readonly Label status;
        readonly float s;

        public HotkeyDialog(Host host)
        {
            this.host = host;
            Text = "Jackamixer hotkey";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            TopMost = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.NameFont;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            ClientSize = new Size(P(380), P(200));

            Controls.Add(new Label
            {
                Text = "Press the keys you want to open Jackamixer with.\r\nCurrent: " + Config.ReadHotkey(),
                Bounds = new Rectangle(P(20), P(16), P(340), P(44)),
                ForeColor = Theme.Text
            });
            status = new Label
            {
                Text = "Waiting for keys... (if nothing happens, Windows already uses that combo)",
                Bounds = new Rectangle(P(20), P(66), P(340), P(64)),
                ForeColor = Theme.SubText
            };
            Controls.Add(status);
            var reset = MakeButton("Use Win+\\", new Rectangle(P(20), P(146), P(160), P(34)));
            reset.Click += (o, e) => Apply(Config.DefaultHotkey);
            var cancel = MakeButton("Cancel", new Rectangle(P(240), P(146), P(120), P(34)));
            cancel.Click += (o, e) => Close();
        }

        int P(float v) { return (int)Math.Round(v * s); }

        Button MakeButton(string text, Rectangle bounds)
        {
            var b = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, BackColor = Theme.RowHover, ForeColor = Theme.Text, TabStop = false };
            b.FlatAppearance.BorderColor = Theme.Line;
            Controls.Add(b);
            return b;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (keyData == Keys.Escape && !win) { Close(); return true; }
            int mods = 0;
            if (win) mods |= 0x0008;
            if ((keyData & Keys.Control) != 0) mods |= 0x0002;
            if ((keyData & Keys.Alt) != 0) mods |= 0x0001;
            if ((keyData & Keys.Shift) != 0) mods |= 0x0004;
            bool aloneOk = (key >= Keys.F13 && key <= Keys.F24) || key == Keys.Pause || key == Keys.Scroll;
            if ((mods & 0x000B) == 0 && !aloneOk)
            {
                // A bare letter or F-key would stop working everywhere else.
                status.ForeColor = Theme.MeterClip;
                status.Text = "Hold Win, Ctrl or Alt together with that key.";
                return true;
            }
            Apply(Config.Describe(mods, (int)key));
            return true;
        }

        void Apply(string text)
        {
            string err = host.TryHotkey(text);
            if (err != null)
            {
                status.ForeColor = Theme.MeterClip;
                status.Text = err;
                return;
            }
            status.ForeColor = Theme.Meter;
            status.Text = "Saved: " + text;
            var t = new System.Windows.Forms.Timer { Interval = 800 };
            t.Tick += (o, e) => { t.Stop(); t.Dispose(); Close(); };
            t.Start();
        }
    }

    // ---------------- Flyout ----------------

    class MixerForm : Form
    {
        readonly AudioEngine engine;
        readonly BackPanel list = new BackPanel();
        readonly Footer footer;
        readonly ContextMenuStrip bgMenu = new ContextMenuStrip();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly List<ChannelRow> rows = new List<ChannelRow>();
        readonly float s;
        readonly bool snapshot;
        readonly Host host;
        Rectangle workArea;
        string signature = "";
        int ticks;
        bool activatedOnce;

        public MixerForm(bool snapshotMode, Host host)
        {
            snapshot = snapshotMode;
            this.host = host;
            Text = "Jackamixer";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            KeyPreview = true;
            TopMost = true;
            BackColor = Theme.Bg;
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            engine = new AudioEngine(P(26));
            list.Dock = DockStyle.Fill;
            list.AutoScroll = true;
            list.BackColor = Theme.Bg;
            footer = new Footer(s, Config.ReadHotkey()) { Dock = DockStyle.Bottom, Height = P(38) };
            footer.HotkeyClicked += (o, e) =>
            {
                if (host != null) host.Request(Host.WM_CHANGE_HOTKEY);
                Close();
            };
            footer.BackgroundClicked += (o, e) => ShowBackgroundMenu();
            bgMenu.Renderer = new MenuRenderer();
            bgMenu.ShowImageMargin = false;
            bgMenu.Font = Theme.NameFont;
            Controls.Add(list);
            Controls.Add(footer);
            engine.Open();
            RefreshChannels(true);
            timer.Interval = 33;
            timer.Tick += (o, e) => Step();
        }

        int P(float v) { return (int)Math.Round(v * s); }

        void ShowBackgroundMenu()
        {
            bool has = Config.Get("background").Length > 0;
            bgMenu.Items.Clear();
            bgMenu.Items.Add("Choose a picture...", null, (o, e) =>
            {
                if (host != null) host.Request(Host.WM_CHOOSE_BACKGROUND);
                Close();
            });
            if (has)
            {
                bgMenu.Items.Add("Darker", null, (o, e) => SetDim(+10));
                bgMenu.Items.Add("Lighter", null, (o, e) => SetDim(-10));
                bgMenu.Items.Add(new ToolStripSeparator());
                bgMenu.Items.Add("Remove picture", null, (o, e) => { Config.Set("background", ""); RedrawBackdrop(); });
            }
            bgMenu.Show(footer, new Point(footer.Width - P(8), 0), ToolStripDropDownDirection.AboveLeft);
        }

        void SetDim(int delta)
        {
            Config.Set("background_dim", Math.Max(0, Math.Min(90, Config.GetInt("background_dim", 0, 90) + delta)).ToString());
            RedrawBackdrop();
        }

        void RedrawBackdrop()
        {
            Backdrop.Build(ClientSize);
            Invalidate(true);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int round = 2; // DWMWCP_ROUND
            Native.DwmSetWindowAttribute(Handle, 33, ref round, 4);
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Native.SetForegroundWindow(Handle);
            if (!snapshot) timer.Start();
        }

        protected override void OnActivated(EventArgs e) { base.OnActivated(e); activatedOnce = true; }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (activatedOnce && !snapshot) Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            bgMenu.Dispose();
            Backdrop.Release();
            base.OnFormClosed(e);
        }

        public void Step()
        {
            ticks++;
            if (ticks % 30 == 0) { try { RefreshChannels(false); } catch { } }
            foreach (var r in rows) { try { r.Tick(); } catch { } }
        }

        void RefreshChannels(bool force)
        {
            if (engine.CurrentDefaultId() != engine.DeviceId) { engine.Open(); force = true; }
            List<AppChannel> apps = engine.ReadApps();
            var sb = new StringBuilder(engine.DeviceId ?? "");
            foreach (var a in apps)
            {
                sb.Append('|').Append(a.Key);
                foreach (var r in a.Sessions) sb.Append(',').Append(r.Id);
            }
            string sig = sb.ToString();
            if (!force && sig == signature) return;
            signature = sig;
            Rebuild(apps);
        }

        void Rebuild(List<AppChannel> apps)
        {
            int rowH = P(58), sepH = P(9), pad = P(6), emptyH = P(44), w = P(380);
            bool hasMaster = engine.Master != null;
            int total = pad * 2 + (hasMaster ? rowH + sepH : 0) + (apps.Count > 0 ? apps.Count * rowH : emptyH) + footer.Height;
            if (workArea.IsEmpty) workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int maxH = (int)(workArea.Height * 0.85);
            int h = Math.Min(total, maxH);
            int rw = w - (total > maxH ? SystemInformation.VerticalScrollBarWidth : 0);

            list.SuspendLayout();
            while (list.Controls.Count > 0) list.Controls[0].Dispose();
            rows.Clear();
            list.AutoScrollPosition = Point.Empty;
            list.AutoScrollMargin = new Size(0, pad);
            int y = pad;
            if (hasMaster)
            {
                AddRow(engine.Master, y, rw, rowH);
                y += rowH;
                list.Controls.Add(new Separator(s) { Bounds = new Rectangle(0, y, rw, sepH) });
                y += sepH;
            }
            if (apps.Count == 0)
                list.Controls.Add(new Note(s, hasMaster ? "No apps are playing sound" : "No sound output device found") { Bounds = new Rectangle(0, y, rw, emptyH) });
            foreach (var a in apps) { AddRow(a, y, rw, rowH); y += rowH; }
            list.ResumeLayout();

            Bounds = new Rectangle(workArea.Right - w - P(12), workArea.Bottom - h - P(12), w, h);
            Backdrop.Build(new Size(w, h));
            Invalidate(true);
        }

        void AddRow(Channel ch, int y, int w, int h)
        {
            var row = new ChannelRow(ch, s) { Bounds = new Rectangle(0, y, w, h) };
            list.Controls.Add(row);
            rows.Add(row);
        }
    }

    // ---------------- Hotkey host ----------------

    // Hidden window that owns the hotkey. Idle cost is one thread blocked in GetMessage.
    class Host : NativeWindow
    {
        public const string Title = "Jackamixer.Host";
        public const int WM_TOGGLE = 0x8001; // WM_APP + 1
        public const int WM_QUIT_HOST = 0x8002;
        public const int WM_RELOAD = 0x8003;
        public const int WM_CHANGE_HOTKEY = 0x8004;
        public const int WM_CHOOSE_BACKGROUND = 0x8005;
        const int WM_HOTKEY = 0x0312;
        MixerForm form;

        public Host()
        {
            CreateHandle(new CreateParams { Caption = Title });
        }

        // Runs the action after the flyout has closed.
        public void Request(int msg)
        {
            Native.PostMessage(Handle, msg, IntPtr.Zero, IntPtr.Zero);
        }

        // Returns null when the hotkey is active, otherwise a message for the user.
        public string RegisterFromConfig()
        {
            Native.UnregisterHotKey(Handle, 1);
            string text = Config.ReadHotkey();
            int mods, vk;
            if (!Config.ParseHotkey(text, out mods, out vk))
                return "Could not read the hotkey \"" + text + "\" in Jackamixer.ini. Examples: Win+\\, Ctrl+Alt+M, F13.";
            if (!Native.RegisterHotKey(Handle, 1, mods | 0x4000, vk)) // MOD_NOREPEAT
                return text + " is already taken by Windows or another app. Open Jackamixer from the Start menu and click Change hotkey.";
            return null;
        }

        // Checks Windows will give us this combo, then saves it. Null on success.
        public string TryHotkey(string text)
        {
            int mods, vk;
            if (!Config.ParseHotkey(text, out mods, out vk)) return "Could not use " + text + ". Try another.";
            if (!Native.RegisterHotKey(Handle, 1, mods | 0x4000, vk)) return text + " is already used by Windows or another app. Try another.";
            Native.UnregisterHotKey(Handle, 1);
            Config.Set("hotkey", text);
            return null;
        }

        public void Toggle()
        {
            if (form != null && !form.IsDisposed) { form.Close(); return; }
            form = new MixerForm(false, this);
            form.FormClosed += (o, e) =>
            {
                form = null;
                GC.Collect(); // drop the audio session objects right away
            };
            form.Show();
        }

        void ChangeHotkey()
        {
            Native.UnregisterHotKey(Handle, 1); // so the current combo can be pressed in the dialog
            using (var dlg = new HotkeyDialog(this)) dlg.ShowDialog();
            string err = RegisterFromConfig();
            if (err != null) MessageBox.Show(err, "Jackamixer");
        }

        void ChooseBackground()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Pick a background picture for Jackamixer";
                dlg.Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
                if (dlg.ShowDialog() != DialogResult.OK) return;
                Config.Set("background", dlg.FileName);
            }
            Toggle(); // reopen so the new picture shows
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_HOTKEY:
                case WM_TOGGLE: Toggle(); return;
                case WM_CHANGE_HOTKEY: ChangeHotkey(); return;
                case WM_CHOOSE_BACKGROUND: ChooseBackground(); return;
                case WM_RELOAD:
                    string err = RegisterFromConfig();
                    MessageBox.Show(err ?? ("Hotkey set to " + Config.ReadHotkey() + "."), "Jackamixer");
                    return;
                case WM_QUIT_HOST:
                    Native.UnregisterHotKey(Handle, 1);
                    Application.ExitThread();
                    return;
            }
            base.WndProc(ref m);
        }
    }

    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            string first = args.Length > 0 ? args[0].ToLowerInvariant() : "";
            bool background = first == "--background" || first == "--reload";
            string snapshot = first == "--snapshot" && args.Length > 1 ? args[1] : null;

            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Load();

            if (snapshot != null) { Snapshot(snapshot); return; }

            bool created;
            using (var mutex = new Mutex(true, @"Local\Jackamixer", out created))
            {
                if (!created)
                {
                    // Already running: hand the request to it.
                    IntPtr h = Native.FindWindow(null, Host.Title);
                    if (h != IntPtr.Zero && first != "--background")
                    {
                        Native.AllowSetForegroundWindow(-1);
                        int msg = first == "--quit" ? Host.WM_QUIT_HOST : first == "--reload" ? Host.WM_RELOAD : Host.WM_TOGGLE;
                        Native.PostMessage(h, msg, IntPtr.Zero, IntPtr.Zero);
                    }
                    return;
                }
                if (first == "--quit") return;

                var host = new Host();
                string err = host.RegisterFromConfig();
                if (err != null) MessageBox.Show(err, "Jackamixer");
                else if (first == "--reload") MessageBox.Show("Hotkey set to " + Config.ReadHotkey() + ".", "Jackamixer");
                if (!background) host.Toggle();
                Application.Run();
                host.DestroyHandle();
            }
        }

        static void Snapshot(string file)
        {
            var form = new MixerForm(true, null);
            form.Show();
            for (int i = 0; i < 30; i++) { form.Step(); Application.DoEvents(); Thread.Sleep(33); }
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(file, ImageFormat.Png);
            }
            form.Close();
        }
    }
}
