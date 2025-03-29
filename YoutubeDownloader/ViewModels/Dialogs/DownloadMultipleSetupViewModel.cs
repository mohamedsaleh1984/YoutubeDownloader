using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Avalonia;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using YoutubeDownloader.Core.Downloading;
using YoutubeDownloader.Framework;
using YoutubeDownloader.Services;
using YoutubeDownloader.Utils;
using YoutubeDownloader.Utils.Extensions;
using YoutubeDownloader.ViewModels.Components;
using YoutubeExplode.Videos;
using YoutubeExplode.Videos.Streams;

namespace YoutubeDownloader.ViewModels.Dialogs;

public partial class DownloadMultipleSetupViewModel(
    ViewModelManager viewModelManager,
    DialogManager dialogManager,
    SettingsService settingsService
) : DialogViewModelBase<IReadOnlyList<DownloadViewModel>>
{
    [ObservableProperty]
    private string? _title;

    [ObservableProperty]
    private IReadOnlyList<IVideo>? _availableVideos;

    [ObservableProperty]
    private Container _selectedContainer = Container.Mp4;

    [ObservableProperty]
    private VideoQualityPreference _selectedVideoQualityPreference = VideoQualityPreference.Highest;

    public ObservableCollection<IVideo> SelectedVideos { get; } = [];

    public IReadOnlyList<Container> AvailableContainers { get; } =
        [Container.Mp4, Container.WebM, Container.Mp3, new Container("ogg")];

    public IReadOnlyList<VideoQualityPreference> AvailableVideoQualityPreferences { get; } =
        Enum.GetValues<VideoQualityPreference>().ToArray();

    [RelayCommand]
    private void Initialize()
    {
        SelectedContainer = settingsService.LastContainer;
        SelectedVideoQualityPreference = settingsService.LastVideoQualityPreference;
        SelectedVideos.CollectionChanged += (_, _) => ConfirmCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CopyTitleAsync()
    {
        if (Application.Current?.ApplicationLifetime?.TryGetTopLevel()?.Clipboard is { } clipboard)
            await clipboard.SetTextAsync(Title);
    }

    private bool CanConfirm() => SelectedVideos.Any();

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private async Task ConfirmAsync()
    {
        var dirPath = await dialogManager.PromptDirectoryPathAsync();
        if (string.IsNullOrWhiteSpace(dirPath))
            return;

        if (settingsService.ShouldGeneratePlaylistMeta)
        {
            WritePlaylistMeta(dirPath);
        }

        bool IsPlaylist = Title == null ? false : Title.ToString().ToLower().Contains("playlist");

        var downloads = new List<DownloadViewModel>();
        for (var i = 0; i < SelectedVideos.Count; i++)
        {
            var video = SelectedVideos[i];

            string strFileNameTemplate =
                IsPlaylist && settingsService.ShouldAddSequenceNumber
                    ? "$num-$title"
                    : settingsService.FileNameTemplate;

            var baseFilePath = Path.Combine(
                dirPath,
                FileNameTemplate.Apply(
                    strFileNameTemplate,
                    video,
                    SelectedContainer,
                    (i + 1).ToString().PadLeft(SelectedVideos.Count.ToString().Length, '0')
                )
            );

            if (settingsService.ShouldSkipExistingFiles && File.Exists(baseFilePath))
                continue;

            var filePath = PathEx.EnsureUniquePath(baseFilePath);

            // Download does not start immediately, so lock in the file path to avoid conflicts
            DirectoryEx.CreateDirectoryForFile(filePath);
            await File.WriteAllBytesAsync(filePath, []);

            downloads.Add(
                viewModelManager.CreateDownloadViewModel(
                    video,
                    new VideoDownloadPreference(SelectedContainer, SelectedVideoQualityPreference),
                    filePath
                )
            );
        }

        settingsService.LastContainer = SelectedContainer;
        settingsService.LastVideoQualityPreference = SelectedVideoQualityPreference;

        Close(downloads);
    }

    private long TimeSpanToSeconds( TimeSpan ts)
    {
        long result = 0;
        int hoursToMins = ts.Hours * 60;
        result = (hoursToMins * 60) + (ts.Minutes * 60) + ts.Seconds;
        return result;
    }

    private string ConvertSecondsToTime(long totalSeconds)
    {
        long hours = totalSeconds / 3600;
        long minutes = (totalSeconds % 3600) / 60;
        long seconds = totalSeconds % 60;
        string str = string.Format("{0:00}:{1:00}:{2:00}", hours, minutes, seconds);
        return str;
    }

    private void WritePlaylistMeta(string dirPath)
    {
        string strFileName = "PlaylistInfo.txt";
        string strFilePath = Path.Combine(dirPath, strFileName);

        if (File.Exists(strFilePath))
            File.Delete(strFilePath);
        
        StringBuilder sb = new StringBuilder();
        sb.AppendLine(Title == null ? "Playlist: NA" : Title.ToString());
        sb.AppendLine("Videos List: ");
        List<IVideo> vidList = new List<IVideo>();
        if (AvailableVideos != null)
        {
            vidList.AddRange(AvailableVideos);
        }

        long seconds = 0;
        foreach (var item in vidList)
        {
            string strLine = item.ToString() + "   " + item.Duration.ToString();
            sb.AppendLine(strLine);

            if (item.Duration != null)
                seconds += TimeSpanToSeconds(item.Duration.Value);
        }

        sb.AppendLine();
        sb.AppendLine("Playlist Duration: " + ConvertSecondsToTime(seconds));
        File.WriteAllText(strFilePath, sb.ToString());
    }
}
