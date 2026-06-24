using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SmoothTube
{
    public sealed partial class ChannelPage : Page
    {
        private const string ChannelCacheFileName = "channel-page-cache.json";

        private sealed class ChannelPageCacheEntry
        {
            public ChannelItem Channel { get; set; } = new();
            public List<VideoItem> Videos { get; set; } = [];
            public int RequestedUploadCount { get; set; }
            public bool? IsSubscribed { get; set; }
            public DateTimeOffset LastLoadedAt { get; set; } = DateTimeOffset.Now;
        }

        private static readonly Dictionary<string, ChannelPageCacheEntry> ChannelCache =
            new(StringComparer.OrdinalIgnoreCase);

        private static bool triedPersistentChannelCache;

        public ChannelItem Channel { get; set; } = new();

        public ObservableCollection<VideoItem> UploadVideos { get; } = [];

        public ObservableCollection<VideoItem> ShortVideos { get; } = [];

        public ObservableCollection<VideoItem> LivestreamVideos { get; } = [];

        private List<VideoItem> allVideos = [];

        public string StatusText { get; set; } = "";

        private int requestedUploadCount = 24;
        private bool isLoadingMore;

        protected override void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is ChannelItem channel)
            {
                Channel = channel;
                requestedUploadCount = 24;
                Bindings.Update();
                UpdateBannerVisibility();
                _ = LoadChannelAsync(false);
            }
        }

        public ChannelPage()
        {
            InitializeComponent();
        }

        private async System.Threading.Tasks.Task LoadChannelAsync(bool forceRefresh)
        {
            if (!forceRefresh && TryLoadFromCache())
            {
                Bindings.Update();
                return;
            }

            StatusText = "Loading channel...";
            Bindings.Update();

            try
            {
                ChannelItem? updatedChannel =
                    await ServiceLocator.YouTube.GetChannelAsync(Channel.Id);

                if (updatedChannel != null)
                {
                    Channel = updatedChannel;
                    Bindings.Update();
                    UpdateBannerVisibility();
                }

                bool isSubscribed =
                    await UpdateSubscriptionButtonAsync();

                List<VideoItem> videos =
                    await ServiceLocator.YouTube.GetChannelVideosAsync(
                        Channel.Id,
                        requestedUploadCount);

                ReplaceVideos(videos);
                SaveToCache(isSubscribed);
            }
            catch (Exception ex)
            {
                StatusText = $"Could not load this channel: {ex.Message}";
                Bindings.Update();
                return;
            }

            StatusText = FormatLoadedStatusText();
            Bindings.Update();
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingMore)
                return;

            isLoadingMore = true;
            requestedUploadCount += 24;
            StatusText = "Loading more uploads...";
            Bindings.Update();

            int previousCount = allVideos.Count;
            await LoadChannelAsync(false);

            if (allVideos.Count <= previousCount)
            {
                StatusText =
                    "No more uploads were returned for this channel.\n" +
                    FormatLoadedStatusText();

                Bindings.Update();
            }

            isLoadingMore = false;
        }

        private static void EnsurePersistentChannelCacheLoaded()
        {
            if (triedPersistentChannelCache)
                return;

            triedPersistentChannelCache = true;

            Dictionary<string, ChannelPageCacheEntry>? cache =
                PersistentCacheService.Load<Dictionary<string, ChannelPageCacheEntry>>(
                    ChannelCacheFileName);

            if (cache == null || cache.Count == 0)
                return;

            ChannelCache.Clear();

            foreach (KeyValuePair<string, ChannelPageCacheEntry> item in cache)
            {
                if (!string.IsNullOrWhiteSpace(item.Key) &&
                    item.Value?.Videos != null)
                {
                    ChannelCache[item.Key] = item.Value;
                }
            }
        }

        private bool TryLoadFromCache()
        {
            EnsurePersistentChannelCacheLoaded();
            if (string.IsNullOrWhiteSpace(Channel.Id) ||
                !ChannelCache.TryGetValue(Channel.Id, out ChannelPageCacheEntry? cache) ||
                cache.Videos.Count == 0 ||
                cache.RequestedUploadCount < requestedUploadCount)
            {
                return false;
            }

            Channel = cache.Channel;
            ReplaceVideos(cache.Videos.Take(requestedUploadCount));
            StatusText = FormatLoadedStatusText(cache.LastLoadedAt);
            UpdateBannerVisibility();

            if (cache.IsSubscribed.HasValue)
            {
                ApplySubscriptionButtonState(cache.IsSubscribed.Value);
            }

            return true;
        }

        private void SaveToCache(bool isSubscribed)
        {
            if (string.IsNullOrWhiteSpace(Channel.Id))
                return;

            ChannelCache[Channel.Id] = new ChannelPageCacheEntry
            {
                Channel = Channel,
                Videos = allVideos.ToList(),
                RequestedUploadCount = requestedUploadCount,
                IsSubscribed = isSubscribed,
                LastLoadedAt = DateTimeOffset.Now
            };

            PersistentCacheService.Save(
                ChannelCacheFileName,
                ChannelCache);
        }

        private string FormatLoadedStatusText(DateTimeOffset? lastLoadedAt = null)
        {
            if (allVideos.Count == 0)
                return "No recent uploads loaded for this channel.\nLoad more to fetch additional content.";

            string text =
                $"Currently loaded: {UploadVideos.Count} uploads • {ShortVideos.Count} shorts • {LivestreamVideos.Count} livestreams\n" +
                "Load more to fetch additional content.";

            if (lastLoadedAt.HasValue)
            {
                text += $" Last loaded {lastLoadedAt.Value.LocalDateTime:g}.";
            }

            return text;
        }

        private void ReplaceVideos(IEnumerable<VideoItem> videos)
        {
            allVideos =
                videos
                    .Where(video => !string.IsNullOrWhiteSpace(video.Id))
                    .GroupBy(video => video.Id)
                    .Select(group => group.First())
                    .ToList();

            UploadVideos.Clear();
            ShortVideos.Clear();
            LivestreamVideos.Clear();

            foreach (VideoItem video in allVideos.Where(video =>
                !video.IsLive &&
                !IsLikelyShort(video)))
            {
                UploadVideos.Add(video);
            }

            foreach (VideoItem video in allVideos.Where(video =>
                !video.IsLive &&
                IsLikelyShort(video)))
            {
                ShortVideos.Add(video);
            }

            foreach (VideoItem video in allVideos.Where(video => video.IsLive))
            {
                LivestreamVideos.Add(video);
            }
        }

        private static bool IsLikelyShort(VideoItem video)
        {
            if (video.IsShort)
                return true;

            string title = video.Title ?? "";

            return
                title.Contains("#short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" short ", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" short", StringComparison.OrdinalIgnoreCase);
        }

        private void UpdateBannerVisibility()
        {
            bool hasBanner =
                !string.IsNullOrWhiteSpace(Channel.BannerImage);

            ChannelBannerImage.Source =
                hasBanner
                    ? CreateImageSource(Channel.BannerImage)
                    : null;

            ChannelThumbnailImage.Source =
                CreateImageSource(Channel.Thumbnail);

            ChannelBannerImage.Visibility =
                hasBanner
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            ChannelBannerPlaceholder.Visibility =
                hasBanner
                    ? Visibility.Collapsed
                    : Visibility.Visible;
        }

        private static BitmapImage? CreateImageSource(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !Uri.TryCreate(value, UriKind.RelativeOrAbsolute, out Uri? uri))
            {
                return null;
            }

            return new BitmapImage(uri);
        }

        private void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void SubscribeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SubscribeButton.IsEnabled = false;
            SubscribeButton.Content = "Subscribing...";

            bool success =
                await ServiceLocator.YouTube.SubscribeToChannelAsync(Channel.Id);

            SubscribeButton.Content =
                success
                    ? "Subscribed"
                    : "Sign in to subscribe";

            SubscribeButton.IsEnabled = !success;
        }

        private async System.Threading.Tasks.Task<bool> UpdateSubscriptionButtonAsync()
        {
            bool isSubscribed =
                await ServiceLocator.YouTube.IsSubscribedToChannelAsync(
                    Channel.Id);

            ApplySubscriptionButtonState(isSubscribed);
            return isSubscribed;
        }

        private void ApplySubscriptionButtonState(bool isSubscribed)
        {
            SubscribeButton.Content =
                isSubscribed
                    ? "Subscribed"
                    : "Subscribe";

            SubscribeButton.IsEnabled = !isSubscribed;
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                List<VideoItem> upNext =
                    allVideos
                        .Where(item => item.Id != video.Id)
                        .Take(24)
                        .ToList();

                upNext.Insert(0, video);

                Frame.Navigate(
                    typeof(VideoPage),
                    new VideoNavigationData
                    {
                        CurrentVideo = video,
                        AllVideos = upNext
                    });
            }
        }
    }
}
