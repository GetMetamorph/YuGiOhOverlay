using System.Diagnostics;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using YuGiOhOverlay.Infrastructure;

namespace YuGiOhOverlay.UI;

public partial class OverlayWindow : Window
{
    private readonly AssistantViewModel _viewModel;
    private readonly Action _settings;
    private readonly Action _stop;
    private HwndSource? _source;
    private bool _passive = true;
    private readonly List<int> _registered = [];
    private bool _canToggle;
    public OverlayWindow(AssistantViewModel viewModel, Action settings, Action stop)
    {
        InitializeComponent();
        _viewModel = viewModel; _settings = settings; _stop = stop;
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            _source = HwndSource.FromHwnd(handle);
            _source.AddHook(WindowMessage);
            foreach (var (id, key) in new[] { (1, 0x70u), (2, 0x71u), (3, 0x27u), (4, 0x25u) })
                if (OverlayNative.RegisterHotKey(handle, id, 0x4002, key)) _registered.Add(id);
            _canToggle = _registered.Contains(1);
            if (!_canToggle) InteractionHint.Text = "Ctrl+F1 indisponible · overlay interactif";
            OverlayNative.ExcludeFromCapture(handle);
            SetPassive(_canToggle);
        };
        Closed += (_, _) =>
        {
            var handle = new WindowInteropHelper(this).Handle;
            foreach (var id in _registered) OverlayNative.UnregisterHotKey(handle, id);
            _source?.RemoveHook(WindowMessage);
        };
    }
    public void SetPassive(bool passive)
    {
        _passive = passive && _canToggle;
        var handle = new WindowInteropHelper(this).Handle;
        if (handle == 0) return;
        OverlayNative.SetPassive(handle, _passive);
        InteractionHint.Text = !_canToggle ? "Raccourci indisponible · overlay interactif"
            : _passive ? "Passif · Ctrl+F1 pour interagir" : "Interactif · déplacer par l’en-tête · Ctrl+F1 pour verrouiller";
    }
    private nint WindowMessage(nint handle, int message, nint parameter, nint data, ref bool handled)
    {
        if (message != 0x0312) return 0;
        switch ((int)parameter)
        {
            case 1: SetPassive(!_passive); break;
            case 2: _stop(); break;
            case 3: _viewModel.MoveStep(1); break;
            case 4: _viewModel.MoveStep(-1); break;
            default: return 0;
        }
        handled = true;
        return 0;
    }
    private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (!_passive && e.LeftButton == MouseButtonState.Pressed && e.OriginalSource is System.Windows.Controls.TextBlock or System.Windows.Controls.Grid)
            DragMove();
    }
    private void Hide_Click(object sender, RoutedEventArgs e) { _stop(); Hide(); }
    private void Settings_Click(object sender, RoutedEventArgs e) => _settings();
    private void Follow_Click(object sender, RoutedEventArgs e) => _viewModel.ToggleFollow();
    private void Previous_Click(object sender, RoutedEventArgs e) => _viewModel.MoveStep(-1);
    private void Next_Click(object sender, RoutedEventArgs e) => _viewModel.MoveStep(1);
    private void Source_Click(object sender, RoutedEventArgs e)
    {
        var url = _viewModel.Selected?.Combo.SourceUrl;
        if (!CatalogValidator.IsSourceUrl(url)) return;
        try { Process.Start(new ProcessStartInfo(url!) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception) { InteractionHint.Text = "Impossible d’ouvrir le navigateur."; }
    }
}
