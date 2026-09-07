# Accessibility

The target is WCAG 2.2 AA. Accessibility is handled in the engine and the host component rather than
left to the application, because a tour that is only usable with a mouse is not a tour, it is an
obstacle.

## What the library provides

### Dialog semantics

The popover is a `role="dialog"` with `tabindex="-1"`, so it can receive focus.

- `aria-labelledby` points at the step title when there is one; otherwise `aria-label` falls back to
  `Labels.DialogLabel`, so the dialog is never unnamed.
- `aria-describedby` points at the description when there is one.
- `aria-modal` is `true` only when `Interaction` is `Blocked`. In `TargetOnly` and `Free` the rest of
  the page really is available, and claiming otherwise would hide content a screen reader user can
  legitimately reach.

### Focus management

- The element focused before the tour started is remembered and restored when it ends.
  `RestoreFocus = false` opts out.
- Focus moves into the popover when a step is shown, once per step, after the content has rendered.
  It prefers an element marked `data-bo-autofocus`, then the first focusable control, then the dialog
  itself. `AutoFocus = false` opts out.
- Focus is trapped inside the popover while `TrapFocus` is on (the default). On a `TargetOnly` step
  the trap also includes the highlighted element, so the user can Tab to the control the step is
  asking them to use.

### Keyboard

| Key | Action |
|---|---|
| `Escape` | Ends the tour, unless `CloseOnEscape` is false |
| `→` / `←` | Next / previous, reversed in RTL |
| `Home` / `End` | First / last step |
| `Tab` / `Shift+Tab` | Cycles inside the trap |
| `Enter` / `Space` | Activates the focused button, as any button does |

Arrow keys are ignored while focus is in a text input, textarea, select or contenteditable, so a step
that asks the user to type something does not steal their cursor keys.

Key handling is installed on the document in the capture phase, so `Escape` works wherever focus is
- including inside the application, on a `Free` step.

### Announcements

A polite live region outside the tour's own subtree announces each step:

> Step 2 of 5. Create a project

It also announces the waiting state. The region is rendered whether or not a tour is running, so
assistive technology never has to discover it mid-tour, and the format strings are part of
`OnboardingLabels` so they can be translated.

Progress indicators are announced correctly for their shape: the bar is a `role="progressbar"` with
`aria-valuenow`, `aria-valuemax` and `aria-valuetext`; the dots are decorative and carry a visually
hidden text equivalent.

### Motion

`prefers-reduced-motion` is sampled and, when honoured, sets the animation duration to zero, which
disables the transitions, the entry fades and the spotlight's movement between steps. Set
`RespectReducedMotion = false` to ignore the preference, though there is rarely a good reason to.

### Contrast and forced colors

The default light and dark palettes meet AA contrast for body text, muted text and button labels. A
`forced-colors` block replaces the shadow with a border, outlines the spotlight with `Highlight`, and
gives buttons a `ButtonText` border, so nothing relies on colour that Windows high contrast has
removed.

### Target size

Under `pointer: coarse`, buttons grow to a minimum of 36 CSS pixels tall and the close button to
36×36, clearing the WCAG 2.2 target-size minimum of 24×24 with room to spare.

## What you still own

**The content.** Titles and descriptions should make sense read aloud, out of visual context. "Click
the button on the left" is not a description; "Create a project from the toolbar" is.

**Custom templates.** If you replace the popover contents, the dialog element, its label, the focus
trap and the announcements are still handled for you, but the content inside is yours: use real
buttons, label your inputs, and keep a sensible tab order.

**Colour overrides.** Changing `--bo-accent` to something low-contrast against `--bo-accent-text`
will break contrast. Check both themes after restyling.

**Interactive steps.** A step with `AdvanceOn` asks the user to operate a control. Make sure that
control is reachable by keyboard, and say what to do in words rather than relying on the highlight.

## Testing

Things worth checking in your own application:

- Run a whole tour with the keyboard only, starting from a page where focus is in a text field.
- Confirm focus returns to the element you started from.
- Listen to a tour with a screen reader and check that each step is announced once.
- Turn on reduced motion and confirm nothing animates.
- Turn on Windows high contrast and confirm the spotlight and the popover are both visible.
- Zoom to 200% and confirm the popover stays on screen and its content scrolls rather than clipping.

The library's own test suite covers dialog semantics, labelling, live-region announcements, focus
capture and restoration, keyboard navigation, and the reduced-motion path.
