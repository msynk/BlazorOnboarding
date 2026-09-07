namespace BlazorOnboarding.Tests;

public class PlacementEngineTests
{
    private static PlacementRequest Request(
        Rect target,
        Size popover,
        Placement preferred = Placement.Auto,
        Rect? viewport = null,
        bool rtl = false,
        IReadOnlyList<Placement>? fallbacks = null) => new()
        {
            Target = target,
            Popover = popover,
            Viewport = viewport ?? new Rect(0, 0, 1000, 800),
            Preferred = preferred,
            Fallbacks = fallbacks,
            Offset = 12,
            ViewportPadding = 16,
            ArrowSize = 10,
            CornerRadius = 12,
            RightToLeft = rtl,
            HasTarget = true,
        };

    [Fact]
    public void Centers_When_There_Is_No_Target()
    {
        var request = Request(Rect.Empty, new Size(300, 200)) with { HasTarget = false };

        var result = PlacementEngine.Place(request);

        Assert.Equal(PlacementSide.Center, result.Side);
        Assert.False(result.ShowArrow);
        Assert.Equal(350, result.Popover.X);
        Assert.Equal(300, result.Popover.Y);
    }

    [Fact]
    public void Honours_An_Explicit_Placement_That_Fits()
    {
        var result = PlacementEngine.Place(Request(new Rect(400, 400, 100, 40), new Size(300, 150), Placement.Top));

        Assert.Equal(PlacementSide.Top, result.Side);
        Assert.True(result.UsedPreferred);
        // Bottom edge of the popover sits one offset above the target.
        Assert.Equal(400 - 12 - 150, result.Popover.Y);
        // Centre-aligned on the target.
        Assert.Equal(450 - 150, result.Popover.X);
    }

    [Fact]
    public void Flips_To_The_Opposite_Side_When_The_Preferred_One_Does_Not_Fit()
    {
        // Target near the top: there is no room above for a 300px tall popover.
        var result = PlacementEngine.Place(Request(new Rect(400, 20, 100, 40), new Size(300, 300), Placement.Top));

        Assert.Equal(PlacementSide.Bottom, result.Side);
        Assert.False(result.UsedPreferred);
        Assert.Equal(0, result.Overflow);
    }

    [Fact]
    public void Falls_Back_To_A_Perpendicular_Side_When_Neither_Vertical_Side_Fits()
    {
        // A tall popover in a short viewport: only an inline side can work, and Right is tried
        // before Left.
        var request = Request(
            new Rect(700, 300, 60, 60),
            new Size(240, 560),
            Placement.Bottom,
            viewport: new Rect(0, 0, 1200, 640));

        var result = PlacementEngine.Place(request);

        Assert.Equal(PlacementSide.Right, result.Side);
        Assert.Equal(0, result.Overflow);
    }

    [Fact]
    public void Shifts_Along_The_Cross_Axis_Rather_Than_Flipping()
    {
        // Target hard against the left edge; a centred popover would hang off it. Sliding it right
        // keeps the requested side, which is much less disruptive than moving it beside the target.
        var result = PlacementEngine.Place(Request(new Rect(10, 300, 40, 40), new Size(320, 160), Placement.Bottom));

        Assert.Equal(PlacementSide.Bottom, result.Side);
        Assert.True(result.UsedPreferred);
        Assert.Equal(16, result.Popover.Left);
        Assert.True(result.Popover.Right <= 1000 - 16);
        Assert.Equal(0, result.Overflow);
    }

    [Fact]
    public void Uses_An_Explicit_Fallback_Order()
    {
        var result = PlacementEngine.Place(Request(
            new Rect(400, 20, 100, 40),
            new Size(300, 300),
            Placement.Top,
            fallbacks: [Placement.Right]));

        Assert.Equal(PlacementSide.Right, result.Side);
    }

    [Fact]
    public void Auto_Picks_The_Side_With_The_Most_Room()
    {
        // Target low in the viewport, so above has far more space than below.
        var result = PlacementEngine.Place(Request(new Rect(450, 700, 100, 40), new Size(200, 120)));

        Assert.Equal(PlacementSide.Top, result.Side);
    }

    [Fact]
    public void Arrow_Points_At_The_Target_Centre()
    {
        var result = PlacementEngine.Place(Request(new Rect(400, 400, 100, 40), new Size(300, 150), Placement.Bottom));

        Assert.True(result.ShowArrow);
        // The popover is centred, so the arrow sits at its midpoint.
        Assert.Equal(150, result.ArrowOffset, 1);
    }

    [Fact]
    public void Arrow_Stays_Inside_The_Popover_Corners_For_An_Edge_Target()
    {
        var result = PlacementEngine.Place(Request(new Rect(4, 300, 24, 24), new Size(320, 160), Placement.Bottom));

        Assert.True(result.ShowArrow);
        // Corner radius plus half the arrow keeps it off the rounded corner.
        Assert.True(result.ArrowOffset >= 12 + 5, $"Arrow offset {result.ArrowOffset} overlaps the corner.");
        Assert.True(result.ArrowOffset <= 320 - 12 - 5);
    }

    [Fact]
    public void Arrow_Is_Hidden_When_The_Popover_Does_Not_Overlap_The_Target()
    {
        // A wide popover under a tiny corner target is shifted clear of it entirely, so an arrow
        // would point at empty space.
        var result = PlacementEngine.Place(Request(new Rect(0, 0, 10, 10), new Size(400, 200), Placement.Bottom));

        Assert.Equal(PlacementSide.Bottom, result.Side);
        Assert.Equal(16, result.Popover.Left);
        Assert.False(result.ShowArrow);
    }

    [Fact]
    public void Arrow_Is_Hidden_When_Its_Size_Is_Zero()
    {
        var request = Request(new Rect(400, 400, 100, 40), new Size(300, 150), Placement.Bottom) with { ArrowSize = 0 };

        Assert.False(PlacementEngine.Place(request).ShowArrow);
    }

    [Fact]
    public void Mirrors_Start_And_End_Alignment_In_RightToLeft()
    {
        var ltr = PlacementEngine.Place(Request(new Rect(400, 300, 200, 40), new Size(120, 100), Placement.BottomStart));
        var rtl = PlacementEngine.Place(Request(new Rect(400, 300, 200, 40), new Size(120, 100), Placement.BottomStart, rtl: true));

        Assert.Equal(PlacementAlign.Start, ltr.Align);
        Assert.Equal(400, ltr.Popover.X);

        Assert.Equal(PlacementAlign.End, rtl.Align);
        Assert.Equal(600 - 120, rtl.Popover.X);
    }

    [Fact]
    public void Clamps_A_Popover_Larger_Than_The_Viewport_To_The_Safe_Area()
    {
        var request = Request(
            new Rect(100, 100, 40, 40),
            new Size(900, 900),
            Placement.Bottom,
            viewport: new Rect(0, 0, 400, 400));

        var result = PlacementEngine.Place(request);

        // The start of the content stays reachable rather than being scrolled off the top-left.
        Assert.Equal(16, result.Popover.X);
        Assert.Equal(16, result.Popover.Y);
    }

    [Fact]
    public void Survives_A_Viewport_Smaller_Than_Its_Own_Padding()
    {
        var request = Request(
            new Rect(5, 5, 10, 10),
            new Size(50, 50),
            Placement.Bottom,
            viewport: new Rect(0, 0, 20, 20));

        var result = PlacementEngine.Place(request);

        Assert.False(double.IsNaN(result.Popover.X));
        Assert.False(double.IsNaN(result.Popover.Y));
    }

    [Fact]
    public void Placement_Is_Deterministic()
    {
        var request = Request(new Rect(123.456, 234.567, 89.1, 33.3), new Size(301.7, 155.2));

        var first = PlacementEngine.Place(request);
        var second = PlacementEngine.Place(request);

        Assert.Equal(first, second);
    }

    [Theory]
    [InlineData(Placement.Top, PlacementSide.Top)]
    [InlineData(Placement.TopStart, PlacementSide.Top)]
    [InlineData(Placement.BottomEnd, PlacementSide.Bottom)]
    [InlineData(Placement.LeftStart, PlacementSide.Left)]
    [InlineData(Placement.Right, PlacementSide.Right)]
    public void Every_Placement_Resolves_To_Its_Own_Side_When_There_Is_Room(Placement placement, PlacementSide expected)
    {
        var result = PlacementEngine.Place(Request(new Rect(480, 380, 40, 40), new Size(160, 100), placement));

        Assert.Equal(expected, result.Side);
    }
}
