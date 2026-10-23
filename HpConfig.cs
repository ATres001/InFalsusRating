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

public class HpConfig
{
        public int? Max;
        public int? Min;
        public int? Pass;
        public bool? Regen;
        public int? PerSong;
        public bool? Fatal;

        public int? NearLoss;
        public int? BreakLoss;
        public int? RegenNeed;
}