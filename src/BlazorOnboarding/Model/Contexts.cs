namespace BlazorOnboarding;

/// <summary>Information handed to tour-level hooks such as <see cref="TourDefinition.CanStart"/>.</summary>
public sealed class TourContext(TourDefinition tour, IServiceProvider services, ITourSession? session = null)
{
    /// <summary>The tour being evaluated.</summary>
    public TourDefinition Tour { get; } = tour;

    /// <summary>The application's service provider, for resolving anything the hook needs.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>The live session, when one already exists.</summary>
    public ITourSession? Session { get; } = session;
}

/// <summary>
/// Information handed to step-level hooks. Everything needed to make a decision about the current
/// step, plus the service provider for anything else.
/// </summary>
public sealed class StepContext(
    ITourSession session,
    StepDefinition step,
    int index,
    IServiceProvider services,
    CancellationToken cancellationToken = default)
{
    /// <summary>The running session.</summary>
    public ITourSession Session { get; } = session;

    /// <summary>The tour the step belongs to.</summary>
    public TourDefinition Tour => Session.Tour;

    /// <summary>The step being evaluated.</summary>
    public StepDefinition Step { get; } = step;

    /// <summary>Zero-based position of <see cref="Step"/> in <see cref="TourDefinition.Steps"/>.</summary>
    public int Index { get; } = index;

    /// <summary>Total number of declared steps.</summary>
    public int Count => Tour.Steps.Count;

    /// <summary>The application's service provider.</summary>
    public IServiceProvider Services { get; } = services;

    /// <summary>Cancelled when the session stops or the step is left.</summary>
    public CancellationToken CancellationToken { get; } = cancellationToken;

    /// <summary>Per-session scratch state, shared by every hook in the tour.</summary>
    public IDictionary<string, object?> State => Session.State;
}
