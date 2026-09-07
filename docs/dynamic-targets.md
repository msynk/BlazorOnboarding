# Dynamic and async targets

Real applications render late. A tour that assumes every element exists the moment it starts will
break on the first slow query. Each step declares what should happen instead.

## Missing-target strategies

```csharp
new StepDefinition
{
    Target = StepTarget.Anchor("results-table"),
    MissingTarget = MissingTargetBehavior.Wait,
    WaitTimeout = TimeSpan.FromSeconds(10),
    WaitTimeoutBehavior = MissingTargetBehavior.Skip,
}
```

| Strategy | Behaviour |
|---|---|
| `Wait` (default) | Watch the DOM until the element appears, then show the step. On timeout, apply `WaitTimeoutBehavior`. |
| `Skip` | Move silently to the next step in the direction of travel. |
| `Center` | Show the step as a centred card with no spotlight. |
| `Fail` | End the tour with `TourEndReason.Failed` and an `OnboardingException`. |

`WaitTimeoutBehavior` defaults to `Skip`. Setting it to `Wait` would spin forever, so it is treated
as `Skip`.

While waiting, the session's `Status` is `TourStatus.Waiting` and `IsWaiting` is true. The default UI
shows a small spinner; replace it with `OnboardingRoot.WaitingTemplate`.

### Which to choose

- **`Wait`** for elements that are on their way: a table behind a query, a panel behind an animation.
- **`Skip`** for steps that are nice to have. This is the safest default for tours that run across
  different plans or permissions, though a `When` condition expresses that intent more clearly.
- **`Center`** when the step's text stands on its own and pointing at the element is a bonus.
- **`Fail`** only when a missing element means something is genuinely broken and you want to know.

## How waiting works

Waiting uses a `MutationObserver` on the document, coalesced onto animation frames. There is no
polling timer, so a step waiting for ten seconds costs nothing while the page is idle.

The observer watches for added and removed nodes anywhere in the tree, plus changes to the attributes
that affect whether a target matches (`data-bo-anchor`, `id`, `class`, `hidden`, `style`). When the
element appears it is scrolled into view, the observers are attached, and the step is shown.

## Targets that are replaced, not removed

Blazor frequently discards a DOM node and renders an identical one. If the library reported that as
"the target disappeared", tours would break constantly on ordinary re-renders.

Instead, when the observed element leaves the DOM the browser layer re-queries the step's target
first. If a new element matches, it rebinds to it silently and the step never notices. Only when
nothing matches is the target reported as lost, and the step's `MissingTarget` strategy then applies
exactly as it did on activation.

This is why anchors and selectors are preferred over captured `ElementReference`s: a reference names
one specific node and cannot be re-resolved.

## Targets chosen at run time

`StepTarget.Dynamic` defers the decision until the step activates:

```csharp
new StepDefinition
{
    Target = StepTarget.Dynamic(ctx =>
        StepTarget.Anchor($"row-{ctx.State["selectedRow"]}")),
    Title = "The row you selected",
}
```

The resolver receives the full `StepContext`, so it can read the session state, the tour, and
anything in DI. It can be async:

```csharp
Target = StepTarget.Dynamic(async ctx =>
{
    var id = await ctx.Services.GetRequiredService<IProjects>().GetFirstIdAsync(ctx.CancellationToken);
    return id is null ? StepTarget.None : StepTarget.Css($"[data-project='{id}']");
})
```

Returning `null` or `StepTarget.None` makes the step a centred card. A resolver that returns another
dynamic target is resolved again, up to a small depth limit, so an accidental cycle degrades to
"no target" rather than hanging.

## Keeping up with a moving target

Once a step is showing, the browser layer keeps the popover glued to its target using:

- a `ResizeObserver` on the target, the popover and the document element;
- an `IntersectionObserver` for entering and leaving a scroll container;
- capture-phase `scroll` listeners, which catch every scrollable ancestor without having to find
  them;
- `resize` on both the window and the visual viewport, which is what mobile zoom and the on-screen
  keyboard change.

All of them feed one animation-frame callback that takes a single measurement, compares it with the
last one, and only calls into .NET when something actually moved by more than half a pixel. An idle
page costs nothing; a momentum scroll costs one interop call per frame at most.

## Virtualized lists

A row inside `Virtualize` may be recycled or removed as the user scrolls. Two things make this work:

- the target is re-queried rather than remembered, so recycling rebinds cleanly;
- `MissingTargetBehavior.Wait` combined with scrolling means a step pointing at a row that has
  scrolled out of the rendered window waits for it to come back rather than failing.

If the row may genuinely never return, prefer `Skip`.

## Refreshing manually

The observers cover layout changes the browser can see. If your own code changes geometry in a way
that produces no resize, scroll or mutation - animating a CSS transform, for instance - ask for a
fresh measurement:

```csharp
await session.RefreshAsync();
```
