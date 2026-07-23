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
    private readonly Func<Task> _save;
    private readonly ReviewQueueService _reviewQueue;
    private readonly ObservableCollection<ArchitectPreferenceItem> _architects = [];
    private OnlineCandidate? _candidate;
    private CancellationTokenSource? _onlineCts;
    private bool _allowClose;

    public MainWindow(RotationService rotation, ReviewQueueService reviewQueue, Func<Task> save)
    {
        InitializeComponent();
        _rotation = rotation;
        _reviewQueue = reviewQueue;
        _save = save;
        LoadSettings();
        MonitorText.Text = _rotation.MonitorSummary();
        _rotation.StatusChanged += Rotation_StatusChanged;
        ShowNextCandidate();
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
            .Concat(settings.BoostedArchitects)
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
                IsEnabled = settings.PreferredArchitects.Contains(name, StringComparer.OrdinalIgnoreCase),
                IsBoosted = settings.BoostedArchitects.Contains(name, StringComparer.OrdinalIgnoreCase)
            });
        }
        ArchitectList.ItemsSource = _architects;
        IntervalCombo.SelectedItem = IntervalCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == settings.RotationMinutes.ToString()) ?? IntervalCombo.Items[2];
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveFromControlsAsync();

    private async Task SaveFromControlsAsync()
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
        settings.BoostedArchitects = _architects.Where(x => x.IsBoosted).Select(x => x.Name).ToList();
        if (IntervalCombo.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out var minutes))
            settings.RotationMinutes = minutes;
        await _save();
        StatusText.Text = "Preferences saved.";
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

    private async void ChangeNow_Click(object sender, RoutedEventArgs e)
    {
        await SaveFromControlsAsync();
        StatusText.Text = "Choosing wallpapers…";
        await _rotation.RotateAsync();
    }

    private void Rotation_StatusChanged(object? sender, string message) =>
        Dispatcher.Invoke(() => StatusText.Text = message);

    private async void Discover_Click(object sender, RoutedEventArgs e)
    {
        DiscoverButton.IsEnabled = false;
        QueueStatus.Text = "Searching the architecture collection…";
        _onlineCts?.Cancel();
        _onlineCts = new CancellationTokenSource();
        try
        {
            await SaveFromControlsAsync();
            var added = await _reviewQueue.DiscoverAsync(_rotation.Settings, _onlineCts.Token);
            QueueStatus.Text = added == 0 ? "No new suitable images found. Try again later." : $"Added {added} images for review.";
            ShowNextCandidate();
            RefreshApprovedCollection();
        }
        catch (OperationCanceledException) { QueueStatus.Text = "Search cancelled."; }
        catch (Exception ex)
        {
            AppLog.Error(ex);
            QueueStatus.Text = "The online collection is temporarily unavailable. Local rotation is unaffected.";
        }
        finally { DiscoverButton.IsEnabled = true; }
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
        catch (Exception ex) { AppLog.Error(ex); QueueStatus.Text = "Download failed; the image remains in the review queue."; }
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
        Process.Start(new ProcessStartInfo(_candidate.SourcePageUrl) { UseShellExecute = true });
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
        ApprovedList.ItemsSource = null;
        ApprovedList.ItemsSource = _reviewQueue.Approved;
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
            Process.Start(new ProcessStartInfo(selected.SourcePageUrl) { UseShellExecute = true });
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
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        try
        {
            if (value is not string path || !File.Exists(path)) return DependencyProperty.UnsetValue;
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = 300;
            bitmap.UriSource = new Uri(path, UriKind.Absolute);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return DependencyProperty.UnsetValue;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
