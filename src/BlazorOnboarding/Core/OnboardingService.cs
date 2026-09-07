using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BlazorOnboarding;

/// <summary>
/// The default <see cref="IOnboardingService"/>. Scoped, so it is per-circuit on Blazor Server and
/// per-app on WebAssembly, which is exactly the lifetime a user's onboarding state wants.
/// </summary>
internal sealed class OnboardingService : IOnboardingService, IDisposable, IAsyncDisposable
{
    private readonly OnboardingOptions _options;
    private readonly IOnboardingInterop _interop;
    private readonly IOnboardingStore _store;
    private readonly IOnboardingLocalizer _localizer;
    private readonly NavigationManager? _navigation;
    private readonly IServiceProvider _services;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<OnboardingService> _logger;
    private readonly IReadOnlyList<IOnboardingObserver> _observers;

    private readonly Dictionary<string, TourDefinition> _registered = new(StringComparer.Ordinal);
    private readonly List<TourSession> _sessions = [];
    private readonly SemaphoreSlim _startGate = new(1, 1);

    private bool _disposed;

    public OnboardingService(
        IOptions<OnboardingOptions> options,
        IOnboardingInterop interop,
        IOnboardingStore store,
        IOnboardingLocalizer localizer,
        IServiceProvider services,
        ILoggerFactory loggerFactory,
        IEnumerable<IOnboardingObserver> observers,
        NavigationManager? navigation = null)
    {
        _options = options.Value;
        _interop = interop;
        _store = store;
        _localizer = localizer;
        _services = services;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger<OnboardingService>();
        _observers = observers.ToArray();
        _navigation = navigation;
    }

    public ITourSession? Active { get; private set; }

    public IReadOnlyList<ITourSession> Sessions => _sessions;

    public IReadOnlyCollection<TourDefinition> RegisteredTours => _registered.Values;

    public event EventHandler<OnboardingEvent>? EventRaised;

    public event EventHandler? SessionsChanged;

    // ---- Registration ------------------------------------------------------

    public void Register(TourDefinition tour)
    {
        ArgumentNullException.ThrowIfNull(tour);
        ArgumentException.ThrowIfNullOrWhiteSpace(tour.Id);

        _registered[tour.Id] = tour;
    }

    public void Unregister(string tourId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tourId);
        _registered.Remove(tourId);
    }

    public TourDefinition? GetTour(string tourId)
        => _registered.GetValueOrDefault(tourId);

    // ---- Starting ----------------------------------------------------------

    public async Task<ITourSession> StartAsync(
        TourDefinition tour, StartOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tour);
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (tour.Steps.Count == 0)
        {
            throw new OnboardingException($"Tour '{tour.Id}' has no steps.") { TourId = tour.Id };
        }

        var startOptions = options ?? new StartOptions();

        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!startOptions.Force && tour.CanStart is not null)
            {
                var allowed = await tour.CanStart(new TourContext(tour, _services)).ConfigureAwait(false);
                if (!allowed)
                {
                    throw new OnboardingException($"Tour '{tour.Id}' declined to start.") { TourId = tour.Id };
                }
            }

            if (startOptions.StopOthers)
            {
                await StopRunningSessionsAsync(TourEndReason.Cancelled).ConfigureAwait(false);
            }

            // Restarting a tour that is already running should not leave the old session attached.
            var existing = _sessions.FirstOrDefault(s => s.Tour.Id == tour.Id && s.IsActive);
            if (existing is not null)
            {
                await existing.CancelAsync(CancellationToken.None).ConfigureAwait(false);
            }

            Register(tour);

            var session = new TourSession(
                tour, _options, _interop, _store, _localizer, _navigation, _services,
                _loggerFactory.CreateLogger<TourSession>(), PublishAsync);

            session.Changed += OnSessionChanged;
            _sessions.Add(session);
            Active = session;

            RaiseSessionsChanged();

            await session.StartAsync(startOptions, cancellationToken).ConfigureAwait(false);
            return session;
        }
        finally
        {
            _startGate.Release();
        }
    }

    public Task<ITourSession> StartAsync(string tourId, StartOptions? options = null, CancellationToken cancellationToken = default)
    {
        var tour = GetTour(tourId)
            ?? throw new OnboardingException($"No tour is registered with id '{tourId}'.") { TourId = tourId };

        return StartAsync(tour, options, cancellationToken);
    }

    public async Task<ITourSession?> StartOnceAsync(
        TourDefinition tour, StartOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tour);

        var startOptions = options ?? new StartOptions();

        if (!startOptions.Force && await IsClosedAsync(tour.Id, cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        if (!startOptions.Force && tour.CanStart is not null)
        {
            var allowed = await tour.CanStart(new TourContext(tour, _services)).ConfigureAwait(false);
            if (!allowed) return null;
        }

        // CanStart has already been consulted; skip the second evaluation inside StartAsync.
        return await StartAsync(tour, CloneForced(startOptions), cancellationToken).ConfigureAwait(false);
    }

    public async Task<ITourSession> RestartAsync(TourDefinition tour, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(tour);

        await ResetAsync(tour.Id, cancellationToken).ConfigureAwait(false);

        return await StartAsync(
            tour,
            new StartOptions { Resume = false, Force = true },
            cancellationToken).ConfigureAwait(false);
    }

    private static StartOptions CloneForced(StartOptions source) => new()
    {
        StartAtStepId = source.StartAtStepId,
        StartAtIndex = source.StartAtIndex,
        Resume = source.Resume,
        Force = true,
        State = source.State,
        StopOthers = source.StopOthers,
    };

    // ---- Stopping and querying --------------------------------------------

    public async Task StopAllAsync(TourEndReason reason = TourEndReason.Cancelled, CancellationToken cancellationToken = default)
    {
        await StopRunningSessionsAsync(reason).ConfigureAwait(false);
        RaiseSessionsChanged();
    }

    private async Task StopRunningSessionsAsync(TourEndReason reason)
    {
        foreach (var session in _sessions.Where(s => s.IsActive).ToArray())
        {
            switch (reason)
            {
                case TourEndReason.Completed:
                    await session.CompleteAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                case TourEndReason.Skipped:
                    await session.SkipAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                case TourEndReason.Dismissed:
                    await session.DismissAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
                default:
                    await session.CancelAsync(CancellationToken.None).ConfigureAwait(false);
                    break;
            }
        }
    }

    public ITourSession? FindSession(string tourId)
        => _sessions.FirstOrDefault(s => s.Tour.Id == tourId);

    public async Task<bool> IsCompletedAsync(string tourId, CancellationToken cancellationToken = default)
        => (await GetRecordAsync(tourId, cancellationToken).ConfigureAwait(false))?.Completed ?? false;

    public async Task<bool> IsClosedAsync(string tourId, CancellationToken cancellationToken = default)
        => (await GetRecordAsync(tourId, cancellationToken).ConfigureAwait(false))?.IsClosed ?? false;

    public async Task<OnboardingRecord?> GetRecordAsync(string tourId, CancellationToken cancellationToken = default)
    {
        try
        {
            var record = await _store.GetAsync(tourId, cancellationToken).ConfigureAwait(false);

            // A record written by an earlier version of the tour is not usable.
            if (record is not null && GetTour(tourId) is { } tour && record.Version != tour.Version)
            {
                return null;
            }

            return record;
        }
        catch (Exception ex) when (TourSession.IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not read the record for tour {TourId}.", tourId);
            return null;
        }
    }

    public async Task ResetAsync(string tourId, CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.RemoveAsync(tourId, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (TourSession.IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not reset tour {TourId}.", tourId);
        }
    }

    public async Task ResetAllAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            await _store.ClearAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (TourSession.IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not reset stored progress.");
        }
    }

    // ---- Event fan-out -----------------------------------------------------

    private async ValueTask PublishAsync(OnboardingEvent onboardingEvent)
    {
        try
        {
            EventRaised?.Invoke(this, onboardingEvent);
        }
        catch (Exception ex)
        {
            // An analytics handler must never be able to break a tour.
            _logger.LogWarning(ex, "An onboarding event handler threw.");
        }

        foreach (var observer in _observers)
        {
            try
            {
                await observer.OnEventAsync(onboardingEvent).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Onboarding observer {Observer} threw.", observer.GetType().Name);
            }
        }
    }

    private void OnSessionChanged(object? sender, TourSessionChangedEventArgs e)
    {
        if (!e.Change.HasFlag(TourSessionChange.Ended)) return;

        if (ReferenceEquals(Active, sender))
        {
            Active = _sessions.FirstOrDefault(s => s.IsActive);
        }

        RaiseSessionsChanged();
    }

    private void RaiseSessionsChanged()
    {
        try
        {
            SessionsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "An onboarding SessionsChanged handler threw.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var session in _sessions)
        {
            session.Changed -= OnSessionChanged;
            await session.DisposeAsync().ConfigureAwait(false);
        }

        Cleanup();
    }

    /// <summary>
    /// Synchronous teardown. Blazor disposes its scopes asynchronously, but plenty of hosts and
    /// test frameworks do not, and a service that only supports async disposal makes those throw.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var session in _sessions)
        {
            session.Changed -= OnSessionChanged;
            session.Dispose();
        }

        Cleanup();
    }

    private void Cleanup()
    {
        _sessions.Clear();
        Active = null;
        _startGate.Dispose();
    }
}
