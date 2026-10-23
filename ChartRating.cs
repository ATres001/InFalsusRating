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

public class ChartRating
{
    public string SongId;
    public string Difficulty;
    public ulong Score;
    public double Constant;
    public int PaceValue;
    public int ExactCount;
    public double ExactRate;
    public double Rating;
    public bool IsFC;
    public bool IsPM;

        public ResultDetail Detail;
}