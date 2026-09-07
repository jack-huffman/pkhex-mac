using PKHeX.Core;

namespace PKHeX.Mac.Services;

/// <summary>
/// Moving a Pokémon from one open save to another, with the conversion that implies.
/// </summary>
/// <remarks>
/// This is the deliberate, inspectable version of what trading and HOME transfers do.
/// PKHeX.Core owns the hard part — <see cref="EntityConverter"/> knows every route
/// between formats and refuses the ones that do not exist — so this reports its verdict
/// rather than second-guessing it, and checks legality on the far side because a
/// conversion that succeeds mechanically can still land somewhere illegal.
/// </remarks>
public static class SaveTransfer
{
    /// <summary>Works out what would happen, without changing anything.</summary>
    /// <param name="source">The Pokémon to send; it is cloned, never modified.</param>
    /// <param name="destination">The save it would land in.</param>
    /// <param name="strings">Names for the messages.</param>
    /// <param name="assignHomeTracker">
    /// Give the result a Pokémon HOME tracker. Anything that has genuinely moved between
    /// games carries one, and without it the destination rightly calls the Pokémon
    /// illegal — but the value is invented here, not issued by HOME, so this is offered
    /// as a choice rather than done quietly.
    /// </param>
    public static TransferPlan Plan(PKM source, SaveFile destination, GameStrings strings,
                                    bool assignHomeTracker = false)
    {
        var name = strings.SpeciesName(source);

        if (source.Species == 0)
            return TransferPlan.Blocked(name, "That slot is empty.");

        // Species that simply do not exist in the destination game.
        if (source.Species > destination.MaxSpeciesID)
        {
            return TransferPlan.Blocked(name,
                $"{name} does not exist in {GameInfo.GetVersionName(destination.Version)}.");
        }

        var converted = EntityConverter.ConvertToType(source.Clone(), destination.PKMType, out var result);
        if (converted is null)
            return TransferPlan.Blocked(name, Explain(result, name, destination));

        // Hand it to the receiving trainer, which is what a trade does. Without this
        // every transfer arrives illegal on "Current handler cannot be the OT", because
        // the entity still claims to be held by a trainer who no longer has it.
        if (converted is IHandlerUpdate handler)
            handler.UpdateHandler(destination);

        // Anything that has genuinely crossed games carries a HOME tracker.
        var lacksTracker = converted is IHomeTrack { Tracker: 0 };
        var assigned = false;
        if (assignHomeTracker && lacksTracker && converted is IHomeTrack track)
        {
            track.Tracker = HomeTracker.NewRandom();
            assigned = true;
        }
        converted.RefreshChecksum();

        var legality = new LegalityAnalysis(converted);
        var issue = LegalitySummary.FirstIssue(legality, "Fails a legality check in the destination game.");
        return new TransferPlan(name, converted, result, legality.Valid, issue, null)
        {
            NeedsHomeTracker = lacksTracker && !assigned,
            HomeTrackerAssigned = assigned,
        };
    }

    /// <summary>Writes a planned transfer into the first free slot of a box.</summary>
    public static bool Commit(TransferPlan plan, SaveFile destination, int box, out string message)
    {
        if (plan.Entity is null)
        {
            message = plan.Problem ?? "Nothing to transfer.";
            return false;
        }
        if (!destination.HasBox || (uint)box >= destination.BoxCount)
        {
            message = "That box does not exist in the destination save.";
            return false;
        }

        for (int slot = 0; slot < destination.BoxSlotCount; slot++)
        {
            if (destination.GetBoxSlotAtIndex(box, slot).Species != 0)
                continue;
            var pk = plan.Entity.Clone();
            pk.RefreshChecksum();
            destination.SetBoxSlotAtIndex(pk, box, slot);
            message = $"Copied {plan.Species} into box {box + 1}, slot {slot + 1}.";
            return true;
        }
        message = $"Box {box + 1} is full.";
        return false;
    }

    private static string Explain(EntityConverterResult result, string name, SaveFile destination) => result switch
    {
        EntityConverterResult.NoTransferRoute =>
            $"There is no route from this game to {GameInfo.GetVersionName(destination.Version)}. "
            + "Pokémon can only move forward through the generations.",
        EntityConverterResult.IncompatibleSpecies =>
            $"{name} cannot exist in {GameInfo.GetVersionName(destination.Version)}.",
        EntityConverterResult.IncompatibleForm =>
            $"This form of {name} cannot exist in the destination game.",
        EntityConverterResult.IncompatibleLanguageGB =>
            "The two games use incompatible language encodings.",
        _ => $"The conversion failed ({result}).",
    };
}

/// <summary>What a transfer would produce, or why it cannot happen.</summary>
public sealed record TransferPlan(
    string Species,
    PKM? Entity,
    EntityConverterResult Result,
    bool IsLegal,
    string Issue,
    string? Problem)
{
    public bool CanTransfer => Entity is not null;

    /// <summary>The destination expects a HOME tracker and this plan has none.</summary>
    public bool NeedsHomeTracker { get; init; }

    /// <summary>A tracker was invented for this transfer.</summary>
    public bool HomeTrackerAssigned { get; init; }

    /// <summary>True when the formats differ and a real conversion took place.</summary>
    public bool WasConverted => Result is EntityConverterResult.Success
        or EntityConverterResult.SuccessIncompatibleManual
        or EntityConverterResult.SuccessIncompatibleReflection;

    public static TransferPlan Blocked(string species, string problem) =>
        new(species, null, EntityConverterResult.None, false, string.Empty, problem);
}
