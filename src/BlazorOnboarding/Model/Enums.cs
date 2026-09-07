namespace BlazorOnboarding;

/// <summary>Requested position of the popover relative to its target.</summary>
public enum Placement
{
    /// <summary>Pick the side with the most available space, then align to the target centre.</summary>
    Auto,
    Top, TopStart, TopEnd,
    Bottom, BottomStart, BottomEnd,
    Left, LeftStart, LeftEnd,
    Right, RightStart, RightEnd,
    /// <summary>Ignore the target and centre the popover in the viewport.</summary>
    Center,
}

/// <summary>The physical side a popover was finally placed on.</summary>
public enum PlacementSide { Top, Right, Bottom, Left, Center }

/// <summary>Alignment of the popover along the target's cross axis.</summary>
public enum PlacementAlign { Start, Center, End }

/// <summary>What to do when a step's target element cannot be found.</summary>
public enum MissingTargetBehavior
{
    /// <summary>Wait for the element to appear, up to <see cref="StepOptions.WaitTimeout"/>, then apply <see cref="StepOptions.WaitTimeoutBehavior"/>.</summary>
    Wait,
    /// <summary>Silently advance to the next step.</summary>
    Skip,
    /// <summary>Show the step as a centred modal with no spotlight.</summary>
    Center,
    /// <summary>Fail the tour with an <see cref="OnboardingException"/>.</summary>
    Fail,
}

/// <summary>Lifecycle state of a tour session.</summary>
public enum TourStatus
{
    /// <summary>Created but not started.</summary>
    Idle,
    /// <summary>A step is displayed.</summary>
    Running,
    /// <summary>Waiting for a target element, a route change, or a guard.</summary>
    Waiting,
    /// <summary>Explicitly paused by the application.</summary>
    Paused,
    /// <summary>Reached the end and was finished by the user or programmatically.</summary>
    Completed,
    /// <summary>The user skipped the remainder of the tour.</summary>
    Skipped,
    /// <summary>The user closed the tour.</summary>
    Dismissed,
    /// <summary>
    /// Stopped by the application rather than by the user, for instance because another tour
    /// started. Progress is kept, so the tour resumes where it left off next time.
    /// </summary>
    Cancelled,
    /// <summary>Stopped by an unrecoverable error.</summary>
    Failed,
}

/// <summary>Why a tour stopped.</summary>
public enum TourEndReason { Completed, Skipped, Dismissed, Cancelled, Failed }

/// <summary>How the tour reached the current step.</summary>
public enum StepChangeReason { Start, Next, Previous, Jump, Resume, Retarget }

/// <summary>How much of the page the user may interact with while a step is shown.</summary>
public enum InteractionMode
{
    /// <summary>The overlay swallows every pointer event outside the popover.</summary>
    Blocked,
    /// <summary>The spotlight cut-out is click-through, so the user can operate the highlighted element.</summary>
    TargetOnly,
    /// <summary>The overlay never intercepts pointer events; the whole page stays usable.</summary>
    Free,
}

/// <summary>Scroll strategy used to bring a target into view.</summary>
public enum ScrollMode { Auto, Smooth, Instant, None }

/// <summary>Shape of the spotlight cut-out around a target.</summary>
public enum SpotlightShape { Rounded, Rectangle, Circle, None }

/// <summary>Built-in progress indicator styles.</summary>
public enum ProgressStyle { None, Text, Dots, Bar }

/// <summary>Colour scheme applied to the default UI.</summary>
public enum OnboardingTheme { System, Light, Dark }

/// <summary>Text direction applied to the default UI.</summary>
public enum OnboardingDirection { Auto, LeftToRight, RightToLeft }
