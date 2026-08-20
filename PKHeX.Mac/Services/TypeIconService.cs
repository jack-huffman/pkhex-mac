using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace PKHeX.Mac.Services;

/// <summary>
/// The circular type symbol icons, built from vector geometry so they stay sharp
/// at any size. Indexed by PKHeX's internal type id.
/// </summary>
public static class TypeIconService
{
    private static readonly Dictionary<int, IImage?> Cache = new();

    /// <summary>
    /// An icon for the type, or null for types with no symbol (Stellar), letting
    /// callers fall back to the flat colour chip.
    /// </summary>
    public static IImage? Get(int typeId)
    {
        if (Cache.TryGetValue(typeId, out var cached))
            return cached;

        IImage? image = null;
        if ((uint)typeId < TypeIconData.Shapes.Length)
        {
            var group = new DrawingGroup();
            foreach (var (fill, data) in TypeIconData.Shapes[typeId])
            {
                group.Children.Add(new GeometryDrawing
                {
                    Brush = new SolidColorBrush(Color.Parse(fill)),
                    Geometry = Geometry.Parse(data),
                });
            }
            // The source artwork is drawn on a 256x256 canvas.
            image = new DrawingImage { Drawing = group };
        }
        Cache[typeId] = image;
        return image;
    }

    public static bool HasIcon(int typeId) => Get(typeId) is not null;

    /// <summary>Size the icons render at inside a 256-unit canvas, for layout hints.</summary>
    public static Size NativeSize => new(256, 256);
}
