using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using PKHeX.Mac.Services;

namespace PKHeX.Mac.Controls;

/// <summary>
/// The physical / special / status damage-category badge, drawn as vectors.
/// </summary>
public partial class MoveCategoryIcon : UserControl
{
    public static readonly StyledProperty<MoveDataService.Category> CategoryProperty =
        AvaloniaProperty.Register<MoveCategoryIcon, MoveDataService.Category>(nameof(Category));

    public MoveDataService.Category Category
    {
        get => GetValue(CategoryProperty);
        set => SetValue(CategoryProperty, value);
    }

    public MoveCategoryIcon()
    {
        InitializeComponent();
        UpdateVisual();
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CategoryProperty)
            UpdateVisual();
    }

    private void UpdateVisual()
    {
        // The control is a stack of three badges; show the one that applies.
        var physical = this.FindControl<Control>("PhysicalIcon");
        var special = this.FindControl<Control>("SpecialIcon");
        var status = this.FindControl<Control>("StatusIcon");
        if (physical is null || special is null || status is null)
            return;

        physical.IsVisible = Category == MoveDataService.Category.Physical;
        special.IsVisible = Category == MoveDataService.Category.Special;
        status.IsVisible = Category == MoveDataService.Category.Status;
        IsVisible = Category != MoveDataService.Category.Unknown;
    }
}
