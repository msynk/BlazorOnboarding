using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>When a declarative tour should start itself.</summary>
public enum OnboardingAutoStart
{
    /// <summary>Never; call <see cref="OnboardingTour.StartAsync"/> yourself.</summary>
    None,

    /// <summary>Once per user, skipped when the tour is already completed or dismissed.</summary>
    Once,

    /// <summary>On every render of the component, resuming persisted progress.</summary>
    Always,
}

/// <summary>
/// Declares a tour in markup. Child <see cref="OnboardingStep"/> components become its steps, in
/// the order they are rendered.
/// </summary>
public sealed partial class OnboardingTour : ComponentBase, IAsyncDisposable
{
    private readonly List<OnboardingStep> _steps = [];

    private TourDefinition? _definition;
    private bool _autoStarted;
    private bool _disposed;

    [Inject] private IOnboardingService Onboarding { get; set; } = default!;

    /// <summary>Stable identifier, also used as the persistence key.</summary>
    [Parameter, EditorRequired] public required string Id { get; set; }

    /// <summary>Human-readable name, used as the dialog label when a step has no title.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Bump this after editing the steps to discard stale saved progress.</summary>
    [Parameter] public int Version { get; set; } = 1;

    /// <summary>Whether the tour starts itself once its markup is on screen.</summary>
    [Parameter] public OnboardingAutoStart AutoStart { get; set; } = OnboardingAutoStart.None;

    /// <summary>The steps.</summary>
    [Parameter] public RenderFragment? ChildContent { get; set; }

    // ---- Common option overrides, applied to every step ---------------------

    [Parameter] public Placement? Placement { get; set; }
    [Parameter] public OnboardingTheme? Theme { get; set; }
    [Parameter] public OnboardingDirection? Direction { get; set; }
    [Parameter] public ProgressStyle? Progress { get; set; }
    [Parameter] public InteractionMode? Interaction { get; set; }
    [Parameter] public bool? ShowOverlay { get; set; }
    [Parameter] public bool? ShowSpotlight { get; set; }
    [Parameter] public bool? ShowSkip { get; set; }
    [Parameter] public bool? ShowClose { get; set; }
    [Parameter] public bool? ShowPrevious { get; set; }
    [Parameter] public bool? CloseOnEscape { get; set; }
    [Parameter] public bool? CloseOnOverlayClick { get; set; }
    [Parameter] public bool? Persist { get; set; }
    [Parameter] public OnboardingLabels? Labels { get; set; }

    /// <summary>Replaces the popover contents for every step that has no template of its own.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? Template { get; set; }

    /// <summary>
    /// Escape hatch for anything not exposed as a parameter. Runs after the parameters are applied
    /// and before the tour is registered.
    /// </summary>
    [Parameter] public Action<TourDefinition>? Configure { get; set; }

    // ---- Lifecycle callbacks ------------------------------------------------

    [Parameter] public EventCallback OnStarted { get; set; }
    [Parameter] public EventCallback<StepChangedEventArgs> OnStepChanged { get; set; }
    [Parameter] public EventCallback<TourEndedEventArgs> OnEnded { get; set; }

    /// <summary>The definition assembled from the markup. Safe to inspect and to pass around.</summary>
    public TourDefinition Definition => _definition ??= new TourDefinition { Id = Id };

    /// <summary>The running session for this tour, if there is one.</summary>
    public ITourSession? Session => Onboarding.FindSession(Id);

    /// <summary>True while this tour is on screen.</summary>
    public bool IsRunning => Session is { IsActive: true };

    protected override void OnParametersSet()
    {
        Rebuild();
        Onboarding.Register(Definition);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Children have registered by the time the first render completes, and OnAfterRender never
        // runs while prerendering, so this is the earliest safe point to start.
        if (!firstRender || _disposed || AutoStart == OnboardingAutoStart.None || _autoStarted) return;

        _autoStarted = true;

        if (Definition.Steps.Count == 0) return;

        if (AutoStart == OnboardingAutoStart.Once)
        {
            await Onboarding.StartOnceAsync(Definition).ConfigureAwait(false);
        }
        else
        {
            await Onboarding.StartAsync(Definition).ConfigureAwait(false);
        }
    }

    // ---- Step registration --------------------------------------------------

    internal void AttachStep(OnboardingStep step)
    {
        if (_steps.Contains(step)) return;

        _steps.Add(step);
        Rebuild();
    }

    internal void DetachStep(OnboardingStep step)
    {
        if (!_steps.Remove(step)) return;
        Rebuild();
    }

    internal void NotifyStepChanged() => Rebuild();

    /// <summary>
    /// Re-projects the child components into the definition. The definition object itself is kept,
    /// so a tour that is already running picks up edits without being restarted.
    /// </summary>
    private void Rebuild()
    {
        var definition = Definition;

        definition.Id = Id;
        definition.Title = Title;
        definition.Version = Version;
        definition.Persist = Persist;
        definition.Labels = Labels;
        definition.Template = Template;

        definition.Placement = Placement;
        definition.Theme = Theme;
        definition.Direction = Direction;
        definition.Progress = Progress;
        definition.Interaction = Interaction;
        definition.ShowOverlay = ShowOverlay;
        definition.ShowSpotlight = ShowSpotlight;
        definition.ShowSkip = ShowSkip;
        definition.ShowClose = ShowClose;
        definition.ShowPrevious = ShowPrevious;
        definition.CloseOnEscape = CloseOnEscape;
        definition.CloseOnOverlayClick = CloseOnOverlayClick;

        definition.OnStarted = OnStarted.HasDelegate
            ? _ => new ValueTask(OnStarted.InvokeAsync())
            : null;

        definition.OnStepChanged = OnStepChanged.HasDelegate
            ? args => new ValueTask(OnStepChanged.InvokeAsync(args))
            : null;

        definition.OnEnded = OnEnded.HasDelegate
            ? args => new ValueTask(OnEnded.InvokeAsync(args))
            : null;

        definition.Steps.Clear();
        foreach (var step in _steps.OrderBy(step => step.SortKey))
        {
            definition.Steps.Add(step.Definition);
        }

        Configure?.Invoke(definition);
    }

    // ---- Imperative API -----------------------------------------------------

    /// <summary>Starts the tour.</summary>
    public Task<ITourSession> StartAsync(StartOptions? options = null, CancellationToken cancellationToken = default)
        => Onboarding.StartAsync(Definition, options, cancellationToken);

    /// <summary>Starts the tour unless the user has already completed or dismissed it.</summary>
    public Task<ITourSession?> StartOnceAsync(CancellationToken cancellationToken = default)
        => Onboarding.StartOnceAsync(Definition, cancellationToken: cancellationToken);

    /// <summary>Clears saved progress and starts again from the first step.</summary>
    public Task<ITourSession> RestartAsync(CancellationToken cancellationToken = default)
        => Onboarding.RestartAsync(Definition, cancellationToken);

    /// <summary>Ends the tour if it is running.</summary>
    public Task StopAsync(TourEndReason reason = TourEndReason.Dismissed, CancellationToken cancellationToken = default)
    {
        if (Session is not { IsActive: true } session) return Task.CompletedTask;

        return reason switch
        {
            TourEndReason.Completed => session.CompleteAsync(cancellationToken),
            TourEndReason.Skipped => session.SkipAsync(cancellationToken),
            _ => session.DismissAsync(cancellationToken),
        };
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        // Leave a running session alone: the tour may legitimately outlive the markup that
        // declared it, for instance while navigating between the pages of a multi-page tour.
        if (Session is not { IsActive: true })
        {
            Onboarding.Unregister(Id);
        }

        return ValueTask.CompletedTask;
    }
}
