# Theming

Every value in the default look is a CSS custom property under `.bo-root`. Restyling the library is a
matter of overriding variables in your own stylesheet - no selector fights, no `!important`, and no
CSS framework to install.

```css
/* Your stylesheet, loaded after the library's. */
.bo-root {
    --bo-accent: #6d28d9;
    --bo-radius: 6px;
    --bo-font: "Inter", system-ui, sans-serif;
}
```

## The variables

### Colour

| Variable | Default (light) | Used for |
|---|---|---|
| `--bo-surface` | `#ffffff` | Popover background, arrow |
| `--bo-surface-subtle` | `#f6f7f9` | Hover states, progress track |
| `--bo-text` | `#14161a` | Titles, primary text |
| `--bo-text-muted` | `#5c6270` | Descriptions, progress text |
| `--bo-border` | `#e3e6ea` | Popover and arrow borders |
| `--bo-border-strong` | `#cfd4dc` | Secondary button borders, inactive dots |
| `--bo-accent` | `#2563eb` | Primary button, current dot, focus ring |
| `--bo-accent-hover` / `--bo-accent-active` | | Primary button states |
| `--bo-accent-text` | `#ffffff` | Text on the primary button |
| `--bo-accent-soft` | 12% accent | Completed dots |
| `--bo-danger` / `--bo-danger-hover` | `#dc2626` | Danger-styled actions |
| `--bo-overlay` | `0, 0, 0` | Overlay colour, as `R, G, B` |
| `--bo-overlay-opacity` | `0.55` | Set from `OverlayOpacity`; overriding in CSS is not useful |
| `--bo-spotlight-ring` | 90% white | Inner hairline around the spotlight |
| `--bo-spotlight-glow` | 35% accent | Outer glow around the spotlight |

`--bo-overlay` is an unwrapped RGB triple so the opacity can be varied independently. To dim with a
brand colour rather than black:

```css
.bo-root { --bo-overlay: 15, 23, 42; }
```

### Shape and depth

| Variable | Default |
|---|---|
| `--bo-radius` | `14px` |
| `--bo-radius-sm` | `8px` (buttons, close) |
| `--bo-radius-pill` | `999px` (dots, progress bar) |
| `--bo-shadow` | two-layer elevation shadow |
| `--bo-focus-ring` | `0 0 0 2px surface, 0 0 0 4px accent` |

### Typography

| Variable | Default |
|---|---|
| `--bo-font` | system UI stack |
| `--bo-font-size` | `0.9375rem` |
| `--bo-font-size-sm` | `0.8125rem` (buttons, progress) |
| `--bo-title-size` | `1.0625rem` |
| `--bo-title-weight` | `620` |
| `--bo-line-height` | `1.55` |

### Metrics and motion

| Variable | Default | Notes |
|---|---|---|
| `--bo-popover-width` | `22rem` | |
| `--bo-popover-max-height` | `min(70vh, 34rem)` | The body scrolls beyond this |
| `--bo-space` | `1rem` | Padding rhythm |
| `--bo-arrow` | `10px` | Set from `ArrowSize` |
| `--bo-duration` | `220ms` | Set from `AnimationDuration`; zero under reduced motion |
| `--bo-ease` | `cubic-bezier(0.22, 0.9, 0.3, 1)` | |
| `--bo-z` | `9000` | Set from `ZIndex` |

Variables written by the engine - `--bo-z`, `--bo-duration`, `--bo-overlay-opacity`, `--bo-arrow` -
are set inline on the root element, so a stylesheet rule cannot override them. Change the
corresponding option instead.

## Dark mode

The dark palette is applied by a `data-bo-theme="dark"` attribute on the root element, which the
engine sets from the resolved `Theme`:

```csharp
new TourDefinition { Theme = OnboardingTheme.Dark }
builder.Services.AddBlazorOnboarding(o => o.Theme = OnboardingTheme.Dark);
```

`OnboardingTheme.System` (the default) leaves the attribute as `system` and lets the light palette
apply. If your application has its own theme switch, bind it to the host:

```razor
<OnboardingRoot Theme="@(_dark ? OnboardingTheme.Dark : OnboardingTheme.Light)" />
```

The host parameter overrides both the tour and the global setting, which is what you want for a
switch: it keeps tours consistent with the rest of the page.

To adjust the dark palette:

```css
.bo-root[data-bo-theme="dark"] {
    --bo-surface: #0b0e14;
    --bo-accent: #8b5cf6;
}
```

## Scoping a theme

### To one tour

`PopoverClass` is added to the popover element, and accumulates from global, tour and step levels:

```csharp
new TourDefinition { Id = "checkout", PopoverClass = "checkout-tour" }
```

```css
.checkout-tour { --bo-accent: #16a34a; }
```

Because custom properties inherit, this restyles everything inside the popover. To also restyle the
overlay and spotlight, put the class on the host instead, where it lands on `.bo-root`:

```razor
<OnboardingRoot Class="checkout-theme" />
```

### To one step

```csharp
new StepDefinition { PopoverClass = "danger-step" }
```

## Layout tweaks

A wider popover for a step with a form:

```csharp
new StepDefinition { PopoverClass = "wide-step" }
```

```css
.wide-step { --bo-popover-width: 32rem; }
```

The popover is `max-width: calc(100vw - 2rem)` and its body scrolls once the content exceeds
`--bo-popover-max-height`, so a wide or long step degrades gracefully on a phone rather than
overflowing.

## Mobile

Below `34rem` the library narrows the popover to the viewport, reduces padding and tightens the
footer. Under `pointer: coarse` it enlarges buttons for touch. Both are plain media queries in the
stylesheet, so you can extend or replace them:

```css
@media (max-width: 30rem) {
    .bo-root { --bo-popover-width: calc(100vw - 1rem); }
}
```

## Replacing the stylesheet entirely

Nothing in the engine depends on the stylesheet's rules; it only depends on the elements existing.
If you would rather write your own, skip the `<link>` and style these hooks:

| Element | Class | Data attributes |
|---|---|---|
| Root | `.bo-root` | `data-bo-theme`, `data-bo-status`, `data-bo-reduced-motion`, `dir` |
| Spotlight | `.bo-spotlight` | positioned inline |
| Flat overlay | `.bo-overlay` | shown when there is no spotlight |
| Pointer blockers | `.bo-blocker` | one or four, depending on `Interaction` |
| Popover | `.bo-popover` | `data-bo-placed`, `data-bo-side`, `data-bo-untitled` |
| Arrow | `.bo-arrow` | positioned inline |
| Buttons | `.bo-button`, `.bo-button-primary` … | `data-bo-action` |
| Live region | `.bo-live` | |

The popover must remain `position: fixed` with its position applied through the inline `transform`
the engine sets; everything else is yours. `Unstyled="true"` on the host drops the built-in popover
classes altogether - see [headless rendering](headless.md).
