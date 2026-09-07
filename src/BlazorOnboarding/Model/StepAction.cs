namespace BlazorOnboarding;

/// <summary>Visual weight of a <see cref="StepAction"/> button.</summary>
public enum StepActionStyle { Primary, Secondary, Ghost, Danger }

/// <summary>What the tour does after a <see cref="StepAction"/>'s handler completes.</summary>
public enum StepActionEffect
{
    /// <summary>Stay on the current step.</summary>
    None,
    Next,
    Previous,
    /// <summary>Jump to <see cref="StepAction.TargetStepId"/>.</summary>
    GoTo,
    Complete,
    Skip,
    Dismiss,
}

/// <summary>
/// An extra button rendered in a step's footer. Combined with
/// <see cref="StepActionEffect.GoTo"/> this is the simplest way to build a branching tour.
/// </summary>
public sealed class StepAction
{
    /// <summary>Stable identifier, surfaced on the button as <c>data-bo-action</c>.</summary>
    public string? Id { get; set; }

    /// <summary>Button text.</summary>
    public required string Label { get; set; }

    /// <summary>Visual weight. Default <see cref="StepActionStyle.Secondary"/>.</summary>
    public StepActionStyle Style { get; set; } = StepActionStyle.Secondary;

    /// <summary>Optional handler, awaited before <see cref="Effect"/> is applied.</summary>
    public Func<StepContext, ValueTask>? OnClick { get; set; }

    /// <summary>Navigation performed after <see cref="OnClick"/>. Default <see cref="StepActionEffect.None"/>.</summary>
    public StepActionEffect Effect { get; set; } = StepActionEffect.None;

    /// <summary>Destination step id when <see cref="Effect"/> is <see cref="StepActionEffect.GoTo"/>.</summary>
    public string? TargetStepId { get; set; }

    /// <summary>Disables the button when it returns <see langword="true"/>.</summary>
    public Func<StepContext, bool>? IsDisabled { get; set; }

    /// <summary>Extra CSS classes on the button element.</summary>
    public string? CssClass { get; set; }

    /// <summary>Receive focus when the step is shown instead of the default primary button.</summary>
    public bool AutoFocus { get; set; }
}
