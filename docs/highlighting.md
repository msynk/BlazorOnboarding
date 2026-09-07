# Highlighting and contextual help

Not everything is a tour. A single step is a perfectly good hint, and the same engine powers both.

## A one-step hint

```csharp
await Onboarding.StartAsync(new TourDefinition
{
    Id = "export-hint",
    Persist = false,
    Steps =
    {
        new StepDefinition
        {
            Target = StepTarget.Anchor("export"),
            Title = "Exports live here now",
            Description = "The old menu item has moved.",
            Progress = ProgressStyle.None,
            ShowSkip = false,
            ShowPrevious = false,
            DoneLabel = "Got it",
        },
    },
});
```

A one-step tour is automatically on its last step, so the forward button reads `DoneLabel` and
finishing it completes the tour.

## A feature announcement

Announcements usually want to be centred, shown once, and to not dim the page too heavily:

```csharp
await Onboarding.StartOnceAsync(new TourDefinition
{
    Id = "whats-new-2024-03",
    Version = 1,
    OverlayOpacity = 0.35,
    Steps =
    {
        new StepDefinition
        {
            Title = "Three new things",
            Description = "Saved views, bulk editing, and a much faster search.",
            DoneLabel = "Have a look",
        },
    },
});
```

`StartOnceAsync` never shows it twice. Bump `Version` when the announcement changes and it will be
offered again.

## The spotlight

The spotlight is a transparent box whose outer shadow does the dimming, so the cut-out stays sharp at
any zoom level and animates smoothly between steps.

```csharp
new StepDefinition
{
    ShowSpotlight = true,               // false dims without a cut-out
    SpotlightShape = SpotlightShape.Rounded,   // Rounded, Rectangle, Circle, None
    SpotlightPadding = 8,               // breathing room around the target
    SpotlightRadius = 10,               // corner radius, ignored by Circle and Rectangle
}
```

`SpotlightPadding` also grows the rectangle the popover is positioned against, so the gap you see
between the highlight and the popover is `SpotlightPadding + Offset`.

To dim without highlighting anything, set `SpotlightShape = SpotlightShape.None`. To highlight
without dimming, keep the spotlight and set `ShowOverlay = false`; the ring around the target
remains.

## Interaction modes

`InteractionMode` decides how much of the page the user can still touch.

| Mode | Behaviour |
|---|---|
| `Blocked` (default) | A full-screen blocker swallows every pointer event outside the popover. The dialog is `aria-modal="true"`. |
| `TargetOnly` | Four blockers surround the spotlight, leaving the hole click-through. The user can operate the highlighted control and nothing else. |
| `Free` | No blockers at all. The whole page stays usable, and the popover floats above it. |

`TargetOnly` and `Free` render the dialog as `aria-modal="false"`, because the rest of the page is
genuinely still available to a screen reader user.

```csharp
new StepDefinition
{
    Target = StepTarget.Anchor("filters"),
    Title = "Try a filter",
    Interaction = InteractionMode.TargetOnly,
}
```

## Let the user do it

An interactive step waits for a real interaction rather than a Next button:

```csharp
new StepDefinition
{
    Target = StepTarget.Anchor("new-project"),
    Title = "Now create your first project",
    Description = "Go ahead and press it.",
    AdvanceOn = AdvanceTrigger.TargetClick,
    ShowNext = false,
}
```

Declaring `AdvanceOn` switches the step to `TargetOnly` automatically unless you set `Interaction`
yourself, because a step asking the user to click something has to let the click through.

The trigger can listen for any DOM event, on any element, and can filter delegated events:

```csharp
AdvanceOn = new AdvanceTrigger
{
    EventName = "change",
    Selector = "#plan-picker",       // null listens on the step's own target
    MatchSelector = "[data-plan]",   // only when the event came from a matching element
    DelayMs = 250,                   // let the UI settle before advancing
}
```

The listener fires once and is then removed, since the step is about to change anyway.

## Scrolling

Off-screen targets are scrolled into view before the step is shown:

```csharp
new StepDefinition
{
    Scroll = ScrollMode.Smooth,   // Smooth (default), Instant, Auto, None
    ScrollPadding = 24,           // margin kept around the target
}
```

Scrolling walks every scrollable ancestor, so nested panes, dialogs and virtualized lists all work.
A target that is already comfortably visible is left alone rather than being re-centred, and a smooth
scroll is allowed to settle before the first measurement is taken so the popover does not appear
mid-flight.

## Closing behaviour

```csharp
new StepDefinition
{
    CloseOnEscape = true,          // default
    CloseOnOverlayClick = false,   // default
}
```

Escape is handled at the document level, so it works regardless of where focus is. Keep it on unless
the tour is genuinely mandatory; it is the escape hatch users reach for first.

## Very large and very small targets

Both are handled without special configuration:

- A target **larger than the viewport** is still measured as one rectangle. The popover is placed
  against whichever part is on screen and clamped inside the viewport padding.
- A target **smaller than the popover's corner radius** would leave the arrow pointing past its edge,
  so the arrow is clamped to stay within both the target and the popover's straight edge.
- When the popover ends up with no cross-axis overlap with the target at all, the arrow is hidden
  rather than pointing at nothing.
