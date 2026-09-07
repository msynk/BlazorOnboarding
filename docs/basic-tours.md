# Basic tours

## Two ways to define a tour

### Declarative

`OnboardingTour` collects its `OnboardingStep` children, in render order, into a `TourDefinition`
and registers it with the service. Neither component renders any markup.

```razor
<OnboardingTour @ref="_tour" Id="welcome" Title="Welcome" Version="1">
    <OnboardingStep Anchor="new-project" Title="Create a project" />
    <OnboardingStep Anchor="search" Title="Find anything" />
</OnboardingTour>
```

The definition is rebuilt whenever parameters change, and the same object is kept, so editing a
running tour is picked up without restarting it.

Steps normally keep their declaration order. Give a step an explicit `Order` when it is rendered
conditionally and you need its position pinned:

```razor
<OnboardingStep Order="10" ... />
```

### Programmatic

```csharp
var tour = new TourDefinition
{
    Id = "welcome",
    Steps =
    {
        new StepDefinition { Target = StepTarget.Anchor("new-project"), Title = "Create a project" },
        new StepDefinition { Target = "#search", Title = "Find anything" },
    },
};
```

Use this when tours are data: loaded from configuration, generated per tenant, or assembled from a
database.

## Targeting

A step's `Target` is a `StepTarget`. There are five kinds.

| | |
|---|---|
| `StepTarget.Anchor("name")` | The element carrying `data-bo-anchor="name"`. **Preferred.** |
| `StepTarget.Css("#id .cls")` | The first element matching a CSS selector. |
| `StepTarget.Element(reference)` | A captured `ElementReference`. |
| `StepTarget.Dynamic(resolver)` | Decided when the step activates. See [dynamic targets](dynamic-targets.md). |
| `StepTarget.None` | No element: a centred card. The default. |

Strings and element references convert implicitly, so all of these are equivalent to explicit calls:

```csharp
new StepDefinition { Target = "#search" }        // a CSS selector
new StepDefinition { Target = _searchElement }   // an ElementReference
```

In markup, the `OnboardingStep` component exposes each kind as its own parameter, which reads better
than a cast:

```razor
<OnboardingStep Anchor="new-project" ... />
<OnboardingStep Selector="#search" ... />
<OnboardingStep Element="_element" ... />
<OnboardingStep Target="StepTarget.Dynamic(...)" ... />
```

### Prefer anchors over element references

An `ElementReference` is captured for one particular DOM node. If Blazor re-renders and replaces that
node, the reference is stale. An anchor is re-queried on every measurement, so it survives
re-rendering, virtualization and elements that appear later. Selectors have the same advantage.

### Highlighting several elements at once

`AdditionalTargets` folds more elements into the same spotlight. The popover is positioned against
the bounding union:

```csharp
new StepDefinition
{
    Target = StepTarget.Anchor("toolbar-save"),
    AdditionalTargets = [StepTarget.Anchor("toolbar-undo"), StepTarget.Anchor("toolbar-redo")],
    Title = "Editing controls",
}
```

## Steps without a target

A step with no target is centred in the viewport with no spotlight and no arrow. This is the right
shape for a welcome, a summary, a mid-tour explanation, or a feature announcement:

```csharp
new StepDefinition
{
    Title = "What is new in March",
    Description = "Three things worth two minutes of your time.",
}
```

## The buttons

The default footer shows, in order: progress, skip, back, your custom actions, and the forward
button. Each part can be turned off per step, per tour or globally:

```csharp
new StepDefinition
{
    ShowSkip = false,
    ShowPrevious = false,
    ShowClose = false,
    Progress = ProgressStyle.None,
    NextLabel = "Got it",
}
```

`Back` is hidden on the first step and the forward button reads `DoneLabel` on the last one, both
automatically.

### Custom actions

`Actions` adds buttons. Each has a handler, an effect, or both:

```csharp
new StepDefinition
{
    Title = "Invite your team?",
    ShowNext = false,
    Actions =
    [
        new StepAction
        {
            Label = "Invite people",
            Style = StepActionStyle.Primary,
            OnClick = ctx => ctx.Services.GetRequiredService<IDialogs>().ShowInviteAsync(),
            Effect = StepActionEffect.Next,
        },
        new StepAction
        {
            Label = "Later",
            Style = StepActionStyle.Ghost,
            Effect = StepActionEffect.Next,
        },
    ],
}
```

The handler runs first and is awaited; the effect is applied afterwards, and is skipped if the
handler already ended the tour or moved to another step. Effects are `None`, `Next`, `Previous`,
`GoTo` (with `TargetStepId`), `Complete`, `Skip` and `Dismiss`.

Set `AutoFocus` on an action to make it, rather than the forward button, the control that receives
focus when the step appears.

## Driving a tour

```csharp
await session.NextAsync();
await session.PreviousAsync();
await session.GoToAsync("billing");   // by id
await session.GoToAsync(3);           // by index

await session.CompleteAsync();        // finished, recorded as completed
await session.SkipAsync();            // user skipped the rest
await session.DismissAsync();         // user closed it
await session.CancelAsync();          // interrupted; progress kept for next time

await session.PauseAsync();           // hide, keep the position
await session.ResumeAsync();

await session.RefreshAsync();         // re-measure after your own layout change
```

`CancelAsync` is the one worth knowing about: unlike dismissing, it does not record that the user
turned the tour down, so `StartOnceAsync` will offer it again and `StartAsync` will resume it.

### Reading state

```csharp
session.Status            // Idle, Running, Waiting, Paused, Completed, Skipped, Dismissed, Cancelled, Failed
session.CurrentStep       // the StepDefinition on screen
session.DisplayPosition   // 1-based, counting only steps that will actually be shown
session.DisplayCount
session.IsFirstStep / IsLastStep
session.History           // ids of the steps actually visited
session.State             // a per-session dictionary shared by every hook and template
```

Subscribe to `session.Changed` to re-render your own UI alongside a tour.

## Lifecycle hooks

```csharp
new StepDefinition
{
    OnBeforeShow = async ctx => await ctx.Services.GetRequiredService<IDataLoader>().PreloadAsync(),
    OnShown      = ctx => { Log("shown", ctx.Step.Id); return ValueTask.CompletedTask; },
    OnBeforeLeave = ctx => ValidateAsync(ctx),   // throwing cancels the transition
}
```

```csharp
new TourDefinition
{
    CanStart = ctx => ValueTask.FromResult(user.IsInRole("Member")),
    OnStarted = ctx => ValueTask.CompletedTask,
    OnStepChanged = args => ValueTask.CompletedTask,
    OnEnded = args => ValueTask.CompletedTask,
}
```

Every hook receives a `StepContext` (or `TourContext`) carrying the session, the step, the service
provider and a cancellation token that fires when the step is left.

Throwing from a hook fails the tour cleanly: the session ends with `TourEndReason.Failed`, `Error` is
populated, `OnEnded` still runs, and everything is torn down. It never escapes as an unobserved
exception.

## Running more than one tour

Starting a tour cancels any other running one by default. To run several at once - a tour plus a
persistent hint, say - opt out:

```csharp
await Onboarding.StartAsync(hint, new StartOptions { StopOthers = false });
```

Each session is independent. A single `OnboardingRoot` renders `Onboarding.Active`; to show a second
one, give another host an explicit `Session`. See [headless rendering](headless.md#several-hosts).

## Start options

```csharp
await Onboarding.StartAsync(tour, new StartOptions
{
    StartAtStepId = "billing",   // begin here instead of the first step
    Resume = false,              // ignore saved progress
    Force = true,                // start even if completed, dismissed, or CanStart says no
    StopOthers = false,
    State = new Dictionary<string, object?> { ["plan"] = "pro" },
});
```
