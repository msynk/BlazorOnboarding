using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.Extensions.Logging;

namespace BlazorOnboarding;

/// <summary>
/// The tour state machine. Owns step selection, branching, waiting, persistence and the
/// conversation with the browser layer. It renders nothing: the UI observes
/// <see cref="Changed"/> and reads the published state.
/// </summary>
/// <remarks>
/// Every public command funnels through <see cref="RunGuardedAsync"/>, which holds a single
/// transition lock. That is what makes rapid clicking, a resize storm and a route change arriving
/// at the same moment safe: they queue instead of interleaving. Long-running work inside a
/// transition also bumps <c>_generation</c>, so an await that resumes after the user has already
/// moved on can detect it lost the race and abandon its results.
/// </remarks>
internal sealed class TourSession : ITourSession, IOnboardingInteropCallbacks, IDisposable, IAsyncDisposable
{
    private readonly OnboardingOptions _globalOptions;
    private readonly IOnboardingInterop _interop;
    private readonly IOnboardingStore _store;
    private readonly IOnboardingLocalizer _localizer;
    private readonly NavigationManager? _navigation;
    private readonly IServiceProvider _services;
    private readonly ILogger _logger;
    private readonly Func<OnboardingEvent, ValueTask> _publish;

    /// <summary>Serialises transitions so two commands can never interleave.</summary>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// Marks the async flow that currently owns <see cref="_gate"/>. Application hooks are awaited
    /// while the lock is held and are entirely within their rights to call back into the session
    /// (a validation handler that dismisses the tour, say), so those nested calls have to run
    /// inline instead of waiting for a lock their own caller is holding.
    /// </summary>
    private readonly AsyncLocal<bool> _inTransition = new();

    private readonly List<string> _history = [];
    private readonly Dictionary<string, object?> _state = [];

    /// <summary>Indices of the steps whose conditions currently pass, in order.</summary>
    private List<int> _plan = [];

    private CancellationTokenSource? _stepCts;
    private BrowserEnvironment _environment = BrowserEnvironment.Unknown;

    private StepGeometry? _geometry;
    private long _lastSequence = -1;
    private bool _attached;
    private bool _navigationHooked;
    private bool _disposed;
    private bool _focusCaptured;
    private DateTimeOffset _stepShownAt;
    private string? _pendingRoute;
    private StepTarget _resolvedTarget = StepTarget.None;

    /// <summary>Bumped on every transition so stale async work can detect it lost the race.</summary>
    private int _generation;

    public TourSession(
        TourDefinition tour,
        OnboardingOptions globalOptions,
        IOnboardingInterop interop,
        IOnboardingStore store,
        IOnboardingLocalizer localizer,
        NavigationManager? navigation,
        IServiceProvider services,
        ILogger logger,
        Func<OnboardingEvent, ValueTask> publish)
    {
        Tour = tour;
        _globalOptions = globalOptions;
        _interop = interop;
        _store = store;
        _localizer = localizer;
        _navigation = navigation;
        _services = services;
        _logger = logger;
        _publish = publish;

        CurrentOptions = ResolveOptions(null);
    }

    public string Id { get; } = $"bo-{Guid.NewGuid():N}";

    public TourDefinition Tour { get; }

    public TourStatus Status { get; private set; } = TourStatus.Idle;

    public StepDefinition? CurrentStep { get; private set; }

    public int CurrentIndex { get; private set; } = -1;

    public EffectiveStepOptions? CurrentOptions { get; private set; }

    public Rect? TargetRect => _geometry is { Found: true } geometry && !geometry.Target.IsEmpty ? geometry.Target : null;

    public PlacementResult? Placement { get; private set; }

    public bool HasTarget => TargetRect is not null;

    public bool IsWaiting => Status == TourStatus.Waiting;

    public bool IsActive => Status is TourStatus.Running or TourStatus.Waiting;

    public IReadOnlyList<string> History => _history;

    public IDictionary<string, object?> State => _state;

    public TourEndReason? EndReason { get; private set; }

    public Exception? Error { get; private set; }

    /// <summary>True while the tour has stopped for good.</summary>
    public bool IsFinished => Status
        is TourStatus.Completed or TourStatus.Dismissed or TourStatus.Skipped
        or TourStatus.Cancelled or TourStatus.Failed;

    public int DisplayCount => _plan.Count > 0 ? _plan.Count : (CurrentIndex >= 0 ? 1 : 0);

    public int DisplayPosition
    {
        get
        {
            if (CurrentIndex < 0) return 0;
            var position = _plan.IndexOf(CurrentIndex);
            return position >= 0 ? position + 1 : Math.Min(Math.Max(_history.Count, 1), DisplayCount);
        }
    }

    public bool IsFirstStep => CurrentStep?.ResolvePrevious is null && _history.Count <= 1;

    public bool IsLastStep
    {
        get
        {
            if (CurrentStep is null) return true;

            // A branching step cannot be known to be last without running user code, so the
            // forward button keeps saying "Next" and the hook decides at click time.
            if (CurrentStep.ResolveNext is not null) return false;

            var position = _plan.IndexOf(CurrentIndex);
            return position < 0 ? CurrentIndex >= Tour.Steps.Count - 1 : position >= _plan.Count - 1;
        }
    }

    public event EventHandler<TourSessionChangedEventArgs>? Changed;

    // ---- Start -------------------------------------------------------------

    public Task StartAsync(CancellationToken cancellationToken = default)
        => StartAsync(new StartOptions(), cancellationToken);

    /// <summary>Applies <see cref="StartOptions"/> and shows the first eligible step.</summary>
    public Task StartAsync(StartOptions options, CancellationToken cancellationToken = default)
        => RunGuardedAsync(async ct =>
        {
            if (Status != TourStatus.Idle) return;

            if (options.State is not null)
            {
                foreach (var (key, value) in options.State) _state[key] = value;
            }

            _environment = await SafeInitializeAsync(ct).ConfigureAwait(false);
            CurrentOptions = ResolveOptions(null);

            await SafeAttachAsync(ct).ConfigureAwait(false);
            HookNavigation();

            var startIndex = await DetermineStartIndexAsync(options, ct).ConfigureAwait(false);

            Status = TourStatus.Running;

            if (Tour.OnStarted is not null)
            {
                await Tour.OnStarted(new TourContext(Tour, _services, this)).ConfigureAwait(false);
            }

            await PublishAsync(OnboardingEventKind.TourStarted).ConfigureAwait(false);
            await MoveToAsync(startIndex, StepChangeReason.Start, +1, ct).ConfigureAwait(false);
        }, cancellationToken);

    private async Task<int> DetermineStartIndexAsync(StartOptions options, CancellationToken cancellationToken)
    {
        if (options.StartAtStepId is { } stepId)
        {
            var explicitStep = Tour.IndexOf(stepId);
            if (explicitStep >= 0) return explicitStep;
        }

        if (options.StartAtIndex is { } explicitIndex)
        {
            return Math.Clamp(explicitIndex, 0, Math.Max(0, Tour.Steps.Count - 1));
        }

        if (!options.Resume || !ShouldPersist) return 0;

        var record = await SafeGetRecordAsync(cancellationToken).ConfigureAwait(false);
        if (record is null || record.IsClosed || record.Version != Tour.Version) return 0;

        // Prefer the id: indices shift as soon as a step is inserted.
        var resumeIndex = record.CurrentStepId is null ? -1 : Tour.IndexOf(record.CurrentStepId);
        if (resumeIndex < 0) resumeIndex = Math.Clamp(record.CurrentIndex, 0, Math.Max(0, Tour.Steps.Count - 1));

        if (resumeIndex > 0)
        {
            _history.Clear();
            // Keep only history entries that still exist, so Back cannot land on a deleted step.
            _history.AddRange(record.VisitedStepIds.Where(id => Tour.IndexOf(id) >= 0));
            if (_history.Count > 0 && _history[^1] == Tour.Steps[resumeIndex].Id)
            {
                _history.RemoveAt(_history.Count - 1);
            }

            await PublishAsync(OnboardingEventKind.TourResumed).ConfigureAwait(false);
        }

        return resumeIndex;
    }

    // ---- Public commands ---------------------------------------------------

    public Task NextAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(NextCoreAsync, cancellationToken);

    public Task PreviousAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(PreviousCoreAsync, cancellationToken);

    public Task GoToAsync(string stepId, CancellationToken cancellationToken = default)
        => RunGuardedAsync(ct => GoToCoreAsync(stepId, ct), cancellationToken);

    public Task GoToAsync(int index, CancellationToken cancellationToken = default)
        => RunGuardedAsync(ct =>
        {
            if (index < 0 || index >= Tour.Steps.Count) throw new ArgumentOutOfRangeException(nameof(index));
            return MoveToAsync(index, StepChangeReason.Jump, +1, ct);
        }, cancellationToken);

    public Task SkipAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(ct => EndAsync(TourEndReason.Skipped, ct), cancellationToken);

    public Task CompleteAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(ct => EndAsync(TourEndReason.Completed, ct), cancellationToken);

    public Task DismissAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(ct => EndAsync(TourEndReason.Dismissed, ct), cancellationToken);

    public Task CancelAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(ct => EndAsync(TourEndReason.Cancelled, ct), cancellationToken);

    public Task PauseAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(async _ =>
        {
            if (Status is not (TourStatus.Running or TourStatus.Waiting)) return;

            Status = TourStatus.Paused;
            await SafeDeactivateAsync().ConfigureAwait(false);
            await PublishAsync(OnboardingEventKind.TourPaused).ConfigureAwait(false);
            Notify(TourSessionChange.Step);
        }, cancellationToken);

    public Task ResumeAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(async ct =>
        {
            if (Status != TourStatus.Paused || CurrentIndex < 0) return;

            Status = TourStatus.Running;
            await MoveToAsync(CurrentIndex, StepChangeReason.Resume, +1, ct).ConfigureAwait(false);
        }, cancellationToken);

    public Task RefreshAsync(CancellationToken cancellationToken = default)
        => RunGuardedAsync(async _ =>
        {
            if (CurrentStep is null || Status != TourStatus.Running) return;

            var geometry = await SafeMeasureAsync().ConfigureAwait(false);
            if (geometry is null) return;

            ApplyGeometry(geometry);
            Notify(TourSessionChange.Geometry);
        }, cancellationToken);

    public Task InvokeActionAsync(StepAction action, CancellationToken cancellationToken = default)
        => RunGuardedAsync(async ct =>
        {
            if (CurrentStep is null) return;

            var step = CurrentStep;

            if (action.OnClick is not null)
            {
                await action.OnClick(CreateContext(step)).ConfigureAwait(false);
            }

            await PublishAsync(OnboardingEventKind.ActionInvoked, step, action).ConfigureAwait(false);

            // The handler is allowed to end the tour or navigate itself.
            if (!IsActive || !ReferenceEquals(CurrentStep, step)) return;

            switch (action.Effect)
            {
                case StepActionEffect.Next:
                    await NextCoreAsync(ct).ConfigureAwait(false);
                    break;
                case StepActionEffect.Previous:
                    await PreviousCoreAsync(ct).ConfigureAwait(false);
                    break;
                case StepActionEffect.GoTo when action.TargetStepId is { } targetId:
                    await GoToCoreAsync(targetId, ct).ConfigureAwait(false);
                    break;
                case StepActionEffect.Complete:
                    await EndAsync(TourEndReason.Completed, ct).ConfigureAwait(false);
                    break;
                case StepActionEffect.Skip:
                    await EndAsync(TourEndReason.Skipped, ct).ConfigureAwait(false);
                    break;
                case StepActionEffect.Dismiss:
                    await EndAsync(TourEndReason.Dismissed, ct).ConfigureAwait(false);
                    break;
            }
        }, cancellationToken);

    // ---- Command cores (transition lock already held) ----------------------

    private async Task NextCoreAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep is null || IsFinished) return;

        var step = CurrentStep;
        var context = CreateContext(step);

        if (step.CanAdvance is not null && !await step.CanAdvance(context).ConfigureAwait(false))
        {
            return;
        }

        if (step.ResolveNext is not null)
        {
            var nextId = await step.ResolveNext(context).ConfigureAwait(false);

            if (nextId == TourDefinition.EndStepId)
            {
                await EndAsync(TourEndReason.Completed, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (nextId is not null)
            {
                await GoToCoreAsync(nextId, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        await MoveToAsync(null, StepChangeReason.Next, +1, cancellationToken).ConfigureAwait(false);
    }

    private async Task PreviousCoreAsync(CancellationToken cancellationToken)
    {
        if (CurrentStep is null || IsFinished) return;

        if (CurrentStep.ResolvePrevious is not null)
        {
            var previousId = await CurrentStep.ResolvePrevious(CreateContext(CurrentStep)).ConfigureAwait(false);
            if (previousId is not null && Tour.IndexOf(previousId) is var explicitIndex and >= 0)
            {
                PopHistory();
                await MoveToAsync(explicitIndex, StepChangeReason.Previous, -1, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        // The visit history is authoritative: with branching, "the step before this one" is
        // whatever the user actually saw, not the previous array element.
        if (_history.Count >= 2)
        {
            var targetId = _history[^2];
            var index = Tour.IndexOf(targetId);
            if (index >= 0)
            {
                // Drop both the current entry and the destination; MoveToAsync re-pushes it.
                _history.RemoveRange(_history.Count - 2, 2);
                await MoveToAsync(index, StepChangeReason.Previous, -1, cancellationToken).ConfigureAwait(false);
                return;
            }
        }

        if (PreviousPlanned(CurrentIndex) < 0) return;

        PopHistory();
        await MoveToAsync(null, StepChangeReason.Previous, -1, cancellationToken).ConfigureAwait(false);
    }

    private Task GoToCoreAsync(string stepId, CancellationToken cancellationToken)
    {
        var index = Tour.IndexOf(stepId);
        if (index < 0)
        {
            throw new OnboardingException($"Step '{stepId}' was not found in tour '{Tour.Id}'.")
            {
                TourId = Tour.Id,
                StepId = stepId,
            };
        }

        return MoveToAsync(index, StepChangeReason.Jump, +1, cancellationToken);
    }

    // ---- The transition itself --------------------------------------------

    /// <summary>
    /// Performs one transition. Pass <see langword="null"/> for <paramref name="requestedIndex"/>
    /// to mean "whichever step comes next in <paramref name="direction"/>": the destination is then
    /// resolved after the leave hooks have run, so a hook that changes the state a condition
    /// depends on is reflected in where the user actually lands.
    /// </summary>
    private async Task MoveToAsync(int? requestedIndex, StepChangeReason reason, int direction, CancellationToken cancellationToken)
    {
        if (_disposed || IsFinished) return;

        var previousStep = CurrentStep;
        var isReentry = reason is StepChangeReason.Resume or StepChangeReason.Retarget;

        if (previousStep is not null && !isReentry)
        {
            if (previousStep.OnBeforeLeave is not null)
            {
                await previousStep.OnBeforeLeave(CreateContext(previousStep)).ConfigureAwait(false);
            }

            await PublishAsync(OnboardingEventKind.StepLeft, previousStep, duration: DateTimeOffset.UtcNow - _stepShownAt)
                .ConfigureAwait(false);
        }

        await RebuildPlanAsync().ConfigureAwait(false);

        var index = requestedIndex is { } explicitIndex
            ? ResolveEligible(explicitIndex, direction)
            : direction > 0 ? NextPlanned(CurrentIndex) : PreviousPlanned(CurrentIndex);

        if (index < 0)
        {
            // Nothing eligible ahead: forward means the tour is over, backward means stay put.
            if (direction > 0) await EndAsync(TourEndReason.Completed, cancellationToken).ConfigureAwait(false);
            return;
        }

        var generation = ++_generation;

        CancelStep();
        _stepCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var stepToken = _stepCts.Token;

        await SafeDeactivateAsync().ConfigureAwait(false);

        var step = Tour.Steps[index];
        CurrentStep = step;
        CurrentIndex = index;
        CurrentOptions = ResolveOptions(step);
        _geometry = null;
        _lastSequence = -1;
        Placement = null;

        if (_history.Count == 0 || _history[^1] != step.Id) _history.Add(step.Id);

        Notify(TourSessionChange.Step);

        // Route handling comes first: the element cannot exist until the right page is rendered.
        if (!EnsureRoute(step)) return;

        _resolvedTarget = await step.Target.ResolveAsync(CreateContext(step, stepToken)).ConfigureAwait(false);
        if (generation != _generation) return;

        if (step.OnBeforeShow is not null)
        {
            await step.OnBeforeShow(CreateContext(step, stepToken)).ConfigureAwait(false);
            if (generation != _generation) return;
        }

        var options = CurrentOptions!;
        var wantsTarget = !_resolvedTarget.IsNone;
        var geometry = await ActivateInBrowserAsync(step, options, stepToken).ConfigureAwait(false);

        if (generation != _generation) return;

        if (wantsTarget && geometry is { Found: false })
        {
            await PublishAsync(OnboardingEventKind.TargetMissing, step).ConfigureAwait(false);

            if (await HandleMissingTargetAsync(options.MissingTarget, step, direction, stepToken).ConfigureAwait(false))
            {
                return;
            }
        }
        else if (geometry is not null)
        {
            ApplyGeometry(geometry);
            if (geometry.Found) await PublishAsync(OnboardingEventKind.TargetResolved, step).ConfigureAwait(false);
        }

        Status = TourStatus.Running;
        _stepShownAt = DateTimeOffset.UtcNow;

        Notify(TourSessionChange.Step);

        await PersistAsync(stepToken).ConfigureAwait(false);
        await PublishAsync(OnboardingEventKind.StepShown, step).ConfigureAwait(false);

        if (Tour.OnStepChanged is not null)
        {
            await Tour.OnStepChanged(new StepChangedEventArgs(this, step, index, previousStep, reason)).ConfigureAwait(false);
        }

        if (step.OnShown is not null)
        {
            await step.OnShown(CreateContext(step, stepToken)).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Returns false when the step cannot proceed yet because the route does not match. The
    /// transition then resumes from <see cref="HandleLocationChangedAsync"/>.
    /// </summary>
    private bool EnsureRoute(StepDefinition step)
    {
        _pendingRoute = null;

        if (step.Route is null || _navigation is null) return true;

        var current = _navigation.ToBaseRelativePath(_navigation.Uri);
        if (RouteMatcher.Matches(step.Route, current)) return true;

        _pendingRoute = step.Route;
        Status = TourStatus.Waiting;
        Notify(TourSessionChange.Step);

        // A wildcard route is a constraint rather than a destination, so there is nowhere to go.
        if (_globalOptions.AutoNavigateToStepRoute && !step.Route.EndsWith('*'))
        {
            _navigation.NavigateTo(step.Route);
        }

        return false;
    }

    private async Task<StepGeometry?> ActivateInBrowserAsync(
        StepDefinition step, EffectiveStepOptions options, CancellationToken cancellationToken)
    {
        if (!_interop.IsAvailable) return null;

        var activation = new StepActivation
        {
            SessionId = Id,
            Target = TargetDescriptor.From(_resolvedTarget),
            AdditionalTargets = BuildAdditionalDescriptors(step),
            WaitForTarget = options.MissingTarget == MissingTargetBehavior.Wait,
            WaitTimeoutMs = (int)options.WaitTimeout.TotalMilliseconds,
            Scroll = options.Scroll switch
            {
                ScrollMode.None => "none",
                ScrollMode.Instant => "instant",
                ScrollMode.Auto => "auto",
                _ => "smooth",
            },
            ScrollPadding = options.ScrollPadding,
            Interaction = options.Interaction switch
            {
                InteractionMode.Free => "free",
                InteractionMode.TargetOnly => "target",
                _ => "blocked",
            },
            CloseOnEscape = options.CloseOnEscape,
            KeyboardNavigation = options.KeyboardNavigation,
            AdvanceOn = step.AdvanceOn is { } trigger
                ? new AdvanceTriggerDescriptor
                {
                    EventName = trigger.EventName,
                    Selector = trigger.Selector,
                    MatchSelector = trigger.MatchSelector,
                    DelayMs = trigger.DelayMs,
                }
                : null,
        };

        try
        {
            return await _interop.ActivateStepAsync(activation, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            // Circuit disconnects and prerender teardown must never take the tour down with them.
            _logger.LogDebug(ex, "Onboarding step activation could not reach the browser.");
            return null;
        }
    }

    private IReadOnlyList<TargetDescriptor>? BuildAdditionalDescriptors(StepDefinition step)
    {
        if (step.AdditionalTargets is not { Count: > 0 } extra) return null;

        var list = new List<TargetDescriptor>(extra.Count);
        foreach (var target in extra)
        {
            // Dynamic extra targets are not supported; they would need a second resolution pass
            // for a case that has not come up in practice.
            if (target.IsDynamic) continue;

            var descriptor = TargetDescriptor.From(target);
            if (descriptor.Kind != TargetKind.None) list.Add(descriptor);
        }

        return list.Count == 0 ? null : list;
    }

    /// <summary>Returns true when the step was handled elsewhere and this transition must stop.</summary>
    private async Task<bool> HandleMissingTargetAsync(
        MissingTargetBehavior behavior, StepDefinition step, int direction, CancellationToken cancellationToken)
    {
        switch (behavior)
        {
            case MissingTargetBehavior.Skip:
                await PublishAsync(OnboardingEventKind.StepSkipped, step).ConfigureAwait(false);

                await MoveToAsync(
                    null,
                    direction > 0 ? StepChangeReason.Next : StepChangeReason.Previous,
                    direction,
                    cancellationToken).ConfigureAwait(false);
                return true;

            case MissingTargetBehavior.Fail:
                throw new OnboardingException($"Target for step '{step.Id}' of tour '{Tour.Id}' was not found.")
                {
                    TourId = Tour.Id,
                    StepId = step.Id,
                };

            case MissingTargetBehavior.Wait:
                // The browser layer is watching for the element; show the waiting state until it
                // reports back or the wait times out.
                Status = TourStatus.Waiting;
                Notify(TourSessionChange.Step);
                return true;

            default:
                // Center: drop the target and fall through to render a modal card.
                _resolvedTarget = StepTarget.None;
                return false;
        }
    }

    // ---- Step eligibility --------------------------------------------------

    /// <summary>
    /// Re-evaluates every step condition. Conditions are expected to be cheap and free of side
    /// effects, because progress display and lookahead both depend on asking them repeatedly.
    /// </summary>
    private async Task RebuildPlanAsync()
    {
        var plan = new List<int>(Tour.Steps.Count);

        for (var i = 0; i < Tour.Steps.Count; i++)
        {
            var step = Tour.Steps[i];
            if (step.When is null || await step.When(CreateContext(step, index: i)).ConfigureAwait(false))
            {
                plan.Add(i);
            }
        }

        _plan = plan;
    }

    /// <summary>Snaps a requested index onto the nearest planned step in the direction of travel.</summary>
    private int ResolveEligible(int requestedIndex, int direction)
    {
        if (_plan.Count == 0) return -1;
        if (_plan.Contains(requestedIndex)) return requestedIndex;

        if (direction >= 0)
        {
            foreach (var candidate in _plan)
            {
                if (candidate >= requestedIndex) return candidate;
            }

            return -1;
        }

        for (var i = _plan.Count - 1; i >= 0; i--)
        {
            if (_plan[i] <= requestedIndex) return _plan[i];
        }

        return -1;
    }

    private int NextPlanned(int fromIndex)
    {
        foreach (var index in _plan)
        {
            if (index > fromIndex) return index;
        }

        return -1;
    }

    private int PreviousPlanned(int fromIndex)
    {
        for (var i = _plan.Count - 1; i >= 0; i--)
        {
            if (_plan[i] < fromIndex) return _plan[i];
        }

        return -1;
    }

    private void PopHistory()
    {
        if (_history.Count > 0) _history.RemoveAt(_history.Count - 1);
    }

    // ---- Ending ------------------------------------------------------------

    private async Task EndAsync(TourEndReason reason, CancellationToken cancellationToken)
    {
        if (IsFinished) return;

        var lastStep = CurrentStep;
        var lastIndex = CurrentIndex;

        if (lastStep is not null)
        {
            await PublishAsync(OnboardingEventKind.StepLeft, lastStep, duration: DateTimeOffset.UtcNow - _stepShownAt)
                .ConfigureAwait(false);
        }

        Status = reason switch
        {
            TourEndReason.Completed => TourStatus.Completed,
            TourEndReason.Skipped => TourStatus.Skipped,
            TourEndReason.Failed => TourStatus.Failed,
            TourEndReason.Cancelled => TourStatus.Cancelled,
            _ => TourStatus.Dismissed,
        };

        EndReason = reason;
        _pendingRoute = null;
        CurrentStep = null;
        Placement = null;
        _geometry = null;

        _generation++;
        CancelStep();
        await SafeDeactivateAsync().ConfigureAwait(false);
        await RestoreFocusAsync().ConfigureAwait(false);
        UnhookNavigation();

        await PersistEndAsync(reason, lastStep, lastIndex).ConfigureAwait(false);

        await PublishAsync(
            reason switch
            {
                TourEndReason.Completed => OnboardingEventKind.TourCompleted,
                TourEndReason.Skipped => OnboardingEventKind.TourSkipped,
                TourEndReason.Failed => OnboardingEventKind.TourFailed,
                TourEndReason.Cancelled => OnboardingEventKind.TourCancelled,
                _ => OnboardingEventKind.TourDismissed,
            },
            lastStep,
            reason: reason).ConfigureAwait(false);

        if (Tour.OnEnded is not null)
        {
            await Tour.OnEnded(new TourEndedEventArgs(this, reason, lastStep, lastIndex, Error)).ConfigureAwait(false);
        }

        if (_attached)
        {
            _attached = false;
            await SafeDetachAsync().ConfigureAwait(false);
        }

        Notify(TourSessionChange.Step | TourSessionChange.Ended);
    }

    // ---- Interop callbacks -------------------------------------------------

    public ValueTask OnGeometryChangedAsync(StepGeometry geometry)
    {
        if (_disposed || CurrentStep is null) return ValueTask.CompletedTask;

        // Frames can arrive out of order after a resize storm; keep only the newest.
        if (geometry.Sequence <= _lastSequence) return ValueTask.CompletedTask;

        var wasWaitingForTarget = Status == TourStatus.Waiting && _pendingRoute is null;

        ApplyGeometry(geometry);

        if (wasWaitingForTarget && geometry.Found)
        {
            Status = TourStatus.Running;
            _stepShownAt = DateTimeOffset.UtcNow;
            Notify(TourSessionChange.Step);
            return PublishAsync(OnboardingEventKind.TargetResolved, CurrentStep);
        }

        Notify(TourSessionChange.Geometry);
        return ValueTask.CompletedTask;
    }

    public async ValueTask OnTargetLostAsync()
    {
        if (_disposed || CurrentStep is null || Status != TourStatus.Running) return;

        var behavior = CurrentOptions!.MissingTarget;
        await PublishAsync(OnboardingEventKind.TargetMissing, CurrentStep).ConfigureAwait(false);

        switch (behavior)
        {
            case MissingTargetBehavior.Skip:
                await NextAsync().ConfigureAwait(false);
                break;

            case MissingTargetBehavior.Fail:
                await RunGuardedAsync(
                    _ => throw new OnboardingException($"Target for step '{CurrentStep.Id}' disappeared.")
                    {
                        TourId = Tour.Id,
                        StepId = CurrentStep.Id,
                    },
                    CancellationToken.None).ConfigureAwait(false);
                break;

            case MissingTargetBehavior.Center:
                _geometry = null;
                Placement = null;
                Notify(TourSessionChange.Step);
                break;

            default:
                Status = TourStatus.Waiting;
                _geometry = null;
                Placement = null;
                Notify(TourSessionChange.Step);
                break;
        }
    }

    public async ValueTask OnKeyAsync(string key, bool shiftKey)
    {
        if (_disposed || CurrentOptions is null || !IsActive) return;

        var options = CurrentOptions;
        var rtl = options.RightToLeft;

        switch (key)
        {
            case "Escape" when options.CloseOnEscape:
                await DismissAsync().ConfigureAwait(false);
                break;
            case "ArrowRight" when options.KeyboardNavigation:
                await (rtl ? PreviousAsync() : NextAsync()).ConfigureAwait(false);
                break;
            case "ArrowLeft" when options.KeyboardNavigation:
                await (rtl ? NextAsync() : PreviousAsync()).ConfigureAwait(false);
                break;
            case "Home" when options.KeyboardNavigation && _plan.Count > 0:
                await GoToAsync(_plan[0]).ConfigureAwait(false);
                break;
            case "End" when options.KeyboardNavigation && _plan.Count > 0:
                await GoToAsync(_plan[^1]).ConfigureAwait(false);
                break;
        }
    }

    public async ValueTask OnOverlayClickAsync()
    {
        if (_disposed || CurrentOptions is null || !IsActive) return;
        if (!CurrentOptions.CloseOnOverlayClick) return;

        await DismissAsync().ConfigureAwait(false);
    }

    public async ValueTask OnAdvanceTriggeredAsync()
    {
        if (_disposed || Status != TourStatus.Running) return;
        await NextAsync().ConfigureAwait(false);
    }

    public async ValueTask OnWaitTimedOutAsync()
    {
        if (_disposed || CurrentStep is null || Status != TourStatus.Waiting) return;

        var step = CurrentStep;
        var behavior = CurrentOptions!.WaitTimeoutBehavior;

        // Waiting again after a wait expired would spin forever.
        if (behavior == MissingTargetBehavior.Wait) behavior = MissingTargetBehavior.Skip;

        await RunGuardedAsync(async ct =>
        {
            if (!ReferenceEquals(CurrentStep, step)) return;

            if (behavior == MissingTargetBehavior.Center)
            {
                _resolvedTarget = StepTarget.None;
                Status = TourStatus.Running;
                Notify(TourSessionChange.Step);
                return;
            }

            await HandleMissingTargetAsync(behavior, step, +1, ct).ConfigureAwait(false);
        }, CancellationToken.None).ConfigureAwait(false);
    }

    // ---- Geometry and placement -------------------------------------------

    private void ApplyGeometry(StepGeometry geometry)
    {
        _geometry = geometry;
        _lastSequence = geometry.Sequence;

        var options = CurrentOptions;
        if (options is null) return;

        if (geometry.Popover.IsEmpty)
        {
            // The popover has not rendered yet, so there is nothing to place. The resize observer
            // calls back as soon as it has a size.
            Placement = null;
            return;
        }

        var hasTarget = geometry.Found && !geometry.Target.IsEmpty;
        var padded = hasTarget ? geometry.Target.Inflate(options.SpotlightPadding) : Rect.Empty;
        var viewport = geometry.Viewport.IsEmpty
            ? new Rect(0, 0, _environment.Viewport.Width, _environment.Viewport.Height)
            : geometry.Viewport;

        Placement = PlacementEngine.Place(new PlacementRequest
        {
            Target = padded,
            Popover = geometry.Popover,
            Viewport = viewport,
            Preferred = options.Placement,
            Fallbacks = options.PlacementFallbacks,
            Offset = options.Offset,
            ViewportPadding = options.ViewportPadding,
            ArrowSize = options.ArrowSize,
            CornerRadius = 12,
            RightToLeft = options.RightToLeft,
            HasTarget = hasTarget,
        });
    }

    /// <summary>Called by the host once the popover element exists, so it can be measured.</summary>
    public async ValueTask SetPopoverElementAsync(ElementReference? element)
    {
        if (_disposed || !_interop.IsAvailable) return;

        try
        {
            await _interop.SetPopoverAsync(Id, element, CurrentOptions?.TrapFocus ?? true).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not register the popover element.");
        }
    }

    /// <summary>Called by the host after the popover renders, to move focus into it.</summary>
    public async ValueTask FocusPopoverAsync()
    {
        if (_disposed || !_interop.IsAvailable || CurrentOptions is not { AutoFocus: true }) return;

        try
        {
            if (!_focusCaptured)
            {
                await _interop.CaptureFocusAsync(Id).ConfigureAwait(false);
                _focusCaptured = true;
            }

            await _interop.FocusPopoverAsync(Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not move focus into the popover.");
        }
    }

    // ---- Routing -----------------------------------------------------------

    private void HookNavigation()
    {
        if (_navigation is null || _navigationHooked) return;

        _navigation.LocationChanged += OnLocationChanged;
        _navigationHooked = true;
    }

    private void UnhookNavigation()
    {
        if (_navigation is null || !_navigationHooked) return;

        _navigation.LocationChanged -= OnLocationChanged;
        _navigationHooked = false;
    }

    private void OnLocationChanged(object? sender, LocationChangedEventArgs e)
    {
        if (_disposed || _navigation is null) return;

        _ = HandleLocationChangedAsync(_navigation.ToBaseRelativePath(e.Location));
    }

    private async Task HandleLocationChangedAsync(string relative)
    {
        try
        {
            if (_pendingRoute is { } pending)
            {
                if (!RouteMatcher.Matches(pending, relative)) return;

                _pendingRoute = null;

                // Re-enter the same step now the page is right. Retarget skips the leave hooks so
                // the step is not counted as visited twice.
                await RunGuardedAsync(
                    ct => MoveToAsync(CurrentIndex, StepChangeReason.Retarget, +1, ct),
                    CancellationToken.None).ConfigureAwait(false);
                return;
            }

            if (CurrentStep?.Route is { } route && !RouteMatcher.Matches(route, relative))
            {
                // The user navigated away from the step's page. Hold rather than yanking them back:
                // fighting the user for control of the address bar is never the right call.
                _pendingRoute = route;
                Status = TourStatus.Waiting;
                await SafeDeactivateAsync().ConfigureAwait(false);
                Notify(TourSessionChange.Step);
                return;
            }

            // Same page, new content: the target may have been re-rendered underneath us.
            if (Status == TourStatus.Running && CurrentStep is not null)
            {
                await RefreshAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Onboarding failed to handle a location change.");
        }
    }

    // ---- Persistence -------------------------------------------------------

    private bool ShouldPersist => Tour.Persist ?? _globalOptions.Persist;

    private async Task PersistAsync(CancellationToken cancellationToken)
    {
        if (!ShouldPersist || CurrentStep is null) return;

        try
        {
            await _store.SetAsync(
                new OnboardingRecord
                {
                    TourId = Tour.Id,
                    Version = Tour.Version,
                    CurrentStepId = CurrentStep.Id,
                    CurrentIndex = CurrentIndex,
                    VisitedStepIds = _history.ToArray(),
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding progress could not be persisted.");
        }
    }

    private async Task PersistEndAsync(TourEndReason reason, StepDefinition? lastStep, int lastIndex)
    {
        if (!ShouldPersist) return;

        // A cancelled tour was interrupted rather than refused, so the record must stay open for
        // it to resume from. The position was already written when the step was shown.
        if (reason == TourEndReason.Cancelled) return;

        try
        {
            await _store.SetAsync(
                new OnboardingRecord
                {
                    TourId = Tour.Id,
                    Version = Tour.Version,
                    CurrentStepId = lastStep?.Id,
                    CurrentIndex = lastIndex,
                    Completed = reason == TourEndReason.Completed,
                    Dismissed = reason is TourEndReason.Dismissed or TourEndReason.Skipped,
                    CompletedAt = reason == TourEndReason.Completed ? DateTimeOffset.UtcNow : null,
                    VisitedStepIds = _history.ToArray(),
                },
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding completion could not be persisted.");
        }
    }

    private async Task<OnboardingRecord?> SafeGetRecordAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _store.GetAsync(Tour.Id, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding progress could not be read.");
            return null;
        }
    }

    // ---- Plumbing ----------------------------------------------------------

    private EffectiveStepOptions ResolveOptions(StepDefinition? step)
        => OptionResolver.Resolve(
            step,
            Tour,
            _globalOptions,
            _localizer.GetLabels(Tour),
            _environment.ReducedMotion,
            _environment.RightToLeft ? OnboardingDirection.RightToLeft : OnboardingDirection.LeftToRight);

    private StepContext CreateContext(StepDefinition step, CancellationToken cancellationToken = default, int? index = null)
        => new(this, step, index ?? Tour.Steps.IndexOf(step), _services, cancellationToken);

    /// <summary>
    /// Runs a command with the transition lock held, turning an unhandled fault into a failed tour
    /// rather than an unobserved exception.
    /// </summary>
    private async Task RunGuardedAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        if (_disposed) return;

        // Re-entrant call from inside a hook: the lock is already ours, so just run.
        if (_inTransition.Value)
        {
            await RunAsync(action, cancellationToken).ConfigureAwait(false);
            return;
        }

        try
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or OperationCanceledException)
        {
            return;
        }

        _inTransition.Value = true;

        try
        {
            await RunAsync(action, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _inTransition.Value = false;
            try { _gate.Release(); } catch (ObjectDisposedException) { }
        }
    }

    /// <summary>Runs a command body, turning an unhandled fault into a failed tour.</summary>
    private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken cancellationToken)
    {
        try
        {
            await action(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // A cancelled transition is the normal outcome of ending a tour mid-flight.
        }
        catch (Exception ex)
        {
            Error = ex;
            _logger.LogError(ex, "Onboarding tour {TourId} failed during a transition.", Tour.Id);

            try
            {
                await EndAsync(TourEndReason.Failed, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception endFailure)
            {
                _logger.LogError(endFailure, "Onboarding tour {TourId} failed while shutting down.", Tour.Id);
            }
        }
    }

    private async ValueTask PublishAsync(
        OnboardingEventKind kind,
        StepDefinition? step = null,
        StepAction? action = null,
        TourEndReason? reason = null,
        TimeSpan? duration = null)
    {
        var target = step ?? CurrentStep;

        await _publish(new OnboardingEvent
        {
            Kind = kind,
            Session = this,
            Step = target,
            StepIndex = target is null ? -1 : Tour.Steps.IndexOf(target),
            Action = action,
            Reason = reason,
            Error = kind == OnboardingEventKind.TourFailed ? Error : null,
            Duration = duration,
        }).ConfigureAwait(false);
    }

    private void Notify(TourSessionChange change)
        => Changed?.Invoke(this, new TourSessionChangedEventArgs(change));

    private void CancelStep()
    {
        var cts = _stepCts;
        _stepCts = null;

        if (cts is null) return;

        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
    }

    private async Task<BrowserEnvironment> SafeInitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _interop.InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not sample the browser environment.");
            return BrowserEnvironment.Unknown;
        }
    }

    private async Task SafeAttachAsync(CancellationToken cancellationToken)
    {
        if (!_interop.IsAvailable) return;

        try
        {
            await _interop.AttachAsync(Id, this, cancellationToken).ConfigureAwait(false);
            _attached = true;
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not attach the session to the browser.");
        }
    }

    private async Task<StepGeometry?> SafeMeasureAsync()
    {
        if (!_interop.IsAvailable) return null;

        try
        {
            return await _interop.MeasureAsync(Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not measure the current step.");
            return null;
        }
    }

    private async Task SafeDeactivateAsync()
    {
        if (!_interop.IsAvailable || !_attached) return;

        try
        {
            await _interop.DeactivateStepAsync(Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not deactivate the current step.");
        }
    }

    private async Task SafeDetachAsync()
    {
        if (!_interop.IsAvailable) return;

        try
        {
            await _interop.DetachAsync(Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not detach the session.");
        }
    }

    private async Task RestoreFocusAsync()
    {
        if (!_focusCaptured || !_interop.IsAvailable) return;
        _focusCaptured = false;

        if (CurrentOptions is { RestoreFocus: false }) return;

        try
        {
            await _interop.RestoreFocusAsync(Id).ConfigureAwait(false);
        }
        catch (Exception ex) when (IsTransientInteropFailure(ex))
        {
            _logger.LogDebug(ex, "Onboarding could not restore focus.");
        }
    }

    /// <summary>
    /// Interop failures during prerender, circuit teardown and page unload are expected rather than
    /// bugs, and must degrade to "no browser available" instead of failing the tour.
    /// </summary>
    internal static bool IsTransientInteropFailure(Exception ex)
        => ex is Microsoft.JSInterop.JSDisconnectedException
            or Microsoft.JSInterop.JSException
            or ObjectDisposedException
            or OperationCanceledException
            // Blazor throws this from JS interop during prerendering.
            or InvalidOperationException;

    public async ValueTask DisposeAsync()
    {
        var wasAttached = ReleaseLocalResources();

        if (wasAttached)
        {
            await SafeDetachAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Synchronous teardown, for containers that dispose their scopes without awaiting. Everything
    /// local is released; the browser-side cleanup is skipped because it cannot be awaited, which
    /// is harmless since a scope going away synchronously means the page is going away too.
    /// </summary>
    public void Dispose() => ReleaseLocalResources();

    private bool ReleaseLocalResources()
    {
        if (_disposed) return false;
        _disposed = true;

        UnhookNavigation();
        CancelStep();

        var wasAttached = _attached;
        _attached = false;

        _gate.Dispose();
        Changed = null;

        return wasAttached;
    }
}
