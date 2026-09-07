using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>How a target is described to the browser layer.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<TargetKind>))]
public enum TargetKind
{
    /// <summary>No element; the step is a centred card.</summary>
    None,
    /// <summary>A CSS selector, re-queried on every measurement so re-renders are survivable.</summary>
    Selector,
    /// <summary>A captured element reference.</summary>
    Element,
}

/// <summary>A target flattened into something the browser layer can look up.</summary>
public sealed record TargetDescriptor
{
    /// <summary>A descriptor that matches nothing.</summary>
    public static TargetDescriptor None { get; } = new() { Kind = TargetKind.None };

    public TargetKind Kind { get; init; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="TargetKind.Selector"/>.</summary>
    public string? Selector { get; init; }

    /// <summary>Set when <see cref="Kind"/> is <see cref="TargetKind.Element"/>.</summary>
    public ElementReference? Element { get; init; }

    /// <summary>Flattens a <see cref="StepTarget"/>; dynamic targets must be resolved first.</summary>
    public static TargetDescriptor From(StepTarget target) => target switch
    {
        StepTarget.CssTarget css => new TargetDescriptor { Kind = TargetKind.Selector, Selector = css.Selector },
        StepTarget.AnchorTarget anchor => new TargetDescriptor { Kind = TargetKind.Selector, Selector = anchor.Selector },
        StepTarget.ElementTarget element => new TargetDescriptor { Kind = TargetKind.Element, Element = element.Reference },
        _ => None,
    };
}

/// <summary>An event listener that advances the tour when the user acts on the page.</summary>
public sealed record AdvanceTriggerDescriptor
{
    public required string EventName { get; init; }
    public string? Selector { get; init; }
    public string? MatchSelector { get; init; }
    public int DelayMs { get; init; }
}

/// <summary>Everything the browser layer needs to make one step live.</summary>
public sealed record StepActivation
{
    public required string SessionId { get; init; }
    public required TargetDescriptor Target { get; init; }

    /// <summary>Folded into the spotlight and into the rectangle used for positioning.</summary>
    public IReadOnlyList<TargetDescriptor>? AdditionalTargets { get; init; }

    /// <summary>Keep polling the DOM through a mutation observer until the target appears.</summary>
    public bool WaitForTarget { get; init; }

    /// <summary>Milliseconds before <see cref="WaitForTarget"/> gives up. Zero waits forever.</summary>
    public int WaitTimeoutMs { get; init; }

    /// <summary>One of <c>smooth</c>, <c>instant</c>, <c>auto</c> or <c>none</c>.</summary>
    public required string Scroll { get; init; }

    public double ScrollPadding { get; init; }

    /// <summary>One of <c>blocked</c>, <c>target</c> or <c>free</c>.</summary>
    public required string Interaction { get; init; }

    public bool CloseOnEscape { get; init; }

    public bool KeyboardNavigation { get; init; }

    public AdvanceTriggerDescriptor? AdvanceOn { get; init; }
}

/// <summary>A measurement pushed from the browser.</summary>
public sealed record StepGeometry
{
    /// <summary>A geometry meaning "there is no target and none is expected".</summary>
    public static StepGeometry Missing { get; } = new();

    /// <summary>True when at least one target element was located.</summary>
    public bool Found { get; init; }

    /// <summary>Union of the target rectangles in viewport coordinates.</summary>
    public Rect Target { get; init; }

    /// <summary>The visual viewport.</summary>
    public Rect Viewport { get; init; }

    /// <summary>Measured popover size; zero until the popover has rendered.</summary>
    public Size Popover { get; init; }

    /// <summary>False when the target is scrolled out of sight or hidden.</summary>
    public bool TargetVisible { get; init; }

    /// <summary>Monotonic counter, used to drop out-of-order frames.</summary>
    public long Sequence { get; init; }
}

/// <summary>Browser facts sampled once per host.</summary>
public sealed record BrowserEnvironment
{
    /// <summary>Safe defaults for prerendering, where no browser is available.</summary>
    public static BrowserEnvironment Unknown { get; } = new();

    public bool ReducedMotion { get; init; }
    public bool RightToLeft { get; init; }
    public Size Viewport { get; init; } = new(1024, 768);
    public bool CoarsePointer { get; init; }
}

/// <summary>Callbacks the browser layer raises against a session.</summary>
public interface IOnboardingInteropCallbacks
{
    /// <summary>A new measurement is available.</summary>
    ValueTask OnGeometryChangedAsync(StepGeometry geometry);

    /// <summary>The target element left the DOM while the step was showing.</summary>
    ValueTask OnTargetLostAsync();

    /// <summary>A key was pressed while the tour had control of the keyboard.</summary>
    ValueTask OnKeyAsync(string key, bool shiftKey);

    /// <summary>The user clicked the dimmed area.</summary>
    ValueTask OnOverlayClickAsync();

    /// <summary>The step's <see cref="AdvanceTrigger"/> fired.</summary>
    ValueTask OnAdvanceTriggeredAsync();

    /// <summary>Waiting for the target timed out.</summary>
    ValueTask OnWaitTimedOutAsync();
}

/// <summary>
/// The only place the library talks to the browser. Substitute it in tests to run the whole engine
/// without a DOM.
/// </summary>
public interface IOnboardingInterop : IAsyncDisposable
{
    /// <summary>
    /// False while prerendering or after the circuit has gone away. Callers must degrade
    /// gracefully rather than throwing.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>Loads the browser module and samples the environment.</summary>
    ValueTask<BrowserEnvironment> InitializeAsync(CancellationToken cancellationToken = default);

    /// <summary>Registers a session so it can receive callbacks.</summary>
    ValueTask AttachAsync(string sessionId, IOnboardingInteropCallbacks callbacks, CancellationToken cancellationToken = default);

    /// <summary>Unregisters a session and tears down everything it owns in the browser.</summary>
    ValueTask DetachAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Resolves the target, scrolls it into view, installs observers, and returns the first
    /// measurement. Further measurements arrive through
    /// <see cref="IOnboardingInteropCallbacks.OnGeometryChangedAsync"/>.
    /// </summary>
    ValueTask<StepGeometry> ActivateStepAsync(StepActivation activation, CancellationToken cancellationToken = default);

    /// <summary>Removes the observers and listeners for the current step.</summary>
    ValueTask DeactivateStepAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Forces a fresh measurement.</summary>
    ValueTask<StepGeometry> MeasureAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the browser layer which element is the popover, so it can be measured and, when
    /// requested, act as a focus trap.
    /// </summary>
    ValueTask SetPopoverAsync(string sessionId, ElementReference? popover, bool trapFocus, CancellationToken cancellationToken = default);

    /// <summary>Remembers the currently focused element so it can be restored when the tour ends.</summary>
    ValueTask CaptureFocusAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Restores the focus captured by <see cref="CaptureFocusAsync"/>.</summary>
    ValueTask RestoreFocusAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Moves focus to the first sensible control inside the popover.</summary>
    ValueTask FocusPopoverAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>Reads a value from browser storage; used by the default persistence store.</summary>
    ValueTask<string?> GetStorageAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Writes a value to browser storage.</summary>
    ValueTask SetStorageAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>Removes a value from browser storage.</summary>
    ValueTask RemoveStorageAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>Lists the keys in browser storage that start with a prefix.</summary>
    ValueTask<IReadOnlyList<string>> ListStorageKeysAsync(string prefix, CancellationToken cancellationToken = default);
}
