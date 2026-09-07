# Events and analytics

Onboarding is only worth building if you can tell whether it worked. Every moment of a tour is
published as an `OnboardingEvent`, including how long each step was on screen.

## The event stream

| Kind | When |
|---|---|
| `TourStarted` | A session started |
| `TourResumed` | It resumed from saved progress rather than starting fresh |
| `StepShown` | A step is on screen |
| `StepLeft` | A step was left; `Duration` says for how long it was shown |
| `StepSkipped` | A step was skipped because its target was missing |
| `TargetMissing` | A target could not be found, or disappeared mid-step |
| `TargetResolved` | A target was found, including after a wait |
| `ActionInvoked` | A custom footer action was pressed; `Action` is populated |
| `TourPaused` | `PauseAsync` was called |
| `TourCompleted` | Ran to the end |
| `TourSkipped` | The user skipped the rest |
| `TourDismissed` | The user closed it |
| `TourCancelled` | Interrupted by the application; progress kept |
| `TourFailed` | A hook threw, or a `Fail` strategy fired; `Error` is populated |

Each event carries:

```csharp
e.Kind
e.Session      // the ITourSession
e.Tour         // shorthand for e.Session.Tour
e.Step         // the StepDefinition, when the event is about one
e.StepIndex
e.Duration     // how long the step was shown, on StepLeft
e.Action       // on ActionInvoked
e.Reason       // TourEndReason, on terminal events
e.Error        // on TourFailed
e.Timestamp
```

## Observers

The registered way to consume events is an `IOnboardingObserver`. Any number can be registered; each
receives every event.

```csharp
public sealed class TourAnalytics(IAnalyticsClient client, ILogger<TourAnalytics> logger)
    : IOnboardingObserver
{
    public async ValueTask OnEventAsync(OnboardingEvent e, CancellationToken ct = default)
    {
        switch (e.Kind)
        {
            case OnboardingEventKind.TourStarted:
                await client.TrackAsync("tour_started", new { tour = e.Tour.Id }, ct);
                break;

            case OnboardingEventKind.StepShown:
                await client.TrackAsync("tour_step_shown", new
                {
                    tour = e.Tour.Id,
                    step = e.Step?.Id,
                    position = e.Session.DisplayPosition,
                    total = e.Session.DisplayCount,
                }, ct);
                break;

            case OnboardingEventKind.StepLeft:
                await client.TrackAsync("tour_step_left", new
                {
                    step = e.Step?.Id,
                    ms = e.Duration?.TotalMilliseconds,
                }, ct);
                break;

            case OnboardingEventKind.TourCompleted:
            case OnboardingEventKind.TourSkipped:
            case OnboardingEventKind.TourDismissed:
                await client.TrackAsync("tour_ended", new
                {
                    tour = e.Tour.Id,
                    reason = e.Reason?.ToString(),
                    lastStep = e.Step?.Id,
                }, ct);
                break;

            case OnboardingEventKind.TourFailed:
                logger.LogError(e.Error, "Tour {TourId} failed at step {StepId}.", e.Tour.Id, e.Step?.Id);
                break;
        }
    }
}
```

```csharp
builder.Services.AddOnboardingObserver<TourAnalytics>();
```

An observer that throws is logged and the tour carries on. Analytics being down is never a reason for
onboarding to break.

## The quick way

For something local - a debug panel, a page that reacts to a tour - subscribe to the service:

```razor
@implements IDisposable
@inject IOnboardingService Onboarding

@code {
    protected override void OnInitialized() => Onboarding.EventRaised += OnEvent;

    public void Dispose() => Onboarding.EventRaised -= OnEvent;

    private void OnEvent(object? sender, OnboardingEvent e) => InvokeAsync(StateHasChanged);
}
```

## Per-tour and per-step callbacks

For logic that belongs to one tour rather than to your analytics pipeline:

```csharp
new TourDefinition
{
    OnStarted = ctx => ValueTask.CompletedTask,
    OnStepChanged = args => ValueTask.CompletedTask,   // Step, PreviousStep, Reason
    OnEnded = args => ValueTask.CompletedTask,         // Reason, LastStep, Error, IsSuccess
}
```

```csharp
new StepDefinition
{
    OnBeforeShow = ctx => ValueTask.CompletedTask,
    OnShown = ctx => ValueTask.CompletedTask,
    OnBeforeLeave = ctx => ValueTask.CompletedTask,
}
```

`StepChangedEventArgs.Reason` tells you how the user got there: `Start`, `Next`, `Previous`, `Jump`,
`Resume` or `Retarget` (a re-entry after a route change).

## Attaching your own dimensions

`StepDefinition.Metadata` and `.Data` ride along to every event and template:

```csharp
new StepDefinition
{
    Id = "connect-repo",
    Metadata = new Dictionary<string, object?>
    {
        ["funnel"] = "activation",
        ["required"] = true,
    },
}
```

```csharp
if (e.Step?.Metadata?.TryGetValue("funnel", out var funnel) == true)
{
    await client.TrackAsync("funnel_step", new { funnel, step = e.Step.Id });
}
```

Session-scoped values live in `e.Session.State`, which is shared by every hook and template in the
tour and is a good place for things like the branch a user chose.

## What to measure

A few things that tend to be worth the trouble:

- **Completion rate per tour.** `TourCompleted` over `TourStarted`, ignoring `TourCancelled`.
- **Drop-off by step.** The last `StepShown` before a `TourDismissed` or `TourSkipped`. This is the
  single most useful number: it tells you which step is not worth the interruption.
- **Time per step**, from `StepLeft.Duration`. A step people leave in under a second was not read.
- **`TargetMissing` counts.** A step whose target regularly goes missing is pointing at something
  that has moved or been renamed, and is quietly being skipped in production.
- **Resume rate.** `TourResumed` over `TourStarted` shows how often people come back to a tour they
  did not finish in one sitting.
