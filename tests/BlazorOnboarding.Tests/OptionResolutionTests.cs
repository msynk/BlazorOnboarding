using BlazorOnboarding.Tests.Infrastructure;

namespace BlazorOnboarding.Tests;

public class OptionResolutionTests
{
    private static async Task<EffectiveStepOptions> ResolveAsync(
        Action<OnboardingOptions>? global = null,
        Action<TourDefinition>? tour = null,
        Action<StepDefinition>? step = null,
        Action<FakeInterop>? browser = null)
    {
        await using var harness = new OnboardingHarness(global);
        harness.WithElements("#a");
        browser?.Invoke(harness.Interop);

        var definition = new StepDefinition { Id = "a", Target = StepTarget.Css("#a") };
        step?.Invoke(definition);

        var tourDefinition = new TourDefinition { Id = "options", Steps = [definition] };
        tour?.Invoke(tourDefinition);

        var session = await harness.Service.StartAsync(tourDefinition);
        return session.CurrentOptions!;
    }

    [Fact]
    public async Task Falls_Back_To_The_Built_In_Defaults()
    {
        var options = await ResolveAsync();

        Assert.Equal(OnboardingDefaults.Placement, options.Placement);
        Assert.Equal(OnboardingDefaults.Offset, options.Offset);
        Assert.Equal(OnboardingDefaults.SpotlightPadding, options.SpotlightPadding);
        Assert.Equal(OnboardingDefaults.Interaction, options.Interaction);
        Assert.Equal(OnboardingDefaults.Progress, options.Progress);
        Assert.True(options.ShowNext);
    }

    [Fact]
    public async Task Global_Options_Beat_The_Defaults()
    {
        var options = await ResolveAsync(global: o => o.Placement = Placement.Right);

        Assert.Equal(Placement.Right, options.Placement);
    }

    [Fact]
    public async Task Tour_Options_Beat_Global_Options()
    {
        var options = await ResolveAsync(
            global: o => o.Placement = Placement.Right,
            tour: t => t.Placement = Placement.Left);

        Assert.Equal(Placement.Left, options.Placement);
    }

    [Fact]
    public async Task Step_Options_Beat_Everything()
    {
        var options = await ResolveAsync(
            global: o => o.Placement = Placement.Right,
            tour: t => t.Placement = Placement.Left,
            step: s => s.Placement = Placement.Top);

        Assert.Equal(Placement.Top, options.Placement);
    }

    [Fact]
    public async Task Popover_Classes_Accumulate_Down_The_Chain()
    {
        var options = await ResolveAsync(
            global: o => o.PopoverClass = "app-theme",
            tour: t => t.PopoverClass = "tour-theme",
            step: s => s.PopoverClass = "step-theme");

        Assert.Equal("app-theme tour-theme step-theme", options.PopoverClass);
    }

    [Fact]
    public async Task Reduced_Motion_Zeroes_The_Animation_Duration()
    {
        var options = await ResolveAsync(browser: b => b.Environment = new BrowserEnvironment { ReducedMotion = true });

        Assert.True(options.ReducedMotion);
        Assert.Equal(TimeSpan.Zero, options.AnimationDuration);
    }

    [Fact]
    public async Task Reduced_Motion_Can_Be_Ignored_Deliberately()
    {
        var options = await ResolveAsync(
            tour: t => t.RespectReducedMotion = false,
            browser: b => b.Environment = new BrowserEnvironment { ReducedMotion = true });

        Assert.False(options.ReducedMotion);
        Assert.Equal(OnboardingDefaults.AnimationDuration, options.AnimationDuration);
    }

    [Fact]
    public async Task Automatic_Direction_Follows_The_Document()
    {
        var rtl = await ResolveAsync(browser: b => b.Environment = new BrowserEnvironment { RightToLeft = true });
        Assert.Equal(OnboardingDirection.RightToLeft, rtl.Direction);
        Assert.True(rtl.RightToLeft);

        var ltr = await ResolveAsync();
        Assert.Equal(OnboardingDirection.LeftToRight, ltr.Direction);
    }

    [Fact]
    public async Task An_Explicit_Direction_Overrides_The_Document()
    {
        var options = await ResolveAsync(
            tour: t => t.Direction = OnboardingDirection.LeftToRight,
            browser: b => b.Environment = new BrowserEnvironment { RightToLeft = true });

        Assert.Equal(OnboardingDirection.LeftToRight, options.Direction);
    }

    [Fact]
    public async Task Labels_Come_From_The_Tour_When_It_Supplies_Them()
    {
        var options = await ResolveAsync(
            global: o => o.Labels = new OnboardingLabels { Next = "Global next" },
            tour: t => t.Labels = new OnboardingLabels { Next = "Weiter" });

        Assert.Equal("Weiter", options.Labels.Next);
    }

    [Fact]
    public async Task Labels_Fall_Back_To_The_Global_Set()
    {
        var options = await ResolveAsync(global: o => o.Labels = new OnboardingLabels { Next = "Continue" });

        Assert.Equal("Continue", options.Labels.Next);
    }
}

public class GeometryTests
{
    [Fact]
    public void Rect_Exposes_Its_Edges_And_Centre()
    {
        var rect = new Rect(10, 20, 100, 50);

        Assert.Equal(10, rect.Left);
        Assert.Equal(20, rect.Top);
        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
        Assert.Equal(60, rect.CenterX);
        Assert.Equal(45, rect.CenterY);
        Assert.Equal(5000, rect.Area);
    }

    [Fact]
    public void Inflate_Grows_On_Every_Side()
    {
        var rect = new Rect(10, 10, 20, 20).Inflate(5);

        Assert.Equal(new Rect(5, 5, 30, 30), rect);
    }

    [Fact]
    public void Intersect_Returns_Empty_For_Disjoint_Rectangles()
    {
        var a = new Rect(0, 0, 10, 10);
        var b = new Rect(20, 20, 10, 10);

        Assert.True(a.Intersect(b).IsEmpty);
        Assert.False(a.IntersectsWith(b));
    }

    [Fact]
    public void Intersect_Returns_The_Overlap()
    {
        var a = new Rect(0, 0, 20, 20);
        var b = new Rect(10, 10, 20, 20);

        Assert.Equal(new Rect(10, 10, 10, 10), a.Intersect(b));
    }

    [Fact]
    public void Union_Ignores_Empty_Rectangles()
    {
        var a = new Rect(0, 0, 10, 10);

        Assert.Equal(a, a.Union(Rect.Empty));
        Assert.Equal(a, Rect.Empty.Union(a));
    }

    [Fact]
    public void ApproximatelyEquals_Absorbs_Sub_Pixel_Jitter()
    {
        var a = new Rect(10, 10, 100, 50);
        var b = new Rect(10.2, 9.9, 100.1, 50);

        Assert.True(a.ApproximatelyEquals(b));
        Assert.False(a.ApproximatelyEquals(b with { Width = 102 }));
    }
}

public class StepTargetTests
{
    [Fact]
    public void Converts_Implicitly_From_A_Selector_String()
    {
        StepTarget target = "#create";

        Assert.Equal("#create", TargetDescriptor.From(target).Selector);
        Assert.Equal(TargetKind.Selector, TargetDescriptor.From(target).Kind);
    }

    [Fact]
    public void An_Anchor_Escapes_Quotes_In_Its_Name()
    {
        var target = StepTarget.Anchor("say \"hi\"");

        Assert.Equal("[data-bo-anchor=\"say \\\"hi\\\"\"]", TargetDescriptor.From(target).Selector);
    }

    [Fact]
    public void Equality_Is_Structural()
    {
        Assert.Equal(StepTarget.Css("#a"), StepTarget.Css("#a"));
        Assert.NotEqual(StepTarget.Css("#a"), StepTarget.Css("#b"));
        Assert.Equal(StepTarget.Anchor("x"), StepTarget.Anchor("x"));
        Assert.NotEqual<StepTarget>(StepTarget.Anchor("x"), StepTarget.Css("x"));
        Assert.Equal(StepTarget.None, StepTarget.None);
    }

    [Fact]
    public async Task A_Dynamic_Target_That_Never_Settles_Degrades_To_None()
    {
        StepTarget? recursive = null;
        recursive = StepTarget.Dynamic(_ => recursive);

        var resolved = await recursive.ResolveAsync(null!);

        Assert.True(resolved.IsNone);
    }

    [Fact]
    public void Rejects_An_Empty_Selector()
    {
        Assert.Throws<ArgumentException>(() => StepTarget.Css("  "));
        Assert.Throws<ArgumentException>(() => StepTarget.Anchor(""));
    }
}
