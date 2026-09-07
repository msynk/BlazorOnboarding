using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.JSInterop;

namespace BlazorOnboarding;

/// <summary>
/// The default browser layer: one lazily imported JS module per scope, one
/// <see cref="DotNetObjectReference"/> for every session, and no polling anywhere.
/// </summary>
internal sealed class OnboardingInterop : IOnboardingInterop, IDisposable
{
    private const string ModulePath = "./_content/BlazorOnboarding/onboarding.js";

    private readonly IJSRuntime _js;
    private readonly OnboardingOptions _options;
    private readonly ILogger<OnboardingInterop> _logger;

    private readonly Dictionary<string, IOnboardingInteropCallbacks> _callbacks = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _importGate = new(1, 1);

    private IJSObjectReference? _module;
    private DotNetObjectReference<OnboardingInterop>? _self;
    private bool _disposed;

    public OnboardingInterop(IJSRuntime js, IOptions<OnboardingOptions> options, ILogger<OnboardingInterop> logger)
    {
        _js = js;
        _options = options.Value;
        _logger = logger;
    }

    public bool IsAvailable => !_disposed && _module is not null;

    public async ValueTask<BrowserEnvironment> InitializeAsync(CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return BrowserEnvironment.Unknown;

        return await module.InvokeAsync<BrowserEnvironment>(
            "initialize",
            cancellationToken,
            new { enableJsDiagnostics = _options.EnableJsDiagnostics }).ConfigureAwait(false);
    }

    private async ValueTask<IJSObjectReference?> EnsureModuleAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return null;
        if (_module is not null) return _module;

        await _importGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_module is not null) return _module;

            // During prerendering this throws InvalidOperationException, which is a normal state
            // rather than an error: the engine simply runs without a browser until hydration.
            _module = await _js.InvokeAsync<IJSObjectReference>("import", cancellationToken, ModulePath)
                .ConfigureAwait(false);

            _self = DotNetObjectReference.Create(this);
            return _module;
        }
        finally
        {
            _importGate.Release();
        }
    }

    public async ValueTask AttachAsync(
        string sessionId, IOnboardingInteropCallbacks callbacks, CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return;

        _callbacks[sessionId] = callbacks;
        await module.InvokeVoidAsync("attach", cancellationToken, sessionId, _self).ConfigureAwait(false);
    }

    public async ValueTask DetachAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        _callbacks.Remove(sessionId);

        if (_module is null) return;
        await _module.InvokeVoidAsync("detach", cancellationToken, sessionId).ConfigureAwait(false);
    }

    public async ValueTask<StepGeometry> ActivateStepAsync(
        StepActivation activation, CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return StepGeometry.Missing;

        return await module.InvokeAsync<StepGeometry>("activateStep", cancellationToken, activation)
            .ConfigureAwait(false);
    }

    public async ValueTask DeactivateStepAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_module is null) return;
        await _module.InvokeVoidAsync("deactivateStep", cancellationToken, sessionId).ConfigureAwait(false);
    }

    public async ValueTask<StepGeometry> MeasureAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_module is null) return StepGeometry.Missing;
        return await _module.InvokeAsync<StepGeometry>("measure", cancellationToken, sessionId).ConfigureAwait(false);
    }

    public async ValueTask SetPopoverAsync(
        string sessionId, ElementReference? popover, bool trapFocus, CancellationToken cancellationToken = default)
    {
        if (_module is null) return;
        await _module.InvokeVoidAsync("setPopover", cancellationToken, sessionId, popover, trapFocus)
            .ConfigureAwait(false);
    }

    public async ValueTask CaptureFocusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_module is null) return;
        await _module.InvokeVoidAsync("captureFocus", cancellationToken, sessionId).ConfigureAwait(false);
    }

    public async ValueTask RestoreFocusAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_module is null) return;
        await _module.InvokeVoidAsync("restoreFocus", cancellationToken, sessionId).ConfigureAwait(false);
    }

    public async ValueTask FocusPopoverAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (_module is null) return;
        await _module.InvokeVoidAsync("focusPopover", cancellationToken, sessionId).ConfigureAwait(false);
    }

    // ---- Storage -----------------------------------------------------------

    public async ValueTask<string?> GetStorageAsync(string key, CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return null;

        return await module.InvokeAsync<string?>("storageGet", cancellationToken, key).ConfigureAwait(false);
    }

    public async ValueTask SetStorageAsync(string key, string value, CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return;

        await module.InvokeVoidAsync("storageSet", cancellationToken, key, value).ConfigureAwait(false);
    }

    public async ValueTask RemoveStorageAsync(string key, CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return;

        await module.InvokeVoidAsync("storageRemove", cancellationToken, key).ConfigureAwait(false);
    }

    public async ValueTask<IReadOnlyList<string>> ListStorageKeysAsync(
        string prefix, CancellationToken cancellationToken = default)
    {
        var module = await EnsureModuleAsync(cancellationToken).ConfigureAwait(false);
        if (module is null) return [];

        return await module.InvokeAsync<string[]>("storageKeys", cancellationToken, prefix).ConfigureAwait(false);
    }

    // ---- Callbacks from the browser ---------------------------------------

    /// <summary>Payload for <see cref="OnKey"/>; kept as an object so the shape can grow.</summary>
    public sealed record KeyPayload(string Key, bool ShiftKey);

    [JSInvokable]
    public ValueTask OnGeometryChanged(string sessionId, StepGeometry geometry)
        => Dispatch(sessionId, callbacks => callbacks.OnGeometryChangedAsync(geometry));

    [JSInvokable]
    public ValueTask OnTargetLost(string sessionId)
        => Dispatch(sessionId, callbacks => callbacks.OnTargetLostAsync());

    [JSInvokable]
    public ValueTask OnKey(string sessionId, KeyPayload payload)
        => Dispatch(sessionId, callbacks => callbacks.OnKeyAsync(payload.Key, payload.ShiftKey));

    [JSInvokable]
    public ValueTask OnAdvanceTriggered(string sessionId)
        => Dispatch(sessionId, callbacks => callbacks.OnAdvanceTriggeredAsync());

    [JSInvokable]
    public ValueTask OnWaitTimedOut(string sessionId)
        => Dispatch(sessionId, callbacks => callbacks.OnWaitTimedOutAsync());

    private async ValueTask Dispatch(string sessionId, Func<IOnboardingInteropCallbacks, ValueTask> action)
    {
        if (_disposed || !_callbacks.TryGetValue(sessionId, out var callbacks)) return;

        try
        {
            await action(callbacks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // A throw here would surface in the browser console as an unhandled interop failure and
            // tell the user nothing useful.
            _logger.LogError(ex, "An onboarding browser callback failed for session {SessionId}.", sessionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        var module = _module;
        ReleaseLocalResources();

        if (module is not null)
        {
            try
            {
                await module.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (TourSession.IsTransientInteropFailure(ex))
            {
                // The circuit is already gone; there is nothing left to clean up in the browser.
                _logger.LogDebug(ex, "Onboarding could not dispose its browser module.");
            }
        }
    }

    /// <summary>
    /// Synchronous teardown for hosts that dispose scopes without awaiting. The JS module handle is
    /// dropped rather than released, which is safe: releasing it needs a round trip that a
    /// synchronous disposal cannot make, and the page is on its way out regardless.
    /// </summary>
    public void Dispose() => ReleaseLocalResources();

    private void ReleaseLocalResources()
    {
        if (_disposed) return;
        _disposed = true;

        _callbacks.Clear();
        _module = null;
        _self?.Dispose();
        _self = null;
        _importGate.Dispose();
    }
}
