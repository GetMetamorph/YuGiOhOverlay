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
    private readonly Canvas _canvas = new();
    private readonly Rectangle _rectangle = new() { Stroke = Brushes.Aquamarine, StrokeThickness = 2, Fill = new SolidColorBrush(Color.FromArgb(45, 114, 227, 194)) };
    private Point? _start;
    public NormalizedRect? Selection { get; private set; }
    public CalibrationWindow(ClientGeometry geometry, bool alliedField = false)
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
            Text = alliedField ? "Trace uniquement TON terrain (hors main et terrain adverse) · Échap pour annuler"
                : "Trace la zone de ta main · relâche pour valider · Échap pour annuler",
            Foreground = Brushes.White,
            Background = new SolidColorBrush(Color.FromRgb(15, 30, 45)),
            Padding = new Thickness(14),
            FontSize = 16,
            IsHitTestVisible = false
        };
        _canvas.Children.Add(hint);
        Canvas.SetLeft(hint, 18); Canvas.SetTop(hint, 18);
        _canvas.Children.Add(_rectangle);
        SourceInitialized += (_, _) => OverlayNative.Position(new WindowInteropHelper(this).Handle,
            geometry.X, geometry.Y, geometry.Width, geometry.Height);
        KeyDown += (_, e) => { if (e.Key == Key.Escape) DialogResult = false; };
        MouseLeftButtonDown += (_, e) => { _start = e.GetPosition(_canvas); CaptureMouse(); };
        MouseMove += (_, e) => { if (_start is { } start) Draw(start, e.GetPosition(_canvas)); };
        MouseLeftButtonUp += (_, e) =>
        {
            if (_start is not { } start) return;
            Draw(start, e.GetPosition(_canvas)); ReleaseMouseCapture();
            if (_rectangle.Width < 20 || _rectangle.Height < 20) { _start = null; return; }
            var selection = new NormalizedRect(Canvas.GetLeft(_rectangle) / _canvas.ActualWidth,
                Canvas.GetTop(_rectangle) / _canvas.ActualHeight,
                _rectangle.Width / _canvas.ActualWidth, _rectangle.Height / _canvas.ActualHeight);
            if (!selection.IsValid) return;
            Selection = selection;
            DialogResult = true;
        };
    }
    private void Draw(Point start, Point end)
    {
        end.X = Math.Clamp(end.X, 0, _canvas.ActualWidth); end.Y = Math.Clamp(end.Y, 0, _canvas.ActualHeight);
        Canvas.SetLeft(_rectangle, Math.Min(start.X, end.X)); Canvas.SetTop(_rectangle, Math.Min(start.Y, end.Y));
        _rectangle.Width = Math.Abs(end.X - start.X); _rectangle.Height = Math.Abs(end.Y - start.Y);
    }
}
