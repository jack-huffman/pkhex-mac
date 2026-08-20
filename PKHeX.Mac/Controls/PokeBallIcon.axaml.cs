using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace PKHeX.Mac.Controls;

/// <summary>
/// An outline Poké Ball, used as the empty-state mark. Vector-drawn so it stays
/// crisp at any size and inherits the surrounding text colour.
/// </summary>
public partial class PokeBallIcon : UserControl
{
    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<PokeBallIcon, IBrush?>(nameof(Stroke));

    /// <summary>Colour of the shell, band and button.</summary>
    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public PokeBallIcon() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
