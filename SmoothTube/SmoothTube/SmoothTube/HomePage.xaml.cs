using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using SmoothTube.Models;
using SmoothTube.Services;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class HomePage : Page
    {
        public ObservableCollection<VideoItem> Videos { get; } = [];

        public ObservableCollection<VideoItem> ContinueWatchingVideos { get; } = [];

        public ObservableCollection<int> SkeletonItems { get; } = [];

        public string PrimarySectionTitle { get; set; } = "Recommended";

        public string StatusText { get; set; } = "Loading videos...";

        public Visibility ContinueWatchingVisibility { get; set; } = Visibility.Collapsed;

        public Visibility LoadingVisibility { get; set; } = Visibility.Visible;

        public Visibility VideosVisibility { get; set; } = Visibility.Collapsed;

        public Visibility LoadMoreVisibility { get; set; } = Visibility.Collapsed;

        private bool isLoadingMore;

        private bool hasLoadedOnce;

        protected override async void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (hasLoadedOnce)
            {
                await RefreshContinueWatchingAsync();
            }
        }

        public HomePage()
        {
            InitializeComponent();

            Loaded += HomePage_Loaded;
        }

        private async void HomePage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await LoadVideosAsync();
        }

        private async Task RefreshContinueWatchingAsync()
        {
            List<VideoItem> continueWatchingVideos =
                WatchHistoryService.GetContinueWatching();

            await EnrichContinueWatchingVideosAsync(continueWatchingVideos);

            ReplaceVideos(
                ContinueWatchingVideos,
                continueWatchingVideos);

            ContinueWatchingVisibility =
                ContinueWatchingVideos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            Bindings.Update();
        }

        private async Task LoadVideosAsync()
        {
            StatusText = "Loading videos...";
            LoadingVisibility = Visibility.Visible;
            VideosVisibility = Visibility.Collapsed;

            PrimarySectionTitle = "Recommended";

            await RefreshContinueWatchingAsync();

            if (SkeletonItems.Count == 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    SkeletonItems.Add(i);
                }
            }

            Bindings.Update();

            try
            {
                Task<List<VideoItem>> primaryTask =
                    ServiceLocator.YouTube.GetHomeVideosAsync();

                await primaryTask;
                ReplaceVideosIfAny(Videos, primaryTask.Result);
                StatusText = "";
                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Visibility.Visible;
                LoadMoreVisibility =
                    Videos.Count > 0
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                Bindings.Update();
            }
            catch (System.Exception)
            {
                StatusText = "";
                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Videos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                LoadMoreVisibility = VideosVisibility;
            }

            hasLoadedOnce = true;
            Bindings.Update();
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingMore)
                return;

            isLoadingMore = true;
            StatusText = "Loading more recommendations...";
            LoadMoreVisibility = Visibility.Collapsed;
            Bindings.Update();

            try
            {
                List<VideoItem> moreVideos =
                    await ServiceLocator.YouTube.GetMoreHomeVideosAsync(
                        Videos.Select(video => video.Id));

                foreach (VideoItem video in moreVideos)
                {
                    if (Videos.All(item => item.Id != video.Id))
                    {
                        Videos.Add(video);
                    }
                }

                StatusText =
                    moreVideos.Count == 0
                        ? "No more recommendations returned right now."
                        : "";
            }
            catch (System.Exception)
            {
                StatusText = "Could not load more recommendations.";
            }

            isLoadingMore = false;
            LoadMoreVisibility = Visibility.Visible;
            Bindings.Update();
        }


        private static async Task EnrichContinueWatchingVideosAsync(
            List<VideoItem> videos)
        {
            List<VideoItem> targets =
                videos
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .Where(video =>
                        string.IsNullOrWhiteSpace(video.Duration) ||
                        string.IsNullOrWhiteSpace(video.Thumbnail) ||
                        string.IsNullOrWhiteSpace(video.Channel))
                    .Take(10)
                    .ToList();

            if (targets.Count == 0)
            {
                return;
            }

            Task<VideoItem?>[] tasks =
                targets
                    .Select(video =>
                        ServiceLocator.YouTube.GetVideoAsync(video.Id))
                    .ToArray();

            VideoItem?[] enrichedVideos;

            try
            {
                enrichedVideos = await Task.WhenAll(tasks);
            }
            catch
            {
                return;
            }

            foreach (VideoItem target in targets)
            {
                VideoItem? enriched =
                    enrichedVideos.FirstOrDefault(video => video?.Id == target.Id);

                if (enriched == null)
                {
                    continue;
                }

                double progress = target.Progress;
                double resumeSeconds = target.ResumeSeconds;
                double durationSeconds = target.DurationSeconds;

                target.Title = string.IsNullOrWhiteSpace(target.Title) ? enriched.Title : target.Title;
                target.Channel = string.IsNullOrWhiteSpace(target.Channel) ? enriched.Channel : target.Channel;
                target.ChannelId = string.IsNullOrWhiteSpace(target.ChannelId) ? enriched.ChannelId : target.ChannelId;
                target.Views = string.IsNullOrWhiteSpace(target.Views) ? enriched.Views : target.Views;
                target.Duration = string.IsNullOrWhiteSpace(target.Duration) ? enriched.Duration : target.Duration;
                target.PublishedAt = string.IsNullOrWhiteSpace(target.PublishedAt) ? enriched.PublishedAt : target.PublishedAt;
                target.Thumbnail = string.IsNullOrWhiteSpace(target.Thumbnail) ? enriched.Thumbnail : target.Thumbnail;
                target.IsEmbeddable = target.IsEmbeddable || enriched.IsEmbeddable;
                target.IsLive = enriched.IsLive;
                target.IsPremiere = enriched.IsPremiere;
                target.IsShort = target.IsShort || enriched.IsShort;
                target.LiveChatId = enriched.LiveChatId;

                target.Progress = progress;
                target.ResumeSeconds = resumeSeconds;
                target.DurationSeconds = durationSeconds;

                WatchHistoryService.UpdateMetadata(target);
                WatchHistoryService.ApplySavedProgress(target);
            }
        }

        private static void ReplaceVideosIfAny(
            ObservableCollection<VideoItem> target,
            List<VideoItem> videos)
        {
            if (videos.Count > 0)
            {
                ReplaceVideos(target, videos);
            }
        }

        private static void ReplaceVideos(
            ObservableCollection<VideoItem> target,
            IEnumerable<VideoItem> videos)
        {
            target.Clear();

            foreach (VideoItem video in videos)
            {
                target.Add(video);
            }
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                Frame.Navigate(
                    typeof(VideoPage),
                    new VideoNavigationData
                    {
                        CurrentVideo = video,
                        AllVideos = GetCurrentHomeVideos()
                    });
            }
        }

        private List<VideoItem> GetCurrentHomeVideos()
        {
            List<VideoItem> videos = [];
            videos.AddRange(Videos);
            videos.AddRange(ContinueWatchingVideos);
            return videos;
        }
    }
}
