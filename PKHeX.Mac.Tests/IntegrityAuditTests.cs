using PKHeX.Core;
using PKHeX.Mac.Services;
using Xunit;

namespace PKHeX.Mac.Tests;

/// <summary>
/// The audit grades its findings, and the grade is a claim about how sure it is. These
/// tests pin down the evidence rules — particularly the one that caught a real bug:
/// before Gen 6 the encryption constant is the PID, so the two are not independent.
/// </summary>
public class IntegrityAuditTests
{
    private static readonly GameStrings Strings = GameInfo.GetStrings("en");

    /// <summary>A blank Gen 5 save, which round-trips reliably unlike Gen 8/9 blanks.</summary>
    private static SAV5B2W2 NewGen5() => new();

    private static PKM Pokemon(SaveFile sav, ushort species, uint pid, int level = 20)
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.CurrentLevel = (byte)level;
        pk.PID = pid;
        pk.Gender = pk.GetSaneGender();
        pk.RefreshChecksum();
        return pk;
    }

    [Fact]
    public void CleanSaveReportsNothingSerious()
    {
        var sav = NewGen5();
        sav.SetBoxSlotAtIndex(Pokemon(sav, 1, 0x11111111), 0, 0);
        sav.SetBoxSlotAtIndex(Pokemon(sav, 4, 0x22222222), 0, 1);
        sav.SetBoxSlotAtIndex(Pokemon(sav, 7, 0x33333333), 0, 2);

        var result = IntegrityAudit.Run(sav, Strings);
        Assert.Equal(3, result.Scanned);
        Assert.DoesNotContain(result.Findings, f => f.Severity == AuditSeverity.Conclusive);
    }

    [Fact]
    public void IdenticalEntriesAreConclusive()
    {
        var sav = NewGen5();
        var pk = Pokemon(sav, 25, 0xABCDEF01);
        sav.SetBoxSlotAtIndex(pk, 0, 0);
        sav.SetBoxSlotAtIndex(pk, 0, 1);   // the same Pokémon in two places

        var finding = Assert.Single(IntegrityAudit.Run(sav, Strings).Findings,
                                    f => f.Severity == AuditSeverity.Conclusive);
        Assert.Equal(2, finding.Entries.Count);
        Assert.Contains("identical", finding.Title, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PreGenSixDoesNotCountThePidTwice()
    {
        // In Gen 5 the encryption constant IS the PID. Two different species sharing a
        // PID must therefore not be graded as if two independent identifiers matched.
        var sav = NewGen5();
        sav.SetBoxSlotAtIndex(Pokemon(sav, 1, 0x0BADF00D), 0, 0);
        sav.SetBoxSlotAtIndex(Pokemon(sav, 4, 0x0BADF00D), 0, 1);

        var shared = IntegrityAudit.Run(sav, Strings).Findings
            .Where(f => f.Title.Contains("PID", StringComparison.Ordinal)).ToList();
        var finding = Assert.Single(shared);
        Assert.Equal(AuditSeverity.Notable, finding.Severity);
        Assert.DoesNotContain("encryption constant", finding.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SamePidAndSpeciesAndIvsIsAClone()
    {
        // Even without a separate encryption constant, matching species, PID and every
        // IV is only explicable as a copy.
        var sav = NewGen5();
        var a = Pokemon(sav, 25, 0x5EED5EED);
        a.IV_HP = a.IV_ATK = a.IV_DEF = a.IV_SPA = a.IV_SPD = a.IV_SPE = 31;
        a.RefreshChecksum();
        var b = a.Clone();
        b.CurrentLevel = 55;                 // differs, but identity does not
        b.RefreshChecksum();

        sav.SetBoxSlotAtIndex(a, 0, 0);
        sav.SetBoxSlotAtIndex(b, 0, 1);

        Assert.Contains(IntegrityAudit.Run(sav, Strings).Findings,
                        f => f.Severity == AuditSeverity.Conclusive);
    }

    [Fact]
    public void DifferentPokemonAreNotFlagged()
    {
        var sav = NewGen5();
        for (ushort i = 0; i < 6; i++)
            sav.SetBoxSlotAtIndex(Pokemon(sav, (ushort)(i + 1), 0x1000 + i * 0x999u), 0, i);

        var findings = IntegrityAudit.Run(sav, Strings).Findings;
        Assert.DoesNotContain(findings, f => f.Severity >= AuditSeverity.Notable
                                             && f.Title.Contains("share", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FlawlessIvsAreInformationalNotAFault()
    {
        var sav = NewGen5();
        for (ushort i = 0; i < 5; i++)
        {
            var pk = Pokemon(sav, (ushort)(i + 1), 0x2000 + i * 0x777u);
            pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31;
            pk.RefreshChecksum();
            sav.SetBoxSlotAtIndex(pk, 0, i);
        }

        var finding = Assert.Single(IntegrityAudit.Run(sav, Strings).Findings,
                                    f => f.Title.Contains("flawless"));
        Assert.Equal(AuditSeverity.Info, finding.Severity);
    }

    [Fact]
    public void EmptySaveProducesNoFindings()
    {
        var result = IntegrityAudit.Run(NewGen5(), Strings);
        Assert.Equal(0, result.Scanned);
        Assert.Empty(result.Findings);
    }

    [Fact]
    public void FindingsAreOrderedWorstFirst()
    {
        var sav = NewGen5();
        var pk = Pokemon(sav, 25, 0xAAAABBBB);
        pk.IV_HP = pk.IV_ATK = pk.IV_DEF = pk.IV_SPA = pk.IV_SPD = pk.IV_SPE = 31;
        pk.RefreshChecksum();
        sav.SetBoxSlotAtIndex(pk, 0, 0);
        sav.SetBoxSlotAtIndex(pk, 0, 1);
        for (ushort i = 2; i < 6; i++)
        {
            var other = Pokemon(sav, (ushort)(i + 30), 0x3000 + i * 0x555u);
            other.IV_HP = other.IV_ATK = other.IV_DEF = other.IV_SPA = other.IV_SPD = other.IV_SPE = 31;
            other.RefreshChecksum();
            sav.SetBoxSlotAtIndex(other, 0, i);
        }

        var findings = IntegrityAudit.Run(sav, Strings).Findings;
        Assert.True(findings.Count > 1);
        for (int i = 1; i < findings.Count; i++)
            Assert.True(findings[i - 1].Severity >= findings[i].Severity);
    }
}
