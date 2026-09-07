# Multi-page tours

A step can declare the route it belongs to. The engine navigates there, waits for the page to render,
and only then looks for the target.

```csharp
new StepDefinition
{
    Id = "api-keys",
    Route = "workspace/settings",
    Target = StepTarget.Anchor("api-keys"),
    Title = "Your API keys",
}
```

Routes are matched against the base-relative path. Leading and trailing slashes, query strings and
fragments are ignored, and matching is case-insensitive, so `"/settings/"`, `"settings"` and
`"settings?tab=keys"` are all the same route.

## Wildcards

A trailing `*` turns the route into a **constraint rather than a destination**:

```csharp
new StepDefinition
{
    Route = "workspace/*",
    Title = "Somewhere under the workspace",
}
```

The step waits until the user is on a matching page, but the engine does not navigate, because there
is no single URL to navigate to. Use this for steps that apply across a section.

## Navigating away mid-tour

If the user leaves the page a step belongs to, the tour **holds its place**. The overlay is removed,
the observers are torn down, and the session moves to `TourStatus.Waiting`. When the user returns to a
matching route, the step is re-activated exactly where it was.

The library deliberately does not drag the user back. Fighting someone for control of the address bar
is never the right call, and a user who navigated away usually meant it.

To turn off automatic navigation entirely — so routes are only ever constraints — set:

```csharp
builder.Services.AddBlazorOnboarding(options => options.AutoNavigateToStepRoute = false);
```

## Surviving a full page load

Two things make this work:

- **Progress is written after every step**, so the store always knows where the user was.
- **`StartAsync` resumes by default.** Given the same tour id, it reads the record and starts at the
  saved step.

So the pattern for a tour that must survive a reload is simply to define it in one place and start it
from every page it touches:

```csharp
public static class WorkspaceTour
{
    public const string Id = "workspace";

    public static TourDefinition Create() => new()
    {
        Id = Id,
        Version = 1,
        Steps = { /* ... */ },
    };
}
```

```razor
@* Both pages: *@
<button @onclick="() => Onboarding.StartAsync(WorkspaceTour.Create())">Take the tour</button>
```

Or auto-start it once per user from your layout:

```razor
@code {
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) await Onboarding.StartOnceAsync(WorkspaceTour.Create());
    }
}
```

Because ids are what get persisted, **give every step in a multi-page tour an explicit `Id`**.
Auto-generated ids change on every construction and cannot be resumed into.

## Enhanced navigation and static rendering

Blazor's enhanced navigation replaces the page content without a full load. The library listens to
`NavigationManager.LocationChanged`, so both enhanced and full navigations are handled the same way.

On a same-page navigation, where the route requirement still matches, the engine takes a fresh
measurement instead — a query-string change usually means the content underneath the target has been
replaced.

Tours run in interactive components. A step whose target lives on a statically rendered page still
works, because targets are located by querying the DOM rather than by holding a component reference,
as long as the `OnboardingRoot` host itself is interactive.

## Where to put the host

One `OnboardingRoot` in the layout is enough, and it is the right place for a multi-page tour: a host
placed on a page would be disposed by the navigation the tour just performed.

## Choosing a route for the first step

If the first step declares a route and the user starts the tour elsewhere, the engine navigates there
before showing anything. That is usually what you want for a "start the product tour" button in a
help menu, which can then be pressed from anywhere.

If instead the tour should only be offered on the right page, leave the first step's route unset and
start it from that page.

## Waiting indefinitely

A session waiting on a route holds no observers, no listeners and no timers, so waiting forever costs
nothing. There is deliberately no timeout: the user may take a coffee break in the middle of a tour,
and quietly abandoning their progress would be worse than waiting.

If you want a bound, end the session yourself:

```csharp
await Task.Delay(TimeSpan.FromMinutes(5));
if (session.Status == TourStatus.Waiting) await session.CancelAsync();
```

`CancelAsync` keeps the saved progress, so the tour resumes next time rather than being recorded as
refused.
