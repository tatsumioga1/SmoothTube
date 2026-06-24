using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace SmoothTube
{
    public sealed partial class SubscriptionsPage : Page
    {
        private const string SubscriptionsCacheFileName = "subscriptions-cache.json";

        private sealed class SubscriptionsCache
        {
            public List<VideoItem> Uploads { get; set; } = [];
            public List<VideoItem> Broadcasts { get; set; } = [];
            public int UploadDays { get; set; }
            public bool BroadcastsLoaded { get; set; }
            public bool BroadcastQuotaExhausted { get; set; }
            public bool UploadsIncludeShorts { get; set; }
            public DateTimeOffset? UploadsRefreshedAt { get; set; }
            public DateTimeOffset? BroadcastsRefreshedAt { get; set; }
        }

        private static readonly List<VideoItem> CachedUploads = [];
        private static readonly List<VideoItem> CachedBroadcasts = [];
        private static int cachedUploadDays;
        private static bool cachedBroadcastsLoaded;
        private static bool cachedBroadcastQuotaExhausted;
        private static bool cachedUploadsIncludeShorts;
        private static DateTimeOffset? cachedUploadsRefreshedAt;
        private static DateTimeOffset? cachedBroadcastsRefreshedAt;
        private static bool triedPersistentSubscriptionsCache;

        public ObservableCollection<VideoItem> Videos { get; } = [];

        public ObservableCollection<VideoItem> PremiereVideos { get; } = [];

        public ObservableCollection<VideoItem> LivestreamVideos { get; } = [];

        private readonly List<VideoItem> loadedUploads = [];

        private readonly List<VideoItem> loadedBroadcasts = [];

        public string StatusText { get; set; } =
            "Sign in from Settings to load your YouTube subscriptions.";

        public Visibility LoadMoreVisibility { get; set; } = Visibility.Visible;

        private bool isLoaded;
        private bool isLoading;
        private bool broadcastsLoaded;
        private bool broadcastsLoading;
        private int loadedUploadDays = 30;
        private CancellationTokenSource? loadCancellation;

        private const int InitialUploadLimit = 24;
        private const int InitialUploadLookbackDays = 30;

        public SubscriptionsPage()
        {
            InitializeComponent();

            Loaded += SubscriptionsPage_Loaded;
        }

        private async void SubscriptionsPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (!ServiceLocator.GoogleOAuth.IsSignedIn)
            {
                StatusText =
                    "Sign in from Settings to load your YouTube subscriptions.";

                Bindings.Update();
                return;
            }

            isLoaded = true;

            EnsurePersistentSubscriptionsCacheLoaded();

            if (TryLoadFromPageCache())
            {
                ApplyVisibleFilters();
                return;
            }

            await LoadVideosAsync(false);
        }

        private async void Filters_Changed(
            object sender,
            RoutedEventArgs e)
        {
            if (!isLoaded)
                return;

            if (ReferenceEquals(sender, IncludeShortsSwitch))
            {
                if (IncludeShortsSwitch.IsOn &&
                    CachedUploads.Count > 0 &&
                    !cachedUploadsIncludeShorts)
                {
                    await LoadVideosAsync(true);
                    return;
                }

                ApplyVisibleFilters();
            }
        }

        private async void RefreshButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoading)
                return;

            await LoadVideosAsync(true);
        }

        private async void LoadMoreButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoading)
                return;

            loadedUploadDays++;

            await LoadUploadRangeAsync(
                loadedUploadDays,
                true,
                10,
                loadCancellation?.Token ?? CancellationToken.None);

            SaveUploadsToPageCache();
            ApplyVisibleFilters();
        }

        private async Task LoadVideosAsync(bool forceRefresh)
        {
            loadCancellation?.Cancel();
            loadCancellation = new CancellationTokenSource();
            CancellationToken cancellationToken = loadCancellation.Token;

            EnsurePersistentSubscriptionsCacheLoaded();

            if (forceRefresh)
            {
                // Refresh must be a clean time-based upload fetch.
                // Do not merge stale upload cache, because a bad cache can keep
                // older videos pinned above newer uploads.
                // Keep cached livestream/premiere results so a normal uploads refresh
                // does not force an expensive broadcast rescan.
                ServiceLocator.YouTube.ClearSubscribedVideoCache();
                ClearUploadsCache(clearPersistent: true);
            }
            else if (TryLoadFromPageCache())
            {
                ApplyVisibleFilters();
                return;
            }

            loadedUploadDays = InitialUploadLookbackDays;

            loadedUploads.Clear();
            loadedBroadcasts.Clear();

            Videos.Clear();
            PremiereVideos.Clear();
            LivestreamVideos.Clear();

            broadcastsLoaded = false;
            broadcastsLoading = false;

            StatusText = forceRefresh
                ? IncludeShortsSwitch.IsOn
                    ? "Refreshing latest subscription uploads..."
                    : "Refreshing latest long-form subscription uploads..."
                : IncludeShortsSwitch.IsOn
                    ? "Loading recent subscription uploads..."
                    : "Loading recent long-form subscription uploads...";

            Bindings.Update();

            try
            {
                await LoadUploadRangeAsync(
                    loadedUploadDays,
                    false,
                    InitialUploadLimit,
                    cancellationToken);

                SaveUploadsToPageCache();
                ApplyVisibleFilters();

                // Livestreams/premieres are intentionally not auto-loaded here.
                // They use the expensive broadcast scan and load only when those tabs are opened.
            }
            catch (TaskCanceledException)
            {
            }
            catch (Exception)
            {
                if (CachedUploads.Count > 0 || CachedBroadcasts.Count > 0)
                {
                    TryLoadFromPageCache();
                    ApplyVisibleFilters();
                    StatusText =
                        "Refresh failed. Showing cached subscription results. Try again in a moment.\n" +
                        FormatStatusText(false);
                }
                else
                {
                    StatusText =
                        "Could not load subscription uploads. Try Refresh in a moment.";
                }

                Bindings.Update();
            }
        }

        private async Task LoadUploadRangeAsync(
            int days,
            bool append,
            int? maxNewVideos,
            CancellationToken cancellationToken)
        {
            if (isLoading)
                return;

            isLoading = true;
            LoadMoreButton.IsEnabled = false;

            StatusText = append
                ? "Loading more recent uploads..."
                : IncludeShortsSwitch.IsOn
                    ? "Loading recent subscription uploads..."
                    : "Loading recent long-form subscription uploads...";

            Bindings.Update();

            try
            {
                int previousCount = loadedUploads.Count;

                await foreach (List<VideoItem> batch in
                    ServiceLocator.YouTube.GetSubscribedVideoBatchesAsync(
                        days,
                        IncludeShortsSwitch.IsOn,
                        cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    List<VideoItem> uploadVideos =
                        batch
                            .Where(video => !video.IsLive && !video.IsPremiere)
                            .Where(video => IncludeShortsSwitch.IsOn || !IsLikelyShort(video))
                            .Where(video =>
                                loadedUploads.All(existingVideo =>
                                    existingVideo.Id != video.Id))
                            .OrderByDescending(GetPublishedAtSort)
                            .ToList();

                    if (maxNewVideos != null)
                    {
                        uploadVideos =
                            uploadVideos
                                .Take(maxNewVideos.Value)
                                .ToList();
                    }

                    MergeVideos(loadedUploads, uploadVideos);
                    ApplyVisibleFilters();

                    break;
                }

                if (append && loadedUploads.Count <= previousCount)
                {
                    StatusText =
                        "No more uploads were returned right now.\n" +
                        FormatStatusText(false);

                    Bindings.Update();
                }
            }
            finally
            {
                isLoading = false;
                LoadMoreButton.IsEnabled = true;
            }
        }

        private async Task LoadBroadcastsAsync(CancellationToken cancellationToken)
        {
            if (broadcastsLoading || broadcastsLoaded)
                return;

            if (cachedBroadcastQuotaExhausted)
            {
                loadedBroadcasts.Clear();
                loadedBroadcasts.AddRange(CachedBroadcasts);
                ApplyVisibleFilters();

                StatusText = FormatBroadcastQuotaStatus();
                Bindings.Update();
                return;
            }

            if (CachedBroadcasts.Count > 0 || cachedBroadcastsLoaded)
            {
                loadedBroadcasts.Clear();
                loadedBroadcasts.AddRange(CachedBroadcasts);
                broadcastsLoaded = cachedBroadcastsLoaded;
                ApplyVisibleFilters();
                return;
            }

            broadcastsLoading = true;

            StatusText = "Checking live and upcoming subscriptions...";
            Bindings.Update();

            try
            {
                await foreach (List<VideoItem> batch in
                    ServiceLocator.YouTube.GetSubscribedBroadcastBatchesAsync(
                        cancellationToken: cancellationToken))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    MergeVideos(loadedBroadcasts, batch);
                    SaveBroadcastsToPageCache(false);

                    ApplyVisibleFilters(true);
                }

                if (ServiceLocator.YouTube.IsSearchQuotaExhausted)
                {
                    cachedBroadcastQuotaExhausted = true;
                    broadcastsLoaded = false;
                    SaveBroadcastsToPageCache(false);
                    ApplyVisibleFilters();

                    StatusText = FormatBroadcastQuotaStatus();
                    Bindings.Update();
                    return;
                }

                broadcastsLoaded = true;
                cachedBroadcastQuotaExhausted = false;
                SaveBroadcastsToPageCache(true);
                ApplyVisibleFilters();
            }
            catch (TaskCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is System.Runtime.InteropServices.COMException)
            {
                StatusText =
                    "Live and premiere checks failed. Try Refresh in a moment.\n" +
                    FormatStatusText(false);

                Bindings.Update();
            }
            finally
            {
                broadcastsLoading = false;
            }
        }

        private async void SubscriptionsPivot_SelectionChanged(
            object sender,
            SelectionChangedEventArgs e)
        {
            if (!isLoaded || SubscriptionsPivot.SelectedIndex == 0)
                return;

            await LoadBroadcastsAsync(
                loadCancellation?.Token ?? CancellationToken.None);
        }

        private void VideoCard_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border card &&
                card.DataContext is VideoItem video)
            {
                OpenVideo(video);
            }
        }

        private void ApplyVisibleFilters(bool stillLoading = false)
        {
            List<VideoItem> visibleVideos =
                loadedUploads
                    .Concat(loadedBroadcasts)
                    .Where(video => IncludeShortsSwitch.IsOn || !IsLikelyShort(video))
                    .ToList();

            Videos.Clear();
            PremiereVideos.Clear();
            LivestreamVideos.Clear();

            foreach (VideoItem video in visibleVideos
                .Where(video => !video.IsLive && !video.IsPremiere)
                .OrderByDescending(GetPublishedAtSort))
            {
                Videos.Add(video);
            }

            foreach (VideoItem video in visibleVideos
                .Where(video => video.IsPremiere)
                .OrderByDescending(GetPublishedAtSort))
            {
                PremiereVideos.Add(video);
            }

            foreach (VideoItem video in visibleVideos
                .Where(video => video.IsLive)
                .OrderByDescending(GetPublishedAtSort))
            {
                LivestreamVideos.Add(video);
            }

            StatusText =
                Videos.Count == 0 && PremiereVideos.Count == 0 && LivestreamVideos.Count == 0
                    ? stillLoading
                        ? "Loading subscriptions..."
                        : "No recent videos loaded for these filters.\n" + FormatStatusText(false)
                    : FormatStatusText(stillLoading);

            Bindings.Update();
        }

        private bool TryLoadFromPageCache()
        {
            if (CachedUploads.Count == 0 && CachedBroadcasts.Count == 0)
                return false;

            if (IncludeShortsSwitch.IsOn && CachedUploads.Count > 0 && !cachedUploadsIncludeShorts)
                return false;

            loadedUploads.Clear();
            loadedUploads.AddRange(CachedUploads);

            loadedBroadcasts.Clear();
            loadedBroadcasts.AddRange(CachedBroadcasts);

            loadedUploadDays = Math.Max(InitialUploadLookbackDays, cachedUploadDays);
            broadcastsLoaded = cachedBroadcastsLoaded;

            return true;
        }

        private static void EnsurePersistentSubscriptionsCacheLoaded()
        {
            if (triedPersistentSubscriptionsCache)
                return;

            triedPersistentSubscriptionsCache = true;

            SubscriptionsCache? cache =
                PersistentCacheService.Load<SubscriptionsCache>(
                    SubscriptionsCacheFileName);

            if (cache == null)
                return;

            CachedUploads.Clear();
            CachedUploads.AddRange(cache.Uploads ?? []);

            CachedBroadcasts.Clear();
            CachedBroadcasts.AddRange(cache.Broadcasts ?? []);

            cachedUploadDays = cache.UploadDays;
            cachedBroadcastsLoaded = cache.BroadcastsLoaded;
            cachedBroadcastQuotaExhausted = cache.BroadcastQuotaExhausted;
            cachedUploadsIncludeShorts = cache.UploadsIncludeShorts;
            cachedUploadsRefreshedAt = cache.UploadsRefreshedAt;
            cachedBroadcastsRefreshedAt = cache.BroadcastsRefreshedAt;
        }

        private static void SavePersistentSubscriptionsCache()
        {
            if (CachedUploads.Count == 0 && CachedBroadcasts.Count == 0)
                return;

            PersistentCacheService.Save(
                SubscriptionsCacheFileName,
                new SubscriptionsCache
                {
                    Uploads = CachedUploads.ToList(),
                    Broadcasts = CachedBroadcasts.ToList(),
                    UploadDays = cachedUploadDays,
                    BroadcastsLoaded = cachedBroadcastsLoaded,
                    BroadcastQuotaExhausted = cachedBroadcastQuotaExhausted,
                    UploadsIncludeShorts = cachedUploadsIncludeShorts,
                    UploadsRefreshedAt = cachedUploadsRefreshedAt,
                    BroadcastsRefreshedAt = cachedBroadcastsRefreshedAt
                });
        }

        private void SaveUploadsToPageCache()
        {
            CachedUploads.Clear();
            CachedUploads.AddRange(loadedUploads);
            cachedUploadDays = loadedUploadDays;
            cachedUploadsIncludeShorts = IncludeShortsSwitch.IsOn;
            cachedUploadsRefreshedAt = DateTimeOffset.Now;
            SavePersistentSubscriptionsCache();
        }

        private void SaveBroadcastsToPageCache(bool completed)
        {
            CachedBroadcasts.Clear();
            CachedBroadcasts.AddRange(loadedBroadcasts);
            cachedBroadcastsLoaded = completed;
            cachedBroadcastsRefreshedAt = DateTimeOffset.Now;
            SavePersistentSubscriptionsCache();
        }

        private static void ClearUploadsCache(bool clearPersistent = false)
        {
            CachedUploads.Clear();
            cachedUploadDays = 0;
            cachedUploadsIncludeShorts = false;
            cachedUploadsRefreshedAt = null;

            if (clearPersistent)
            {
                if (CachedBroadcasts.Count > 0)
                {
                    SavePersistentSubscriptionsCache();
                }
                else
                {
                    PersistentCacheService.Clear(SubscriptionsCacheFileName);
                }
            }
        }

        private static void ClearPageCache(bool clearPersistent = false)
        {
            CachedUploads.Clear();
            CachedBroadcasts.Clear();
            cachedUploadDays = 0;
            cachedBroadcastsLoaded = false;
            cachedBroadcastQuotaExhausted = false;
            cachedUploadsIncludeShorts = false;
            cachedUploadsRefreshedAt = null;
            cachedBroadcastsRefreshedAt = null;

            if (clearPersistent)
            {
                PersistentCacheService.Clear(SubscriptionsCacheFileName);
            }
        }

        private static bool IsLikelyShort(VideoItem video)
        {
            if (video.IsShort)
                return true;

            string title = video.Title ?? "";

            bool titleLooksShort =
                title.Contains("#short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" shorts", StringComparison.OrdinalIgnoreCase) ||
                title.Contains(" short ", StringComparison.OrdinalIgnoreCase) ||
                title.EndsWith(" short", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("ytshorts", StringComparison.OrdinalIgnoreCase);

            if (string.IsNullOrWhiteSpace(video.Duration))
                return titleLooksShort;

            string[] parts = video.Duration.Split(':');

            if (parts.Length == 1 &&
                int.TryParse(parts[0], out int secondsOnly))
            {
                return secondsOnly <= 180 || titleLooksShort;
            }

            if (parts.Length == 2 &&
                int.TryParse(parts[0], out int minutes) &&
                int.TryParse(parts[1], out int seconds))
            {
                int totalSeconds = minutes * 60 + seconds;

                return titleLooksShort || totalSeconds <= 180;
            }

            return titleLooksShort;
        }

        private string FormatBroadcastQuotaStatus()
        {
            string cacheText =
                CachedBroadcasts.Count > 0
                    ? " Showing cached live/premiere results if available."
                    : " No cached live/premiere results are available yet.";

            return
                "Live and premiere results may be partial because the YouTube Search API quota has been reached." +
                cacheText +
                " Try again after the quota resets.\n" +
                FormatStatusText(false);
        }

        private string FormatStatusText(bool isLoading)
        {
            string loadingSuffix = isLoading ? "..." : "";

            string text =
                $"Currently loaded: {Videos.Count} recent uploads • {PremiereVideos.Count} premieres • {LivestreamVideos.Count} livestreams{loadingSuffix}\n" +
                "Load more to fetch additional content.";

            if (!IncludeShortsSwitch.IsOn)
            {
                text += " Shorts are hidden. Turn on Shorts to include them.";
            }

            if (cachedUploadsRefreshedAt.HasValue)
            {
                text += $" Last refreshed {cachedUploadsRefreshedAt.Value.LocalDateTime:g}.";
            }

            return text;
        }

        private static void MergeVideos(
            List<VideoItem> target,
            IEnumerable<VideoItem> videos)
        {
            foreach (VideoItem video in videos)
            {
                if (!target.Any(existingVideo => existingVideo.Id == video.Id))
                {
                    target.Add(video);
                }
            }
        }

        private static DateTime ParsePublishedAt(string value)
        {
            return DateTime.TryParse(
                value,
                CultureInfo.CurrentCulture,
                DateTimeStyles.AssumeLocal,
                out DateTime result)
                    ? result
                    : DateTime.MinValue;
        }

        private static DateTime GetPublishedAtSort(VideoItem video)
        {
            return video.PublishedAtSort?.LocalDateTime ??
                ParsePublishedAt(video.PublishedAt);
        }

        private void VideosGrid_ItemClick(
            object sender,
            ItemClickEventArgs e)
        {
            if (e.ClickedItem is VideoItem video)
            {
                OpenVideo(video);
            }
        }

        private void OpenVideo(VideoItem video)
        {
            List<VideoItem> upNext =
                loadedUploads
                    .Concat(loadedBroadcasts)
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
