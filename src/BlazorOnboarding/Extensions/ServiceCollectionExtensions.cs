using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BlazorOnboarding;

/// <summary>Registration helpers for the onboarding services.</summary>
public static class OnboardingServiceCollectionExtensions
{
    /// <summary>
    /// Registers everything the library needs. All services are scoped, which is per-circuit on
    /// Blazor Server and per-application on WebAssembly.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional global defaults.</param>
    public static IServiceCollection AddBlazorOnboarding(
        this IServiceCollection services, Action<OnboardingOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<OnboardingOptions>();
        if (configure is not null) services.Configure(configure);

        services.TryAddScoped<OnboardingHostRegistry>();
        services.TryAddScoped<IOnboardingInterop, OnboardingInterop>();
        services.TryAddScoped<IOnboardingStore, BrowserOnboardingStore>();
        services.TryAddScoped<IOnboardingLocalizer, DefaultOnboardingLocalizer>();
        services.TryAddScoped<IOnboardingService, OnboardingService>();

        return services;
    }

    /// <summary>
    /// Replaces the persistence layer, for example to store progress against the user's profile on
    /// the server instead of in the browser.
    /// </summary>
    public static IServiceCollection AddOnboardingStore<TStore>(this IServiceCollection services)
        where TStore : class, IOnboardingStore
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IOnboardingStore>();
        services.AddScoped<IOnboardingStore, TStore>();
        return services;
    }

    /// <summary>Keeps onboarding progress in memory only, so nothing is written to the browser.</summary>
    public static IServiceCollection AddInMemoryOnboardingStore(this IServiceCollection services)
        => services.AddOnboardingStore<InMemoryOnboardingStore>();

    /// <summary>Replaces the label source, for example with an <c>IStringLocalizer</c> adapter.</summary>
    public static IServiceCollection AddOnboardingLocalizer<TLocalizer>(this IServiceCollection services)
        where TLocalizer : class, IOnboardingLocalizer
    {
        ArgumentNullException.ThrowIfNull(services);

        services.RemoveAll<IOnboardingLocalizer>();
        services.AddScoped<IOnboardingLocalizer, TLocalizer>();
        return services;
    }

    /// <summary>
    /// Adds an analytics sink. Several observers may be registered; each receives every event and
    /// a throwing observer can never break a tour.
    /// </summary>
    public static IServiceCollection AddOnboardingObserver<TObserver>(this IServiceCollection services)
        where TObserver : class, IOnboardingObserver
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IOnboardingObserver, TObserver>();
        return services;
    }
}
