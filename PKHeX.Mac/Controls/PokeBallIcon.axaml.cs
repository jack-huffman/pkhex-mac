using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace PKHeX.Mac.Controls;

/// <summary>
/// A Poké Ball, drawn as vector geometry so it stays crisp at any size and takes
/// its colour from the theme.
/// </summary>
public partial class PokeBallIcon : UserControl
{
    public static readonly StyledProperty<IBrush?> IconBrushProperty =
        AvaloniaProperty.Register<PokeBallIcon, IBrush?>(nameof(IconBrush));

    /// <summary>Colour of the whole icon.</summary>
    public IBrush? IconBrush
    {
        get => GetValue(IconBrushProperty);
        set => SetValue(IconBrushProperty, value);
    }

    public PokeBallIcon() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
