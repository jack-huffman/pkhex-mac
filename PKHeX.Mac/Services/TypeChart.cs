using System;
using System.Collections.Generic;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Battle type effectiveness. PKHeX.Core does not carry a type chart — it validates
/// legality, not damage — so this supplies one.
/// </summary>
/// <remarks>
/// The base table is the modern chart (Gen 6 onwards). Two documented adjustments are
/// applied for older games: Gen 2–5 had Steel resisting Dark and Ghost and had no
/// Fairy type, and Gen 1 had the Ghost/Psychic bug plus the Bug–Poison pairing.
/// Gen 1 is marked approximate in the UI because its chart has further oddities that
/// are not modelled here.
/// </remarks>
public static class TypeChart
{
    /// <summary>Real defensive types, Normal through Fairy. Stellar (18) is Tera-only.</summary>
    public const int TypeCount = 18;

    private const int Normal = 0, Fighting = 1, Flying = 2, Poison = 3, Ground = 4, Rock = 5,
                      Bug = 6, Ghost = 7, Steel = 8, Fire = 9, Water = 10, Grass = 11,
                      Electric = 12, Psychic = 13, Ice = 14, Dragon = 15, Dark = 16, Fairy = 17;

    /// <summary>[attacker][defender] multiplier for the modern chart.</summary>
    private static readonly double[,] Modern = BuildModern();

    private static double[,] BuildModern()
    {
        var t = new double[TypeCount, TypeCount];
        for (int a = 0; a < TypeCount; a++)
        for (int d = 0; d < TypeCount; d++)
            t[a, d] = 1.0;

        void Set(int attacker, double value, params int[] defenders)
        {
            foreach (var d in defenders)
                t[attacker, d] = value;
        }

        Set(Normal, 0.5, Rock, Steel);              Set(Normal, 0, Ghost);
        Set(Fighting, 2, Normal, Rock, Steel, Ice, Dark);
        Set(Fighting, 0.5, Flying, Poison, Bug, Psychic, Fairy);
        Set(Fighting, 0, Ghost);
        Set(Flying, 2, Fighting, Bug, Grass);        Set(Flying, 0.5, Rock, Steel, Electric);
        Set(Poison, 2, Grass, Fairy);                Set(Poison, 0.5, Poison, Ground, Rock, Ghost);
        Set(Poison, 0, Steel);
        Set(Ground, 2, Poison, Rock, Steel, Fire, Electric);
        Set(Ground, 0.5, Bug, Grass);                Set(Ground, 0, Flying);
        Set(Rock, 2, Flying, Bug, Fire, Ice);        Set(Rock, 0.5, Fighting, Ground, Steel);
        Set(Bug, 2, Grass, Psychic, Dark);
        Set(Bug, 0.5, Fighting, Flying, Poison, Ghost, Steel, Fire, Fairy);
        Set(Ghost, 2, Ghost, Psychic);               Set(Ghost, 0.5, Dark);
        Set(Ghost, 0, Normal);
        Set(Steel, 2, Rock, Ice, Fairy);             Set(Steel, 0.5, Steel, Fire, Water, Electric);
        Set(Fire, 2, Bug, Steel, Grass, Ice);        Set(Fire, 0.5, Rock, Fire, Water, Dragon);
        Set(Water, 2, Ground, Rock, Fire);           Set(Water, 0.5, Water, Grass, Dragon);
        Set(Grass, 2, Ground, Rock, Water);
        Set(Grass, 0.5, Flying, Poison, Bug, Steel, Fire, Grass, Dragon);
        Set(Electric, 2, Flying, Water);             Set(Electric, 0.5, Grass, Electric, Dragon);
        Set(Electric, 0, Ground);
        Set(Psychic, 2, Fighting, Poison);           Set(Psychic, 0.5, Steel, Psychic);
        Set(Psychic, 0, Dark);
        Set(Ice, 2, Flying, Ground, Grass, Dragon);  Set(Ice, 0.5, Steel, Fire, Water, Ice);
        Set(Dragon, 2, Dragon);                      Set(Dragon, 0.5, Steel);
        Set(Dragon, 0, Fairy);
        Set(Dark, 2, Ghost, Psychic);                Set(Dark, 0.5, Fighting, Dark, Fairy);
        Set(Fairy, 2, Fighting, Dragon, Dark);       Set(Fairy, 0.5, Poison, Steel, Fire);
        return t;
    }

    /// <summary>Which chart a save's generation uses.</summary>
    public static ChartEra GetEra(int generation) => generation switch
    {
        <= 1 => ChartEra.Gen1,
        <= 5 => ChartEra.Gen2To5,
        _ => ChartEra.Modern,
    };

    /// <summary>
    /// The attacking types that exist in an era, in canonical order. Fairy arrived in
    /// Gen 6; Dark and Steel in Gen 2.
    /// </summary>
    public static IReadOnlyList<int> GetTypes(ChartEra era)
    {
        var result = new List<int>(TypeCount);
        for (int type = 0; type < TypeCount; type++)
        {
            if (era != ChartEra.Modern && type == Fairy)
                continue;
            if (era == ChartEra.Gen1 && type is Steel or Dark)
                continue;
            result.Add(type);
        }
        return result;
    }

    /// <summary>Multiplier for one attacking type against one defending type.</summary>
    public static double Get(int attacker, int defender, ChartEra era)
    {
        if ((uint)attacker >= TypeCount || (uint)defender >= TypeCount)
            return 1.0; // Stellar and anything unexpected: treat as neutral

        if (era != ChartEra.Modern)
        {
            // Steel lost its Dark and Ghost resistances in Gen 6.
            if (defender == Steel && attacker is Dark or Ghost)
                return 0.5;
        }
        if (era == ChartEra.Gen1)
        {
            // The famous Gen 1 bug, and the Bug/Poison pairing of the time.
            if (attacker == Ghost && defender == Psychic)
                return 0;
            if (attacker == Bug && defender == Poison)
                return 2;
            if (attacker == Poison && defender == Bug)
                return 2;
        }
        return Modern[attacker, defender];
    }

    /// <summary>
    /// Multiplier against a defender's type pairing, before abilities.
    /// </summary>
    public static double GetAgainst(int attacker, int type1, int type2, ChartEra era)
    {
        var result = Get(attacker, type1, era);
        if (type2 != type1)
            result *= Get(attacker, type2, era);
        return result;
    }

    /// <summary>
    /// Applies the well-known immunity and damage-reducing abilities. Anything not
    /// listed is left alone rather than guessed at.
    /// </summary>
    public static double ApplyAbility(double multiplier, int attacker, int ability)
    {
        switch (ability)
        {
            case 26: // Levitate
            case 297: // Earth Eater
                if (attacker == Ground) return 0;
                break;
            case 18: // Flash Fire
            case 273: // Well-Baked Body
                if (attacker == Fire) return 0;
                break;
            case 11: // Water Absorb
            case 114: // Storm Drain
            case 87: // Dry Skin
                if (attacker == Water) return 0;
                break;
            case 10: // Volt Absorb
            case 31: // Lightning Rod
            case 78: // Motor Drive
                if (attacker == Electric) return 0;
                break;
            case 157: // Sap Sipper
                if (attacker == Grass) return 0;
                break;
            case 47: // Thick Fat
                if (attacker is Fire or Ice) return multiplier * 0.5;
                break;
            case 85: // Heatproof
                if (attacker == Fire) return multiplier * 0.5;
                break;
            case 25: // Wonder Guard: only super-effective hits land
                return multiplier > 1 ? multiplier : 0;
        }
        return multiplier;
    }

    /// <summary>True when this ability changes how the chart applies.</summary>
    public static bool IsRelevantAbility(int ability) => ability
        is 26 or 297 or 18 or 273 or 11 or 114 or 87 or 10 or 31 or 78 or 157 or 47 or 85 or 25;
}

/// <summary>Which historical type chart applies.</summary>
public enum ChartEra
{
    Gen1,
    Gen2To5,
    Modern,
}
