using Avalonia.Interactivity;

namespace PKHeX.Mac.Views.Panels;

/// <summary>Event flags and the raw SCBlock save-structure editor.</summary>
public partial class FlagsPanel : PanelBase
{
    public FlagsPanel() => InitializeComponent();

    /// <summary>Needs a TopLevel for the file picker, so the window handles it.</summary>
    public void OnExportBlockClicked(object? sender, RoutedEventArgs e) => Owner?.OnExportBlockClicked(sender, e);

    /// <summary>Needs a TopLevel for the file picker, so the window handles it.</summary>
    public void OnImportBlockClicked(object? sender, RoutedEventArgs e) => Owner?.OnImportBlockClicked(sender, e);
}
