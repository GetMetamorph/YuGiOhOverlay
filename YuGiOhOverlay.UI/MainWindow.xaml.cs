using System.ComponentModel;
using System.IO;
using System.Windows;
using YuGiOhOverlay.Domain;
using YuGiOhOverlay.Application;
using YuGiOhOverlay.Infrastructure;
using YuGiOhOverlay.Windows;

namespace YuGiOhOverlay.UI;

public partial class MainWindow : Window
{
    private readonly CompositionRoot _services;
    private readonly AssistantViewModel _viewModel;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly OverlayWindow _overlay;
    private AssistantSettings _settings = AssistantSettings.Default;
    private ComboCatalog? _catalog;
    private CancellationTokenSource? _observationCancellation;
    private Task? _observation;
    private bool _busy;
    private bool _closing;
    private bool _canClose;

    public MainWindow(CompositionRoot services)
    {
        InitializeComponent();
        _services = services;
        _viewModel = new(Dispatcher);
        DataContext = _viewModel;
        _overlay = new(_viewModel, ShowSettings, () => _observationCancellation?.Cancel());
        Loaded += async (_, _) => await RunUiAsync(async () =>
        {
            _settings = await _services.LoadSettingsAsync(_lifetime.Token);
            SourceUrl.Text = _settings.SourceUrl;
            DiagnosticsCheck.IsChecked = _settings.Recognition.Diagnostics;
            CalibrationStatus.Text = _settings.Capture is null ? "Calibration requise" : "Profil mémorisé · vérifier la zone dans le jeu";
            SetCatalog(await _services.Repository.LoadAsync(_lifetime.Token));
            RefreshTargets();
        });
        Closing += OnClosing;
    }

    private void SetCatalog(ComboCatalog? catalog)
    {
        _catalog = catalog;
        if (catalog is null) return;
        _viewModel.SetCatalog(catalog);
        CatalogStatus.Text = $"{catalog.DeckName} · {catalog.Rules.Count} combos · {catalog.Cards.Count} illustrations · synchronisé le {catalog.ImportedAt.ToLocalTime():g}";
        ImportWarnings.Text = catalog.ImportWarnings.Count == 0 ? "Aucun prérequis non pris en charge."
            : string.Join(Environment.NewLine, catalog.ImportWarnings);
    }

    private async void Sync_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        await StopAsync();
        var url = SourceUrl.Text.Trim();
        Message.Text = "Chargement des combos Master Duel Meta…";
        var catalog = await _services.MasterDuelMeta.ImportAsync(url, _lifetime.Token);
        if (catalog.Rules.Count == 0) throw new InvalidDataException("Aucun combo compatible avec l’import. Le catalogue précédent est conservé.");
        await _services.MasterDuelMeta.CacheImagesAsync(catalog, _services.ImagesFolder,
            new Progress<string>(text => Message.Text = text), _lifetime.Token);
        await _services.Repository.SaveAsync(catalog, _lifetime.Token);
        _settings = _settings with { SourceUrl = url };
        await _services.SaveSettingsAsync(_settings, _lifetime.Token);
        SetCatalog(catalog);
        Message.Text = "Deck prêt. Les illustrations sont disponibles hors ligne.";
    });

    private async void Import_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Catalogue v2 (*.json)|*.json", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true) return;
        await StopAsync();
        var catalog = await new JsonComboRepository(dialog.FileName).LoadAsync(_lifetime.Token)
            ?? throw new InvalidDataException("Catalogue absent.");
        await _services.MasterDuelMeta.CacheImagesAsync(catalog, _services.ImagesFolder,
            new Progress<string>(text => Message.Text = text), _lifetime.Token);
        await _services.Repository.SaveAsync(catalog, _lifetime.Token);
        SetCatalog(catalog);
        Message.Text = "Catalogue importé et validé.";
    });

    private void Refresh_Click(object sender, RoutedEventArgs e) => RefreshTargets();
    private void RefreshTargets()
    {
        var previous = TargetCombo.SelectedItem as TargetWindow;
        var windows = WindowCatalog.List();
        TargetCombo.ItemsSource = windows;
        TargetCombo.SelectedItem = windows.FirstOrDefault(w => w.Handle == previous?.Handle)
            ?? windows.FirstOrDefault(w => w.Title.Contains("Master Duel", StringComparison.OrdinalIgnoreCase));
    }

    private async void Calibrate_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        await StopAsync();
        var target = TargetCombo.SelectedItem as TargetWindow ?? throw new InvalidOperationException("Choisis d’abord la fenêtre du jeu.");
        var geometry = WindowCatalog.Geometry(target);
        _overlay.Hide();
        var calibration = new CalibrationWindow(geometry);
        if (calibration.ShowDialog() != true || calibration.Selection is not { } selection) return;
        if (WindowCatalog.Geometry(target) != geometry) throw new InvalidOperationException("La fenêtre a bougé. Recommencer la calibration.");
        _settings = _settings with { Capture = new(selection, geometry.Width / (double)geometry.Height, geometry.Dpi) };
        await _services.SaveSettingsAsync(_settings, _lifetime.Token);
        CalibrationStatus.Text = $"Zone enregistrée · {selection.Width:P0} × {selection.Height:P0} de la fenêtre";
        Message.Text = "Calibration enregistrée. Laisse toutes les illustrations de la main dans cette zone.";
    });

    private async void Start_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        await StopAsync();
        var target = TargetCombo.SelectedItem as TargetWindow ?? throw new InvalidOperationException("Choisis la fenêtre du jeu.");
        var profile = _settings.Capture ?? throw new InvalidOperationException("Calibre la zone de main avant de démarrer.");
        var catalog = _catalog ?? throw new InvalidOperationException("Synchronise d’abord un deck.");
        if (catalog.Rules.Count == 0) throw new InvalidOperationException("Le catalogue ne contient aucun combo utilisable.");
        _settings = _settings with { Recognition = _settings.Recognition with { Diagnostics = DiagnosticsCheck.IsChecked == true } };
        await _services.SaveSettingsAsync(_settings, _lifetime.Token);
        _viewModel.Reset();
        _overlay.Show();
        _overlay.SetPassive(true);
        _observationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _observation = RunObservationAsync(target, profile, catalog, _observationCancellation.Token);
        Message.Text = "Observation active. Remets le jeu au premier plan.";
        StopButton.IsEnabled = true;
    });

    private async Task RunObservationAsync(TargetWindow target, CaptureProfile profile, ComboCatalog catalog, CancellationToken ct)
    {
        try { await _services.ObserveAsync(target, profile, _settings.Recognition, catalog, _viewModel, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Message.Text = "Observation arrêtée : " + ex.Message; }
        finally { _observationCancellation?.Cancel(); StopButton.IsEnabled = false; _viewModel.Reset(); UpdateButtons(); }
    }

    private async void Demo_Click(object sender, RoutedEventArgs e) => await RunUiAsync(async () =>
    {
        await StopAsync();
        var demo = new DemoSession();
        _viewModel.SetCatalog(demo.Catalog);
        _overlay.Show();
        _overlay.SetPassive(false);
        _observationCancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        _observation = RunDemoAsync(demo, _observationCancellation.Token);
        StopButton.IsEnabled = true;
        Message.Text = "DÉMONSTRATION · données fictives · aucune capture de fenêtre";
    });

    private async Task RunDemoAsync(DemoSession demo, CancellationToken ct)
    {
        try { await demo.RunAsync(_viewModel, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) { Message.Text = "Démo arrêtée : " + ex.Message; }
        finally
        {
            _observationCancellation?.Cancel();
            if (_catalog is not null) _viewModel.SetCatalog(_catalog); else _viewModel.Reset();
            StopButton.IsEnabled = false;
            UpdateButtons();
        }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e) => await StopAsync();
    private async Task StopAsync()
    {
        _observationCancellation?.Cancel();
        if (_observation is not null) await _observation;
        _observationCancellation?.Dispose();
        _observationCancellation = null;
        _observation = null;
        _viewModel.Reset();
        StopButton.IsEnabled = false;
        UpdateButtons();
    }

    private void Legacy_Click(object sender, RoutedEventArgs e) => new LegacyOverlayWindow().Show();
    private void Overlay_Click(object sender, RoutedEventArgs e) => _overlay.Show();
    private void ShowSettings() { Show(); WindowState = WindowState.Normal; Activate(); }

    private async Task RunUiAsync(Func<Task> action)
    {
        if (_busy || _closing) return;
        _busy = true;
        UpdateButtons();
        try { await action(); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex) { Message.Text = "Impossible de continuer : " + ex.Message; }
        finally { _busy = false; UpdateButtons(); }
    }

    private void UpdateButtons()
    {
        ConfigurationPanel.IsEnabled = !_busy && !_closing && _observationCancellation?.IsCancellationRequested != false;
        StartButton.IsEnabled = !_busy && !_closing;
        DemoButton.IsEnabled = !_busy && !_closing;
    }

    private async void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_canClose) return;
        e.Cancel = true;
        if (_closing) return;
        _closing = true;
        _lifetime.Cancel();
        await StopAsync();
        _overlay.Close();
        _lifetime.Dispose();
        _canClose = true;
        Close();
    }
}
