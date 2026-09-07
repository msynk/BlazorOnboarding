using BlazorOnboarding.Tests.Infrastructure;

namespace BlazorOnboarding.Tests;

public class PersistenceTests
{
    private static TourDefinition Tour(int version = 1) => new()
    {
        Id = "persisted",
        Version = version,
        Steps =
        [
            new StepDefinition { Id = "a", Target = StepTarget.Css("#a") },
            new StepDefinition { Id = "b", Target = StepTarget.Css("#b") },
            new StepDefinition { Id = "c", Target = StepTarget.Css("#c") },
        ],
    };

    [Fact]
    public async Task Progress_Is_Written_On_Every_Step()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.NextAsync();

        var record = await harness.Service.GetRecordAsync("persisted");

        Assert.NotNull(record);
        Assert.Equal("b", record!.CurrentStepId);
        Assert.Equal(1, record.CurrentIndex);
        Assert.False(record.Completed);
        Assert.Equal(["a", "b"], record.VisitedStepIds);
    }

    [Fact]
    public async Task Completion_Is_Recorded()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.CompleteAsync();

        Assert.True(await harness.Service.IsCompletedAsync("persisted"));
        Assert.True(await harness.Service.IsClosedAsync("persisted"));
        Assert.NotNull((await harness.Service.GetRecordAsync("persisted"))!.CompletedAt);
    }

    [Fact]
    public async Task Dismissal_Closes_The_Tour_Without_Completing_It()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.DismissAsync();

        Assert.False(await harness.Service.IsCompletedAsync("persisted"));
        Assert.True(await harness.Service.IsClosedAsync("persisted"));
    }

    [Fact]
    public async Task An_Interrupted_Tour_Resumes_Where_The_User_Left_Off()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var first = await harness.Service.StartAsync(Tour());
        await first.NextAsync();

        // Interrupted rather than refused: a page reload, a lost circuit, or another tour.
        await first.CancelAsync();

        var resumed = await harness.Service.StartAsync(Tour(), new StartOptions { Force = true });

        Assert.Equal("b", resumed.CurrentStep!.Id);
        Assert.Contains(OnboardingEventKind.TourResumed, harness.Events.Kinds);
    }

    [Fact]
    public async Task Cancelling_Does_Not_Count_As_The_User_Refusing_The_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.CancelAsync();

        Assert.Equal(TourStatus.Cancelled, session.Status);
        Assert.False(await harness.Service.IsClosedAsync("persisted"));

        // So StartOnce still offers it next time.
        Assert.NotNull(await harness.Service.StartOnceAsync(Tour()));
    }

    [Fact]
    public async Task Dismissing_Does_Count_As_The_User_Refusing_The_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.DismissAsync();

        Assert.True(await harness.Service.IsClosedAsync("persisted"));
        Assert.Null(await harness.Service.StartOnceAsync(Tour()));
    }

    [Fact]
    public async Task Resume_Can_Be_Turned_Off()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var first = await harness.Service.StartAsync(Tour());
        await first.NextAsync();
        await first.CancelAsync();

        var restarted = await harness.Service.StartAsync(
            Tour(), new StartOptions { Resume = false, Force = true });

        Assert.Equal("a", restarted.CurrentStep!.Id);
    }

    [Fact]
    public async Task A_Bumped_Version_Discards_Stale_Progress()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var first = await harness.Service.StartAsync(Tour());
        await first.NextAsync();
        await first.CancelAsync();

        var next = await harness.Service.StartAsync(Tour(version: 2), new StartOptions { Force = true });

        Assert.Equal("a", next.CurrentStep!.Id);
    }

    [Fact]
    public async Task StartOnce_Skips_A_Tour_The_User_Has_Finished_With()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var first = await harness.Service.StartOnceAsync(Tour());
        Assert.NotNull(first);
        await first!.CompleteAsync();

        Assert.Null(await harness.Service.StartOnceAsync(Tour()));
    }

    [Fact]
    public async Task Restart_Clears_Progress_And_Begins_Again()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var first = await harness.Service.StartAsync(Tour());
        await first.CompleteAsync();

        var restarted = await harness.Service.RestartAsync(Tour());

        Assert.Equal("a", restarted.CurrentStep!.Id);
        Assert.False(await harness.Service.IsCompletedAsync("persisted"));
    }

    [Fact]
    public async Task Reset_Makes_A_Finished_Tour_Startable_Again()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.CompleteAsync();

        await harness.Service.ResetAsync("persisted");

        Assert.Null(await harness.Service.GetRecordAsync("persisted"));
        Assert.NotNull(await harness.Service.StartOnceAsync(Tour()));
    }

    [Fact]
    public async Task Persistence_Can_Be_Disabled_Per_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var tour = Tour();
        tour.Persist = false;

        var session = await harness.Service.StartAsync(tour);
        await session.CompleteAsync();

        Assert.Null(await harness.Service.GetRecordAsync("persisted"));
    }

    [Fact]
    public async Task Persistence_Can_Be_Disabled_Globally()
    {
        await using var harness = new OnboardingHarness(options => options.Persist = false);
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour());
        await session.CompleteAsync();

        Assert.Null(await harness.Service.GetRecordAsync("persisted"));
    }

    [Fact]
    public async Task Resuming_Onto_A_Deleted_Step_Falls_Back_To_The_Index()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        await harness.Store.SetAsync(new OnboardingRecord
        {
            TourId = "persisted",
            Version = 1,
            CurrentStepId = "removed-long-ago",
            CurrentIndex = 2,
            VisitedStepIds = ["a", "removed-long-ago"],
        });

        var session = await harness.Service.StartAsync(Tour(), new StartOptions { Force = true });

        Assert.Equal("c", session.CurrentStep!.Id);
        // The history keeps only steps that still exist, so Back cannot land on a ghost.
        Assert.DoesNotContain("removed-long-ago", session.History);
    }

    [Fact]
    public async Task The_Browser_Store_Round_Trips_Through_Storage()
    {
        await using var harness = new OnboardingHarness();
        var store = new BrowserOnboardingStore(
            harness.Interop,
            Microsoft.Extensions.Options.Options.Create(new OnboardingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BrowserOnboardingStore>.Instance);

        await store.SetAsync(new OnboardingRecord { TourId = "t1", CurrentStepId = "s2", CurrentIndex = 1 });

        Assert.Single(harness.Interop.Storage);
        Assert.True(harness.Interop.Storage.ContainsKey("blazor-onboarding:t1"));

        var reloaded = await store.GetAsync("t1");
        Assert.Equal("s2", reloaded!.CurrentStepId);

        await store.ClearAsync();
        Assert.Empty(harness.Interop.Storage);
    }

    [Fact]
    public async Task The_Browser_Store_Discards_Unreadable_Values()
    {
        await using var harness = new OnboardingHarness();
        harness.Interop.Storage["blazor-onboarding:broken"] = "{not json";

        var store = new BrowserOnboardingStore(
            harness.Interop,
            Microsoft.Extensions.Options.Options.Create(new OnboardingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BrowserOnboardingStore>.Instance);

        Assert.Null(await store.GetAsync("broken"));
        Assert.Empty(harness.Interop.Storage);
    }
}
