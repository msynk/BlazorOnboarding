# Headless and custom rendering

There are three levels of customisation. Reach for the smallest one that does the job.

| Level | How | Use when |
|---|---|---|
| Restyle | [CSS variables](theming.md) | The layout is right, the colours are not |
| Recompose | `Header`, `Body`, `Footer` on a step | One step needs a form, an image, or different buttons |
| Replace | `OnboardingRoot.ChildContent`, or a tour or step `Template` | The card should look nothing like the default |

## What stays yours, and what stays the library's

Even at the "replace" level, the library keeps the popover **element**: a `position: fixed` div that
it measures, positions, gives dialog semantics, focuses and traps focus within. You supply what goes
inside it.

That split is deliberate. Collision-aware placement, the resize and scroll observers, focus
restoration and the live region are the parts that are genuinely hard to get right, and
reimplementing them in every application is not a reasonable ask. Everything above them is markup,
which is exactly what Razor is for.

## Replacing the popover contents

```razor
<OnboardingRoot Unstyled="true" PopoverClass="my-card">
    <ChildContent>
        <h2>@context.Title</h2>
        <p>@context.Description</p>

        <footer>
            <button @onclick="context.CloseAsync">Close</button>
            @if (!context.IsFirst)
            {
                <button @onclick="context.PreviousAsync">@context.PreviousLabel</button>
            }
            <button data-bo-autofocus @onclick="context.NextAsync">@context.NextLabel</button>
        </footer>
    </ChildContent>
</OnboardingRoot>
```

`Unstyled="true"` drops the built-in `.bo-popover` class so your own CSS starts from nothing. Leave
it off to keep the default card as a base and add to it.

`data-bo-autofocus` marks the control that should receive focus when a step is shown.

## The render context

Every template receives a `StepRenderContext`:

```csharp
context.Session        // the running ITourSession
context.Step           // the StepDefinition
context.Options        // fully resolved EffectiveStepOptions for this step
context.Labels         // the label set in force

context.Title          // localized
context.Description    // localized
context.Index          // index in TourDefinition.Steps
context.Position       // 1-based, counting only steps that will be shown
context.Count
context.IsFirst / IsLast / IsWaiting
context.TargetRect     // last measured target rectangle
context.Placement      // side, alignment, arrow offset

context.NextLabel      // already switched to the done label on the last step
context.PreviousLabel
context.SkipLabel
context.ProgressText   // "2 of 5", formatted from the labels

await context.NextAsync();
await context.PreviousAsync();
await context.SkipAsync();
await context.CloseAsync();
await context.CompleteAsync();
await context.GoToAsync("billing");
await context.InvokeAsync(action);
```

## Per-step and per-tour templates

Templates are resolved most-specific first: step `Template`, then tour `Template`, then the host's
`ChildContent`, then the default chrome.

```csharp
new StepDefinition
{
    Template = context => builder => { /* this step only */ },
}
```

In markup, a step's `ChildContent` is its body, leaving the default header and footer in place — the
common case:

```razor
<OnboardingStep Anchor="plan" Title="Choose a plan">
    <PlanPicker Selected="_plan" SelectedChanged="OnPlanChanged" />
</OnboardingStep>
```

```csharp
private async Task OnPlanChanged(string plan)
{
    _plan = plan;
    await _tour!.Session!.NextAsync();
}
```

`Header` and `Footer` replace those regions individually:

```razor
<OnboardingStep Anchor="save" Title="Save your work">
    <Footer>
        <div class="my-footer">
            <span>@context.ProgressText</span>
            <button @onclick="context.NextAsync">@context.NextLabel</button>
        </div>
    </Footer>
</OnboardingStep>
```

Note that replacing the footer removes the close button along with everything else, so make sure
there is still a way out — or leave `CloseOnEscape` on.

## Replacing the overlay

```razor
<OnboardingRoot>
    <OverlayTemplate>
        @if (context.TargetRect is { } rect)
        {
            <svg class="my-overlay" width="100%" height="100%">
                <defs>
                    <mask id="hole">
                        <rect width="100%" height="100%" fill="white" />
                        <rect x="@rect.X" y="@rect.Y" width="@rect.Width" height="@rect.Height"
                              rx="12" fill="black" />
                    </mask>
                </defs>
                <rect width="100%" height="100%" fill="rgba(0,0,0,0.6)" mask="url(#hole)" />
            </svg>
        }
    </OverlayTemplate>
</OnboardingRoot>
```

`OverlayTemplate` replaces the visual layer only. The pointer blockers that implement `Interaction`
are rendered separately and keep working, so a custom overlay does not have to reimplement
click-through.

## Replacing the waiting state

```razor
<OnboardingRoot>
    <WaitingTemplate>
        <MySkeleton Text="Loading the next part of the page" />
    </WaitingTemplate>
</OnboardingRoot>
```

## Several hosts

A host given an explicit `Session` **claims** it, and the application's main host stops rendering
that session. This is what lets one page take over the presentation of one tour while the layout
keeps handling everything else:

```razor
@* Layout: handles every tour *@
<OnboardingRoot />
```

```razor
@* One page: renders its own tour with a custom card *@
<OnboardingRoot Session="_session" Unstyled="true">
    <ChildContent> ... </ChildContent>
</OnboardingRoot>

@code {
    private ITourSession? _session;

    private async Task StartAsync()
    {
        _session = await Onboarding.StartAsync(tour);
        StateHasChanged();
    }
}
```

The claim is released when the host is disposed, so navigating away hands the session back to the
layout rather than leaving it invisible.

## Building the UI from scratch

If you want no rendering from the library at all, do not place an `OnboardingRoot`. Drive
`IOnboardingService` and `ITourSession` directly and render whatever you like from
`session.CurrentStep`, `session.Placement` and `session.TargetRect`, subscribing to `session.Changed`
to re-render.

The trade-off is real, and worth stating plainly: without the host, nothing calls
`SetPopoverElementAsync`, so the engine never learns your popover's size and `session.Placement`
stays `null`. You would be positioning it yourself. `PlacementEngine.Place` is public and pure, so
you can still use the same algorithm:

```csharp
var result = PlacementEngine.Place(new PlacementRequest
{
    Target = session.TargetRect!.Value.Inflate(8),
    Popover = new Size(320, 180),
    Viewport = new Rect(0, 0, viewportWidth, viewportHeight),
    Preferred = Placement.Bottom,
    Offset = 12,
    ViewportPadding = 16,
    ArrowSize = 10,
    CornerRadius = 12,
    HasTarget = true,
});
```

For almost every application, `Unstyled="true"` with your own `ChildContent` is the better trade: the
same visual freedom, none of the plumbing.
