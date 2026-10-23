using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace InFalsusRating;

public static class Hotkeys
{

        public const int ModCtrl = 1, ModShift = 2, ModAlt = 4;

    const int VK_BACKSPACE = 0x08, VK_TAB = 0x09, VK_ENTER = 0x0D, VK_PAUSE = 0x13,
              VK_CAPSLOCK = 0x14, VK_ESCAPE = 0x1B, VK_SPACE = 0x20,
              VK_PRIOR = 0x21, VK_NEXT = 0x22, VK_END = 0x23, VK_HOME = 0x24,
              VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28,
              VK_SNAPSHOT = 0x2C, VK_INSERT = 0x2D, VK_DELETE = 0x2E,
              VK_NUMLOCK = 0x90, VK_SCROLL = 0x91;

        public sealed class Def
    {
        public readonly string Id;
        public readonly string Name;
        public readonly int DefVk;
        public readonly int DefMods;

        public Def(string id, string name, int vk, int mods)
        {
            Id = id; Name = name; DefVk = vk; DefMods = mods;
        }
    }

    public static readonly Def[] Defs =
    {
        new Def("panel",      "面板",         0x75, 0),
        new Def("banner",     "横幅显隐",     0x77, 0),
        new Def("export_b30", "导出 B30",     0x78, 0),
    };

    public static int Count => Defs.Length;

        public sealed class Binding
    {
        public readonly int Vk, Mods;
        public Binding(int vk, int mods) { Vk = vk; Mods = mods; }
    }

    static volatile Binding[] _cur = BuildDefaults();
    static readonly object _loadLock = new object();

    static Binding[] BuildDefaults()
    {
        var a = new Binding[Defs.Length];
        for (int i = 0; i < Defs.Length; i++) a[i] = new Binding(Defs[i].DefVk, Defs[i].DefMods);
        return a;
    }

        public static Binding[] Snapshot() => _cur;

        public static volatile bool Capturing;

    public static int VkOf(int i) => _cur[i].Vk;
    public static int ModsOf(int i) => _cur[i].Mods;
    public static int IndexOf(string id)
    {
        for (int i = 0; i < Defs.Length; i++)
            if (string.Equals(Defs[i].Id, id, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    public static string Text(int i)
    {
        var b = _cur[i];
        return TextOf(b.Vk, b.Mods);
    }

        public static bool IsDefault(int i)
    {
        var b = _cur[i];
        return b.Vk == Defs[i].DefVk && b.Mods == Defs[i].DefMods;
    }

        public static string OverrideText(int i) => IsDefault(i) ? null : Text(i);

        public static bool Set(int i, int vk, int mods, out string err)
    {
        err = null;
        if (i < 0 || i >= Defs.Length) { err = "没有这个动作"; return false; }
        if (!IsBindable(vk)) { err = "这个键不能当快捷键（鼠标 / 修饰键 / 系统键）"; return false; }
        mods &= ModCtrl | ModShift | ModAlt;

        var cur = _cur;
        for (int j = 0; j < Defs.Length; j++)
        {
            if (j == i) continue;
            if (cur[j].Vk == vk && cur[j].Mods == mods)
            {
                err = $"和「{Defs[j].Name}」的 {TextOf(vk, mods)} 撞了";
                return false;
            }
        }

        var next = new Binding[Defs.Length];
        Array.Copy(cur, next, cur.Length);
        next[i] = new Binding(vk, mods);
        _cur = next;
        return true;
    }

        public static void ResetAll() => _cur = BuildDefaults();

        public static void LoadOverride(string id, string text)
    {
        int i = IndexOf(id);
        if (i < 0)
        {
            Plugin.Logger?.LogWarning($"[热键] settings.json 里有个认不出的动作 \"{id}\"，跳过");
            return;
        }
        if (!TryParse(text, out int vk, out int mods))
        {
            Plugin.Logger?.LogWarning($"[热键] \"{id}\" 的键位 \"{text}\" 认不出，按出厂键走");
            return;
        }
        lock (_loadLock)
        {
            if (!Set(i, vk, mods, out string err))
                Plugin.Logger?.LogWarning($"[热键] \"{id}\" 的键位没读进来：{err}");
        }
    }

        public static bool IsBindable(int vk)
    {
        if (vk <= 0 || vk > 0xFE) return false;
        if (vk >= 0x01 && vk <= 0x06) return false;
        if (vk == 0x10 || vk == 0x11 || vk == 0x12) return false;
        if (vk >= 0xA0 && vk <= 0xA5) return false;
        if (vk == 0x5B || vk == 0x5C || vk == 0x5D) return false;
        return true;
    }

        static string ModPrefix(int mods)
    {
        var sb = new StringBuilder();
        if ((mods & ModCtrl) != 0) sb.Append("Ctrl+");
        if ((mods & ModShift) != 0) sb.Append("Shift+");
        if ((mods & ModAlt) != 0) sb.Append("Alt+");
        return sb.ToString();
    }

    public static string TextOf(int vk, int mods) => ModPrefix(mods) + KeyName(vk);

        public static string KeyName(int vk)
    {
        if (vk >= 0x70 && vk <= 0x87) return "F" + (vk - 0x70 + 1);
        if (vk >= 0x30 && vk <= 0x39) return ((char)vk).ToString();
        if (vk >= 0x41 && vk <= 0x5A) return ((char)vk).ToString();
        if (vk >= 0x60 && vk <= 0x69) return "Num" + (vk - 0x60);
        if (vk >= 0xBA && vk <= 0xC0) return PunctName(vk);
        if (vk >= 0xDB && vk <= 0xDE) return PunctName(vk);
        switch (vk)
        {
            case VK_BACKSPACE: return "Backspace";
            case VK_TAB: return "Tab";
            case VK_ENTER: return "Enter";
            case VK_PAUSE: return "Pause";
            case VK_CAPSLOCK: return "CapsLock";
            case VK_ESCAPE: return "Esc";
            case VK_SPACE: return "Space";
            case VK_PRIOR: return "PgUp";
            case VK_NEXT: return "PgDn";
            case VK_END: return "End";
            case VK_HOME: return "Home";
            case VK_LEFT: return "Left";
            case VK_UP: return "Up";
            case VK_RIGHT: return "Right";
            case VK_DOWN: return "Down";
            case VK_SNAPSHOT: return "PrintScreen";
            case VK_INSERT: return "Insert";
            case VK_DELETE: return "Delete";
            case VK_NUMLOCK: return "NumLock";
            case VK_SCROLL: return "ScrollLock";
        }
        return "VK" + vk.ToString(CultureInfo.InvariantCulture);
    }

    static string PunctName(int vk)
    {
        switch (vk)
        {
            case 0xBA: return "Semicolon";
            case 0xBB: return "Equal";
            case 0xBC: return "Comma";
            case 0xBD: return "Minus";
            case 0xBE: return "Period";
            case 0xBF: return "Slash";
            case 0xC0: return "Backtick";
            case 0xDB: return "LBracket";
            case 0xDC: return "Backslash";
            case 0xDD: return "RBracket";
            case 0xDE: return "Quote";
        }
        return "VK" + vk.ToString(CultureInfo.InvariantCulture);
    }

        public static bool TryVk(string name, out int vk)
    {
        vk = 0;
        if (string.IsNullOrEmpty(name)) return false;
        string s = name.Trim();

        if (s.Length >= 2 && (s[0] == 'F' || s[0] == 'f'))
        {
            if (int.TryParse(s.Substring(1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                && n >= 1 && n <= 24)
            { vk = 0x70 + n - 1; return true; }
        }
        if (s.Length == 1)
        {
            char c = char.ToUpperInvariant(s[0]);
            if (c >= 'A' && c <= 'Z') { vk = c; return true; }
            if (c >= '0' && c <= '9') { vk = c; return true; }
        }
        if (s.Length == 4 && (s[0] == 'N' || s[0] == 'n'))
        {
            if (int.TryParse(s.Substring(3), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                && n >= 0 && n <= 9 && string.Equals(s.Substring(0, 3), "num", StringComparison.OrdinalIgnoreCase))
            { vk = 0x60 + n; return true; }
        }
        if (s.Length > 2 && (s[0] == 'V' || s[0] == 'v') && (s[1] == 'K' || s[1] == 'k'))
        {
            if (int.TryParse(s.Substring(2), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n)
                && n > 0 && n <= 0xFE)
            { vk = n; return true; }
        }

        string key = s.ToLowerInvariant();
        foreach (var kv in Specials)
            if (kv.Key == key) { vk = kv.Value; return true; }

        switch (key)
        {
            case "semicolon": vk = 0xBA; return true;
            case "equal": vk = 0xBB; return true;
            case "comma": vk = 0xBC; return true;
            case "minus": vk = 0xBD; return true;
            case "period": vk = 0xBE; return true;
            case "slash": vk = 0xBF; return true;
            case "backtick": vk = 0xC0; return true;
            case "lbracket": vk = 0xDB; return true;
            case "backslash": vk = 0xDC; return true;
            case "rbracket": vk = 0xDD; return true;
            case "quote": vk = 0xDE; return true;
        }
        return false;
    }

    static readonly Dictionary<string, int> Specials = new Dictionary<string, int>
    {
        ["backspace"] = VK_BACKSPACE, ["tab"] = VK_TAB, ["enter"] = VK_ENTER,
        ["pause"] = VK_PAUSE, ["capslock"] = VK_CAPSLOCK, ["esc"] = VK_ESCAPE,
        ["space"] = VK_SPACE, ["pgup"] = VK_PRIOR, ["pgdn"] = VK_NEXT,
        ["end"] = VK_END, ["home"] = VK_HOME, ["left"] = VK_LEFT,
        ["up"] = VK_UP, ["right"] = VK_RIGHT, ["down"] = VK_DOWN,
        ["printscreen"] = VK_SNAPSHOT, ["insert"] = VK_INSERT, ["delete"] = VK_DELETE,
        ["numlock"] = VK_NUMLOCK, ["scrolllock"] = VK_SCROLL,
    };

        public static bool TryParse(string text, out int vk, out int mods)
    {
        vk = 0; mods = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] parts = text.Split('+');
        string name = parts[parts.Length - 1].Trim();
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl": case "control": mods |= ModCtrl; break;
                case "shift": mods |= ModShift; break;
                case "alt": mods |= ModAlt; break;
                default: return false;
            }
        }
        return TryVk(name, out vk);
    }
}
