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

public class HpRules
{
        public int Max = 100;
        public int Min = -100;
        public int Pass = 0;
        public int PerSong = 0;
        public bool Regen = true;
        public bool Fatal = true;
        public int NearLoss = 0;
        public int BreakLoss = 1;
        public int RegenNeed = 5;

        public int Weight(int tier) => WeightOf(tier, NearLoss);

        public static int WeightOf(int tier, int nearLoss)
    {
        int baseTier = nearLoss <= 0 ? 1 : 2;
        if (tier < baseTier) return 0;
        return tier - baseTier + 1;
    }

        public string Text()
        => ((!Fatal) ? "简单" : NearLoss > 0 ? "困难" : "普通")
         + $"（上限 {Max} / 下限 {Min}，"
         + (Fatal ? $"掉到 {Min} 整场结束" : "掉到下限**不判死**，卡在下限接着打")
         + $" / 通过线 > {Pass} / break -{BreakLoss}"
         + (NearLoss > 0 ? $" near -{NearLoss}" : "")
         + " / "
         + (Regen
             ? $"会回血（连续 {(NearLoss > 0 ? "Exact" : "Near")} 以上攒 {RegenNeed} 点回 1）"
             : PerSong > 0 ? $"不回血，每首补 {PerSong}" : "不回血") + "）";

        public string Short()
        => $"{(Fatal ? NearLoss > 0 ? "困难" : "普通" : "简单")} {Max}/{Min} >{Pass}"
         + $" 破-{BreakLoss}" + (NearLoss > 0 ? $" 近-{NearLoss}" : "")
         + (Regen ? $" 回{RegenNeed}" : PerSong > 0 ? $" 每首+{PerSong}" : " 不回");
}