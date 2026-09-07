# BlazorOnboarding

A native Blazor onboarding and user-guidance library: product tours, spotlights, tooltips,
contextual hints and full application walkthroughs, written in C# and Razor.

It is not a wrapper. There is no JavaScript tour engine underneath - the browser layer is a single
small module that measures elements, watches them for movement, scrolls, and routes keyboard events.
Every decision about what to show, when to show it, and where to put it is made in C#.

```razor
<OnboardingTour @ref="tour" Id="welcome" AutoStart="OnboardingAutoStart.Once">
    <OnboardingStep Anchor="new-project"
                    Title="Create a project"
                    Description="Start by creating your first project." />

    <OnboardingStep Anchor="settings"
                    Title="Customise your workspace"
                    Description="Configure your preferences here." />
</OnboardingTour>
```

```razor
<button data-bo-anchor="new-project">New project</button>
```

## Highlights

- **Multi-step tours** with progress indicators, keyboard navigation and resumable state.
- **Collision-aware positioning** computed in C#: shift before flip, arrow clamping, viewport
  padding, and sane behaviour for targets larger than the screen.
- **Elements that are not there yet** - wait, skip, centre or fail, per step, driven by a mutation
  observer rather than a polling timer.
- **Interactive steps** that advance when the user actually uses the highlighted control.
- **Conditional and branching flows**, where Back follows the path the user took.
- **Route-aware tours** that span pages, and that wait rather than fighting the user for the address
  bar.
- **Accessibility as architecture**: dialog semantics, focus trap and restoration, live-region
  announcements, reduced motion, forced colors.
- **Theming through CSS variables**, per-step templates, or a fully headless popover that still gets
  measurement, placement and focus management.
- **Analytics-grade events**, including how long each step was on screen.
- Works unchanged on **Blazor Server, Blazor WebAssembly and Blazor Web App**, including while
  prerendering.
- No external CSS framework, and no JavaScript dependency beyond the library's own module.

## Install

```bash
dotnet add package BlazorOnboarding
```

Register the services:

```csharp
builder.Services.AddBlazorOnboarding();
```

Reference the stylesheet from your host page:

```html
<link rel="stylesheet" href="_content/BlazorOnboarding/onboarding.css" />
```

Add one host component, usually in your main layout:

```razor
<OnboardingRoot />
```

Full instructions, including Blazor Web App projects where services must be registered in both
projects, are in [docs/installation.md](docs/installation.md).

## Documentation

| | |
|---|---|
| [Getting started](docs/getting-started.md) | Your first tour, end to end |
| [Installation](docs/installation.md) | Every hosting model, including prerendering |
| [Basic tours](docs/basic-tours.md) | Declarative and programmatic tours, targeting, actions |
| [Highlighting & contextual help](docs/highlighting.md) | Spotlights, single hints, interaction modes |
| [Dynamic & async targets](docs/dynamic-targets.md) | Waiting, missing-target strategies, run-time targets |
| [Conditional & branching flows](docs/conditional-branching.md) | Gates, forks, validation, history |
| [Multi-page tours](docs/multi-page-tours.md) | Routes, navigation, resuming across pages |
| [Persistence](docs/persistence.md) | Stores, resume, versioning, server-side storage |
| [Localization & RTL](docs/localization-rtl.md) | Labels, localizers, right-to-left layouts |
| [Accessibility](docs/accessibility.md) | What is provided, and what you still own |
| [Theming](docs/theming.md) | CSS variables, dark mode, per-tour skins |
| [Headless & custom rendering](docs/headless.md) | Templates and complete UI replacement |
| [Events & analytics](docs/events-analytics.md) | The event stream and observers |
| [Performance](docs/performance.md) | What it costs, and how to keep it cheap |
| [API reference](docs/api-reference.md) | Every public type and member |

## The demo

```bash
dotnet run --project samples/BlazorOnboarding.Demo/BlazorOnboarding.Demo
```

The demo covers placement and spotlights, dynamic and missing targets, conditional and branching
flows, a tour spanning two routes, theming and RTL, headless rendering, and a live event log.

## Requirements

- .NET 10 or later
- Blazor Server, Blazor WebAssembly, or Blazor Web App (Server, WebAssembly or Auto)

## Repository layout

```
src/BlazorOnboarding          The library
tests/BlazorOnboarding.Tests  Engine, layout and component tests
samples/BlazorOnboarding.Demo Demo application
docs/                         Documentation
```

## Licence

MIT. See [LICENSE](LICENSE).
