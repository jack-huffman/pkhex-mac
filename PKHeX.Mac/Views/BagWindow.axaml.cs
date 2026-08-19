using Avalonia.Controls;
using Avalonia.Interactivity;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

public partial class BagWindow : Window
{
    public bool Applied { get; private set; }

    public BagWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        ((BagViewModel)DataContext!).Apply();
        Applied = true;
        Close();
    }
}
