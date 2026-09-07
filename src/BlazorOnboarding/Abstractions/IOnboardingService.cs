namespace BlazorOnboarding;

/// <summary>Fine control over how a tour starts.</summary>
public sealed class StartOptions
{
    /// <summary>Begin at this step instead of the first one.</summary>
    public string? StartAtStepId { get; set; }

    /// <summary>Begin at this index instead of the first step. Ignored when <see cref="StartAtStepId"/> is set.</summary>
    public int? StartAtIndex { get; set; }

    /// <summary>
    /// Continue from persisted progress when there is any. Default <see langword="true"/>.
    /// Ignored when a start position is given explicitly.
    /// </summary>
    public bool Resume { get; set; } = true;

    /// <summary>
    /// Start even when the tour is recorded as completed or dismissed, and even when
    /// <see cref="TourDefinition.CanStart"/> says no. Default <see langword="false"/>.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>Seed values for <see cref="ITourSession.State"/>.</summary>
    public IReadOnlyDictionary<string, object?>? State { get; set; }

    /// <summary>
    /// End any other running tour first. Default <see langword="true"/>; set false to manage
    /// several simultaneous sessions yourself.
    /// </summary>
    public bool StopOthers { get; set; } = true;
}

/// <summary>
/// The entry point for starting, querying and controlling tours. Registered as a scoped service by
/// <c>AddBlazorOnboarding</c>, so it is per-circuit on Blazor Server and per-app on WebAssembly.
/// </summary>
public interface IOnboardingService
{
    /// <summary>The session a host should currently render, if any.</summary>
    ITourSession? Active { get; }

    /// <summary>Every session created in this scope that has not been discarded.</summary>
    IReadOnlyList<ITourSession> Sessions { get; }

    /// <summary>Every tour registered with <see cref="Register"/> or by an <c>OnboardingTour</c> component.</summary>
    IReadOnlyCollection<TourDefinition> RegisteredTours { get; }

    /// <summary>Raised for every lifecycle moment; the simplest analytics hook.</summary>
    event EventHandler<OnboardingEvent>? EventRaised;

    /// <summary>Raised when <see cref="Active"/> or <see cref="Sessions"/> changes.</summary>
    event EventHandler? SessionsChanged;

    /// <summary>
    /// Makes a tour startable by id, e.g. from a help menu. Declarative
    /// <c>&lt;OnboardingTour&gt;</c> components register themselves automatically.
    /// </summary>
    void Register(TourDefinition tour);

    /// <summary>Removes a registration.</summary>
    void Unregister(string tourId);

    /// <summary>Looks up a registered tour.</summary>
    TourDefinition? GetTour(string tourId);

    /// <summary>Starts a tour and returns its session.</summary>
    Task<ITourSession> StartAsync(TourDefinition tour, StartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Starts a previously registered tour by id.</summary>
    Task<ITourSession> StartAsync(string tourId, StartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Starts the tour only if the user has not already completed or dismissed it. Returns
    /// <see langword="null"/> when it was skipped. This is the call most applications want on first
    /// login.
    /// </summary>
    Task<ITourSession?> StartOnceAsync(TourDefinition tour, StartOptions? options = null, CancellationToken cancellationToken = default);

    /// <summary>Clears persisted progress and starts the tour from the beginning.</summary>
    Task<ITourSession> RestartAsync(TourDefinition tour, CancellationToken cancellationToken = default);

    /// <summary>Ends every running session.</summary>
    Task StopAllAsync(TourEndReason reason = TourEndReason.Cancelled, CancellationToken cancellationToken = default);

    /// <summary>The running session for a tour id, if there is one.</summary>
    ITourSession? FindSession(string tourId);

    /// <summary>True when the tour was run to the end at some point.</summary>
    Task<bool> IsCompletedAsync(string tourId, CancellationToken cancellationToken = default);

    /// <summary>True when the tour was completed, skipped or closed, so it should not auto-start.</summary>
    Task<bool> IsClosedAsync(string tourId, CancellationToken cancellationToken = default);

    /// <summary>Reads the persisted record for a tour.</summary>
    Task<OnboardingRecord?> GetRecordAsync(string tourId, CancellationToken cancellationToken = default);

    /// <summary>Forgets a tour, so it can run again.</summary>
    Task ResetAsync(string tourId, CancellationToken cancellationToken = default);

    /// <summary>Forgets every tour.</summary>
    Task ResetAllAsync(CancellationToken cancellationToken = default);
}
