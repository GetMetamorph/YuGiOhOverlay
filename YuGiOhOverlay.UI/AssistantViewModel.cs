using System.ComponentModel;
using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Media;
using System.Windows;
using System.Globalization;
using System.Windows.Threading;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Domain;

namespace YuGiOhOverlay.UI;

public sealed record HandItem(string Name, string Confidence, NormalizedRect Zone);

public sealed class AssistantViewModel(Dispatcher dispatcher, GuideSession? session = null) : INotifyPropertyChanged, IOverlayPresenter
{
    private ComboCatalog? _catalog;
    private RecommendedAction? _selected;
    public GuideSession Session { get; } = session ?? new GuideSession();
    public bool IsFollowing => Session.Snapshot.IsFollowing;
    public Visibility SearchVisibility => IsFollowing ? Visibility.Collapsed : Visibility.Visible;
    public Visibility GuideVisibility => IsFollowing ? Visibility.Visible : Visibility.Collapsed;
    public bool AutoAdvance
    {
        get => Session.Snapshot.AutoAdvance;
        set { Session.SetAutoAdvance(value); GuideStatus = value ? "Observation automatique activée" : "Progression manuelle"; Refresh(); }
    }
    public string Status { get; private set; } = "Observation · configuration requise";
    public string GuideStatus { get; private set; } = "";
    public string DeckName => _catalog?.DeckName ?? "Aucun deck synchronisé";
    public IReadOnlyList<HandItem> Hand { get; private set; } = [];
    public IReadOnlyList<RecommendedAction> Recommendations { get; private set; } = [];
    public BitmapSource? DiagnosticImage { get; private set; }
    public string Timing { get; private set; } = "";
    public RecommendedAction? Selected => _selected;
    public RecommendedAction? SelectedSuggestion
    {
        get => IsFollowing ? null : Recommendations.FirstOrDefault(r => r.Combo.Id == Selected?.Combo.Id);
        set
        {
            if (IsFollowing || value is null || value.Combo.Id == Selected?.Combo.Id) return;
            _selected = value;
            Refresh();
        }
    }
    private int StepIndex => Session.Snapshot.StepIndex;
    public string ComboTitle => Selected?.Combo.Title ?? "En attente d’une main compatible";
    public string StepLabel => Selected is null ? "GUIDE DE COMBO" : Session.Snapshot.Completed ? "DERNIÈRE ÉTAPE OBSERVÉE" : $"ÉTAPE {StepIndex + 1} / {Selected.Combo.Actions.Count}";
    public string StepText => Selected?.Combo.Actions.ElementAtOrDefault(StepIndex) ?? "Laisse la main visible et stable. Les cartes inconnues ne sont pas forcées vers une correspondance.";
    public IReadOnlyList<GuideLine> StepLines => GuideTextFormatter.Format(Selected?.Combo.GuideSteps?.ElementAtOrDefault(StepIndex), StepText);
    public string NextStep => Selected is not null && StepIndex + 1 < Selected.Combo.Actions.Count
        ? "Ensuite · " + GuideTextFormatter.Summary(Selected.Combo.GuideSteps?.ElementAtOrDefault(StepIndex + 1), Selected.Combo.Actions[StepIndex + 1]) : "Dernière étape du guide";
    public double Progress => Selected is null ? 0 : 100.0 * (Session.Snapshot.Completed ? Selected.Combo.Actions.Count : StepIndex) / Selected.Combo.Actions.Count;
    public string ExpectedOutcome => Session.Snapshot.Completed ? "Résultat final observé · Quitter le combo pour reprendre la recherche."
        : Session.Snapshot.Cue is not { } cue ? "Cette étape nécessite une validation manuelle."
        : "À observer : " + string.Join(" + ", cue.Cards.Select(r => $"{r.Count} × {_catalog?.Cards.FirstOrDefault(c => c.Id == r.CardId)?.Name ?? r.CardId}"))
            + (cue.Area == ObservationArea.Hand ? " ajouté(s) à la main" : " apparu(s) sur ton terrain");
    public string Confidence => IsFollowing ? "Recherche de nouveaux combos suspendue" : Selected is null ? "" : $"Score visuel {Selected.Confidence:P0} · tri par popularité";
    public string Prerequisites => Selected is null ? "" : "Cartes requises : " + (Selected.Combo.RequiredCards.Count > 0
        ? string.Join(" · ", Selected.Combo.RequiredCards) : "consulter les étapes") + "\nTerrain et ressources à vérifier.";
    public string Notes => GuideTextFormatter.PlainText(Selected?.Combo.Explanation ?? "");
    public string Attribution => _catalog?.DeckId == "demo" ? "Démo locale · données fictives" : Selected is null ? "Master Duel Meta" : "Master Duel Meta · " + Selected.Combo.Author;
    public string FollowLabel => IsFollowing ? "Quitter le combo" : "Suivre ce combo";
    public string GuideMode => IsFollowing ? "Combo en cours · les suggestions sont arrêtées" : "Suggestion selon la main observée";

    public void SetCatalog(ComboCatalog catalog) { _catalog = catalog; Reset(); }
    public void Reset()
    {
        Session.Exit(); Status = "Suspendu"; GuideStatus = ""; Timing = ""; _selected = null;
        Hand = []; Recommendations = []; DiagnosticImage = null; Refresh();
    }
    public void Suspend()
    {
        Session.RestartObservation();
        if (!IsFollowing) _selected = null;
        Status = "Suspendu"; Timing = ""; Hand = []; Recommendations = []; DiagnosticImage = null;
        GuideStatus = IsFollowing ? "Combo conservé · relance l’observation pour reprendre" : "";
        Refresh();
    }
    public void ToggleFollow()
    {
        if (IsFollowing) { ExitCombo(); return; }
        if (Selected is null) return;
        Session.Follow(Selected.Combo);
        Hand = []; Recommendations = []; DiagnosticImage = null;
        Status = "Suivi du combo · recherche suspendue";
        GuideStatus = "Observation de l’étape en préparation";
        Refresh();
    }
    public void ExitCombo()
    {
        Session.Exit(); _selected = null; Hand = []; Recommendations = []; DiagnosticImage = null;
        Status = "Observation · nouvelle stabilisation de la main"; GuideStatus = ""; Refresh();
    }
    public void MoveStep(int delta)
    {
        if (Selected is null) return;
        if (!IsFollowing) ToggleFollow();
        Session.Move(delta);
        GuideStatus = "Étape choisie manuellement · nouvelle observation";
        Refresh();
    }

    public Task PresentAsync(ObservationResult result, CancellationToken cancellationToken) => dispatcher.InvokeAsync(() =>
    {
        if (result.SessionRevision is { } revision && revision != Session.Snapshot.Revision) return;
        Status = result.Status;
        GuideStatus = result.GuideStatus;
        var observedCards = result.State.Hand.Select(d => new HandItem(
            _catalog?.Cards.FirstOrDefault(c => c.Id == d.CardId)?.Name ?? "Unknown", $"{d.Confidence:P0}", d.Zone)).ToArray();
        Hand = IsFollowing ? [] : observedCards;
        Recommendations = IsFollowing ? [] : result.Recommendations;
        if (!IsFollowing)
            _selected = Recommendations.FirstOrDefault(r => r.Combo.Id == Selected?.Combo.Id) ?? Recommendations.FirstOrDefault();
        DiagnosticImage = null;
        if (result.DiagnosticPng is { } bytes)
        {
            using var stream = new MemoryStream(bytes);
            var image = new BitmapImage();
            image.BeginInit(); image.CacheOption = BitmapCacheOption.OnLoad; image.StreamSource = stream; image.EndInit(); image.Freeze();
            var drawing = new DrawingVisual();
            using (var context = drawing.RenderOpen())
            {
                context.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                foreach (var item in observedCards)
                {
                    var box = new Rect(item.Zone.X * image.PixelWidth, item.Zone.Y * image.PixelHeight,
                        item.Zone.Width * image.PixelWidth, item.Zone.Height * image.PixelHeight);
                    context.DrawRectangle(null, new Pen(Brushes.Aquamarine, 2), box);
                    var label = new FormattedText(item.Name + " " + item.Confidence, CultureInfo.CurrentUICulture,
                        FlowDirection.LeftToRight, new Typeface("Segoe UI"), 12, Brushes.White, 1);
                    var origin = new Point(box.Left, Math.Max(0, box.Top - label.Height));
                    context.DrawRectangle(Brushes.Black, null, new Rect(origin.X, origin.Y, label.Width, label.Height));
                    context.DrawText(label, origin);
                }
            }
            var preview = new RenderTargetBitmap(image.PixelWidth, image.PixelHeight, 96, 96, PixelFormats.Pbgra32);
            preview.Render(drawing); preview.Freeze();
            DiagnosticImage = preview;
        }
        Timing = $"{result.ElapsedMilliseconds:F0} ms · capture {result.CaptureMilliseconds:F0} / vision {result.RecognitionMilliseconds:F0} / décision {result.DecisionMilliseconds:F0} ms\n"
            + (IsFollowing ? "Suivi de l’étape uniquement · recherche de starters arrêtée" : $"{Hand.Count} carte(s) identifiée(s) · {Recommendations.Count} combo(s) compatible(s)");
        Refresh();
    }, DispatcherPriority.Background, cancellationToken).Task;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
