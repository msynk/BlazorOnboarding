using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>
/// Renders the active tour. Place one of these once, typically in the main layout, and it hosts
/// every tour the application starts.
/// </summary>
/// <remarks>
/// The component owns the popover box itself even when the visuals are supplied by a template.
/// That is deliberate: measurement, placement, focus management and dialog semantics all depend on
/// that element existing and being known to the engine, and reimplementing them correctly in every
/// application is not a reasonable ask.
/// </remarks>
public sealed partial class OnboardingRoot : ComponentBase, IAsyncDisposable
{
    private ElementReference? _popoverElement;
    private ElementReference? _registeredElement;

    private TourSession? _session;
    private StepDefinition? _step;
    private EffectiveStepOptions? _options;
    private StepRenderContext? _renderContext;

    private string _titleId = string.Empty;
    private string _descriptionId = string.Empty;
    private string? _announcement;
    private string? _announcedStepId;
    private string? _focusedStepId;
    private string? _claimedSessionId;
    private bool _disposed;

    [Inject] private IOnboardingService Onboarding { get; set; } = default!;

    [Inject] private IOnboardingLocalizer Localizer { get; set; } = default!;

    [Inject] private OnboardingHostRegistry Hosts { get; set; } = default!;

    /// <summary>
    /// Render this session instead of whichever one is active. Doing so also claims it, so the
    /// application's main host stops rendering it and the two cannot draw the same tour twice.
    /// </summary>
    [Parameter] public ITourSession? Session { get; set; }

    /// <summary>Extra classes on the root element, for application-level theming.</summary>
    [Parameter] public string? Class { get; set; }

    /// <summary>
    /// Forces a colour scheme for everything this host renders, overriding the tour and global
    /// settings. Bind it to your own theme switch so tours follow the rest of the application.
    /// </summary>
    [Parameter] public OnboardingTheme? Theme { get; set; }

    /// <summary>Extra classes on the popover element.</summary>
    [Parameter] public string? PopoverClass { get; set; }

    /// <summary>
    /// Drops the built-in popover classes so the popover carries only your own. The element, its
    /// position and its dialog semantics are still managed for you.
    /// </summary>
    [Parameter] public bool Unstyled { get; set; }

    /// <summary>Replaces the contents of the popover for every step.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? ChildContent { get; set; }

    /// <summary>Replaces the overlay and spotlight visuals.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? OverlayTemplate { get; set; }

    /// <summary>Replaces the placeholder shown while the engine waits for a target or a route.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? WaitingTemplate { get; set; }

    protected override void OnInitialized()
    {
        Onboarding.SessionsChanged += OnSessionsChanged;
        Hosts.Changed += OnSessionsChanged;
        Bind(ResolveSession());
    }

    protected override void OnParametersSet()
    {
        UpdateClaim();
        Bind(ResolveSession());
    }

    /// <summary>Claims the explicitly supplied session, releasing any previously claimed one.</summary>
    private void UpdateClaim()
    {
        var wanted = Session?.Id;
        if (wanted == _claimedSessionId) return;

        if (_claimedSessionId is not null) Hosts.Release(_claimedSessionId);

        _claimedSessionId = wanted;

        if (_claimedSessionId is not null) Hosts.Claim(_claimedSessionId);
    }

    private TourSession? ResolveSession()
    {
        if (Session is not null) return Session as TourSession;

        // An ambient host leaves claimed sessions to the host that claimed them.
        var active = Onboarding.Active;
        return active is not null && !Hosts.IsClaimed(active.Id) ? active as TourSession : null;
    }

    private void Bind(TourSession? session)
    {
        if (ReferenceEquals(session, _session))
        {
            Snapshot();
            return;
        }

        if (_session is not null) _session.Changed -= OnSessionChanged;

        _session = session;

        if (_session is not null) _session.Changed += OnSessionChanged;

        // A new session means the previous popover element is gone as far as the engine knows.
        _registeredElement = null;
        _focusedStepId = null;
        Snapshot();
    }

    /// <summary>
    /// Copies the pieces of session state the render tree depends on. Reading them once per render
    /// keeps the markup free of null checks and means a mid-render change cannot tear.
    /// </summary>
    private void Snapshot()
    {
        var session = _session;

        if (session is null || !session.IsActive || session.CurrentStep is null || session.CurrentOptions is null)
        {
            _step = null;
            _options = null;
            _renderContext = null;
            return;
        }

        _step = session.CurrentStep;
        _options = session.CurrentOptions;
        _titleId = $"bo-title-{session.Id}-{_step.Id}";
        _descriptionId = $"bo-desc-{session.Id}-{_step.Id}";

        _renderContext = new StepRenderContext
        {
            Session = session,
            Step = _step,
            Options = _options,
            Title = Localizer.Localize(_step.Title),
            Description = Localizer.Localize(_step.Description),
        };

        if (_announcedStepId != _step.Id && !session.IsWaiting)
        {
            _announcedStepId = _step.Id;
            _announcement = string.Format(
                _options.Labels.StepAnnouncementFormat,
                session.DisplayPosition,
                session.DisplayCount,
                _renderContext.Title ?? _renderContext.Description ?? string.Empty).Trim();
        }
        else if (session.IsWaiting && _announcement != _options.Labels.WaitingAnnouncement)
        {
            _announcement = _options.Labels.WaitingAnnouncement;
            _announcedStepId = null;
        }
    }

    private void OnSessionsChanged(object? sender, EventArgs e)
    {
        if (_disposed) return;

        _ = InvokeAsync(() =>
        {
            Bind(ResolveSession());
            StateHasChanged();
        });
    }

    private void OnSessionChanged(object? sender, TourSessionChangedEventArgs e)
    {
        if (_disposed) return;

        _ = InvokeAsync(() =>
        {
            Snapshot();
            StateHasChanged();
        });
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (_disposed || _session is null) return;

        // Register the popover as soon as it exists so the engine can measure it. The element is
        // stable across steps, so this normally happens once per tour.
        if (_popoverElement is { } element && _registeredElement?.Id != element.Id)
        {
            _registeredElement = element;
            await _session.SetPopoverElementAsync(element).ConfigureAwait(false);
        }
        else if (_popoverElement is null && _registeredElement is not null)
        {
            _registeredElement = null;
            await _session.SetPopoverElementAsync(null).ConfigureAwait(false);
        }

        // Move focus once per step, after the content for that step has actually rendered.
        if (_step is not null && !_session.IsWaiting && _focusedStepId != _step.Id)
        {
            _focusedStepId = _step.Id;
            await _session.FocusPopoverAsync().ConfigureAwait(false);
        }
    }

    private Task HandleOverlayClickAsync()
        => _session is null ? Task.CompletedTask : _session.OnOverlayClickAsync().AsTask();

    // ---- Computed presentation --------------------------------------------

    private bool IsPlaced => _session?.Placement is not null;

    private PlacementResult? Placement => _session?.Placement;

    private bool ShowSpotlight
        => _options is { ShowSpotlight: true, SpotlightShape: not SpotlightShape.None } && SpotlightRect is not null;

    /// <summary>The padded target rectangle the spotlight and the blockers share.</summary>
    private Rect? SpotlightRect
    {
        get
        {
            if (_options is null || _session?.TargetRect is not { } target) return null;
            return target.Inflate(_options.SpotlightPadding);
        }
    }

    private bool ShowArrow => Placement is { ShowArrow: true } && _options is { ArrowSize: > 0 };

    private string SideAttribute => Placement?.SideName ?? "center";

    private string ThemeAttribute => (Theme ?? _options?.Theme) switch
    {
        OnboardingTheme.Dark => "dark",
        OnboardingTheme.Light => "light",
        _ => "system",
    };

    private string DirectionAttribute => _options?.Direction == OnboardingDirection.RightToLeft ? "rtl" : "ltr";

    private string RootStyle
    {
        get
        {
            if (_options is null) return string.Empty;

            var opacity = _options.ShowOverlay ? _options.OverlayOpacity : 0;

            return string.Concat(
                "--bo-z:", Css.Int(_options.ZIndex),
                ";--bo-duration:", Css.Ms(_options.AnimationDuration),
                ";--bo-overlay-opacity:", Css.Number(opacity),
                ";--bo-arrow:", Css.Px(_options.ArrowSize));
        }
    }

    private string SpotlightStyle
    {
        get
        {
            if (SpotlightRect is not { } rect || _options is null) return string.Empty;

            var radius = _options.SpotlightShape switch
            {
                SpotlightShape.Circle => Css.Px(Math.Max(rect.Width, rect.Height)),
                SpotlightShape.Rectangle => "0",
                _ => Css.Px(_options.SpotlightRadius),
            };

            return string.Concat(
                "left:", Css.Px(rect.X),
                ";top:", Css.Px(rect.Y),
                ";width:", Css.Px(rect.Width),
                ";height:", Css.Px(rect.Height),
                ";border-radius:", radius);
        }
    }

    private string PopoverStyle
    {
        get
        {
            if (Placement is not { } placement)
            {
                // Off-screen but laid out, so it can be measured without ever being visible.
                return "transform:translate3d(0,0,0)";
            }

            return string.Concat(
                "transform:translate3d(",
                Css.Px(placement.Popover.X), ",", Css.Px(placement.Popover.Y), ",0)");
        }
    }

    private string ArrowStyle
    {
        get
        {
            if (Placement is not { } placement || _options is null) return string.Empty;

            var offset = Css.Px(placement.ArrowOffset - (_options.ArrowSize / 2));

            return placement.Side switch
            {
                PlacementSide.Top or PlacementSide.Bottom => "left:" + offset,
                _ => "top:" + offset,
            };
        }
    }

    private enum BlockerSide { Top, Bottom, Left, Right }

    /// <summary>
    /// Positions one of the four rectangles that surround the spotlight hole, so pointer events
    /// reach the highlighted element and nothing else.
    /// </summary>
    private static string BlockerStyle(BlockerSide side, Rect hole) => side switch
    {
        BlockerSide.Top => string.Concat("left:0;right:0;top:0;height:", Css.Px(Math.Max(0, hole.Top))),
        BlockerSide.Bottom => string.Concat("left:0;right:0;top:", Css.Px(Math.Max(0, hole.Bottom)), ";bottom:0"),
        BlockerSide.Left => string.Concat(
            "left:0;width:", Css.Px(Math.Max(0, hole.Left)),
            ";top:", Css.Px(Math.Max(0, hole.Top)),
            ";height:", Css.Px(Math.Max(0, hole.Height))),
        _ => string.Concat(
            "left:", Css.Px(Math.Max(0, hole.Right)),
            ";right:0;top:", Css.Px(Math.Max(0, hole.Top)),
            ";height:", Css.Px(Math.Max(0, hole.Height))),
    };

    private string? AriaLabel
        => string.IsNullOrEmpty(_renderContext?.Title)
            ? _options?.Labels.DialogLabel ?? _session?.Tour.Title
            : null;

    private string? AriaLabelledBy
        => string.IsNullOrEmpty(_renderContext?.Title) ? null : _titleId;

    private string? AriaDescribedBy
        => string.IsNullOrEmpty(_renderContext?.Description) ? null : _descriptionId;

    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        Onboarding.SessionsChanged -= OnSessionsChanged;
        Hosts.Changed -= OnSessionsChanged;

        if (_claimedSessionId is not null)
        {
            Hosts.Release(_claimedSessionId);
            _claimedSessionId = null;
        }

        if (_session is not null) _session.Changed -= OnSessionChanged;

        _session = null;
        return ValueTask.CompletedTask;
    }
}
