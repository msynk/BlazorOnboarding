# Conditional and branching flows

## Conditional steps

`When` gates a step. A step whose condition is false is skipped in whichever direction the user is
travelling, and is left out of the progress count.

```csharp
new StepDefinition
{
    Id = "billing",
    Target = StepTarget.Anchor("billing"),
    Title = "Billing",
    When = ctx => ValueTask.FromResult(user.IsInRole("Administrator")),
}
```

Conditions are re-evaluated on **every transition**, not once at the start. That means a step can be
unlocked by something an earlier step did:

```csharp
new StepDefinition
{
    Id = "connect",
    Title = "Connect a repository",
    OnBeforeLeave = ctx => { ctx.State["connected"] = true; return ValueTask.CompletedTask; },
},
new StepDefinition
{
    Id = "deploy",
    Title = "Deploy it",
    When = ctx => ValueTask.FromResult(ctx.State.ContainsKey("connected")),
}
```

Because they are asked repeatedly, **conditions must be cheap and free of side effects**. Read state,
do not change it. If a condition needs data from the server, load it in `TourDefinition.OnStarted` or
a step's `OnBeforeShow` and stash it in `Session.State`.

### Should this be a condition or a missing-target strategy?

Both can hide a step. Prefer `When` when the reason is about the *user or the data* ("admins only",
"only on the pro plan"), and `MissingTarget = Skip` when the reason is about *the page* ("this
control is not rendered here"). Conditions also keep the progress indicator honest, which
missing-target skipping cannot do until the step is reached.

## Branching

### With buttons

The simplest fork is a step with actions that jump:

```csharp
new StepDefinition
{
    Id = "ask",
    Title = "What brings you here?",
    ShowNext = false,
    Actions =
    [
        new StepAction
        {
            Label = "I design",
            Style = StepActionStyle.Primary,
            Effect = StepActionEffect.GoTo,
            TargetStepId = "design-path",
        },
        new StepAction
        {
            Label = "I build",
            Style = StepActionStyle.Primary,
            Effect = StepActionEffect.GoTo,
            TargetStepId = "build-path",
        },
    ],
}
```

### With a resolver

`ResolveNext` decides in code where the forward button goes:

```csharp
new StepDefinition
{
    Id = "plan",
    ResolveNext = ctx => ValueTask.FromResult<string?>(
        ctx.State["plan"] as string == "free" ? "upgrade" : "advanced-features"),
}
```

Three return values matter:

| Return | Meaning |
|---|---|
| a step id | Jump there. An unknown id fails the tour. |
| `null` | Fall through to the next step in order. |
| `TourDefinition.EndStepId` | Finish the tour now. |

A step with a `ResolveNext` is never reported as the last step, because that cannot be known without
running your code. The forward button keeps saying "Next", and the hook decides when it is pressed.

To end a branch without falling into the next one, point it at the step where the paths rejoin:

```csharp
new StepDefinition
{
    Id = "design-path",
    ResolveNext = _ => ValueTask.FromResult<string?>("shared-ending"),
}
```

## Going back through a branch

Back follows the **visit history**, not the declaration order. If a user went from `ask` straight to
`build-path`, pressing Back returns them to `ask` - not to the `design-path` step that happens to sit
above `build-path` in the array.

`session.History` exposes the ids visited so far, oldest first.

`ResolvePrevious` overrides this when you need something else:

```csharp
new StepDefinition
{
    ResolvePrevious = _ => ValueTask.FromResult<string?>("ask"),
}
```

A step with a `ResolvePrevious` always shows its Back button, since the hook can go somewhere even
from the first step.

## Validation

`CanAdvance` blocks the forward button while it returns false:

```csharp
new StepDefinition
{
    Target = StepTarget.Anchor("project-name"),
    Title = "Name your project",
    Interaction = InteractionMode.TargetOnly,
    CanAdvance = ctx => ValueTask.FromResult(!string.IsNullOrWhiteSpace(_projectName)),
}
```

Pressing Next simply does nothing while the guard is unsatisfied. To explain *why*, render your own
message in the step body - the tour re-renders whenever the session changes, and a two-way bound
field will update it.

For validation that should stop the transition and surface an error, throw from `OnBeforeLeave`
instead; that fails the tour with your exception attached.

## Whether the tour should run at all

`CanStart` is checked by `StartAsync` and `StartOnceAsync`:

```csharp
new TourDefinition
{
    Id = "admin-tour",
    CanStart = ctx => ValueTask.FromResult(user.IsInRole("Administrator")),
}
```

`StartOnceAsync` returns `null` when it declines. `StartAsync` throws an `OnboardingException`,
because you asked for it explicitly. `StartOptions.Force` bypasses the check.

## A worked example

```csharp
var tour = new TourDefinition
{
    Id = "setup",
    Steps =
    {
        new StepDefinition
        {
            Id = "role",
            Title = "How will you use this?",
            ShowNext = false,
            Actions =
            [
                new StepAction { Label = "Solo",  Effect = StepActionEffect.GoTo, TargetStepId = "solo" },
                new StepAction { Label = "Team",  Effect = StepActionEffect.GoTo, TargetStepId = "team" },
            ],
        },

        new StepDefinition
        {
            Id = "solo",
            Target = StepTarget.Anchor("new-project"),
            Title = "Start a project",
            ResolveNext = _ => ValueTask.FromResult<string?>("finish"),
        },

        new StepDefinition
        {
            Id = "team",
            Target = StepTarget.Anchor("invite"),
            Title = "Invite your colleagues",
        },

        new StepDefinition
        {
            Id = "team-roles",
            Target = StepTarget.Anchor("roles"),
            Title = "Set their permissions",
            // Only relevant on a plan that has roles at all.
            When = ctx => ValueTask.FromResult(ctx.State["plan"] as string != "free"),
        },

        new StepDefinition
        {
            Id = "finish",
            Title = "You are set up",
            DoneLabel = "Finish",
        },
    },
};
```
