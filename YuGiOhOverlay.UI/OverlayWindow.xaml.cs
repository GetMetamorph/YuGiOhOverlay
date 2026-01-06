using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Infrastructure;
using Microsoft.Win32;

namespace YuGiOhOverlay.UI;

public partial class OverlayWindow : Window
{
    private IDataStore _dataStore = null!;
    private readonly ISettingsStore _settingsStore = new JsonSettingsStore();
    private AppData _data = new(Version: 1, Decks: Array.Empty<DeckDefinition>());

    private bool _isClickThroughEnabled = true;

    // Global hotkey: Ctrl + F1 (works even when overlay is click-through and not focused)
    private const int HotkeyId = 0xBEEF;
    private const uint ModControl = 0x0002;
    private const uint VkF1 = 0x70;
    private HwndSource? _hwndSource;

    public OverlayWindow()
    {
        InitializeComponent();

        var dataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "YuGiOhOverlay",
            "data.json");

        _dataStore = new JsonDataStore(dataPath);

        Loaded += OverlayWindow_Loaded;
        Closed += OverlayWindow_Closed;
        SourceInitialized += OverlayWindow_SourceInitialized;
    }

    private async void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var settings = await _settingsStore.LoadAsync(CancellationToken.None);

        var dataFilePath = settings?.DataFilePath
                           ?? Path.Combine(AppContext.BaseDirectory, "data.json");

        if (string.IsNullOrWhiteSpace(dataFilePath) || !File.Exists(dataFilePath))
        {
            dataFilePath = AskUserForDataFilePath();
            if (dataFilePath is null)
            {
                Close(); // utilisateur a annulé → on quitte proprement
                return;
            }

            await _settingsStore.SaveAsync(
                new AppSettings(dataFilePath),
                CancellationToken.None);
        }

        _dataStore = new JsonDataStore(dataFilePath);

        _data = await _dataStore.LoadAsync(CancellationToken.None);

        DeckCombo.ItemsSource = _data.Decks;
        DeckCombo.DisplayMemberPath = nameof(DeckDefinition.Name);

        if (_data.Decks.Count > 0)
            DeckCombo.SelectedIndex = 0;

        ApplyClickThrough(_isClickThroughEnabled);
        BringToFront();
    }

    private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource.AddHook(WndProc);

        // Register global hotkey (Ctrl+F1)
        if (!RegisterHotKey(hwnd, HotkeyId, ModControl, VkF1))
        {
            // Not fatal; overlay will still work if focused.
            // But in click-through mode you won't be able to toggle without the hotkey.
        }
    }

    private void OverlayWindow_Closed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyId);

        if (_hwndSource is not null)
        {
            _hwndSource.RemoveHook(WndProc);
            _hwndSource = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleClickThrough();
            handled = true;
        }

        return IntPtr.Zero;
    }

    private string? AskUserForDataFilePath()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Sélectionne ton fichier de deck (data.json)",
            Filter = "JSON (*.json)|*.json",
            CheckFileExists = false
        };

        if (dialog.ShowDialog() != true)
            return null;

        // Si le fichier n'existe pas encore, on le crée vide
        if (!File.Exists(dialog.FileName))
        {
            var emptyData = new AppData(
                Version: 1,
                Decks: new List<DeckDefinition>());

            var json = System.Text.Json.JsonSerializer.Serialize(
                emptyData,
                new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(dialog.FileName, json);
        }

        return dialog.FileName;
    }

    private void ToggleClickThrough()
    {
        _isClickThroughEnabled = !_isClickThroughEnabled;
        ApplyClickThrough(_isClickThroughEnabled);

        // When becoming interactive, bring to front + focus so clicks go to overlay.
        if (!_isClickThroughEnabled)
            BringToFront();
    }

    private void DeckCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeckCombo.SelectedItem is not DeckDefinition deck)
            return;

        CardsList.ItemsSource = deck.Cards;
        CardsList.DisplayMemberPath = nameof(CardPlan.Name);

        StepsText.Text = string.Empty;
        if (deck.Cards.Count > 0)
            CardsList.SelectedIndex = 0;
    }

    private void CardsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CardsList.SelectedItem is not CardPlan card)
        {
            StepsText.Text = string.Empty;
            return;
        }

        StepsText.Text = string.Join(Environment.NewLine + Environment.NewLine, card.Steps);
    }

    private void ApplyClickThrough(bool enable)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var exStyle = GetWindowLong(hwnd, GwlExstyle);

        // Always keep layered for transparency
        exStyle |= WsExLayered;

        if (enable)
            exStyle |= WsExTransparent;   // click-through
        else
            exStyle &= ~WsExTransparent;  // interactive

        SetWindowLong(hwnd, GwlExstyle, exStyle);
    }

    private void BringToFront()
    {
        // A common WPF trick to reliably bring topmost window to front
        Topmost = true;
        Activate();
        Focus();
        // Sometimes toggling Topmost helps if another topmost window is around
        Topmost = false;
        Topmost = true;
    }

    private const int GwlExstyle = -20;
    private const int WsExTransparent = 0x20;
    private const int WsExLayered = 0x80000;

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll")]
    private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}
