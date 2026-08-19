using Avalonia.Controls;
using Avalonia.Interactivity;
using PKHeX.Core;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

public partial class AddPokemonWindow : Window
{
    /// <summary>Set when the user clicked "Add to Box" with a generated Pokémon.</summary>
    public PKM? ResultToAdd { get; private set; }

    public AddPokemonWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        var vm = (AddPokemonViewModel)DataContext!;
        if (vm.Result is null)
            vm.Generate();
        if (vm.Result is null)
            return; // status text explains why
        ResultToAdd = vm.Result;
        Close();
    }
}
