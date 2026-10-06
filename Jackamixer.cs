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
using System.Globalization;
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
        [PreserveSig] int GetMeteringChannelCount(out int count);
        [PreserveSig] int GetChannelsPeakValues(int count, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 0)] float[] peaks);
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

        // Left/right peaks for the stereo meters; mono sources report the same value twice.
        public abstract void StereoPeak(out float left, out float right);

        protected static void ReadStereo(IAudioMeterInformation m, ref float left, ref float right)
        {
            int n;
            if (m == null || m.GetMeteringChannelCount(out n) < 0 || n < 1) return;
            var peaks = new float[n];
            if (m.GetChannelsPeakValues(n, peaks) < 0) return;
            float l = peaks[0], r = n > 1 ? peaks[1] : peaks[0];
            if (l > left) left = l;
            if (r > right) right = r;
        }
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

        public override void StereoPeak(out float left, out float right)
        {
            left = 0f; right = 0f;
            ReadStereo(meter, ref left, ref right);
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
        public string HideId; // "discord.exe" style id used by hidden_apps; null when it can't be hidden
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

        public override void StereoPeak(out float left, out float right)
        {
            left = 0f; right = 0f;
            foreach (var r in Sessions) ReadStereo(r.Meter, ref left, ref right);
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
                    ch.HideId = sys ? "system sounds" : path != null ? Path.GetFileName(path).ToLowerInvariant() : null;
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
        public static Color Bg, RowHover, Text, SubText, Track, Thumb, ThumbEdge, Line, Accent, WindowsAccent, Meter, MeterHot, MeterClip, Hold, Shadow;
        public static Font NameFont, TitleFont, GlyphFont, SmallGlyph, GearFont;

        public static void Load()
        {
            LoadColors();
            string ui = FontExists("Segoe UI Variable Text") ? "Segoe UI Variable Text" : "Segoe UI";
            string ic = FontExists("Segoe Fluent Icons") ? "Segoe Fluent Icons" : "Segoe MDL2 Assets";
            NameFont = new Font(ui, 9.75f);
            TitleFont = new Font(ui, 14f);
            GlyphFont = new Font(ic, 13f);
            SmallGlyph = new Font(ic, 7f);
            GearFont = new Font(ic, 10f);
        }

        // Re-read whenever a window opens, so Windows theme/accent changes and the Look tab show up.
        public static void LoadColors()
        {
            bool useWindows = Config.GetBool("use_windows_colors");
            if (useWindows)
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
            }
            else Dark = Config.Get("theme") != "light";

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
                Meter = Hex(0x6CCB5F); MeterHot = Hex(0xFCE100); MeterClip = Hex(0xFF6B6B); Hold = Hex(0xE0E0E0); Shadow = Hex(0x000000);
            }
            else
            {
                Bg = Hex(0xF3F3F3); RowHover = Hex(0xEAEAEA); Text = Hex(0x1A1A1A); SubText = Hex(0x5F5F5F);
                Track = Hex(0xC8C8C8); Thumb = Hex(0xFFFFFF); ThumbEdge = Hex(0xCFCFCF); Line = Hex(0xE0E0E0);
                Meter = Hex(0x0F7B0F); MeterHot = Hex(0xC29C00); MeterClip = Hex(0xC42B1C); Hold = Hex(0x404040); Shadow = Hex(0xFFFFFF);
            }
            WindowsAccent = Dark ? accForDark : accForLight;
            Color custom;
            Accent = !useWindows && TryParseColor(Config.Get("accent"), out custom) ? custom : WindowsAccent;
        }

        public static Color Hex(int rgb) { return Color.FromArgb((rgb >> 16) & 0xFF, (rgb >> 8) & 0xFF, rgb & 0xFF); }

        public static string ToHex(Color c) { return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }

        public static bool TryParseColor(string text, out Color c)
        {
            c = Color.Empty;
            if (string.IsNullOrEmpty(text)) return false;
            string t = text.Trim().TrimStart('#');
            int v;
            if (t.Length != 6 || !int.TryParse(t, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return false;
            c = Hex(v);
            return true;
        }

        static bool FontExists(string name)
        {
            using (var f = new Font(name, 10f)) return string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase);
        }
    }

    // Look and Effects options, re-read each time the mixer opens.
    class Style
    {
        public static Style Current = new Style();
        public bool TextShadow, Gradient, Animate, IconGlow, Stereo, Compact, Frosted;
        public float IconIdle = 0.55f; // resting opacity: icon of an app that isn't playing (glow on)
        public float IconLit = 1f;     // light-up opacity: icon of an app that is playing (glow on)
        public float TextIdle = 1f, TextLit = 1f; // same pair for the app's name and percent
        public float BarAlpha = 1f;    // opacity of the slider tracks and level meters
        public bool KnobFade = true;   // the slider knobs follow BarAlpha too (off = knobs stay solid)

        public static Style Load()
        {
            return new Style
            {
                TextShadow = Config.GetBool("text_shadow"),
                Gradient = Config.GetBool("gradient_meters"),
                Animate = Config.GetBool("animate"),
                IconGlow = Config.GetBool("icon_glow"),
                Stereo = Config.GetBool("stereo_meters"),
                Compact = Config.GetBool("compact"),
                Frosted = Config.GetBool("frosted"),
                IconIdle = Config.GetInt("icon_idle", 10, 100) / 100f,
                IconLit = Config.GetInt("icon_lit", 10, 100) / 100f,
                TextIdle = Config.GetInt("text_idle", 10, 100) / 100f,
                TextLit = Config.GetInt("text_lit", 10, 100) / 100f,
                BarAlpha = Config.GetInt("bar_opacity", 10, 100) / 100f,
                KnobFade = Config.GetBool("knob_fade")
            };
        }
    }

    // ---------------- Settings ----------------

    // Jackamixer.ini next to the exe. Created with defaults on first run.
    static class Config
    {
        public const string DefaultHotkey = @"Win+\";
        static readonly string[] Order = { "hotkey", "background", "background_dim", "background_zoom", "background_x", "background_y", "show_gear", "right_click_options", "hidden_apps", "hotkey_mode",
            "use_windows_colors", "accent", "theme", "text_shadow", "gradient_meters", "animate", "icon_glow", "stereo_meters", "compact", "frosted", "icon_idle", "icon_lit", "text_idle", "text_lit", "bar_opacity", "knob_fade" };

        static string FilePath
        {
            get { return Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "Jackamixer.ini"); }
        }

        static string Default(string key)
        {
            switch (key)
            {
                case "hotkey": return DefaultHotkey;
                case "background_dim": return "55";
                case "background_zoom": return "100";
                case "background_x": return "50";
                case "background_y": return "50";
                case "show_gear": return "0";
                case "right_click_options": return "1";
                case "hotkey_mode": return "close";
                case "use_windows_colors": return "1";
                case "accent": return "#FF8AD8";
                case "theme": return "dark";
                case "text_shadow": return "1";
                case "gradient_meters": return "1";
                case "animate": return "1";
                case "icon_glow": return "1";
                case "stereo_meters": return "0";
                case "compact": return "0";
                case "frosted": return "0";
                case "icon_idle": return "55";
                case "icon_lit": return "100";
                case "text_idle": return "100";
                case "text_lit": return "100";
                case "knob_fade": return "1";
                case "bar_opacity": return "100";
            }
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
            sb.Append("; Jackamixer settings. Easiest way to change these: Start menu > Jackamixer Options.\r\n");
            sb.Append("; If you edit this file by hand, run  Jackamixer.exe --reload  afterwards.\r\n");
            sb.Append(";\r\n");
            sb.Append("; hotkey           Win, Ctrl, Alt, Shift joined with + and then one key. Examples: Win+\\  Ctrl+Alt+M  F13\r\n");
            sb.Append("; background       full path to a picture (png, jpg, bmp, gif). Empty = plain.\r\n");
            sb.Append("; background_dim   0 to 90, how much the picture is darkened so the text stays readable.\r\n");
            sb.Append("; background_zoom  100 to 500 (percent).\r\n");
            sb.Append("; background_x/y   0 to 100, which part of the picture sits in the middle of the mixer.\r\n");
            sb.Append("; show_gear            1 = gear button on the mixer that opens these options.\r\n");
            sb.Append("; right_click_options  1 = right-clicking the mixer opens these options.\r\n");
            sb.Append("; hidden_apps          apps left out of the mixer, by exe name, separated by ;  e.g. icue.exe;steamwebhelper.exe\r\n");
            sb.Append("; hotkey_mode          close = hotkey opens the mixer, clicking away closes it. toggle = hotkey opens and closes it.\r\n");
            sb.Append("; use_windows_colors   1 = follow the Windows accent and light/dark mode. 0 = use accent and theme below.\r\n");
            sb.Append("; accent / theme       your own accent color (#RRGGBB) and dark or light.\r\n");
            sb.Append("; text_shadow, gradient_meters, animate, icon_glow, stereo_meters, compact, frosted   1 = on, 0 = off.\r\n");
            sb.Append("; icon_idle            10 to 100, resting opacity: icons of apps that aren't playing (icon_glow on).\r\n");
            sb.Append("; icon_lit             10 to 100, light-up opacity: icons of apps that are playing (icon_glow on).\r\n");
            sb.Append("; text_idle / text_lit the same two, for the app names and percentages.\r\n");
            sb.Append("; bar_opacity          10 to 100, opacity of the slider tracks and level meters.\r\n");
            sb.Append("; knob_fade            1 = the slider knobs follow bar_opacity too, 0 = knobs stay solid.\r\n");
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

        public static void SetMany(params string[] pairs)
        {
            var d = Load();
            for (int i = 0; i + 1 < pairs.Length; i += 2) d[pairs[i]] = pairs[i + 1];
            Save(d);
        }

        public static bool GetBool(string key) { return Get(key) == "1"; }

        public static HashSet<string> HiddenApps()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in Get("hidden_apps").Split(';'))
            {
                string t = part.Trim();
                if (t.Length > 0) set.Add(t);
            }
            return set;
        }

        public static void SetHidden(string id, bool hide)
        {
            var set = HiddenApps();
            if (hide) set.Add(id); else set.Remove(id);
            var list = new List<string>(set);
            list.Sort(StringComparer.OrdinalIgnoreCase);
            Set("hidden_apps", string.Join(";", list.ToArray()));
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

    // Where the picture sits: zoom (1 = just covers the mixer) and which point of the
    // picture (0..1 fractions) lands in the middle of the mixer.
    struct Crop
    {
        public float Zoom, X, Y;
        public int Dim;

        public static Crop FromConfig()
        {
            return new Crop
            {
                Zoom = Config.GetInt("background_zoom", 100, 500) / 100f,
                X = Config.GetInt("background_x", 0, 100) / 100f,
                Y = Config.GetInt("background_y", 0, 100) / 100f,
                Dim = Config.GetInt("background_dim", 0, 90)
            };
        }

        public void Save()
        {
            Config.SetMany(
                "background_zoom", ((int)Math.Round(Zoom * 100)).ToString(),
                "background_x", ((int)Math.Round(X * 100)).ToString(),
                "background_y", ((int)Math.Round(Y * 100)).ToString(),
                "background_dim", Dim.ToString());
        }
    }

    // Optional picture behind the whole flyout, darkened so text stays readable.
    // Every control paints its own slice of one form-sized frame.
    static class Backdrop
    {
        static Bitmap frame;
        static Bitmap source;
        static string sourcePath;

        // Small cached copy of the picture, so a 4K wallpaper doesn't sit in memory.
        public static Bitmap Source(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (path == sourcePath) return source;
            if (source != null) source.Dispose();
            source = null;
            sourcePath = path;
            try
            {
                using (var img = Image.FromFile(path))
                {
                    float k = Math.Min(1f, 1600f / Math.Max(img.Width, img.Height));
                    source = new Bitmap(Math.Max(1, (int)(img.Width * k)), Math.Max(1, (int)(img.Height * k)));
                    using (var g = Graphics.FromImage(source))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.DrawImage(img, 0, 0, source.Width, source.Height);
                    }
                }
            }
            catch { source = null; }
            return source;
        }

        // Rectangle the picture is drawn into for a target of this size.
        public static RectangleF Place(Size size, Bitmap src, Crop c)
        {
            float k = Math.Max(size.Width / (float)src.Width, size.Height / (float)src.Height) * Math.Max(1f, c.Zoom);
            float w = src.Width * k, h = src.Height * k;
            float x = size.Width / 2f - c.X * w, y = size.Height / 2f - c.Y * h;
            x = Math.Min(0f, Math.Max(size.Width - w, x));
            y = Math.Min(0f, Math.Max(size.Height - h, y));
            return new RectangleF(x, y, w, h);
        }

        // Keeps X/Y inside the range where the picture still covers the target.
        public static void ClampCrop(Size size, Bitmap src, ref Crop c)
        {
            float k = Math.Max(size.Width / (float)src.Width, size.Height / (float)src.Height) * Math.Max(1f, c.Zoom);
            float w = src.Width * k, h = src.Height * k;
            float mx = size.Width / 2f / w, my = size.Height / 2f / h;
            c.X = Math.Max(mx, Math.Min(1f - mx, c.X));
            c.Y = Math.Max(my, Math.Min(1f - my, c.Y));
        }

        public static void Compose(Graphics g, Size size, Bitmap src, Crop c)
        {
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(src, Place(size, src, c));
            using (var b = new SolidBrush(Color.FromArgb((int)(c.Dim * 2.55f), Theme.Bg))) g.FillRectangle(b, 0, 0, size.Width, size.Height);
        }

        public static bool Active { get { return frame != null; } }

        // bounds = where the mixer sits on screen (frosted glass needs the position, a picture only the size).
        public static void Build(Rectangle bounds)
        {
            Release();
            Size size = bounds.Size;
            if (size.Width <= 0 || size.Height <= 0) return;
            Bitmap src = Source(Config.Get("background"));
            if (src != null)
            {
                frame = new Bitmap(size.Width, size.Height);
                using (var g = Graphics.FromImage(frame)) Compose(g, size, src, Crop.FromConfig());
                return;
            }
            if (Style.Current.Frosted) frame = Frosted(bounds);
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool SystemParametersInfo(int action, int param, StringBuilder buf, int winIni);

        static Bitmap wallSmall;
        static string wallKey;

        // Windows 11 "Mica" style: the part of the wallpaper behind the mixer, blurred and tinted.
        static Bitmap Frosted(Rectangle bounds)
        {
            try
            {
                var sb = new StringBuilder(520);
                if (!SystemParametersInfo(0x73, sb.Capacity, sb, 0)) return null; // SPI_GETDESKWALLPAPER
                string path = sb.ToString();
                if (path.Length == 0 || !File.Exists(path)) return null;
                Rectangle scr = Screen.FromRectangle(bounds).Bounds;

                // Small copy of the wallpaper as it fills this monitor, cached until the wallpaper or monitor changes.
                const int Shrink = 8;
                string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks + "|" + scr;
                if (key != wallKey || wallSmall == null)
                {
                    if (wallSmall != null) wallSmall.Dispose();
                    wallSmall = null;
                    using (var wall = Image.FromFile(path))
                    {
                        var small = new Bitmap(Math.Max(1, scr.Width / Shrink), Math.Max(1, scr.Height / Shrink));
                        using (var g = Graphics.FromImage(small))
                        {
                            g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                            float k = Math.Max(small.Width / (float)wall.Width, small.Height / (float)wall.Height); // "Fill"
                            float w = wall.Width * k, h = wall.Height * k;
                            g.DrawImage(wall, (small.Width - w) / 2f, (small.Height - h) / 2f, w, h);
                        }
                        wallSmall = small;
                        wallKey = key;
                    }
                }

                // Cut out the mixer's area, shrink it further, then stretch it back up: a cheap, smooth blur.
                var area = new RectangleF((bounds.X - scr.X) / (float)Shrink, (bounds.Y - scr.Y) / (float)Shrink, bounds.Width / (float)Shrink, bounds.Height / (float)Shrink);
                int tw = Math.Max(2, bounds.Width / 24), th = Math.Max(2, bounds.Height / 24);
                using (var tiny = new Bitmap(tw, th))
                using (var wrap = new ImageAttributes())
                {
                    wrap.SetWrapMode(WrapMode.TileFlipXY);
                    using (var g = Graphics.FromImage(tiny))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBilinear;
                        g.DrawImage(wallSmall, new Rectangle(0, 0, tw, th), area.X, area.Y, area.Width, area.Height, GraphicsUnit.Pixel, wrap);
                    }
                    var frosted = new Bitmap(bounds.Width, bounds.Height);
                    using (var g = Graphics.FromImage(frosted))
                    {
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.Half;
                        g.DrawImage(tiny, new Rectangle(0, 0, bounds.Width, bounds.Height), 0, 0, tw, th, GraphicsUnit.Pixel, wrap);
                        using (var b = new SolidBrush(Color.FromArgb(Theme.Dark ? 175 : 160, Theme.Bg))) g.FillRectangle(b, 0, 0, bounds.Width, bounds.Height);
                    }
                    return frosted;
                }
            }
            catch { return null; }
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

        // Text with an optional 1px shadow so it stays readable on pictures and frosted glass.
        protected void DrawLabel(Graphics g, string text, Font f, Rectangle r, Color c, TextFormatFlags flags)
        {
            if (Backdrop.Active && Style.Current.TextShadow)
            {
                Rectangle sr = r;
                int o = Math.Max(1, P(1));
                sr.Offset(o, o);
                TextRenderer.DrawText(g, text, f, sr, Theme.Shadow, flags);
            }
            TextRenderer.DrawText(g, text, f, r, c, flags);
        }

        // Pill shape: rounded ends with diameter h.
        protected static void FillRound(Graphics g, Brush b, float x, float y, float w, float h)
        {
            if (w < h) w = h;
            using (var p = new GraphicsPath())
            {
                p.AddArc(x, y, h, h, 90, 180);
                p.AddArc(x + w - h, y, h, h, 270, 180);
                p.CloseFigure();
                g.FillPath(b, p);
            }
        }

        // Same slider look everywhere: rounded track, accent fill, ringed thumb.
        protected void DrawSlider(Graphics g, int x0, int x1, int y, float frac, Color fill, bool big)
        {
            DrawTrack(g, x0, x1, y, frac, fill);
            DrawThumb(g, x0, x1, y, frac, fill, big);
        }

        protected void DrawTrack(Graphics g, int x0, int x1, int y, float frac, Color fill)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tx = x0 + (int)Math.Round((x1 - x0) * Util.Clamp01(frac));
            using (var p = new Pen(Theme.Track, P(4)))
            {
                p.StartCap = p.EndCap = LineCap.Round;
                g.DrawLine(p, x0, y, x1, y);
            }
            if (tx > x0)
            {
                using (var p = new Pen(fill, P(4)))
                {
                    p.StartCap = p.EndCap = LineCap.Round;
                    g.DrawLine(p, x0, y, tx, y);
                }
            }
        }

        protected void DrawThumb(Graphics g, int x0, int x1, int y, float frac, Color fill, bool big)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int tx = x0 + (int)Math.Round((x1 - x0) * Util.Clamp01(frac));
            int ro = P(9), ri = big ? P(6) : P(5);
            using (var b = new SolidBrush(Theme.Thumb)) g.FillEllipse(b, tx - ro, y - ro, ro * 2, ro * 2);
            using (var p = new Pen(Theme.ThumbEdge, 1f)) g.DrawEllipse(p, tx - ro, y - ro, ro * 2, ro * 2);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, tx - ri, y - ri, ri * 2, ri * 2);
        }
    }

    class ChannelRow : PaintedControl
    {
        public readonly Channel Ch;
        public event EventHandler OptionsClicked;
        readonly bool gear;
        bool hover, dragging, gearHover;
        float vol;
        bool muted;
        readonly float[] shown = new float[2], hold = new float[2]; // left, right (mono uses [0])
        readonly int[] holdTicks = new int[2];

        public ChannelRow(Channel ch, float scale, bool showGear) : base(scale)
        {
            Ch = ch;
            gear = showGear && ch.IsMaster;
            SetStyle(ControlStyles.Selectable, true);
            vol = ch.Volume;
            muted = ch.Muted;
        }

        static bool Compact { get { return Style.Current.Compact; } }
        public static int HeightFor(float s) { return (int)Math.Round((Compact ? 46 : 58) * s); }

        int X0 { get { return P(52); } }
        int X1 { get { return Width - P(18); } }
        int NameY { get { return P(Compact ? 3 : 8); } }
        int SliderY { get { return P(Compact ? 26 : 34); } }
        int MeterY { get { return P(Compact ? 36 : 45); } }

        Rectangle IconRect
        {
            get
            {
                int size = P(Compact ? 22 : 26);
                return new Rectangle(P(14) + (P(26) - size) / 2, (Height - size) / 2, size, size);
            }
        }

        Rectangle GearRect { get { return gear ? new Rectangle(X1 - P(16), NameY - P(2), P(24), P(24)) : Rectangle.Empty; } }

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
            float l = 0f, r = 0f;
            if (!m)
            {
                if (Style.Current.Stereo) Ch.StereoPeak(out l, out r);
                else l = r = Ch.Peak;
            }
            bool changed = Math.Abs(v - vol) > 0.0005f || m != muted;
            changed |= StepMeter(0, MeterPos(l));
            changed |= StepMeter(1, MeterPos(r));
            vol = v;
            muted = m;
            if (changed) Invalidate();
        }

        // Fast rise, slow fall, plus a peak tick that holds for a moment.
        bool StepMeter(int i, float target)
        {
            float ns = target >= shown[i] ? target : Math.Max(target, shown[i] - 0.035f);
            float nh = hold[i];
            int nht = holdTicks[i];
            if (target >= nh) { nh = target; nht = 24; }
            else if (nht > 0) nht--;
            else nh = Math.Max(target, nh - 0.02f);
            bool changed = Math.Abs(ns - shown[i]) > 0.001f || Math.Abs(nh - hold[i]) > 0.001f;
            shown[i] = ns; hold[i] = nh; holdTicks[i] = nht;
            return changed;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            Backdrop.Paint(g, this, hover);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            var center = TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            // icon
            Rectangle ir = IconRect;
            if (Ch.IsMaster)
            {
                DrawLabel(g, muted ? "" : "", Theme.GlyphFont, ir, muted ? Theme.SubText : Theme.Text, center);
            }
            else if (Ch.Icon != null)
            {
                float alpha = muted ? 0.35f : 1f;
                if (!muted && Style.Current.IconGlow)
                {
                    // fades between the resting and light-up opacity with how loud the app is
                    float rest = Style.Current.IconIdle, lit = Style.Current.IconLit;
                    alpha = rest + (lit - rest) * Util.Clamp01(Math.Max(shown[0], shown[1]) * 1.8f);
                }
                DrawIcon(g, Ch.Icon, ir, alpha);
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

            // gear (optional, master row only), name, percent
            int textRight = x1;
            if (gear)
            {
                DrawLabel(g, "", Theme.GearFont, GearRect, gearHover ? Theme.Text : Theme.SubText, center);
                textRight = GearRect.Left - P(6);
            }
            var left = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;
            var right = TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine;

            // Name and percent fade between the resting and light-up opacity too. Windows text drawing
            // has no transparency, so the text goes onto a copy of the background that is blended in.
            float textAlpha = 1f;
            if (!Ch.IsMaster && Style.Current.IconGlow)
            {
                float rest = Style.Current.TextIdle, lit = Style.Current.TextLit;
                textAlpha = rest + (lit - rest) * Util.Clamp01(Math.Max(shown[0], shown[1]) * 1.8f);
            }
            Graphics tg = g;
            if (textAlpha < 0.999f)
            {
                if (textLayer == null || textLayer.Width != Width || textLayer.Height != Height)
                {
                    if (textLayer != null) textLayer.Dispose();
                    textLayer = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppRgb);
                }
                tg = Graphics.FromImage(textLayer);
                Backdrop.Paint(tg, this, hover);
            }
            DrawLabel(tg, Ch.Name, Theme.NameFont, new Rectangle(x0 - P(1), NameY, textRight - x0 - P(52), P(20)), muted ? Theme.SubText : Theme.Text, left);
            string pct = muted ? "Muted" : ((int)Math.Round(vol * 100)).ToString();
            DrawLabel(tg, pct, Theme.NameFont, new Rectangle(textRight - P(56), NameY, P(58), P(20)), Theme.SubText, right);
            if (tg != g)
            {
                tg.Dispose();
                var area = Rectangle.FromLTRB(x0 - P(2), Math.Max(0, NameY - P(1)), Math.Min(Width, x1 + P(4)), Math.Min(Height, NameY + P(23)));
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = textAlpha;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(textLayer, area, area.X, area.Y, area.Width, area.Height, GraphicsUnit.Pixel, ia);
                }
            }

            // Slider track and level meter(s). With bar opacity below 100% they are drawn on their own
            // layer first and blended in, so overlapping parts don't double up. The knob goes on that
            // layer too unless "knobs follow bar opacity" is off.
            Color fill = muted ? Theme.SubText : Theme.Accent;
            float barAlpha = Style.Current.BarAlpha;
            Graphics bars = g;
            if (barAlpha < 0.999f)
            {
                if (barLayer == null || barLayer.Width != Width || barLayer.Height != Height)
                {
                    if (barLayer != null) barLayer.Dispose();
                    barLayer = new Bitmap(Math.Max(1, Width), Math.Max(1, Height), PixelFormat.Format32bppPArgb);
                }
                bars = Graphics.FromImage(barLayer);
                bars.Clear(Color.Transparent);
            }
            DrawTrack(bars, x0, x1, SliderY, vol, fill);
            if (Style.Current.Stereo)
            {
                int bh = Math.Max(2, P(3)), gap = Math.Max(1, P(1));
                DrawMeter(bars, x0, x1, MeterY, bh, shown[0], hold[0]);
                DrawMeter(bars, x0, x1, MeterY + bh + gap, bh, shown[1], hold[1]);
            }
            else DrawMeter(bars, x0, x1, MeterY + P(1), Math.Max(3, P(4)), shown[0], hold[0]);
            bool knobOnLayer = bars != g && Style.Current.KnobFade;
            if (knobOnLayer) DrawThumb(bars, x0, x1, SliderY, vol, fill, hover || dragging);
            if (bars != g)
            {
                bars.Dispose();
                using (var ia = new ImageAttributes())
                {
                    var cm = new ColorMatrix();
                    cm.Matrix33 = barAlpha;
                    ia.SetColorMatrix(cm);
                    g.DrawImage(barLayer, new Rectangle(0, 0, Width, Height), 0, 0, Width, Height, GraphicsUnit.Pixel, ia);
                }
            }
            if (!knobOnLayer) DrawThumb(g, x0, x1, SliderY, vol, fill, hover || dragging);
        }

        Bitmap barLayer, textLayer;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (barLayer != null) { barLayer.Dispose(); barLayer = null; }
                if (textLayer != null) { textLayer.Dispose(); textLayer = null; }
            }
            base.Dispose(disposing);
        }

        static void DrawIcon(Graphics g, Bitmap icon, Rectangle r, float alpha)
        {
            if (alpha >= 0.999f) { g.DrawImage(icon, r); return; }
            using (var ia = new ImageAttributes())
            {
                var cm = new ColorMatrix();
                cm.Matrix33 = alpha;
                ia.SetColorMatrix(cm);
                g.DrawImage(icon, r, 0, 0, icon.Width, icon.Height, GraphicsUnit.Pixel, ia);
            }
        }

        void DrawMeter(Graphics g, int x0, int x1, int y, int h, float level, float peak)
        {
            int mw = x1 - x0;
            int fill = (int)(mw * level);
            if (Style.Current.Gradient)
            {
                // one smooth green -> yellow -> red run with rounded ends
                g.SmoothingMode = SmoothingMode.AntiAlias;
                using (var b = new SolidBrush(Theme.Track)) FillRound(g, b, x0, y, mw, h);
                if (fill > 0)
                {
                    using (var lg = new LinearGradientBrush(new RectangleF(x0 - 1, y, mw + 2, h), Theme.Meter, Theme.MeterClip, LinearGradientMode.Horizontal))
                    {
                        var blend = new ColorBlend();
                        blend.Colors = new[] { Theme.Meter, Theme.Meter, Theme.MeterHot, Theme.MeterClip };
                        blend.Positions = new[] { 0f, 0.72f, 0.9f, 1f };
                        lg.InterpolationColors = blend;
                        FillRound(g, lg, x0, y, Math.Max(fill, h), h);
                    }
                }
            }
            else
            {
                // classic: three flat blocks at -6 dB and -1 dB
                g.SmoothingMode = SmoothingMode.None;
                using (var b = new SolidBrush(Theme.Track)) g.FillRectangle(b, x0, y, mw, h);
                int hot = (int)(mw * 0.9f), clip = (int)(mw * 0.983f);
                if (fill > 0)
                {
                    using (var b = new SolidBrush(Theme.Meter)) g.FillRectangle(b, x0, y, Math.Min(fill, hot), h);
                    if (fill > hot) using (var b = new SolidBrush(Theme.MeterHot)) g.FillRectangle(b, x0 + hot, y, Math.Min(fill, clip) - hot, h);
                    if (fill > clip) using (var b = new SolidBrush(Theme.MeterClip)) g.FillRectangle(b, x0 + clip, y, fill - clip, h);
                }
            }
            if (peak > 0.01f)
            {
                g.SmoothingMode = SmoothingMode.None;
                int hx = x0 + (int)(mw * peak);
                using (var b = new SolidBrush(Theme.Hold)) g.FillRectangle(b, Math.Min(hx, x1 - P(2)), y, Math.Max(2, P(2)), h);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; gearHover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left && GearRect.Contains(e.Location))
            {
                if (OptionsClicked != null) OptionsClicked(this, EventArgs.Empty);
                return;
            }
            if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && IconRect.Contains(e.Location))) { ToggleMute(); return; }
            if (e.Button == MouseButtons.Left && e.X >= X0 - P(12) && e.Y >= SliderY - P(10)) { dragging = true; SetFromX(e.X); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) { SetFromX(e.X); return; }
            bool g = GearRect.Contains(e.Location);
            if (g != gearHover) { gearHover = g; Cursor = g ? Cursors.Hand : Cursors.Default; Invalidate(); }
        }

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
            DrawLabel(e.Graphics, Text, Theme.NameFont, ClientRectangle, Theme.SubText,
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

    // Plain slider for the options window.
    class SimpleSlider : PaintedControl
    {
        public int Min, Max = 100;
        int value;
        bool dragging, hover;
        public event EventHandler ValueChanged;
        public event EventHandler Committed;

        public SimpleSlider(float scale) : base(scale) { Height = P(28); }

        public int Value
        {
            get { return value; }
            set { int v = Math.Max(Min, Math.Min(Max, value)); if (v != this.value) { this.value = v; Invalidate(); } }
        }

        int X0 { get { return P(10); } }
        int X1 { get { return Width - P(10); } }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg);
            DrawSlider(e.Graphics, X0, X1, Height / 2, (value - Min) / (float)Math.Max(1, Max - Min), Enabled ? Theme.Accent : Theme.SubText, hover || dragging);
        }

        void SetFromX(int x)
        {
            int v = Min + (int)Math.Round(Util.Clamp01((x - X0) / (float)(X1 - X0)) * (Max - Min));
            if (v == value) return;
            Value = v;
            if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { dragging = true; SetFromX(e.X); } }
        protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromX(e.X); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            Invalidate();
            if (Committed != null) Committed(this, EventArgs.Empty);
        }
    }

    // Mixer-shaped preview of the background: drag to move the picture, scroll to zoom.
    class CropPreview : PaintedControl
    {
        public Bitmap Source;
        public Crop Crop;
        public event EventHandler CropChanged;   // live, while dragging
        public event EventHandler CropCommitted; // mouse released or wheel
        bool dragging;
        Point last;

        public CropPreview(float scale) : base(scale) { Cursor = Cursors.SizeAll; }

        public void ClampAndRefresh()
        {
            if (Source != null) Backdrop.ClampCrop(Size, Source, ref Crop);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            if (Source == null)
            {
                TextRenderer.DrawText(g, "No picture yet", Theme.NameFont, ClientRectangle, Theme.SubText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                Backdrop.Compose(g, Size, Source, Crop);
                // A few fake rows so it is obvious how readable the text will be.
                g.SmoothingMode = SmoothingMode.AntiAlias;
                for (int i = 0; i < 6; i++)
                {
                    int y = P(14) + i * P(40);
                    if (y + P(30) > Height) break;
                    using (var b = new SolidBrush(Color.FromArgb(200, Theme.Text))) g.FillRectangle(b, P(36), y, P(60) + (i * 37 % 40), P(5));
                    using (var p = new Pen(Theme.Accent, P(3))) g.DrawLine(p, P(36), y + P(14), P(36) + (Width - P(56)) * (60 + i * 7) / 100, y + P(14));
                    using (var b = new SolidBrush(Theme.Track)) g.FillEllipse(b, P(10), y, P(18), P(18));
                }
            }
            using (var p = new Pen(Theme.Line)) g.DrawRectangle(p, 0, 0, Width - 1, Height - 1);
        }

        RectangleF Placed() { return Backdrop.Place(Size, Source, Crop); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (Source == null || e.Button != MouseButtons.Left) return;
            dragging = true;
            last = e.Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging) return;
            RectangleF r = Placed();
            Crop.X -= (e.X - last.X) / r.Width;
            Crop.Y -= (e.Y - last.Y) / r.Height;
            last = e.Location;
            ClampAndRefresh();
            if (CropChanged != null) CropChanged(this, EventArgs.Empty);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            if (CropCommitted != null) CropCommitted(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            var he = e as HandledMouseEventArgs;
            if (he != null) he.Handled = true;
            if (Source == null) return;
            Crop.Zoom = Math.Max(1f, Math.Min(5f, Crop.Zoom * (float)Math.Pow(1.1, e.Delta / 120.0)));
            ClampAndRefresh();
            if (CropChanged != null) CropChanged(this, EventArgs.Empty);
            if (CropCommitted != null) CropCommitted(this, EventArgs.Empty);
        }
    }

    // Dark look for the small right-click menu on app rows.
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
            using (var b = new SolidBrush(e.Item.Selected ? Theme.RowHover : Theme.Bg))
                e.Graphics.FillRectangle(b, new Rectangle(Point.Empty, e.Item.Size));
        }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = Theme.Text;
            base.OnRenderItemText(e);
        }
        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            using (var b = new SolidBrush(Theme.Line)) e.Graphics.FillRectangle(b, 4, e.Item.Height / 2, e.Item.Width - 8, 1);
        }
    }

    // Round color button for the Look tab.
    class Swatch : PaintedControl
    {
        public readonly Color Color;
        public bool Selected;
        bool hover;

        public Swatch(float scale, Color color) : base(scale)
        {
            Color = color;
            Size = new Size(P(30), P(30));
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Theme.Bg);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (Selected || hover)
            {
                using (var p = new Pen(Selected ? Theme.Text : Theme.SubText, Math.Max(1.5f, 2 * s)))
                    g.DrawEllipse(p, P(1), P(1), Width - P(3), Height - P(3));
            }
            int inset = P(5);
            Color c = Enabled ? Color : Color.FromArgb(70, Color);
            using (var b = new SolidBrush(c)) g.FillEllipse(b, inset, inset, Width - inset * 2 - 1, Height - inset * 2 - 1);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // Tab button on the left side of the options window.
    class NavItem : PaintedControl
    {
        public bool Selected;
        bool hover;

        public NavItem(float scale, string text) : base(scale)
        {
            Text = text;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Selected || hover ? Theme.RowHover : Theme.Bg);
            if (Selected)
                using (var b = new SolidBrush(Theme.Accent)) g.FillRectangle(b, 0, P(9), Math.Max(2, P(3)), Height - P(18));
            TextRenderer.DrawText(g, Text, Theme.NameFont, new Rectangle(P(14), 0, Width - P(14), Height), Selected ? Theme.Text : Theme.SubText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
    }

    // ---------------- Options window ----------------

    class OptionsDialog : Form
    {
        [DllImport("user32.dll")] static extern short GetAsyncKeyState(int vk);

        static readonly string[] PageNames = { "General", "Background", "Look", "Effects", "Apps" };
        static readonly int[] Presets = { 0xFF8AD8, 0xB794F6, 0x60CDFF, 0x4FD1C5, 0x6CCB5F, 0xF6D55C, 0xFF9F43, 0xFF6B6B };

        readonly Host host;
        readonly float s;
        readonly List<NavItem> navs = new List<NavItem>();
        readonly List<Panel> pages = new List<Panel>();
        readonly List<Swatch> swatches = new List<Swatch>();
        readonly List<Control> ownColorControls = new List<Control>();
        readonly ToolTip tips = new ToolTip();
        Label hotkeyValue, hotkeyStatus, accentName;
        Button removeBtn;
        CropPreview preview;
        SimpleSlider dimSlider, zoomSlider;
        bool capturing;
        bool childOpen; // file picker, color picker or message box up: don't close on deactivate
        public bool ClosedByUser; // Done or Esc, as opposed to clicking somewhere else

        // Every setting change goes through here so the preview mixer redraws.
        void Changed() { if (host != null) host.SettingsChanged(); }

        public OptionsDialog(Host host, Size mixerSize)
        {
            this.host = host;
            Theme.LoadColors();
            Text = "Jackamixer options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            KeyPreview = true;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.NameFont;
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            using (var g = CreateGraphics()) s = g.DpiX / 96f;
            ClientSize = new Size(P(780), P(500));

            for (int i = 0; i < PageNames.Length; i++)
            {
                int index = i;
                var nav = new NavItem(s, PageNames[i]) { Bounds = new Rectangle(P(12), P(14) + i * P(40), P(150), P(36)) };
                nav.Click += (o, e) => ShowPage(index);
                Controls.Add(nav);
                navs.Add(nav);
                var page = new Panel { Bounds = new Rectangle(P(196), P(14), ClientSize.Width - P(214), ClientSize.Height - P(76)), BackColor = Theme.Bg, Visible = false };
                Controls.Add(page);
                pages.Add(page);
            }
            Controls.Add(new Panel { Bounds = new Rectangle(P(176), P(14), 1, ClientSize.Height - P(28)), BackColor = Theme.Line });

            BuildGeneral(pages[0]);
            BuildBackground(pages[1], mixerSize);
            BuildLook(pages[2]);
            BuildEffects(pages[3]);
            BuildApps(pages[4]);

            var done = MakeButton(this, "Done", new Rectangle(ClientSize.Width - P(130), ClientSize.Height - P(50), P(110), P(32)));
            done.Click += (o, e) => { ClosedByUser = true; Close(); };
            ShowPage(0);
        }

        int P(float v) { return (int)Math.Round(v * s); }

        public void ShowPage(int index)
        {
            for (int i = 0; i < pages.Count; i++)
            {
                pages[i].Visible = i == index;
                navs[i].Selected = i == index;
                navs[i].Invalidate();
            }
        }

        // ---- General: hotkey, what the hotkey does, right-click/gear ----

        void BuildGeneral(Panel p)
        {
            int w = p.Width;
            MakeLabel(p, "Hotkey", 0, 0, w, Theme.SubText);
            hotkeyValue = MakeLabel(p, Config.ReadHotkey(), 0, P(22), w, Theme.Text);
            hotkeyValue.Font = Theme.TitleFont;
            hotkeyValue.Height = P(32);
            MakeButton(p, "Change", new Rectangle(0, P(60), P(110), P(32))).Click += (o, e) => StartCapture();
            MakeButton(p, "Use Win+\\", new Rectangle(P(120), P(60), P(110), P(32))).Click += (o, e) => Apply(Config.DefaultHotkey);
            hotkeyStatus = MakeLabel(p, "", 0, P(98), w, Theme.SubText);
            hotkeyStatus.Height = P(40);

            MakeLabel(p, "When you press the hotkey", 0, P(150), w, Theme.SubText);
            MakeRadio(p, "Open the mixer (clicking away closes it)", "hotkey_mode", "close", 0, P(172), w, null);
            MakeRadio(p, "Toggle the mixer on and off", "hotkey_mode", "toggle", 0, P(198), w, null);

            MakeLabel(p, "On the mixer", 0, P(244), w, Theme.SubText);
            MakeCheck(p, "Right-click menu (hide app, options)", "right_click_options", 0, P(266), w, null);
            MakeCheck(p, "Gear button opens options", "show_gear", 0, P(292), w, null);
        }

        // ---- Background: picture, crop, darkness, frosted glass ----

        void BuildBackground(Panel p, Size mixerSize)
        {
            int ph = P(340);
            int pw = Math.Max(P(160), Math.Min(P(250), (int)(ph * mixerSize.Width / (float)Math.Max(1, mixerSize.Height))));
            preview = new CropPreview(s) { Bounds = new Rectangle(0, 0, pw, ph) };
            preview.Source = Backdrop.Source(Config.Get("background"));
            preview.Crop = Crop.FromConfig();
            preview.CropChanged += (o, e) =>
            {
                zoomSlider.Value = (int)Math.Round(preview.Crop.Zoom * 100);
                preview.Crop.Save();
                Changed();
            };
            preview.CropCommitted += (o, e) => preview.Crop.Save();
            p.Controls.Add(preview);
            MakeLabel(p, "Drag to move, scroll to zoom.", 0, ph + P(8), pw + P(10), Theme.SubText);

            int x = pw + P(30), w = p.Width - x;
            MakeLabel(p, "Picture", x, 0, w, Theme.SubText);
            MakeButton(p, "Choose...", new Rectangle(x, P(22), P(110), P(32))).Click += (o, e) => ChoosePicture();
            removeBtn = MakeButton(p, "Remove", new Rectangle(x + P(120), P(22), P(110), P(32)));
            removeBtn.Click += (o, e) =>
            {
                Config.Set("background", "");
                preview.Source = null;
                preview.Invalidate();
                UpdateEnabled();
                Changed();
            };

            MakeLabel(p, "Darkness", x, P(74), w, Theme.SubText);
            dimSlider = new SimpleSlider(s) { Min = 0, Max = 90, Bounds = new Rectangle(x - P(10), P(94), w + P(10), P(28)) };
            dimSlider.Value = preview.Crop.Dim;
            dimSlider.ValueChanged += (o, e) => { preview.Crop.Dim = dimSlider.Value; preview.Invalidate(); preview.Crop.Save(); Changed(); };
            dimSlider.Committed += (o, e) => preview.Crop.Save();
            p.Controls.Add(dimSlider);

            MakeLabel(p, "Zoom", x, P(130), w, Theme.SubText);
            zoomSlider = new SimpleSlider(s) { Min = 100, Max = 500, Bounds = new Rectangle(x - P(10), P(150), w + P(10), P(28)) };
            zoomSlider.Value = (int)Math.Round(preview.Crop.Zoom * 100);
            zoomSlider.ValueChanged += (o, e) => { preview.Crop.Zoom = zoomSlider.Value / 100f; preview.ClampAndRefresh(); preview.Crop.Save(); Changed(); };
            zoomSlider.Committed += (o, e) => preview.Crop.Save();
            p.Controls.Add(zoomSlider);

            MakeLabel(p, "Without a picture", x, P(200), w, Theme.SubText);
            MakeCheck(p, "Frosted glass (blurred wallpaper)", "frosted", x, P(222), w, null);

            preview.ClampAndRefresh();
            UpdateEnabled();
        }

        // ---- Look: colors and bar opacity ----

        void BuildLook(Panel p)
        {
            int w = p.Width;
            MakeCheck(p, "Use Windows colors (accent color and light/dark mode)", "use_windows_colors", 0, 0, w, ColorsChanged);

            ownColorControls.Add(MakeLabel(p, "Your colors", 0, P(36), w, Theme.SubText));
            for (int i = 0; i < Presets.Length; i++)
            {
                Color c = Theme.Hex(Presets[i]);
                var sw = new Swatch(s, c) { Location = new Point(i * P(38), P(58)) };
                sw.Click += (o, e) => { Config.Set("accent", Theme.ToHex(c)); ColorsChanged(); };
                tips.SetToolTip(sw, Theme.ToHex(c));
                p.Controls.Add(sw);
                swatches.Add(sw);
                ownColorControls.Add(sw);
            }
            var custom = MakeButton(p, "Custom...", new Rectangle(Presets.Length * P(38) + P(4), P(58), P(100), P(30)));
            custom.Click += (o, e) => PickCustomColor();
            ownColorControls.Add(custom);
            accentName = MakeLabel(p, "", 0, P(94), w, Theme.SubText);
            ownColorControls.Add(MakeRadio(p, "Dark background", "theme", "dark", 0, P(118), P(170), ColorsChanged));
            ownColorControls.Add(MakeRadio(p, "Light background", "theme", "light", P(180), P(118), P(170), ColorsChanged));

            MakePercentSlider(p, "Bar opacity (slider tracks and meters)", "bar_opacity", 10, 100, 0, P(164), Math.Min(w, P(360)));
            MakeCheck(p, "Slider knobs follow bar opacity", "knob_fade", 0, P(220), w, null);

            UpdateColorControls();
        }

        // ---- Effects: the on/off extras ----

        void BuildEffects(Panel p)
        {
            int w = p.Width;
            MakeLabel(p, "Effects", 0, 0, w, Theme.SubText);
            string[,] checks =
            {
                { "Text shadow on pictures", "text_shadow" },
                { "Smooth gradient meters", "gradient_meters" },
                { "Open and close animation", "animate" },
                { "Apps light up when they play sound", "icon_glow" },
                { "Stereo meters (left and right)", "stereo_meters" },
                { "Compact rows", "compact" }
            };
            var lightUp = new List<SimpleSlider>();
            for (int i = 0; i < checks.GetLength(0); i++)
            {
                string key = checks[i, 1];
                CheckBox box = null;
                Action changed = null;
                if (key == "icon_glow")
                    changed = () =>
                    {
                        foreach (var sl in lightUp) { sl.Enabled = box.Checked; sl.Invalidate(); }
                        Changed();
                    };
                box = MakeCheck(p, checks[i, 0], key, 0, P(22) + i * P(26), w, changed);
            }

            // How faded quiet apps are and how bright playing ones get, for icons and for names
            // (only while "Apps light up" is on). Two columns so it stays tidy.
            int cw = P(250), tx = P(300);
            MakeLabel(p, "App icons", 0, P(186), cw, Theme.SubText);
            MakeLabel(p, "App names", tx, P(186), cw, Theme.SubText);
            lightUp.Add(MakePercentSlider(p, "Resting (not playing)", "icon_idle", 10, 100, 0, P(212), cw));
            lightUp.Add(MakePercentSlider(p, "Light-up (playing)", "icon_lit", 10, 100, 0, P(268), cw));
            lightUp.Add(MakePercentSlider(p, "Resting (not playing)", "text_idle", 10, 100, tx, P(212), cw));
            lightUp.Add(MakePercentSlider(p, "Light-up (playing)", "text_lit", 10, 100, tx, P(268), cw));
            bool on = Config.GetBool("icon_glow");
            foreach (var sl in lightUp) sl.Enabled = on;
        }

        // Label with the live value ("Bar opacity: 80%") above a slider bound to one setting.
        SimpleSlider MakePercentSlider(Control parent, string text, string key, int min, int max, int x, int y, int w)
        {
            var label = MakeLabel(parent, "", x, y, w, Theme.SubText);
            var slider = new SimpleSlider(s) { Min = min, Max = max, Bounds = new Rectangle(x - P(10), y + P(20), w + P(20), P(28)) };
            slider.Value = Config.GetInt(key, min, max);
            label.Text = text + ": " + slider.Value + "%";
            slider.ValueChanged += (o, e) =>
            {
                label.Text = text + ": " + slider.Value + "%";
                Config.Set(key, slider.Value.ToString());
                Changed();
            };
            parent.Controls.Add(slider);
            return slider;
        }

        void ColorsChanged()
        {
            Theme.LoadColors();
            UpdateColorControls();
            Invalidate(true);
            Changed();
        }

        void UpdateColorControls()
        {
            bool own = !Config.GetBool("use_windows_colors");
            foreach (var c in ownColorControls) c.Enabled = own;
            foreach (var sw in swatches)
            {
                sw.Selected = own && sw.Color.ToArgb() == Theme.Accent.ToArgb();
                sw.Invalidate();
            }
            accentName.Text = own ? "Accent " + Theme.ToHex(Theme.Accent) : "Following Windows: accent " + Theme.ToHex(Theme.Accent) + (Theme.Dark ? ", dark mode" : ", light mode");
        }

        void PickCustomColor()
        {
            using (var dlg = new ColorDialog())
            {
                dlg.FullOpen = true;
                dlg.Color = Theme.Accent;
                childOpen = true;
                DialogResult r = dlg.ShowDialog(this);
                childOpen = false;
                if (r != DialogResult.OK) return;
                Config.Set("accent", Theme.ToHex(dlg.Color));
                ColorsChanged();
            }
        }

        // ---- Apps: show/hide list ----

        class AppItem
        {
            public string Id, Name;
            public override string ToString() { return Name; }
        }

        void BuildApps(Panel p)
        {
            MakeLabel(p, "Show in the mixer (untick to hide)", 0, 0, P(320), Theme.SubText);
            var list = new CheckedListBox
            {
                Bounds = new Rectangle(0, P(26), P(300), p.Height - P(30)),
                CheckOnClick = true, BorderStyle = BorderStyle.None, IntegralHeight = false,
                BackColor = Theme.Bg, ForeColor = Theme.Text, Font = Theme.NameFont
            };
            FillApps(list);
            list.ItemCheck += (o, e) =>
            {
                var item = (AppItem)list.Items[e.Index];
                Config.SetHidden(item.Id, e.NewValue != CheckState.Checked);
                Changed();
            };
            p.Controls.Add(list);
            var note = MakeLabel(p, "Apps that only listen to your sound (iCUE lighting, Discord screen share, OBS) bounce along with everything else. " +
                "Hide them here, or right-click them in the mixer.", P(320), P(26), p.Width - P(320), Theme.SubText);
            note.Height = P(100);
        }

        // Apps that currently have audio sessions, plus hidden ones that aren't running right now.
        void FillApps(CheckedListBox list)
        {
            var hidden = Config.HiddenApps();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var engine = new AudioEngine(P(16));
                engine.Open();
                foreach (var a in engine.ReadApps())
                    if (a.HideId != null && seen.Add(a.HideId))
                        list.Items.Add(new AppItem { Id = a.HideId, Name = a.Name }, !hidden.Contains(a.HideId));
            }
            catch { }
            foreach (string id in hidden)
                if (seen.Add(id))
                    list.Items.Add(new AppItem { Id = id, Name = id + " (not running)" }, false);
        }

        // ---- small builders ----

        Label MakeLabel(Control parent, string text, int x, int y, int w, Color color)
        {
            var l = new Label { Text = text, Bounds = new Rectangle(x, y, w, P(22)), ForeColor = color, BackColor = Theme.Bg };
            parent.Controls.Add(l);
            return l;
        }

        Button MakeButton(Control parent, string text, Rectangle bounds)
        {
            var b = new Button { Text = text, Bounds = bounds, FlatStyle = FlatStyle.Flat, BackColor = Theme.RowHover, ForeColor = Theme.Text, TabStop = false };
            b.FlatAppearance.BorderColor = Theme.Line;
            parent.Controls.Add(b);
            return b;
        }

        CheckBox MakeCheck(Control parent, string text, string key, int x, int y, int w, Action changed)
        {
            var c = new CheckBox
            {
                Text = text, Bounds = new Rectangle(x, y, w, P(24)), Checked = Config.GetBool(key),
                ForeColor = Theme.Text, BackColor = Theme.Bg, FlatStyle = FlatStyle.Flat, TabStop = false
            };
            c.CheckedChanged += (o, e) =>
            {
                Config.Set(key, c.Checked ? "1" : "0");
                if (changed != null) changed();
                else Changed();
            };
            parent.Controls.Add(c);
            return c;
        }

        RadioButton MakeRadio(Control parent, string text, string key, string value, int x, int y, int w, Action changed)
        {
            var r = new RadioButton
            {
                Text = text, Bounds = new Rectangle(x, y, w, P(24)), Checked = Config.Get(key) == value,
                ForeColor = Theme.Text, BackColor = Theme.Bg, FlatStyle = FlatStyle.Flat, TabStop = false
            };
            r.CheckedChanged += (o, e) =>
            {
                if (!r.Checked) return;
                Config.Set(key, value);
                if (changed != null) changed();
                else Changed();
            };
            parent.Controls.Add(r);
            return r;
        }

        void UpdateEnabled()
        {
            bool has = preview.Source != null;
            removeBtn.Enabled = has;
            dimSlider.Enabled = has;
            zoomSlider.Enabled = has;
            dimSlider.Invalidate();
            zoomSlider.Invalidate();
        }

        void ChoosePicture()
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "Pick a background picture for Jackamixer";
                dlg.Filter = "Pictures|*.png;*.jpg;*.jpeg;*.bmp;*.gif|All files|*.*";
                childOpen = true;
                DialogResult picked = dlg.ShowDialog(this);
                childOpen = false;
                if (picked != DialogResult.OK) return;
                Bitmap src = Backdrop.Source(dlg.FileName);
                if (src == null)
                {
                    childOpen = true;
                    MessageBox.Show(this, "Could not open that picture.", "Jackamixer");
                    childOpen = false;
                    return;
                }
                Config.Set("background", dlg.FileName);
                preview.Source = src;
                preview.Crop.Zoom = 1f;
                preview.Crop.X = 0.5f;
                preview.Crop.Y = 0.5f;
                preview.ClampAndRefresh();
                preview.Crop.Save();
                zoomSlider.Value = 100;
                UpdateEnabled();
                Changed();
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
        }

        // ---- hotkey capture ----

        void StartCapture()
        {
            if (host == null) return;
            capturing = true;
            host.PauseHotkey(); // so the current combo can be pressed here too
            hotkeyValue.Text = "Press keys...";
            hotkeyStatus.ForeColor = Theme.SubText;
            hotkeyStatus.Text = "Hold Win, Ctrl or Alt and press a key. Esc cancels.";
        }

        void StopCapture()
        {
            capturing = false;
            hotkeyValue.Text = Config.ReadHotkey();
            if (host != null) host.RegisterFromConfig();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!capturing)
            {
                if (keyData == Keys.Escape) { ClosedByUser = true; Close(); return true; }
                return base.ProcessCmdKey(ref msg, keyData);
            }
            Keys key = keyData & Keys.KeyCode;
            if (key == Keys.ShiftKey || key == Keys.ControlKey || key == Keys.Menu || key == Keys.LWin || key == Keys.RWin) return true;
            bool win = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            if (keyData == Keys.Escape && !win) { StopCapture(); hotkeyStatus.Text = ""; return true; }
            int mods = 0;
            if (win) mods |= 0x0008;
            if ((keyData & Keys.Control) != 0) mods |= 0x0002;
            if ((keyData & Keys.Alt) != 0) mods |= 0x0001;
            if ((keyData & Keys.Shift) != 0) mods |= 0x0004;
            bool aloneOk = (key >= Keys.F13 && key <= Keys.F24) || key == Keys.Pause || key == Keys.Scroll;
            if ((mods & 0x000B) == 0 && !aloneOk)
            {
                // A bare letter or F-key would stop working everywhere else.
                hotkeyStatus.ForeColor = Theme.MeterClip;
                hotkeyStatus.Text = "Hold Win, Ctrl or Alt together with that key.";
                return true;
            }
            Apply(Config.Describe(mods, (int)key));
            return true;
        }

        void Apply(string text)
        {
            if (host == null) return;
            host.PauseHotkey();
            string err = host.TryHotkey(text);
            if (err != null)
            {
                hotkeyStatus.ForeColor = Theme.MeterClip;
                hotkeyStatus.Text = err;
                if (!capturing) host.RegisterFromConfig();
                return;
            }
            StopCapture();
            hotkeyStatus.ForeColor = Theme.Meter;
            hotkeyStatus.Text = "Saved.";
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
        }

        // Like the mixer: clicking anywhere else closes it (everything is already saved).
        // Clicking over to the preview mixer is fine.
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (host == null) return;
            BeginInvoke((Action)(() =>
            {
                if (IsDisposed || childOpen || capturing || Form.ActiveForm is MixerForm) return;
                Close();
            }));
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (capturing) StopCapture();
            tips.Dispose();
            base.OnFormClosed(e);
        }
    }

    // ---------------- Flyout ----------------

    class MixerForm : Form
    {
        readonly AudioEngine engine;
        readonly BackPanel list = new BackPanel();
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        readonly List<ChannelRow> rows = new List<ChannelRow>();
        readonly float s;
        readonly bool snapshot;
        readonly Host host;
        bool showGear, rightClickOptions, closeOnClickAway;
        HashSet<string> hidden;
        readonly ContextMenuStrip rowMenu = new ContextMenuStrip();
        Rectangle workArea;
        string signature = "";
        int ticks;
        bool activatedOnce;

        public MixerForm(bool snapshotMode, Host host, bool preview = false)
        {
            snapshot = snapshotMode;
            Preview = preview; // before anything else: the window can get activated while it is being built
            this.host = host;
            Theme.LoadColors();
            Style.Current = Style.Load();
            if (Style.Current.Animate && !snapshot) Opacity = 0; // faded in by OnShown
            showGear = Config.GetBool("show_gear");
            rightClickOptions = Config.GetBool("right_click_options");
            closeOnClickAway = Config.Get("hotkey_mode") != "toggle";
            hidden = Config.HiddenApps();
            rowMenu.Renderer = new MenuRenderer();
            rowMenu.ShowImageMargin = false;
            rowMenu.Font = Theme.NameFont;
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
            HookRightClick(list);
            Controls.Add(list);
            engine.Open();
            RefreshChannels(true);
            timer.Interval = 33;
            timer.Tick += (o, e) => Step();
            animTimer.Interval = 10;
            animTimer.Tick += (o, e) => AnimStep();
        }

        int P(float v) { return (int)Math.Round(v * s); }

        // ---- open/close animation: short slide + fade, like the Windows flyouts ----

        readonly System.Windows.Forms.Timer animTimer = new System.Windows.Forms.Timer();
        bool animOpening, closingAnim, closeReady;
        int animStart, restTop;

        void StartAnim(bool opening)
        {
            animOpening = opening;
            animStart = Environment.TickCount;
            if (opening)
            {
                restTop = Top;
                Top = restTop + P(14);
            }
            animTimer.Start();
        }

        void AnimStep()
        {
            float t = Math.Min(1f, (Environment.TickCount - animStart) / (animOpening ? 160f : 110f));
            float ease = 1f - (1f - t) * (1f - t) * (1f - t);
            if (animOpening)
            {
                Opacity = ease;
                Top = restTop + (int)Math.Round(P(14) * (1f - ease));
            }
            else
            {
                Opacity = 1f - ease;
                Top = restTop + (int)Math.Round(P(8) * ease);
            }
            if (t < 1f) return;
            animTimer.Stop();
            if (!animOpening) { closeReady = true; Close(); }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (Style.Current.Animate && !snapshot && !closeReady && Visible && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                if (!closingAnim)
                {
                    closingAnim = true;
                    animTimer.Stop();
                    restTop = Top;
                    StartAnim(false);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        void OpenOptions()
        {
            if (host != null) host.OpenOptions(this);
            else Close();
        }

        // While the options window is open the mixer stays up as a live preview:
        // clicking between the two keeps both, clicking anywhere else closes both.
        public bool Preview;

        // The preview pops up beside the options window without taking focus from it.
        protected override bool ShowWithoutActivation { get { return Preview; } }

        // Re-read every setting and redraw (called by the options window as things change).
        public void Reload()
        {
            Theme.LoadColors();
            Style.Current = Style.Load();
            showGear = Config.GetBool("show_gear");
            rightClickOptions = Config.GetBool("right_click_options");
            closeOnClickAway = Config.Get("hotkey_mode") != "toggle";
            hidden = Config.HiddenApps();
            BackColor = Theme.Bg;
            list.BackColor = Theme.Bg;
            int dark = Theme.Dark ? 1 : 0;
            Native.DwmSetWindowAttribute(Handle, 20, ref dark, 4);
            RefreshChannels(true);
        }

        // Right-click menu: "Hide <app>" on an app row, "Options..." everywhere. Can be turned off in the options.
        void HookRightClick(Control c)
        {
            c.MouseUp += (o, e) =>
            {
                if (e.Button != MouseButtons.Right || !rightClickOptions) return;
                var row = c as ChannelRow;
                ShowMenu(c, row == null ? null : row.Ch as AppChannel, e.Location);
            };
        }

        void ShowMenu(Control source, AppChannel app, Point at)
        {
            rowMenu.Items.Clear();
            if (app != null && app.HideId != null)
            {
                rowMenu.Items.Add("Hide " + app.Name, null, (o, e) =>
                {
                    Config.SetHidden(app.HideId, true);
                    hidden.Add(app.HideId);
                    // after the menu has closed, since the row it came from goes away
                    BeginInvoke((Action)(() => RefreshChannels(true)));
                });
                rowMenu.Items.Add(new ToolStripSeparator());
            }
            rowMenu.Items.Add("Options...", null, (o, e) => BeginInvoke((Action)OpenOptions));
            rowMenu.Show(source, at);
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
            if (!Preview) // a preview opened next to the options window shouldn't take focus from it
            {
                Activate();
                Native.SetForegroundWindow(Handle);
            }
            if (!snapshot) timer.Start();
            if (Style.Current.Animate && !snapshot) StartAnim(true);
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            if (Visible) activatedOnce = true; // activations while it is still being built do not count
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            if (snapshot) return;
            if (Preview)
            {
                if (!activatedOnce) return;
                // wait until focus has landed: moving to the options window is fine, anywhere else ends both
                BeginInvoke((Action)(() =>
                {
                    if (Form.ActiveForm is OptionsDialog || IsDisposed) return;
                    if (host != null) host.CloseOptions();
                    Close();
                }));
                return;
            }
            if (activatedOnce && closeOnClickAway) Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) Close();
            base.OnKeyDown(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Stop();
            rowMenu.Dispose();
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
            apps.RemoveAll(a => a.HideId != null && hidden.Contains(a.HideId));
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
            int rowH = ChannelRow.HeightFor(s), sepH = P(9), pad = P(6), emptyH = P(44), w = P(380);
            bool hasMaster = engine.Master != null;
            int total = pad * 2 + (hasMaster ? rowH + sepH : 0) + (apps.Count > 0 ? apps.Count * rowH : emptyH);
            if (workArea.IsEmpty) workArea = AvoidTaskbar(Screen.FromPoint(Cursor.Position).WorkingArea);
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
                AddOther(new Separator(s) { Bounds = new Rectangle(0, y, rw, sepH) });
                y += sepH;
            }
            if (apps.Count == 0)
                AddOther(new Note(s, hasMaster ? "No apps are playing sound" : "No sound output device found") { Bounds = new Rectangle(0, y, rw, emptyH) });
            foreach (var a in apps) { AddRow(a, y, rw, rowH); y += rowH; }
            list.ResumeLayout();

            var rest = new Rectangle(workArea.Right - w - P(12), workArea.Bottom - h - P(12), w, h);
            if (animTimer.Enabled) restTop = rest.Top; // mid-animation: only move where it settles
            else Bounds = rest;
            Size = rest.Size;
            Backdrop.Build(rest);
            Invalidate(true);
        }

        [StructLayout(LayoutKind.Sequential)]
        struct AppBarData { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage, uEdge; public int left, top, right, bottom; public IntPtr lParam; }
        [DllImport("shell32.dll")] static extern IntPtr SHAppBarMessage(uint msg, ref AppBarData data);

        // An auto-hide taskbar doesn't reserve screen space, so the work area runs under it. Stay clear of it anyway.
        static Rectangle AvoidTaskbar(Rectangle area)
        {
            var abd = new AppBarData { cbSize = Marshal.SizeOf(typeof(AppBarData)) };
            if (SHAppBarMessage(5, ref abd) == IntPtr.Zero) return area; // ABM_GETTASKBARPOS
            var bar = Rectangle.FromLTRB(abd.left, abd.top, abd.right, abd.bottom);
            if (!bar.IntersectsWith(area)) return area;
            switch (abd.uEdge)
            {
                case 3: return Rectangle.FromLTRB(area.Left, area.Top, area.Right, Math.Min(area.Bottom, bar.Top));    // bottom
                case 1: return Rectangle.FromLTRB(area.Left, Math.Max(area.Top, bar.Bottom), area.Right, area.Bottom); // top
                case 2: return Rectangle.FromLTRB(area.Left, area.Top, Math.Min(area.Right, bar.Left), area.Bottom);   // right
                case 0: return Rectangle.FromLTRB(Math.Max(area.Left, bar.Right), area.Top, area.Right, area.Bottom);  // left
            }
            return area;
        }

        void AddRow(Channel ch, int y, int w, int h)
        {
            var row = new ChannelRow(ch, s, showGear) { Bounds = new Rectangle(0, y, w, h) };
            row.OptionsClicked += (o, e) => OpenOptions();
            AddOther(row);
            rows.Add(row);
        }

        void AddOther(Control c)
        {
            HookRightClick(c);
            list.Controls.Add(c);
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
        public const int WM_OPTIONS = 0x8004;
        const int WM_HOTKEY = 0x0312;
        MixerForm form;
        OptionsDialog options;
        Size lastMixerSize = new Size(380, 540);
        bool backToMixer;
        readonly System.Windows.Forms.Timer reloadTimer = new System.Windows.Forms.Timer { Interval = 60 };

        public Host()
        {
            CreateHandle(new CreateParams { Caption = Title });
            reloadTimer.Tick += (o, e) =>
            {
                reloadTimer.Stop();
                if (form != null && !form.IsDisposed) form.Reload();
            };
        }

        // From the mixer: it stays open as the live preview, and Done goes back to it.
        public void OpenOptions(MixerForm from)
        {
            lastMixerSize = from.ClientSize;
            from.Preview = true;
            backToMixer = true;
            Native.PostMessage(Handle, WM_OPTIONS, IntPtr.Zero, IntPtr.Zero);
        }

        // From the Start menu or the command line.
        public void OpenOptions(Size mixerSize, bool fromMixer)
        {
            lastMixerSize = mixerSize;
            backToMixer = fromMixer;
            Native.PostMessage(Handle, WM_OPTIONS, IntPtr.Zero, IntPtr.Zero);
        }

        // The options window calls this on every change; the preview redraws a moment later
        // (batched, so dragging a slider doesn't rebuild the mixer on every pixel).
        public void SettingsChanged()
        {
            reloadTimer.Stop();
            reloadTimer.Start();
        }

        public void CloseOptions()
        {
            if (options != null && !options.IsDisposed) options.Close();
        }

        public void PauseHotkey() { Native.UnregisterHotKey(Handle, 1); }

        // Returns null when the hotkey is active, otherwise a message for the user.
        public string RegisterFromConfig()
        {
            Native.UnregisterHotKey(Handle, 1);
            string text = Config.ReadHotkey();
            int mods, vk;
            if (!Config.ParseHotkey(text, out mods, out vk))
                return "Could not read the hotkey \"" + text + "\" in Jackamixer.ini. Examples: Win+\\, Ctrl+Alt+M, F13.";
            if (!Native.RegisterHotKey(Handle, 1, mods | 0x4000, vk)) // MOD_NOREPEAT
                return text + " is already taken by Windows or another app. Open Jackamixer from the Start menu, right-click it and pick another.";
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

        MixerForm NewMixer(bool preview)
        {
            var m = new MixerForm(false, this, preview);
            m.FormClosed += (o, e) =>
            {
                if (form == m) form = null;
                GC.Collect(); // drop the audio session objects right away
            };
            return m;
        }

        public void Toggle()
        {
            if (options != null) { options.Activate(); return; }
            if (form != null && !form.IsDisposed) { form.Close(); return; }
            form = NewMixer(false);
            form.Show();
        }

        // Options window plus the mixer beside it as a live preview.
        void ShowOptions()
        {
            if (options != null) { options.Activate(); return; }
            if (form == null || form.IsDisposed)
            {
                form = NewMixer(true); // opened from the Start menu: bring the mixer up as the preview
                form.Preview = true;
                form.Show();
            }
            else form.Preview = true;

            options = new OptionsDialog(this, lastMixerSize);
            options.FormClosed += (o, e) =>
            {
                bool done = ((OptionsDialog)o).ClosedByUser;
                options = null;
                string err = RegisterFromConfig();
                if (err != null) MessageBox.Show(err, "Jackamixer");
                if (form != null && !form.IsDisposed)
                {
                    // Done/Esc after coming from the mixer: back to it. Otherwise the preview goes too.
                    if (done && backToMixer) { form.Preview = false; form.Activate(); }
                    else form.Close();
                }
                backToMixer = false;
            };
            options.Show();
        }

        protected override void WndProc(ref Message m)
        {
            switch (m.Msg)
            {
                case WM_HOTKEY:
                case WM_TOGGLE: Toggle(); return;
                case WM_OPTIONS:
                    if (m.WParam != IntPtr.Zero) backToMixer = false; // sent by "Jackamixer.exe --options"
                    ShowOptions();
                    return;
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

            Native.SetProcessDPIAware();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Theme.Load();

            if ((first == "--snapshot" || first == "--snapshot-options") && args.Length > 1) { Snapshot(first == "--snapshot-options", args[1], args.Length > 2 ? int.Parse(args[2]) : 0); return; }

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
                        int msg = first == "--quit" ? Host.WM_QUIT_HOST : first == "--reload" ? Host.WM_RELOAD :
                                  first == "--options" ? Host.WM_OPTIONS : Host.WM_TOGGLE;
                        Native.PostMessage(h, msg, new IntPtr(1), IntPtr.Zero);
                    }
                    return;
                }
                if (first == "--quit") return;

                var host = new Host();
                string err = host.RegisterFromConfig();
                if (err != null) MessageBox.Show(err, "Jackamixer");
                else if (first == "--reload") MessageBox.Show("Hotkey set to " + Config.ReadHotkey() + ".", "Jackamixer");
                if (first == "--options") host.OpenOptions(new Size(380, 540), false);
                else if (!background) host.Toggle();
                Application.Run();
                host.DestroyHandle();
            }
        }

        // Renders a window to a PNG without user interaction (for checking the look).
        static void Snapshot(bool optionsWindow, string file, int page)
        {
            Form form;
            if (optionsWindow)
            {
                var dlg = new OptionsDialog(null, new Size(380, 540));
                dlg.ShowPage(page);
                form = dlg;
            }
            else form = new MixerForm(true, null);
            form.Show();
            var mixer = form as MixerForm;
            for (int i = 0; i < 30; i++) { if (mixer != null) mixer.Step(); Application.DoEvents(); Thread.Sleep(33); }
            using (var bmp = new Bitmap(form.Width, form.Height))
            {
                form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
                bmp.Save(file, ImageFormat.Png);
            }
            form.Close();
        }
    }
}
