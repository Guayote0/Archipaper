using System.Windows;
using System.Windows.Controls;
using Archipaper.Services;
using Archipaper.Models;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Archipaper.Views;

public partial class MainWindow : Window
{
    private readonly RotationService _rotation;
    private readonly Func<bool, Task> _save;
    private readonly ReviewQueueService _reviewQueue;
    private readonly ObservableCollection<ArchitectPreferenceItem> _architects = [];
    private OnlineCandidate? _candidate;
    private CancellationTokenSource? _onlineCts;
    private bool _allowClose;

    public MainWindow(RotationService rotation, ReviewQueueService reviewQueue, Func<bool, Task> save)
    {
        InitializeComponent();
        _rotation = rotation;
        _reviewQueue = reviewQueue;
        _save = save;
        LoadSettings();
        MonitorText.Text = _rotation.MonitorSummary();
        _rotation.StatusChanged += Rotation_StatusChanged;
        ShowNextCandidate();
        Closed += (_, _) => { _rotation.StatusChanged -= Rotation_StatusChanged; _onlineCts?.Cancel(); };
        Closing += (_, args) =>
        {
            if (_allowClose) return;
            args.Cancel = true;
            Hide();
            StatusText.Text = "Archipaper is still running in the notification area.";
        };
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    private void LoadSettings()
    {
        var settings = _rotation.Settings;
        FolderText.Text = settings.LocalImageFolder;
        WallpaperSourceCombo.SelectedItem = WallpaperSourceCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == settings.WallpaperSourceMode)
            ?? WallpaperSourceCombo.Items[2];
        AutomaticCheck.IsChecked = settings.RotateAutomatically;
        DifferentCheck.IsChecked = settings.UseDifferentImagePerMonitor;
        RecentCheck.IsChecked = settings.AvoidRecentImages;
        StartupCheck.IsChecked = settings.StartWithWindows;
        WikimediaSourceCheck.IsChecked = settings.SearchWikimedia;
        OpenverseSourceCheck.IsChecked = settings.SearchOpenverse;
        LibraryOfCongressSourceCheck.IsChecked = settings.SearchLibraryOfCongress;
        BuildingsCategory.IsChecked = settings.EnabledCategories.Contains("Buildings");
        InteriorsCategory.IsChecked = settings.EnabledCategories.Contains("Interiors");
        DetailsCategory.IsChecked = settings.EnabledCategories.Contains("Details");
        DrawingsCategory.IsChecked = settings.EnabledCategories.Contains("Drawings");
        ModelsCategory.IsChecked = settings.EnabledCategories.Contains("Models");
        StrictArchitectCheck.IsChecked = settings.StrictArchitectSearch;
        var names = settings.AvailableArchitects
            .Concat(settings.PreferredArchitects)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x)
            .ToList();
        _architects.Clear();
        foreach (var name in names)
        {
            _architects.Add(new ArchitectPreferenceItem
            {
                Name = name,
                IsEnabled = settings.PreferredArchitects.Contains(name, StringComparer.OrdinalIgnoreCase)
            });
        }
        ArchitectList.ItemsSource = _architects;
        IntervalCombo.SelectedItem = IntervalCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == settings.RotationMinutes.ToString()) ?? IntervalCombo.Items[2];
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveFromControlsAsync();

    private async Task SaveFromControlsAsync(bool rotateNow = false)
    {
        var settings = _rotation.Settings;
        settings.LocalImageFolder = FolderText.Text.Trim();
        settings.WallpaperSourceMode = WallpaperSourceCombo.SelectedItem is ComboBoxItem sourceItem
            ? sourceItem.Tag?.ToString() ?? AppSettings.ApprovedAndLocal
            : AppSettings.ApprovedAndLocal;
        settings.RotateAutomatically = AutomaticCheck.IsChecked == true;
        settings.UseDifferentImagePerMonitor = DifferentCheck.IsChecked == true;
        settings.AvoidRecentImages = RecentCheck.IsChecked == true;
        settings.StartWithWindows = StartupCheck.IsChecked == true;
        settings.SearchWikimedia = WikimediaSourceCheck.IsChecked == true;
        settings.SearchOpenverse = OpenverseSourceCheck.IsChecked == true;
        settings.SearchLibraryOfCongress = LibraryOfCongressSourceCheck.IsChecked == true;
        settings.StrictArchitectSearch = StrictArchitectCheck.IsChecked == true;
        settings.EnabledCategories = new[]
        {
            ("Buildings", BuildingsCategory), ("Interiors", InteriorsCategory),
            ("Details", DetailsCategory), ("Drawings", DrawingsCategory),
            ("Models", ModelsCategory)
        }.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1).ToList();
        settings.AvailableArchitects = _architects.Select(x => x.Name).ToList();
        settings.PreferredArchitects = _architects.Where(x => x.IsEnabled).Select(x => x.Name).ToList();
        if (IntervalCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var minutes))
            settings.RotationMinutes = minutes;
        await _save(rotateNow);
        if (!rotateNow) StatusText.Text = "Preferences saved.";
    }

    private void ChooseFolder_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "Choose your Archipaper image collection",
            UseDescriptionForTitle = true,
            SelectedPath = Directory.Exists(FolderText.Text) ? FolderText.Text : ""
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK) FolderText.Text = dialog.SelectedPath;
    }

    private void AddArchitect_Click(object sender, RoutedEventArgs e)
    {
        var name = NewArchitectText.Text.Trim();
        if (name.Length == 0) return;
        var existing = _architects.FirstOrDefault(x => x.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.IsEnabled = true;
            ArchitectList.Items.Refresh();
            ArchitectList.ScrollIntoView(existing);
            StatusText.Text = $"{existing.Name} is selected again.";
        }
        else
        {
            var item = new ArchitectPreferenceItem { Name = name, IsEnabled = true };
            _architects.Add(item);
            ArchitectList.ScrollIntoView(item);
            StatusText.Text = $"{name} added to preferred architects.";
        }
        NewArchitectText.Clear();
    }

    private void RemoveArchitect_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not System.Windows.Controls.Button { DataContext: ArchitectPreferenceItem item }) return;
        _architects.Remove(item);
        StatusText.Text = $"{item.Name} removed from the saved architect list.";
    }

    private string BuildWebSearchQuery()
    {
        var architectNames = _architects.Where(x => x.IsEnabled).Select(x => $"\"{x.Name}\"").ToList();
        var categoryTerms = new[]
        {
            (BuildingsCategory.IsChecked == true, "architecture"),
            (InteriorsCategory.IsChecked == true, "interior"),
            (DetailsCategory.IsChecked == true, "architectural detail"),
            (DrawingsCategory.IsChecked == true, "architectural drawing sketch"),
            (ModelsCategory.IsChecked == true, "architectural model")
        }.Where(x => x.Item1).Select(x => x.Item2).ToList();

        var subject = architectNames.Count switch
        {
            0 => "architecture",
            1 => architectNames[0],
            _ => "(" + string.Join(" OR ", architectNames) + ")"
        };
        var category = categoryTerms.Count == 0 ? "architecture" : string.Join(" OR ", categoryTerms);
        return $"{subject} {category}";
    }

    private void SearchFlickr_Click(object sender, RoutedEventArgs e)
    {
        var query = Uri.EscapeDataString(BuildWebSearchQuery());
        OpenWebPage($"https://www.flickr.com/search/?text={query}&license=4%2C5%2C9%2C10");
        StatusText.Text = "Opened a Creative Commons Flickr search. Import any images you choose.";
    }

    private void SearchGoogle_Click(object sender, RoutedEventArgs e)
    {
        var query = Uri.EscapeDataString(BuildWebSearchQuery());
        OpenWebPage($"https://www.google.com/search?tbm=isch&q={query}");
        StatusText.Text = "Opened Google Images. Check usage rights before importing an image.";
    }

    private static void OpenWebPage(string url) =>
        Process.Start(new ProcessStartInfo(HttpTransfer.UrlKey(url)) { UseShellExecute = true });

    private async void ImportImages_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "Import images into Archipaper",
            Filter = "Image files|*.jpg;*.jpeg;*.png;*.bmp",
            Multiselect = true
        };
        if (dialog.ShowDialog(this) == true)
            await ImportFilesAsync(dialog.FileNames);
    }

    private void Collection_DragOver(object sender, System.Windows.DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
            ? System.Windows.DragDropEffects.Copy
            : System.Windows.DragDropEffects.None;
        e.Handled = true;
    }

    private async void Collection_Drop(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] paths)
            await ImportFilesAsync(paths);
    }

    private async Task ImportFilesAsync(IEnumerable<string> paths)
    {
        try
        {
            await _reviewQueue.ImportLocalFilesAsync(paths);
            RefreshApprovedCollection();
            QueueStatus.Text = _reviewQueue.LastImportMessage;
        }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            QueueStatus.Text = "One or more images could not be imported.";
        }
    }

    private async void ChangeNow_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Choosing wallpapers…";
        await SaveFromControlsAsync(rotateNow: true);
    }

    private void Rotation_StatusChanged(object? sender, string message) =>
        Dispatcher.BeginInvoke(() => StatusText.Text = message);

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        if (_onlineCts is not null) { _onlineCts.Cancel(); return; }
        DiscoverButton.Content = "CANCEL SEARCH";
        QueueStatus.Text = "Searching the architecture collection…";
        _onlineCts = new CancellationTokenSource();
        try
        {
            await SaveFromControlsAsync();
            await _reviewQueue.DiscoverAsync(_rotation.Settings, _onlineCts.Token);
            ShowNextCandidate();
            RefreshApprovedCollection();
            QueueStatus.Text = _reviewQueue.LastDiscoveryMessage;
        }
        catch (OperationCanceledException) { QueueStatus.Text = "Search cancelled."; }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            QueueStatus.Text = "The online collection is temporarily unavailable. Local rotation is unaffected.";
        }
        finally { _onlineCts.Dispose(); _onlineCts = null; DiscoverButton.Content = "DISCOVER NEW IMAGES"; }
    }

    private async void Approve_Click(object sender, RoutedEventArgs e)
    {
        if (_candidate is null) return;
        _candidate.Architect = CandidateArchitectText.Text.Trim();
        _candidate.ProjectName = CandidateProjectText.Text.Trim();
        ReviewPanel.IsEnabled = false;
        QueueStatus.Text = "Downloading the full-resolution image…";
        try
        {
            var fullResolution = await _reviewQueue.ApproveAsync(_candidate, CancellationToken.None);
            QueueStatus.Text = fullResolution
                ? "Approved. This image is now available for wallpaper rotation."
                : "Approved using the cached preview because the full-resolution download was unavailable.";
            ShowNextCandidate();
            RefreshApprovedCollection();
        }
        catch (Exception ex) { AppLog.Error(ex); QueueStatus.Text = "No usable full-size image could be saved; the image remains pending. Retry or skip it."; }
        finally { ReviewPanel.IsEnabled = true; }
    }

    private async void Skip_Click(object sender, RoutedEventArgs e)
    {
        if (_candidate is null) return;
        await _reviewQueue.SkipAsync(_candidate);
        QueueStatus.Text = "Skipped for now. The image remains in the review queue.";
        ShowNextCandidate();
    }

    private async void Reject_Click(object sender, RoutedEventArgs e)
    {
        if (_candidate is null) return;
        await _reviewQueue.RejectAsync(_candidate);
        QueueStatus.Text = "Rejected. Archipaper will not show that image again.";
        ShowNextCandidate();
    }

    private void Source_Click(object sender, RoutedEventArgs e)
    {
        if (_candidate is null || string.IsNullOrWhiteSpace(_candidate.SourcePageUrl)) return;
        OpenWebPage(_candidate.SourcePageUrl);
    }

    private void ShowNextCandidate()
    {
        _candidate = _reviewQueue.Current();
        ReviewPanel.Visibility = _candidate is null ? Visibility.Collapsed : Visibility.Visible;
        if (_candidate is null)
        {
            CandidateImage.Source = null;
            QueueStatus.Text = "No images waiting for review.";
            return;
        }
        CandidateTitle.Text = _candidate.Title;
        CandidateSubject.Text = _candidate.ArchitectOrCategory;
        var resolution = _candidate.Width > 0 && _candidate.Height > 0 ? $" · {_candidate.Width:N0} × {_candidate.Height:N0}" : "";
        CandidateCredit.Text = $"{_candidate.SourceName} · {_candidate.Artist} · {_candidate.License}{resolution}";
        var architect = _candidate.Architect?.Trim() ?? "";
        CandidateArchitectText.Text = architect;
        CandidateProjectText.Text = string.IsNullOrWhiteSpace(_candidate.ProjectName)
            ? ArchitectureMetadata.CleanProjectName(_candidate.Title, architect)
            : _candidate.ProjectName;
        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 600;
            bitmap.UriSource = new Uri(_candidate.PreviewFilePath, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            CandidateImage.Source = bitmap;
        }
        catch (Exception ex) { AppLog.Error(ex); CandidateImage.Source = null; }
    }

    private void Collection_Expanded(object sender, RoutedEventArgs e) => RefreshApprovedCollection();

    private void RefreshApprovedCollection()
    {
        if (!ApprovedExpander.IsExpanded) return;
        var selected = (ApprovedList.SelectedItem as ApprovedImageMetadata)?.Id;
        var approved = _reviewQueue.Approved;
        ApprovedList.ItemsSource = approved;
        ApprovedList.SelectedItem = approved.FirstOrDefault(x => x.Id == selected);
        ApprovedDetails.Text = _reviewQueue.Approved.Count == 0
            ? "Approve images from the review queue to build your collection."
            : $"{_reviewQueue.Approved.Count} approved image{(_reviewQueue.Approved.Count == 1 ? "" : "s")}";
    }

    private void ApprovedList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = ApprovedList.SelectedItem as ApprovedImageMetadata;
        FavoriteButton.IsEnabled = ApprovedSourceButton.IsEnabled = RemoveButton.IsEnabled = selected is not null;
        if (selected is null) return;
        FavoriteButton.Content = selected.IsFavorite ? "UNFAVORITE" : "FAVORITE";
        ApprovedDetails.Text = $"{selected.SourceName} · {selected.Artist} · {selected.License}".Trim(' ', '·');
    }

    private async void Favorite_Click(object sender, RoutedEventArgs e)
    {
        if (ApprovedList.SelectedItem is not ApprovedImageMetadata selected) return;
        await _reviewQueue.ToggleFavoriteAsync(selected);
        RefreshApprovedCollection();
        QueueStatus.Text = selected.IsFavorite ? "Added to favorites." : "Removed from favorites.";
    }

    private void ApprovedSource_Click(object sender, RoutedEventArgs e)
    {
        if (ApprovedList.SelectedItem is ApprovedImageMetadata selected && !string.IsNullOrWhiteSpace(selected.SourcePageUrl))
            OpenWebPage(selected.SourcePageUrl);
    }

    private async void Remove_Click(object sender, RoutedEventArgs e)
    {
        if (ApprovedList.SelectedItem is not ApprovedImageMetadata selected) return;
        var answer = MessageBox.Show("Remove this image from wallpaper rotation? The file will be kept in Archipaper's removed-items folder.",
            "Remove wallpaper", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (answer != MessageBoxResult.Yes) return;
        await _reviewQueue.RemoveApprovedAsync(selected);
        RefreshApprovedCollection();
        QueueStatus.Text = "Removed from rotation. The image file was retained.";
    }

}

public sealed class FilePathToImageConverter : IValueConverter
{
    private readonly Dictionary<string, (DateTime Modified, BitmapImage Image)> _cache = new(StringComparer.OrdinalIgnoreCase);
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is not string path || !File.Exists(path)) return DependencyProperty.UnsetValue;
            var modified = File.GetLastWriteTimeUtc(path);
            if (_cache.TryGetValue(path, out var cached) && cached.Modified == modified) return cached.Image;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 300;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            if (_cache.Count >= 256) _cache.Remove(_cache.Keys.First());
            _cache[path] = (modified, bitmap);
            return bitmap;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        System.Windows.Data.Binding.DoNothing;
}
