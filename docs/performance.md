# Performance

## What a running tour actually costs

**When nothing is happening: nothing.** There are no timers and no polling. The browser layer holds a
`ResizeObserver`, an `IntersectionObserver`, two passive listeners and, only while waiting for a
target, a `MutationObserver`. An idle page with a tour open does no work at all.

**When something moves:** at most one measurement per animation frame. Every observer feeds a single
`requestAnimationFrame` callback that takes one measurement of the target, the popover and the
viewport, and compares it with the previous frame. If nothing changed by more than half a pixel, no
interop call is made. A momentum scroll therefore costs one small interop call per frame, and a
resize storm the same.

**Per step:** one `activateStep` call, which resolves the target, scrolls it into view, installs the
observers and returns the first measurement. Not one call per operation.

**Per render:** one component. `OnboardingRoot` is the only thing that re-renders when a tour moves.
Nothing in your application re-renders because a tour advanced, unless you subscribed to the events
yourself.

## Where the cost actually is

### Conditions are asked repeatedly

`StepDefinition.When` is evaluated for **every step on every transition**, because that is what keeps
the progress count honest and lets a condition depend on state an earlier step changed.

So conditions must be cheap:

```csharp
// Good: reads state.
When = ctx => ValueTask.FromResult(_user.IsAdmin)

// Bad: a network call, several times per step change.
When = async ctx => (await _api.GetPermissionsAsync()).CanBill
```

Load once and cache in the session:

```csharp
new TourDefinition
{
    OnStarted = async ctx =>
    {
        ctx.Session!.State["canBill"] = await _api.CanBillAsync();
    },
    Steps =
    {
        new StepDefinition
        {
            When = ctx => ValueTask.FromResult(ctx.State["canBill"] is true),
        },
    },
}
```

### Selectors are re-queried

Targets are located by `querySelector` on every measurement where the previous element left the DOM,
and on every explicit refresh. That is what makes re-rendered targets work, but it means an expensive
selector is run more often than you might expect.

`[data-bo-anchor="x"]` is an attribute-value match on a single attribute and is about as cheap as a
selector gets. Prefer it to deep descendant selectors like `.sidebar > ul li:nth-child(3) a`.

### Blazor Server round trips

On Blazor Server each interop call is a round trip over the circuit. The frame de-duplication is what
makes this viable: a scroll produces one small message per frame at most, and only while the geometry
is actually changing.

If your users are on high-latency connections and tours feel sluggish while scrolling, the usual
lever is `Scroll = ScrollMode.Instant`, which removes the settle-detection frames.

### Very large tours

Nothing is rendered for steps that are not on screen, so a 50-step tour costs the same per step as a
3-step one. The plan rebuild is O(steps) per transition, which is only worth thinking about if your
conditions are expensive - see above.

## Things that are already handled

- **Out-of-order frames.** Measurements carry a monotonic sequence number and older ones are dropped,
  so a delayed message from a resize storm cannot move the popover backwards.
- **Rapid clicking.** Every command takes a single transition lock, so three fast clicks on Next
  advance three steps in order rather than interleaving.
- **Stale async work.** A transition bumps a generation counter. Work that resumes after the user has
  already moved on detects it lost the race and abandons its results instead of applying them to the
  wrong step.
- **Invariant formatting.** All generated CSS uses the invariant culture, so nothing changes
  behaviour under a comma-decimal locale.

## Cleanup

Everything is released when a tour ends or a scope is disposed: observers disconnected, listeners
removed, animation frames cancelled, timers cleared, the `DotNetObjectReference` disposed and the JS
module released.

Both synchronous and asynchronous disposal are implemented. Blazor disposes scopes asynchronously,
but plenty of hosts and test frameworks do not, and a service that only supported `IAsyncDisposable`
would make those throw.

Interop failures during teardown - a dropped circuit, a page unload, prerendering - are treated as
expected rather than as errors, so a closing tab never produces an exception in your logs.

## Measuring it yourself

The demo's [events page](events-analytics.md) shows the live event stream, which is the easiest way
to see how many transitions a tour is actually performing. For interop volume, the browser's
performance profiler will show the `requestAnimationFrame` callbacks; a healthy idle tour shows none.

## A checklist

- Prefer `data-bo-anchor` over deep CSS selectors.
- Keep `When` conditions synchronous and free of I/O.
- Give every step an explicit `Id` so persistence does not fall back to indices.
- Use `Persist = false` for playgrounds and transient hints so they never write.
- Set `Scroll = ScrollMode.None` for steps whose target is always in view.
- Place exactly one `OnboardingRoot`, in the layout.
