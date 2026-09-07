# Getting started

This walks through a real first tour: marking targets, declaring steps, starting it once per user,
and letting people replay it from a help menu.

If you have not installed the package yet, start with [installation](installation.md).

## 1. Mark the things you want to point at

A step needs to know which element it is about. The most robust way is an attribute on the element:

```razor
<button class="primary" data-bo-anchor="new-project">New project</button>

<input type="search" data-bo-anchor="search" placeholder="Search" />

<nav data-bo-anchor="sidebar"> ... </nav>
```

`data-bo-anchor` is just an attribute. It survives re-renders, works on elements that do not exist
yet, and does not require you to hold on to an `ElementReference`.

If you prefer, there is a helper that splats the same attribute:

```razor
<button @attributes="Onboarding.Anchor("new-project")">New project</button>
```

CSS selectors and captured element references work too - see [basic tours](basic-tours.md#targeting).

## 2. Declare the tour

```razor
@page "/"

<OnboardingTour @ref="_tour" Id="welcome" Title="Welcome tour">

    <OnboardingStep Id="intro"
                    Title="Welcome aboard"
                    Description="A quick look around. Press Escape at any point to leave." />

    <OnboardingStep Id="create"
                    Anchor="new-project"
                    Title="Create a project"
                    Description="Everything starts here."
                    Placement="Placement.Bottom" />

    <OnboardingStep Id="search"
                    Anchor="search"
                    Title="Find anything"
                    Description="Search across every project you can see." />

    <OnboardingStep Id="done"
                    Title="That is it"
                    Description="You can replay this from the help menu."
                    DoneLabel="Finish" />

</OnboardingTour>
```

Some things worth noticing:

- **The first and last steps have no target.** A step without one is shown as a centred card, which
  is the right shape for a welcome or a summary.
- **Every step has an explicit `Id`.** Ids are optional, but they are what makes resuming, branching
  and analytics stable when you later insert a step.
- `OnboardingTour` renders no markup of its own. The `OnboardingRoot` in your layout displays it.

## 3. Start it

Once per user, automatically:

```razor
<OnboardingTour Id="welcome" AutoStart="OnboardingAutoStart.Once">
```

`Once` checks the store first and does nothing if the user already completed or dismissed the tour.
It starts after the first render, so it is safe under prerendering.

Or start it yourself, which is what you want when the tour should follow some application event:

```razor
@inject IOnboardingService Onboarding

<button @onclick="StartAsync">Show me around</button>

@code {
    private OnboardingTour? _tour;

    private Task StartAsync() => _tour!.StartAsync();
}
```

`IOnboardingService` is the general entry point:

```csharp
await Onboarding.StartAsync(tour);          // always start, resuming saved progress
await Onboarding.StartOnceAsync(tour);      // only if not already completed or dismissed
await Onboarding.RestartAsync(tour);        // forget progress and start from the top
```

## 4. Let people replay it

Because a tour is registered by id, a help menu anywhere in the application can start it:

```razor
@inject IOnboardingService Onboarding

<button @onclick='() => Onboarding.StartAsync("welcome")'>Replay the tour</button>
```

## 5. Know how it went

```csharp
builder.Services.AddOnboardingObserver<TourAnalytics>();
```

```csharp
public sealed class TourAnalytics(IAnalyticsClient client) : IOnboardingObserver
{
    public ValueTask OnEventAsync(OnboardingEvent e, CancellationToken ct = default)
        => e.Kind switch
        {
            OnboardingEventKind.StepShown =>
                client.TrackAsync("tour_step", new { tour = e.Tour.Id, step = e.Step?.Id }),

            OnboardingEventKind.TourCompleted or OnboardingEventKind.TourSkipped =>
                client.TrackAsync("tour_ended", new { tour = e.Tour.Id, reason = e.Reason }),

            _ => ValueTask.CompletedTask,
        };
}
```

See [events and analytics](events-analytics.md) for the full stream, including per-step durations.

## The same tour, as data

The declarative components build a `TourDefinition`. Building one directly is equally supported, and
is what you want when tours come from configuration, a CMS or a database:

```csharp
var tour = new TourDefinition
{
    Id = "welcome",
    Title = "Welcome tour",
    Steps =
    {
        new StepDefinition
        {
            Id = "intro",
            Title = "Welcome aboard",
            Description = "A quick look around.",
        },
        new StepDefinition
        {
            Id = "create",
            Target = StepTarget.Anchor("new-project"),
            Placement = Placement.Bottom,
            Title = "Create a project",
            Description = "Everything starts here.",
        },
    },
};

await Onboarding.StartOnceAsync(tour);
```

## Where to go next

- [Basic tours](basic-tours.md) - targeting, buttons, per-step options
- [Dynamic targets](dynamic-targets.md) - elements that render late or never
- [Conditional and branching flows](conditional-branching.md) - different tours for different users
- [Theming](theming.md) - make it look like your product
