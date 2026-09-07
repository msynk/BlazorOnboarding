using System.Globalization;

namespace BlazorOnboarding;

/// <summary>The outcome of <see cref="PlacementEngine.Place"/>.</summary>
public readonly record struct PlacementResult
{
    /// <summary>Final popover rectangle in viewport coordinates.</summary>
    public Rect Popover { get; init; }

    /// <summary>Side of the target the popover ended up on.</summary>
    public PlacementSide Side { get; init; }

    /// <summary>Alignment along the target's cross axis.</summary>
    public PlacementAlign Align { get; init; }

    /// <summary>Whether an arrow should be drawn.</summary>
    public bool ShowArrow { get; init; }

    /// <summary>
    /// Distance from the popover's leading edge (left for top/bottom, top for left/right) to the
    /// arrow's centre, in pixels.
    /// </summary>
    public double ArrowOffset { get; init; }

    /// <summary>True when the requested placement was used unchanged.</summary>
    public bool UsedPreferred { get; init; }

    /// <summary>Pixels of the popover that had to be clipped to fit; <c>0</c> when it fits cleanly.</summary>
    public double Overflow { get; init; }

    /// <summary>The CSS class suffix for the chosen side, e.g. <c>"bottom"</c>.</summary>
    public string SideName => Side switch
    {
        PlacementSide.Top => "top",
        PlacementSide.Right => "right",
        PlacementSide.Bottom => "bottom",
        PlacementSide.Left => "left",
        _ => "center",
    };

    public override string ToString()
        => string.Create(CultureInfo.InvariantCulture, $"{SideName}/{Align} at {Popover}");
}
