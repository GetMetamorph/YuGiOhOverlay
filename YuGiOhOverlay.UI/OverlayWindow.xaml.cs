using Microsoft.Win32;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Infrastructure;
using Forms = System.Windows.Forms;
using WpfPoint = System.Windows.Point;


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

    private HwndSource? _source;

    private const int WM_NCHITTEST = 0x0084;

    private const int HTCLIENT = 1;
    private const int HTCAPTION = 2;
    private const int HTLEFT = 10;
    private const int HTRIGHT = 11;
    private const int HTTOP = 12;
    private const int HTTOPLEFT = 13;
    private const int HTTOPRIGHT = 14;
    private const int HTBOTTOM = 15;
    private const int HTBOTTOMLEFT = 16;
    private const int HTBOTTOMRIGHT = 17;

    // Épaisseur de bord “resize” (en DIP, on convertit avec le DPI)
    private const double ResizeBorderDip = 8.0;


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
        ApplyInteractiveUiState();

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

    private void ApplyInteractiveUiState()
    {
        var isInteractive = !_isClickThroughEnabled;

        // On garde le style (pas de "disabled white"), mais on empêche les clics
        OpenDataButton.IsHitTestVisible = isInteractive;
        OpenDataButton.Opacity = isInteractive ? 1.0 : 0.65;

        // Optionnel : pareil pour le reste si tu veux
        DeckCombo.IsHitTestVisible = isInteractive;
        DeckCombo.Opacity = isInteractive ? 1.0 : 0.65;

        StartersOnlyCheckBox.IsHitTestVisible = isInteractive;
        StartersOnlyCheckBox.Opacity = isInteractive ? 1.0 : 0.65;

        CardsList.IsHitTestVisible = isInteractive;
        CardsList.Opacity = isInteractive ? 1.0 : 0.85;

        StepsText.IsHitTestVisible = isInteractive;
        StepsText.Opacity = isInteractive ? 1.0 : 0.85;
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

    // Handlers

    private void ResizeThumb_OnDragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
    {
        if (_isClickThroughEnabled)
            return;

        const double minWidth = 380;
        const double minHeight = 260;

        var newWidth = Width + e.HorizontalChange;
        var newHeight = Height + e.VerticalChange;

        Width = Math.Max(minWidth, newWidth);
        Height = Math.Max(minHeight, newHeight);
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

        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(WndProc);

        RegisterHotKey(hwnd, HotkeyId, ModControl, VkF1);
    }

    private void OverlayWindow_Closed(object? sender, EventArgs e)
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        UnregisterHotKey(hwnd, HotkeyId);

        if (_source is not null)
        {
            _source.RemoveHook(WndProc);
            _source = null;
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        const int WM_HOTKEY = 0x0312;

        if (msg == WM_HOTKEY && wParam.ToInt32() == HotkeyId)
        {
            ToggleClickThrough();
            handled = true;
            return IntPtr.Zero;
        }

        if (msg == WM_NCHITTEST)
        {
            // Si click-through ON, on ne capte rien (les clics passent déjà via WS_EX_TRANSPARENT),
            // mais on laisse Windows tranquille.
            if (_isClickThroughEnabled)
                return IntPtr.Zero;

            handled = true;
            return HitTestNca(hwnd, lParam);
        }

        return IntPtr.Zero;
    }

    private IntPtr HitTestNca(IntPtr hwnd, IntPtr lParam)
    {
        // lParam contient la position écran du curseur (en pixels)
        var mouseScreen = GetMouseScreenPoint(lParam);

        // Convert en coordonnées client (pixels)
        if (!ScreenToClient(hwnd, ref mouseScreen))
            return new IntPtr(HTCLIENT);

        // Convert en coords WPF (DIP)
        var mouseDip = ClientPixelsToWpfDip(hwnd, mouseScreen);

        // 1) Ne jamais “capturer” le resize/drag si on survole un contrôle interactif
        // (ComboBox/ListBox/Button/TextBox etc.)
        if (IsOverInteractiveElement(mouseDip))
            return new IntPtr(HTCLIENT);

        // 2) Resize sur bords + coins
        var borderPx = GetResizeBorderThicknessInPixels(hwnd, ResizeBorderDip);

        // on a mouseScreen en *client pixels*
        var wDip = ActualWidth > 0 ? ActualWidth : Width;
        var hDip = ActualHeight > 0 ? ActualHeight : Height;

        var widthPx = (int)Math.Round(wDip * GetDpiScaleX(hwnd));
        var heightPx = (int)Math.Round(hDip * GetDpiScaleY(hwnd));


        var x = mouseScreen.X;
        var y = mouseScreen.Y;

        var left = x <= borderPx;
        var right = x >= widthPx - borderPx;
        var top = y <= borderPx;
        var bottom = y >= heightPx - borderPx;

        if (top && left) return new IntPtr(HTTOPLEFT);
        if (top && right) return new IntPtr(HTTOPRIGHT);
        if (bottom && left) return new IntPtr(HTBOTTOMLEFT);
        if (bottom && right) return new IntPtr(HTBOTTOMRIGHT);

        if (left) return new IntPtr(HTLEFT);
        if (right) return new IntPtr(HTRIGHT);
        if (top) return new IntPtr(HTTOP);
        if (bottom) return new IntPtr(HTBOTTOM);

        // 3) Drag depuis le header (zone dédiée)
        if (IsOverHeaderDragArea(mouseDip))
            return new IntPtr(HTCAPTION);

        return new IntPtr(HTCLIENT);
    }

    // HELPERS for hit testing

    private static POINT GetMouseScreenPoint(IntPtr lParam)
    {
        // LOWORD / HIWORD signés
        var x = (short)((long)lParam & 0xFFFF);
        var y = (short)(((long)lParam >> 16) & 0xFFFF);
        return new POINT { X = x, Y = y };
    }

    private WpfPoint ClientPixelsToWpfDip(IntPtr hwnd, POINT clientPx)
    {
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is null)
            return new WpfPoint(clientPx.X, clientPx.Y);

        var transform = source.CompositionTarget.TransformFromDevice; // pixels -> DIP
        return transform.Transform(new WpfPoint(clientPx.X, clientPx.Y));
    }


    private static int GetResizeBorderThicknessInPixels(IntPtr hwnd, double borderDip)
    {
        var scaleX = GetDpiScaleX(hwnd); // approx ok
        return (int)Math.Ceiling(borderDip * scaleX);
    }

    private static double GetDpiScaleX(IntPtr hwnd)
    {
        // WPF provides correct per-monitor scale via the current source
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is null) return 1.0;
        return source.CompositionTarget.TransformToDevice.M11;
    }

    private static double GetDpiScaleY(IntPtr hwnd)
    {
        var source = HwndSource.FromHwnd(hwnd);
        if (source?.CompositionTarget is null) return 1.0;
        return source.CompositionTarget.TransformToDevice.M22;
    }

    //.

    private bool IsOverHeaderDragArea(WpfPoint mouseDip)
    {
        // On fait un hit-test et on vérifie si l’élément est dans HeaderDragArea
        var element = InputHitTest(mouseDip) as DependencyObject;
        if (element is null)
            return false;

        var header = HeaderDragArea as DependencyObject;
        if (header is null)
            return false;

        while (element is not null)
        {
            if (ReferenceEquals(element, header))
                return true;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }

    private bool IsOverInteractiveElement(WpfPoint mouseDip)
    {
        var element = InputHitTest(mouseDip) as DependencyObject;
        if (element is null)
            return false;

        // On remonte l’arbre visuel : si on tombe sur un contrôle interactif, on ne traite pas en NC
        while (element is not null)
        {
            if (element is System.Windows.Controls.Primitives.ButtonBase) return true;
            if (element is System.Windows.Controls.Primitives.ToggleButton) return true;
            if (element is System.Windows.Controls.Primitives.Selector) return true; // ListBox, ComboBox, etc.
            if (element is System.Windows.Controls.Primitives.TextBoxBase) return true;
            // TextBox / RichTextBox
            if (element is System.Windows.Controls.ComboBox) return true;
            if (element is System.Windows.Controls.Primitives.ScrollBar) return true;

            element = VisualTreeHelper.GetParent(element);
        }

        return false;
    }



    private void ToggleClickThrough()
    {
        _isClickThroughEnabled = !_isClickThroughEnabled;
        ApplyClickThrough(_isClickThroughEnabled);

        // bouton actif seulement en mode interactif
        ApplyInteractiveUiState();
        
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
    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

}