using Avalonia.Media;
using PKHeX.Core;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.ViewModels;

/// <summary>A single type badge: the circular symbol, with a colour-chip fallback for Stellar.</summary>
public sealed class TypeBadgeViewModel
{
    public TypeBadgeViewModel(int typeId, GameStrings strings)
    {
        TypeId = typeId;
        TypeName = strings.TypeName(typeId);
        Icon = TypeIconService.Get(typeId);
        Brush = TypePalette.GetBrush(typeId);
    }

    public int TypeId { get; }
    public string TypeName { get; }
    public IImage? Icon { get; }
    public IBrush? Brush { get; }
    public bool HasIcon => Icon is not null;
}
