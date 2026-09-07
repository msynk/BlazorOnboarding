using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>
/// A complete tour. Inherits every presentation knob from <see cref="StepOptions"/> as the
/// default for its steps.
/// </summary>
public sealed class TourDefinition : StepOptions
{
    /// <summary>
    /// Sentinel returned by <see cref="StepDefinition.ResolveNext"/> to end the tour instead of
    /// moving to another step.
    /// </summary>
    public const string EndStepId = "$end";

    /// <summary>Stable identifier. Also the persistence key.</summary>
    public required string Id { get; set; }

    /// <summary>Human-readable name, used as the dialog label when a step has no title.</summary>
    public string? Title { get; set; }

    /// <summary>Optional description, purely informational.</summary>
    public string? Description { get; set; }

    /// <summary>The steps, in their default order. Branching hooks may deviate from it.</summary>
    public List<StepDefinition> Steps { get; init; } = [];

    /// <summary>Overrides the global label set for this tour.</summary>
    public OnboardingLabels? Labels { get; set; }

    /// <summary>Overrides <see cref="OnboardingOptions.Persist"/> for this tour.</summary>
    public bool? Persist { get; set; }

    /// <summary>
    /// Bumping this discards previously persisted progress for the tour, so an edited tour does
    /// not resume users into a step that no longer means the same thing.
    /// </summary>
    public int Version { get; set; } = 1;

    /// <summary>Replaces the popover for every step that does not define its own template.</summary>
    public RenderFragment<StepRenderContext>? Template { get; set; }

    /// <summary>Gate evaluated by <see cref="IOnboardingService.StartAsync(TourDefinition, StartOptions, CancellationToken)"/>; false aborts the start.</summary>
    public Func<TourContext, ValueTask<bool>>? CanStart { get; set; }

    /// <summary>Raised once the first step is about to be shown.</summary>
    public Func<TourContext, ValueTask>? OnStarted { get; set; }

    /// <summary>Raised after every successful step transition.</summary>
    public Func<StepChangedEventArgs, ValueTask>? OnStepChanged { get; set; }

    /// <summary>Raised exactly once when the tour stops, whatever the reason.</summary>
    public Func<TourEndedEventArgs, ValueTask>? OnEnded { get; set; }

    /// <summary>Arbitrary payload carried through to callbacks and analytics.</summary>
    public object? Data { get; set; }

    /// <summary>Finds a step by id.</summary>
    public StepDefinition? FindStep(string stepId) => Steps.FirstOrDefault(s => s.Id == stepId);

    /// <summary>Index of a step by id, or <c>-1</c>.</summary>
    public int IndexOf(string stepId) => Steps.FindIndex(s => s.Id == stepId);

    public override string ToString() => $"Tour({Id}, {Steps.Count} steps)";
}
