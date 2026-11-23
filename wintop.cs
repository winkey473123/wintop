// wintop：置顶窗口（PowerToys AlwaysOnTop 的复现版）。https://github.com/winkey473123/wintop
// 标题栏右键菜单里有"总在最前面"；置顶窗口周围画彩色圆角边框；托盘设置里可调颜色/不透明度/粗细/圆角。
// 单文件、零依赖、跑在 Win11 自带的 .NET Framework 上。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

static class Native
{
    public const int GWL_EXSTYLE = -20, WS_EX_TOPMOST = 0x8, WM_HOTKEY = 0x0312;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4;
    public const uint SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    public static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1), HWND_NOTOPMOST = (IntPtr)(-2), HWND_BROADCAST = (IntPtr)0xFFFF;

    public const int WS_POPUP = unchecked((int)0x80000000), WS_VISIBLE = 0x10000000, WS_DISABLED = 0x08000000;
    public const int WS_EX_LAYERED = 0x80000, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
    public const int WS_EX_TOOLWINDOW = 0x80, WS_EX_TOPMOST_FLAG = 0x8;
    public const int ULW_ALPHA = 2, SW_SHOWNA = 8;

    public const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_WINDOW_CORNER_PREFERENCE = 33;

    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003, EVENT_SYSTEM_MENUPOPUPSTART = 0x0006, EVENT_SYSTEM_MENUPOPUPEND = 0x0007;
    public const uint EVENT_SYSTEM_MOVESIZEEND = 0x000B, EVENT_SYSTEM_MINIMIZESTART = 0x0016, EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_DESTROY = 0x8001, EVENT_OBJECT_FOCUS = 0x8005, EVENT_OBJECT_LOCATIONCHANGE = 0x800B, EVENT_OBJECT_INVOKED = 0x8013;
    public const int OBJID_SYSMENU = -1;
    public const uint WINEVENT_OUTOFCONTEXT = 0, WINEVENT_SKIPOWNPROCESS = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int x, y; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    public struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, clrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MENUITEMINFO
    {
        public uint cbSize, fMask, fType, fState, wID;
        public IntPtr hSubMenu, hbmpChecked, hbmpUnchecked, dwItemData;
        public string dwTypeData;
        public uint cch;
        public IntPtr hbmpItem;
    }

    public delegate void WinEventDelegate(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time);

    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);
    [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateWindowEx(int exStyle, string cls, string name, int style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr inst, IntPtr param);
    [DllImport("user32.dll")] public static extern bool DestroyWindow(IntPtr hWnd);
    [DllImport("user32.dll")]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDC, ref POINT dst, ref SIZE size, IntPtr srcDC, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);

    [DllImport("user32.dll")] public static extern IntPtr GetDC(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern int ReleaseDC(IntPtr hWnd, IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] public static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] public static extern bool DeleteDC(IntPtr dc);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER bmi, uint usage, out IntPtr bits, IntPtr section, uint offset);

    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out RECT value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
    [DllImport("dwmapi.dll")] public static extern int DwmGetColorizationColor(out uint color, out bool opaque);

    [DllImport("user32.dll")] public static extern IntPtr GetSystemMenu(IntPtr hWnd, bool revert);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool InsertMenuItem(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool SetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern bool GetMenuItemInfo(IntPtr menu, uint item, bool byPosition, ref MENUITEMINFO info);
    [DllImport("user32.dll")] public static extern uint GetMenuState(IntPtr menu, uint item, uint flags);
    [DllImport("user32.dll")] public static extern bool RemoveMenu(IntPtr menu, uint item, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr hmod, WinEventDelegate proc, uint pid, uint tid, uint flags);
    [DllImport("user32.dll")] public static extern bool UnhookWinEvent(IntPtr hook);
}

static class Topmost
{
    // 置顶状态以窗口自身的 ex-style 为准：别的程序置顶过的窗口，这里也能正确翻转。
    public static bool Is(IntPtr h) { return (Native.GetWindowLong(h, Native.GWL_EXSTYLE) & Native.WS_EX_TOPMOST) != 0; }

    public static bool Set(IntPtr h, bool on)
    {
        return Native.SetWindowPos(h, on ? Native.HWND_TOPMOST : Native.HWND_NOTOPMOST, 0, 0, 0, 0,
            Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE);
    }
}

sealed class Settings
{
    public uint HotkeyMod = Native.MOD_CONTROL;
    public uint HotkeyVk = (uint)Keys.T;
    public bool EnableFrame = true;
    public bool ShowInMenu = true;           // 标题栏右键菜单里显示"总在最前面"
    public bool AccentColor = false;        // 颜色模式：Windows 默认（系统强调色）/ 自定义（默认 #3A5F4B）
    public int FrameColor = unchecked((int)0xFF3A5F4B);
    public int FrameOpacity = 100;
    public int FrameThickness = 4;
    public bool RoundCorners = true;

    public static string ConfigPath { get { return Path.Combine(Application.StartupPath, "settings.ini"); } }

    public static Settings Load(string path)
    {
        if (path == null) path = ConfigPath;
        var s = new Settings();
        try
        {
            if (!File.Exists(path)) { s.Save(path); return s; }
            foreach (string line in File.ReadAllLines(path))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                if (k == "hotkey_mod") s.HotkeyMod = uint.Parse(v);
                else if (k == "hotkey_vk") s.HotkeyVk = uint.Parse(v);
                else if (k == "enable_frame") s.EnableFrame = v == "1";
                else if (k == "show_in_menu") s.ShowInMenu = v == "1";
                else if (k == "accent_color") s.AccentColor = v == "1";
                else if (k == "frame_color") s.FrameColor = int.Parse(v);
                else if (k == "frame_opacity") s.FrameOpacity = int.Parse(v);
                else if (k == "frame_thickness") s.FrameThickness = int.Parse(v);
                else if (k == "round_corners") s.RoundCorners = v == "1";
            }
            // 手改文件容易写出注册不上的值，这里兜回默认
            if (s.HotkeyVk == 0) s.HotkeyVk = (uint)Keys.T;
            if (s.HotkeyMod == 0) s.HotkeyMod = Native.MOD_CONTROL;
            s.FrameOpacity = Math.Max(0, Math.Min(100, s.FrameOpacity));
            s.FrameThickness = Math.Max(1, Math.Min(20, s.FrameThickness));
        }
        catch { s = new Settings(); }
        return s;
    }

    public void Save(string path)
    {
        if (path == null) path = ConfigPath;
        try
        {
            File.WriteAllLines(path, new string[] {
                "hotkey_mod=" + HotkeyMod,
                "hotkey_vk=" + HotkeyVk,
                "enable_frame=" + (EnableFrame ? "1" : "0"),
                "show_in_menu=" + (ShowInMenu ? "1" : "0"),
                "accent_color=" + (AccentColor ? "1" : "0"),
                "frame_color=" + FrameColor,
                "frame_opacity=" + FrameOpacity,
                "frame_thickness=" + FrameThickness,
                "round_corners=" + (RoundCorners ? "1" : "0"),
            });
        }
        catch (Exception e) { MessageBox.Show("设置保存失败：" + e.Message, "wintop"); }
    }

    public string HotkeyText()
    {
        var sb = new StringBuilder();
        if ((HotkeyMod & Native.MOD_CONTROL) != 0) sb.Append("Ctrl+");
        if ((HotkeyMod & Native.MOD_ALT) != 0) sb.Append("Alt+");
        if ((HotkeyMod & Native.MOD_SHIFT) != 0) sb.Append("Shift+");
        sb.Append(new KeysConverter().ConvertToString((Keys)HotkeyVk));
        return sb.ToString();
    }

    // "Windows default" = 系统强调色（跟 PowerToys 一样取 DWM 的配色主色）
    public int EffectiveColor()
    {
        if (!AccentColor) return FrameColor;
        uint argb;
        bool opaque;
        if (Native.DwmGetColorizationColor(out argb, out opaque) != 0) return unchecked((int)0xFF0078D7);
        return unchecked((int)(argb | 0xFF000000));
    }
}

// 系统菜单里的"总在最前面"：把菜单项插进目标窗口的 system menu，点击靠 EVENT_OBJECT_INVOKED 捕获。
static class SystemMenu
{
    public const uint ToggleId = 0xEFE0;
    const uint TAG = 0x414F5450, MF_BYCOMMAND = 0, SC_CLOSE = 0xF060;
    const uint MIIM_STATE = 0x1, MIIM_ID = 0x2, MIIM_DATA = 0x20, MIIM_STRING = 0x40;
    const uint MFS_CHECKED = 0x8;
    const string TEXT = "总在最前面";

    static bool Exists(IntPtr menu) { return Native.GetMenuState(menu, ToggleId, MF_BYCOMMAND) != 0xFFFFFFFF; }

    static bool IsOurs(IntPtr menu)
    {
        var mii = new Native.MENUITEMINFO();
        mii.cbSize = (uint)Marshal.SizeOf(typeof(Native.MENUITEMINFO));
        mii.fMask = MIIM_DATA;
        return Native.GetMenuItemInfo(menu, ToggleId, false, ref mii) && mii.dwItemData == (IntPtr)TAG;
    }

    public static bool Present(IntPtr hwnd)
    {
        IntPtr menu = Native.GetSystemMenu(hwnd, false);
        return menu != IntPtr.Zero && Exists(menu) && IsOurs(menu);
    }

    // 关掉这个功能时，把所有窗口菜单里我们插进去的那项删掉
    public static void RemoveEverywhere()
    {
        Native.EnumWindows(delegate(IntPtr h, IntPtr p)
        {
            if (Present(h)) Native.RemoveMenu(Native.GetSystemMenu(h, false), ToggleId, MF_BYCOMMAND);
            return true;
        }, IntPtr.Zero);
    }

    public static void Update(IntPtr hwnd, bool show, bool pinned)
    {
        if (hwnd == IntPtr.Zero || !Native.IsWindow(hwnd)) return;
        IntPtr menu = Native.GetSystemMenu(hwnd, false);
        if (menu == IntPtr.Zero) return;

        bool exists = Exists(menu);
        if (!show)
        {
            if (exists && IsOurs(menu)) Native.RemoveMenu(menu, ToggleId, MF_BYCOMMAND);
            return;
        }

        var mii = new Native.MENUITEMINFO();
        mii.cbSize = (uint)Marshal.SizeOf(typeof(Native.MENUITEMINFO));
        mii.fMask = MIIM_ID | MIIM_STATE | MIIM_STRING | MIIM_DATA;
        mii.wID = ToggleId;
        mii.fState = pinned ? MFS_CHECKED : 0;
        mii.dwTypeData = TEXT;
        mii.cch = (uint)TEXT.Length;
        mii.dwItemData = (IntPtr)TAG;

        if (!exists) Native.InsertMenuItem(menu, SC_CLOSE, false, ref mii);
        else if (IsOurs(menu)) { mii.fMask = MIIM_STATE | MIIM_STRING; Native.SetMenuItemInfo(menu, ToggleId, false, ref mii); }
    }
}

// 置顶窗口外面的那圈边框：分层 popup + 逐像素 alpha，环形路径（外圆角矩形减内圆角矩形）。
// 位置用 DWM 视觉外框外扩 thickness，跟 PowerToys 一样排在目标窗口下面一层，不挡窗口内容。
sealed class BorderWindow
{
    IntPtr hwnd;
    int lastW = -1, lastH = -1, lastThickness = -1, lastColor, lastOpacity = -1, lastRadius = -1;
    bool shown;

    public bool Create()
    {
        hwnd = Native.CreateWindowEx(
            Native.WS_EX_LAYERED | Native.WS_EX_TRANSPARENT | Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW | Native.WS_EX_TOPMOST_FLAG,
            "STATIC", null, Native.WS_POPUP | Native.WS_DISABLED,
            0, 0, 1, 1, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        return hwnd != IntPtr.Zero;
    }


    public IntPtr Handle { get { return hwnd; } }

    public void Destroy()
    {
        if (hwnd != IntPtr.Zero) { Native.DestroyWindow(hwnd); hwnd = IntPtr.Zero; }
    }

    public void Hide()
    {
        if (hwnd != IntPtr.Zero) { Native.ShowWindow(hwnd, 0); shown = false; }
    }

    public void Apply(IntPtr target, Settings s)
    {
        if (hwnd == IntPtr.Zero) return;
        int t = Math.Max(1, Math.Min(20, s.FrameThickness));
        Native.RECT r;
        if (!FrameRect(target, t, out r)) { Hide(); return; }
        int w = r.Right - r.Left, h = r.Bottom - r.Top;
        if (w <= 0 || h <= 0) { Hide(); return; }

        int color = s.EffectiveColor();
        int radius = s.RoundCorners ? CornerRadius(target) : 0;
        if (w != lastW || h != lastH || t != lastThickness || color != lastColor || s.FrameOpacity != lastOpacity || radius != lastRadius)
        {
            Paint(w, h, t, radius, color, s.FrameOpacity);
            lastW = w; lastH = h; lastThickness = t; lastColor = color; lastOpacity = s.FrameOpacity; lastRadius = radius;
        }
        // 排在目标窗口正下方：边框画在窗口外框之外，所以看得见，又不会盖住窗口内容
        Native.SetWindowPos(hwnd, IntPtr.Zero, r.Left, r.Top, w, h, Native.SWP_NOZORDER | Native.SWP_NOACTIVATE);
        if (!shown) { Native.ShowWindow(hwnd, Native.SW_SHOWNA); shown = true; }
    }

    static bool FrameRect(IntPtr hwnd, int thickness, out Native.RECT rect)
    {
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out rect, Marshal.SizeOf(typeof(Native.RECT))) != 0
            && !Native.GetWindowRect(hwnd, out rect)) return false;
        rect.Left -= thickness;
        rect.Top -= thickness;
        rect.Right += thickness;
        rect.Bottom += thickness;
        return true;
    }

    // Win11 的窗口圆角半径：ROUND=8、ROUNDSMALL=4、DEFAULT=8，其它（Win10/不要圆角）=0
    static int CornerRadius(IntPtr hwnd)
    {
        int pref;
        if (Native.DwmGetWindowAttribute(hwnd, Native.DWMWA_WINDOW_CORNER_PREFERENCE, out pref, 4) != 0) return 0;
        if (pref == 2) return 8;
        if (pref == 3) return 4;
        if (pref == 0) return 8;
        return 0;
    }

    void Paint(int w, int h, int thickness, int radius, int argb, int opacity)
    {
        IntPtr screen = Native.GetDC(IntPtr.Zero);
        IntPtr memDC = Native.CreateCompatibleDC(screen);
        var bmi = new Native.BITMAPINFOHEADER();
        bmi.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
        bmi.biWidth = w;
        bmi.biHeight = -h;
        bmi.biPlanes = 1;
        bmi.biBitCount = 32;
        IntPtr bits;
        IntPtr dib = Native.CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
        Native.ReleaseDC(IntPtr.Zero, screen);
        IntPtr old = Native.SelectObject(memDC, dib);

        Color c = Color.FromArgb(argb);
        int alpha = Math.Max(0, Math.Min(100, opacity)) * 255 / 100;

        using (var bmp = new Bitmap(w, h, w * 4, PixelFormat.Format32bppArgb, bits))
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            using (var path = RingPath(w, h, thickness, radius))
            using (var brush = new SolidBrush(Color.FromArgb(alpha, c.R, c.G, c.B)))
                g.FillPath(brush, path);
        }

        var dst = new Native.POINT();
        var src = new Native.POINT();
        var sz = new Native.SIZE { cx = w, cy = h };
        var blend = new Native.BLENDFUNCTION { BlendOp = 0, SourceConstantAlpha = 255, AlphaFormat = 1 };
        Native.UpdateLayeredWindow(hwnd, IntPtr.Zero, ref dst, ref sz, memDC, ref src, 0, ref blend, Native.ULW_ALPHA);

        Native.SelectObject(memDC, old);
        Native.DeleteObject(dib);
        Native.DeleteDC(memDC);
    }

    static GraphicsPath RingPath(int w, int h, int thickness, int radius)
    {
        var path = new GraphicsPath(FillMode.Alternate);
        path.AddPath(RoundRect(0, 0, w, h, radius + thickness), false);
        path.AddPath(RoundRect(thickness, thickness, w - 2 * thickness, h - 2 * thickness, radius), false);
        return path;
    }

    static GraphicsPath RoundRect(float x, float y, float w, float h, float r)
    {
        var p = new GraphicsPath();
        if (r <= 0.5f || w <= 0 || h <= 0) { p.AddRectangle(new RectangleF(x, y, Math.Max(0, w), Math.Max(0, h))); return p; }
        float d = Math.Min(r * 2, Math.Min(w, h));
        p.AddArc(x, y, d, d, 180, 90);
        p.AddArc(x + w - d, y, d, d, 270, 90);
        p.AddArc(x + w - d, y + h - d, d, d, 0, 90);
        p.AddArc(x, y + h - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }
}

sealed class HotkeyWindow : NativeWindow
{
    const int ID = 1;
    readonly App app;

    public HotkeyWindow(App app) { this.app = app; CreateHandle(new CreateParams()); }

    public bool Register(uint mod, uint vk)
    {
        Native.UnregisterHotKey(Handle, ID);
        return Native.RegisterHotKey(Handle, ID, mod, vk);
    }

    public void Unregister() { Native.UnregisterHotKey(Handle, ID); }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY && m.WParam == (IntPtr)ID) app.ToggleForeground();
        else if (m.Msg == (int)Program.WM_SHOW_SETTINGS) app.ShowSettings();   // 第二次双击 exe
        base.WndProc(ref m);
    }
}

sealed class Pinned
{
    public IntPtr Hwnd;
    public BorderWindow Border;
}

sealed class SettingsForm : Form
{
    readonly App app;
    readonly TextBox hk, colorBox;
    readonly ComboBox colorMode;
    readonly Button pickButton;
    readonly NumericUpDown opacityBox, thicknessBox;
    bool syncing;

    public SettingsForm(App app)
    {
        this.app = app;
        Settings s = app.Settings;

        Text = "wintop 设置";
        ClientSize = new Size(380, 322);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        TopMost = true;   // 不然会被置顶窗口盖住
        if (app.Icon != null) Icon = app.Icon;

        Controls.Add(Lbl("置顶/取消置顶", 16, 20));
        hk = new TextBox { Left = 148, Top = 16, Width = 196, ReadOnly = true, TabStop = false, Text = s.HotkeyText(), TextAlign = HorizontalAlignment.Center };
        hk.KeyDown += OnHotkeyKey;
        hk.Enter += delegate { app.SuspendHotkey(); hk.Text = "按下组合键…"; hk.ForeColor = SystemColors.GrayText; };
        hk.Leave += delegate { hk.Text = app.Settings.HotkeyText(); hk.ForeColor = SystemColors.WindowText; app.ResumeHotkey(); };
        Controls.Add(hk);

        var frame = new CheckBox { Left = 148, Top = 54, Width = 196, Text = "显示置顶边框", Checked = s.EnableFrame };
        frame.CheckedChanged += delegate { app.SetFrameEnabled(((CheckBox)frame).Checked); };
        Controls.Add(frame);

        Controls.Add(Lbl("颜色模式", 16, 90));
        colorMode = new ComboBox { Left = 148, Top = 86, Width = 196, DropDownStyle = ComboBoxStyle.DropDownList };
        colorMode.Items.Add("Windows 默认（系统强调色）");
        colorMode.Items.Add("自定义颜色");
        colorMode.SelectedIndex = s.AccentColor ? 0 : 1;
        colorMode.SelectedIndexChanged += OnColorMode;
        Controls.Add(colorMode);

        Controls.Add(Lbl("颜色", 16, 124));
        colorBox = new TextBox { Left = 148, Top = 120, Width = 120, TextAlign = HorizontalAlignment.Center };
        colorBox.KeyDown += OnColorKey;
        colorBox.Leave += delegate { ApplyHex(); };
        Controls.Add(colorBox);

        pickButton = new Button { Left = 274, Top = 119, Width = 70, Height = 24, Text = "取色…" };
        pickButton.Click += OnPickColor;
        Controls.Add(pickButton);
        SyncColorControls();

        Controls.Add(Lbl("不透明度", 16, 158));
        opacityBox = new NumericUpDown { Left = 148, Top = 154, Width = 80, Minimum = 0, Maximum = 100, Value = s.FrameOpacity };
        opacityBox.ValueChanged += delegate { app.SetFrameOptions(null, null, (int)opacityBox.Value, null, null); };
        Controls.Add(opacityBox);
        Controls.Add(new Label { Left = 234, Top = 158, AutoSize = true, Text = "%" });

        Controls.Add(Lbl("粗细", 16, 192));
        thicknessBox = new NumericUpDown { Left = 148, Top = 188, Width = 80, Minimum = 1, Maximum = 20, Value = s.FrameThickness };
        thicknessBox.ValueChanged += delegate { app.SetFrameOptions(null, null, null, (int)thicknessBox.Value, null); };
        Controls.Add(thicknessBox);
        Controls.Add(new Label { Left = 234, Top = 192, AutoSize = true, Text = "px" });

        var round = new CheckBox { Left = 148, Top = 222, Width = 196, Text = "启用圆角", Checked = s.RoundCorners };
        round.CheckedChanged += delegate { app.SetFrameOptions(null, null, null, null, ((CheckBox)round).Checked); };
        Controls.Add(round);

        var inMenu = new CheckBox { Left = 148, Top = 252, Width = 196, Text = "标题栏菜单显示“总在最前面”", Checked = s.ShowInMenu };
        inMenu.CheckedChanged += delegate { app.SetShowInMenu(((CheckBox)inMenu).Checked); };
        Controls.Add(inMenu);

        var auto = new CheckBox { Left = 148, Top = 282, Width = 196, Text = "开机自启", Checked = app.IsAutostart };
        auto.CheckedChanged += delegate { app.SetAutostart(((CheckBox)auto).Checked); };
        Controls.Add(auto);

        Deactivate += delegate { app.ResumeHotkey(); };   // 切走时不管焦点在不在快捷键框，都把热键恢复
    }

    static Label Lbl(string text, int x, int y) { return new Label { Left = x, Top = y, AutoSize = true, Text = text }; }

    // 打开时不抢焦点：直接显示当前快捷键；点进框里才进入"按下组合键…"状态
    protected override void OnShown(EventArgs e) { base.OnShown(e); hk.Text = app.Settings.HotkeyText(); }
    protected override void OnFormClosing(FormClosingEventArgs e) { base.OnFormClosing(e); app.ResumeHotkey(); }

    void OnHotkeyKey(object sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey || e.KeyCode == Keys.Menu
            || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin) return;

        uint mod = 0;
        if ((e.Modifiers & Keys.Control) != 0) mod |= Native.MOD_CONTROL;
        if ((e.Modifiers & Keys.Alt) != 0) mod |= Native.MOD_ALT;
        if ((e.Modifiers & Keys.Shift) != 0) mod |= Native.MOD_SHIFT;
        // 不带修饰键时只接受功能键这类非输入键，避免把普通打字键抢成全局热键
        if (mod == 0 && e.KeyCode < Keys.F1) return;

        app.SetHotkey(mod, (uint)e.KeyCode);
        hk.Text = app.Settings.HotkeyText();
        hk.ForeColor = SystemColors.WindowText;
    }

    void OnColorMode(object sender, EventArgs e)
    {
        app.SetFrameOptions(((ComboBox)sender).SelectedIndex == 0, null, null, null, null);
        SyncColorControls();
    }

    void OnColorKey(object sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; ApplyHex(); }
        else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; SyncColorControls(); }
    }

    // 直接输入/粘贴 #RRGGBB 就生效；格式不对就还原成当前颜色
    void ApplyHex()
    {
        int argb;
        if (!syncing && TryParseHex(colorBox.Text, out argb))
        {
            if (app.Settings.AccentColor) colorMode.SelectedIndex = 1;   // 粘了自定义色就自动切过去
            app.SetFrameOptions(false, argb, null, null, null);
        }
        SyncColorControls();
    }

    void OnPickColor(object sender, EventArgs e)
    {
        using (var d = new ColorDialog { Color = Color.FromArgb(app.Settings.FrameColor), FullOpen = true })
        {
            if (d.ShowDialog(this) != DialogResult.OK) return;
            if (app.Settings.AccentColor) colorMode.SelectedIndex = 1;   // 选了具体颜色就切到自定义，不然看不出效果
            app.SetFrameOptions(false, d.Color.ToArgb(), null, null, null);
            SyncColorControls();
        }
    }

    void SyncColorControls()
    {
        syncing = true;
        Settings s = app.Settings;
        Color c = Color.FromArgb(s.AccentColor ? s.EffectiveColor() : s.FrameColor);
        colorBox.Text = string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        colorBox.BackColor = c;
        colorBox.ForeColor = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) > 140 ? Color.Black : Color.White;

        syncing = false;
    }

    static bool TryParseHex(string text, out int argb)
    {
        argb = 0;
        if (text == null) return false;
        string s = text.Trim().TrimStart('#').Trim();
        if (s.Length != 6) return false;
        int v;
        if (!int.TryParse(s, System.Globalization.NumberStyles.HexNumber, null, out v)) return false;
        argb = unchecked((int)(0xFF000000u | (uint)v));
        return true;
    }
}

sealed class App : ApplicationContext
{
    const string RUN_PATH = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string RUN_NAME = "wintop";
    const string TRAY_TEXT = "wintop";

    readonly NotifyIcon tray;
    readonly Icon icon;
    readonly HotkeyWindow hotkey;
    readonly System.Windows.Forms.Timer timer;
    readonly Dictionary<IntPtr, Pinned> pinned = new Dictionary<IntPtr, Pinned>();
    readonly Native.WinEventDelegate winEventProc;   // 必须保住引用，否则回调会被 GC
    readonly List<IntPtr> winEventHooks = new List<IntPtr>();
    SettingsForm form;
    IntPtr lastMenuWindow, lastForeground;

    public Settings Settings { get; private set; }

    public Icon Icon { get { return icon; } }

    public App()
    {
        Settings = Settings.Load(null);
        icon = LoadIcon();
        hotkey = new HotkeyWindow(this);
        winEventProc = OnWinEvent;

        var menu = new ContextMenuStrip();
        menu.Items.Add("设置…", null, OnSettings);
        menu.Items.Add("退出", null, OnExit);
        tray = new NotifyIcon
        {
            Icon = icon,
            Text = TRAY_TEXT,
            ContextMenuStrip = menu,
            Visible = true,
        };
        tray.MouseClick += OnTrayClick;

        // 兜底：窗口关掉/最小化/TOP 被系统重置这类事，500ms 内一定纠正
        timer = new System.Windows.Forms.Timer { Interval = 500 };
        timer.Tick += OnTick;
        timer.Start();

        SubscribeWinEvents();
        SystemMenu.Update(Native.GetForegroundWindow(), Settings.ShowInMenu, false);

        if (!hotkey.Register(Settings.HotkeyMod, Settings.HotkeyVk))
            tray.ShowBalloonTip(6000, "wintop", "热键 " + Settings.HotkeyText() + " 注册失败（被别的程序占用），去托盘设置里换一个。", ToolTipIcon.Warning);
    }

    // 托盘/标题栏用 app.ico（16~64 是 BMP 条目，System.Drawing.Icon 能直接读）；没有就退回 exe 自带图标
    static Icon LoadIcon()
    {
        try
        {
            string path = Path.Combine(Application.StartupPath, "app.ico");
            if (File.Exists(path)) return new Icon(path, SystemInformation.SmallIconSize);
        }
        catch { }
        try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
        catch { return SystemIcons.Application; }
    }

    void SubscribeWinEvents()
    {
        uint[] events = {
            Native.EVENT_OBJECT_LOCATIONCHANGE, Native.EVENT_SYSTEM_MINIMIZESTART, Native.EVENT_SYSTEM_MINIMIZEEND,
            Native.EVENT_SYSTEM_MOVESIZEEND, Native.EVENT_SYSTEM_FOREGROUND, Native.EVENT_OBJECT_DESTROY,
            Native.EVENT_OBJECT_FOCUS, Native.EVENT_SYSTEM_MENUPOPUPSTART, Native.EVENT_SYSTEM_MENUPOPUPEND,
            Native.EVENT_OBJECT_INVOKED,
        };
        foreach (uint e in events)
        {
            IntPtr hook = Native.SetWinEventHook(e, e, IntPtr.Zero, winEventProc, 0, 0,
                Native.WINEVENT_OUTOFCONTEXT | Native.WINEVENT_SKIPOWNPROCESS);
            if (hook != IntPtr.Zero) winEventHooks.Add(hook);
        }
    }

    void OnWinEvent(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild, uint thread, uint time)
    {
        if (evt == Native.EVENT_SYSTEM_MENUPOPUPSTART)
        {
            // 不靠 idObject 判断（不同 Windows 版本给的 OBJID 不一样），直接看这个窗口的菜单里有没有我们那项
            if (hwnd != IntPtr.Zero)
            {
                SystemMenu.Update(hwnd, Settings.ShowInMenu, IsPinned(hwnd));
                if (SystemMenu.Present(hwnd)) lastMenuWindow = hwnd;
            }
            return;
        }
        if (evt == Native.EVENT_SYSTEM_MENUPOPUPEND)
        {
            if (idObject == Native.OBJID_SYSMENU && hwnd == lastMenuWindow) lastMenuWindow = IntPtr.Zero;
            return;
        }
        if (evt == Native.EVENT_OBJECT_INVOKED)
        {
            if (idChild != (int)SystemMenu.ToggleId) return;
            IntPtr target = ResolveMenuTarget(hwnd);
            if (target != IntPtr.Zero) Toggle(target);
            return;
        }
        if (hwnd == IntPtr.Zero) return;

        switch (evt)
        {
            case Native.EVENT_OBJECT_LOCATIONCHANGE:
            case Native.EVENT_SYSTEM_MOVESIZEEND:
                UpdateBorder(hwnd);
                break;
            case Native.EVENT_SYSTEM_MINIMIZESTART:
                if (pinned.ContainsKey(hwnd) && pinned[hwnd].Border != null) pinned[hwnd].Border.Hide();
                break;
            case Native.EVENT_SYSTEM_MINIMIZEEND:
                if (pinned.ContainsKey(hwnd))
                {
                    Topmost.Set(hwnd, true);   // 有些程序还原后会把 TOPMOST 丢掉
                    UpdateBorder(hwnd);
                }
                break;
            case Native.EVENT_OBJECT_DESTROY:
                Drop(hwnd);
                break;
            case Native.EVENT_OBJECT_FOCUS:
                foreach (IntPtr h in pinned.Keys)   // TOP 被别的程序重置时补回来
                    if (!Topmost.Is(h)) Topmost.Set(h, true);
                break;
            case Native.EVENT_SYSTEM_FOREGROUND:
                // 像 PowerToys 一样，窗口一拿到前台就把菜单项插进去，右键菜单弹出时它已经在里面
                lastForeground = hwnd;
                SystemMenu.Update(hwnd, Settings.ShowInMenu, IsPinned(hwnd));
                break;
        }
    }

    // 谁的系统菜单里有我们那项，就把命令发给谁
    IntPtr ResolveMenuTarget(IntPtr eventWindow)
    {
        if (lastMenuWindow != IntPtr.Zero && Native.IsWindow(lastMenuWindow) && SystemMenu.Present(lastMenuWindow)) return lastMenuWindow;
        if (lastForeground != IntPtr.Zero && Native.IsWindow(lastForeground) && SystemMenu.Present(lastForeground)) return lastForeground;
        if (eventWindow != IntPtr.Zero && SystemMenu.Present(eventWindow)) return eventWindow;
        IntPtr fg = Native.GetForegroundWindow();
        if (fg != IntPtr.Zero && SystemMenu.Present(fg)) return fg;
        return IntPtr.Zero;
    }

    public void ToggleForeground()
    {
        IntPtr h = Native.GetForegroundWindow();
        if (h == IntPtr.Zero || h == Native.GetDesktopWindow() || IsOwnWindow(h)) return;
        Toggle(h);
    }

    void Toggle(IntPtr h)
    {
        if (h == IntPtr.Zero || h == Native.GetDesktopWindow() || IsOwnWindow(h)) return;
        if (pinned.ContainsKey(h)) Unpin(h);
        else Pin(h);
    }

    static bool IsOwnWindow(IntPtr h)
    {
        uint pid;
        Native.GetWindowThreadProcessId(h, out pid);
        return pid == (uint)System.Diagnostics.Process.GetCurrentProcess().Id;
    }

    bool IsPinned(IntPtr h) { return pinned.ContainsKey(h); }

    void Pin(IntPtr h)
    {
        if (!Topmost.Set(h, true))
        {
            // 提升权限的窗口（任务管理器等）UIPI 会拒绝我们改它的 z-order
            tray.ShowBalloonTip(6000, "wintop", "这个窗口以管理员权限运行，wintop 无法置顶它。需要的话请以管理员身份运行 wintop。", ToolTipIcon.Warning);
            return;
        }
        var p = new Pinned { Hwnd = h };
        pinned[h] = p;
        if (Settings.EnableFrame)
        {
            p.Border = new BorderWindow();
            if (!p.Border.Create()) p.Border = null;
            else p.Border.Apply(h, Settings);
        }
        SystemMenu.Update(h, Settings.ShowInMenu, true);
    }

    void Unpin(IntPtr h)
    {
        Pinned p;
        if (!pinned.TryGetValue(h, out p)) return;
        if (p.Border != null) { p.Border.Destroy(); p.Border = null; }
        Topmost.Set(h, false);
        pinned.Remove(h);
        SystemMenu.Update(h, Settings.ShowInMenu, false);
    }

    void Drop(IntPtr h)
    {
        Pinned p;
        if (!pinned.TryGetValue(h, out p)) return;
        if (p.Border != null) { p.Border.Destroy(); p.Border = null; }
        pinned.Remove(h);
    }

    void UpdateBorder(IntPtr h)
    {
        Pinned p;
        if (!pinned.TryGetValue(h, out p) || p.Border == null) return;
        p.Border.Apply(h, Settings);
    }

    void OnTick(object sender, EventArgs e)
    {
        if (pinned.Count == 0) return;
        foreach (IntPtr h in new List<IntPtr>(pinned.Keys))
        {
            if (!Native.IsWindow(h)) { Drop(h); continue; }
            if (Native.IsIconic(h) || !Native.IsWindowVisible(h)) { if (pinned[h].Border != null) pinned[h].Border.Hide(); continue; }
            if (!Topmost.Is(h)) Topmost.Set(h, true);
            UpdateBorder(h);
        }
    }

    // ---- 设置项 ----
    public void SetFrameEnabled(bool on)
    {
        Settings.EnableFrame = on;
        Settings.Save(null);
        foreach (Pinned p in pinned.Values)
        {
            if (on)
            {
                if (p.Border == null) { p.Border = new BorderWindow(); if (!p.Border.Create()) { p.Border = null; continue; } }
                p.Border.Apply(p.Hwnd, Settings);
            }
            else if (p.Border != null) { p.Border.Destroy(); p.Border = null; }
        }
    }

    public void SetFrameOptions(bool? accent, int? color, int? opacity, int? thickness, bool? round)
    {
        if (accent.HasValue) Settings.AccentColor = accent.Value;
        if (color.HasValue) Settings.FrameColor = color.Value;
        if (opacity.HasValue) Settings.FrameOpacity = Math.Max(0, Math.Min(100, opacity.Value));
        if (thickness.HasValue) Settings.FrameThickness = Math.Max(1, Math.Min(20, thickness.Value));
        if (round.HasValue) Settings.RoundCorners = round.Value;
        Settings.Save(null);
        foreach (Pinned p in pinned.Values) if (p.Border != null) p.Border.Apply(p.Hwnd, Settings);
    }

    public void SetHotkey(uint mod, uint vk)
    {
        uint oldMod = Settings.HotkeyMod, oldVk = Settings.HotkeyVk;
        hotkey.Unregister();
        if (!hotkey.Register(mod, vk))
        {
            hotkey.Register(oldMod, oldVk);
            MessageBox.Show("这个快捷键被占用了，换一个。", "wintop");
            return;
        }
        Settings.HotkeyMod = mod;
        Settings.HotkeyVk = vk;
        Settings.Save(null);
    }

    public void SuspendHotkey() { hotkey.Unregister(); }

    public void ResumeHotkey()
    {
        if (!hotkey.Register(Settings.HotkeyMod, Settings.HotkeyVk))
            tray.ShowBalloonTip(6000, "wintop", "热键 " + Settings.HotkeyText() + " 注册失败（被别的程序占用）。", ToolTipIcon.Warning);
    }

    public void SetShowInMenu(bool on)
    {
        Settings.ShowInMenu = on;
        Settings.Save(null);
        foreach (IntPtr h in pinned.Keys) SystemMenu.Update(h, on, true);
        if (!on) SystemMenu.RemoveEverywhere();   // 已经插进别的窗口菜单里的也一并清掉
    }

    public bool IsAutostart
    {
        get
        {
            using (var key = Registry.CurrentUser.OpenSubKey(RUN_PATH))
                return key != null && (string)key.GetValue(RUN_NAME) == "\"" + Application.ExecutablePath + "\"";
        }
    }

    public void SetAutostart(bool on)
    {
        using (var key = Registry.CurrentUser.CreateSubKey(RUN_PATH))
        {
            if (on) key.SetValue(RUN_NAME, "\"" + Application.ExecutablePath + "\"");
            else key.DeleteValue(RUN_NAME, false);
        }
    }

    void OnSettings(object sender, EventArgs e) { ShowSettings(); }

    void OnTrayClick(object sender, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowSettings(); }

    public void ShowSettings()
    {
        if (form == null || form.IsDisposed) form = new SettingsForm(this);
        form.Show();
        form.Activate();
    }

    void OnExit(object sender, EventArgs e)
    {
        timer.Stop();
        if (form != null) { form.Dispose(); form = null; }   // 先关设置窗：它的 FormClosing 还要用热键句柄
        foreach (IntPtr hook in winEventHooks) Native.UnhookWinEvent(hook);
        winEventHooks.Clear();
        hotkey.DestroyHandle();
        foreach (IntPtr h in new List<IntPtr>(pinned.Keys)) Unpin(h);
        tray.Visible = false;
        tray.Dispose();
        ExitThread();
    }
}

static class Program
{
    public static readonly uint WM_SHOW_SETTINGS = Native.RegisterWindowMessage("wintop-show-settings");

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length == 1 && args[0] == "--self-test") return SelfTest();

        bool created;
        using (var mutex = new Mutex(true, @"Local\wintop-9A4E2C61", out created))
        {
            if (!created)   // 已经在跑：让那个实例把设置窗口弹出来
            {
                Native.PostMessage(Native.HWND_BROADCAST, WM_SHOW_SETTINGS, IntPtr.Zero, IntPtr.Zero);
                return 0;
            }
            Application.EnableVisualStyles();
            var app = new App();
            if (args.Length == 1 && args[0] == "--settings") app.ShowSettings();   // 快捷方式直接开设置
            Application.Run(app);
        }
        return 0;
    }

    // 自检：置顶能开关、设置能存能读、边框窗口能画在正确的位置、系统菜单项能插进去。
    // 退出码 0=通过，2/3/4=置顶哪步不对，5=设置读写不对，6=边框窗口建不出来，7=边框位置不对，8=菜单项没插上。
    static int SelfTest()
    {
        using (var f = new Form
        {
            Text = "wintop self-test",
            StartPosition = FormStartPosition.Manual,
            Location = new Point(300, 200),
            Size = new Size(400, 300),
        })
        {
            f.Show();

            bool before = Topmost.Is(f.Handle);
            Topmost.Set(f.Handle, true);
            bool on = Topmost.Is(f.Handle);
            Topmost.Set(f.Handle, false);
            bool off = Topmost.Is(f.Handle);
            if (before) return 2;
            if (!on) return 3;
            if (off) return 4;

            var s = new Settings();
            var border = new BorderWindow();
            if (!border.Create()) return 6;
            border.Apply(f.Handle, s);

            Native.RECT frame;
            if (Native.DwmGetWindowAttribute(f.Handle, Native.DWMWA_EXTENDED_FRAME_BOUNDS, out frame, Marshal.SizeOf(typeof(Native.RECT))) != 0)
                Native.GetWindowRect(f.Handle, out frame);
            Native.RECT br;
            Native.GetWindowRect(border.Handle, out br);
            int t = s.FrameThickness;
            bool placed = Math.Abs(br.Left - (frame.Left - t)) <= 2 && Math.Abs(br.Top - (frame.Top - t)) <= 2
                          && Math.Abs(br.Right - (frame.Right + t)) <= 2 && Math.Abs(br.Bottom - (frame.Bottom + t)) <= 2;
            border.Destroy();

            SystemMenu.Update(f.Handle, true, true);
            bool menuOk = SystemMenu.Present(f.Handle);
            Native.RemoveMenu(Native.GetSystemMenu(f.Handle, false), SystemMenu.ToggleId, 0);

            f.Hide();
            if (!placed) return 7;
            if (!menuOk) return 8;
        }

        string tmp = Path.Combine(Path.GetTempPath(), "wintop-selftest.ini");
        try
        {
            var a = new Settings { HotkeyMod = Native.MOD_ALT, HotkeyVk = (uint)Keys.F9, FrameOpacity = 55, FrameThickness = 6, FrameColor = unchecked((int)0xFF112233), RoundCorners = false };
            a.Save(tmp);
            Settings b = Settings.Load(tmp);
            if (b.HotkeyMod != a.HotkeyMod || b.HotkeyVk != a.HotkeyVk || b.FrameOpacity != a.FrameOpacity
                || b.FrameThickness != a.FrameThickness || b.FrameColor != a.FrameColor || b.RoundCorners != a.RoundCorners) return 5;
        }
        finally { try { File.Delete(tmp); } catch { } }
        return 0;
    }
}
