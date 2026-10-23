using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using BepInEx;
using BepInEx.Unity.IL2CPP;
using BepInEx.Logging;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using ifapp.Game.Data;

namespace InFalsusRating;

public static class UpdateWatch
{
        public static int Count;
        public static int FirstFrame = -1;
        public static int LastFrame = -1;
        public static string FirstMsg;

    public static Exception Finalizer(Exception __exception)
    {
        if (__exception == null) return null;
        try
        {
            if (FirstFrame < 0)
            {
                FirstFrame = SceneProbe.FrameNo;
                FirstMsg = __exception.GetType().Name + ": " + __exception.Message;
            }
            Count++;
            LastFrame = SceneProbe.FrameNo;
        }
        catch { }
        return __exception;
    }
}