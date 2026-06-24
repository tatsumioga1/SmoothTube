using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class HomePage : Page
    {
        private const string RecommendedCacheFileName = "home-recommended-cache.json";

        private sealed class HomeRecommendedCache
        {
            public List<VideoItem> Videos { get; set; } = [];
            public DateTimeOffset? RefreshedAt { get; set; }
        }

        private static readonly List<VideoItem> CachedRecommendedVideos = [];
        private static DateTimeOffset? cachedRecommendedRefreshedAt;
        private static bool triedPersistentRecommendedCache;

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
        private bool hasLoadedThisPage;

        public HomePage()
        {
            InitializeComponent();

            Loaded += HomePage_Loaded;
        }

        private async void HomePage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (hasLoadedThisPage)
                return;

            hasLoadedThisPage = true;
            await LoadVideosAsync(false);
        }

        private async void RefreshRecommendedButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadVideosAsync(true);
        }

        private async Task LoadVideosAsync(bool forceRefresh)
        {
            RefreshContinueWatching();

            PrimarySectionTitle = "Recommended";

            if (SkeletonItems.Count == 0)
            {
                for (int i = 0; i < 8; i++)
                {
                    SkeletonItems.Add(i);
                }
            }

            if (!forceRefresh)
            {
                EnsurePersistentRecommendedCacheLoaded();
            }

            if (!forceRefresh && CachedRecommendedVideos.Count > 0)
            {
                ReplaceVideos(Videos, CachedRecommendedVideos);
                StatusText = FormatRecommendedCacheStatus();
                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Visibility.Visible;
                LoadMoreVisibility = Visibility.Visible;
                Bindings.Update();
                return;
            }

            StatusText = forceRefresh
                ? "Refreshing recommendations..."
                : "Loading videos...";

            LoadingVisibility = Videos.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            VideosVisibility = Videos.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            Bindings.Update();

            try
            {
                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetHomeVideosAsync();

                if (videos.Count > 0)
                {
                    CachedRecommendedVideos.Clear();
                    CachedRecommendedVideos.AddRange(videos);
                    cachedRecommendedRefreshedAt = DateTimeOffset.Now;
                    SavePersistentRecommendedCache();

                    ReplaceVideos(Videos, videos);
                }

                StatusText = Videos.Count == 0
                    ? "No recommendations loaded. Press Refresh to try again."
                    : FormatRecommendedCacheStatus();

                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Videos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                LoadMoreVisibility = Videos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            catch (Exception)
            {
                StatusText = Videos.Count > 0
                    ? FormatRecommendedCacheStatus()
                    : "Could not load recommendations. Press Refresh to try again.";

                LoadingVisibility = Visibility.Collapsed;
                VideosVisibility = Videos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
                LoadMoreVisibility = VideosVisibility;
            }

            Bindings.Update();
        }

        private void RefreshContinueWatching()
        {
            ReplaceVideos(
                ContinueWatchingVideos,
                WatchHistoryService.GetContinueWatching());

            ContinueWatchingVisibility =
                ContinueWatchingVideos.Count > 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
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

                    if (CachedRecommendedVideos.All(item => item.Id != video.Id))
                    {
                        CachedRecommendedVideos.Add(video);
                    }
                }

                if (moreVideos.Count > 0)
                {
                    cachedRecommendedRefreshedAt ??= DateTimeOffset.Now;
                    SavePersistentRecommendedCache();
                }

                StatusText = moreVideos.Count == 0
                    ? "No more recommendations returned right now."
                    : FormatRecommendedCacheStatus();
            }
            catch (Exception)
            {
                StatusText = "Could not load more recommendations.";
            }

            isLoadingMore = false;
            LoadMoreVisibility = Videos.Count > 0
                ? Visibility.Visible
                : Visibility.Collapsed;
            Bindings.Update();
        }

        private static void EnsurePersistentRecommendedCacheLoaded()
        {
            if (triedPersistentRecommendedCache)
                return;

            triedPersistentRecommendedCache = true;

            HomeRecommendedCache? cache =
                PersistentCacheService.Load<HomeRecommendedCache>(
                    RecommendedCacheFileName);

            if (cache?.Videos == null || cache.Videos.Count == 0)
                return;

            CachedRecommendedVideos.Clear();
            CachedRecommendedVideos.AddRange(cache.Videos);
            cachedRecommendedRefreshedAt = cache.RefreshedAt;
        }

        private static void SavePersistentRecommendedCache()
        {
            if (CachedRecommendedVideos.Count == 0)
                return;

            PersistentCacheService.Save(
                RecommendedCacheFileName,
                new HomeRecommendedCache
                {
                    Videos = CachedRecommendedVideos.ToList(),
                    RefreshedAt = cachedRecommendedRefreshedAt
                });
        }

        private string FormatRecommendedCacheStatus()
        {
            if (Videos.Count == 0)
                return "Press Refresh to load recommendations.";

            string refreshedText = cachedRecommendedRefreshedAt.HasValue
                ? $" Last refreshed {cachedRecommendedRefreshedAt.Value.LocalDateTime:g}."
                : "";

            return $"Currently loaded: {Videos.Count} recommended videos.{refreshedText} Press Refresh to check for newer recommendations.";
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
