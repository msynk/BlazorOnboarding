# Installation

## Package

```bash
dotnet add package BlazorOnboarding
```

Requires .NET 10 or later.

## 1. Register the services

```csharp
using BlazorOnboarding;

builder.Services.AddBlazorOnboarding();
```

Everything is registered as **scoped**, which is per-circuit on Blazor Server and per-application on
WebAssembly. That matches the lifetime of a user's onboarding state.

Global defaults can be set at the same time:

```csharp
builder.Services.AddBlazorOnboarding(options =>
{
    options.Placement = Placement.Bottom;
    options.Progress = ProgressStyle.Bar;
    options.Labels.Skip = "Not now";
    options.StorageKeyPrefix = "acme:onboarding:";
});
```

Every option is described in [the API reference](api-reference.md#onboardingoptions).

### Blazor Web App

A Blazor Web App has two projects, and a component rendered with `InteractiveServer` resolves
services from the server project while a component rendered with `InteractiveWebAssembly` resolves
them from the client project. Register in **both**, with the same configuration:

```csharp
// Server project: Program.cs
builder.Services.AddBlazorOnboarding(ConfigureOnboarding);

// Client project: Program.cs
builder.Services.AddBlazorOnboarding(ConfigureOnboarding);
```

If your tours only ever run inside components with one render mode, registering in that project
alone is enough.

## 2. Reference the stylesheet

Add this to `App.razor` (Blazor Web App), `index.html` (WebAssembly) or `_Host.cshtml`
(Blazor Server), before your own stylesheet so your overrides win:

```html
<link rel="stylesheet" href="_content/BlazorOnboarding/onboarding.css" />
```

If your project uses fingerprinted static assets, the usual form applies:

```razor
<link rel="stylesheet" href="@Assets["_content/BlazorOnboarding/onboarding.css"]" />
```

The stylesheet has no dependencies and defines no global selectors: everything is scoped under
`.bo-root`. See [theming](theming.md) to restyle it.

## 3. Add the host component

Place one `OnboardingRoot` in your layout. It renders whichever tour is running, from anywhere in the
application:

```razor
@* MainLayout.razor *@
<div class="page">
    @Body
</div>

<OnboardingRoot />
```

One host is enough for the whole application. Add more only when you deliberately want to render a
particular session yourself — see [headless rendering](headless.md).

## 4. Add `@using BlazorOnboarding`

Put it in `_Imports.razor` so the components and types are available everywhere:

```razor
@using BlazorOnboarding
```

## Prerendering

The library is prerender-safe. During prerendering there is no browser, so:

- no measurement happens and no popover is positioned;
- the default store falls back to its in-memory mirror, so reads return nothing rather than throwing;
- the engine still runs, so a tour started during prerendering does not crash the render.

The important consequence is **where you start a tour**. `OnInitializedAsync` runs during
prerendering *and* again after hydration, which would start a tour twice in two different scopes.
Start tours from `OnAfterRenderAsync(firstRender: true)`, from a user action, or with
`OnboardingTour.AutoStart`, which already does the right thing:

```razor
<OnboardingTour Id="welcome" AutoStart="OnboardingAutoStart.Once">
    ...
</OnboardingTour>
```

## Blazor Server reconnection

When a circuit drops and reconnects, Blazor creates a new scope: services, sessions and the JS module
are all rebuilt. The library treats interop failures during teardown as expected rather than as
errors, so a lost circuit never leaves a broken overlay behind or throws into your logs.

Because tour progress is written to the store after every step, a reconnected user resumes where they
were as long as persistence is on and the tour is started with `Resume` (the default).

## Content Security Policy

The library loads one ES module from your own origin (`_content/BlazorOnboarding/onboarding.js`) and
uses inline `style` attributes for positioning. A policy that allows Blazor itself will already allow
both, with the exception of a strict `style-src` that forbids `unsafe-inline`; positioning cannot be
expressed without inline styles.

## Verifying the setup

The quickest check:

```razor
@inject IOnboardingService Onboarding

<button data-bo-anchor="probe" @onclick="RunAsync">Test</button>

@code {
    private Task RunAsync() => Onboarding.StartAsync(new TourDefinition
    {
        Id = "probe",
        Persist = false,
        Steps =
        {
            new StepDefinition
            {
                Target = StepTarget.Anchor("probe"),
                Title = "It works",
                Description = "Positioning, spotlight and keyboard navigation are all live.",
            },
        },
    });
}
```

If the popover appears but is not positioned, the stylesheet is not loaded. If nothing appears at
all, `OnboardingRoot` is missing from the layout.
