namespace BlazorOnboarding;

/// <summary>
/// A fully resolved, non-nullable snapshot of the options that apply to one step, produced by
/// merging step, tour and global settings. Templates and custom UIs read this rather than walking
/// the three levels themselves.
/// </summary>
public sealed class EffectiveStepOptions
{
    internal EffectiveStepOptions() { }

    public Placement Placement { get; internal init; } = OnboardingDefaults.Placement;
    public IReadOnlyList<Placement>? PlacementFallbacks { get; internal init; }
    public double Offset { get; internal init; } = OnboardingDefaults.Offset;
    public double ViewportPadding { get; internal init; } = OnboardingDefaults.ViewportPadding;
    public double ArrowSize { get; internal init; } = OnboardingDefaults.ArrowSize;

    public bool ShowSpotlight { get; internal init; } = OnboardingDefaults.ShowSpotlight;
    public double SpotlightPadding { get; internal init; } = OnboardingDefaults.SpotlightPadding;
    public double SpotlightRadius { get; internal init; } = OnboardingDefaults.SpotlightRadius;
    public SpotlightShape SpotlightShape { get; internal init; } = OnboardingDefaults.SpotlightShape;

    public bool ShowOverlay { get; internal init; } = OnboardingDefaults.ShowOverlay;
    public double OverlayOpacity { get; internal init; } = OnboardingDefaults.OverlayOpacity;
    public InteractionMode Interaction { get; internal init; } = OnboardingDefaults.Interaction;
    public bool CloseOnOverlayClick { get; internal init; } = OnboardingDefaults.CloseOnOverlayClick;
    public bool CloseOnEscape { get; internal init; } = OnboardingDefaults.CloseOnEscape;

    public ScrollMode Scroll { get; internal init; } = OnboardingDefaults.Scroll;
    public double ScrollPadding { get; internal init; } = OnboardingDefaults.ScrollPadding;

    public MissingTargetBehavior MissingTarget { get; internal init; } = OnboardingDefaults.MissingTarget;
    public TimeSpan WaitTimeout { get; internal init; } = OnboardingDefaults.WaitTimeout;
    public MissingTargetBehavior WaitTimeoutBehavior { get; internal init; } = OnboardingDefaults.WaitTimeoutBehavior;

    public ProgressStyle Progress { get; internal init; } = OnboardingDefaults.Progress;
    public bool ShowNext { get; internal init; } = OnboardingDefaults.ShowNext;
    public bool ShowPrevious { get; internal init; } = OnboardingDefaults.ShowPrevious;
    public bool ShowSkip { get; internal init; } = OnboardingDefaults.ShowSkip;
    public bool ShowClose { get; internal init; } = OnboardingDefaults.ShowClose;

    /// <summary>Already zero when reduced motion is requested and honoured.</summary>
    public TimeSpan AnimationDuration { get; internal init; } = OnboardingDefaults.AnimationDuration;

    public bool RespectReducedMotion { get; internal init; } = OnboardingDefaults.RespectReducedMotion;

    /// <summary>True when the user asked for reduced motion and the tour honours it.</summary>
    public bool ReducedMotion { get; internal init; }

    public OnboardingTheme Theme { get; internal init; } = OnboardingDefaults.Theme;
    public OnboardingDirection Direction { get; internal init; } = OnboardingDefaults.Direction;
    public int ZIndex { get; internal init; } = OnboardingDefaults.ZIndex;
    public string? PopoverClass { get; internal init; }

    public bool KeyboardNavigation { get; internal init; } = OnboardingDefaults.KeyboardNavigation;
    public bool TrapFocus { get; internal init; } = OnboardingDefaults.TrapFocus;
    public bool RestoreFocus { get; internal init; } = OnboardingDefaults.RestoreFocus;
    public bool AutoFocus { get; internal init; } = OnboardingDefaults.AutoFocus;

    /// <summary>The label set in force, after tour-level overrides and localization.</summary>
    public OnboardingLabels Labels { get; internal init; } = OnboardingLabels.Default;

    /// <summary>True when the layout should be mirrored.</summary>
    public bool RightToLeft => Direction == OnboardingDirection.RightToLeft;
}

/// <summary>Merges step, tour and global options into an <see cref="EffectiveStepOptions"/>.</summary>
internal static class OptionResolver
{
    public static EffectiveStepOptions Resolve(
        StepDefinition? step,
        TourDefinition tour,
        OnboardingOptions global,
        OnboardingLabels labels,
        bool reducedMotion,
        OnboardingDirection? documentDirection)
    {
        var respectReducedMotion = Pick(step?.RespectReducedMotion, tour.RespectReducedMotion, global.RespectReducedMotion, OnboardingDefaults.RespectReducedMotion);
        var effectiveReducedMotion = respectReducedMotion && reducedMotion;
        var duration = Pick(step?.AnimationDuration, tour.AnimationDuration, global.AnimationDuration, OnboardingDefaults.AnimationDuration);

        var direction = Pick(step?.Direction, tour.Direction, global.Direction, OnboardingDefaults.Direction);
        if (direction == OnboardingDirection.Auto)
        {
            direction = documentDirection ?? OnboardingDirection.LeftToRight;
        }

        var interaction = Pick(step?.Interaction, tour.Interaction, global.Interaction, OnboardingDefaults.Interaction);

        // A step that waits for a real interaction has to let that interaction through.
        if (step?.AdvanceOn is not null && step.Interaction is null && tour.Interaction is null && global.Interaction is null)
        {
            interaction = InteractionMode.TargetOnly;
        }

        return new EffectiveStepOptions
        {
            Placement = Pick(step?.Placement, tour.Placement, global.Placement, OnboardingDefaults.Placement),
            PlacementFallbacks = step?.PlacementFallbacks ?? tour.PlacementFallbacks ?? global.PlacementFallbacks,
            Offset = Pick(step?.Offset, tour.Offset, global.Offset, OnboardingDefaults.Offset),
            ViewportPadding = Pick(step?.ViewportPadding, tour.ViewportPadding, global.ViewportPadding, OnboardingDefaults.ViewportPadding),
            ArrowSize = Pick(step?.ArrowSize, tour.ArrowSize, global.ArrowSize, OnboardingDefaults.ArrowSize),

            ShowSpotlight = Pick(step?.ShowSpotlight, tour.ShowSpotlight, global.ShowSpotlight, OnboardingDefaults.ShowSpotlight),
            SpotlightPadding = Pick(step?.SpotlightPadding, tour.SpotlightPadding, global.SpotlightPadding, OnboardingDefaults.SpotlightPadding),
            SpotlightRadius = Pick(step?.SpotlightRadius, tour.SpotlightRadius, global.SpotlightRadius, OnboardingDefaults.SpotlightRadius),
            SpotlightShape = Pick(step?.SpotlightShape, tour.SpotlightShape, global.SpotlightShape, OnboardingDefaults.SpotlightShape),

            ShowOverlay = Pick(step?.ShowOverlay, tour.ShowOverlay, global.ShowOverlay, OnboardingDefaults.ShowOverlay),
            OverlayOpacity = Pick(step?.OverlayOpacity, tour.OverlayOpacity, global.OverlayOpacity, OnboardingDefaults.OverlayOpacity),
            Interaction = interaction,
            CloseOnOverlayClick = Pick(step?.CloseOnOverlayClick, tour.CloseOnOverlayClick, global.CloseOnOverlayClick, OnboardingDefaults.CloseOnOverlayClick),
            CloseOnEscape = Pick(step?.CloseOnEscape, tour.CloseOnEscape, global.CloseOnEscape, OnboardingDefaults.CloseOnEscape),

            Scroll = Pick(step?.Scroll, tour.Scroll, global.Scroll, OnboardingDefaults.Scroll),
            ScrollPadding = Pick(step?.ScrollPadding, tour.ScrollPadding, global.ScrollPadding, OnboardingDefaults.ScrollPadding),

            MissingTarget = Pick(step?.MissingTarget, tour.MissingTarget, global.MissingTarget, OnboardingDefaults.MissingTarget),
            WaitTimeout = Pick(step?.WaitTimeout, tour.WaitTimeout, global.WaitTimeout, OnboardingDefaults.WaitTimeout),
            WaitTimeoutBehavior = Pick(step?.WaitTimeoutBehavior, tour.WaitTimeoutBehavior, global.WaitTimeoutBehavior, OnboardingDefaults.WaitTimeoutBehavior),

            Progress = Pick(step?.Progress, tour.Progress, global.Progress, OnboardingDefaults.Progress),
            ShowNext = Pick(step?.ShowNext, tour.ShowNext, global.ShowNext, OnboardingDefaults.ShowNext),
            ShowPrevious = Pick(step?.ShowPrevious, tour.ShowPrevious, global.ShowPrevious, OnboardingDefaults.ShowPrevious),
            ShowSkip = Pick(step?.ShowSkip, tour.ShowSkip, global.ShowSkip, OnboardingDefaults.ShowSkip),
            ShowClose = Pick(step?.ShowClose, tour.ShowClose, global.ShowClose, OnboardingDefaults.ShowClose),

            AnimationDuration = effectiveReducedMotion ? TimeSpan.Zero : duration,
            RespectReducedMotion = respectReducedMotion,
            ReducedMotion = effectiveReducedMotion,
            Theme = Pick(step?.Theme, tour.Theme, global.Theme, OnboardingDefaults.Theme),
            Direction = direction,
            ZIndex = Pick(step?.ZIndex, tour.ZIndex, global.ZIndex, OnboardingDefaults.ZIndex),
            PopoverClass = JoinClasses(global.PopoverClass, tour.PopoverClass, step?.PopoverClass),

            KeyboardNavigation = Pick(step?.KeyboardNavigation, tour.KeyboardNavigation, global.KeyboardNavigation, OnboardingDefaults.KeyboardNavigation),
            TrapFocus = Pick(step?.TrapFocus, tour.TrapFocus, global.TrapFocus, OnboardingDefaults.TrapFocus),
            RestoreFocus = Pick(step?.RestoreFocus, tour.RestoreFocus, global.RestoreFocus, OnboardingDefaults.RestoreFocus),
            AutoFocus = Pick(step?.AutoFocus, tour.AutoFocus, global.AutoFocus, OnboardingDefaults.AutoFocus),

            Labels = labels,
        };
    }

    private static T Pick<T>(T? step, T? tour, T? global, T fallback) where T : struct
        => step ?? tour ?? global ?? fallback;

    private static string? JoinClasses(params string?[] values)
    {
        var present = values.Where(v => !string.IsNullOrWhiteSpace(v)).ToArray();
        return present.Length == 0 ? null : string.Join(' ', present);
    }
}
