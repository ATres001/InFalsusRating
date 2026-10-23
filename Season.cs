namespace InFalsusRating;

public static class Season
{
        public const string Id = "S0";

        public const string Name = "神罚";

        public const string FinalKey = "theme9";

        public static bool IsSeasonFinal(Challenge c)
        => c != null && c.Final
           && string.Equals(c.Key, FinalKey, System.StringComparison.Ordinal);
}
