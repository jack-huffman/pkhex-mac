using Avalonia.Interactivity;

namespace PKHeX.Mac.Views.Panels;

/// <summary>Multi-criteria search over save storage or a folder.</summary>
public partial class SearchPanel : PanelBase
{
    public SearchPanel() => InitializeComponent();

    /// <summary>Needs a TopLevel for the file picker, so the window handles it.</summary>
    public void OnChooseSearchFolderClicked(object? sender, RoutedEventArgs e) => Owner?.OnChooseSearchFolderClicked(sender, e);
}
