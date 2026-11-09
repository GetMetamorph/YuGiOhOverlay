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

public sealed class AssistantViewModel(Dispatcher dispatcher) : INotifyPropertyChanged, IOverlayPresenter
{
    private ComboCatalog? _catalog;
    private RecommendedAction? _selected;
    private int _step;
    private bool _following;
    public string Status { get; private set; } = "Observation · configuration requise";
    public string DeckName => _catalog?.DeckName ?? "Aucun deck synchronisé";
    public IReadOnlyList<HandItem> Hand { get; private set; } = [];
    public IReadOnlyList<RecommendedAction> Recommendations { get; private set; } = [];
    public BitmapSource? DiagnosticImage { get; private set; }
    public string Timing { get; private set; } = "";
    public RecommendedAction? Selected
    {
        get => _selected;
        set { if (_selected?.Combo.Id == value?.Combo.Id) return; _selected = value; _step = 0; Refresh(); }
    }
    public string ComboTitle => Selected?.Combo.Title ?? "En attente d’une main compatible";
    public string StepLabel => Selected is null ? "GUIDE DE COMBO" : $"ÉTAPE {_step + 1} / {Selected.Combo.Actions.Count}";
    public string StepText => Selected?.Combo.Actions.ElementAtOrDefault(_step) ?? "Laisse la main visible et stable. Les cartes inconnues ne sont pas forcées vers une correspondance.";
    public string Confidence => Selected is null ? "" : $"Score visuel {Selected.Confidence:P0} · tri par popularité";
    public string Prerequisites => Selected is null ? "" : "Cartes requises : " + (Selected.Combo.RequiredCards.Count > 0
        ? string.Join(" · ", Selected.Combo.RequiredCards) : "consulter les étapes") + "\nTerrain et ressources à vérifier.";
    public string Notes => Selected?.Combo.Explanation ?? "";
    public string Attribution => _catalog?.DeckId == "demo" ? "Démo locale · données fictives" : Selected is null ? "Master Duel Meta" : "Master Duel Meta · " + Selected.Combo.Author;
    public string FollowLabel => _following ? "Revenir à la détection" : "Suivre ce combo";
    public string GuideMode => _following ? "Guide conservé · progression manuelle" : "Suggestion selon la main observée";

    public void SetCatalog(ComboCatalog catalog) { _catalog = catalog; Reset(); Refresh(); }
    public void Reset() { Status = "Suspendu"; Timing = ""; _following = false; _selected = null; _step = 0; Hand = []; Recommendations = []; DiagnosticImage = null; Refresh(); }
    public void ToggleFollow() { if (Selected is null) return; _following = !_following; if (!_following) Selected = Recommendations.FirstOrDefault(); Refresh(); }
    public void MoveStep(int delta)
    {
        if (Selected is null) return;
        _following = true;
        _step = Math.Clamp(_step + delta, 0, Selected.Combo.Actions.Count - 1);
        Refresh();
    }

    public Task PresentAsync(ObservationResult result, CancellationToken cancellationToken) => dispatcher.InvokeAsync(() =>
    {
        Status = result.Status;
        Hand = result.State.Hand.Select(d => new HandItem(
            _catalog?.Cards.FirstOrDefault(c => c.Id == d.CardId)?.Name ?? "Unknown", $"{d.Confidence:P0}", d.Zone)).ToArray();
        Recommendations = result.Recommendations;
        if (!_following)
        {
            var next = Recommendations.FirstOrDefault(r => r.Combo.Id == Selected?.Combo.Id) ?? Recommendations.FirstOrDefault();
            if (next?.Combo.Id == _selected?.Combo.Id) _selected = next;
            else Selected = next;
        }
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
                foreach (var item in Hand)
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
        Timing = $"{result.ElapsedMilliseconds:F0} ms · capture {result.CaptureMilliseconds:F0} / vision {result.RecognitionMilliseconds:F0} / décision {result.DecisionMilliseconds:F0} ms\n{Hand.Count} carte(s) identifiée(s) · {Recommendations.Count} combo(s) compatible(s)";
        Refresh();
    }, DispatcherPriority.Background, cancellationToken).Task;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Refresh() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
}
