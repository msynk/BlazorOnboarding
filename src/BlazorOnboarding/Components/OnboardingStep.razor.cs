using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>
/// Declares one step of the enclosing <see cref="OnboardingTour"/>. Renders no markup.
/// </summary>
public sealed partial class OnboardingStep : ComponentBase, IDisposable
{
    private static int _sequenceSource;

    private readonly int _sequence = Interlocked.Increment(ref _sequenceSource);

    private bool _attached;

    [CascadingParameter] private OnboardingTour? Tour { get; set; }

    /// <summary>The definition this component maintains. Live: edits are picked up by a running tour.</summary>
    public StepDefinition Definition { get; } = new();

    /// <summary>Explicit ordering. Steps without one keep their declaration order.</summary>
    [Parameter] public int? Order { get; set; }

    internal double SortKey => Order ?? _sequence;

    // ---- Targeting ----------------------------------------------------------

    /// <summary>
    /// The element to point at. Accepts a CSS selector string or a captured
    /// <see cref="ElementReference"/> directly.
    /// </summary>
    [Parameter] public StepTarget? Target { get; set; }

    /// <summary>Points at the element carrying <c>data-bo-anchor="..."</c>.</summary>
    [Parameter] public string? Anchor { get; set; }

    /// <summary>Points at the first element matching a CSS selector.</summary>
    [Parameter] public string? Selector { get; set; }

    /// <summary>Points at a captured element reference.</summary>
    [Parameter] public ElementReference? Element { get; set; }

    /// <summary>Extra elements folded into the same spotlight.</summary>
    [Parameter] public IReadOnlyList<StepTarget>? AdditionalTargets { get; set; }

    // ---- Content ------------------------------------------------------------

    /// <summary>Stable identifier, needed for jumps, branching and reliable resume.</summary>
    [Parameter] public string? Id { get; set; }

    /// <summary>Heading, also the dialog's accessible name.</summary>
    [Parameter] public string? Title { get; set; }

    /// <summary>Body text. Ignored when <see cref="ChildContent"/> is supplied.</summary>
    [Parameter] public string? Description { get; set; }

    /// <summary>Custom body markup.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? ChildContent { get; set; }

    /// <summary>Custom header markup, replacing the title row.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? Header { get; set; }

    /// <summary>Custom footer markup, replacing the whole button row.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? Footer { get; set; }

    /// <summary>Replaces the entire popover for this step.</summary>
    [Parameter] public RenderFragment<StepRenderContext>? Template { get; set; }

    /// <summary>Extra footer buttons.</summary>
    [Parameter] public IReadOnlyList<StepAction>? Actions { get; set; }

    // ---- Behaviour ----------------------------------------------------------

    /// <summary>Route this step belongs to; the engine navigates there first.</summary>
    [Parameter] public string? Route { get; set; }

    /// <summary>Skips the step when this returns false.</summary>
    [Parameter] public Func<StepContext, ValueTask<bool>>? When { get; set; }

    /// <summary>Blocks the forward button while this returns false.</summary>
    [Parameter] public Func<StepContext, ValueTask<bool>>? CanAdvance { get; set; }

    /// <summary>Branching: returns the id of the next step.</summary>
    [Parameter] public Func<StepContext, ValueTask<string?>>? ResolveNext { get; set; }

    /// <summary>Branching: returns the id of the previous step.</summary>
    [Parameter] public Func<StepContext, ValueTask<string?>>? ResolvePrevious { get; set; }

    /// <summary>Advances when the user interacts with the highlighted element.</summary>
    [Parameter] public AdvanceTrigger? AdvanceOn { get; set; }

    /// <summary>Runs before the popover is shown.</summary>
    [Parameter] public Func<StepContext, ValueTask>? OnBeforeShow { get; set; }

    /// <summary>Runs once the step is on screen.</summary>
    [Parameter] public Func<StepContext, ValueTask>? OnShown { get; set; }

    /// <summary>Runs before the step is left; throw to cancel the transition.</summary>
    [Parameter] public Func<StepContext, ValueTask>? OnBeforeLeave { get; set; }

    /// <summary>Arbitrary payload carried through to callbacks and templates.</summary>
    [Parameter] public object? Data { get; set; }

    // ---- Presentation overrides ---------------------------------------------

    [Parameter] public Placement? Placement { get; set; }
    [Parameter] public IReadOnlyList<Placement>? PlacementFallbacks { get; set; }
    [Parameter] public double? Offset { get; set; }
    [Parameter] public bool? ShowSpotlight { get; set; }
    [Parameter] public double? SpotlightPadding { get; set; }
    [Parameter] public double? SpotlightRadius { get; set; }
    [Parameter] public SpotlightShape? SpotlightShape { get; set; }
    [Parameter] public bool? ShowOverlay { get; set; }
    [Parameter] public InteractionMode? Interaction { get; set; }
    [Parameter] public ScrollMode? Scroll { get; set; }
    [Parameter] public MissingTargetBehavior? MissingTarget { get; set; }
    [Parameter] public TimeSpan? WaitTimeout { get; set; }
    [Parameter] public MissingTargetBehavior? WaitTimeoutBehavior { get; set; }
    [Parameter] public ProgressStyle? Progress { get; set; }
    [Parameter] public bool? ShowNext { get; set; }
    [Parameter] public bool? ShowPrevious { get; set; }
    [Parameter] public bool? ShowSkip { get; set; }
    [Parameter] public bool? ShowClose { get; set; }
    [Parameter] public string? NextLabel { get; set; }
    [Parameter] public string? PreviousLabel { get; set; }
    [Parameter] public string? SkipLabel { get; set; }
    [Parameter] public string? DoneLabel { get; set; }
    [Parameter] public string? PopoverClass { get; set; }

    /// <summary>Escape hatch for anything not exposed as a parameter.</summary>
    [Parameter] public Action<StepDefinition>? Configure { get; set; }

    protected override void OnInitialized()
    {
        if (Tour is null)
        {
            throw new InvalidOperationException(
                $"{nameof(OnboardingStep)} must be placed inside an {nameof(OnboardingTour)}.");
        }
    }

    protected override void OnParametersSet()
    {
        Apply();

        if (Tour is null) return;

        if (!_attached)
        {
            _attached = true;
            Tour.AttachStep(this);
        }
        else
        {
            Tour.NotifyStepChanged();
        }
    }

    private void Apply()
    {
        if (Id is not null) Definition.Id = Id;

        Definition.Target = Target
            ?? (Element is { } element ? StepTarget.Element(element) : null)
            ?? (Anchor is not null ? StepTarget.Anchor(Anchor) : null)
            ?? (Selector is not null ? StepTarget.Css(Selector) : null)
            ?? StepTarget.None;

        Definition.AdditionalTargets = AdditionalTargets;
        Definition.Title = Title;
        Definition.Description = Description;
        Definition.Body = ChildContent;
        Definition.Header = Header;
        Definition.Footer = Footer;
        Definition.Template = Template;
        Definition.Actions = Actions;
        Definition.Route = Route;
        Definition.When = When;
        Definition.CanAdvance = CanAdvance;
        Definition.ResolveNext = ResolveNext;
        Definition.ResolvePrevious = ResolvePrevious;
        Definition.AdvanceOn = AdvanceOn;
        Definition.OnBeforeShow = OnBeforeShow;
        Definition.OnShown = OnShown;
        Definition.OnBeforeLeave = OnBeforeLeave;
        Definition.Data = Data;

        Definition.Placement = Placement;
        Definition.PlacementFallbacks = PlacementFallbacks;
        Definition.Offset = Offset;
        Definition.ShowSpotlight = ShowSpotlight;
        Definition.SpotlightPadding = SpotlightPadding;
        Definition.SpotlightRadius = SpotlightRadius;
        Definition.SpotlightShape = SpotlightShape;
        Definition.ShowOverlay = ShowOverlay;
        Definition.Interaction = Interaction;
        Definition.Scroll = Scroll;
        Definition.MissingTarget = MissingTarget;
        Definition.WaitTimeout = WaitTimeout;
        Definition.WaitTimeoutBehavior = WaitTimeoutBehavior;
        Definition.Progress = Progress;
        Definition.ShowNext = ShowNext;
        Definition.ShowPrevious = ShowPrevious;
        Definition.ShowSkip = ShowSkip;
        Definition.ShowClose = ShowClose;
        Definition.NextLabel = NextLabel;
        Definition.PreviousLabel = PreviousLabel;
        Definition.SkipLabel = SkipLabel;
        Definition.DoneLabel = DoneLabel;
        Definition.PopoverClass = PopoverClass;

        Configure?.Invoke(Definition);
    }

    public void Dispose()
    {
        if (!_attached) return;

        _attached = false;
        Tour?.DetachStep(this);
    }
}
