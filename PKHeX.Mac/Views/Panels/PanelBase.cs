using Avalonia.Controls;
using Avalonia.VisualTree;

namespace PKHeX.Mac.Views.Panels;

/// <summary>
/// Shared base for the extracted view panels.
/// </summary>
/// <remarks>
/// Most panel interaction is command-bound and needs nothing from the window. A few
/// actions genuinely do — clipboard access and file pickers both need a TopLevel — and
/// those handlers already live on <see cref="MainWindow"/>. Rather than duplicate that
/// plumbing per panel, a panel forwards to its owning window through here.
/// </remarks>
public abstract class PanelBase : UserControl
{
    /// <summary>The window hosting this panel, or null before it is attached.</summary>
    protected MainWindow? Owner => this.FindAncestorOfType<MainWindow>();
}
