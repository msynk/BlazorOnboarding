using Microsoft.Extensions.Options;

namespace BlazorOnboarding;

/// <summary>
/// Resolves labels from <see cref="TourDefinition.Labels"/> when present, otherwise from
/// <see cref="OnboardingOptions.Labels"/>.
/// </summary>
public sealed class DefaultOnboardingLocalizer(IOptions<OnboardingOptions> options) : IOnboardingLocalizer
{
    private readonly OnboardingOptions _options = options.Value;

    public OnboardingLabels GetLabels(TourDefinition? tour)
        => tour?.Labels ?? _options.Labels;
}
