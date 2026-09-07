namespace BlazorOnboarding;

/// <summary>
/// Collision-aware popover positioning. Deterministic, allocation-light and completely
/// side-effect free, which is what makes it directly unit testable without a browser.
/// </summary>
/// <remarks>
/// The strategy is the one users expect from a good tooltip. Each candidate side is first slid
/// along the target's cross axis to stay on screen, and only then scored by how many pixels would
/// still be clipped. Shifting therefore beats flipping: a popover next to a target at the edge of
/// the window slides sideways rather than jumping to another side. When the requested side really
/// does not fit, the opposite side is tried, then the perpendicular ones, and if nothing fits the
/// least-bad candidate is pushed fully inside the viewport.
/// </remarks>
public static class PlacementEngine
{
    /// <summary>Positions a popover against a target.</summary>
    public static PlacementResult Place(in PlacementRequest request)
    {
        var safe = request.Viewport.Inflate(-request.ViewportPadding);

        // A degenerate safe area (tiny windows, or measurement taken before layout) would make
        // every candidate score as a total miss, so fall back to the raw viewport.
        if (safe.Width <= 0 || safe.Height <= 0)
        {
            safe = request.Viewport;
        }

        if (!request.HasTarget || request.Preferred == Placement.Center || request.Target.IsEmpty)
        {
            return Centered(request, safe);
        }

        var preferred = Resolve(request.Preferred, request.Target, safe, request.RightToLeft);
        var candidates = BuildCandidates(preferred, request.Fallbacks, request.RightToLeft);

        PlacementResult best = default;
        var bestScore = double.PositiveInfinity;
        var first = true;

        foreach (var candidate in candidates)
        {
            // Score each candidate as it would actually be rendered, i.e. after the cross-axis
            // shift that keeps it on screen. Sliding a popover sideways is far less disruptive than
            // moving it to another side, so a side that only fits once shifted still wins.
            var rect = ClampCross(Compute(candidate, request), candidate.Side, safe);
            var overflow = ClippedArea(rect, safe);

            if (overflow <= 0.01)
            {
                return Finish(request, rect, candidate, safe, usedPreferred: first, overflow: 0);
            }

            if (overflow < bestScore)
            {
                bestScore = overflow;
                best = Finish(request, rect, candidate, safe, usedPreferred: first, overflow);
            }

            first = false;
        }

        // Nothing fit outright: shift the least-bad candidate into the safe area. When the popover
        // is simply larger than the viewport we clamp to the start of the safe area so the top of
        // the content stays readable and the popover scrolls internally.
        var shifted = Shift(best.Popover, safe);
        return Finish(request, shifted, (best.Side, best.Align), safe, best.UsedPreferred, ClippedArea(shifted, safe));
    }

    private static PlacementResult Centered(in PlacementRequest request, Rect safe)
    {
        var width = Math.Min(request.Popover.Width, safe.Width);
        var height = Math.Min(request.Popover.Height, safe.Height);
        var rect = new Rect(
            safe.X + ((safe.Width - width) / 2),
            safe.Y + ((safe.Height - height) / 2),
            width,
            height);

        return new PlacementResult
        {
            Popover = rect.Round(),
            Side = PlacementSide.Center,
            Align = PlacementAlign.Center,
            ShowArrow = false,
            ArrowOffset = 0,
            UsedPreferred = true,
            Overflow = 0,
        };
    }

    /// <summary>Turns <see cref="Placement.Auto"/> into a concrete side and normalises RTL alignment.</summary>
    private static (PlacementSide Side, PlacementAlign Align) Resolve(
        Placement placement, Rect target, Rect safe, bool rtl)
    {
        if (placement == Placement.Auto)
        {
            return (BestSide(target, safe), PlacementAlign.Center);
        }

        var (side, align) = Split(placement);
        return (side, rtl && side is PlacementSide.Top or PlacementSide.Bottom ? Mirror(align) : align);
    }

    private static PlacementSide BestSide(Rect target, Rect safe)
    {
        // Free space on each side of the target, inside the safe area.
        var below = safe.Bottom - target.Bottom;
        var above = target.Top - safe.Top;
        var right = safe.Right - target.Right;
        var left = target.Left - safe.Left;

        // Ties favour bottom, where users look first, then top, then the inline sides.
        var bestValue = below;
        var bestSide = PlacementSide.Bottom;

        if (above > bestValue) { bestValue = above; bestSide = PlacementSide.Top; }
        if (right > bestValue) { bestValue = right; bestSide = PlacementSide.Right; }
        if (left > bestValue) { bestSide = PlacementSide.Left; }

        return bestSide;
    }

    private static List<(PlacementSide Side, PlacementAlign Align)> BuildCandidates(
        (PlacementSide Side, PlacementAlign Align) preferred,
        IReadOnlyList<Placement>? explicitFallbacks,
        bool rtl)
    {
        var list = new List<(PlacementSide, PlacementAlign)>(8) { preferred };

        if (explicitFallbacks is { Count: > 0 })
        {
            foreach (var fallback in explicitFallbacks)
            {
                if (fallback is Placement.Auto or Placement.Center) continue;
                var (side, align) = Split(fallback);
                if (rtl && side is PlacementSide.Top or PlacementSide.Bottom) align = Mirror(align);
                AddDistinct(list, (side, align));
            }

            return list;
        }

        // The opposite side keeps the same axis, which usually preserves the layout best.
        AddDistinct(list, (Opposite(preferred.Side), preferred.Align));

        foreach (var side in Perpendicular(preferred.Side))
        {
            AddDistinct(list, (side, PlacementAlign.Center));
            AddDistinct(list, (side, PlacementAlign.Start));
        }

        // Re-try the preferred axis with the other alignments before giving up on it.
        AddDistinct(list, (preferred.Side, PlacementAlign.Start));
        AddDistinct(list, (preferred.Side, PlacementAlign.End));

        return list;

        static void AddDistinct(List<(PlacementSide, PlacementAlign)> target, (PlacementSide, PlacementAlign) value)
        {
            if (!target.Contains(value)) target.Add(value);
        }
    }

    private static Rect Compute((PlacementSide Side, PlacementAlign Align) placement, in PlacementRequest request)
    {
        var target = request.Target;
        var size = request.Popover;
        var gap = request.Offset;

        double x, y;

        switch (placement.Side)
        {
            case PlacementSide.Top:
                y = target.Top - gap - size.Height;
                x = AlignAlong(target.Left, target.Width, size.Width, placement.Align);
                break;
            case PlacementSide.Bottom:
                y = target.Bottom + gap;
                x = AlignAlong(target.Left, target.Width, size.Width, placement.Align);
                break;
            case PlacementSide.Left:
                x = target.Left - gap - size.Width;
                y = AlignAlong(target.Top, target.Height, size.Height, placement.Align);
                break;
            case PlacementSide.Right:
                x = target.Right + gap;
                y = AlignAlong(target.Top, target.Height, size.Height, placement.Align);
                break;
            default:
                x = target.CenterX - (size.Width / 2);
                y = target.CenterY - (size.Height / 2);
                break;
        }

        return new Rect(x, y, size.Width, size.Height);
    }

    private static double AlignAlong(double start, double extent, double size, PlacementAlign align) => align switch
    {
        PlacementAlign.Start => start,
        PlacementAlign.End => start + extent - size,
        _ => start + ((extent - size) / 2),
    };

    /// <summary>Area of the popover falling outside the safe box; the candidate score.</summary>
    private static double ClippedArea(Rect rect, Rect safe)
    {
        var area = rect.Area;
        if (area <= 0) return 0;
        return Math.Max(0, area - rect.Intersect(safe).Area);
    }

    /// <summary>Slides a rectangle back inside the safe box without resizing it.</summary>
    private static Rect Shift(Rect rect, Rect safe)
    {
        var x = rect.Width <= safe.Width
            ? Math.Clamp(rect.X, safe.Left, safe.Right - rect.Width)
            : safe.Left;

        var y = rect.Height <= safe.Height
            ? Math.Clamp(rect.Y, safe.Top, safe.Bottom - rect.Height)
            : safe.Top;

        return rect with { X = x, Y = y };
    }

    /// <summary>
    /// Slides a candidate along the target's cross axis so it stays inside the safe area. The
    /// main-axis position is never touched, because that is what keeps the arrow pointing at the
    /// target and the popover clear of it.
    /// </summary>
    private static Rect ClampCross(Rect rect, PlacementSide side, Rect safe) => side switch
    {
        PlacementSide.Top or PlacementSide.Bottom => rect with
        {
            X = rect.Width <= safe.Width ? Math.Clamp(rect.X, safe.Left, safe.Right - rect.Width) : safe.Left,
        },
        PlacementSide.Left or PlacementSide.Right => rect with
        {
            Y = rect.Height <= safe.Height ? Math.Clamp(rect.Y, safe.Top, safe.Bottom - rect.Height) : safe.Top,
        },
        _ => rect,
    };

    private static PlacementResult Finish(
        in PlacementRequest request,
        Rect rect,
        (PlacementSide Side, PlacementAlign Align) placement,
        Rect safe,
        bool usedPreferred,
        double overflow)
    {
        var adjusted = ClampCross(rect, placement.Side, safe);

        var (showArrow, arrowOffset) = Arrow(request, adjusted, placement.Side);

        return new PlacementResult
        {
            Popover = adjusted.Round(),
            Side = placement.Side,
            Align = placement.Align,
            ShowArrow = showArrow,
            ArrowOffset = Math.Round(arrowOffset, 2),
            UsedPreferred = usedPreferred,
            Overflow = Math.Round(overflow, 2),
        };
    }

    private static (bool Show, double Offset) Arrow(in PlacementRequest request, Rect popover, PlacementSide side)
    {
        if (request.ArrowSize <= 0 || side == PlacementSide.Center) return (false, 0);

        var target = request.Target;
        var horizontal = side is PlacementSide.Top or PlacementSide.Bottom;

        var popoverStart = horizontal ? popover.Left : popover.Top;
        var popoverEnd = horizontal ? popover.Right : popover.Bottom;
        var targetStart = horizontal ? target.Left : target.Top;
        var targetEnd = horizontal ? target.Right : target.Bottom;
        var targetCenter = horizontal ? target.CenterX : target.CenterY;

        // No cross-axis overlap means an arrow would point at empty space.
        if (targetEnd <= popoverStart || targetStart >= popoverEnd) return (false, 0);

        var inset = request.CornerRadius + (request.ArrowSize / 2);
        var min = popoverStart + inset;
        var max = popoverEnd - inset;
        if (max < min) return (false, 0);

        // Aim at the target centre, but never let the arrow leave the popover corners or slide off
        // the end of a target that is narrower than the corner inset.
        var center = Math.Clamp(targetCenter, min, max);
        center = Math.Clamp(center, Math.Min(targetStart, max), Math.Max(targetEnd, min));
        center = Math.Clamp(center, min, max);

        return (true, center - popoverStart);
    }

    internal static (PlacementSide Side, PlacementAlign Align) Split(Placement placement) => placement switch
    {
        Placement.Top => (PlacementSide.Top, PlacementAlign.Center),
        Placement.TopStart => (PlacementSide.Top, PlacementAlign.Start),
        Placement.TopEnd => (PlacementSide.Top, PlacementAlign.End),
        Placement.Bottom => (PlacementSide.Bottom, PlacementAlign.Center),
        Placement.BottomStart => (PlacementSide.Bottom, PlacementAlign.Start),
        Placement.BottomEnd => (PlacementSide.Bottom, PlacementAlign.End),
        Placement.Left => (PlacementSide.Left, PlacementAlign.Center),
        Placement.LeftStart => (PlacementSide.Left, PlacementAlign.Start),
        Placement.LeftEnd => (PlacementSide.Left, PlacementAlign.End),
        Placement.Right => (PlacementSide.Right, PlacementAlign.Center),
        Placement.RightStart => (PlacementSide.Right, PlacementAlign.Start),
        Placement.RightEnd => (PlacementSide.Right, PlacementAlign.End),
        Placement.Center => (PlacementSide.Center, PlacementAlign.Center),
        _ => (PlacementSide.Bottom, PlacementAlign.Center),
    };

    private static PlacementAlign Mirror(PlacementAlign align) => align switch
    {
        PlacementAlign.Start => PlacementAlign.End,
        PlacementAlign.End => PlacementAlign.Start,
        _ => PlacementAlign.Center,
    };

    private static PlacementSide Opposite(PlacementSide side) => side switch
    {
        PlacementSide.Top => PlacementSide.Bottom,
        PlacementSide.Bottom => PlacementSide.Top,
        PlacementSide.Left => PlacementSide.Right,
        PlacementSide.Right => PlacementSide.Left,
        _ => PlacementSide.Center,
    };

    private static PlacementSide[] Perpendicular(PlacementSide side) => side switch
    {
        PlacementSide.Top or PlacementSide.Bottom => [PlacementSide.Right, PlacementSide.Left],
        PlacementSide.Left or PlacementSide.Right => [PlacementSide.Bottom, PlacementSide.Top],
        _ => [PlacementSide.Bottom, PlacementSide.Top],
    };
}
