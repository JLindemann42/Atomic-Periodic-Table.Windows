using System;
using Windows.Storage;

public static class XpManager
{
    private const string XP_KEY = "user_xp";
    private const int LEVELS = 100;

    // Custom XP table for first 21 levels
    private static readonly int[] xpTable = new int[]
    {
        0,    // Level 1
        90,   // Level 2
        180,  // Level 3
        270,  // Level 4
        360,  // Level 5
        495,  // Level 6
        630,  // Level 7
        765,  // Level 8
        900,  // Level 9
        1050, // Level 10
        1275, // Level 11
        1500, // Level 12
        1725, // Level 13
        1950, // Level 14
        2175, // Level 15
        2550, // Level 16
        2925, // Level 17
        3300, // Level 18
        3675, // Level 19
        4050, // Level 20
        4575  // Level 21
    };

    // XP required to reach a given level
    public static int GetXpForLevel(int n)
    {
        if (n <= 1) return 0;
        if (n - 1 < xpTable.Length) return xpTable[n - 1];
        // Exponential scaling after level 21
        var baseXp = xpTable[^1];
        var extraLevels = n - xpTable.Length;
        return (int)Math.Round(baseXp + (800 * (Math.Pow(1.10, extraLevels) - 1) / 0.10));
    }

    // Get level for a given XP
    public static int GetLevel(int xp)
    {
        int level = 1;
        while (level < LEVELS && GetXpForLevel(level + 1) <= xp)
        {
            level++;
        }
        return level;
    }

    // Get current XP from local settings
    public static int GetXp()
    {
        var values = ApplicationData.Current.LocalSettings.Values;
        if (values.TryGetValue(XP_KEY, out object value))
        {
            if (value is int i) return i;
            if (value is long l) return (int)l;
        }
        return 0;
    }

    // Add XP and save to local settings
    public static void AddXp(int amount)
    {
        int oldXp = GetXp();
        int newXp = oldXp + amount;
        ApplicationData.Current.LocalSettings.Values[XP_KEY] = newXp;
    }

    // Set XP (for initialization or reset)
    public static void SetXp(int xp)
    {
        ApplicationData.Current.LocalSettings.Values[XP_KEY] = xp;
    }

    // Get current level from local settings
    public static int GetCurrentLevel()
    {
        return GetLevel(GetXp());
    }

    // Get progress in current level: (current, total for level)
    public static (int current, int total) GetXpProgressInLevel()
    {
        int xp = GetXp();
        int level = GetLevel(xp);
        int minXp = GetXpForLevel(level);
        int maxXp = GetXpForLevel(level + 1);
        return (xp - minXp, maxXp - minXp);
    }
}