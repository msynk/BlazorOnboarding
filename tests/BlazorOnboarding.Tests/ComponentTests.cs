using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using BlazorOnboarding.Tests.Infrastructure;

namespace BlazorOnboarding.Tests;

/// <summary>
/// Renders the shipped components against the fake browser layer, so the markup, the ARIA wiring
/// and the button behaviour are covered as well as the engine underneath them.
/// </summary>
public class ComponentTests : BunitContext
{
    private readonly FakeInterop _interop = new();

    public ComponentTests()
    {
        Services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
        Services.AddBlazorOnboarding();
        Services.AddSingleton<IOnboardingInterop>(_interop);
        Services.AddInMemoryOnboardingStore();

        _interop.ExistingSelectors.Add("#a");
        _interop.ExistingSelectors.Add("#b");
        _interop.ExistingSelectors.Add("#c");
    }

    private IOnboardingService Onboarding => Services.GetRequiredService<IOnboardingService>();

    private static TourDefinition Tour(params StepDefinition[] steps)
        => new() { Id = "ui", Steps = [.. steps] };

    private static StepDefinition Step(string id, string? title = null, string? selector = "#a") => new()
    {
        Id = id,
        Title = title ?? id,
        Description = $"Description of {id}.",
        Target = selector is null ? StepTarget.None : StepTarget.Css(selector),
    };

    // ---- Rendering ----------------------------------------------------------

    [Fact]
    public async Task Renders_Nothing_When_No_Tour_Is_Running()
    {
        var cut = Render<OnboardingRoot>();

        Assert.Empty(cut.FindAll(".bo-root"));

        // The live region is always present so announcements are never missed.
        Assert.NotNull(cut.Find(".bo-live"));
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Renders_The_Popover_For_The_Active_Step()
    {
        await Onboarding.StartAsync(Tour(Step("a", "Create a project"), Step("b", "Second")));

        var cut = Render<OnboardingRoot>();

        Assert.Equal("Create a project", cut.Find(".bo-title").TextContent);
        Assert.Contains("Description of a.", cut.Find(".bo-description").TextContent);
        Assert.NotNull(cut.Find(".bo-spotlight"));
    }

    [Fact]
    public async Task Gives_The_Dialog_Correct_Accessible_Semantics()
    {
        await Onboarding.StartAsync(Tour(Step("a", "Create a project")));

        var cut = Render<OnboardingRoot>();
        var dialog = cut.Find(".bo-popover");

        Assert.Equal("dialog", dialog.GetAttribute("role"));
        Assert.Equal("true", dialog.GetAttribute("aria-modal"));
        Assert.Equal("-1", dialog.GetAttribute("tabindex"));

        var labelledBy = dialog.GetAttribute("aria-labelledby");
        Assert.False(string.IsNullOrEmpty(labelledBy));
        Assert.Equal("Create a project", cut.Find($"#{labelledBy}").TextContent);

        var describedBy = dialog.GetAttribute("aria-describedby");
        Assert.False(string.IsNullOrEmpty(describedBy));
        Assert.Contains("Description of a.", cut.Find($"#{describedBy}").TextContent);
    }

    [Fact]
    public async Task Uses_A_Non_Modal_Dialog_When_The_Page_Stays_Interactive()
    {
        var step = Step("a");
        step.Interaction = InteractionMode.TargetOnly;

        await Onboarding.StartAsync(Tour(step));

        var cut = Render<OnboardingRoot>();

        Assert.Equal("false", cut.Find(".bo-popover").GetAttribute("aria-modal"));
    }

    [Fact]
    public async Task Falls_Back_To_A_Label_When_A_Step_Has_No_Title()
    {
        var step = Step("a", title: null);
        step.Title = null;

        await Onboarding.StartAsync(Tour(step));

        var cut = Render<OnboardingRoot>();
        var dialog = cut.Find(".bo-popover");

        Assert.Equal("Product tour", dialog.GetAttribute("aria-label"));
        Assert.True(string.IsNullOrEmpty(dialog.GetAttribute("aria-labelledby")));
        Assert.Equal("true", dialog.GetAttribute("data-bo-untitled"));
    }

    [Fact]
    public async Task Announces_Each_Step_In_A_Live_Region()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a", "First"), Step("b", "Second")));

        var cut = Render<OnboardingRoot>();
        var live = cut.Find(".bo-live");

        Assert.Equal("polite", live.GetAttribute("aria-live"));
        Assert.Contains("Step 1 of 2", live.TextContent);
        Assert.Contains("First", live.TextContent);

        await cut.InvokeAsync(() => session.NextAsync());

        Assert.Contains("Step 2 of 2", cut.Find(".bo-live").TextContent);
    }

    // ---- Buttons -------------------------------------------------------------

    [Fact]
    public async Task The_Next_Button_Advances_The_Tour()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a"), Step("b")));

        var cut = Render<OnboardingRoot>();
        await cut.Find("[data-bo-action='next']").ClickAsync(new());

        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Equal("b", cut.Find(".bo-title").TextContent);
    }

    [Fact]
    public async Task The_Back_Button_Only_Appears_After_The_First_Step()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a"), Step("b")));

        var cut = Render<OnboardingRoot>();
        Assert.Empty(cut.FindAll("[data-bo-action='previous']"));

        await cut.InvokeAsync(() => session.NextAsync());
        await cut.Find("[data-bo-action='previous']").ClickAsync(new());

        Assert.Equal("a", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task The_Forward_Button_Reads_Done_On_The_Last_Step()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a"), Step("b")));

        var cut = Render<OnboardingRoot>();
        Assert.Equal("Next", cut.Find("[data-bo-action='next']").TextContent);

        await cut.InvokeAsync(() => session.NextAsync());

        Assert.Equal("Done", cut.Find("[data-bo-action='next']").TextContent);
        // Skipping is meaningless once there is nothing left to skip.
        Assert.Empty(cut.FindAll("[data-bo-action='skip']"));
    }

    [Fact]
    public async Task The_Close_Button_Dismisses_The_Tour()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a")));

        var cut = Render<OnboardingRoot>();
        await cut.Find("[data-bo-action='close']").ClickAsync(new());

        Assert.Equal(TourStatus.Dismissed, session.Status);
        Assert.Empty(cut.FindAll(".bo-root"));
    }

    [Fact]
    public async Task The_Skip_Button_Ends_The_Tour_As_Skipped()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a"), Step("b")));

        var cut = Render<OnboardingRoot>();
        await cut.Find("[data-bo-action='skip']").ClickAsync(new());

        Assert.Equal(TourStatus.Skipped, session.Status);
    }

    [Fact]
    public async Task Custom_Actions_Render_And_Run()
    {
        var branched = false;
        var step = Step("a");
        step.Actions =
        [
            new StepAction
            {
                Id = "designer",
                Label = "I design",
                Style = StepActionStyle.Secondary,
                Effect = StepActionEffect.GoTo,
                TargetStepId = "c",
                OnClick = _ => { branched = true; return ValueTask.CompletedTask; },
            },
        ];

        var session = await Onboarding.StartAsync(Tour(step, Step("b"), Step("c")));

        var cut = Render<OnboardingRoot>();
        await cut.Find("[data-bo-action='designer']").ClickAsync(new());

        Assert.True(branched);
        Assert.Equal("c", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task A_Disabled_Action_Renders_As_Disabled()
    {
        var step = Step("a");
        step.Actions =
        [
            new StepAction { Id = "locked", Label = "Locked", IsDisabled = _ => true },
        ];

        await Onboarding.StartAsync(Tour(step));

        var cut = Render<OnboardingRoot>();

        Assert.True(cut.Find("[data-bo-action='locked']").HasAttribute("disabled"));
    }

    // ---- Progress -------------------------------------------------------------

    [Fact]
    public async Task Renders_Progress_Dots_With_A_Readable_Fallback()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a"), Step("b"), Step("c")));

        var cut = Render<OnboardingRoot>();

        Assert.Equal(3, cut.FindAll(".bo-dot").Count);
        Assert.Equal("current", cut.FindAll(".bo-dot")[0].GetAttribute("data-bo-state"));

        await cut.InvokeAsync(() => session.NextAsync());

        var dots = cut.FindAll(".bo-dot");
        Assert.Equal("done", dots[0].GetAttribute("data-bo-state"));
        Assert.Equal("current", dots[1].GetAttribute("data-bo-state"));
        Assert.Equal("todo", dots[2].GetAttribute("data-bo-state"));
    }

    [Fact]
    public async Task Renders_A_Progress_Bar_With_Aria_Values()
    {
        var tour = Tour(Step("a"), Step("b"), Step("c"));
        tour.Progress = ProgressStyle.Bar;

        var session = await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>();
        await cut.InvokeAsync(() => session.NextAsync());

        var bar = cut.Find("[role='progressbar']");
        Assert.Equal("2", bar.GetAttribute("aria-valuenow"));
        Assert.Equal("3", bar.GetAttribute("aria-valuemax"));
        Assert.Equal("2 of 3", bar.GetAttribute("aria-valuetext"));
    }

    [Fact]
    public async Task Progress_Can_Be_Turned_Off()
    {
        var tour = Tour(Step("a"), Step("b"));
        tour.Progress = ProgressStyle.None;

        await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>();

        Assert.Empty(cut.FindAll(".bo-dot"));
        Assert.Empty(cut.FindAll("[role='progressbar']"));
    }

    // ---- Overlay and interaction -----------------------------------------------

    [Fact]
    public async Task A_Blocked_Step_Renders_One_Full_Screen_Blocker()
    {
        await Onboarding.StartAsync(Tour(Step("a")));

        var cut = Render<OnboardingRoot>();

        Assert.Single(cut.FindAll(".bo-blocker"));
    }

    [Fact]
    public async Task An_Interactive_Step_Renders_Blockers_Around_The_Hole()
    {
        var step = Step("a");
        step.Interaction = InteractionMode.TargetOnly;

        await Onboarding.StartAsync(Tour(step));

        var cut = Render<OnboardingRoot>();

        // Four rectangles surrounding the spotlight, leaving the target clickable.
        Assert.Equal(4, cut.FindAll(".bo-blocker").Count);
    }

    [Fact]
    public async Task A_Free_Step_Renders_No_Blockers_At_All()
    {
        var step = Step("a");
        step.Interaction = InteractionMode.Free;

        await Onboarding.StartAsync(Tour(step));

        var cut = Render<OnboardingRoot>();

        Assert.Empty(cut.FindAll(".bo-blocker"));
    }

    [Fact]
    public async Task Clicking_The_Overlay_Closes_The_Tour_When_Configured()
    {
        var tour = Tour(Step("a"));
        tour.CloseOnOverlayClick = true;

        var session = await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>();
        await cut.Find(".bo-blocker").ClickAsync(new());

        Assert.Equal(TourStatus.Dismissed, session.Status);
    }

    [Fact]
    public async Task A_Targetless_Step_Renders_A_Flat_Overlay_And_No_Spotlight()
    {
        await Onboarding.StartAsync(Tour(Step("a", "Welcome", selector: null)));

        var cut = Render<OnboardingRoot>();

        Assert.Empty(cut.FindAll(".bo-spotlight"));
        Assert.NotNull(cut.Find(".bo-overlay"));
    }

    // ---- Styling and theming ----------------------------------------------------

    [Fact]
    public async Task Publishes_Its_Options_As_Css_Variables()
    {
        var tour = Tour(Step("a"));
        tour.ZIndex = 12345;
        tour.OverlayOpacity = 0.4;

        await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>();
        var style = cut.Find(".bo-root").GetAttribute("style")!;

        Assert.Contains("--bo-z:12345", style);
        Assert.Contains("--bo-overlay-opacity:0.4", style);
        // Invariant formatting: a comma here would silently break the page in some locales.
        Assert.DoesNotContain(",", style.Replace("translate3d", string.Empty));
    }

    [Fact]
    public async Task Applies_The_Theme_And_Direction_To_The_Root()
    {
        var tour = Tour(Step("a"));
        tour.Theme = OnboardingTheme.Dark;
        tour.Direction = OnboardingDirection.RightToLeft;

        await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>();
        var root = cut.Find(".bo-root");

        Assert.Equal("dark", root.GetAttribute("data-bo-theme"));
        Assert.Equal("rtl", root.GetAttribute("dir"));
    }

    [Fact]
    public async Task Extra_Classes_Reach_The_Root_And_The_Popover()
    {
        var tour = Tour(Step("a"));
        tour.PopoverClass = "tour-class";

        await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>(parameters => parameters
            .Add(p => p.Class, "app-class")
            .Add(p => p.PopoverClass, "host-class"));

        Assert.Contains("app-class", cut.Find(".bo-root").GetAttribute("class"));

        var popoverClass = cut.Find("[role='dialog']").GetAttribute("class")!;
        Assert.Contains("tour-class", popoverClass);
        Assert.Contains("host-class", popoverClass);
    }

    // ---- Waiting ------------------------------------------------------------------

    [Fact]
    public async Task Shows_A_Waiting_State_While_A_Target_Is_Missing()
    {
        var step = Step("a", selector: "#late");
        step.MissingTarget = MissingTargetBehavior.Wait;

        var session = await Onboarding.StartAsync(Tour(step));

        var cut = Render<OnboardingRoot>();
        Assert.NotNull(cut.Find(".bo-waiting"));

        await cut.InvokeAsync(() => _interop.AppearAsync(session.Id, "#late"));

        Assert.Empty(cut.FindAll(".bo-waiting"));
        Assert.Equal("a", cut.Find(".bo-title").TextContent);
    }

    // ---- Headless -------------------------------------------------------------------

    [Fact]
    public async Task A_Template_Replaces_The_Popover_Contents()
    {
        await Onboarding.StartAsync(Tour(Step("a", "Ignored")));

        RenderFragment<StepRenderContext> template = context => builder =>
        {
            builder.OpenElement(0, "p");
            builder.AddAttribute(1, "class", "custom");
            builder.AddContent(2, $"Custom {context.Step.Id}");
            builder.CloseElement();
        };

        var cut = Render<OnboardingRoot>(parameters => parameters.Add(p => p.ChildContent, template));

        Assert.Equal("Custom a", cut.Find(".custom").TextContent);
        Assert.Empty(cut.FindAll(".bo-title"));

        // The library still owns the dialog element, so accessibility survives the takeover.
        Assert.Equal("dialog", cut.Find("[role='dialog']").GetAttribute("role"));
    }

    [Fact]
    public async Task Unstyled_Drops_The_Built_In_Popover_Class()
    {
        await Onboarding.StartAsync(Tour(Step("a")));

        var cut = Render<OnboardingRoot>(parameters => parameters.Add(p => p.Unstyled, true));

        Assert.Empty(cut.FindAll(".bo-popover"));
        Assert.NotNull(cut.Find("[role='dialog']"));
    }

    [Fact]
    public async Task A_Step_Template_Wins_Over_A_Tour_Template()
    {
        var step = Step("a");
        step.Template = _ => builder => builder.AddMarkupContent(0, "<span class=\"from-step\">step</span>");

        var tour = Tour(step);
        tour.Template = _ => builder => builder.AddMarkupContent(0, "<span class=\"from-tour\">tour</span>");

        await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>();

        Assert.NotNull(cut.Find(".from-step"));
        Assert.Empty(cut.FindAll(".from-tour"));
    }

    // ---- Declarative markup -----------------------------------------------------------

    [Fact]
    public void A_Declarative_Tour_Registers_Its_Steps_In_Order()
    {
        var cut = Render<DeclarativeTourHarness>();

        var tour = Onboarding.GetTour("declared");

        Assert.NotNull(tour);
        Assert.Equal(["one", "two", "three"], tour!.Steps.Select(step => step.Id));
        Assert.Equal("Second", tour.Steps[1].Title);
        Assert.Equal(Placement.Right, tour.Steps[1].Placement);
        Assert.Equal("[data-bo-anchor=\"create\"]", TargetDescriptor.From(tour.Steps[0].Target).Selector);
        Assert.NotNull(cut.Instance);
    }

    [Fact]
    public async Task A_Declarative_Tour_Can_Be_Driven_Through_Its_Reference()
    {
        _interop.ExistingSelectors.Add("[data-bo-anchor=\"create\"]");

        var cut = Render<DeclarativeTourHarness>();
        var tour = cut.FindComponent<OnboardingTour>().Instance;

        await cut.InvokeAsync(() => tour.StartAsync());

        Assert.True(tour.IsRunning);
        Assert.Equal("one", tour.Session!.CurrentStep!.Id);

        await cut.InvokeAsync(() => tour.StopAsync());

        Assert.False(tour.IsRunning);
    }

    [Fact]
    public void A_Step_Outside_A_Tour_Fails_Loudly()
    {
        var failure = Assert.Throws<InvalidOperationException>(() => Render<OnboardingStep>());

        Assert.Contains("must be placed inside", failure.Message);
    }

    [Fact]
    public async Task A_Host_With_An_Explicit_Session_Claims_It_From_The_Ambient_Host()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a", "Claimed")));

        var ambient = Render<OnboardingRoot>();
        Assert.NotEmpty(ambient.FindAll(".bo-popover"));

        // A second host takes over rendering, so the tour is not drawn twice.
        var owner = Render<OnboardingRoot>(parameters => parameters
            .Add(p => p.Session, session)
            .Add(p => p.PopoverClass, "owned"));

        Assert.NotEmpty(owner.FindAll(".owned"));
        Assert.Empty(ambient.FindAll(".bo-popover"));

        // Releasing the claim hands the session back.
        await owner.Instance.DisposeAsync();
        ambient.Render();

        Assert.NotEmpty(ambient.FindAll(".bo-popover"));
    }

    [Fact]
    public async Task The_Host_Theme_Overrides_The_Tour_Theme()
    {
        var tour = Tour(Step("a"));
        tour.Theme = OnboardingTheme.Light;

        await Onboarding.StartAsync(tour);

        var cut = Render<OnboardingRoot>(parameters => parameters.Add(p => p.Theme, OnboardingTheme.Dark));

        Assert.Equal("dark", cut.Find(".bo-root").GetAttribute("data-bo-theme"));
    }

    // ---- Cleanup ---------------------------------------------------------------------

    [Fact]
    public async Task Disposing_The_Host_Unsubscribes_From_The_Session()
    {
        var session = await Onboarding.StartAsync(Tour(Step("a"), Step("b")));

        var cut = Render<OnboardingRoot>();
        await cut.Instance.DisposeAsync();

        // Nothing should throw or re-render after the host is gone.
        await session.NextAsync();

        Assert.Equal("b", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task Registers_The_Popover_Element_With_The_Browser_Layer()
    {
        await Onboarding.StartAsync(Tour(Step("a")));

        Render<OnboardingRoot>();

        Assert.NotNull(_interop.Popover);
        Assert.True(_interop.TrapFocus);
        Assert.True(_interop.FocusPopoverCount > 0);
    }
}

/// <summary>Markup fixture for the declarative component tests.</summary>
public sealed class DeclarativeTourHarness : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenComponent<OnboardingTour>(0);
        builder.AddComponentParameter(1, nameof(OnboardingTour.Id), "declared");
        builder.AddComponentParameter(2, nameof(OnboardingTour.Title), "Declared tour");
        builder.AddComponentParameter(3, nameof(OnboardingTour.ChildContent), (RenderFragment)(child =>
        {
            child.OpenComponent<OnboardingStep>(0);
            child.AddComponentParameter(1, nameof(OnboardingStep.Id), "one");
            child.AddComponentParameter(2, nameof(OnboardingStep.Anchor), "create");
            child.AddComponentParameter(3, nameof(OnboardingStep.Title), "First");
            child.CloseComponent();

            child.OpenComponent<OnboardingStep>(4);
            child.AddComponentParameter(5, nameof(OnboardingStep.Id), "two");
            child.AddComponentParameter(6, nameof(OnboardingStep.Selector), "#b");
            child.AddComponentParameter(7, nameof(OnboardingStep.Title), "Second");
            child.AddComponentParameter(8, nameof(OnboardingStep.Placement), (Placement?)Placement.Right);
            child.CloseComponent();

            child.OpenComponent<OnboardingStep>(9);
            child.AddComponentParameter(10, nameof(OnboardingStep.Id), "three");
            child.AddComponentParameter(11, nameof(OnboardingStep.Title), "Third");
            child.CloseComponent();
        }));
        builder.CloseComponent();
    }
}
