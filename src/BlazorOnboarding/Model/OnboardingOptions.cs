namespace BlazorOnboarding;

/// <summary>
/// Application-wide defaults, configured through
/// <c>services.AddBlazorOnboarding(options =&gt; ...)</c>. Anything left <see langword="null"/>
/// falls back to <see cref="OnboardingDefaults"/>.
/// </summary>
public sealed class OnboardingOptions : StepOptions
{
    /// <summary>Default user-visible strings. Overridable per tour and per step.</summary>
    public OnboardingLabels Labels { get; set; } = new();

    /// <summary>
    /// Whether tour progress and completion are written to the registered
    /// <see cref="IOnboardingStore"/>. Default <see langword="true"/>.
    /// </summary>
    public bool Persist { get; set; } = true;

    /// <summary>Key prefix used by storage implementations. Default <c>"blazor-onboarding:"</c>.</summary>
    public string StorageKeyPrefix { get; set; } = "blazor-onboarding:";

    /// <summary>
    /// When a tour spans routes and a step declares a <see cref="StepDefinition.Route"/>, navigate
    /// there automatically. Default <see langword="true"/>.
    /// </summary>
    public bool AutoNavigateToStepRoute { get; set; } = true;

    /// <summary>
    /// Emitted diagnostics verbosity guard: when false the library never touches
    /// <c>console</c> from JavaScript. Default <see langword="false"/>.
    /// </summary>
    public bool EnableJsDiagnostics { get; set; }
}
