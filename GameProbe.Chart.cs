using System;
using System.Linq;
using System.Reflection;
using BepInEx;

namespace InFalsusRating;

public static partial class GameProbe
{
    static FieldInfo _qePtrField;
    static MethodInfo _poolGet;
    static bool _npComplained;

    static void NoPlayer(string why)
    {
        if (_npComplained) return;
        _npComplained = true;
        W("⚠ 拿不到 LogicalNotePlayer：" + why);
        Flush();
    }

    static object NotePlayer()
    {
        try
        {
            if (_qePtrField == null)
            {
                Type t = T("ifapp.Game.LogicalNotePlayer");
                if (t == null) { NoPlayer("类型 `ifapp.Game.LogicalNotePlayer` 找不到"); return null; }
                var f = t.GetField("NativeFieldInfoPtr__Qe", FS);
                if (f == null) { NoPlayer("字段 `NativeFieldInfoPtr__Qe` 找不到"); return null; }
                var pool = T("Il2CppInterop.Runtime.Runtime.Il2CppObjectPool");
                if (pool == null) { NoPlayer("类型 `Il2CppObjectPool` 找不到"); return null; }
                var get = pool.GetMethods(FS).FirstOrDefault(
                    m => m.Name == "Get" && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
                if (get == null) { NoPlayer("`Il2CppObjectPool.Get<T>` 找不到"); return null; }
                _poolGet = get.MakeGenericMethod(t);
                _qePtrField = f;
            }

            var fld = (IntPtr)_qePtrField.GetValue(null);
            if (fld == IntPtr.Zero) return null;
            var obj = Il2CppInterop.Runtime.IL2CPP.il2cpp_field_get_value_object(fld, IntPtr.Zero);
            if (obj == IntPtr.Zero) return null;
            return _poolGet.Invoke(null, new object[] { obj });
        }
        catch (Exception e)
        {
            NoPlayer("反射抛 " + (e.InnerException ?? e).GetType().Name + ": " + e.Message);
            return null;
        }
    }

    static MethodInfo _gbGet;
    static bool _gbMissed;

    static string EnsureCook(object lnp)
    {
        if (_gbGet == null && !_gbMissed)
        {
            _gbGet = lnp.GetType().GetMethods(F).FirstOrDefault(
                m => m.Name == "_Gb" && m.GetParameters().Length == 0 && m.ReturnType != typeof(void));
            if (_gbGet == null) _gbMissed = true;
        }
        if (_gbMissed) return "`_Gb` 找不到";
        return null;
    }

    static object CurrentTuple(object lnp)
    {
        if (lnp == null) return null;
        if (EnsureCook(lnp) != null) return P(lnp, "_Ue");
        try { return _gbGet.Invoke(lnp, null) ?? P(lnp, "_Ue"); }
        catch { return P(lnp, "_Ue"); }
    }
}
