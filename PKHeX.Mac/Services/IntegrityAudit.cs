using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Looks at the save as a whole rather than one Pokémon at a time.
/// </summary>
/// <remarks>
/// PKHeX's legality engine validates each entity in isolation, which is the right
/// design for legality but leaves a blind spot: it cannot see that two Pokémon share
/// an identifier that is supposed to be unique, because it never sees them together.
/// Every finding here comes from comparing entries against each other.
///
/// Nothing here is an accusation. A shared identifier is reported with how unlikely it
/// is and what usually explains it; fixed-seed event distributions genuinely do hand
/// the same PID to everyone who claims them, so the caveats matter.
///
/// The save is read once, on the thread that owns it, by <see cref="Collect"/>; the
/// expensive comparison in <see cref="Analyze"/> then works on copies and can run anywhere.
/// </remarks>
public static class IntegrityAudit
{
    /// <summary>Runs every check on the calling thread. Convenient for tests and small saves.</summary>
    public static AuditResult Run(SaveFile sav, GameStrings strings, CancellationToken token = default) =>
        Analyze(Collect(sav, strings), strings, token);

    /// <summary>Copies every stored Pokémon out of the save, so analysis can leave the UI thread.</summary>
    public static IReadOnlyList<AuditEntity> Collect(SaveFile sav, GameStrings strings)
    {
        var list = new List<AuditEntity>();
        foreach (var slot in sav.EnumerateOccupiedSlots())
            list.Add(new AuditEntity(slot.Entity, strings.SpeciesName(slot.Entity), slot.Location));
        return list;
    }

    /// <summary>Runs every check over a snapshot. Legality dominates the cost.</summary>
    public static AuditResult Analyze(IReadOnlyList<AuditEntity> entries, GameStrings strings, CancellationToken token = default)
    {
        var findings = new List<AuditFinding>();
        if (entries.Count == 0)
            return new AuditResult(0, findings);

        findings.AddRange(FindSharedIdentifiers(entries));
        findings.AddRange(FindIllegal(entries, token));
        findings.AddRange(FindIvConcentration(entries));
        findings.AddRange(FindMetClusters(entries, strings));

        // Worst first, then largest.
        findings.Sort((a, b) => b.Severity != a.Severity
            ? b.Severity.CompareTo(a.Severity)
            : b.Entries.Count.CompareTo(a.Entries.Count));
        return new AuditResult(entries.Count, findings);
    }

    // =====================================================================
    // Shared identifiers
    // =====================================================================

    /// <summary>
    /// Clusters entries that share a PID, encryption constant or HOME tracker, then
    /// describes each cluster by everything its members have in common. Clustering
    /// first avoids reporting one cloned pair four times over.
    /// </summary>
    private static IEnumerable<AuditFinding> FindSharedIdentifiers(IReadOnlyList<AuditEntity> entries)
    {
        // Before Gen 6 there is no separate encryption constant: PKHeX returns the PID
        // for it, so treating the two as independent evidence would double-count one
        // value. Verified on real saves — 6/6 Gen 5 entries have EC == PID, 0/173 in
        // Gen 8 and 9.
        var hasOwnEc = entries.All(e => e.Entity.Format >= 6);

        var union = new DisjointSet(entries.Count);
        LinkBy(entries, union, e => e.Entity.PID);
        if (hasOwnEc)
            LinkBy(entries, union, e => e.Entity.EncryptionConstant);
        LinkBy(entries, union, e => e.Entity is IHomeTrack { Tracker: not 0 } t ? t.Tracker : 0UL, skipZero: true);

        foreach (var group in union.Groups().Where(g => g.Count > 1))
        {
            var members = group.Select(i => entries[i]).ToList();
            var samePid = members.Select(m => m.Entity.PID).Distinct().Count() == 1;
            var sameEc = hasOwnEc && members.Select(m => m.Entity.EncryptionConstant).Distinct().Count() == 1;
            var trackers = members.Select(m => m.Entity is IHomeTrack t ? t.Tracker : 0UL).ToList();
            var sameTracker = trackers.All(t => t != 0) && trackers.Distinct().Count() == 1;
            var sameIvs = members.Select(IvKey).Distinct().Count() == 1;
            var sameSpecies = members.Select(m => m.Entity.Species).Distinct().Count() == 1;

            var shared = new List<string>();
            if (samePid) shared.Add($"PID {members[0].Entity.PID:X8}");
            if (sameEc) shared.Add($"encryption constant {members[0].Entity.EncryptionConstant:X8}");
            if (sameTracker) shared.Add($"HOME tracker {trackers[0]:X16}");

            // A shared PID is only conclusive once something else corroborates it: a
            // separate encryption constant, or the same species with the same IVs.
            var duplicate = (samePid && sameEc) || (samePid && sameIvs && sameSpecies);
            var severity = sameTracker || duplicate
                ? AuditSeverity.Conclusive
                : sameEc ? AuditSeverity.Strong
                : AuditSeverity.Notable;

            var title = sameTracker
                ? $"{members.Count} Pokémon share one HOME tracker"
                : duplicate
                    ? $"{members.Count} identical {members[0].Species} entries"
                    : $"{members.Count} Pokémon share " + string.Join(" and ", shared);

            yield return new AuditFinding(
                title,
                Explain(members, samePid, sameEc, sameTracker, sameIvs, sameSpecies, hasOwnEc, entries.Count),
                severity, members.Select(m => m.ToEntry()).ToList());
        }
    }

    private static string Explain(List<AuditEntity> members, bool samePid, bool sameEc,
                                 bool sameTracker, bool sameIvs, bool sameSpecies,
                                 bool hasOwnEc, int total)
    {
        if (sameTracker)
        {
            return "A HOME tracker is assigned once, per Pokémon, when it first enters Pokémon HOME, "
                   + "and is meant to be unique worldwide. Several entries carrying the same one cannot "
                   + "have happened through normal play. PKHeX cannot flag this, because judging it needs "
                   + "every Pokémon at once.";
        }
        if (samePid && sameEc)
        {
            return "These agree on both the PID and the encryption constant, the two values that make a "
                   + "Pokémon distinguishable from an identical-looking one. That is the signature of a copy "
                   + "rather than a second catch.";
        }
        if (samePid && sameIvs && sameSpecies)
        {
            return hasOwnEc
                ? "Same species, same PID, same IVs. Two separate encounters agreeing on all of that is not "
                  + "realistic; this is a copy."
                : "Same species, same PID, same IVs. This generation stores no separate encryption constant — "
                  + "the PID is the whole of a Pokémon's identity — so matching on it and every IV means one "
                  + "of these is a copy of the other.";
        }
        if (sameEc)
        {
            return "The encryption constant is a 32-bit value rolled per Pokémon. Two entries sharing one "
                   + "while differing elsewhere usually means one was derived from the other.";
        }

        // PID-only: quantify how surprising the collision is before implying anything.
        // Probability that any PID collision at all appears among this many entries.
        var odds = total * (total - 1) / 2.0 / 4294967296.0;
        return $"Only the PID matches — species and IVs differ, so these are not copies of each other. "
               + $"Across {total} stored Pokémon, chance alone would throw up even one PID collision about "
               + $"{odds:P4} of the time, so it is worth knowing about. It is not proof of anything on its "
               + "own though: fixed-seed event distributions hand the same PID to everyone who claims them, "
               + "so two such Pokémon can legitimately match.";
    }

    private static void LinkBy<T>(IReadOnlyList<AuditEntity> entries, DisjointSet union,
                                  Func<AuditEntity, T> key, bool skipZero = false)
        where T : notnull
    {
        var seen = new Dictionary<T, int>();
        for (int i = 0; i < entries.Count; i++)
        {
            var k = key(entries[i]);
            if (skipZero && k.Equals(default(T)!))
                continue;
            if (seen.TryGetValue(k, out var first))
                union.Union(first, i);
            else
                seen[k] = i;
        }
    }

    private static string IvKey(AuditEntity e)
    {
        var p = e.Entity;
        return $"{p.IV_HP}/{p.IV_ATK}/{p.IV_DEF}/{p.IV_SPA}/{p.IV_SPD}/{p.IV_SPE}";
    }

    // =====================================================================
    // Per-entity legality, reported as one group
    // =====================================================================

    private static IEnumerable<AuditFinding> FindIllegal(IReadOnlyList<AuditEntity> entries, CancellationToken token)
    {
        var bad = new List<AuditEntity>();
        foreach (var entry in entries)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                if (!new LegalityAnalysis(entry.Entity).Valid)
                    bad.Add(entry);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                bad.Add(entry); // an entity the analyser cannot even parse is itself a finding
            }
        }
        if (bad.Count == 0)
            yield break;

        yield return new AuditFinding(
            $"{bad.Count} of {entries.Count} fail a legality check",
            "These are PKHeX's own per-Pokémon verdicts. Open one in the editor to read the full report.",
            AuditSeverity.Strong,
            bad.Select(b => b.ToEntry()).ToList());
    }

    // =====================================================================
    // Statistical shape — informational, never a defect on its own
    // =====================================================================

    private static IEnumerable<AuditFinding> FindIvConcentration(IReadOnlyList<AuditEntity> entries)
    {
        var flawless = entries.Where(e => IvKey(e) == "31/31/31/31/31/31").ToList();
        if (flawless.Count < 3)
            yield break;
        var share = flawless.Count / (double)entries.Count;
        yield return new AuditFinding(
            $"{flawless.Count} of {entries.Count} have flawless IVs ({share:P0})",
            "Perfect spreads are obtainable through breeding, Hyper Training and raids, so this is not a "
            + "fault. It is listed because a high share is a fingerprint of bulk generation rather than play, "
            + "which is useful to know before trading any of them.",
            AuditSeverity.Info,
            flawless.Select(f => f.ToEntry()).ToList());
    }

    private static IEnumerable<AuditFinding> FindMetClusters(IReadOnlyList<AuditEntity> entries, GameStrings strings)
    {
        // Location ids are only meaningful within a game, so the same number in two
        // versions is two different places and must not be clustered together.
        var clusters = entries
            .Where(e => e.Entity.MetLocation != 0)
            .GroupBy(e => (e.Entity.Version, e.Entity.MetLocation, e.Entity.MetYear, e.Entity.MetMonth, e.Entity.MetDay))
            .Where(g => g.Count() >= 4)
            .OrderByDescending(g => g.Count())
            .Take(5);

        foreach (var cluster in clusters)
        {
            var (version, loc, year, month, day) = cluster.Key;
            var members = cluster.ToList();
            var sample = members[0].Entity;
            var place = strings.GetLocationName(false, loc, sample.Format, sample.Generation, version);
            if (string.IsNullOrEmpty(place))
                place = $"location {loc}";
            yield return new AuditFinding(
                $"{members.Count} met at the same place on the same day",
                $"{place} on {2000 + year:0000}-{month:00}-{day:00}. Entirely normal for an outbreak "
                + "or a raid session; listed so a batch that was generated in one go is easy to spot.",
                AuditSeverity.Info,
                members.Select(m => m.ToEntry()).ToList());
        }
    }

    /// <summary>Union-find, so overlapping shared identifiers collapse into one cluster.</summary>
    private sealed class DisjointSet(int count)
    {
        private readonly int[] _parent = Enumerable.Range(0, count).ToArray();

        private int Find(int i) => _parent[i] == i ? i : _parent[i] = Find(_parent[i]);

        public void Union(int a, int b)
        {
            var (ra, rb) = (Find(a), Find(b));
            if (ra != rb)
                _parent[rb] = ra;
        }

        public IEnumerable<List<int>> Groups()
        {
            var map = new Dictionary<int, List<int>>();
            for (int i = 0; i < _parent.Length; i++)
            {
                var root = Find(i);
                if (!map.TryGetValue(root, out var list))
                    map[root] = list = [];
                list.Add(i);
            }
            return map.Values;
        }
    }
}

/// <summary>One stored Pokémon, with where it lives.</summary>
public sealed class AuditEntity(PKM entity, string species, string location)
{
    public PKM Entity { get; } = entity;
    public string Species { get; } = species;
    public string Location { get; } = location;

    public AuditEntry ToEntry() => new(Species, Location, Entity);
}

/// <summary>A Pokémon named in a finding.</summary>
public sealed record AuditEntry(string Species, string Location, PKM Entity)
{
    public string Detail =>
        $"Lv. {Entity.CurrentLevel} · PID {Entity.PID:X8} · EC {Entity.EncryptionConstant:X8}";
}

/// <summary>How much confidence a finding carries.</summary>
public enum AuditSeverity
{
    Info,
    Notable,
    Strong,
    Conclusive,
}

public sealed record AuditFinding(string Title, string Detail, AuditSeverity Severity,
                                 IReadOnlyList<AuditEntry> Entries);

public sealed record AuditResult(int Scanned, IReadOnlyList<AuditFinding> Findings);
