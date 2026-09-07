using Avalonia.Controls;

namespace PKHeX.Mac.Views.Panels;

/// <summary>
/// The Pokémon inspector: hero, apply/revert, and the Main, Stats, Moves, Met, Trainer
/// and Ribbons tabs. Bound to a <see cref="ViewModels.PokemonDetailViewModel"/>; every
/// action is a command on it, so this panel has no code of its own.
/// </summary>
public partial class InspectorPanel : UserControl
{
    public InspectorPanel() => InitializeComponent();
}
