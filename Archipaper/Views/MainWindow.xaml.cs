using System.Windows;
using System.Windows.Controls;
using Archipaper.Services;
using Archipaper.Models;
using System.Diagnostics;
using System.Windows.Media.Imaging;
using Forms = System.Windows.Forms;

namespace Archipaper.Views;

public partial class MainWindow : Window
{
    private readonly RotationService _rotation;
    private readonly Func<Task> _save;
    private readonly ReviewQueueService _reviewQueue;
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
        AutomaticCheck.IsChecked = settings.RotateAutomatically;
        DifferentCheck.IsChecked = settings.UseDifferentImagePerMonitor;
        RecentCheck.IsChecked = settings.AvoidRecentImages;
        StartupCheck.IsChecked = settings.StartWithWindows;
        BuildingsCategory.IsChecked = settings.EnabledCategories.Contains("Buildings");
        InteriorsCategory.IsChecked = settings.EnabledCategories.Contains("Interiors");
        DetailsCategory.IsChecked = settings.EnabledCategories.Contains("Details");
        DrawingsCategory.IsChecked = settings.EnabledCategories.Contains("Drawings");
        ModelsCategory.IsChecked = settings.EnabledCategories.Contains("Models");
        ParametricCategory.IsChecked = settings.EnabledCategories.Contains("Parametric Architecture");
        ArchitectsText.Text = string.Join(", ", settings.PreferredArchitects);
        BoostedArchitectsText.Text = string.Join(", ", settings.BoostedArchitects);
        IntervalCombo.SelectedItem = IntervalCombo.Items.Cast<ComboBoxItem>()
            .FirstOrDefault(x => x.Tag?.ToString() == settings.RotationMinutes.ToString()) ?? IntervalCombo.Items[2];
    }

    private async void Save_Click(object sender, RoutedEventArgs e) => await SaveFromControlsAsync();

    private async Task SaveFromControlsAsync()
    {
        var settings = _rotation.Settings;
        settings.LocalImageFolder = FolderText.Text.Trim();
        settings.RotateAutomatically = AutomaticCheck.IsChecked == true;
        settings.UseDifferentImagePerMonitor = DifferentCheck.IsChecked == true;
        settings.AvoidRecentImages = RecentCheck.IsChecked == true;
        settings.StartWithWindows = StartupCheck.IsChecked == true;
        settings.EnabledCategories = new[]
        {
            ("Buildings", BuildingsCategory), ("Interiors", InteriorsCategory),
            ("Details", DetailsCategory), ("Drawings", DrawingsCategory),
            ("Models", ModelsCategory), ("Parametric Architecture", ParametricCategory)
        }.Where(x => x.Item2.IsChecked == true).Select(x => x.Item1).ToList();
        settings.PreferredArchitects = ParseList(ArchitectsText.Text);
        settings.BoostedArchitects = ParseList(BoostedArchitectsText.Text);
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
        ReviewPanel.IsEnabled = false;
        QueueStatus.Text = "Downloading the full-resolution image…";
        try
        {
            await _reviewQueue.ApproveAsync(_candidate, CancellationToken.None);
            QueueStatus.Text = "Approved. This image is now available for wallpaper rotation.";
            ShowNextCandidate();
            RefreshApprovedCollection();
        }
        catch (Exception ex) { AppLog.Error(ex); QueueStatus.Text = "Download failed; the image remains in the review queue."; }
        finally { ReviewPanel.IsEnabled = true; }
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
        CandidateCredit.Text = $"{_candidate.Artist} · {_candidate.License} · {_candidate.Width:N0} × {_candidate.Height:N0}";
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

    private static List<string> ParseList(string value) => value
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Distinct(StringComparer.OrdinalIgnoreCase).ToList();

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
        ApprovedDetails.Text = $"{selected.Artist} · {selected.License}";
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

    private void History_Expanded(object sender, RoutedEventArgs e)
    {
        HistoryList.ItemsSource = _rotation.History.Take(30)
            .Select(x => $"{x.AppliedAt.LocalDateTime:g}   ·   {Path.GetFileNameWithoutExtension(x.FilePath)}")
            .ToList();
    }
}
