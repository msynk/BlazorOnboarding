namespace BlazorOnboarding;

/// <summary>Everything <see cref="PlacementEngine"/> needs to position a popover.</summary>
public readonly record struct PlacementRequest
{
    /// <summary>The rectangle to point at, in viewport coordinates, spotlight padding already applied.</summary>
    public Rect Target { get; init; }

    /// <summary>Measured size of the popover.</summary>
    public Size Popover { get; init; }

    /// <summary>The visible viewport, normally <c>(0, 0, innerWidth, innerHeight)</c>.</summary>
    public Rect Viewport { get; init; }

    /// <summary>Requested placement.</summary>
    public Placement Preferred { get; init; }

    /// <summary>Explicit fallback order; when null a sensible order is derived.</summary>
    public IReadOnlyList<Placement>? Fallbacks { get; init; }

    /// <summary>Gap between target and popover.</summary>
    public double Offset { get; init; }

    /// <summary>Minimum gap between popover and viewport edge.</summary>
    public double ViewportPadding { get; init; }

    /// <summary>Length of the arrow's visible edge; <c>0</c> disables the arrow.</summary>
    public double ArrowSize { get; init; }

    /// <summary>Corner radius kept clear of the arrow.</summary>
    public double CornerRadius { get; init; }

    /// <summary>Mirrors <see cref="PlacementAlign.Start"/> / <see cref="PlacementAlign.End"/> horizontally.</summary>
    public bool RightToLeft { get; init; }

    /// <summary>True when the step has no target and should be centred.</summary>
    public bool HasTarget { get; init; }
}
