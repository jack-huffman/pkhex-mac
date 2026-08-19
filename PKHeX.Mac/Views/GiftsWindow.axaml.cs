using Avalonia.Controls;
using Avalonia.Interactivity;
using PKHeX.Core;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

public partial class GiftsWindow : Window
{
    public PKM? ResultToAdd { get; private set; }

    public GiftsWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnAddClicked(object? sender, RoutedEventArgs e)
    {
        var vm = (GiftsViewModel)DataContext!;
        if (vm.Result is null)
            vm.ConvertSelected();
        if (vm.Result is null)
            return;
        ResultToAdd = vm.Result;
        Close();
    }
}
