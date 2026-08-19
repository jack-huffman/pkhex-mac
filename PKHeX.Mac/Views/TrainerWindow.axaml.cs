using Avalonia.Controls;
using Avalonia.Interactivity;
using PKHeX.Mac.ViewModels;

namespace PKHeX.Mac.Views;

public partial class TrainerWindow : Window
{
    public bool Applied { get; private set; }

    public TrainerWindow()
    {
        InitializeComponent();
    }

    private void OnCancelClicked(object? sender, RoutedEventArgs e) => Close();

    private void OnApplyClicked(object? sender, RoutedEventArgs e)
    {
        ((TrainerEditorViewModel)DataContext!).Apply();
        Applied = true;
        Close();
    }
}
