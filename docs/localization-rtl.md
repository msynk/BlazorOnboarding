# Localization and RTL

## Labels

Every string the default UI renders comes from an `OnboardingLabels` instance.

```csharp
public sealed class OnboardingLabels
{
    public string Next { get; set; } = "Next";
    public string Previous { get; set; } = "Back";
    public string Skip { get; set; } = "Skip tour";
    public string Done { get; set; } = "Done";
    public string Close { get; set; } = "Close";

    public string DialogLabel { get; set; } = "Product tour";
    public string ProgressFormat { get; set; } = "{0} of {1}";
    public string StepAnnouncementFormat { get; set; } = "Step {0} of {1}. {2}";
    public string WaitingAnnouncement { get; set; } = "Waiting for the next part of the page.";
    public string ProgressLabel { get; set; } = "Tour progress";
}
```

`DialogLabel`, `StepAnnouncementFormat`, `WaitingAnnouncement` and `ProgressLabel` are read by
assistive technology rather than shown on screen. Translate them: they are the only thing a screen
reader user hears.

### Globally

```csharp
builder.Services.AddBlazorOnboarding(options =>
{
    options.Labels.Next = "Weiter";
    options.Labels.Previous = "Zurück";
    options.Labels.Skip = "Überspringen";
    options.Labels.Done = "Fertig";
});
```

### Per tour

```csharp
new TourDefinition
{
    Id = "welcome",
    Labels = new OnboardingLabels { Next = "Weiter", Done = "Fertig" },
}
```

A tour's labels replace the global set wholesale rather than merging. `Clone()` gives you a copy of
an existing set to adjust, so changing one string does not mean restating the rest:

```csharp
var labels = OnboardingLabels.Default.Clone();
labels.Next = "Weiter";

var tour = new TourDefinition { Id = "welcome", Labels = labels };
```

### Per step

```csharp
new StepDefinition
{
    NextLabel = "Show me",
    PreviousLabel = "Go back",
    SkipLabel = "Not now",
    DoneLabel = "Finish setup",
}
```

## Using `IStringLocalizer`

Register your own `IOnboardingLocalizer` to source labels from resource files or a translation
service:

```csharp
public sealed class ResourceOnboardingLocalizer(IStringLocalizer<Tours> localizer) : IOnboardingLocalizer
{
    public OnboardingLabels GetLabels(TourDefinition? tour) => new()
    {
        Next = localizer["Tour_Next"],
        Previous = localizer["Tour_Back"],
        Skip = localizer["Tour_Skip"],
        Done = localizer["Tour_Done"],
        Close = localizer["Tour_Close"],
        DialogLabel = localizer["Tour_DialogLabel"],
        ProgressFormat = localizer["Tour_Progress"],
        StepAnnouncementFormat = localizer["Tour_Announcement"],
        WaitingAnnouncement = localizer["Tour_Waiting"],
        ProgressLabel = localizer["Tour_ProgressLabel"],
    };

    // Lets step titles and descriptions be resource keys rather than literals.
    public string? Localize(string? text) => text is null ? null : localizer[text];
}
```

```csharp
builder.Services.AddBlazorOnboarding();
builder.Services.AddOnboardingLocalizer<ResourceOnboardingLocalizer>();
```

`Localize` is applied to step titles and descriptions before they are rendered. The default
implementation returns the value unchanged, so tours written with literal text keep working.

Labels are resolved when a step is activated, so a tour that is running when the culture changes
picks up the new strings on its next step. To re-render the current step immediately, call
`session.RefreshAsync()`.

## Right-to-left

```csharp
new TourDefinition { Direction = OnboardingDirection.RightToLeft }
```

`Direction` is one of:

| | |
|---|---|
| `Auto` (default) | Follows the document's computed direction, sampled from the browser. |
| `LeftToRight` | Forced. |
| `RightToLeft` | Forced. |

`Auto` is usually right: set `dir="rtl"` on your `<html>` element as you already would, and tours
follow.

Direction changes more than text alignment:

- **`Start` and `End` alignments mirror.** `Placement.BottomStart` aligns to the target's right edge
  in RTL.
- **Arrow keys swap.** In an RTL tour, Left advances and Right goes back, matching reading order.
- **Layout uses logical properties.** Padding, margins and the footer's button order all flip.

The root element carries `dir="rtl"`, so anything you render inside a template inherits it.

## Mixed-direction applications

A page can be LTR while one tour is RTL, or the reverse. Set `Direction` on the tour rather than
relying on `Auto`, and the popover's own `dir` attribute keeps its contents consistent regardless of
the page around it.

## Number and date formatting

`ProgressFormat` and `StepAnnouncementFormat` are passed through `string.Format` with the current
culture, so digits are localized the way the rest of your application formats them.

Everything the library generates for CSS — positions, sizes, opacities, durations — is formatted with
the invariant culture. A comma decimal separator would produce silently broken styles, and that is a
bug that only shows up for users in another locale.
