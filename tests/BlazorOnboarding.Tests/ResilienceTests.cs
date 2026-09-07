using BlazorOnboarding.Tests.Infrastructure;
using Microsoft.JSInterop;

namespace BlazorOnboarding.Tests;

/// <summary>
/// The failure modes that a real deployment actually hits: a Blazor Server circuit dropping
/// mid-tour, a page unloading, storage being unavailable, and the scope being torn down while a
/// tour is running. None of them should surface as an error to the application.
/// </summary>
public class ResilienceTests
{
    private static TourDefinition Tour() => new()
    {
        Id = "resilient",
        Steps =
        [
            new StepDefinition { Id = "a", Target = StepTarget.Css("#a"), Title = "A" },
            new StepDefinition { Id = "b", Target = StepTarget.Css("#b"), Title = "B" },
        ],
    };

    public static TheoryData<Func<Exception>> TransientFailures =>
    [
        () => new JSDisconnectedException("the circuit is gone"),
        () => new JSException("the page is unloading"),
        () => new ObjectDisposedException("module"),
        // Blazor throws this from interop during prerendering.
        () => new InvalidOperationException("JavaScript interop calls cannot be issued during prerendering"),
        () => new OperationCanceledException(),
    ];

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task A_Browser_Failure_During_Activation_Does_Not_Fail_The_Tour(Func<Exception> failure)
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");
        harness.Interop.Failure = failure;

        var session = await harness.Service.StartAsync(Tour());

        // The step is shown without measurement rather than the tour dying.
        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Equal("a", session.CurrentStep!.Id);
        Assert.Null(session.Error);
    }

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task The_Circuit_Can_Drop_Mid_Tour(Func<Exception> failure)
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour());
        Assert.Equal(TourStatus.Running, session.Status);

        harness.Interop.Failure = failure;

        await session.NextAsync();
        await session.RefreshAsync();

        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Null(session.Error);
    }

    [Theory]
    [MemberData(nameof(TransientFailures))]
    public async Task Ending_A_Tour_Survives_A_Dead_Circuit(Func<Exception> failure)
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour());
        harness.Interop.Failure = failure;

        await session.CompleteAsync();

        Assert.Equal(TourStatus.Completed, session.Status);
        Assert.Null(session.Error);
    }

    [Fact]
    public async Task A_Store_That_Is_Unavailable_Does_Not_Break_A_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        // The browser store falls back to memory when interop is unavailable.
        var store = new BrowserOnboardingStore(
            harness.Interop,
            Microsoft.Extensions.Options.Options.Create(new OnboardingOptions()),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<BrowserOnboardingStore>.Instance);

        await store.SetAsync(new OnboardingRecord { TourId = "resilient", CurrentStepId = "a" });

        Assert.NotNull(await store.GetAsync("resilient"));
    }

    [Fact]
    public async Task Disposing_The_Scope_While_A_Tour_Runs_Is_Clean()
    {
        var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour());
        Assert.True(session.IsActive);

        await harness.DisposeAsync();

        // Commands issued after disposal are ignored rather than throwing.
        await session.NextAsync();
        await session.DismissAsync();
    }

    [Fact]
    public async Task A_Session_Can_Be_Disposed_Twice()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = (TourSession)await harness.Service.StartAsync(Tour());

        await session.DisposeAsync();
        await session.DisposeAsync();
        session.Dispose();
    }

    [Fact]
    public async Task A_Prerendered_Pass_Leaves_No_Browser_State_Behind()
    {
        await using var harness = new OnboardingHarness();
        harness.Interop.IsAvailable = false;

        var session = await harness.Service.StartAsync(Tour());
        await session.CompleteAsync();

        Assert.Empty(harness.Interop.AttachedSessions);
        Assert.Empty(harness.Interop.Activations);
        Assert.Equal(TourStatus.Completed, session.Status);
    }

    [Fact]
    public async Task An_Interleaved_Storm_Of_Commands_Leaves_A_Consistent_State()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour());

        // Navigation, geometry pushes and a refresh all racing, as they do on a resizing page.
        await Task.WhenAll(
            session.NextAsync(),
            session.PreviousAsync(),
            harness.Interop.PushGeometryAsync(session.Id),
            session.RefreshAsync(),
            harness.Interop.PushGeometryAsync(session.Id));

        Assert.True(session.Status is TourStatus.Running or TourStatus.Completed);
        Assert.Null(session.Error);
    }

    [Fact]
    public async Task Out_Of_Order_Measurements_Are_Discarded()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = (TourSession)await harness.Service.StartAsync(Tour());
        await harness.Interop.PushGeometryAsync(session.Id);

        var current = session.Placement;

        // A frame from before the current one must not move the popover backwards.
        await session.OnGeometryChangedAsync(new StepGeometry
        {
            Found = true,
            Target = new Rect(900, 900, 10, 10),
            Viewport = new Rect(0, 0, 1024, 768),
            Popover = new Size(320, 200),
            TargetVisible = true,
            Sequence = 0,
        });

        Assert.Equal(current, session.Placement);
    }
}
