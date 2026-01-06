using Microsoft.Win32;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Infrastructure;
using Forms = System.Windows.Forms;
using System.Collections.Generic;
using System.Linq;


namespace YuGiOhOverlay.UI;

public partial class OverlayWindow : Window
{
    private AppData _data = new(Version: 1, Decks: Array.Empty<DeckDefinition>());
    private IDataStore _dataStore = null!;

    private readonly ISettingsStore _settingsStore = new JsonSettingsStorePortable();
    private bool _isClickThroughEnabled = true;

    private DeckDefinition? _currentDeck;


    // Global hotkey: Ctrl + F1
    private const int HotkeyId = 0xBEEF;
    private const uint ModControl = 0x0002;
    private const uint VkF1 = 0x70;
    private HwndSource? _hwndSource;

    public OverlayWindow()
    {
        InitializeComponent();

        Loaded += OverlayWindow_Loaded;
        Closed += OverlayWindow_Closed;
        SourceInitialized += OverlayWindow_SourceInitialized;
    }

    private async void OverlayWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Aucun popup au démarrage.
        // On charge soit le chemin mémorisé, soit data.json à côté de l'exe (créé si absent).
        var dataPath = await ResolveDataPathWithoutPromptAsync(CancellationToken.None);

        await LoadAndBindAsync(dataPath, CancellationToken.None);

        ApplyClickThrough(_isClickThroughEnabled);
        OpenDataButton.IsEnabled = !_isClickThroughEnabled;
        BringToFront();
    }

    // ------- Menu / bouton -------

    private void OpenDataButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
            return;

        // Ouvre le menu au clic (UX simple)
        OpenDataButton.ContextMenu.PlacementTarget = OpenDataButton;
        OpenDataButton.ContextMenu.IsOpen = true;
    }

    private async void MenuOpenFile_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
            return;

        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Choisir un data.json",
            Filter = "JSON (*.json)|*.json",
            CheckFileExists = true
        };

        if (dialog.ShowDialog(this) != true)
            return;

        await PersistDataPathAsync(dialog.FileName, CancellationToken.None);
        await LoadAndBindAsync(dialog.FileName, CancellationToken.None);
    }

    private async void MenuChooseFolder_OnClick(object sender, RoutedEventArgs e)
    {
        if (_isClickThroughEnabled)
            return;

        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choisis un dossier : data.json sera créé dedans si absent.",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };

        var result = dialog.ShowDialog();
        if (result != Forms.DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            return;

        var dataPath = Path.Combine(dialog.SelectedPath, "data.json");

        EnsureDataFileExists(dataPath);

        await PersistDataPathAsync(dataPath, CancellationToken.None);
        await LoadAndBindAsync(dataPath, CancellationToken.None);
    }

    // ------- Data load / bind -------

    private async Task LoadAndBindAsync(string dataPath, CancellationToken ct)
    {
        _dataStore = new JsonDataStore(dataPath);
        _data = await _dataStore.LoadAsync(ct);

        BindDecks(_data);
    }

    private void BindDecks(AppData data)
    {
        var decks = data.Decks ?? Array.Empty<DeckDefinition>();

        DeckCombo.ItemsSource = decks;
        DeckCombo.DisplayMemberPath = nameof(DeckDefinition.Name);

        StepsText.Text = string.Empty;
        CardsList.ItemsSource = null;

        if (decks.Count > 0)
            DeckCombo.SelectedIndex = 0;
    }
    private void ShowCardSteps(CardPlan? card)
    {
        if (card is null)
        {
            StepsText.Text = string.Empty;
            return;
        }

        var steps = card.Steps ?? Array.Empty<string>();
        StepsText.Text = string.Join(Environment.NewLine + Environment.NewLine, steps);
    }

    private void DeckCombo_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _currentDeck = DeckCombo.SelectedItem as DeckDefinition;
        RefreshCardsList();
    }

    private void CardsList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var item = CardsList.SelectedItem as CardListItem;
        ShowCardSteps(item?.Source);
    }


    private void StartersOnlyCheckBox_OnChanged(object sender, RoutedEventArgs e)
    {
        RefreshCardsList();
    }

    private void RefreshCardsList()
    {
        var deck = _currentDeck;
        if (deck is null)
        {
            CardsList.ItemsSource = null;
            ShowCardSteps(null);
            return;
        }

        var cards = (deck.Cards ?? Array.Empty<CardPlan>()).ToList();


        if (StartersOnlyCheckBox.IsChecked == true)
        {
            cards = cards
                .Where(IsStarterCard)
                .OrderByDescending(c => c.Priority) // meilleur starter en haut
                .ThenBy(c => c.Name)
                .ToList();
        }
        else
        {
            // tri global optionnel (tu peux enlever si tu veux l’ordre “manuel”)
            cards = cards
                .OrderByDescending(c => c.Priority)
                .ThenBy(c => c.Name)
                .ToList();
        }

        var items = cards.Select(c => new CardListItem(
                                            CardId: c.CardId,
                                            Name: c.Name,
                                            Priority: c.Priority,
                                            Tags: (c.Tags ?? Array.Empty<string>()).ToList(),
                                            Source: c))
            .ToList();


        var previouslySelected = CardsList.SelectedItem as CardListItem;
        var previousId = previouslySelected?.CardId;

        CardsList.ItemsSource = items;

        CardListItem? newSelected = null;

        if (!string.IsNullOrWhiteSpace(previousId))
            newSelected = items.FirstOrDefault(i => i.CardId == previousId);

        newSelected ??= items.FirstOrDefault();

        CardsList.SelectedItem = null;
        CardsList.SelectedItem = newSelected;

        ShowCardSteps(newSelected?.Source);
    }


    private static bool IsStarterCard(CardPlan card)
    {
        var tags = card.Tags ?? Array.Empty<string>();

        return tags.Any(t =>
            string.Equals(t, "starter", StringComparison.OrdinalIgnoreCase));
    }


    // ------- Path resolution rules -------

    private async Task<string> ResolveDataPathWithoutPromptAsync(CancellationToken ct)
    {
        var settings = await _settingsStore.LoadAsync(ct);
        var candidate = settings?.DataFilePath;

        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            return candidate;

        // Fallback: data.json next to exe (portable). Create if missing.
        var defaultPath = Path.Combine(AppContext.BaseDirectory, "data.json");
        EnsureDataFileExists(defaultPath);

        await PersistDataPathAsync(defaultPath, ct);
        return defaultPath;
    }

    private async Task PersistDataPathAsync(string path, CancellationToken ct)
    {
        await _settingsStore.SaveAsync(new AppSettings(path), ct);
    }

    private static void EnsureDataFileExists(string dataFilePath)
    {
        if (File.Exists(dataFilePath))
            return;

        var emptyData = new AppData(
            Version: 1,
            Decks: new List<DeckDefinition>());

        var json = JsonSerializer.Serialize(emptyData, new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(dataFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(dataFilePath, json);
    }

    // ---------- Click-through + hotkey ----------

    private void OverlayWindow_SourceInitialized(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource.AddHook(WndProc);

        RegisterHotKey(hwnd, HotkeyId, ModControl, VkF1);
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

    private void ToggleClickThrough()
    {
        _isClickThroughEnabled = !_isClickThroughEnabled;
        ApplyClickThrough(_isClickThroughEnabled);

        // bouton actif seulement en mode interactif
        OpenDataButton.IsEnabled = !_isClickThroughEnabled;

        if (!_isClickThroughEnabled)
            BringToFront();
    }

    private void ApplyClickThrough(bool enable)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var exStyle = GetWindowLong(hwnd, GwlExstyle);

        exStyle |= WsExLayered;

        if (enable)
            exStyle |= WsExTransparent;
        else
            exStyle &= ~WsExTransparent;

        SetWindowLong(hwnd, GwlExstyle, exStyle);
    }

    private void BringToFront()
    {
        Topmost = true;
        Activate();
        Focus();
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

    // ---------- Portable settings store (next to exe) ----------

    private sealed class JsonSettingsStorePortable : ISettingsStore
    {
        private readonly string _filePath = Path.Combine(AppContext.BaseDirectory, "settings.json");
        private readonly JsonSerializerOptions _options = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };

        public async Task<AppSettings?> LoadAsync(CancellationToken ct)
        {
            if (!File.Exists(_filePath))
                return null;

            await using var stream = File.OpenRead(_filePath);
            return await JsonSerializer.DeserializeAsync<AppSettings>(stream, _options, ct);
        }

        public async Task SaveAsync(AppSettings settings, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(settings);
            await using var stream = File.Create(_filePath);
            await JsonSerializer.SerializeAsync(stream, settings, _options, ct);
        }
    }
}
