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

        TimeSpan totalDuration = TimeSpan.FromSeconds(0);
        foreach (var item in vidList)
        {
            sb.AppendLine(item.ToString());

            if (item.Duration != null)
                totalDuration += (TimeSpan)item.Duration;
        }
        sb.AppendLine();
        sb.AppendLine("Playlist Duration: " + totalDuration.ToString());
        File.WriteAllText(strFilePath, sb.ToString());
    }
}
