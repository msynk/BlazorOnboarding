using BlazorOnboarding.Tests.Infrastructure;

namespace BlazorOnboarding.Tests;

public class TourSessionTests
{
    private static TourDefinition Tour(params StepDefinition[] steps)
        => new() { Id = "test-tour", Steps = [.. steps] };

    private static StepDefinition Step(string id, string? selector = null) => new()
    {
        Id = id,
        Target = selector is null ? StepTarget.None : StepTarget.Css(selector),
        Title = id,
    };

    // ---- Basic navigation ---------------------------------------------------

    [Fact]
    public async Task Starts_On_The_First_Step()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));

        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Equal("a", session.CurrentStep!.Id);
        Assert.Equal(1, session.DisplayPosition);
        Assert.Equal(2, session.DisplayCount);
        Assert.True(session.IsFirstStep);
        Assert.False(session.IsLastStep);
    }

    [Fact]
    public async Task Moves_Forward_And_Back()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b"), Step("c", "#c")));

        await session.NextAsync();
        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.False(session.IsFirstStep);

        await session.NextAsync();
        Assert.Equal("c", session.CurrentStep!.Id);
        Assert.True(session.IsLastStep);

        await session.PreviousAsync();
        Assert.Equal("b", session.CurrentStep!.Id);

        await session.PreviousAsync();
        Assert.Equal("a", session.CurrentStep!.Id);
        Assert.True(session.IsFirstStep);
    }

    [Fact]
    public async Task Next_On_The_Last_Step_Completes_The_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        await session.NextAsync();

        Assert.Equal(TourStatus.Completed, session.Status);
        Assert.Equal(TourEndReason.Completed, session.EndReason);
        Assert.Null(session.CurrentStep);
        Assert.False(session.IsActive);
    }

    [Fact]
    public async Task Previous_On_The_First_Step_Does_Nothing()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));
        await session.PreviousAsync();

        Assert.Equal("a", session.CurrentStep!.Id);
        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task Jumps_To_A_Step_By_Id()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b"), Step("c", "#c")));
        await session.GoToAsync("c");

        Assert.Equal("c", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task Jumping_To_An_Unknown_Step_Fails_The_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        await session.GoToAsync("nope");

        Assert.Equal(TourStatus.Failed, session.Status);
        Assert.IsType<OnboardingException>(session.Error);
    }

    [Fact]
    public async Task Skip_And_Dismiss_Record_Their_Own_Reasons()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var skipped = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        await skipped.SkipAsync();
        Assert.Equal(TourStatus.Skipped, skipped.Status);

        var dismissed = await harness.Service.StartAsync(
            new TourDefinition { Id = "other", Steps = [Step("a", "#a")] });
        await dismissed.DismissAsync();
        Assert.Equal(TourStatus.Dismissed, dismissed.Status);
    }

    // ---- Conditional steps --------------------------------------------------

    [Fact]
    public async Task Skips_Steps_Whose_Condition_Is_False()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var hidden = Step("b", "#b");
        hidden.When = _ => ValueTask.FromResult(false);

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), hidden, Step("c", "#c")));

        Assert.Equal(2, session.DisplayCount);

        await session.NextAsync();
        Assert.Equal("c", session.CurrentStep!.Id);

        await session.PreviousAsync();
        Assert.Equal("a", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task Conditions_Are_Re_Evaluated_On_Every_Transition()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var conditional = Step("b", "#b");
        conditional.When = context => ValueTask.FromResult(context.State.ContainsKey("unlocked"));

        var first = Step("a", "#a");
        first.OnBeforeLeave = context =>
        {
            context.State["unlocked"] = true;
            return ValueTask.CompletedTask;
        };

        var session = await harness.Service.StartAsync(Tour(first, conditional, Step("c", "#c")));

        // The gate is closed while the first step is showing.
        Assert.Equal(2, session.DisplayCount);

        await session.NextAsync();

        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Equal(3, session.DisplayCount);
    }

    [Fact]
    public async Task A_Condition_That_Excludes_Everything_Completes_The_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var step = Step("a", "#a");
        step.When = _ => ValueTask.FromResult(false);

        var session = await harness.Service.StartAsync(Tour(step));

        Assert.Equal(TourStatus.Completed, session.Status);
    }

    // ---- Branching ----------------------------------------------------------

    [Fact]
    public async Task Branches_Through_ResolveNext()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var fork = Step("a", "#a");
        fork.ResolveNext = _ => ValueTask.FromResult<string?>("c");

        var session = await harness.Service.StartAsync(Tour(fork, Step("b", "#b"), Step("c", "#c")));
        await session.NextAsync();

        Assert.Equal("c", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task Back_Follows_The_Visit_History_Not_The_Declaration_Order()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var fork = Step("a", "#a");
        fork.ResolveNext = _ => ValueTask.FromResult<string?>("c");

        var session = await harness.Service.StartAsync(Tour(fork, Step("b", "#b"), Step("c", "#c")));
        await session.NextAsync();
        await session.PreviousAsync();

        // Back from c must land on a, the step actually visited, not on b.
        Assert.Equal("a", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task ResolveNext_Can_End_The_Tour_Early()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var fork = Step("a", "#a");
        fork.ResolveNext = _ => ValueTask.FromResult<string?>(TourDefinition.EndStepId);

        var session = await harness.Service.StartAsync(Tour(fork, Step("b", "#b")));
        await session.NextAsync();

        Assert.Equal(TourStatus.Completed, session.Status);
    }

    [Fact]
    public async Task A_Branching_Step_Is_Never_Reported_As_Last()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var fork = Step("a", "#a");
        fork.ResolveNext = _ => ValueTask.FromResult<string?>(null);

        var session = await harness.Service.StartAsync(Tour(fork));

        Assert.False(session.IsLastStep);
    }

    // ---- Guards -------------------------------------------------------------

    [Fact]
    public async Task CanAdvance_Blocks_Forward_Navigation()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var allow = false;
        var gated = Step("a", "#a");
        gated.CanAdvance = _ => ValueTask.FromResult(allow);

        var session = await harness.Service.StartAsync(Tour(gated, Step("b", "#b")));

        await session.NextAsync();
        Assert.Equal("a", session.CurrentStep!.Id);

        allow = true;
        await session.NextAsync();
        Assert.Equal("b", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task A_Throwing_Hook_Fails_The_Tour_Rather_Than_Escaping()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var step = Step("a", "#a");
        step.OnBeforeLeave = _ => throw new InvalidOperationException("validation failed");

        var session = await harness.Service.StartAsync(Tour(step, Step("b", "#b")));
        await session.NextAsync();

        Assert.Equal(TourStatus.Failed, session.Status);
        Assert.Equal("validation failed", session.Error!.Message);
    }

    // ---- Missing targets ----------------------------------------------------

    [Fact]
    public async Task Waits_For_A_Missing_Target_Then_Shows_It_When_It_Appears()
    {
        await using var harness = new OnboardingHarness();

        var step = Step("a", "#late");
        step.MissingTarget = MissingTargetBehavior.Wait;

        var session = await harness.Service.StartAsync(Tour(step));

        Assert.Equal(TourStatus.Waiting, session.Status);
        Assert.True(session.IsWaiting);
        Assert.True(harness.Interop.LastActivation!.WaitForTarget);

        await harness.Interop.AppearAsync(session.Id, "#late");

        Assert.Equal(TourStatus.Running, session.Status);
        Assert.NotNull(session.TargetRect);
    }

    [Fact]
    public async Task Skips_A_Step_Whose_Target_Is_Missing()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#b");

        var missing = Step("a", "#gone");
        missing.MissingTarget = MissingTargetBehavior.Skip;

        var session = await harness.Service.StartAsync(Tour(missing, Step("b", "#b")));

        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Contains(OnboardingEventKind.StepSkipped, harness.Events.Kinds);
    }

    [Fact]
    public async Task Centres_A_Step_Whose_Target_Is_Missing()
    {
        await using var harness = new OnboardingHarness();

        var missing = Step("a", "#gone");
        missing.MissingTarget = MissingTargetBehavior.Center;

        var session = await harness.Service.StartAsync(Tour(missing));

        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Equal("a", session.CurrentStep!.Id);
        Assert.Null(session.TargetRect);
    }

    [Fact]
    public async Task Fails_A_Step_Whose_Target_Is_Missing_When_Asked_To()
    {
        await using var harness = new OnboardingHarness();

        var missing = Step("a", "#gone");
        missing.MissingTarget = MissingTargetBehavior.Fail;

        var session = await harness.Service.StartAsync(Tour(missing));

        Assert.Equal(TourStatus.Failed, session.Status);
        Assert.IsType<OnboardingException>(session.Error);
    }

    [Fact]
    public async Task A_Wait_That_Times_Out_Applies_The_Timeout_Behaviour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#b");

        var missing = Step("a", "#never");
        missing.MissingTarget = MissingTargetBehavior.Wait;
        missing.WaitTimeoutBehavior = MissingTargetBehavior.Skip;

        var session = await harness.Service.StartAsync(Tour(missing, Step("b", "#b")));
        Assert.Equal(TourStatus.Waiting, session.Status);

        await harness.Interop.TimeOutWaitAsync(session.Id);

        Assert.Equal("b", session.CurrentStep!.Id);
        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task A_Wait_Timeout_Never_Loops_Back_Into_Waiting()
    {
        await using var harness = new OnboardingHarness();

        var missing = Step("a", "#never");
        missing.MissingTarget = MissingTargetBehavior.Wait;
        missing.WaitTimeoutBehavior = MissingTargetBehavior.Wait;

        var session = await harness.Service.StartAsync(Tour(missing));
        await harness.Interop.TimeOutWaitAsync(session.Id);

        // Waiting again would hang forever, so the timeout degrades to skipping.
        Assert.NotEqual(TourStatus.Waiting, session.Status);
    }

    [Fact]
    public async Task Losing_A_Target_Mid_Step_Returns_To_Waiting()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var step = Step("a", "#a");
        step.MissingTarget = MissingTargetBehavior.Wait;

        var session = await harness.Service.StartAsync(Tour(step));
        Assert.Equal(TourStatus.Running, session.Status);

        await harness.Interop.LoseTargetAsync(session.Id);

        Assert.Equal(TourStatus.Waiting, session.Status);
        Assert.Null(session.TargetRect);
    }

    // ---- Interaction --------------------------------------------------------

    [Fact]
    public async Task An_AdvanceOn_Step_Lets_Pointer_Events_Reach_The_Target()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var interactive = Step("a", "#a");
        interactive.AdvanceOn = AdvanceTrigger.TargetClick;

        var session = await harness.Service.StartAsync(Tour(interactive, Step("b", "#b")));

        Assert.Equal(InteractionMode.TargetOnly, session.CurrentOptions!.Interaction);
        Assert.Equal("target", harness.Interop.LastActivation!.Interaction);

        await harness.Interop.TriggerAdvanceAsync(session.Id);

        Assert.Equal("b", session.CurrentStep!.Id);
    }

    [Fact]
    public async Task An_Explicit_Interaction_Mode_Beats_The_AdvanceOn_Default()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var step = Step("a", "#a");
        step.AdvanceOn = AdvanceTrigger.TargetClick;
        step.Interaction = InteractionMode.Free;

        var session = await harness.Service.StartAsync(Tour(step));

        Assert.Equal(InteractionMode.Free, session.CurrentOptions!.Interaction);
    }

    [Fact]
    public async Task Escape_Dismisses_The_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        await harness.Interop.PressKeyAsync(session.Id, "Escape");

        Assert.Equal(TourStatus.Dismissed, session.Status);
    }

    [Fact]
    public async Task Escape_Is_Ignored_When_Disabled()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var step = Step("a", "#a");
        step.CloseOnEscape = false;

        var session = await harness.Service.StartAsync(Tour(step));
        await harness.Interop.PressKeyAsync(session.Id, "Escape");

        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task Arrow_Keys_Navigate_And_Reverse_In_RightToLeft()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var tour = Tour(Step("a", "#a"), Step("b", "#b"));
        var session = await harness.Service.StartAsync(tour);

        await harness.Interop.PressKeyAsync(session.Id, "ArrowRight");
        Assert.Equal("b", session.CurrentStep!.Id);

        await harness.Interop.PressKeyAsync(session.Id, "ArrowLeft");
        Assert.Equal("a", session.CurrentStep!.Id);

        await session.DismissAsync();

        var rtlTour = new TourDefinition
        {
            Id = "rtl",
            Direction = OnboardingDirection.RightToLeft,
            Steps = [Step("a", "#a"), Step("b", "#b")],
        };

        var rtl = await harness.Service.StartAsync(rtlTour);
        await harness.Interop.PressKeyAsync(rtl.Id, "ArrowLeft");

        Assert.Equal("b", rtl.CurrentStep!.Id);
    }

    [Fact]
    public async Task Overlay_Clicks_Only_Close_When_Enabled()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        await harness.Interop.ClickOverlayAsync(session.Id);
        Assert.Equal(TourStatus.Running, session.Status);

        await session.DismissAsync();

        var closable = new TourDefinition
        {
            Id = "closable",
            CloseOnOverlayClick = true,
            Steps = [Step("a", "#a")],
        };

        var second = await harness.Service.StartAsync(closable);
        await harness.Interop.ClickOverlayAsync(second.Id);

        Assert.Equal(TourStatus.Dismissed, second.Status);
    }

    // ---- Custom actions -----------------------------------------------------

    [Fact]
    public async Task A_Custom_Action_Runs_Its_Handler_Then_Applies_Its_Effect()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c");

        var clicked = false;
        var action = new StepAction
        {
            Label = "Designer",
            Effect = StepActionEffect.GoTo,
            TargetStepId = "c",
            OnClick = _ => { clicked = true; return ValueTask.CompletedTask; },
        };

        var step = Step("a", "#a");
        step.Actions = [action];

        var session = await harness.Service.StartAsync(Tour(step, Step("b", "#b"), Step("c", "#c")));
        await session.InvokeActionAsync(action);

        Assert.True(clicked);
        Assert.Equal("c", session.CurrentStep!.Id);
        Assert.Contains(OnboardingEventKind.ActionInvoked, harness.Events.Kinds);
    }

    [Fact]
    public async Task An_Action_Handler_That_Ends_The_Tour_Suppresses_Its_Effect()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        ITourSession? captured = null;

        var action = new StepAction
        {
            Label = "Stop",
            Effect = StepActionEffect.Next,
            OnClick = context =>
            {
                captured = context.Session;
                return new ValueTask(context.Session.DismissAsync());
            },
        };

        var step = Step("a", "#a");
        step.Actions = [action];

        var session = await harness.Service.StartAsync(Tour(step, Step("b", "#b")));
        await session.InvokeActionAsync(action);

        Assert.Same(session, captured);
        Assert.Equal(TourStatus.Dismissed, session.Status);
    }

    // ---- Pause and resume ---------------------------------------------------

    [Fact]
    public async Task Pause_Hides_The_Tour_And_Resume_Restores_The_Same_Step()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));
        await session.NextAsync();

        await session.PauseAsync();
        Assert.Equal(TourStatus.Paused, session.Status);
        Assert.False(session.IsActive);

        await session.ResumeAsync();
        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Equal("b", session.CurrentStep!.Id);
    }

    // ---- Cleanup and races ---------------------------------------------------

    [Fact]
    public async Task Ending_A_Tour_Detaches_Everything_It_Owns()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        Assert.Single(harness.Interop.AttachedSessions);

        await session.CompleteAsync();

        Assert.Empty(harness.Interop.AttachedSessions);
        Assert.True(harness.Interop.DeactivateCount > 0);
        Assert.Equal(1, harness.Interop.DetachCount);
    }

    [Fact]
    public async Task Rapid_Navigation_Never_Interleaves_Transitions()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b", "#c", "#d");

        var session = await harness.Service.StartAsync(
            Tour(Step("a", "#a"), Step("b", "#b"), Step("c", "#c"), Step("d", "#d")));

        // Three simultaneous clicks on Next, as an impatient user produces.
        await Task.WhenAll(session.NextAsync(), session.NextAsync(), session.NextAsync());

        Assert.Equal("d", session.CurrentStep!.Id);
        Assert.Equal(TourStatus.Running, session.Status);
    }

    [Fact]
    public async Task Commands_After_The_Tour_Ends_Are_Ignored()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));
        await session.DismissAsync();

        await session.NextAsync();
        await session.PreviousAsync();
        await session.RefreshAsync();

        Assert.Equal(TourStatus.Dismissed, session.Status);
        Assert.Null(session.CurrentStep);
    }

    [Fact]
    public async Task Starting_A_Second_Tour_Ends_The_First_By_Default()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var first = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        var second = await harness.Service.StartAsync(
            new TourDefinition { Id = "second", Steps = [Step("a", "#a")] });

        Assert.False(first.IsActive);
        Assert.True(second.IsActive);
        Assert.Same(second, harness.Service.Active);
    }

    [Fact]
    public async Task Independent_Sessions_Can_Run_Side_By_Side()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var first = await harness.Service.StartAsync(Tour(Step("a", "#a")));
        var second = await harness.Service.StartAsync(
            new TourDefinition { Id = "second", Steps = [Step("a", "#a")] },
            new StartOptions { StopOthers = false });

        Assert.True(first.IsActive);
        Assert.True(second.IsActive);
        Assert.Equal(2, harness.Interop.AttachedSessions.Count);
    }

    // ---- Focus --------------------------------------------------------------

    [Fact]
    public async Task Focus_Is_Captured_On_The_First_Step_And_Restored_At_The_End()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = (TourSession)await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));

        await session.FocusPopoverAsync();
        await session.NextAsync();
        await session.FocusPopoverAsync();

        Assert.Equal(1, harness.Interop.CaptureFocusCount);
        Assert.Equal(2, harness.Interop.FocusPopoverCount);

        await session.CompleteAsync();

        Assert.Equal(1, harness.Interop.RestoreFocusCount);
    }

    [Fact]
    public async Task Focus_Is_Not_Restored_When_The_Tour_Opts_Out()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        var tour = new TourDefinition { Id = "no-restore", RestoreFocus = false, Steps = [Step("a", "#a")] };
        var session = (TourSession)await harness.Service.StartAsync(tour);

        await session.FocusPopoverAsync();
        await session.CompleteAsync();

        Assert.Equal(0, harness.Interop.RestoreFocusCount);
    }

    // ---- Multiple targets ---------------------------------------------------

    [Fact]
    public async Task Additional_Targets_Are_Sent_To_The_Browser_Layer()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#extra");

        var step = Step("a", "#a");
        step.AdditionalTargets = [StepTarget.Css("#extra")];

        var session = await harness.Service.StartAsync(Tour(step));

        var activation = harness.Interop.LastActivation!;
        Assert.NotNull(activation.AdditionalTargets);
        Assert.Equal("#extra", Assert.Single(activation.AdditionalTargets!).Selector);
        Assert.True(session.HasTarget);
    }

    // ---- Dynamic targets -----------------------------------------------------

    [Fact]
    public async Task A_Dynamic_Target_Is_Resolved_When_The_Step_Activates()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#chosen");

        var step = Step("a");
        step.Target = StepTarget.Dynamic(_ => StepTarget.Css("#chosen"));

        var session = await harness.Service.StartAsync(Tour(step));

        Assert.Equal("#chosen", harness.Interop.LastActivation!.Target.Selector);
        Assert.True(session.HasTarget);
    }

    [Fact]
    public async Task A_Dynamic_Target_Returning_Null_Is_Treated_As_No_Target()
    {
        await using var harness = new OnboardingHarness();

        var step = Step("a");
        step.Target = StepTarget.Dynamic(_ => (StepTarget?)null);

        var session = await harness.Service.StartAsync(Tour(step));

        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Equal(TargetKind.None, harness.Interop.LastActivation!.Target.Kind);
    }

    // ---- Anchors -------------------------------------------------------------

    [Fact]
    public async Task An_Anchor_Target_Becomes_A_Data_Attribute_Selector()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("[data-bo-anchor=\"create\"]");

        var step = Step("a");
        step.Target = StepTarget.Anchor("create");

        var session = await harness.Service.StartAsync(Tour(step));

        Assert.Equal("[data-bo-anchor=\"create\"]", harness.Interop.LastActivation!.Target.Selector);
        Assert.True(session.HasTarget);
    }

    // ---- Events ---------------------------------------------------------------

    [Fact]
    public async Task Publishes_A_Usable_Analytics_Stream()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a", "#b");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));
        await session.NextAsync();
        await session.CompleteAsync();

        var kinds = harness.Events.Kinds;

        Assert.Equal(OnboardingEventKind.TourStarted, kinds[0]);
        Assert.Contains(OnboardingEventKind.StepShown, kinds);
        Assert.Contains(OnboardingEventKind.StepLeft, kinds);
        Assert.Equal(OnboardingEventKind.TourCompleted, kinds[^1]);

        var completed = harness.Events.Events[^1];
        Assert.Equal(TourEndReason.Completed, completed.Reason);
        Assert.Equal("test-tour", completed.Tour.Id);

        // Step durations are what make funnel analysis possible.
        Assert.Contains(harness.Events.Events, e => e.Kind == OnboardingEventKind.StepLeft && e.Duration is not null);
    }

    [Fact]
    public async Task A_Throwing_Observer_Cannot_Break_A_Tour()
    {
        await using var harness = new OnboardingHarness();
        harness.WithElements("#a");

        harness.Service.EventRaised += (_, _) => throw new InvalidOperationException("analytics is down");

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a")));

        Assert.Equal(TourStatus.Running, session.Status);
    }

    // ---- Prerendering ----------------------------------------------------------

    [Fact]
    public async Task Runs_Without_A_Browser()
    {
        await using var harness = new OnboardingHarness();
        harness.Interop.IsAvailable = false;

        var session = await harness.Service.StartAsync(Tour(Step("a", "#a"), Step("b", "#b")));

        // No DOM means no measurement, but the engine still advances so a prerendered pass is
        // harmless rather than fatal.
        Assert.Equal(TourStatus.Running, session.Status);
        Assert.Null(session.Placement);
        Assert.Empty(harness.Interop.Activations);

        await session.NextAsync();
        Assert.Equal("b", session.CurrentStep!.Id);
    }
}
