using Avalonia.Interactivity;

namespace PKHeX.Mac.Views.Panels;

/// <summary>Batch edit, team analysis, breeding, integrity audit and box report.</summary>
public partial class ToolsPanel : PanelBase
{
    public ToolsPanel() => InitializeComponent();

    /// <summary>Copying to the clipboard needs a TopLevel, so the window does it.</summary>
    public void OnCopyReportClicked(object? sender, RoutedEventArgs e) =>
        Owner?.OnCopyReportClicked(sender, e);
}
