using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>
/// One step of a tour. Inherits every presentation knob from <see cref="StepOptions"/>; anything
/// left null is inherited from the tour and then from the global options.
/// </summary>
public sealed class StepDefinition : StepOptions
{
    private string? _id;

    /// <summary>
    /// Stable identifier, used for jumps, branching and persistence. Auto-generated when not set,
    /// but persistence and branching are far more robust with an explicit value.
    /// </summary>
    public string Id
    {
        get => _id ??= $"step-{Guid.NewGuid():N}";
        set => _id = value;
    }

    /// <summary>True when <see cref="Id"/> was set explicitly rather than auto-generated.</summary>
    internal bool HasExplicitId => _id is not null;

    /// <summary>The element this step points at. Defaults to <see cref="StepTarget.None"/> (a centred card).</summary>
    public StepTarget Target { get; set; } = StepTarget.None;

    /// <summary>
    /// Additional elements folded into the spotlight, so several controls can be highlighted at
    /// once. Positioning still uses <see cref="Target"/> plus these, as their bounding union.
    /// </summary>
    public IReadOnlyList<StepTarget>? AdditionalTargets { get; set; }

    /// <summary>Heading shown in the popover and used as the dialog's accessible name.</summary>
    public string? Title { get; set; }

    /// <summary>Body text. Ignored when <see cref="Body"/> is supplied.</summary>
    public string? Description { get; set; }

    /// <summary>Custom body markup, replacing <see cref="Description"/>.</summary>
    public RenderFragment<StepRenderContext>? Body { get; set; }

    /// <summary>Custom header markup, replacing the title row (but not the close button).</summary>
    public RenderFragment<StepRenderContext>? Header { get; set; }

    /// <summary>Custom footer markup, replacing the whole button row.</summary>
    public RenderFragment<StepRenderContext>? Footer { get; set; }

    /// <summary>Replaces the entire popover for this step only.</summary>
    public RenderFragment<StepRenderContext>? Template { get; set; }

    /// <summary>
    /// Route this step belongs to, e.g. <c>"/settings"</c>. The engine navigates there before
    /// showing the step and waits for the location to match.
    /// </summary>
    public string? Route { get; set; }

    /// <summary>Extra footer buttons.</summary>
    public IReadOnlyList<StepAction>? Actions { get; set; }

    /// <summary>Overrides the "next" button text for this step.</summary>
    public string? NextLabel { get; set; }

    /// <summary>Overrides the "back" button text for this step.</summary>
    public string? PreviousLabel { get; set; }

    /// <summary>Overrides the "skip" button text for this step.</summary>
    public string? SkipLabel { get; set; }

    /// <summary>Overrides the final-step button text.</summary>
    public string? DoneLabel { get; set; }

    /// <summary>
    /// Gate evaluated when the engine walks onto this step. Returning <see langword="false"/>
    /// skips it in the direction of travel.
    /// </summary>
    public Func<StepContext, ValueTask<bool>>? When { get; set; }

    /// <summary>Runs after the target is resolved but before the popover is shown.</summary>
    public Func<StepContext, ValueTask>? OnBeforeShow { get; set; }

    /// <summary>Runs once the step is on screen and focus has been moved.</summary>
    public Func<StepContext, ValueTask>? OnShown { get; set; }

    /// <summary>
    /// Runs before leaving the step. Throwing cancels the transition; use it for validation.
    /// </summary>
    public Func<StepContext, ValueTask>? OnBeforeLeave { get; set; }

    /// <summary>
    /// Branching hook: returns the id of the next step, <c>null</c> for "the following step in
    /// order", or <see cref="TourDefinition.EndStepId"/> to finish the tour.
    /// </summary>
    public Func<StepContext, ValueTask<string?>>? ResolveNext { get; set; }

    /// <summary>Branching hook for the back button. Falls back to the visit history when null.</summary>
    public Func<StepContext, ValueTask<string?>>? ResolvePrevious { get; set; }

    /// <summary>Blocks forward navigation while it returns <see langword="false"/>.</summary>
    public Func<StepContext, ValueTask<bool>>? CanAdvance { get; set; }

    /// <summary>
    /// Advances the tour when the user interacts with the page, e.g. actually clicking the
    /// highlighted button. Implies <see cref="InteractionMode.TargetOnly"/> unless overridden.
    /// </summary>
    public AdvanceTrigger? AdvanceOn { get; set; }

    /// <summary>Arbitrary payload carried through to callbacks, analytics and custom templates.</summary>
    public object? Data { get; set; }

    /// <summary>Free-form metadata, handy for analytics dimensions.</summary>
    public IReadOnlyDictionary<string, object?>? Metadata { get; set; }

    public override string ToString() => $"Step({Id}{(Title is null ? "" : $", \"{Title}\"")})";
}

/// <summary>
/// Declares a DOM event that advances the tour, enabling "now click this button yourself" steps.
/// </summary>
public sealed class AdvanceTrigger
{
    /// <summary>Advance when the step's own target is clicked.</summary>
    public static AdvanceTrigger TargetClick { get; } = new();

    /// <summary>DOM event name. Default <c>"click"</c>.</summary>
    public string EventName { get; set; } = "click";

    /// <summary>
    /// Element to listen on. <see langword="null"/> listens on the step's resolved target.
    /// </summary>
    public string? Selector { get; set; }

    /// <summary>
    /// Only advance when the event's target matches this selector. Useful for delegated events on
    /// a container.
    /// </summary>
    public string? MatchSelector { get; set; }

    /// <summary>Milliseconds to wait after the event before advancing, letting the UI settle. Default <c>150</c>.</summary>
    public int DelayMs { get; set; } = 150;
}
