namespace BlazorOnboarding;

/// <summary>
/// The persisted footprint of one tour for one user: whether it finished, and where the user got
/// to if it did not.
/// </summary>
public sealed record OnboardingRecord
{
    /// <summary><see cref="TourDefinition.Id"/>.</summary>
    public required string TourId { get; init; }

    /// <summary>
    /// The <see cref="TourDefinition.Version"/> this record was written by. A record from an older
    /// version is discarded rather than resumed into a step that has since changed meaning.
    /// </summary>
    public int Version { get; init; } = 1;

    /// <summary>Id of the step the user was last on.</summary>
    public string? CurrentStepId { get; init; }

    /// <summary>Index of the step the user was last on, as a fallback when ids change.</summary>
    public int CurrentIndex { get; init; }

    /// <summary>True once the tour was run to the end.</summary>
    public bool Completed { get; init; }

    /// <summary>True when the user skipped or closed the tour instead of finishing it.</summary>
    public bool Dismissed { get; init; }

    /// <summary>Ids of the steps actually visited, in order. Restores branching history on resume.</summary>
    public IReadOnlyList<string> VisitedStepIds { get; init; } = [];

    public DateTimeOffset UpdatedAt { get; init; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Application-defined values round-tripped with the record.</summary>
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    /// <summary>True when the tour is finished with, however it ended.</summary>
    public bool IsClosed => Completed || Dismissed;
}

/// <summary>
/// Where onboarding progress lives. Swap the implementation to persist server-side, per tenant, or
/// into an existing user-preferences store.
/// </summary>
public interface IOnboardingStore
{
    /// <summary>Reads the record for a tour, or <see langword="null"/> when the user has no history with it.</summary>
    ValueTask<OnboardingRecord?> GetAsync(string tourId, CancellationToken cancellationToken = default);

    /// <summary>Writes (or overwrites) the record for a tour.</summary>
    ValueTask SetAsync(OnboardingRecord record, CancellationToken cancellationToken = default);

    /// <summary>Forgets one tour, so it can run again from the start.</summary>
    ValueTask RemoveAsync(string tourId, CancellationToken cancellationToken = default);

    /// <summary>Forgets every tour this store knows about.</summary>
    ValueTask ClearAsync(CancellationToken cancellationToken = default);
}
