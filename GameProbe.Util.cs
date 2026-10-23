using System;
using System.Globalization;

namespace InFalsusRating;

public static partial class GameProbe
{
    static readonly CultureInfo InvCult = CultureInfo.InvariantCulture;

        static object GetPath(object o, string path)
    {
        int dot = path.IndexOf('.');
        if (dot < 0) return PF(o, path);
        object sub = PF(o, path.Substring(0, dot));
        return sub == null ? null : PF(sub, path.Substring(dot + 1));
    }

        static bool SetPath(object o, string path, string text)
    {
        int dot = path.IndexOf('.');
        if (dot < 0)
        {
            var f = o.GetType().GetField(path, F);
            object v;
            if (f == null || !ParseVal(f.FieldType, text, out v)) return false;
            try { f.SetValue(o, v); return true; } catch { return false; }
        }
        string head = path.Substring(0, dot);
        var fh = o.GetType().GetField(head, F);
        if (fh == null) return false;
        object sub;
        try { sub = fh.GetValue(o); } catch { return false; }
        if (sub == null) return false;
        if (!SetPath(sub, path.Substring(dot + 1), text)) return false;
        try { fh.SetValue(o, sub); return true; } catch { return false; }
    }

        static bool ParseVal(Type t, string text, out object v)
    {
        v = null;
        text = (text ?? "").Trim();
        try
        {
            if (t.IsEnum) { v = Enum.ToObject(t, long.Parse(text, InvCult)); return true; }
            if (t == typeof(int)) { v = int.Parse(text, InvCult); return true; }
            if (t == typeof(long)) { v = long.Parse(text, InvCult); return true; }
            if (t == typeof(short)) { v = short.Parse(text, InvCult); return true; }
            if (t == typeof(byte)) { v = byte.Parse(text, InvCult); return true; }
            if (t == typeof(uint)) { v = uint.Parse(text, InvCult); return true; }
            if (t == typeof(ulong)) { v = ulong.Parse(text, InvCult); return true; }
            if (t == typeof(double)) { v = double.Parse(text, NumberStyles.Float, InvCult); return true; }
            if (t == typeof(float)) { v = float.Parse(text, NumberStyles.Float, InvCult); return true; }
            if (t == typeof(bool))
            {
                v = text == "1" || text.Equals("true", StringComparison.OrdinalIgnoreCase);
                return true;
            }
            v = Convert.ChangeType(text, t, InvCult);
            return true;
        }
        catch { return false; }
    }

        static void ClearProbeLog()
    {
        _o.Clear();
        W("=========== 日志已清空 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===========");
        Flush();
        Plugin.Logger.LogInfo("[Hotkey] F3 → 探针日志已清空");
    }
}
