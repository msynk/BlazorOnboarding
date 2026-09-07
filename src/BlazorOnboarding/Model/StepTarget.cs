using Microsoft.AspNetCore.Components;

namespace BlazorOnboarding;

/// <summary>
/// Describes how a step finds the element it points at. Create instances with the static
/// factory methods, or rely on the implicit conversions from <see cref="string"/> (a CSS
/// selector) and <see cref="ElementReference"/>.
/// </summary>
public abstract class StepTarget : IEquatable<StepTarget>
{
    /// <summary>The attribute used by <see cref="Anchor"/> to find elements.</summary>
    public const string AnchorAttribute = "data-bo-anchor";

    private protected StepTarget() { }

    /// <summary>A step with no target: rendered as a centred modal card.</summary>
    public static StepTarget None { get; } = new NoTarget();

    /// <summary>Targets the first element matching a CSS selector.</summary>
    public static StepTarget Css(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        return new CssTarget(selector);
    }

    /// <summary>
    /// Targets the element carrying <c>data-bo-anchor="<paramref name="name"/>"</c>. Survives
    /// re-renders and works with elements that do not exist yet, unlike a captured reference.
    /// </summary>
    public static StepTarget Anchor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new AnchorTarget(name);
    }

    /// <summary>Targets a captured <see cref="ElementReference"/>.</summary>
    public static StepTarget Element(ElementReference element) => new ElementTarget(element);

    /// <summary>
    /// Defers the decision until the step is activated. The resolver may return <see cref="None"/>
    /// or <see langword="null"/>, which is treated as a missing target.
    /// </summary>
    public static StepTarget Dynamic(Func<StepContext, ValueTask<StepTarget?>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return new DynamicTarget(resolver);
    }

    /// <summary>Defers the decision until the step is activated.</summary>
    public static StepTarget Dynamic(Func<StepContext, StepTarget?> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        return new DynamicTarget(ctx => ValueTask.FromResult(resolver(ctx)));
    }

    /// <summary>True when this target intentionally points at nothing.</summary>
    public bool IsNone => this is NoTarget;

    /// <summary>True when the target must be resolved by invoking user code before it can be located.</summary>
    public bool IsDynamic => this is DynamicTarget;

    /// <summary>Resolves a dynamic target down to a concrete one. Concrete targets return themselves.</summary>
    public async ValueTask<StepTarget> ResolveAsync(StepContext context)
    {
        var current = this;
        // Guard against a resolver that keeps returning another dynamic target.
        for (var depth = 0; current is DynamicTarget dynamicTarget && depth < 8; depth++)
        {
            current = await dynamicTarget.Resolver(context).ConfigureAwait(false) ?? None;
        }
        return current is DynamicTarget ? None : current;
    }

    public static implicit operator StepTarget(string cssSelector) => Css(cssSelector);

    public static implicit operator StepTarget(ElementReference element) => Element(element);

    public abstract bool Equals(StepTarget? other);

    public sealed override bool Equals(object? obj) => Equals(obj as StepTarget);

    public abstract override int GetHashCode();

    internal sealed class NoTarget : StepTarget
    {
        public override bool Equals(StepTarget? other) => other is NoTarget;
        public override int GetHashCode() => 0;
        public override string ToString() => "none";
    }

    internal sealed class CssTarget(string selector) : StepTarget
    {
        public string Selector { get; } = selector;
        public override bool Equals(StepTarget? other) => other is CssTarget o && o.Selector == Selector;
        public override int GetHashCode() => HashCode.Combine(1, Selector);
        public override string ToString() => $"css({Selector})";
    }

    internal sealed class AnchorTarget(string name) : StepTarget
    {
        public string Name { get; } = name;

        /// <summary>The CSS selector this anchor is located with.</summary>
        public string Selector { get; } = $"[{AnchorAttribute}=\"{CssEscape(name)}\"]";

        public override bool Equals(StepTarget? other) => other is AnchorTarget o && o.Name == Name;
        public override int GetHashCode() => HashCode.Combine(2, Name);
        public override string ToString() => $"anchor({Name})";

        private static string CssEscape(string value) => value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }

    internal sealed class ElementTarget(ElementReference element) : StepTarget
    {
        public ElementReference Reference { get; } = element;
        public override bool Equals(StepTarget? other) => other is ElementTarget o && o.Reference.Id == Reference.Id;
        public override int GetHashCode() => HashCode.Combine(3, Reference.Id);
        public override string ToString() => $"element({Reference.Id})";
    }

    internal sealed class DynamicTarget(Func<StepContext, ValueTask<StepTarget?>> resolver) : StepTarget
    {
        public Func<StepContext, ValueTask<StepTarget?>> Resolver { get; } = resolver;
        public override bool Equals(StepTarget? other) => ReferenceEquals(this, other);
        public override int GetHashCode() => HashCode.Combine(4, Resolver);
        public override string ToString() => "dynamic()";
    }
}
