using System.Windows.Controls;
using System.Windows.Data;
using System.IO;
using System.Windows.Markup;
using System.Windows.Threading;
using System.Xml.Linq;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.UI;

namespace YuGiOhOverlay.UI.Tests;

public sealed class GuideSelectionTests
{
    [Fact]
    public Task DefaultProgressBindingReproducesReadOnlyPropertyCrash() => OnDispatcher(dispatcher =>
    {
        var model = new AssistantViewModel(dispatcher);
        var bar = new ProgressBar();
        try
        {
            var error = Assert.Throws<InvalidOperationException>(() => bar.SetBinding(ProgressBar.ValueProperty,
                new Binding(nameof(model.Progress)) { Source = model }));
            Assert.Contains(nameof(model.Progress), error.Message);
        }
        finally { BindingOperations.ClearBinding(bar, ProgressBar.ValueProperty); }
        return Task.CompletedTask;
    });

    [Fact]
    public Task ProductionProgressXamlLoadsAndTracksGuideWithoutWritingToModel() => OnDispatcher(async dispatcher =>
    {
        // Read the real XAML, so a regression in the view cannot be hidden by a
        // hand-written test binding with a different Mode.
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        var document = XDocument.Load(Path.Combine(AppContext.BaseDirectory, "Fixtures", "OverlayWindow.xaml"));
        var progress = new XElement(Assert.Single(document.Descendants(presentation + "ProgressBar")));
        var wrapper = new XElement(presentation + "Grid", new XAttribute(XNamespace.Xmlns + "x", xaml),
            new XElement(presentation + "Grid.Resources", new XElement(presentation + "SolidColorBrush",
                new XAttribute(xaml + "Key", "Accent"), new XAttribute("Color", "#72E3C2"))), progress);
        var grid = (Grid)XamlReader.Parse(wrapper.ToString());
        var bar = Assert.IsType<ProgressBar>(Assert.Single(grid.Children.Cast<object>()));
        var (model, _, action) = Create(dispatcher);
        grid.DataContext = model;
        await Present(model, [action]);
        await Flush(dispatcher);
        Assert.Equal(0, bar.Value);
        model.MoveStep(1);
        await Flush(dispatcher);
        Assert.Equal(model.Progress, bar.Value, 6);
        Assert.True(bar.Value > 0);
        model.ExitCombo();
        await Flush(dispatcher);
        Assert.Equal(0, bar.Value);
    });

    [Fact]
    public Task FollowingGuideSurvivesHandChangeAndListReset() => OnDispatcher(async dispatcher =>
    {
        var (model, list, action) = Create(dispatcher);
        await Present(model, [action]);
        await Flush(dispatcher);
        Assert.Equal(action, model.Selected);
        model.MoveStep(1);

        await Present(model, []);
        await Flush(dispatcher);

        Assert.Null(list.SelectedItem);
        Assert.Equal(action, model.Selected);
        Assert.Equal(action.Combo.Actions[1], model.StepText);
        model.ToggleFollow();
        Assert.Null(model.Selected);
    });

    [Fact]
    public Task AutomaticSuggestionSurvivesReplacementOfListItems() => OnDispatcher(async dispatcher =>
    {
        var (model, list, action) = Create(dispatcher);
        await Present(model, [action]);
        await Flush(dispatcher);
        var next = action with { Confidence = .91 };

        await Present(model, [next]);
        await Flush(dispatcher);

        Assert.Equal(next, model.Selected);
        Assert.Equal(next, list.SelectedItem);
        await Present(model, []);
        await Flush(dispatcher);
        Assert.Null(model.Selected);
    });

    [Fact]
    public Task FollowHidesStartersAndExitRejectsLateObservation() => OnDispatcher(async dispatcher =>
    {
        var (model, _, action) = Create(dispatcher);
        await Present(model, [action]);
        var beforeFollow = model.Session.Snapshot.Revision;
        model.ToggleFollow();
        Assert.True(model.IsFollowing);
        Assert.Empty(model.Hand); Assert.Empty(model.Recommendations);
        Assert.Equal("Quitter le combo", model.FollowLabel);
        await model.PresentAsync(new("Old hand", new(DateTimeOffset.UtcNow, [], true), [action], SessionRevision: beforeFollow), default);
        Assert.Empty(model.Recommendations); Assert.Equal(action, model.Selected);
        var duringGuide = model.Session.Snapshot.Revision;
        model.ExitCombo();
        await model.PresentAsync(new("Late guide", new(DateTimeOffset.UtcNow, [], true), [action], SessionRevision: duringGuide), default);
        Assert.Null(model.Selected); Assert.Empty(model.Recommendations); Assert.False(model.IsFollowing);
    });

    [Fact]
    public Task SuspendKeepsComboAndStepUntilExplicitExit() => OnDispatcher(async dispatcher =>
    {
        var (model, _, action) = Create(dispatcher);
        await Present(model, [action]);
        model.MoveStep(1);
        var revision = model.Session.Snapshot.Revision;
        model.Suspend();
        Assert.True(model.IsFollowing);
        Assert.Equal(action.Combo.Actions[1], model.StepText);
        Assert.True(model.Session.Snapshot.Revision > revision);
        model.ExitCombo(); Assert.False(model.IsFollowing);
    });

    [Fact]
    public void StructuredGuideSeparatesCardActionDetailAndChoices()
    {
        var step = new ComboStep([new("a", "Card A", "Invoquer", "Normal summon", "arrow")], "Choose",
            [new("If allowed", [new([new("b", "Card B", "Ajouter", "To hand", "")], "", [])])]);
        var lines = GuideTextFormatter.Format(step, "fallback");
        Assert.Equal("Card A", lines[0].CardName); Assert.Equal("Invoquer", lines[0].Badge);
        Assert.Equal("Normal summon", lines[0].Detail); Assert.Equal("Puis ↓", lines[0].Connector);
        Assert.True(lines[1].IsBranch); Assert.Equal("Card B", lines[2].CardName);
        Assert.Equal("Text\nNext & more", GuideTextFormatter.PlainText("<p>Text</p>Next &amp; more"));
    }

    private static (AssistantViewModel Model, ListBox List, RecommendedAction Action) Create(Dispatcher dispatcher)
    {
        var catalog = new DemoSession().Catalog;
        var model = new AssistantViewModel(dispatcher);
        model.SetCatalog(catalog);
        var list = new ListBox { DataContext = model };
        list.SetBinding(ItemsControl.ItemsSourceProperty, new Binding(nameof(model.Recommendations)));
        list.SetBinding(System.Windows.Controls.Primitives.Selector.SelectedItemProperty,
            new Binding(nameof(model.SelectedSuggestion)) { Mode = BindingMode.TwoWay });
        return (model, list, new(catalog.Rules[0], .95, "Synthetic hand"));
    }

    private static Task Present(AssistantViewModel model, IReadOnlyList<RecommendedAction> recommendations) =>
        model.PresentAsync(new("Observation", new(DateTimeOffset.UtcNow, [], true), recommendations), default);

    private static Task Flush(Dispatcher dispatcher) => dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle).Task;

    private static async Task OnDispatcher(Func<Dispatcher, Task> test)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            var dispatcher = Dispatcher.CurrentDispatcher;
            dispatcher.InvokeAsync(async () =>
            {
                try { await test(dispatcher); completion.SetResult(); }
                catch (Exception ex) { completion.SetException(ex); }
                finally { dispatcher.BeginInvokeShutdown(DispatcherPriority.Background); }
            });
            Dispatcher.Run();
        })
        { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        await completion.Task.WaitAsync(TimeSpan.FromSeconds(15));
    }
}
