using BlazorOnboarding.Tests.Infrastructure;

namespace BlazorOnboarding.Tests;

public class RouteMatcherTests
{
    [Theory]
    [InlineData(null, "anything", true)]
    [InlineData("", "anything", true)]
    [InlineData("settings", "settings", true)]
    [InlineData("/settings", "settings", true)]
    [InlineData("settings", "/settings/", true)]
    [InlineData("Settings", "settings", true)]
    [InlineData("settings", "settings?tab=profile", true)]
    [InlineData("settings", "settings#anchor", true)]
    [InlineData("settings", "billing", false)]
    [InlineData("settings", "settings/profile", false)]
    [InlineData("settings/*", "settings/profile", true)]
    [InlineData("settings/*", "settings", true)]
    [InlineData("settings/*", "settingsx", false)]
    [InlineData("*", "anything/at/all", true)]
    public void Matches_Paths_The_Way_Authors_Expect(string? pattern, string path, bool expected)
        => Assert.Equal(expected, RouteMatcher.Matches(pattern, path));

    [Theory]
    [InlineData("/a/b/", "a/b")]
    [InlineData("a/b?x=1", "a/b")]
    [InlineData("a/b#c", "a/b")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void Normalises_Away_Slashes_Queries_And_Fragments(string? input, string expected)
        => Assert.Equal(expected, RouteMatcher.Normalize(input));
}

public class RoutingTests
{
    private static TourDefinition Tour() => new()
    {
        Id = "multi-page",
        Steps =
        [
            new StepDefinition { Id = "a", Target = StepTarget.Css("#a") },
            new StepDefinition { Id = "b", Target = StepTarget.Css("#b"), Route = "settings" },
            new StepDefinition { Id = "c", Target = StepTarget.Css("#c"), Route = "settings" },
        ],
    };

    [Fact]
    public async Task Navigates_To_A_Step_Route_And_Waits_For_The_Page()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.NextAsync();

        // Navigation is synchronous in the test navigation manager, so the step is live again by
        // the time NextAsync returns.
        Assert.Contains("settings", harness.Navigation!.Navigations);
        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task Holds_The_Tour_When_The_User_Navigates_Away_And_Restores_It_On_Return()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.NextAsync();
        Assert.Equal(TourStatus.Running, session.Status);

        harness.Navigation!.SimulateUserNavigation("dashboard");
        Assert.Equal(TourStatus.Waiting, session.Status);

        harness.Navigation.SimulateUserNavigation("settings");
        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Equal("b", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task Does_Not_Navigate_When_Auto_Navigation_Is_Disabled()
    {
        await using var harness = new OnboardingHarness(options => options.AutoNavigateToStepRoute = false);
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.NextAsync();

        Assert.Empty(harness.Navigation!.Navigations);
        Assert.Equal(TourStatus.Waiting, session.Status);

        harness.Navigation.SimulateUserNavigation("settings");

        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task A_Wildcard_Route_Constrains_Without_Navigating()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var tour = new TourDefinition
        {
            Id = "wildcard",
            Steps = [new StepDefinition { Id = "a", Target = StepTarget.Css("#a"), Route = "settings/*" }],
        };

        var session = await harness.Service.StartAsync(tour);

        Assert.Empty(harness.Navigation!.Navigations);
        Assert.Equal(TourStatus.Waiting, session.Status);

        harness.Navigation.SimulateUserNavigation("settings/profile");

        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task Same_Page_Navigation_Refreshes_The_Measurement()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var tour = new TourDefinition
        {
            Id = "same-page",
            Steps = [new StepDefinition { Id = "a", Target = StepTarget.Css("#a") }],
        };

        await harness.Service.StartAsync(tour);
        var before = harness.Interop.MeasureCount;

        harness.Navigation!.SimulateUserNavigation("?tab=two");

        Assert.True(harness.Interop.MeasureCount > before);
    }

    [Fact]
    public async Task Ending_A_Tour_Unhooks_The_Navigation_Handler()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var tour = new TourDefinition
        {
            Id = "cleanup",
            Steps = [new StepDefinition { Id = "a", Target = StepTarget.Css("#a") }],
        };

        var session = await harness.Service.StartAsync(tour);
        await session.CompleteAsync();

        var measurements = harness.Interop.MeasureCount;
        harness.Navigation!.SimulateUserNavigation("elsewhere");

        Assert.Equal(measurements, harness.Interop.MeasureCount);
        Assert.Equal(TourStatus.Completed, session.Status);
    }

    [Fact]
    public async Task Works_Without_A_NavigationManager()
    {
        await using var harness = new OnboardingHarness(withNavigation: false);
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.NextAsync();

        // With no router available the route requirement is simply ignored rather than hanging.
        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Equal(TourStatus.Running, session.Status);
    }
}
