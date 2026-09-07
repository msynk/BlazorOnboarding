namespace BlazorOnboarding;

/// <summary>Small helpers that make the common cases read well in markup.</summary>
public static class Onboarding
{
    /// <summary>
    /// Splattable attributes that mark an element as a named anchor:
    /// <c>&lt;button @attributes="Onboarding.Anchor("create")"&gt;</c>. Equivalent to writing
    /// <c>data-bo-anchor="create"</c> by hand, which is also perfectly fine.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Anchor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return new Dictionary<string, object>(1) { [StepTarget.AnchorAttribute] = name };
    }
}
