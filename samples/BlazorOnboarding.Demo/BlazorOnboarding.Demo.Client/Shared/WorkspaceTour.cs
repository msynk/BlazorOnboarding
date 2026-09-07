using BlazorOnboarding;

namespace BlazorOnboarding.Demo.Client.Shared;

/// <summary>
/// A tour that spans two routes. Defining it once in C# and starting it from either page keeps the
/// step ids stable, which is what makes resuming across a reload work.
/// </summary>
public static class WorkspaceTour
{
    public const string Id = "workspace";

    public static TourDefinition Create() => new()
    {
        Id = Id,
        Title = "Workspace tour",
        Version = 1,
        Progress = ProgressStyle.Bar,
        Steps =
        {
            new StepDefinition
            {
                Id = "overview",
                Route = "workspace",
                Target = StepTarget.Anchor("metrics"),
                Title = "Your workspace",
                Description = "Usage and activity for everything you can see.",
                Placement = Placement.Bottom,
            },
            new StepDefinition
            {
                Id = "sidebar",
                Route = "workspace",
                Target = StepTarget.Anchor("sidebar"),
                Title = "Move around",
                Description = "Projects, members and billing all live here.",
                Placement = Placement.Right,
            },
            new StepDefinition
            {
                Id = "profile",
                // Navigating happens before the target is looked for, so the element does not need
                // to exist on the page the tour started from.
                Route = "workspace/settings",
                Target = StepTarget.Anchor("profile"),
                Title = "Now on the settings page",
                Description = "The tour navigated here on its own and waited for the page to render.",
            },
            new StepDefinition
            {
                Id = "api-keys",
                Route = "workspace/settings",
                Target = StepTarget.Anchor("api-keys"),
                Title = "API keys",
                Description = "Rotate these regularly. Keys are shown once and then hashed.",
                Placement = Placement.Top,
            },
            new StepDefinition
            {
                Id = "back-home",
                Route = "workspace",
                Target = StepTarget.Anchor("new-project"),
                Title = "Back where you started",
                Description = "A tour can cross routes as often as it needs to, in either direction.",
                DoneLabel = "Finish",
            },
        },
    };
}
