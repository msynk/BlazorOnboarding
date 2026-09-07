# BlazorOnboarding documentation

## Start here

- **[Getting started](getting-started.md)** - your first tour, end to end
- **[Installation](installation.md)** - every hosting model, prerendering, CSP

## Building tours

- **[Basic tours](basic-tours.md)** - declarative and programmatic tours, targeting, buttons, hooks
- **[Highlighting & contextual help](highlighting.md)** - spotlights, single hints, interaction modes
- **[Dynamic & async targets](dynamic-targets.md)** - waiting, missing-target strategies, run-time targets
- **[Conditional & branching flows](conditional-branching.md)** - gates, forks, validation, history
- **[Multi-page tours](multi-page-tours.md)** - routes, navigation, resuming across pages

## Making it yours

- **[Theming](theming.md)** - CSS variables, dark mode, per-tour skins
- **[Headless & custom rendering](headless.md)** - templates and complete UI replacement
- **[Localization & RTL](localization-rtl.md)** - labels, localizers, right-to-left layouts
- **[Accessibility](accessibility.md)** - what is provided, and what you still own

## Operating it

- **[Persistence](persistence.md)** - stores, resume, versioning, server-side storage
- **[Events & analytics](events-analytics.md)** - the event stream and observers
- **[Performance](performance.md)** - what it costs, and how to keep it cheap

## Reference

- **[API reference](api-reference.md)** - every public type and member

## How it fits together

```
IOnboardingService        Starts, stops and queries tours. Scoped, one per user.
   └── TourSession        The state machine: step selection, branching, waiting,
                          routing, persistence. Renders nothing.
        ├── PlacementEngine   Pure C#. Given a target rectangle, a popover size and a
        │                     viewport, decides where the popover goes.
        ├── IOnboardingStore  Where progress lives.
        └── IOnboardingInterop  The only seam to the DOM: measure, observe, scroll,
                                focus, keys. Substitutable, which is how the engine is
                                tested without a browser.

OnboardingRoot            Renders the running session. Owns the popover element so
                          measurement, placement, focus and dialog semantics stay
                          correct even when you supply the contents.
OnboardingTour / Step     Markup that builds a TourDefinition. No rendering of their own.
```

The separation is the point: the engine has no opinion about rendering, the layout engine has no
opinion about the DOM, and the browser layer has no opinion about tours.
