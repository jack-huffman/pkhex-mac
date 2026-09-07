using Avalonia;
using Avalonia.Controls;
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

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == CategoryProperty)
            UpdateVisual();
    }

    /// <summary>The control is a stack of three badges; show the one that applies.</summary>
    private void UpdateVisual()
    {
        PhysicalIcon.IsVisible = Category == MoveDataService.Category.Physical;
        SpecialIcon.IsVisible = Category == MoveDataService.Category.Special;
        StatusIcon.IsVisible = Category == MoveDataService.Category.Status;
        IsVisible = Category != MoveDataService.Category.Unknown;
    }
}
