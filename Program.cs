using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ClockStudio
{
    // ================= Settings =================
    static class Settings
    {
        static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClockStudio");
        static readonly string FilePath = Path.Combine(Dir, "settings.ini");
        static Dictionary<string, string> d = new Dictionary<string, string>();
        public static string NotesPath(string id) { return Path.Combine(Dir, id == "notes" ? "notes.txt" : "notes-" + id + ".txt"); }

        public static void Load()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                if (!File.Exists(FilePath)) return;
                foreach (string line in File.ReadAllLines(FilePath))
                {
                    int i = line.IndexOf('=');
                    if (i > 0) d[line.Substring(0, i)] = line.Substring(i + 1);
                }
            }
            catch { }
        }
        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                List<string> l = new List<string>();
                foreach (KeyValuePair<string, string> kv in d) l.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, l.ToArray());
            }
            catch { }
        }
        public static string Get(string k, string def) { string v; return d.TryGetValue(k, out v) ? v : def; }
        public static int GetInt(string k, int def) { int v; return int.TryParse(Get(k, ""), out v) ? v : def; }
        public static bool GetBool(string k, bool def) { string v = Get(k, ""); return v == "" ? def : v == "1"; }
        public static void Set(string k, string v) { d[k] = v; }
        public static void Set(string k, int v) { d[k] = v.ToString(CultureInfo.InvariantCulture); }
        public static void Set(string k, bool v) { d[k] = v ? "1" : "0"; }
        public static void ClearPositions()
        {
            List<string> del = new List<string>();
            foreach (string k in d.Keys) if (k.EndsWith(".x") || k.EndsWith(".y")) del.Add(k);
            foreach (string k in del) d.Remove(k);
        }
        public static void RemovePrefix(string prefix)
        {
            List<string> del = new List<string>();
            foreach (string k in d.Keys) if (k.StartsWith(prefix)) del.Add(k);
            foreach (string k in del) d.Remove(k);
        }
    }

    static class Native
    {
        [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
        public static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        public const uint SWP_NOSIZE = 1, SWP_NOMOVE = 2, SWP_NOACTIVATE = 0x10;
        [DllImport("kernel32.dll")] public static extern bool GlobalMemoryStatusEx(ref MEMSTATUS m);
        [StructLayout(LayoutKind.Sequential)]
        public struct MEMSTATUS
        {
            public uint dwLength, dwMemoryLoad;
            public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
        }
        [DllImport("kernel32.dll")] public static extern bool GetSystemTimes(out long idle, out long kernel, out long user);
        public const int WS_EX_TOOLWINDOW = 0x80;
        [DllImport("user32.dll", EntryPoint = "GetWindowLong")] public static extern int GetWindowLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLong")] public static extern int SetWindowLong(IntPtr h, int i, int v);
        [DllImport("kernel32.dll")] public static extern ulong GetTickCount64();

        [StructLayout(LayoutKind.Sequential)]
        struct AccentPolicy { public int AccentState; public int AccentFlags; public int GradientColor; public int AnimationId; }
        [StructLayout(LayoutKind.Sequential)]
        struct WinCompAttrData { public int Attribute; public IntPtr Data; public int SizeOfData; }
        [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WinCompAttrData data);

        // state: 0 off, 2 transparent gradient, 3 blur behind, 4 acrylic. color = ARGB
        public static void SetAccent(IntPtr hwnd, int state, Color c)
        {
            try
            {
                AccentPolicy ap = new AccentPolicy();
                ap.AccentState = state;
                ap.AccentFlags = 2;
                ap.GradientColor = (c.A << 24) | (c.B << 16) | (c.G << 8) | c.R;
                int size = Marshal.SizeOf(ap);
                IntPtr p = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(ap, p, false);
                WinCompAttrData data = new WinCompAttrData();
                data.Attribute = 19; data.Data = p; data.SizeOfData = size;
                SetWindowCompositionAttribute(hwnd, ref data);
                Marshal.FreeHGlobal(p);
            }
            catch { }
        }
    }

    static class Sty
    {
        public static readonly string[] ShapeNames = { "Rounded", "Rectangle", "Pill", "Ellipse / Circle", "Hexagon", "Octagon", "Diamond", "Cut corners", "Leaf", "Tab (top)", "Squircle", "Parallelogram", "Pentagon", "Trapezoid", "Pill (left)", "Tab (bottom)", "Ticket", "Shield", "Star", "Triangle", "Pill (right)", "Diagonal corners" };
    }

    // ================= Shapes =================
    static class Shapes
    {
        static GraphicsPath RR(RectangleF r, float tl, float tr, float br, float bl)
        {
            GraphicsPath p = new GraphicsPath();
            p.StartFigure();
            if (tl > 0) p.AddArc(r.X, r.Y, tl * 2, tl * 2, 180, 90); else p.AddLine(r.X, r.Y, r.X, r.Y);
            if (tr > 0) p.AddArc(r.Right - tr * 2, r.Y, tr * 2, tr * 2, 270, 90); else p.AddLine(r.Right, r.Y, r.Right, r.Y);
            if (br > 0) p.AddArc(r.Right - br * 2, r.Bottom - br * 2, br * 2, br * 2, 0, 90); else p.AddLine(r.Right, r.Bottom, r.Right, r.Bottom);
            if (bl > 0) p.AddArc(r.X, r.Bottom - bl * 2, bl * 2, bl * 2, 90, 90); else p.AddLine(r.X, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }
        public static GraphicsPath Make(int shape, int radius, RectangleF r)
        {
            float m = Math.Min(r.Width, r.Height);
            float rad = Math.Max(0, Math.Min(radius, m / 2f));
            GraphicsPath p;
            switch (shape)
            {
                case 1: return RR(r, 0, 0, 0, 0);
                case 2: rad = m / 2f; return RR(r, rad, rad, rad, rad);
                case 3: p = new GraphicsPath(); p.AddEllipse(r); return p;
                case 4:
                    p = new GraphicsPath();
                    p.AddPolygon(new PointF[] { new PointF(r.X + r.Width * 0.25f, r.Y), new PointF(r.X + r.Width * 0.75f, r.Y), new PointF(r.Right, r.Y + r.Height / 2), new PointF(r.X + r.Width * 0.75f, r.Bottom), new PointF(r.X + r.Width * 0.25f, r.Bottom), new PointF(r.X, r.Y + r.Height / 2) });
                    return p;
                case 5:
                    { float c = m * 0.29f; p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X + c, r.Y), new PointF(r.Right - c, r.Y), new PointF(r.Right, r.Y + c), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.X + c, r.Bottom), new PointF(r.X, r.Bottom - c), new PointF(r.X, r.Y + c) });
                      return p; }
                case 6:
                    p = new GraphicsPath();
                    p.AddPolygon(new PointF[] { new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right, r.Y + r.Height / 2), new PointF(r.X + r.Width / 2, r.Bottom), new PointF(r.X, r.Y + r.Height / 2) });
                    return p;
                case 7:
                    { float c = rad; p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X + c, r.Y), new PointF(r.Right - c, r.Y), new PointF(r.Right, r.Y + c), new PointF(r.Right, r.Bottom - c), new PointF(r.Right - c, r.Bottom), new PointF(r.X + c, r.Bottom), new PointF(r.X, r.Bottom - c), new PointF(r.X, r.Y + c) });
                      return p; }
                case 8: { float big = Math.Min(m / 2f, Math.Max(rad * 2, 10)); return RR(r, big, 4, big, 4); }
                case 9: return RR(r, rad, rad, 0, 0);
                case 10: rad = m * 0.38f; return RR(r, rad, rad, rad, rad);
                case 11:
                    { float o = Math.Min(r.Width * 0.3f, r.Height * 0.3f); p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X + o, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right - o, r.Bottom), new PointF(r.X, r.Bottom) });
                      return p; }
                case 12: p = new GraphicsPath(); p.AddPolygon(new PointF[] { new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right, r.Y + r.Height * 0.38f), new PointF(r.X + r.Width * 0.82f, r.Bottom), new PointF(r.X + r.Width * 0.18f, r.Bottom), new PointF(r.X, r.Y + r.Height * 0.38f) }); return p;
                case 13: p = new GraphicsPath(); p.AddPolygon(new PointF[] { new PointF(r.X + r.Width * 0.15f, r.Y), new PointF(r.Right - r.Width * 0.15f, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) }); return p;
                case 14: return RR(r, m / 2f, 0, 0, m / 2f);
                case 15: return RR(r, 0, 0, rad, rad);
                case 16:
                    { float n = Math.Min(r.Width, r.Height) * 0.12f; float my = r.Y + r.Height / 2; p = new GraphicsPath();
                      p.AddPolygon(new PointF[] { new PointF(r.X, r.Y), new PointF(r.Right, r.Y), new PointF(r.Right, my - n), new PointF(r.Right - n, my), new PointF(r.Right, my + n), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom), new PointF(r.X, my + n), new PointF(r.X + n, my), new PointF(r.X, my - n) });
                      return p; }
                case 17:
                    p = new GraphicsPath(); p.StartFigure();
                    p.AddLine(r.X, r.Y, r.Right, r.Y); p.AddLine(r.Right, r.Y, r.Right, r.Y + r.Height * 0.55f);
                    p.AddBezier(r.Right, r.Y + r.Height * 0.55f, r.Right, r.Y + r.Height * 0.85f, r.X + r.Width * 0.7f, r.Y + r.Height * 0.95f, r.X + r.Width / 2, r.Bottom);
                    p.AddBezier(r.X + r.Width / 2, r.Bottom, r.X + r.Width * 0.3f, r.Y + r.Height * 0.95f, r.X, r.Y + r.Height * 0.85f, r.X, r.Y + r.Height * 0.55f);
                    p.CloseFigure(); return p;
                case 18:
                    { PointF[] pts = new PointF[10]; float cx = r.X + r.Width / 2, cy = r.Y + r.Height * 0.53f;
                      for (int i = 0; i < 10; i++) { double a = -Math.PI / 2 + i * Math.PI / 5; float k = i % 2 == 0 ? 1f : 0.42f; pts[i] = new PointF(cx + (float)Math.Cos(a) * r.Width / 2 * k, cy + (float)Math.Sin(a) * r.Height / 2 * 1.04f * k); }
                      p = new GraphicsPath(); p.AddPolygon(pts); return p; }
                case 19: p = new GraphicsPath(); p.AddPolygon(new PointF[] { new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right, r.Bottom), new PointF(r.X, r.Bottom) }); return p;
                case 20: return RR(r, 0, m / 2f, m / 2f, 0);
                case 21: { float big = Math.Max(rad, 6); return RR(r, big, 0, big, 0); }
                default: return RR(r, rad, rad, rad, rad);
            }
        }
    }

    // ================= Base widget =================

    // ================= Properties (every option) =================
    class Prop
    {
        public string Cat, Key, Label, Kind, Def; public int Min, Max; public string[] Choices;
        public Prop(string cat, string key, string label, string kind, string def, int min, int max, string[] ch)
        { Cat = cat; Key = key; Label = label; Kind = kind; Def = def; Min = min; Max = max; Choices = ch; }
    }

    static class Props
    {
        public static List<Prop> All = new List<Prop>();
        static Dictionary<string, Prop> map = new Dictionary<string, Prop>();
        public static string Def(string k) { Prop p; return map.TryGetValue(k, out p) ? p.Def : ""; }
        static void A(string cat, string key, string label, string kind, string def, int min, int max, string[] ch)
        { Prop p = new Prop(cat, key, label, kind, def, min, max, ch); All.Add(p); map[key] = p; }
        static void I(string cat, string key, string label, int def, int min, int max) { A(cat, key, label, "int", def.ToString(), min, max, null); }
        static void B(string cat, string key, string label, bool def) { A(cat, key, label, "bool", def ? "1" : "0", 0, 1, null); }
        static void K(string cat, string key, string label, string hex) { A(cat, key, label, "color", hex, 0, 0, null); }
        static void Ch(string cat, string key, string label, int def, params string[] ch) { A(cat, key, label, "choice", def.ToString(), 0, ch.Length - 1, ch); }

        public static readonly string[] DateFmts = { "dddd, d MMMM yyyy", "ddd d MMM", "d/M/yyyy", "yyyy-MM-dd", "MMMM d", "dd.MM.yy", "dddd", "d MMMM", "MMM d, yyyy", "dddd d" };
        public static readonly string[] Seps = { ":", ".", " ", "-", "/", "|", "\u00B7", "_", "" };
        public static readonly string[] Rates = { "50 ms (smoothest)", "100 ms", "250 ms", "500 ms", "1 second", "2 seconds", "5 seconds", "30 seconds", "1 minute" };
        public static readonly int[] RateMs = { 50, 100, 250, 500, 1000, 2000, 5000, 30000, 60000 };

        static Props()
        {
            string[] tz = null;
            A("Clock", "style", "Clock style (63 styles)", "choice", "0", 0, Clocks.StyleNames.Length - 1, Clocks.StyleNames);
            A("Clock", "tz", "Time zone", "tz", "", 0, 0, tz);
            A("Clock", "label", "Label text (e.g. city)", "text", "", 0, 0, null);
            B("Clock", "h24", "24-hour time", true);
            B("Clock", "secs", "Show seconds / second hand", true);
            B("Clock", "date", "Show date", true);
            B("Clock", "ampm", "Show AM/PM (12-hour)", true);
            B("Clock", "lead0", "Leading zero on hour", true);
            Ch("Clock", "sep", "Separator between numbers", 0, ":", ".", "(space)", "-", "/", "|", "\u00B7 (dot)", "_", "(none)");
            B("Clock", "blink", "Blink the separator every second", false);
            Ch("Clock", "datefmt", "Date format", 0, "Thursday, 26 November 2026", "Thu 26 Nov", "26/11/2026", "2026-11-26", "November 26", "26.11.26", "Thursday", "26 November", "Nov 26, 2026", "Thursday 26");
            Ch("Clock", "case", "Letter case", 0, "Normal", "UPPERCASE", "lowercase");
            Ch("Clock", "rate", "Update speed (slower = lighter)", 4, Rates);
            B("Clock", "smooth", "Smooth sweeping hands (uses faster updates)", false);
            I("Clock", "rotate", "Rotate whole clock (degrees)", 0, -180, 180);
            B("Clock", "mirror", "Mirror horizontally", false);

            A("World clocks", "tz2", "Zone 1", "tz", "", 0, 0, null);
            A("World clocks", "l2", "Label 1", "text", "Local", 0, 0, null);
            A("World clocks", "tz3", "Zone 2", "tz", "Eastern Standard Time", 0, 0, null);
            A("World clocks", "l3", "Label 2", "text", "New York", 0, 0, null);
            A("World clocks", "tz4", "Zone 3", "tz", "GMT Standard Time", 0, 0, null);
            A("World clocks", "l4", "Label 3", "text", "London", 0, 0, null);
            A("World clocks", "tz5", "Zone 4", "tz", "Tokyo Standard Time", 0, 0, null);
            A("World clocks", "l5", "Label 4", "text", "Tokyo", 0, 0, null);

            A("Text", "font", "Font", "font", "Segoe UI", 0, 0, null);
            B("Text", "bold", "Bold", true);
            B("Text", "italic", "Italic", false);
            I("Text", "tscale", "Text size %", 100, 20, 400);
            I("Text", "dscale", "Date / small text size %", 100, 20, 400);
            Ch("Text", "effect", "Text effect", 0, "None", "Drop shadow", "Glow", "Outline only", "Outline + fill");
            Ch("Text", "talign", "Text alignment", 1, "Left", "Center", "Right");
            I("Text", "offx", "Move content left/right (px)", 0, -500, 500);
            I("Text", "offy", "Move content up/down (px)", 0, -500, 500);
            I("Text", "fxsize", "Effect strength", 100, 10, 400);
            I("Text", "segthick", "7-segment thickness %", 100, 40, 200);
            I("Text", "segslant", "7-segment slant", 0, 0, 30);

            Ch("Analog", "dial", "Dial marks (0=style default)", 0, "Use style default", "None", "All ticks", "Hour ticks only", "Dots", "Numbers 1-12", "Numbers + ticks", "Quarter numbers + ticks", "Roman + ticks", "Batons", "Ticks + dots");
            Ch("Analog", "hand", "Hand shape (0=style default)", 0, "Use style default", "Classic", "Thin line", "Baton", "Arrow", "Skeleton outline", "Leaf");
            I("Analog", "hourlen", "Hour hand length %", 50, 10, 95);
            I("Analog", "minlen", "Minute hand length %", 76, 10, 98);
            I("Analog", "seclen", "Second hand length %", 86, 10, 100);
            I("Analog", "hourw", "Hour hand width", 6, 1, 30);
            I("Analog", "minw", "Minute hand width", 4, 1, 30);
            I("Analog", "secw", "Second hand width", 2, 1, 20);
            I("Analog", "tail", "Hand tail length %", 10, 0, 40);
            I("Analog", "ticklen", "Major tick length %", 13, 2, 40);
            I("Analog", "ticklen2", "Minor tick length %", 6, 1, 30);
            I("Analog", "tickw", "Major tick width", 3, 1, 15);
            I("Analog", "tickw2", "Minor tick width", 1, 1, 8);
            I("Analog", "numsize", "Numbers size %", 100, 30, 300);
            I("Analog", "numdist", "Numbers distance from center %", 72, 30, 95);
            I("Analog", "dotsize", "Center dot size", 5, 0, 40);
            B("Analog", "face", "Fill the dial", true);
            I("Analog", "facea", "Dial fill opacity", 70, 0, 255);
            I("Analog", "ringw", "Dial ring width", 2, 0, 30);

            A("Size and shape", "shape", "Shape", "choice", "0", 0, Sty.ShapeNames.Length - 1, Sty.ShapeNames);
            I("Size and shape", "w", "Width (exact px)", 200, 60, 3000);
            I("Size and shape", "h", "Height (exact px)", 200, 40, 3000);
            I("Size and shape", "x", "Position X (px)", 100, -5000, 10000);
            I("Size and shape", "y", "Position Y (px)", 100, -5000, 10000);
            I("Size and shape", "radius", "Corner radius (px)", 18, 0, 400);
            I("Size and shape", "cscale", "Clock size inside window %", 100, 20, 400);
            I("Size and shape", "pad", "Inner padding (px)", 4, 0, 200);

            Ch("Background", "bgmode", "Background type", 0, "Solid", "Transparent tint", "Blur (glass)", "Acrylic (frosted glass)");
            I("Background", "bgalpha", "Tint strength (glass modes)", 150, 0, 255);
            I("Background", "opacity", "Window opacity % (Solid)", 96, 15, 100);
            Ch("Background", "grad", "Gradient (Solid)", 0, "None", "Vertical", "Horizontal", "Diagonal");
            I("Background", "borderw", "Border width", 0, 0, 30);
            B("Background", "bgoff", "No background at all (text/graphics only)", false);

            K("Colors", "bg", "Background", "181A20");
            K("Colors", "bg2", "Gradient second color", "30344A");
            K("Colors", "hour", "Hours (digits / hand / ring)", "F0F0F5");
            K("Colors", "min", "Minutes (digits / hand / ring)", "F0F0F5");
            K("Colors", "sec", "Seconds (digits / hand / ring)", "50A0FF");
            K("Colors", "sepc", "Separator (colon)", "969BAA");
            K("Colors", "datec", "Date / small text", "969BAA");
            K("Colors", "tick", "Tick marks", "C8CCD8");
            K("Colors", "num", "Dial numbers", "F0F0F5");
            K("Colors", "facec", "Dial fill", "3C404C");
            K("Colors", "ringc", "Dial ring", "5A5F70");
            K("Colors", "dotc", "Center dot", "50A0FF");
            K("Colors", "track", "Unlit parts / ring track", "3C404C");
            K("Colors", "accent", "Accent (bands, markers, sun, moon)", "50A0FF");
            K("Colors", "borderc", "Border", "5A5F70");
            K("Colors", "fx", "Effect color (glow / outline / shadow)", "50A0FF");
            K("Colors", "labelc", "Label text", "969BAA");

            B("Behavior", "top", "Always on top", false);
            B("Behavior", "locked", "Lock position and size", false);
            B("Behavior", "through", "Click-through (mouse passes through; use tray menu to change)", false);
        }
    }

    // ================= Themes =================
    static class Themes
    {
        public static readonly string[] Data = {
            "Dark|0|181A20|30344A|F0F0F5|F0F0F5|50A0FF|969BAA|969BAA|C8CCD8|F0F0F5|3C404C|5A5F70|50A0FF|3C404C|50A0FF",
            "Light|0|F5F5F8|DCDCE6|1E2028|1E2028|0078D7|6E7382|6E7382|505560|1E2028|E0E2EA|B4B8C4|0078D7|D0D3DC|0078D7",
            "Neon|0|0A0A16|240038|00FFD2|FF00C8|FFEE00|009682|00B4A0|00FFD2|00FFD2|0F0F28|FF00C8|FF00C8|20204A|FF00C8",
            "Terminal|0|000C00|001800|46FF6E|46FF6E|46FF6E|1EA03C|1EA03C|46FF6E|46FF6E|001400|1EA03C|46FF6E|002800|46FF6E",
            "Amber|0|140A00|281400|FFB000|FFB000|FFE070|A06400|A06400|FFB000|FFB000|1E1000|A06400|FFB000|301A00|FFB000",
            "Ocean|0|062850|0082A0|FFFFFF|B4E1F0|78F0E6|B4E1F0|B4E1F0|FFFFFF|FFFFFF|0A3C6E|78F0E6|78F0E6|0A4F80|78F0E6",
            "Forest|0|0C281C|286440|E6FAEB|A0F078|FFE878|A0CDAF|A0CDAF|E6FAEB|E6FAEB|103824|A0F078|A0F078|1C5A38|A0F078",
            "Sunset|0|2A1434|C84632|FFE8D7|FFAA64|FF6450|CDA0AA|FFD7C0|FFE8D7|FFE8D7|3C1E48|FF7850|FF7850|52284E|FF7850",
            "Rose gold|0|3C262A|785050|FFEBE1|FFBEAA|FFD7C8|E1B9AF|FFD7C8|FFEBE1|FFEBE1|4C3034|FFBEAA|FFBEAA|603C40|FFBEAA",
            "Dracula|0|282A36|44475A|F8F8F2|BD93F9|FF79C6|8B94BE|6272A4|F8F8F2|F8F8F2|343746|BD93F9|BD93F9|44475A|BD93F9",
            "Nord|0|2E3440|3B4252|ECEFF4|88C0D0|BF616A|81A1C1|81A1C1|D8DEE9|ECEFF4|3B4252|88C0D0|88C0D0|434C5E|88C0D0",
            "Solarized dark|0|002B36|073642|EEE8D5|B58900|DC322F|839496|93A1A1|EEE8D5|EEE8D5|073642|B58900|B58900|0A4A5A|B58900",
            "Solarized light|0|FDF6E3|EEE8D5|586E75|268BD2|DC322F|93A1A1|657B83|586E75|586E75|EEE8D5|268BD2|268BD2|DDD6C1|268BD2",
            "Mono black|0|000000|202020|FFFFFF|FFFFFF|FFFFFF|969696|969696|FFFFFF|FFFFFF|141414|FFFFFF|FFFFFF|282828|FFFFFF",
            "Mono white|0|FFFFFF|E0E0E0|000000|000000|000000|6E6E6E|6E6E6E|000000|000000|F0F0F0|000000|000000|DCDCDC|000000",
            "Cyberpunk|0|140628|3C0A46|FFEE00|00E6FF|FF008C|00E6FF|00E6FF|FFEE00|FFEE00|1E0A32|00E6FF|FF008C|3C1450|FF008C",
            "Gold|0|1A1408|3A2E10|FFD870|FFD870|FFF0B0|C8A850|C8A850|FFD870|FFD870|241C0A|C8A850|FFD870|3A2E10|FFD870",
            "Aqua glass|3|005070|005070|FFFFFF|C8F0FA|78FFF0|C8F0FA|C8F0FA|FFFFFF|FFFFFF|006080|78FFF0|78FFF0|0A6A8A|78FFF0",
            "Crimson glass|3|5A0014|5A0014|FFFFFF|FFC8CD|FF6E78|FFC8CD|FFC8CD|FFFFFF|FFFFFF|700020|FF6E78|FF6E78|80182C|FF6E78",
            "Blur glass|2|000000|000000|FFFFFF|FFFFFF|FFFFFF|D2D7E1|D2D7E1|FFFFFF|FFFFFF|202020|FFFFFF|FFFFFF|404040|FFFFFF",
            "Clear (no background)|1|000000|000000|FFFFFF|FFFFFF|FFFFFF|DCDCE6|DCDCE6|FFFFFF|FFFFFF|202020|FFFFFF|FFFFFF|404040|FFFFFF"
        };
        static readonly string[] Keys = { "bg", "bg2", "hour", "min", "sec", "sepc", "datec", "tick", "num", "facec", "ringc", "dotc", "track", "accent" };
        public static string Name(int i) { return Data[i].Split('|')[0]; }
        public static void Apply(ClockForm c, int i)
        {
            string[] p = Data[i].Split('|');
            c.Set("bgmode", p[1]);
            c.Set("bgalpha", p[0].StartsWith("Clear") ? "0" : "150");
            for (int k = 0; k < Keys.Length; k++) c.Set(Keys[k], p[2 + k]);
            c.Set("labelc", p[8]); c.Set("fx", p[15]); c.Set("borderc", p[12]);
        }
    }

    // ================= Style names / sizes =================
    static class Clocks
    {
        public static readonly string[] StyleNames = {
            // analog 0-19
            "Analog: Classic numbers", "Analog: Minimal ticks", "Analog: Roman", "Analog: Quarter numbers", "Analog: Baton marks",
            "Analog: Dots", "Analog: Bare (no marks)", "Analog: Skeleton hands", "Analog: Arrow hands", "Analog: Leaf hands",
            "Analog: Big numbers", "Analog: Railway", "Analog: Pilot", "Analog: Dive", "Analog: Art deco",
            "Analog: Modern", "Analog: Station", "Analog: Elegant", "Analog: Sport", "Analog: Thin ring",
            // digital 20-39
            "Digital: HH:MM", "Digital: HH:MM:SS", "Digital: Stacked HH / MM", "Digital: Stacked with seconds", "Digital: Time + date",
            "Digital: 12-hour with AM/PM", "Digital: Vertical digits", "Digital: One line, date + time", "7-segment: HH:MM", "7-segment: with seconds",
            "Flip cards: HH MM", "Flip cards: with seconds", "Big seconds", "Outline text", "Glow text",
            "Roman numeral digits", "Words: It's half past ten", "Fuzzy: almost noon", "Weekday + time", "Time + day progress bar",
            // graphic 40-62
            "Binary dots", "Rings (hours, minutes, seconds)", "Rings (hours, minutes)", "Bars: horizontal", "Bars: vertical",
            "Dot ring (60 dots)", "Orbit planets", "Sun arc (day)", "Day progress %", "Hex color clock",
            "Decimal time (10h/100m)", "Unix timestamp", "Calendar card", "Big day + month", "Week blocks",
            "Year progress ring", "World clocks (4 cities)", "Dual clock (2 zones)", "Moon phase + time", "12-segment hour ring",
            "Seconds circle + digits", "Pie clock", "24-hour segmented bar"
        };
        static readonly string[] Sizes = {
            "200x200","200x200","200x200","200x200","200x200","200x200","200x200","200x200","200x200","200x200",
            "200x200","200x200","200x200","200x200","200x200","200x200","200x200","200x200","200x200","200x200",
            "240x110","280x100","170x200","170x230","280x130","260x120","110x290","320x60","300x120","380x120",
            "260x120","360x120","200x200","300x110","300x110","320x100","300x120","300x110","260x110","300x110",
            "260x150","200x200","200x200","260x120","200x150","200x200","220x220","260x150","220x120","240x110",
            "240x110","200x200","200x130","180x200","320x110","200x200","300x180","300x120","200x230","200x200",
            "200x200","200x200","340x90"
        };
        public static int[] Size(int style) { string[] p = Sizes[Math.Max(0, Math.Min(Sizes.Length - 1, style))].Split('x'); return new[] { int.Parse(p[0]), int.Parse(p[1]) }; }
        // analog preset: dial, hand
        public static readonly int[][] Analog = {
            new[]{5,1}, new[]{3,2}, new[]{8,6}, new[]{7,1}, new[]{9,3}, new[]{4,2}, new[]{1,2}, new[]{2,5}, new[]{3,4}, new[]{2,6},
            new[]{5,1}, new[]{2,3}, new[]{7,4}, new[]{4,3}, new[]{8,5}, new[]{9,2}, new[]{3,3}, new[]{10,6}, new[]{6,4}, new[]{1,1}
        };
    }

    // ================= Clock window =================
    class ClockForm : Form
    {
        public string Id;
        Dictionary<string, string> v = new Dictionary<string, string>();
        Dictionary<string, Color> colCache = new Dictionary<string, Color>();
        Timer tick = new Timer();
        bool dragging, moved; Point dragStart, formStart; int edge; Size resStart; Point resLoc;
        static Dictionary<string, FontFamily> fams = new Dictionary<string, FontFamily>();
        static readonly StringFormat Typo = MakeTypo();
        static StringFormat MakeTypo() { StringFormat f = new StringFormat(StringFormat.GenericTypographic); f.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoWrap; return f; }

        public ClockForm(string id, int defStyle, int idx)
        {
            Id = id;
            FormBorderStyle = FormBorderStyle.None; ShowInTaskbar = false; StartPosition = FormStartPosition.Manual;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            foreach (Prop p in Props.All) { string s = Settings.Get(id + "." + p.Key, null); if (s != null) v[p.Key] = s; }
            if (!v.ContainsKey("style")) { v["style"] = defStyle.ToString(); int[] sz = Clocks.Size(defStyle); v["w"] = sz[0].ToString(); v["h"] = sz[1].ToString(); }
            if (!v.ContainsKey("x")) { v["x"] = (60 + (idx % 4) * 260).ToString(); v["y"] = (60 + (idx / 4) * 260).ToString(); }
            tick.Tick += delegate { if (Visible) Invalidate(); };
        }
        // ---- config ----
        public string S(string k) { string x; return v.TryGetValue(k, out x) ? x : Props.Def(k); }
        public int I(string k) { int r; return int.TryParse(S(k), NumberStyles.Integer, CultureInfo.InvariantCulture, out r) ? r : 0; }
        public bool B(string k) { return S(k) == "1"; }
        public bool Has(string k) { return v.ContainsKey(k); }
        public Color C(string k)
        {
            Color c; if (colCache.TryGetValue(k, out c)) return c;
            try { c = Color.FromArgb(255, Color.FromArgb(Convert.ToInt32(S(k), 16))); } catch { c = Color.Gray; }
            c = Color.FromArgb(255, c.R, c.G, c.B); colCache[k] = c; return c;
        }
        public void Set(string k, string val) { v[k] = val; colCache.Remove(k); Settings.Set(Id + "." + k, val); }
        public void SetColor(string k, Color c) { Set(k, (c.R * 65536 + c.G * 256 + c.B).ToString("X6")); }
        public void SaveAll() { foreach (KeyValuePair<string, string> kv in v) Settings.Set(Id + "." + kv.Key, kv.Value); Settings.Save(); }
        public int Style { get { return Math.Max(0, Math.Min(Clocks.StyleNames.Length - 1, I("style"))); } }

        protected override CreateParams CreateParams
        {
            get { CreateParams cp = base.CreateParams; cp.ExStyle |= Native.WS_EX_TOOLWINDOW; if (B("through")) cp.ExStyle |= 0x20 | 0x80000; return cp; }
        }
        protected override bool ShowWithoutActivation { get { return true; } }
        public void Init() { BuildMenu(); Restyle(); }

        public void Restyle()
        {
            colCache.Clear();
            SaveAll();
            ClientSize = new Size(Math.Max(60, I("w")), Math.Max(40, I("h")));
            Location = new Point(I("x"), I("y"));
            using (GraphicsPath p = Shapes.Make(I("shape"), I("radius"), new RectangleF(0, 0, Width, Height))) Region = new Region(p);
            bool glass = I("bgmode") > 0;
            BackColor = glass ? Color.Black : C("bg");
            TopMost = B("top");
            Opacity = glass ? 1.0 : Math.Max(0.15, Math.Min(1.0, I("opacity") / 100.0));
            if (IsHandleCreated)
            {
                ApplyAccent();
                try
                {
                    int ex = Native.GetWindowLong(Handle, -20);
                    ex = B("through") ? (ex | 0x20 | 0x80000) : (ex & ~0x20);
                    Native.SetWindowLong(Handle, -20, ex);
                }
                catch { }
            }
            int rate = Props.RateMs[Math.Max(0, Math.Min(Props.RateMs.Length - 1, I("rate")))];
            if (B("smooth") && Style < 20) rate = Math.Min(rate, 50);
            tick.Interval = rate; tick.Start();
            Invalidate();
        }
        void ApplyAccent()
        {
            int m = I("bgmode");
            if (m == 0) Native.SetAccent(Handle, 0, Color.Black);
            else Native.SetAccent(Handle, m == 1 ? 2 : (m == 2 ? 3 : 4), Color.FromArgb(I("bgalpha"), C("bg")));
        }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); ApplyAccent(); }

        // ---- mouse ----
        int HitEdge(Point p)
        {
            if (B("locked")) return 0;
            int m = 8, e = 0;
            if (p.X < m) e |= 1; if (p.X >= Width - m) e |= 2; if (p.Y < m) e |= 4; if (p.Y >= Height - m) e |= 8;
            return e;
        }
        static Cursor CursorFor(int e)
        {
            if (e == 1 || e == 2) return Cursors.SizeWE; if (e == 4 || e == 8) return Cursors.SizeNS;
            if (e == 5 || e == 10) return Cursors.SizeNWSE; if (e == 6 || e == 9) return Cursors.SizeNESW; return Cursors.Default;
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                dragStart = Cursor.Position; formStart = Location; moved = false; edge = HitEdge(e.Location); resStart = Size; resLoc = Location;
                dragging = !B("locked") && edge == 0;
            }
            base.OnMouseDown(e);
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                Point c = Cursor.Position; int dx = c.X - dragStart.X, dy = c.Y - dragStart.Y;
                if (Math.Abs(dx) + Math.Abs(dy) > 4) moved = true;
                if (edge != 0 && moved)
                {
                    int nw = resStart.Width, nh = resStart.Height, nx = resLoc.X, ny = resLoc.Y;
                    if ((edge & 2) != 0) nw += dx; if ((edge & 1) != 0) { nw -= dx; nx += dx; }
                    if ((edge & 8) != 0) nh += dy; if ((edge & 4) != 0) { nh -= dy; ny += dy; }
                    if (nw < 60) { if ((edge & 1) != 0) nx -= 60 - nw; nw = 60; }
                    if (nh < 40) { if ((edge & 4) != 0) ny -= 40 - nh; nh = 40; }
                    v["w"] = nw.ToString(); v["h"] = nh.ToString(); v["x"] = nx.ToString(); v["y"] = ny.ToString();
                    Location = new Point(nx, ny); ClientSize = new Size(nw, nh);
                    using (GraphicsPath p = Shapes.Make(I("shape"), I("radius"), new RectangleF(0, 0, Width, Height))) Region = new Region(p);
                    Invalidate();
                }
                else if (dragging && moved) Location = new Point(formStart.X + dx, formStart.Y + dy);
            }
            else Cursor = CursorFor(HitEdge(e.Location));
            base.OnMouseMove(e);
        }
        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (moved && (edge != 0 || dragging)) { v["x"] = Left.ToString(); v["y"] = Top.ToString(); SaveAll(); CustomizeForm.Refresh(this); }
            dragging = false; edge = 0;
            base.OnMouseUp(e);
        }
        protected override void OnShown(EventArgs e) { base.OnShown(e); SendBack(); }
        protected override void OnActivated(EventArgs e) { base.OnActivated(e); SendBack(); }
        void SendBack() { if (!B("top")) Native.SetWindowPos(Handle, Native.HWND_BOTTOM, 0, 0, 0, 0, Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE); }

        public void SetShown(bool vis) { Set("visible", vis ? "1" : "0"); Settings.Save(); if (vis) { Show(); SendBack(); } else Hide(); }

        void BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Items.Add("Customize everything...", null, delegate { CustomizeForm.Open(this); });
            ToolStripMenuItem st = new ToolStripMenuItem("Clock style");
            string[] groups = { "Analog", "Digital / text", "Graphic" }; int[] starts = { 0, 20, 40, Clocks.StyleNames.Length };
            for (int gI = 0; gI < 3; gI++)
            {
                ToolStripMenuItem sub = new ToolStripMenuItem(groups[gI]);
                for (int i = starts[gI]; i < starts[gI + 1]; i++) { int ii = i; sub.DropDownItems.Add(Clocks.StyleNames[i], null, delegate { ChangeStyle(ii); }); }
                st.DropDownItems.Add(sub);
            }
            m.Items.Add(st);
            ToolStripMenuItem th = new ToolStripMenuItem("Quick theme");
            for (int i = 0; i < Themes.Data.Length; i++) { int ii = i; th.DropDownItems.Add(Themes.Name(i), null, delegate { Themes.Apply(this, ii); Restyle(); }); }
            m.Items.Add(th);
            ToolStripMenuItem op = new ToolStripMenuItem("Opacity");
            foreach (int pct in new int[] { 20, 30, 40, 50, 60, 70, 80, 90, 100 })
            { int pp = pct; op.DropDownItems.Add(pp + "%", null, delegate { if (I("bgmode") == 0) Set("opacity", pp.ToString()); else Set("bgalpha", (pp * 255 / 100).ToString()); Restyle(); }); }
            m.Items.Add(op);
            ToolStripMenuItem sz = new ToolStripMenuItem("Size (whole clock)");
            foreach (int pct in new int[] { 40, 50, 60, 70, 85, 100, 115, 130, 150, 175, 200, 250, 300 })
            { int pp = pct; sz.DropDownItems.Add(pp + "%", null, delegate { int[] d = Clocks.Size(Style); Set("w", (d[0] * pp / 100).ToString()); Set("h", (d[1] * pp / 100).ToString()); Restyle(); CustomizeForm.Refresh(this); }); }
            m.Items.Add(sz);
            ToolStripMenuItem lk = new ToolStripMenuItem("Lock position"); lk.CheckOnClick = true;
            lk.Click += delegate { Set("locked", lk.Checked ? "1" : "0"); Restyle(); };
            ToolStripMenuItem top = new ToolStripMenuItem("Always on top"); top.CheckOnClick = true;
            top.Click += delegate { Set("top", top.Checked ? "1" : "0"); Restyle(); };
            m.Opening += delegate { lk.Checked = B("locked"); top.Checked = B("top"); };
            m.Items.Add(top); m.Items.Add(lk);
            m.Items.Add("Duplicate this clock", null, delegate { Program.Duplicate(this); });
            ToolStripMenuItem cm = new ToolStripMenuItem("Clocks: show / restore / add"); Program.AttachClocksMenu(cm); m.Items.Add(cm);
            m.Items.Add(new ToolStripSeparator());
            m.Items.Add("Hide", null, delegate { SetShown(false); });
            m.Items.Add("Delete this clock (can restore)", null, delegate { Program.Delete(this); });
            m.Items.Add("Exit all clocks", null, delegate { Program.ExitAll(); });
            ContextMenuStrip = m;
        }
        public void ChangeStyle(int s)
        {
            Set("style", s.ToString()); int[] d = Clocks.Size(s); Set("w", d[0].ToString()); Set("h", d[1].ToString());
            Restyle(); CustomizeForm.Refresh(this);
        }

        // ================= Painting =================
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias; g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            int mode = I("bgmode"); bool nobg = B("bgoff");
            g.Clear(mode > 0 || nobg ? Color.Black : C("bg"));
            if (!nobg && mode == 0 && I("grad") > 0)
            {
                int gr = I("grad"); float ang = gr == 1 ? 90f : (gr == 2 ? 0f : 45f);
                using (LinearGradientBrush gb = new LinearGradientBrush(new Rectangle(0, 0, Width + 1, Height + 1), C("bg"), C("bg2"), ang)) g.FillRectangle(gb, 0, 0, Width + 1, Height + 1);
            }
            if (nobg && mode == 0) { /* solid black with no bg: make fully transparent via TransparencyKey not used; keep dark */ }
            int bw = I("borderw");
            if (bw > 0)
            {
                float hw = bw / 2f;
                using (GraphicsPath bp = Shapes.Make(I("shape"), I("radius"), new RectangleF(hw, hw, Width - bw, Height - bw)))
                using (Pen pen = new Pen(C("borderc"), bw)) { pen.LineJoin = LineJoin.Round; g.DrawPath(pen, bp); }
            }
            int pad = I("pad"); float W = Width - 2 * pad, H = Height - 2 * pad;
            if (W < 10 || H < 10) return;
            DateTime n = Now(S("tz"));
            if (B("smooth") && Style < 20) n = n.AddMilliseconds(0);
            g.TranslateTransform(pad + I("offx") + W / 2, pad + I("offy") + H / 2);
            if (I("rotate") != 0) g.RotateTransform(I("rotate"));
            float cs = I("cscale") / 100f; g.ScaleTransform(B("mirror") ? -cs : cs, cs);
            g.TranslateTransform(-W / 2, -H / 2);
            try { DrawStyle(g, n, W, H, Style); } catch { }
            string lab = S("label");
            if (lab != "" && Style != 56)
            {
                List<P> lp = new List<P>(); lp.Add(new P(Cs(lab), C("labelc")));
                Run(g, new RectangleF(0, 0, W, H * 0.13f), 16 * I("dscale") / 100f, lp, 1, false);
            }
        }
        static DateTime Now(string tz)
        {
            if (tz == "") return DateTime.Now;
            try { return TimeZoneInfo.ConvertTime(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(tz)); } catch { return DateTime.Now; }
        }

        // ---- text engine ----
        class P { public string S; public Color C; public P(string s, Color c) { S = s; C = c; } }
        string Cs(string s) { int c = I("case"); return c == 1 ? s.ToUpper() : (c == 2 ? s.ToLower() : s); }
        FontFamily Fam()
        {
            string n = S("font"); FontFamily f;
            if (fams.TryGetValue(n, out f)) return f;
            try { f = new FontFamily(n); } catch { f = new FontFamily("Segoe UI"); }
            fams[n] = f; return f;
        }
        FontStyle FS() { FontStyle f = FontStyle.Regular; if (B("bold")) f |= FontStyle.Bold; if (B("italic")) f |= FontStyle.Italic; return f; }
        float Measure(Graphics g, string s, FontFamily ff, FontStyle fs, float sz)
        {
            if (s == "") return 0;
            using (Font f = new Font(ff, sz, fs, GraphicsUnit.Pixel)) return g.MeasureString(s, f, PointF.Empty, Typo).Width;
        }
        // Draw parts in a row, fitted into r (then scaled by text size %). align: -1 use setting
        void Run(Graphics g, RectangleF r, float maxSize, List<P> parts, int align, bool big)
        {
            FontFamily ff = Fam(); FontStyle fs = FS();
            if (!ff.IsStyleAvailable(fs)) fs = FontStyle.Regular;
            float lineK = (float)ff.GetLineSpacing(fs) / ff.GetEmHeight(fs);
            float sz = maxSize;
            for (int it = 0; it < 60 && sz > 5; it++)
            {
                float tw = 0; foreach (P p in parts) tw += Measure(g, p.S, ff, fs, sz);
                if (tw <= r.Width && sz * lineK <= r.Height) break;
                sz *= 0.93f;
            }
            if (big) sz *= I("tscale") / 100f;
            float total = 0; foreach (P p in parts) total += Measure(g, p.S, ff, fs, sz);
            int al = align < 0 ? I("talign") : align;
            float x = al == 0 ? r.X : (al == 2 ? r.Right - total : r.X + (r.Width - total) / 2);
            float y = r.Y + (r.Height - sz * lineK) / 2;
            foreach (P p in parts)
            {
                float w = Measure(g, p.S, ff, fs, sz);
                if (p.S.Trim() != "")
                    using (GraphicsPath path = new GraphicsPath()) { path.AddString(p.S, ff, (int)fs, sz, new PointF(x, y), Typo); PaintPath(g, path, p.C, sz); }
                x += w;
            }
        }
        void PaintPath(Graphics g, GraphicsPath path, Color col, float sz)
        {
            int eff = I("effect"); float k = I("fxsize") / 100f; Color fx = C("fx");
            if (eff == 1)
            {
                using (Matrix m = new Matrix()) using (GraphicsPath sp = (GraphicsPath)path.Clone())
                { m.Translate(sz * 0.05f * k + 1, sz * 0.05f * k + 1); sp.Transform(m); using (SolidBrush sb = new SolidBrush(Color.FromArgb(150, fx))) g.FillPath(sb, sp); }
            }
            else if (eff == 2)
            {
                float[] ws = { 10, 6, 3 };
                foreach (float w in ws) using (Pen gp = new Pen(Color.FromArgb(45, fx), w * k * Math.Max(0.4f, sz / 60f))) { gp.LineJoin = LineJoin.Round; g.DrawPath(gp, path); }
            }
            if (eff == 3) { using (Pen op = new Pen(col, Math.Max(1.2f, sz / 30f * k))) { op.LineJoin = LineJoin.Round; g.DrawPath(op, path); } return; }
            using (SolidBrush b = new SolidBrush(col)) g.FillPath(b, path);
            if (eff == 4) using (Pen op = new Pen(fx, Math.Max(1.2f, sz / 35f * k))) { op.LineJoin = LineJoin.Round; g.DrawPath(op, path); }
        }
        void T(Graphics g, string s, RectangleF r, Color c, float maxSize, bool big)
        {
            List<P> l = new List<P>(); l.Add(new P(s, c)); Run(g, r, maxSize, l, -1, big);
        }
        void D(Graphics g, string s, RectangleF r, float maxSize) { List<P> l = new List<P>(); l.Add(new P(Cs(s), C("datec"))); Run(g, r, maxSize * I("dscale") / 100f, l, -1, false); }
        string HStr(DateTime n) { int h = n.Hour; if (!B("h24")) { h %= 12; if (h == 0) h = 12; } return B("lead0") ? h.ToString("00") : h.ToString(); }
        string Sp(DateTime n) { string sp = Props.Seps[Math.Max(0, Math.Min(Props.Seps.Length - 1, I("sep")))]; if (B("blink") && n.Second % 2 == 1) return new string(' ', Math.Max(1, sp.Length)); return sp; }
        string Ap(DateTime n) { return (!B("h24") && B("ampm")) ? (n.Hour < 12 ? " AM" : " PM") : ""; }
        string DateS(DateTime n) { return n.ToString(Props.DateFmts[Math.Max(0, Math.Min(Props.DateFmts.Length - 1, I("datefmt")))], CultureInfo.InvariantCulture); }
        List<P> TimeParts(DateTime n, bool secs, bool ap)
        {
            List<P> l = new List<P>();
            l.Add(new P(HStr(n), C("hour"))); l.Add(new P(Sp(n), C("sepc"))); l.Add(new P(n.Minute.ToString("00"), C("min")));
            if (secs) { l.Add(new P(Sp(n), C("sepc"))); l.Add(new P(n.Second.ToString("00"), C("sec"))); }
            if (ap && Ap(n) != "") l.Add(new P(Ap(n), C("datec")));
            return l;
        }

        // ---- primitives ----
        void ArcC(Graphics g, RectangleF r, double frac, float width, Color c)
        {
            using (Pen bg = new Pen(Color.FromArgb(120, C("track")), width)) g.DrawArc(bg, r, -90, 360);
            if (frac > 0.003) using (Pen p = new Pen(c, width)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawArc(p, r, -90, (float)(360 * Math.Min(1, frac))); }
        }
        void BarC(Graphics g, RectangleF r, double frac, Color c)
        {
            frac = Math.Max(0, Math.Min(1, frac));
            using (SolidBrush bg = new SolidBrush(Color.FromArgb(140, C("track")))) g.FillRectangle(bg, r);
            using (SolidBrush fg = new SolidBrush(c)) g.FillRectangle(fg, r.X, r.Y, (float)(r.Width * frac), r.Height);
        }
        PointF Pt(float cx, float cy, double deg, float len) { double a = deg * Math.PI / 180; return new PointF(cx + (float)(Math.Sin(a) * len), cy - (float)(Math.Cos(a) * len)); }
        void Hand(Graphics g, float cx, float cy, double deg, float len, float w, Color c, int style, float tail)
        {
            double a = deg * Math.PI / 180; float dx = (float)Math.Sin(a), dy = -(float)Math.Cos(a), px = -dy, py = dx;
            Func<float, float, PointF> P2 = delegate (float l, float s) { return new PointF(cx + dx * l + px * s, cy + dy * l + py * s); };
            if (style == 1 || style == 2 || style == 0)
            {
                float ww = style == 2 ? Math.Max(1f, w * 0.45f) : w;
                using (Pen p = new Pen(c, ww)) { p.StartCap = LineCap.Round; p.EndCap = LineCap.Round; g.DrawLine(p, P2(-tail, 0), P2(len, 0)); }
                return;
            }
            PointF[] pts;
            float hw = w / 2f;
            if (style == 3) pts = new[] { P2(-tail, -hw), P2(len, -hw), P2(len, hw), P2(-tail, hw) };
            else if (style == 4) pts = new[] { P2(-tail * 0.6f, -hw * 0.6f), P2(0, -hw * 1.5f), P2(len, 0), P2(0, hw * 1.5f), P2(-tail * 0.6f, hw * 0.6f) };
            else if (style == 5) { pts = new[] { P2(-tail, -hw), P2(len, -hw), P2(len, hw), P2(-tail, hw) }; using (Pen sp = new Pen(c, Math.Max(1.2f, w * 0.2f))) { sp.LineJoin = LineJoin.Miter; g.DrawPolygon(sp, pts); } return; }
            else pts = new[] { P2(-tail * 0.5f, 0), P2(len * 0.3f, -hw * 1.6f), P2(len, 0), P2(len * 0.3f, hw * 1.6f) };
            using (SolidBrush b = new SolidBrush(c)) g.FillPolygon(b, pts);
        }
        static readonly string[] Rom = { "XII", "I", "II", "III", "IIII", "V", "VI", "VII", "VIII", "IX", "X", "XI" };

        void Analog(Graphics g, DateTime n, float W, float H, int dial, int hand)
        {
            if (I("dial") > 0) dial = I("dial");
            if (I("hand") > 0) hand = I("hand");
            float R = Math.Min(W, H) / 2 - 4; float cx = W / 2, cy = H / 2;
            if (B("face")) using (SolidBrush fb = new SolidBrush(Color.FromArgb(I("facea"), C("facec")))) g.FillEllipse(fb, cx - R, cy - R, 2 * R, 2 * R);
            if (I("ringw") > 0) using (Pen rp = new Pen(C("ringc"), I("ringw"))) g.DrawEllipse(rp, cx - R, cy - R, 2 * R, 2 * R);
            float tl = R * I("ticklen") / 100f, tl2 = R * I("ticklen2") / 100f;
            // dial: 0 default placeholder not used; 1 none,2 all ticks,3 hour ticks,4 dots,5 numbers 1-12,6 numbers+ticks,7 quarter+ticks,8 roman+ticks,9 batons,10 ticks+dots
            for (int i = 0; i < 60; i++)
            {
                bool hr = i % 5 == 0; double deg = i * 6;
                float r2 = R - 3;
                if (dial == 2 || dial == 10 || (dial == 3 && hr) || ((dial == 6 || dial == 8) && true) || (dial == 7 && true))
                {
                    if ((dial == 3) && !hr) continue;
                    if ((dial == 6 || dial == 7 || dial == 8) && !hr && false) continue;
                    float len = hr ? tl : tl2;
                    using (Pen p = new Pen(hr ? C("tick") : Color.FromArgb(170, C("tick")), hr ? I("tickw") : I("tickw2"))) g.DrawLine(p, Pt(cx, cy, deg, r2 - len), Pt(cx, cy, deg, r2));
                }
                if (dial == 4)
                {
                    float rr = hr ? Math.Max(2.5f, R * 0.04f) : Math.Max(1f, R * 0.015f); PointF q = Pt(cx, cy, deg, R * 0.9f);
                    using (SolidBrush b = new SolidBrush(hr ? C("tick") : Color.FromArgb(150, C("tick")))) g.FillEllipse(b, q.X - rr, q.Y - rr, 2 * rr, 2 * rr);
                }
                if (dial == 10 && hr)
                {
                    float rr = Math.Max(2f, R * 0.03f); PointF q = Pt(cx, cy, deg, R - tl - R * 0.06f);
                    using (SolidBrush b = new SolidBrush(C("accent"))) g.FillEllipse(b, q.X - rr, q.Y - rr, 2 * rr, 2 * rr);
                }
                if (dial == 9 && hr)
                {
                    double a = deg * Math.PI / 180; float dx = (float)Math.Sin(a), dy = -(float)Math.Cos(a), px = -dy, py = dx; float ww = Math.Max(2f, I("tickw") * 1.2f);
                    PointF p1 = new PointF(cx + dx * (r2 - tl * 1.5f) + px * ww, cy + dy * (r2 - tl * 1.5f) + py * ww), p2 = new PointF(cx + dx * (r2 - tl * 1.5f) - px * ww, cy + dy * (r2 - tl * 1.5f) - py * ww);
                    PointF p3 = new PointF(cx + dx * r2 - px * ww, cy + dy * r2 - py * ww), p4 = new PointF(cx + dx * r2 + px * ww, cy + dy * r2 + py * ww);
                    using (SolidBrush b = new SolidBrush(C("tick"))) g.FillPolygon(b, new[] { p1, p2, p3, p4 });
                }
            }
            if (dial == 5 || dial == 6 || dial == 7 || dial == 8)
            {
                float fsz = R * (dial == 8 ? 0.15f : 0.22f) * I("numsize") / 100f;
                for (int i = 0; i < 12; i++)
                {
                    if (dial == 7 && i % 3 != 0) continue;
                    string s = (dial == 8) ? Rom[i] : (i == 0 ? "12" : i.ToString());
                    PointF q = Pt(cx, cy, i * 30, R * I("numdist") / 100f);
                    List<P> l = new List<P>(); l.Add(new P(s, C("num")));
                    Run(g, new RectangleF(q.X - fsz * 1.5f, q.Y - fsz * 0.7f, fsz * 3, fsz * 1.4f), fsz, l, 1, false);
                }
            }
            if (B("date") && W > 150)
            {
                List<P> l = new List<P>(); l.Add(new P(Cs(n.ToString("ddd d")), C("datec")));
                Run(g, new RectangleF(cx - R * 0.35f, cy + R * 0.3f, R * 0.7f, R * 0.17f), R * 0.15f * I("dscale") / 100f, l, 1, false);
            }
            double sec = n.Second + (B("smooth") ? n.Millisecond / 1000.0 : 0), min = n.Minute + sec / 60.0, hour = (n.Hour % 12) + min / 60.0;
            float t = R * I("tail") / 100f, k = R / 100f;
            Hand(g, cx, cy, hour * 30, R * I("hourlen") / 100f, I("hourw") * k, C("hour"), hand, t);
            Hand(g, cx, cy, min * 6, R * I("minlen") / 100f, I("minw") * k, C("min"), hand, t);
            if (B("secs")) Hand(g, cx, cy, sec * 6, R * I("seclen") / 100f, I("secw") * k, C("sec"), 1, R * 0.18f);
            float ds = I("dotsize"); if (ds > 0) using (SolidBrush b = new SolidBrush(C("dotc"))) g.FillEllipse(b, cx - ds, cy - ds, 2 * ds, 2 * ds);
        }

        // ---- 7 segment ----
        static readonly int[] SegMask = { 0x3F, 0x06, 0x5B, 0x4F, 0x66, 0x6D, 0x7D, 0x07, 0x7F, 0x6F };
        void SegDigit(Graphics g, int d, float x, float y, float w, float h, float t, Color on)
        {
            float xi = x + t / 2, yi = y + t / 2, wi = w - t, hi = h - t, gap = t * 0.15f, sl = I("segslant") / 100f;
            float[][] segs = {
                new[]{xi+gap, yi, xi+wi-gap, yi}, new[]{xi+wi, yi+gap, xi+wi, yi+hi/2-gap}, new[]{xi+wi, yi+hi/2+gap, xi+wi, yi+hi-gap},
                new[]{xi+gap, yi+hi, xi+wi-gap, yi+hi}, new[]{xi, yi+hi/2+gap, xi, yi+hi-gap}, new[]{xi, yi+gap, xi, yi+hi/2-gap}, new[]{xi+gap, yi+hi/2, xi+wi-gap, yi+hi/2} };
            int mask = d >= 0 && d <= 9 ? SegMask[d] : 0;
            for (int i = 0; i < 7; i++)
            {
                bool lit = (mask & (1 << i)) != 0; float[] s = segs[i]; PointF[] pts;
                if (Math.Abs(s[1] - s[3]) < 0.01f) pts = new[] { new PointF(s[0], s[1]), new PointF(s[0] + t / 2, s[1] - t / 2), new PointF(s[2] - t / 2, s[1] - t / 2), new PointF(s[2], s[1]), new PointF(s[2] - t / 2, s[1] + t / 2), new PointF(s[0] + t / 2, s[1] + t / 2) };
                else pts = new[] { new PointF(s[0], s[1]), new PointF(s[0] + t / 2, s[1] + t / 2), new PointF(s[0] + t / 2, s[3] - t / 2), new PointF(s[0], s[3]), new PointF(s[0] - t / 2, s[3] - t / 2), new PointF(s[0] - t / 2, s[1] + t / 2) };
                if (sl > 0) for (int j = 0; j < pts.Length; j++) pts[j].X += (y + h - pts[j].Y) * sl;
                using (SolidBrush b = new SolidBrush(lit ? on : Color.FromArgb(70, C("track")))) g.FillPolygon(b, pts);
            }
        }
        void Seg(Graphics g, DateTime n, float W, float H, bool secs)
        {
            float dh = B("date") ? H * 0.2f : 0; float ch = (H - dh) * 0.92f;
            int nd = secs ? 6 : 4; int nc = secs ? 2 : 1; float tk = I("segthick") / 100f;
            float dw = ch * 0.55f, gap = ch * 0.1f, cw = ch * 0.22f; float total = nd * (dw + gap) + nc * (cw + 2 * gap) - gap;
            float sc = Math.Min(1f, (W - 4) / total); dw *= sc; ch *= sc; gap *= sc; cw *= sc;
            total = nd * (dw + gap) + nc * (cw + 2 * gap) - gap;
            float x = (W - total) / 2, y = (H - dh - ch) / 2; float t = ch * 0.11f * tk;
            int[] ds = { n.Hour / 10, n.Hour % 10, n.Minute / 10, n.Minute % 10, n.Second / 10, n.Second % 10 };
            if (!B("h24")) { int h12 = n.Hour % 12; if (h12 == 0) h12 = 12; ds[0] = h12 / 10; ds[1] = h12 % 10; }
            Color[] cl = { C("hour"), C("hour"), C("min"), C("min"), C("sec"), C("sec") };
            for (int i = 0; i < nd; i++)
            {
                if (!(i == 0 && !B("h24") && !B("lead0") && ds[0] == 0)) SegDigit(g, ds[i], x, y, dw, ch, t, cl[i]);
                x += dw + gap;
                if ((i == 1) || (secs && i == 3))
                {
                    x += gap; bool on = !(B("blink") && n.Second % 2 == 1); float r = Math.Max(1.5f, t * 0.55f);
                    using (SolidBrush b = new SolidBrush(on ? C("sepc") : Color.FromArgb(60, C("track")))) { g.FillEllipse(b, x + cw / 2 - r, y + ch * 0.32f - r, 2 * r, 2 * r); g.FillEllipse(b, x + cw / 2 - r, y + ch * 0.68f - r, 2 * r, 2 * r); }
                    x += cw + gap;
                }
            }
            if (B("date")) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 40);
        }

        // ================= Style dispatcher =================
        void DrawStyle(Graphics g, DateTime n, float W, float H, int st)
        {
            bool date = B("date"), secs = B("secs");
            if (st < 20) { Analog(g, n, W, H, Clocks.Analog[st][0], Clocks.Analog[st][1]); return; }
            float dh = date ? H * 0.26f : 0;
            switch (st)
            {
                case 20: Run(g, new RectangleF(0, 0, W, H - dh), 300, TimeParts(n, false, true), -1, true); if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 60); break;
                case 21: Run(g, new RectangleF(0, 0, W, H - dh), 300, TimeParts(n, true, true), -1, true); if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 60); break;
                case 22:
                case 23:
                    {
                        float dd = date ? H * 0.14f : 0; int rows = (st == 23 && secs) ? 3 : 2; float rh = (H - dd) / rows;
                        T(g, HStr(n), new RectangleF(0, 0, W, rh), C("hour"), 300, true);
                        T(g, n.Minute.ToString("00"), new RectangleF(0, rh, W, rh), C("min"), 300, true);
                        if (rows == 3) T(g, n.Second.ToString("00"), new RectangleF(0, 2 * rh, W, rh), C("sec"), 300, true);
                        if (date) D(g, DateS(n), new RectangleF(0, H - dd, W, dd), 40);
                        break;
                    }
                case 24: Run(g, new RectangleF(0, 0, W, H * 0.62f), 300, TimeParts(n, secs, false), -1, true); D(g, DateS(n), new RectangleF(0, H * 0.62f, W, H * 0.22f), 60); { List<P> l = new List<P>(); l.Add(new P(Cs(n.ToString("MMMM yyyy")), C("accent"))); Run(g, new RectangleF(0, H * 0.84f, W, H * 0.16f), 40 * I("dscale") / 100f, l, -1, false); } break;
                case 25:
                    {
                        float aw = W * 0.2f; Run(g, new RectangleF(0, 0, W - aw, H - dh), 300, TimeParts(n, secs, false), -1, true);
                        List<P> l = new List<P>(); l.Add(new P(n.Hour < 12 ? "AM" : "PM", C("accent")));
                        Run(g, new RectangleF(W - aw, (H - dh) * 0.3f, aw, (H - dh) * 0.4f), 60, l, 1, false);
                        if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 60); break;
                    }
                case 26:
                    {
                        string hh = HStr(n).PadLeft(2, '0'); string[] ds = { hh.Substring(0, 1), hh.Substring(1, 1), n.Minute.ToString("00").Substring(0, 1), n.Minute.ToString("00").Substring(1, 1) };
                        Color[] cl = { C("hour"), C("hour"), C("min"), C("min") }; float rh = (H - dh) / 4;
                        for (int i = 0; i < 4; i++) T(g, ds[i], new RectangleF(0, i * rh, W, rh), cl[i], 300, true);
                        if (date) D(g, n.ToString("ddd d"), new RectangleF(0, H - dh, W, dh), 40); break;
                    }
                case 27:
                    {
                        List<P> l = new List<P>(); if (date) { l.Add(new P(Cs(n.ToString("ddd d MMM")), C("datec"))); l.Add(new P("   ", C("datec"))); }
                        l.AddRange(TimeParts(n, secs, true)); Run(g, new RectangleF(0, 0, W, H), 300, l, -1, true); break;
                    }
                case 28: Seg(g, n, W, H, false); break;
                case 29: Seg(g, n, W, H, true); break;
                case 30:
                case 31:
                    {
                        int cnt = st == 31 ? 3 : 2; float gap = 8; float dd = date ? H * 0.2f : 0; float bw = (W - gap * (cnt - 1)) / cnt; float bh = H - dd;
                        string[] ts = { HStr(n), n.Minute.ToString("00"), n.Second.ToString("00") }; Color[] cl = { C("hour"), C("min"), C("sec") };
                        for (int i = 0; i < cnt; i++)
                        {
                            RectangleF r = new RectangleF(i * (bw + gap), 0, bw, bh);
                            using (SolidBrush cb = new SolidBrush(Color.FromArgb(Math.Max(60, I("facea") + 100 > 255 ? 255 : I("facea") + 100), C("facec")))) g.FillRectangle(cb, r);
                            T(g, ts[i], RectangleF.Inflate(r, -4, -2), cl[i], 400, true);
                            using (Pen p = new Pen(C("bg"), 2)) g.DrawLine(p, r.X, r.Y + bh / 2, r.Right, r.Y + bh / 2);
                        }
                        if (date) D(g, DateS(n), new RectangleF(0, H - dd, W, dd), 40); break;
                    }
                case 32:
                    T(g, n.Second.ToString("00"), new RectangleF(0, 0, W, H - dh), C("sec"), 500, true);
                    if (date) { List<P> l = TimeParts(n, false, true); Run(g, new RectangleF(0, H - dh, W, dh), 50 * I("dscale") / 100f, l, 1, false); }
                    break;
                case 33: case 34:
                    { int keep = I("effect"); if (keep == 0) Set("effect", st == 33 ? "3" : "2"); Run(g, new RectangleF(0, 0, W, H - dh), 300, TimeParts(n, secs, true), -1, true); if (keep == 0) Set("effect", "0"); if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 60); break; }
                case 35:
                    {
                        string R2(int x) { string[] u = { "", "I", "II", "III", "IV", "V", "VI", "VII", "VIII", "IX" }; string[] t2 = { "", "X", "XX", "XXX", "XL", "L" }; return x == 0 ? "N" : t2[x / 10] + u[x % 10]; }
                        int hh = n.Hour; if (!B("h24")) { hh %= 12; if (hh == 0) hh = 12; }
                        List<P> l = new List<P>(); l.Add(new P(R2(hh), C("hour"))); l.Add(new P(" " + Sp(n) + " ", C("sepc"))); l.Add(new P(R2(n.Minute), C("min")));
                        if (secs) { l.Add(new P(" " + Sp(n) + " ", C("sepc"))); l.Add(new P(R2(n.Second), C("sec"))); }
                        Run(g, new RectangleF(0, 0, W, H - dh), 300, l, -1, true); if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 60); break;
                    }
                case 36:
                    {
                        string[] hn = { "twelve", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven" };
                        int m5 = ((n.Minute + 2) / 5) * 5; int hh = n.Hour; if (m5 == 60) { m5 = 0; hh++; }
                        string cur = hn[hh % 12], nxt = hn[(hh + 1) % 12]; string s;
                        switch (m5)
                        {
                            case 0: s = cur + " o'clock"; break; case 5: s = "five past " + cur; break; case 10: s = "ten past " + cur; break;
                            case 15: s = "quarter past " + cur; break; case 20: s = "twenty past " + cur; break; case 25: s = "twenty five past " + cur; break;
                            case 30: s = "half past " + cur; break; case 35: s = "twenty five to " + nxt; break; case 40: s = "twenty to " + nxt; break;
                            case 45: s = "quarter to " + nxt; break; case 50: s = "ten to " + nxt; break; default: s = "five to " + nxt; break;
                        }
                        T(g, Cs("It's " + s), new RectangleF(0, 0, W, H - dh), C("hour"), 120, true); if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 40); break;
                    }
                case 37:
                    {
                        string[] hn = { "midnight", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten", "eleven", "noon" };
                        int hh = n.Hour, m = n.Minute; string s;
                        Func<int, string> hname = delegate (int h) { h = h % 24; if (h == 0) return "midnight"; if (h == 12) return "noon"; return hn[h % 12]; };
                        if (m < 8) s = "just after " + hname(hh); else if (m < 23) s = "a quarter past " + hname(hh); else if (m < 38) s = "half past " + hname(hh);
                        else if (m < 53) s = "a quarter to " + hname(hh + 1); else s = "almost " + hname(hh + 1);
                        T(g, Cs(s), new RectangleF(0, 0, W, H - dh), C("hour"), 100, true); if (date) D(g, DateS(n), new RectangleF(0, H - dh, W, dh), 40); break;
                    }
                case 38:
                    T(g, Cs(n.ToString("dddd")), new RectangleF(0, 0, W, H * 0.42f), C("accent"), 200, true);
                    Run(g, new RectangleF(0, H * 0.42f, W, H * 0.58f), 300, TimeParts(n, secs, true), -1, true); break;
                case 39:
                    {
                        float bh = Math.Max(6, H * 0.08f); Run(g, new RectangleF(0, 0, W, H - dh - bh - 6), 300, TimeParts(n, secs, true), -1, true);
                        double f = (n.Hour * 3600 + n.Minute * 60 + n.Second) / 86400.0; BarC(g, new RectangleF(2, H - dh - bh, W - 4, bh), f, C("sec"));
                        if (date) D(g, DateS(n) + "   " + (f * 100).ToString("0") + "% of day", new RectangleF(0, H - dh, W, dh), 40); break;
                    }
                default: DrawGraphic(g, n, W, H, st); break;
            }
        }

        void DrawGraphic(Graphics g, DateTime n, float W, float H, int st)
        {
            bool date = B("date"), secs = B("secs"); float cx = W / 2, cy = H / 2;
            double sec = n.Second + (B("smooth") ? n.Millisecond / 1000.0 : 0), min = n.Minute + sec / 60.0, hour = n.Hour + min / 60.0;
            double dayf = (n.Hour * 3600 + n.Minute * 60 + n.Second) / 86400.0;
            switch (st)
            {
                case 40:
                    {
                        int[] digs = { n.Hour / 10, n.Hour % 10, n.Minute / 10, n.Minute % 10, n.Second / 10, n.Second % 10 }; int[] bits = { 2, 4, 3, 4, 3, 4 };
                        Color[] cl = { C("hour"), C("hour"), C("min"), C("min"), C("sec"), C("sec") };
                        int cols = secs ? 6 : 4; float dd = date ? 20 : 0; float cw = W / cols, ch = (H - dd) / 4f; float rad = Math.Min(cw, ch) * 0.36f;
                        for (int c = 0; c < cols; c++) for (int b = 0; b < 4; b++)
                            {
                                int bit = 3 - b; if (bit >= bits[c]) continue; bool on = ((digs[c] >> bit) & 1) == 1; float x = c * cw + cw / 2, y = b * ch + ch / 2;
                                using (SolidBrush br = new SolidBrush(on ? cl[c] : Color.FromArgb(90, C("track")))) g.FillEllipse(br, x - rad, y - rad, 2 * rad, 2 * rad);
                            }
                        if (date) D(g, DateS(n), new RectangleF(0, H - dd, W, dd), 20); break;
                    }
                case 41:
                case 42:
                    {
                        float R = Math.Min(W, H) / 2 - 6; float lw = Math.Max(5, R * 0.1f);
                        ArcC(g, new RectangleF(cx - R, cy - R, 2 * R, 2 * R), (hour % 12) / 12.0, lw, C("hour"));
                        float R2 = R - lw * 1.7f; ArcC(g, new RectangleF(cx - R2, cy - R2, 2 * R2, 2 * R2), min / 60.0, lw, C("min")); float ir = R2 - lw;
                        if (st == 41) { float R3 = R2 - lw * 1.7f; ArcC(g, new RectangleF(cx - R3, cy - R3, 2 * R3, 2 * R3), sec / 60.0, lw, C("sec")); ir = R3 - lw; }
                        if (ir > 8) Run(g, new RectangleF(cx - ir, cy - ir * 0.6f, 2 * ir, ir * 1.2f), 100, TimeParts(n, false, false), 1, true); break;
                    }
                case 43:
                    {
                        string[] lb = { "H", "M", "S" }; double[] fr = { (hour % 12) / 12.0, min / 60.0, sec / 60.0 }; Color[] cl = { C("hour"), C("min"), C("sec") }; string[] vs = { HStr(n), n.Minute.ToString("00"), n.Second.ToString("00") };
                        int rows = 3; float rh = H / rows;
                        for (int i = 0; i < rows; i++)
                        {
                            T(g, lb[i], new RectangleF(0, i * rh, W * 0.12f, rh), C("datec"), 40, false);
                            BarC(g, new RectangleF(W * 0.14f, i * rh + rh * 0.3f, W * 0.62f, rh * 0.4f), fr[i], cl[i]);
                            T(g, vs[i], new RectangleF(W * 0.78f, i * rh, W * 0.22f, rh), cl[i], 60, true);
                        }
                        break;
                    }
                case 44:
                    {
                        double[] fr = { (hour % 12) / 12.0, min / 60.0, sec / 60.0 }; Color[] cl = { C("hour"), C("min"), C("sec") }; string[] lb = { "H", "M", "S" };
                        float cw = W / 3, bh = H - 30;
                        for (int i = 0; i < 3; i++)
                        {
                            RectangleF tr = new RectangleF(i * cw + cw * 0.25f, 4, cw * 0.5f, bh);
                            using (SolidBrush bg = new SolidBrush(Color.FromArgb(140, C("track")))) g.FillRectangle(bg, tr);
                            using (SolidBrush fg = new SolidBrush(cl[i])) g.FillRectangle(fg, tr.X, (float)(tr.Bottom - tr.Height * fr[i]), tr.Width, (float)(tr.Height * fr[i]));
                            T(g, lb[i], new RectangleF(i * cw, H - 26, cw, 24), C("datec"), 30, false);
                        }
                        break;
                    }
                case 45:
                    {
                        float R = Math.Min(W, H) / 2 - 8;
                        for (int i = 0; i < 60; i++)
                        {
                            bool lit = i <= (int)sec; bool hr = i % 5 == 0; PointF q = Pt(cx, cy, i * 6, R); float rr = hr ? R * 0.06f : R * 0.035f;
                            using (SolidBrush b = new SolidBrush(lit ? C("sec") : Color.FromArgb(120, C("track")))) g.FillEllipse(b, q.X - rr, q.Y - rr, 2 * rr, 2 * rr);
                        }
                        PointF ph = Pt(cx, cy, (hour % 12) * 30, R * 0.8f), pm = Pt(cx, cy, min * 6, R * 0.8f);
                        using (SolidBrush b = new SolidBrush(C("hour"))) g.FillEllipse(b, ph.X - R * 0.07f, ph.Y - R * 0.07f, R * 0.14f, R * 0.14f);
                        using (SolidBrush b = new SolidBrush(C("min"))) g.FillEllipse(b, pm.X - R * 0.05f, pm.Y - R * 0.05f, R * 0.1f, R * 0.1f);
                        float ir = R * 0.62f; Run(g, new RectangleF(cx - ir, cy - ir * 0.4f, 2 * ir, ir * 0.8f), 100, TimeParts(n, false, false), 1, true); break;
                    }
                case 46:
                    {
                        float R = Math.Min(W, H) / 2 - 8; float[] rr = { R * 0.35f, R * 0.62f, R * 0.9f }; double[] dg = { (hour % 12) * 30, min * 6, sec * 6 }; Color[] cl = { C("hour"), C("min"), C("sec") }; float[] ps = { R * 0.09f, R * 0.07f, R * 0.05f };
                        using (SolidBrush sb = new SolidBrush(C("accent"))) g.FillEllipse(sb, cx - R * 0.12f, cy - R * 0.12f, R * 0.24f, R * 0.24f);
                        for (int i = 0; i < 3; i++)
                        {
                            using (Pen p = new Pen(Color.FromArgb(120, C("track")), 1.5f)) g.DrawEllipse(p, cx - rr[i], cy - rr[i], 2 * rr[i], 2 * rr[i]);
                            PointF q = Pt(cx, cy, dg[i], rr[i]); using (SolidBrush b = new SolidBrush(cl[i])) g.FillEllipse(b, q.X - ps[i], q.Y - ps[i], 2 * ps[i], 2 * ps[i]);
                        }
                        break;
                    }
                case 47:
                    {
                        float dd = date ? H * 0.2f : 0; float hz = (H - dd) * 0.72f; float R = Math.Min(W * 0.44f, hz * 0.95f);
                        using (Pen hp = new Pen(C("track"), 2)) g.DrawLine(hp, 4, hz, W - 4, hz);
                        using (Pen ap = new Pen(Color.FromArgb(150, C("tick")), 1.5f)) { ap.DashStyle = DashStyle.Dot; g.DrawArc(ap, cx - R, hz - R, 2 * R, 2 * R, 180, 180); }
                        double a = Math.PI * (1 - Math.Max(0, Math.Min(1, (hour - 6) / 12.0))); bool up = hour >= 6 && hour <= 18;
                        float sx = cx + (float)Math.Cos(a) * R, sy = hz - (float)Math.Sin(a) * R; if (!up) { sx = hour < 6 ? cx - R : cx + R; sy = hz; }
                        float sr = Math.Max(5, R * 0.1f);
                        using (SolidBrush b = new SolidBrush(up ? C("accent") : Color.FromArgb(120, C("track")))) g.FillEllipse(b, sx - sr, sy - sr, 2 * sr, 2 * sr);
                        Run(g, new RectangleF(0, hz + 2, W, H - dd - hz - 2), 100, TimeParts(n, secs, false), 1, true);
                        if (date) D(g, DateS(n), new RectangleF(0, H - dd, W, dd), 40); break;
                    }
                case 48:
                    T(g, (dayf * 100).ToString("0.0") + "%", new RectangleF(0, 0, W, H * 0.62f), C("hour"), 300, true); BarC(g, new RectangleF(4, H * 0.68f, W - 8, Math.Max(6, H * 0.1f)), dayf, C("sec"));
                    D(g, "of the day gone", new RectangleF(0, H * 0.82f, W, H * 0.18f), 30); break;
                case 49:
                    {
                        int hx = (int)(dayf * 16777215); Color cc = Color.FromArgb(255, (hx >> 16) & 255, (hx >> 8) & 255, hx & 255);
                        using (SolidBrush b = new SolidBrush(cc)) g.FillRectangle(b, 0, 0, W, H * 0.55f);
                        T(g, "#" + hx.ToString("X6"), new RectangleF(0, H * 0.55f, W, H * 0.45f), C("hour"), 200, true); break;
                    }
                case 50:
                    {
                        double d = dayf * 10; int dh2 = (int)d; int dm = (int)((d - dh2) * 100); int ds2 = (int)((((d - dh2) * 100) - dm) * 100);
                        List<P> l = new List<P>(); l.Add(new P(dh2.ToString(), C("hour"))); l.Add(new P(":", C("sepc"))); l.Add(new P(dm.ToString("00"), C("min"))); l.Add(new P(":", C("sepc"))); l.Add(new P(ds2.ToString("00"), C("sec")));
                        Run(g, new RectangleF(0, 0, W, H * 0.72f), 300, l, -1, true); D(g, "decimal time", new RectangleF(0, H * 0.74f, W, H * 0.24f), 40); break;
                    }
                case 51:
                    T(g, new DateTimeOffset(n.ToUniversalTime()).ToUnixTimeSeconds().ToString(), new RectangleF(0, 0, W, H * 0.7f), C("hour"), 300, true); D(g, "unix time", new RectangleF(0, H * 0.72f, W, H * 0.26f), 40); break;
                case 52:
                    {
                        float bh = H * 0.24f; using (SolidBrush b = new SolidBrush(C("accent"))) g.FillRectangle(b, 0, 0, W, bh);
                        T(g, Cs(n.ToString("MMMM yyyy")), new RectangleF(0, 0, W, bh), C("bg"), 40, false);
                        T(g, n.Day.ToString(), new RectangleF(0, bh, W, H * 0.56f), C("hour"), 300, true); T(g, Cs(n.ToString("dddd")), new RectangleF(0, H * 0.8f, W, H * 0.2f), C("datec"), 40, false); break;
                    }
                case 53:
                    T(g, n.Day.ToString(), new RectangleF(0, 0, W * 0.5f, H), C("accent"), 400, true);
                    D(g, n.ToString("MMM yyyy"), new RectangleF(W * 0.5f, H * 0.12f, W * 0.5f, H * 0.28f), 60); D(g, n.ToString("dddd"), new RectangleF(W * 0.5f, H * 0.4f, W * 0.5f, H * 0.2f), 40);
                    Run(g, new RectangleF(W * 0.5f, H * 0.62f, W * 0.5f, H * 0.3f), 100, TimeParts(n, false, false), 1, false); break;
                case 54:
                    {
                        float dd = H * 0.45f; int dow = ((int)n.DayOfWeek + 6) % 7; string[] dn = { "M", "T", "W", "T", "F", "S", "S" }; float bw = W / 7;
                        for (int i = 0; i < 7; i++)
                        {
                            RectangleF r = new RectangleF(i * bw + 3, H * 0.52f, bw - 6, H * 0.4f);
                            using (SolidBrush b = new SolidBrush(i == dow ? C("accent") : (i < dow ? Color.FromArgb(180, C("sec")) : Color.FromArgb(120, C("track"))))) g.FillRectangle(b, r);
                            T(g, dn[i], r, i == dow ? C("bg") : C("datec"), 30, false);
                        }
                        Run(g, new RectangleF(0, 0, W, dd), 200, TimeParts(n, secs, true), -1, true); break;
                    }
                case 55:
                    {
                        float R = Math.Min(W, H) / 2 - 8; float lw = Math.Max(6, R * 0.14f); int doy = n.DayOfYear; int diy = DateTime.IsLeapYear(n.Year) ? 366 : 365;
                        ArcC(g, new RectangleF(cx - R, cy - R, 2 * R, 2 * R), doy / (double)diy, lw, C("accent")); float ir = R - lw * 1.2f;
                        T(g, ((doy * 100.0) / diy).ToString("0") + "%", new RectangleF(cx - ir, cy - ir * 0.55f, 2 * ir, ir * 0.8f), C("hour"), 100, true); D(g, "day " + doy + "/" + diy, new RectangleF(cx - ir, cy + ir * 0.25f, 2 * ir, ir * 0.4f), 30); break;
                    }
                case 56:
                    {
                        string[] tzk = { "tz2", "tz3", "tz4", "tz5" }, lk = { "l2", "l3", "l4", "l5" }; float rh = H / 4;
                        for (int i = 0; i < 4; i++)
                        {
                            DateTime t = Now(S(tzk[i])); float y = i * rh;
                            T(g, Cs(S(lk[i])), new RectangleF(4, y, W * 0.4f, rh), C("labelc"), 40, false);
                            List<P> l = TimeParts(t, false, true); Run(g, new RectangleF(W * 0.4f, y, W * 0.6f - 4, rh), 100, l, 2, true);
                            if (i > 0) using (Pen p = new Pen(Color.FromArgb(70, C("track")), 1)) g.DrawLine(p, 4, y, W - 4, y);
                        }
                        break;
                    }
                case 57:
                    {
                        DateTime a = Now(S("tz")), b2 = Now(S("tz3")); float hw = W / 2;
                        for (int i = 0; i < 2; i++)
                        {
                            DateTime t = i == 0 ? a : b2; float x = i * hw;
                            T(g, Cs(i == 0 ? (S("l2") == "" ? "Local" : S("l2")) : S("l3")), new RectangleF(x, 0, hw, H * 0.22f), C("labelc"), 40, false);
                            Run(g, new RectangleF(x + 2, H * 0.22f, hw - 4, H * 0.5f), 200, TimeParts(t, false, false), 1, true);
                            if (date) D(g, t.ToString("ddd d MMM"), new RectangleF(x, H * 0.74f, hw, H * 0.22f), 30);
                        }
                        using (Pen p = new Pen(Color.FromArgb(90, C("track")), 1)) g.DrawLine(p, hw, 8, hw, H - 8);
                        break;
                    }
                case 58:
                    {
                        double days = (n.ToUniversalTime() - new DateTime(2000, 1, 6, 18, 14, 0, DateTimeKind.Utc)).TotalDays; double ph = (days % 29.530588853) / 29.530588853; if (ph < 0) ph += 1;
                        float dd = H * 0.3f; float R = Math.Min(W * 0.45f, (H - dd) / 2 - 4); float mx = cx, my = (H - dd) / 2;
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(150, C("track")))) g.FillEllipse(b, mx - R, my - R, 2 * R, 2 * R);
                        RectangleF full = new RectangleF(mx - R, my - R, 2 * R, 2 * R);
                        using (SolidBrush lit = new SolidBrush(C("accent"))) using (SolidBrush dark = new SolidBrush(Color.FromArgb(255, Blend(C("track"), C("bg")))))
                        {
                            bool waxing = ph < 0.5; g.FillPie(lit, Rectangle.Round(full), waxing ? -90 : 90, 180);
                            double k = Math.Cos(2 * Math.PI * ph); float ew = (float)Math.Abs(k) * R;
                            if (ew > 0.5f) g.FillEllipse((ph < 0.25 || ph > 0.75) ? dark : lit, mx - ew, my - R, 2 * ew, 2 * R);
                        }
                        Run(g, new RectangleF(0, H - dd, W, dd * 0.7f), 100, TimeParts(n, false, true), 1, true);
                        D(g, (ph * 100).ToString("0") + "% of cycle", new RectangleF(0, H - dd * 0.3f, W, dd * 0.3f), 20); break;
                    }
                case 59:
                    {
                        float R = Math.Min(W, H) / 2 - 6; float lw = Math.Max(5, R * 0.16f); int hh = n.Hour % 12;
                        for (int i = 0; i < 12; i++)
                            using (Pen p = new Pen(i == hh ? C("hour") : (i < hh ? Color.FromArgb(170, C("hour")) : Color.FromArgb(110, C("track"))), lw))
                                g.DrawArc(p, cx - R + lw / 2, cy - R + lw / 2, 2 * R - lw, 2 * R - lw, -90 + i * 30 + 2, 26);
                        float ir = R - lw * 1.4f; Run(g, new RectangleF(cx - ir, cy - ir * 0.45f, 2 * ir, ir * 0.9f), 100, TimeParts(n, false, false), 1, true); break;
                    }
                case 60:
                    {
                        float R = Math.Min(W, H) / 2 - 8; float lw = Math.Max(6, R * 0.12f); ArcC(g, new RectangleF(cx - R, cy - R, 2 * R, 2 * R), sec / 60.0, lw, C("sec")); float ir = R - lw * 1.2f;
                        Run(g, new RectangleF(cx - ir, cy - ir * 0.45f, 2 * ir, ir * 0.9f), 100, TimeParts(n, false, false), 1, true);
                        if (date) D(g, n.ToString("ddd d MMM"), new RectangleF(cx - ir, cy + ir * 0.35f, 2 * ir, ir * 0.3f), 30); break;
                    }
                case 61:
                    {
                        float R = Math.Min(W, H) / 2 - 6; RectangleF rc = new RectangleF(cx - R, cy - R, 2 * R, 2 * R);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(70, C("track")))) g.FillEllipse(b, rc);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(120, C("hour")))) g.FillPie(b, Rectangle.Round(rc), -90, (float)((hour % 12) * 30));
                        float R2 = R * 0.68f; RectangleF r2 = new RectangleF(cx - R2, cy - R2, 2 * R2, 2 * R2);
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(170, C("min")))) g.FillPie(b, Rectangle.Round(r2), -90, (float)(min * 6));
                        if (secs) using (Pen p = new Pen(C("sec"), 1.5f)) g.DrawLine(p, cx, cy, Pt(cx, cy, sec * 6, R).X, Pt(cx, cy, sec * 6, R).Y);
                        using (Pen p = new Pen(C("ringc"), Math.Max(1, I("ringw")))) g.DrawEllipse(p, rc); break;
                    }
                default:
                    {
                        float dd = date ? H * 0.22f : 0; float bh = (H - dd) * 0.45f; float by = 4; int hh = n.Hour; float sw = (W - 4) / 24f;
                        for (int i = 0; i < 24; i++)
                            using (SolidBrush b = new SolidBrush(i == hh ? C("hour") : (i < hh ? Color.FromArgb(170, C("accent")) : Color.FromArgb(110, C("track"))))) g.FillRectangle(b, 2 + i * sw + 1, by, sw - 2, bh);
                        Run(g, new RectangleF(0, by + bh + 2, W, H - dd - bh - by - 2), 200, TimeParts(n, secs, true), -1, true);
                        if (date) D(g, DateS(n), new RectangleF(0, H - dd, W, dd), 40); break;
                    }
            }
        }
        static Color Blend(Color a, Color b) { return Color.FromArgb((a.R + b.R) / 2, (a.G + b.G) / 2, (a.B + b.B) / 2); }
    }

    // ================= Customize window (generated from the property list) =================
    class CustomizeForm : Form
    {
        static Dictionary<string, CustomizeForm> open = new Dictionary<string, CustomizeForm>();
        ClockForm w; bool building;
        public static void Open(ClockForm w)
        {
            CustomizeForm f;
            if (open.TryGetValue(w.Id, out f) && !f.IsDisposed) { f.Activate(); return; }
            f = new CustomizeForm(w); open[w.Id] = f; f.Show();
        }
        public static void Refresh(ClockForm w)
        {
            CustomizeForm f; if (open.TryGetValue(w.Id, out f) && !f.IsDisposed && !f.building) { int sel = f.tabs.SelectedIndex; f.Build(); if (sel < f.tabs.TabCount) f.tabs.SelectedIndex = sel; }
        }
        TabControl tabs;
        public CustomizeForm(ClockForm widget)
        {
            w = widget; Text = "Customize clock - " + w.Id; Width = 560; Height = 760; StartPosition = FormStartPosition.CenterScreen;
            FormClosed += delegate { open.Remove(w.Id); };
            Build();
        }
        void Build()
        {
            building = true; SuspendLayout(); Controls.Clear();
            Panel top = new Panel(); top.Dock = DockStyle.Top; top.Height = 84;
            Label l1 = new Label(); l1.Text = "Quick theme:"; l1.Left = 8; l1.Top = 10; l1.AutoSize = true; top.Controls.Add(l1);
            ComboBox th = new ComboBox(); th.DropDownStyle = ComboBoxStyle.DropDownList; th.Left = 100; th.Top = 6; th.Width = 200;
            for (int i = 0; i < Themes.Data.Length; i++) th.Items.Add(Themes.Name(i));
            th.SelectedIndexChanged += delegate { if (building) return; Themes.Apply(w, th.SelectedIndex); w.Restyle(); Refresh(w); };
            top.Controls.Add(th);
            Label l2 = new Label(); l2.Text = "Clock style:"; l2.Left = 8; l2.Top = 40; l2.AutoSize = true; top.Controls.Add(l2);
            ComboBox cs = new ComboBox(); cs.DropDownStyle = ComboBoxStyle.DropDownList; cs.Left = 100; cs.Top = 36; cs.Width = 300; cs.Items.AddRange(Clocks.StyleNames); cs.SelectedIndex = w.Style;
            cs.SelectedIndexChanged += delegate { if (building) return; w.ChangeStyle(cs.SelectedIndex); };
            top.Controls.Add(cs);
            Button all = new Button(); all.Text = "Copy this look to ALL clocks"; all.Left = 310; all.Top = 5; all.Width = 220; all.Height = 26;
            all.Click += delegate { Program.CopyLookToAll(w); };
            top.Controls.Add(all);
            Button rnd = new Button(); rnd.Text = "Random theme"; rnd.Left = 410; rnd.Top = 35; rnd.Width = 120; rnd.Height = 26;
            rnd.Click += delegate { Themes.Apply(w, new Random().Next(Themes.Data.Length)); w.Restyle(); Refresh(w); };
            top.Controls.Add(rnd);
            tabs = new TabControl(); tabs.Dock = DockStyle.Fill;
            Controls.Add(tabs); Controls.Add(top);
            List<string> cats = new List<string>(); foreach (Prop p in Props.All) if (!cats.Contains(p.Cat)) cats.Add(p.Cat);
            foreach (string cat in cats)
            {
                TabPage tp = new TabPage(cat); tp.AutoScroll = true;
                TableLayoutPanel tl = new TableLayoutPanel(); tl.AutoSize = true; tl.ColumnCount = 2; tl.Padding = new Padding(8); tl.Dock = DockStyle.Top;
                tp.Controls.Add(tl);
                foreach (Prop p in Props.All) if (p.Cat == cat && p.Key != "style") AddRow(tl, p);
                if (cat == "Colors")
                {
                    Button rb = new Button(); rb.Text = "Apply theme colors again (reset)"; rb.Width = 240; rb.Click += delegate { Themes.Apply(w, th.SelectedIndex < 0 ? 0 : th.SelectedIndex); w.Restyle(); Refresh(w); };
                    int rr = tl.RowCount++; tl.RowStyles.Add(new RowStyle(SizeType.AutoSize)); tl.Controls.Add(rb, 1, rr);
                }
                tabs.TabPages.Add(tp);
            }
            ResumeLayout(); building = false;
        }
        void AddRow(TableLayoutPanel tl, Prop p)
        {
            int r = tl.RowCount++; tl.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            Label l = new Label(); l.Text = p.Label; l.AutoSize = true; l.MaximumSize = new Size(250, 0); l.Margin = new Padding(0, 6, 8, 0); tl.Controls.Add(l, 0, r);
            Control c = null; string key = p.Key;
            if (p.Kind == "bool")
            {
                CheckBox cb = new CheckBox(); cb.Text = ""; cb.Checked = w.B(key); cb.CheckedChanged += delegate { if (building) return; w.Set(key, cb.Checked ? "1" : "0"); w.Restyle(); }; c = cb;
            }
            else if (p.Kind == "int")
            {
                NumericUpDown nu = new NumericUpDown(); nu.Minimum = p.Min; nu.Maximum = p.Max; int val = key == "x" ? w.Left : (key == "y" ? w.Top : w.I(key)); nu.Value = Math.Max(p.Min, Math.Min(p.Max, val)); nu.Width = 100;
                nu.ValueChanged += delegate { if (building) return; w.Set(key, ((int)nu.Value).ToString()); w.Restyle(); }; c = nu;
            }
            else if (p.Kind == "color")
            {
                Button b = new Button(); b.Width = 110; b.Height = 24; b.FlatStyle = FlatStyle.Flat; b.BackColor = w.C(key);
                b.Click += delegate { using (ColorDialog cd = new ColorDialog()) { cd.Color = b.BackColor; cd.FullOpen = true; if (cd.ShowDialog(this) == DialogResult.OK) { b.BackColor = cd.Color; w.SetColor(key, cd.Color); w.Restyle(); } } }; c = b;
            }
            else if (p.Kind == "choice")
            {
                ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Width = 260; cb.Items.AddRange(p.Choices); cb.SelectedIndex = Math.Max(0, Math.Min(p.Choices.Length - 1, w.I(key)));
                cb.SelectedIndexChanged += delegate { if (building) return; w.Set(key, cb.SelectedIndex.ToString()); w.Restyle(); }; c = cb;
            }
            else if (p.Kind == "text")
            {
                TextBox tb = new TextBox(); tb.Width = 260; tb.Text = w.S(key); tb.TextChanged += delegate { if (building) return; w.Set(key, tb.Text); w.Restyle(); }; c = tb;
            }
            else if (p.Kind == "font")
            {
                ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Width = 260;
                foreach (FontFamily ff in FontFamily.Families) cb.Items.Add(ff.Name);
                int fi = cb.Items.IndexOf(w.S(key)); if (fi >= 0) cb.SelectedIndex = fi;
                cb.SelectedIndexChanged += delegate { if (building) return; w.Set(key, (string)cb.SelectedItem); w.Restyle(); }; c = cb;
            }
            else if (p.Kind == "tz")
            {
                ComboBox cb = new ComboBox(); cb.DropDownStyle = ComboBoxStyle.DropDownList; cb.Width = 260;
                List<string> ids = new List<string>(); cb.Items.Add("Local time"); ids.Add("");
                foreach (TimeZoneInfo z in TimeZoneInfo.GetSystemTimeZones()) { cb.Items.Add(z.DisplayName); ids.Add(z.Id); }
                int si = ids.IndexOf(w.S(key)); cb.SelectedIndex = si < 0 ? 0 : si;
                cb.SelectedIndexChanged += delegate { if (building) return; w.Set(key, ids[cb.SelectedIndex]); w.Restyle(); }; c = cb;
            }
            if (c != null) { c.Margin = new Padding(0, 3, 0, 3); tl.Controls.Add(c, 1, r); }
        }
    }

    // ================= Program / tray =================
    static class Program
    {
        public static List<ClockForm> Clocks_ = new List<ClockForm>();
        static NotifyIcon tray;
        static List<string> Ids() { return new List<string>(Settings.Get("ids", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)); }
        static void SaveIds(List<string> l) { Settings.Set("ids", string.Join(",", l.ToArray())); Settings.Save(); }
        static List<string> Deleted() { return new List<string>(Settings.Get("deleted", "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)); }
        static string NewId() { int n = 1; while (true) { string id = "clk" + n; if (!Ids().Contains(id) && !Deleted().Contains(id)) return id; n++; } }

        static ClockForm Spawn(string id, int style, bool show)
        {
            ClockForm c = new ClockForm(id, style, Clocks_.Count); Clocks_.Add(c); c.Init();
            if (show && Settings.GetBool(id + ".visible", true)) c.Show();
            return c;
        }
        public static ClockForm AddNew(int style)
        {
            string id = NewId(); List<string> l = Ids(); l.Add(id); SaveIds(l);
            ClockForm c = new ClockForm(id, style, Clocks_.Count); c.Set("visible", "1"); Clocks_.Add(c); c.Init(); c.Show(); RefreshTray(); return c;
        }
        public static void Duplicate(ClockForm src)
        {
            ClockForm c = AddNew(src.Style);
            foreach (Prop p in Props.All) if (p.Key != "x" && p.Key != "y") c.Set(p.Key, src.S(p.Key));
            c.Set("x", (src.Left + 30).ToString()); c.Set("y", (src.Top + 30).ToString()); c.Restyle();
        }
        public static void CopyLookToAll(ClockForm src)
        {
            string[] skip = { "style", "x", "y", "w", "h", "tz", "label", "tz2", "tz3", "tz4", "tz5", "l2", "l3", "l4", "l5" };
            foreach (ClockForm c in Clocks_) if (c != src) { foreach (Prop p in Props.All) if (Array.IndexOf(skip, p.Key) < 0) c.Set(p.Key, src.S(p.Key)); c.Restyle(); }
        }
        public static void Delete(ClockForm c)
        {
            List<string> l = Ids(); l.Remove(c.Id); SaveIds(l);
            List<string> d = Deleted(); if (!d.Contains(c.Id)) d.Add(c.Id); Settings.Set("deleted", string.Join(",", d.ToArray()));
            c.SaveAll(); Clocks_.Remove(c); c.Hide(); c.Dispose(); Settings.Save(); RefreshTray();
        }
        public static void Restore(string id)
        {
            List<string> d = Deleted(); d.Remove(id); Settings.Set("deleted", string.Join(",", d.ToArray()));
            List<string> l = Ids(); if (!l.Contains(id)) l.Add(id); SaveIds(l);
            Settings.Set(id + ".visible", true); Spawn(id, 0, true); RefreshTray();
        }
        public static void AttachClocksMenu(ToolStripMenuItem root)
        {
            root.DropDownItems.Add("...");
            root.DropDownOpening += delegate
            {
                root.DropDownItems.Clear();
                foreach (ClockForm c in Clocks_)
                {
                    ClockForm cc = c; ToolStripMenuItem it = new ToolStripMenuItem(cc.Id + " - " + ClocksName(cc)); it.Checked = cc.Visible;
                    it.Click += delegate { cc.SetShown(!cc.Visible); }; root.DropDownItems.Add(it);
                }
                root.DropDownItems.Add(new ToolStripSeparator());
                ToolStripMenuItem rs = new ToolStripMenuItem("Restore deleted clocks");
                foreach (string id in Deleted()) { string i2 = id; rs.DropDownItems.Add(i2 + " - " + Clocks.StyleNames[Math.Max(0, Math.Min(Clocks.StyleNames.Length - 1, Settings.GetInt(i2 + ".style", 0)))], null, delegate { Restore(i2); }); }
                if (rs.DropDownItems.Count == 0) { ToolStripMenuItem none = new ToolStripMenuItem("(nothing deleted)"); none.Enabled = false; rs.DropDownItems.Add(none); }
                root.DropDownItems.Add(rs);
                ToolStripMenuItem add = new ToolStripMenuItem("Add another clock (pick a style)");
                string[] groups = { "Analog", "Digital / text", "Graphic" }; int[] starts = { 0, 20, 40, Clocks.StyleNames.Length };
                for (int gI = 0; gI < 3; gI++)
                {
                    ToolStripMenuItem sub = new ToolStripMenuItem(groups[gI]);
                    for (int i = starts[gI]; i < starts[gI + 1]; i++) { int ii = i; sub.DropDownItems.Add(Clocks.StyleNames[i], null, delegate { AddNew(ii); }); }
                    add.DropDownItems.Add(sub);
                }
                root.DropDownItems.Add(add);
                ToolStripMenuItem auto = new ToolStripMenuItem("Start with Windows"); auto.Checked = IsAutostart();
                auto.Click += delegate { SetAutostart(!IsAutostart()); }; root.DropDownItems.Add(auto);
            };
        }
        static string ClocksName(ClockForm c) { return Clocks.StyleNames[c.Style]; }

        [STAThread]
        static void Main()
        {
            bool created;
            System.Threading.Mutex mx = new System.Threading.Mutex(true, "ClockStudio_single_instance", out created);
            if (!created) return;
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            Settings.Load();
            if (Settings.Get("ids", "") == "" && Settings.Get("deleted", "") == "")
            {
                int[] first = { 0, 28, 41 }; List<string> l = new List<string>();
                for (int i = 0; i < first.Length; i++) { string id = "clk" + (i + 1); l.Add(id); Settings.Set(id + ".style", first[i].ToString()); int[] sz = Clocks.Size(first[i]); Settings.Set(id + ".w", sz[0].ToString()); Settings.Set(id + ".h", sz[1].ToString()); Settings.Set(id + ".x", (60 + i * 260).ToString()); Settings.Set(id + ".y", "80"); }
                SaveIds(l);
            }
            if (!Settings.GetBool("autostart_init", false)) { SetAutostart(true); Settings.Set("autostart_init", true); Settings.Save(); }
            else if (IsAutostart()) SetAutostart(true);
            foreach (string id in Ids()) Spawn(id, 0, true);
            tray = new NotifyIcon(); tray.Icon = SystemIcons.Application; tray.Text = "Clock Studio"; tray.Visible = true; RefreshTray();
            tray.MouseClick += delegate (object so, MouseEventArgs me)
            {
                if (me.Button == MouseButtons.Left)
                    try { typeof(NotifyIcon).GetMethod("ShowContextMenu", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).Invoke(tray, null); } catch { }
            };
            Application.Run();
            mx.ReleaseMutex();
        }
        public static void RefreshTray()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            foreach (ClockForm c in Clocks_)
            {
                ClockForm cc = c; ToolStripMenuItem it = new ToolStripMenuItem(cc.Id + " - " + ClocksName(cc)); it.Checked = cc.Visible; it.CheckOnClick = true;
                it.Click += delegate { cc.SetShown(it.Checked); };
                it.DropDownItems.Add("Customize...", null, delegate { CustomizeForm.Open(cc); });
                ToolStripMenuItem th = new ToolStripMenuItem("Click-through on/off"); th.Click += delegate { cc.Set("through", cc.B("through") ? "0" : "1"); cc.Hide(); cc.Restyle(); cc.Show(); };
                it.DropDownItems.Add(th);
                m.Items.Add(it);
            }
            m.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem cm = new ToolStripMenuItem("Restore / add clocks"); AttachClocksMenu(cm); m.Items.Add(cm);
            m.Items.Add("Reset positions", null, delegate { int i = 0; foreach (ClockForm c in Clocks_) { c.Set("x", (60 + (i % 4) * 260).ToString()); c.Set("y", (60 + (i / 4) * 260).ToString()); c.Restyle(); i++; } });
            m.Items.Add("Exit", null, delegate { ExitAll(); });
            tray.ContextMenuStrip = m;
        }
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        public static bool IsAutostart() { try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey)) return k != null && k.GetValue("ClockStudio") != null; } catch { return false; } }
        public static void SetAutostart(bool on)
        {
            try { using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true)) { if (on) k.SetValue("ClockStudio", "\"" + Application.ExecutablePath + "\""); else k.DeleteValue("ClockStudio", false); } } catch { }
        }
        public static void ExitAll()
        {
            foreach (ClockForm c in Clocks_) c.SaveAll();
            Settings.Save(); tray.Visible = false; Application.Exit();
        }
    }
}
