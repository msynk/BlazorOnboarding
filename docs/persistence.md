# Persistence

The library records, per tour, whether the user finished it and where they got to. That is what makes
"show this once", "resume where I left off" and "replay from the help menu" all work.

## What is stored

```csharp
public sealed record OnboardingRecord
{
    public required string TourId { get; init; }
    public int Version { get; init; }
    public string? CurrentStepId { get; init; }
    public int CurrentIndex { get; init; }
    public bool Completed { get; init; }
    public bool Dismissed { get; init; }
    public IReadOnlyList<string> VisitedStepIds { get; init; }
    public DateTimeOffset UpdatedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public IReadOnlyDictionary<string, string>? Data { get; init; }

    public bool IsClosed => Completed || Dismissed;
}
```

A record is written after every step, and again when the tour ends.

## How a tour ends matters

| Ending | `Completed` | `Dismissed` | Offered again by `StartOnceAsync` |
|---|---|---|---|
| `CompleteAsync` | yes | no | no |
| `SkipAsync` | no | yes | no |
| `DismissAsync` | no | yes | no |
| `CancelAsync` | no | no | **yes** |
| Failed | no | no | yes |

`CancelAsync` is the "interrupted, not refused" ending. Starting another tour cancels the running one
rather than dismissing it, so being interrupted never counts as the user turning a tour down.

## Resuming

`StartAsync` resumes by default:

```csharp
await Onboarding.StartAsync(tour);                                  // resumes
await Onboarding.StartAsync(tour, new StartOptions { Resume = false });  // starts at step one
```

Resuming prefers the saved **step id** and falls back to the index, because inserting a step shifts
every index after it. History entries pointing at steps that no longer exist are dropped, so Back
cannot land on a ghost.

A record whose `IsClosed` is true is not resumed into: a user who finished or dismissed a tour and
then explicitly starts it again wants the beginning.

## Versioning

Bump `TourDefinition.Version` whenever you edit the steps in a way that changes what a saved position
means:

```csharp
new TourDefinition { Id = "welcome", Version = 2, ... }
```

A record written by an older version is discarded rather than resumed into, and
`IsCompletedAsync` / `IsClosedAsync` report `false`, so `StartOnceAsync` offers the updated tour
again.

## Turning it off

```csharp
new TourDefinition { Id = "playground", Persist = false }             // one tour
builder.Services.AddBlazorOnboarding(o => o.Persist = false);         // everything
```

Transient hints, playgrounds and previews are usually better off unpersisted.

## Where it is stored

The default is `BrowserOnboardingStore`: one `localStorage` entry per tour, keyed by
`OnboardingOptions.StorageKeyPrefix` (default `blazor-onboarding:`), with an in-memory mirror.

The mirror matters more than it sounds:

- during prerendering there is no browser, so reads come from memory instead of throwing;
- in private browsing, or with site data blocked, writes fail silently and reads still work for the
  rest of the session;
- corrupt or hand-edited JSON is discarded and treated as "never seen this tour" rather than crashing.

`ClearAsync` only removes keys carrying the configured prefix. The rest of `localStorage` belongs to
your application.

### Memory only

```csharp
builder.Services.AddBlazorOnboarding();
builder.Services.AddInMemoryOnboardingStore();
```

### Server-side

Storing progress against the user's profile is usually what a real product wants: it follows people
across devices and survives a cleared browser.

```csharp
public sealed class ProfileOnboardingStore(IUserProfiles profiles, ICurrentUser user) : IOnboardingStore
{
    public ValueTask<OnboardingRecord?> GetAsync(string tourId, CancellationToken ct = default)
        => profiles.GetOnboardingAsync(user.Id, tourId, ct);

    public ValueTask SetAsync(OnboardingRecord record, CancellationToken ct = default)
        => profiles.SaveOnboardingAsync(user.Id, record, ct);

    public ValueTask RemoveAsync(string tourId, CancellationToken ct = default)
        => profiles.DeleteOnboardingAsync(user.Id, tourId, ct);

    public ValueTask ClearAsync(CancellationToken ct = default)
        => profiles.DeleteAllOnboardingAsync(user.Id, ct);
}
```

```csharp
builder.Services.AddBlazorOnboarding();
builder.Services.AddOnboardingStore<ProfileOnboardingStore>();
```

The store is resolved as a scoped service, so injecting the current user is fine.

Writes happen on every step transition. If yours is a network call, consider debouncing or
batching inside the store; the engine never blocks a transition on the result, and a store that
throws a transient failure is logged rather than allowed to break the tour.

## Querying and resetting

```csharp
await Onboarding.IsCompletedAsync("welcome");   // ran to the end at some point
await Onboarding.IsClosedAsync("welcome");      // completed, skipped or dismissed
await Onboarding.GetRecordAsync("welcome");     // the whole record, or null

await Onboarding.ResetAsync("welcome");         // forget one tour
await Onboarding.ResetAllAsync();               // forget every tour

await Onboarding.RestartAsync(tour);            // reset, then start from the beginning
```

A "replay the tours" button in a settings page is `ResetAllAsync` followed by a reload.
