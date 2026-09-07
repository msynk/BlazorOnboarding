namespace BlazorOnboarding;

/// <summary>Every observable moment in a tour's life, for analytics pipelines.</summary>
public enum OnboardingEventKind
{
    TourStarted,
    TourResumed,
    StepShown,
    StepLeft,
    StepSkipped,
    TargetMissing,
    TargetResolved,
    ActionInvoked,
    TourPaused,
    TourCompleted,
    TourSkipped,
    TourDismissed,
    TourCancelled,
    TourFailed,
}

/// <summary>A single analytics-grade record of something that happened during a tour.</summary>
public sealed class OnboardingEvent
{
    public required OnboardingEventKind Kind { get; init; }
    public required ITourSession Session { get; init; }
    public TourDefinition Tour => Session.Tour;
    public StepDefinition? Step { get; init; }

    /// <summary>Zero-based index of <see cref="Step"/>, or <c>-1</c>.</summary>
    public int StepIndex { get; init; } = -1;

    /// <summary>How long the previous step was on screen, for <see cref="OnboardingEventKind.StepLeft"/>.</summary>
    public TimeSpan? Duration { get; init; }

    /// <summary>Populated for <see cref="OnboardingEventKind.ActionInvoked"/>.</summary>
    public StepAction? Action { get; init; }

    /// <summary>Populated for terminal events.</summary>
    public TourEndReason? Reason { get; init; }

    /// <summary>Populated for <see cref="OnboardingEventKind.TourFailed"/>.</summary>
    public Exception? Error { get; init; }

    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;

    public override string ToString() => $"{Kind}({Session.Tour.Id}{(Step is null ? "" : $"/{Step.Id}")})";
}

/// <summary>Arguments for <see cref="TourDefinition.OnStepChanged"/>.</summary>
public sealed class StepChangedEventArgs(
    ITourSession session,
    StepDefinition step,
    int index,
    StepDefinition? previousStep,
    StepChangeReason reason)
{
    public ITourSession Session { get; } = session;
    public StepDefinition Step { get; } = step;
    public int Index { get; } = index;
    public StepDefinition? PreviousStep { get; } = previousStep;
    public StepChangeReason Reason { get; } = reason;
}

/// <summary>Arguments for <see cref="TourDefinition.OnEnded"/>.</summary>
public sealed class TourEndedEventArgs(
    ITourSession session,
    TourEndReason reason,
    StepDefinition? lastStep,
    int lastIndex,
    Exception? error = null)
{
    public ITourSession Session { get; } = session;
    public TourEndReason Reason { get; } = reason;

    /// <summary>The step the user was on when the tour stopped.</summary>
    public StepDefinition? LastStep { get; } = lastStep;

    public int LastIndex { get; } = lastIndex;

    /// <summary>Set when <see cref="Reason"/> is <see cref="TourEndReason.Failed"/>.</summary>
    public Exception? Error { get; } = error;

    /// <summary>True when the tour ran to the end rather than being abandoned.</summary>
    public bool IsSuccess => Reason == TourEndReason.Completed;
}

/// <summary>
/// Receives every <see cref="OnboardingEvent"/>. Register any number of implementations in DI to
/// forward tour telemetry to your analytics provider.
/// </summary>
public interface IOnboardingObserver
{
    ValueTask OnEventAsync(OnboardingEvent onboardingEvent, CancellationToken cancellationToken = default);
}

/// <summary>Raised when the library cannot continue a tour.</summary>
public sealed class OnboardingException : Exception
{
    public OnboardingException(string message) : base(message) { }

    public OnboardingException(string message, Exception innerException) : base(message, innerException) { }

    /// <summary>The tour that failed, when known.</summary>
    public string? TourId { get; init; }

    /// <summary>The step that failed, when known.</summary>
    public string? StepId { get; init; }
}
