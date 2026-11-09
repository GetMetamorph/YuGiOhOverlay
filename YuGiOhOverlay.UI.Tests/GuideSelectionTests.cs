using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.UI;

namespace YuGiOhOverlay.UI.Tests;

public sealed class GuideSelectionTests
{
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
