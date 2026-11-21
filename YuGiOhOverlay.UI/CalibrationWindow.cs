using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Windows;
using Point = System.Windows.Point;

namespace YuGiOhOverlay.UI;

public sealed class CalibrationWindow : Window
{
    private const double MinimumSelectionSize = 20;
    private readonly Canvas _canvas = new();
    private readonly Rectangle _rectangle = new() { Stroke = Brushes.Aquamarine, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(45, 114, 227, 194)) };
    private Point? _start;
    public NormalizedRect? Selection { get; private set; }
    public CalibrationWindow(ClientGeometry geometry, bool alliedField = false,
        NormalizedRect? previous = null, NormalizedRect? excluded = null)
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = new SolidColorBrush(Color.FromArgb(28, 0, 8, 15));
        Topmost = true;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Cursor = Cursors.Cross;
        Content = _canvas;
        var hint = new TextBlock
        {
            Text = alliedField ? "Trace TON terrain · évite la main orange · relâche pour valider · Échap pour annuler"
                : "Trace les illustrations de ta main · évite le terrain orange · relâche pour valider · Échap pour annuler",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(15, 30, 45)),
            Padding = new Thickness(14),
            FontSize = 16,
            TextWrapping = TextWrapping.Wrap,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(hint);
        Canvas.SetLeft(hint, 18); Canvas.SetTop(hint, 18);
        Panel.SetZIndex(hint, 1);
        var reserved = new Rectangle
        {
            Stroke = Brushes.Orange,
            StrokeThickness = 2,
            Fill = new SolidColorBrush(Color.FromArgb(45, 255, 165, 0)),
            IsHitTestVisible = false
        };
        _canvas.Children.Add(reserved);
        _canvas.Children.Add(_rectangle);
        // Position after WPF's initial layout, in physical screen pixels (including DPI).
        Loaded += (_, _) => OverlayNative.Position(new WindowInteropHelper(this).Handle,
            geometry.X, geometry.Y, geometry.Width, geometry.Height);
        _canvas.SizeChanged += (_, _) =>
        {
            hint.MaxWidth = Math.Max(1, _canvas.ActualWidth - 36);
            if (excluded is { } other) ShowZone(reserved, other);
            if (previous is { } saved && _start is null) ShowZone(_rectangle, saved);
        };
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        MouseLeftButtonDown += (_, e) => { _start = Clamp(e.GetPosition(_canvas)); CaptureMouse(); };
        MouseMove += (_, e) => { if (_start is { } start) Draw(start, e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } start) return;
            Draw(start, e.GetPosition(_canvas)); ReleaseMouseCapture();
            if (_rectangle.Width < MinimumSelectionSize || _rectangle.Height < MinimumSelectionSize)
            {
                hint.Text = "Zone trop petite · trace un rectangle contenant les illustrations visibles.";
                _start = null;
                return;
            }
            var selection = new NormalizedRect(Canvas.GetLeft(_rectangle) / _canvas.ActualWidth,
                Canvas.GetTop(_rectangle) / _canvas.ActualHeight,
                _rectangle.Width / _canvas.ActualWidth, _rectangle.Height / _canvas.ActualHeight);
            _start = null;
            if (!selection.IsValid) return;
            if (excluded is { } other && other.Overlap(selection) > 0)
            {
                hint.Text = "Les zones se chevauchent : retrace le rectangle hors de la zone orange. Rien n’a été effacé.";
                _rectangle.Stroke = Brushes.Orange;
                return;
            }
            Selection = selection;
            DialogResult = true;
        };
    }
    private void Draw(Point start, Point end)
    {
        end = Clamp(end);
        _rectangle.Stroke = Brushes.Aquamarine;
        Canvas.SetLeft(_rectangle, Math.Min(start.X, end.X)); Canvas.SetTop(_rectangle, Math.Min(start.Y, end.Y));
        _rectangle.Width = Math.Abs(end.X - start.X); _rectangle.Height = Math.Abs(end.Y - start.Y);
    }
    private Point Clamp(Point point) => new(Math.Clamp(point.X, 0, _canvas.ActualWidth), Math.Clamp(point.Y, 0, _canvas.ActualHeight));
    private void ShowZone(Rectangle rectangle, NormalizedRect zone)
    {
        Canvas.SetLeft(rectangle, zone.X * _canvas.ActualWidth);
        Canvas.SetTop(rectangle, zone.Y * _canvas.ActualHeight);
        rectangle.Width = zone.Width * _canvas.ActualWidth;
        rectangle.Height = zone.Height * _canvas.ActualHeight;
    }
}
