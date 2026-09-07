# API reference

Everything public lives in the `BlazorOnboarding` namespace.

- [Components](#components)
- [Services](#services)
- [Tour and step model](#tour-and-step-model)
- [Options](#options)
- [Targeting](#targeting)
- [Sessions](#sessions)
- [Events](#events)
- [Persistence](#persistence)
- [Localization](#localization)
- [Layout](#layout)
- [Geometry](#geometry)
- [Enums](#enums)
- [Registration](#registration)
- [Browser layer](#browser-layer)

---

## Components

### `OnboardingRoot`

Renders the running tour. Place one, usually in the layout.

| Parameter | Type | Description |
|---|---|---|
| `Session` | `ITourSession?` | Render this session instead of the active one, and claim it so other hosts skip it. |
| `Theme` | `OnboardingTheme?` | Force a colour scheme for everything this host renders. |
| `Class` | `string?` | Extra classes on the root element. |
| `PopoverClass` | `string?` | Extra classes on the popover element. |
| `Unstyled` | `bool` | Drop the built-in popover classes, keeping the element and its behaviour. |
| `ChildContent` | `RenderFragment<StepRenderContext>?` | Replace the popover contents for every step. |
| `OverlayTemplate` | `RenderFragment<StepRenderContext>?` | Replace the overlay and spotlight visuals. |
| `WaitingTemplate` | `RenderFragment<StepRenderContext>?` | Replace the waiting placeholder. |

### `OnboardingTour`

Declares a tour in markup. Renders nothing.

| Parameter | Type | Description |
|---|---|---|
| `Id` | `string` | Required. Also the persistence key. |
| `Title` | `string?` | Dialog label fallback. |
| `Version` | `int` | Bump to discard stale saved progress. Default `1`. |
| `AutoStart` | `OnboardingAutoStart` | `None`, `Once` or `Always`. Default `None`. |
| `ChildContent` | `RenderFragment?` | The `OnboardingStep` children. |
| `Template` | `RenderFragment<StepRenderContext>?` | Replace the popover contents for this tour. |
| `Labels` | `OnboardingLabels?` | Override the label set. |
| `Persist` | `bool?` | Override global persistence. |
| `Placement`, `Theme`, `Direction`, `Progress`, `Interaction`, `ShowOverlay`, `ShowSpotlight`, `ShowSkip`, `ShowClose`, `ShowPrevious`, `CloseOnEscape`, `CloseOnOverlayClick` | nullable | Defaults for every step. |
| `Configure` | `Action<TourDefinition>?` | Escape hatch for anything not exposed as a parameter. |
| `OnStarted` | `EventCallback` | |
| `OnStepChanged` | `EventCallback<StepChangedEventArgs>` | |
| `OnEnded` | `EventCallback<TourEndedEventArgs>` | |

Members: `Definition`, `Session`, `IsRunning`, `StartAsync`, `StartOnceAsync`, `RestartAsync`,
`StopAsync`.

### `OnboardingStep`

Declares one step. Renders nothing. Must be inside an `OnboardingTour`.

| Parameter | Type | Description |
|---|---|---|
| `Id` | `string?` | Strongly recommended for persistence and branching. |
| `Order` | `int?` | Explicit ordering; otherwise declaration order. |
| `Target` | `StepTarget?` | Any target kind. |
| `Anchor` | `string?` | Shorthand for `StepTarget.Anchor`. |
| `Selector` | `string?` | Shorthand for `StepTarget.Css`. |
| `Element` | `ElementReference?` | Shorthand for `StepTarget.Element`. |
| `AdditionalTargets` | `IReadOnlyList<StepTarget>?` | Folded into the same spotlight. |
| `Title`, `Description` | `string?` | |
| `ChildContent` | `RenderFragment<StepRenderContext>?` | Step body. |
| `Header`, `Footer`, `Template` | `RenderFragment<StepRenderContext>?` | Region or whole-popover replacements. |
| `Actions` | `IReadOnlyList<StepAction>?` | Extra footer buttons. |
| `Route` | `string?` | Route this step belongs to; `*` suffix for a prefix match. |
| `When` | `Func<StepContext, ValueTask<bool>>?` | Gate. |
| `CanAdvance` | `Func<StepContext, ValueTask<bool>>?` | Blocks the forward button. |
| `ResolveNext`, `ResolvePrevious` | `Func<StepContext, ValueTask<string?>>?` | Branching. |
| `AdvanceOn` | `AdvanceTrigger?` | Advance on a real interaction. |
| `OnBeforeShow`, `OnShown`, `OnBeforeLeave` | `Func<StepContext, ValueTask>?` | Lifecycle. |
| `Data` | `object?` | Arbitrary payload. |
| `NextLabel`, `PreviousLabel`, `SkipLabel`, `DoneLabel` | `string?` | Per-step label overrides. |
| Presentation overrides | nullable | `Placement`, `PlacementFallbacks`, `Offset`, `ShowSpotlight`, `SpotlightPadding`, `SpotlightRadius`, `SpotlightShape`, `ShowOverlay`, `Interaction`, `Scroll`, `MissingTarget`, `WaitTimeout`, `WaitTimeoutBehavior`, `Progress`, `ShowNext`, `ShowPrevious`, `ShowSkip`, `ShowClose`, `PopoverClass` |
| `Configure` | `Action<StepDefinition>?` | Escape hatch. |

---

## Services

### `IOnboardingService`

Scoped. The entry point for starting and querying tours.

```csharp
ITourSession? Active { get; }
IReadOnlyList<ITourSession> Sessions { get; }
IReadOnlyCollection<TourDefinition> RegisteredTours { get; }

event EventHandler<OnboardingEvent>? EventRaised;
event EventHandler? SessionsChanged;

void Register(TourDefinition tour);
void Unregister(string tourId);
TourDefinition? GetTour(string tourId);

Task<ITourSession> StartAsync(TourDefinition tour, StartOptions? options = null, CancellationToken ct = default);
Task<ITourSession> StartAsync(string tourId, StartOptions? options = null, CancellationToken ct = default);
Task<ITourSession?> StartOnceAsync(TourDefinition tour, StartOptions? options = null, CancellationToken ct = default);
Task<ITourSession> RestartAsync(TourDefinition tour, CancellationToken ct = default);
Task StopAllAsync(TourEndReason reason = TourEndReason.Cancelled, CancellationToken ct = default);

ITourSession? FindSession(string tourId);
Task<bool> IsCompletedAsync(string tourId, CancellationToken ct = default);
Task<bool> IsClosedAsync(string tourId, CancellationToken ct = default);
Task<OnboardingRecord?> GetRecordAsync(string tourId, CancellationToken ct = default);
Task ResetAsync(string tourId, CancellationToken ct = default);
Task ResetAllAsync(CancellationToken ct = default);
```

`StartAsync` throws `OnboardingException` when the tour has no steps, when the id is not registered,
or when `CanStart` declines. `StartOnceAsync` returns `null` instead of throwing.

### `StartOptions`

| Member | Default | Description |
|---|---|---|
| `StartAtStepId` | `null` | Begin at this step. |
| `StartAtIndex` | `null` | Begin at this index. Ignored when `StartAtStepId` is set. |
| `Resume` | `true` | Continue from persisted progress. |
| `Force` | `false` | Start even when closed, and bypass `CanStart`. |
| `State` | `null` | Seed values for `ITourSession.State`. |
| `StopOthers` | `true` | Cancel any other running tour first. |

---

## Tour and step model

### `TourDefinition`

Inherits every member of [`StepOptions`](#stepoptions) as defaults for its steps.

| Member | Type | Description |
|---|---|---|
| `Id` | `string` | Required. |
| `Title`, `Description` | `string?` | |
| `Steps` | `List<StepDefinition>` | |
| `Labels` | `OnboardingLabels?` | Replaces the global set. |
| `Persist` | `bool?` | |
| `Version` | `int` | Default `1`. |
| `Template` | `RenderFragment<StepRenderContext>?` | |
| `CanStart` | `Func<TourContext, ValueTask<bool>>?` | |
| `OnStarted` | `Func<TourContext, ValueTask>?` | |
| `OnStepChanged` | `Func<StepChangedEventArgs, ValueTask>?` | |
| `OnEnded` | `Func<TourEndedEventArgs, ValueTask>?` | |
| `Data` | `object?` | |

Methods: `FindStep(string)`, `IndexOf(string)`. Constant: `EndStepId` (`"$end"`).

### `StepDefinition`

Inherits every member of [`StepOptions`](#stepoptions). Same members as the `OnboardingStep`
parameters above, plus `Body` (the component's `ChildContent`) and `Metadata`.

`Id` is auto-generated when not set. Set it explicitly for anything that persists or branches.

### `StepAction`

| Member | Type | Default |
|---|---|---|
| `Id` | `string?` | Surfaced as `data-bo-action`. |
| `Label` | `string` | Required. |
| `Style` | `StepActionStyle` | `Secondary` |
| `OnClick` | `Func<StepContext, ValueTask>?` | Awaited before `Effect`. |
| `Effect` | `StepActionEffect` | `None` |
| `TargetStepId` | `string?` | For `GoTo`. |
| `IsDisabled` | `Func<StepContext, bool>?` | |
| `CssClass` | `string?` | |
| `AutoFocus` | `bool` | Receives focus instead of the forward button. |

### `AdvanceTrigger`

| Member | Default | Description |
|---|---|---|
| `EventName` | `"click"` | Any DOM event. |
| `Selector` | `null` | Element to listen on; null means the step's target. |
| `MatchSelector` | `null` | Only advance when the event came from a matching element. |
| `DelayMs` | `150` | Settle time before advancing. |

`AdvanceTrigger.TargetClick` is a shared instance of the defaults.

### `StepContext` / `TourContext`

`StepContext`: `Session`, `Tour`, `Step`, `Index`, `Count`, `Services`, `CancellationToken`, `State`.

`TourContext`: `Tour`, `Services`, `Session?`.

### `StepRenderContext`

Passed to every template. See [headless rendering](headless.md#the-render-context).

---

## Options

### `StepOptions`

The inheritable knobs, shared by `OnboardingOptions`, `TourDefinition` and `StepDefinition`. Every
member is nullable; `null` means "inherit". Resolution order is step, tour, global, then
`OnboardingDefaults`.

| Member | Type | Default |
|---|---|---|
| `Placement` | `Placement?` | `Auto` |
| `PlacementFallbacks` | `IReadOnlyList<Placement>?` | derived |
| `Offset` | `double?` | `12` |
| `ViewportPadding` | `double?` | `16` |
| `ArrowSize` | `double?` | `10` |
| `ShowSpotlight` | `bool?` | `true` |
| `SpotlightPadding` | `double?` | `8` |
| `SpotlightRadius` | `double?` | `10` |
| `SpotlightShape` | `SpotlightShape?` | `Rounded` |
| `ShowOverlay` | `bool?` | `true` |
| `OverlayOpacity` | `double?` | `0.55` |
| `Interaction` | `InteractionMode?` | `Blocked` |
| `CloseOnOverlayClick` | `bool?` | `false` |
| `CloseOnEscape` | `bool?` | `true` |
| `Scroll` | `ScrollMode?` | `Smooth` |
| `ScrollPadding` | `double?` | `24` |
| `MissingTarget` | `MissingTargetBehavior?` | `Wait` |
| `WaitTimeout` | `TimeSpan?` | 10 seconds |
| `WaitTimeoutBehavior` | `MissingTargetBehavior?` | `Skip` |
| `Progress` | `ProgressStyle?` | `Dots` |
| `ShowNext`, `ShowPrevious`, `ShowSkip`, `ShowClose` | `bool?` | `true` |
| `AnimationDuration` | `TimeSpan?` | 220 ms |
| `RespectReducedMotion` | `bool?` | `true` |
| `Theme` | `OnboardingTheme?` | `System` |
| `Direction` | `OnboardingDirection?` | `Auto` |
| `ZIndex` | `int?` | `9000` |
| `PopoverClass` | `string?` | accumulates across levels |
| `KeyboardNavigation` | `bool?` | `true` |
| `TrapFocus` | `bool?` | `true` |
| `RestoreFocus` | `bool?` | `true` |
| `AutoFocus` | `bool?` | `true` |

### `OnboardingOptions`

`StepOptions` plus:

| Member | Type | Default |
|---|---|---|
| `Labels` | `OnboardingLabels` | English |
| `Persist` | `bool` | `true` |
| `StorageKeyPrefix` | `string` | `"blazor-onboarding:"` |
| `AutoNavigateToStepRoute` | `bool` | `true` |
| `EnableJsDiagnostics` | `bool` | `false` |

### `EffectiveStepOptions`

The resolved, non-nullable snapshot for one step, exposed as `ITourSession.CurrentOptions` and
`StepRenderContext.Options`. Adds `ReducedMotion` (whether the preference is being honoured) and
`RightToLeft`.

### `OnboardingDefaults`

The terminal fallback constants, so you can reference them rather than repeating literals.

---

## Targeting

### `StepTarget`

```csharp
StepTarget.None
StepTarget.Css(string selector)
StepTarget.Anchor(string name)                     // [data-bo-anchor="name"]
StepTarget.Element(ElementReference element)
StepTarget.Dynamic(Func<StepContext, StepTarget?>)
StepTarget.Dynamic(Func<StepContext, ValueTask<StepTarget?>>)
```

Implicit conversions from `string` (a CSS selector) and `ElementReference`. Members: `IsNone`,
`IsDynamic`, `ResolveAsync(StepContext)`. Equality is structural. Constant:
`StepTarget.AnchorAttribute` (`"data-bo-anchor"`).

### `Onboarding.Anchor(string)`

Returns splattable attributes: `<button @attributes="Onboarding.Anchor("create")">`.

---

## Sessions

### `ITourSession`

State: `Id`, `Tour`, `Status`, `CurrentStep`, `CurrentIndex`, `DisplayPosition`, `DisplayCount`,
`CurrentOptions`, `TargetRect`, `Placement`, `HasTarget`, `IsWaiting`, `IsActive`, `IsFirstStep`,
`IsLastStep`, `History`, `State`, `EndReason`, `Error`.

Event: `Changed` (`TourSessionChangedEventArgs.Change` is `Step`, `Geometry` and/or `Ended`).

Commands: `StartAsync`, `NextAsync`, `PreviousAsync`, `GoToAsync(string)`, `GoToAsync(int)`,
`SkipAsync`, `CompleteAsync`, `DismissAsync`, `CancelAsync`, `PauseAsync`, `ResumeAsync`,
`RefreshAsync`, `InvokeActionAsync`.

Commands are serialised: concurrent calls queue rather than interleaving, and a command issued from
inside a hook runs inline rather than deadlocking on the lock its caller holds.

---

## Events

### `OnboardingEvent`

`Kind`, `Session`, `Tour`, `Step`, `StepIndex`, `Duration`, `Action`, `Reason`, `Error`, `Timestamp`.

### `IOnboardingObserver`

```csharp
ValueTask OnEventAsync(OnboardingEvent onboardingEvent, CancellationToken ct = default);
```

Register with `AddOnboardingObserver<T>()`. A throwing observer is logged and ignored.

### `StepChangedEventArgs`

`Session`, `Step`, `Index`, `PreviousStep`, `Reason`.

### `TourEndedEventArgs`

`Session`, `Reason`, `LastStep`, `LastIndex`, `Error`, `IsSuccess`.

### `OnboardingException`

`TourId` and `StepId` when known.

---

## Persistence

### `IOnboardingStore`

```csharp
ValueTask<OnboardingRecord?> GetAsync(string tourId, CancellationToken ct = default);
ValueTask SetAsync(OnboardingRecord record, CancellationToken ct = default);
ValueTask RemoveAsync(string tourId, CancellationToken ct = default);
ValueTask ClearAsync(CancellationToken ct = default);
```

Implementations: `BrowserOnboardingStore` (default) and `InMemoryOnboardingStore`.

### `OnboardingRecord`

`TourId`, `Version`, `CurrentStepId`, `CurrentIndex`, `Completed`, `Dismissed`, `VisitedStepIds`,
`UpdatedAt`, `CompletedAt`, `Data`, `IsClosed`.

---

## Localization

### `IOnboardingLocalizer`

```csharp
OnboardingLabels GetLabels(TourDefinition? tour);
string? Localize(string? text) => text;    // default: unchanged
```

### `OnboardingLabels`

`Next`, `Previous`, `Skip`, `Done`, `Close`, `DialogLabel`, `ProgressFormat`,
`StepAnnouncementFormat`, `WaitingAnnouncement`, `ProgressLabel`. Static `Default`, instance
`Clone()`.

---

## Layout

### `PlacementEngine`

```csharp
static PlacementResult Place(in PlacementRequest request);
```

Pure and deterministic. Usable on its own.

### `PlacementRequest`

`Target`, `Popover`, `Viewport`, `Preferred`, `Fallbacks`, `Offset`, `ViewportPadding`, `ArrowSize`,
`CornerRadius`, `RightToLeft`, `HasTarget`.

### `PlacementResult`

`Popover` (the final rectangle), `Side`, `Align`, `ShowArrow`, `ArrowOffset`, `UsedPreferred`,
`Overflow`, `SideName`.

### `RouteMatcher`

```csharp
static bool Matches(string? pattern, string? relativePath);
static string Normalize(string? path);
```

---

## Geometry

### `Rect`

`X`, `Y`, `Width`, `Height`, `Left`, `Top`, `Right`, `Bottom`, `CenterX`, `CenterY`, `Area`,
`IsEmpty`. Methods: `FromEdges`, `Inflate`, `Offset`, `Contains`, `IntersectsWith`, `Intersect`,
`Union`, `ApproximatelyEquals`, `Round`.

### `Size`

`Width`, `Height`, `IsEmpty`.

---

## Enums

| Enum | Values |
|---|---|
| `Placement` | `Auto`, `Top`, `TopStart`, `TopEnd`, `Bottom`, `BottomStart`, `BottomEnd`, `Left`, `LeftStart`, `LeftEnd`, `Right`, `RightStart`, `RightEnd`, `Center` |
| `PlacementSide` | `Top`, `Right`, `Bottom`, `Left`, `Center` |
| `PlacementAlign` | `Start`, `Center`, `End` |
| `MissingTargetBehavior` | `Wait`, `Skip`, `Center`, `Fail` |
| `TourStatus` | `Idle`, `Running`, `Waiting`, `Paused`, `Completed`, `Skipped`, `Dismissed`, `Cancelled`, `Failed` |
| `TourEndReason` | `Completed`, `Skipped`, `Dismissed`, `Cancelled`, `Failed` |
| `StepChangeReason` | `Start`, `Next`, `Previous`, `Jump`, `Resume`, `Retarget` |
| `InteractionMode` | `Blocked`, `TargetOnly`, `Free` |
| `ScrollMode` | `Auto`, `Smooth`, `Instant`, `None` |
| `SpotlightShape` | `Rounded`, `Rectangle`, `Circle`, `None` |
| `ProgressStyle` | `None`, `Text`, `Dots`, `Bar` |
| `OnboardingTheme` | `System`, `Light`, `Dark` |
| `OnboardingDirection` | `Auto`, `LeftToRight`, `RightToLeft` |
| `OnboardingAutoStart` | `None`, `Once`, `Always` |
| `StepActionStyle` | `Primary`, `Secondary`, `Ghost`, `Danger` |
| `StepActionEffect` | `None`, `Next`, `Previous`, `GoTo`, `Complete`, `Skip`, `Dismiss` |
| `OnboardingEventKind` | see [events](events-analytics.md#the-event-stream) |
| `TourSessionChange` | `None`, `Step`, `Geometry`, `Ended` (flags) |

---

## Registration

```csharp
IServiceCollection AddBlazorOnboarding(Action<OnboardingOptions>? configure = null);
IServiceCollection AddOnboardingStore<TStore>()       where TStore : class, IOnboardingStore;
IServiceCollection AddInMemoryOnboardingStore();
IServiceCollection AddOnboardingLocalizer<TLocalizer>() where TLocalizer : class, IOnboardingLocalizer;
IServiceCollection AddOnboardingObserver<TObserver>()  where TObserver : class, IOnboardingObserver;
```

All services are scoped.

---

## Browser layer

`IOnboardingInterop` is the single seam between the engine and the DOM. It is public so that it can
be substituted in tests, letting the whole engine run without a browser; applications do not normally
touch it.

```csharp
bool IsAvailable { get; }
ValueTask<BrowserEnvironment> InitializeAsync(CancellationToken ct = default);
ValueTask AttachAsync(string sessionId, IOnboardingInteropCallbacks callbacks, CancellationToken ct = default);
ValueTask DetachAsync(string sessionId, CancellationToken ct = default);
ValueTask<StepGeometry> ActivateStepAsync(StepActivation activation, CancellationToken ct = default);
ValueTask DeactivateStepAsync(string sessionId, CancellationToken ct = default);
ValueTask<StepGeometry> MeasureAsync(string sessionId, CancellationToken ct = default);
ValueTask SetPopoverAsync(string sessionId, ElementReference? popover, bool trapFocus, CancellationToken ct = default);
ValueTask CaptureFocusAsync(string sessionId, CancellationToken ct = default);
ValueTask RestoreFocusAsync(string sessionId, CancellationToken ct = default);
ValueTask FocusPopoverAsync(string sessionId, CancellationToken ct = default);
ValueTask<string?> GetStorageAsync(string key, CancellationToken ct = default);
ValueTask SetStorageAsync(string key, string value, CancellationToken ct = default);
ValueTask RemoveStorageAsync(string key, CancellationToken ct = default);
ValueTask<IReadOnlyList<string>> ListStorageKeysAsync(string prefix, CancellationToken ct = default);
```

Supporting types: `TargetDescriptor`, `TargetKind`, `AdvanceTriggerDescriptor`, `StepActivation`,
`StepGeometry`, `BrowserEnvironment`, `IOnboardingInteropCallbacks`.
