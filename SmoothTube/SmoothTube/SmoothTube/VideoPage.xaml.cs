using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.Web.WebView2.Core;
using SmoothTube.Models;
using SmoothTube.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;

namespace SmoothTube
{
    public sealed partial class VideoPage : Page
    {
        public VideoItem CurrentVideo { get; set; } = new();

        public List<VideoItem> RecommendedVideos { get; set; } = [];

        public List<CommentItem> Comments { get; set; } = [];

        public List<LiveChatMessageItem> LiveChatMessages { get; set; } = [];

        public string CommentsStatus { get; set; } = "Comments load for YouTube videos when an API key is saved.";

        public string LiveChatStatus { get; set; } = "Live chat appears for active livestreams when YouTube exposes a live chat.";

        private List<VideoItem> AllVideos { get; set; } = [];

        private bool playerHostMapped;
        private bool isPlayerFullScreen;
        private bool playerEventsAttached;
        private bool descriptionExpanded;
        private DispatcherTimer? progressTimer;
        private bool isStoppingPlayback;
        private DateTimeOffset? playbackStartedAt;
        private double playbackStartedResumeSeconds;

        public VideoPage()
        {
            InitializeComponent();

            PlayerWebView.NavigationCompleted += PlayerWebView_NavigationCompleted;
            Unloaded += VideoPage_Unloaded;

            Loaded += VideoPage_Loaded;
            SizeChanged += VideoPage_SizeChanged;
        }

        protected override void OnNavigatedTo(
            NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);

            if (e.Parameter is VideoNavigationData data)
            {
                CurrentVideo = data.CurrentVideo;
                EnsureCurrentVideoDefaults();

                AllVideos = data.AllVideos;

                RecommendedVideos =
                    AllVideos
                        .Where(v => v != CurrentVideo)
                        .ToList();

                DataContext = CurrentVideo;
                ApplyLiveChatLayout();
                UpdateChannelButtonVisibility();
                _ = UpdateSubscriptionButtonAsync();
                _ = UpdateRatingStateAsync();
                WatchHistoryService.ApplySavedProgress(CurrentVideo);
                WatchHistoryService.RecordStarted(CurrentVideo);
                RefreshCurrentVideoBindings();
                _ = EnrichCurrentVideoAsync();
                _ = UpdatePlayerSourceAsync();
                _ = LoadSocialDataAsync();
            }
            else
            {
                AllVideos = VideoCatalog.GetAll();
                CurrentVideo = AllVideos.FirstOrDefault() ?? new VideoItem();
                EnsureCurrentVideoDefaults();
                RecommendedVideos = AllVideos.Skip(1).ToList();
                DataContext = CurrentVideo;
                ApplyLiveChatLayout();
                UpdateChannelButtonVisibility();
                _ = UpdateSubscriptionButtonAsync();
                _ = UpdateRatingStateAsync();
                WatchHistoryService.ApplySavedProgress(CurrentVideo);
                WatchHistoryService.RecordStarted(CurrentVideo);
                RefreshCurrentVideoBindings();
                _ = EnrichCurrentVideoAsync();
                _ = UpdatePlayerSourceAsync();
                _ = LoadSocialDataAsync();
            }
        }

        private void EnsureCurrentVideoDefaults()
        {
            if (CurrentVideo == null)
            {
                CurrentVideo = new VideoItem();
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.Category) &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CurrentVideo.Category = "YouTube";
            }

            if (CurrentVideo.Category == "YouTube" &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CurrentVideo.IsEmbeddable = true;
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.Title) &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CurrentVideo.Title = "YouTube video";
            }
        }

        private void RefreshCurrentVideoBindings()
        {
            DataContext = null;
            DataContext = CurrentVideo;
            ApplyLiveChatLayout();
            UpdateChannelButtonVisibility();
            Bindings.Update();
        }

        private void UpNextVideo_Tapped(
            object sender,
            TappedRoutedEventArgs e)
        {
            if (sender is Border border &&
                border.DataContext is VideoItem video)
            {
                NavigateToVideo(video);
            }
        }

        private async void BackButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            // Save a synchronous fallback first so Home can immediately read the new progress
            // even if WebView2 refuses a final JavaScript snapshot while navigating away.
            RecordApproximatePlaybackProgress(forceVisibleProgress: true);
            await RequestProgressSnapshotAsync();
            StopPlayback();

            if (Frame.CanGoBack)
            {
                Frame.GoBack();
            }
        }

        private async void ChannelButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.ChannelId) &&
                !await TryResolveCurrentChannelAsync())
            {
                return;
            }

            StopPlayback();

            Frame.Navigate(
                typeof(ChannelPage),
                new ChannelItem
                {
                    Id = CurrentVideo.ChannelId,
                    Title = CurrentVideo.Channel,
                    Thumbnail = ""
                });
        }

        private void UpdateChannelButtonVisibility()
        {
            if (ChannelButton == null)
                return;

            ChannelButton.IsEnabled =
                !string.IsNullOrWhiteSpace(CurrentVideo.Channel) ||
                !string.IsNullOrWhiteSpace(CurrentVideo.ChannelId);

            if (LikeButton != null &&
                string.IsNullOrWhiteSpace(CurrentVideo.Likes))
            {
                ToolTipService.SetToolTip(
                    LikeButton,
                    "Rate this video as liked on your signed-in YouTube account");
            }
            else if (LikeButton != null)
            {
                ToolTipService.SetToolTip(
                    LikeButton,
                    $"{CurrentVideo.Likes}. Rate this video as liked on your signed-in YouTube account");
            }

            if (DescriptionPanel != null)
            {
                DescriptionPanel.Visibility =
                    string.IsNullOrWhiteSpace(CurrentVideo.Description)
                        ? Visibility.Collapsed
                        : Visibility.Visible;
            }
        }

        protected override void OnNavigatingFrom(
            NavigatingCancelEventArgs e)
        {
            StopPlayback();
            base.OnNavigatingFrom(e);
        }

        private void VideoPage_Unloaded(
            object sender,
            RoutedEventArgs e)
        {
            StopPlayback();
        }

        private void VideoPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (XamlRoot != null)
            {
                XamlRoot.Changed += XamlRoot_Changed;

                UpdateLayoutForWidth();
            }
        }

        private void XamlRoot_Changed(
            XamlRoot sender,
            XamlRootChangedEventArgs args)
        {
            UpdateLayoutForWidth();
        }

        private void VideoPage_SizeChanged(
            object sender,
            SizeChangedEventArgs e)
        {
            UpdatePlayerSize();
        }

        private void UpdateLayoutForWidth()
        {
            if (isPlayerFullScreen)
                return;

            double width = XamlRoot.Size.Width;

            if (width < 1300)
            {
                Grid.SetColumn(UpNextPanel, 0);
                Grid.SetRow(UpNextPanel, 1);

                UpNextPanel.Width =
                    double.NaN;

                UpNextHeaderGrid.Width =
                    double.NaN;

                LiveChatHeaderGrid.Width =
                    double.NaN;

                UpNextPanel.Margin =
                    new Thickness(0, 24, 0, 0);

                SidebarColumn.Width =
                    new GridLength(0);
            }
            else
            {
                Grid.SetColumn(UpNextPanel, 1);
                Grid.SetRow(UpNextPanel, 0);

                UpNextPanel.Margin =
                    new Thickness(0);

                SidebarColumn.Width =
                    new GridLength(340);

                UpNextPanel.Width =
                    SidebarColumn.Width.Value;

                UpNextHeaderGrid.Width =
                    SidebarColumn.Width.Value;

                LiveChatHeaderGrid.Width =
                    SidebarColumn.Width.Value;
            }

            UpdatePlayerSize();
        }

        private void ApplyLiveChatLayout()
        {
            if (LiveChatPanel == null ||
                MainContentPanel == null)
            {
                return;
            }

            LiveChatPanel.Visibility =
                CurrentVideo.IsLive
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            UpdateLiveChatStatusVisibility();
        }

        private void UpdatePlayerSize()
        {
            if (PlayerBorder == null)
                return;

            if (isPlayerFullScreen && XamlRoot != null)
            {
                PlayerBorder.Width = XamlRoot.Size.Width;
                PlayerBorder.Height = XamlRoot.Size.Height;
                PlayerWebView.Width = XamlRoot.Size.Width;
                PlayerWebView.Height = XamlRoot.Size.Height;
                return;
            }

            double availableWidth =
                MainLayoutGrid.ActualWidth;

            if (availableWidth <= 0)
                return;

            double playerWidth =
                SidebarColumn.Width.Value > 0
                    ? availableWidth - 364
                    : availableWidth;

            PlayerBorder.Height =
                playerWidth * 9 / 16;

            PlayerWebView.Width = double.NaN;
            PlayerWebView.Height = double.NaN;
        }

        private async Task UpdatePlayerSourceAsync()
        {
            bool canEmbedYouTube =
                CurrentVideo.Category == "YouTube" &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id) &&
                CurrentVideo.IsEmbeddable;

            bool canOpenYouTube =
                CurrentVideo.Category == "YouTube" &&
                !string.IsNullOrWhiteSpace(CurrentVideo.Id);

            OpenOnYouTubeButton.Visibility =
                canOpenYouTube
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PlayerWebView.Visibility =
                canEmbedYouTube
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            BlockedPlayerOverlay.Visibility =
                canOpenYouTube && !canEmbedYouTube
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            MockPlayerGrid.Visibility =
                canEmbedYouTube || canOpenYouTube
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            MockControlsPanel.Visibility =
                canEmbedYouTube
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            PlayerBorder.CornerRadius =
                canEmbedYouTube
                    ? new CornerRadius(16)
                    : new CornerRadius(16, 16, 0, 0);

            if (canEmbedYouTube)
            {
                string videoId =
                    Uri.EscapeDataString(CurrentVideo.Id);

                await PlayerWebView.EnsureCoreWebView2Async();
                EnsurePlayerHostMapped();
                EnsurePlayerEventsAttached();

                PlayerWebView.CoreWebView2.ContainsFullScreenElementChanged -=
                    CoreWebView2_ContainsFullScreenElementChanged;

                PlayerWebView.CoreWebView2.ContainsFullScreenElementChanged +=
                    CoreWebView2_ContainsFullScreenElementChanged;

                double resumeSeconds =
                    CurrentVideo.IsLive || CurrentVideo.IsPremiere
                        ? 0
                        : Math.Max(
                            CurrentVideo.ResumeSeconds,
                            WatchHistoryService.GetResumeSeconds(CurrentVideo.Id));

                string startParameter =
                    resumeSeconds >= 5
                        ? $"&startSeconds={Math.Floor(resumeSeconds).ToString(CultureInfo.InvariantCulture)}"
                        : "";

                playbackStartedAt = DateTimeOffset.Now;
                playbackStartedResumeSeconds = resumeSeconds;

                PlayerWebView.CoreWebView2.Navigate(
                    $"https://smoothtube.local/youtube-player.html?videoId={videoId}{startParameter}");
            }
        }

        private void OpenOnYouTube_Click(
            object sender,
            RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.Id))
                return;

            var window =
                new YouTubeWatchWindow(CurrentVideo.Id);

            window.Activate();
        }

        private void EnsurePlayerHostMapped()
        {
            if (playerHostMapped)
                return;

            string rootPlayerPath =
                Path.Combine(AppContext.BaseDirectory, "youtube-player.html");

            string assetsPath =
                Path.Combine(AppContext.BaseDirectory, "Assets");

            string mappedPath =
                File.Exists(rootPlayerPath)
                    ? AppContext.BaseDirectory
                    : assetsPath;

            System.Diagnostics.Debug.WriteLine(
                $"SmoothTube player host mapped to: {mappedPath}");

            PlayerWebView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "smoothtube.local",
                mappedPath,
                CoreWebView2HostResourceAccessKind.Allow);

            playerHostMapped = true;
        }

        private void EnsurePlayerEventsAttached()
        {
            if (playerEventsAttached ||
                PlayerWebView.CoreWebView2 == null)
            {
                return;
            }

            PlayerWebView.CoreWebView2.NewWindowRequested +=
                CoreWebView2_NewWindowRequested;

            PlayerWebView.CoreWebView2.NavigationStarting +=
                CoreWebView2_NavigationStarting;

            PlayerWebView.CoreWebView2.WebMessageReceived +=
                CoreWebView2_WebMessageReceived;

            playerEventsAttached = true;
        }

        private void CoreWebView2_WebMessageReceived(
            CoreWebView2 sender,
            CoreWebView2WebMessageReceivedEventArgs args)
        {
            string message;

            try
            {
                message = args.TryGetWebMessageAsString();
            }
            catch (ArgumentException)
            {
                message = args.WebMessageAsJson;
            }

            if (message == "ended")
            {
                WatchHistoryService.RecordCompleted(CurrentVideo);

                if (AutoPlayUpNextSwitch.IsOn &&
                    RecommendedVideos.Count > 0)
                {
                    NavigateToVideo(RecommendedVideos[0]);
                }

                return;
            }

            HandlePlayerProgressMessage(message);
        }

        private void HandlePlayerProgressMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            try
            {
                using JsonDocument document =
                    JsonDocument.Parse(message);

                JsonElement root = document.RootElement;

                if (root.ValueKind == JsonValueKind.String)
                {
                    string nested = root.GetString() ?? "";

                    if (string.IsNullOrWhiteSpace(nested) ||
                        nested == message)
                    {
                        return;
                    }

                    HandlePlayerProgressMessage(nested);
                    return;
                }

                if (root.ValueKind != JsonValueKind.Object)
                {
                    return;
                }

                string type =
                    root.TryGetProperty("type", out JsonElement typeElement)
                        ? typeElement.GetString() ?? ""
                        : "";

                if (type.Equals("ended", StringComparison.OrdinalIgnoreCase))
                {
                    WatchHistoryService.RecordCompleted(CurrentVideo);
                    return;
                }

                double currentSeconds =
                    GetJsonDouble(root, "currentTime");

                double durationSeconds =
                    GetJsonDouble(root, "duration");

                if (currentSeconds <= 0 ||
                    CurrentVideo.IsLive ||
                    CurrentVideo.IsPremiere)
                {
                    return;
                }

                System.Diagnostics.Debug.WriteLine(
                    $"SmoothTube progress message | Video: {CurrentVideo.Id} | Current: {currentSeconds} | Duration: {durationSeconds} | Type: {type}");

                WatchHistoryService.RecordProgress(
                    CurrentVideo,
                    currentSeconds,
                    durationSeconds);
            }
            catch (JsonException)
            {
            }
        }

        private static double GetJsonDouble(
            JsonElement root,
            string propertyName)
        {
            if (!root.TryGetProperty(propertyName, out JsonElement element))
            {
                return 0;
            }

            return element.ValueKind switch
            {
                JsonValueKind.Number => element.GetDouble(),
                JsonValueKind.String when double.TryParse(
                    element.GetString(),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double value) => value,
                _ => 0
            };
        }

        private void CoreWebView2_NewWindowRequested(
            CoreWebView2 sender,
            CoreWebView2NewWindowRequestedEventArgs args)
        {
            if (TryHandlePlayerUri(args.Uri))
            {
                args.Handled = true;
            }
        }

        private void CoreWebView2_NavigationStarting(
            CoreWebView2 sender,
            CoreWebView2NavigationStartingEventArgs args)
        {
            if (args.Uri.StartsWith(
                    "https://smoothtube.local/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (TryHandlePlayerUri(args.Uri))
            {
                args.Cancel = true;
            }
        }

        private bool TryHandlePlayerUri(string uri)
        {
            if (!Uri.TryCreate(uri, UriKind.Absolute, out Uri? parsedUri))
                return false;

            string videoId =
                ExtractVideoId(parsedUri);

            if (!string.IsNullOrWhiteSpace(videoId) &&
                videoId != CurrentVideo.Id)
            {
                StopPlayback();

                VideoItem nextVideo =
                    RecommendedVideos.FirstOrDefault(video => video.Id == videoId) ??
                    new VideoItem
                    {
                        Id = videoId,
                        Title = "Loading...",
                        Category = "YouTube",
                        IsEmbeddable = true
                    };

                NavigateToVideo(nextVideo);

                return true;
            }

            string channelId =
                ExtractChannelId(parsedUri);

            if (!string.IsNullOrWhiteSpace(channelId))
            {
                StopPlayback();

                Frame.Navigate(
                    typeof(ChannelPage),
                    new ChannelItem
                    {
                        Id = channelId,
                        Title = ""
                    });

                return true;
            }

            return parsedUri.Host.Contains(
                "youtube.com",
                StringComparison.OrdinalIgnoreCase);
        }

        private void CoreWebView2_ContainsFullScreenElementChanged(
            CoreWebView2 sender,
            object args)
        {
            ApplyPlayerFullScreen(sender.ContainsFullScreenElement);
        }

        private void ApplyPlayerFullScreen(bool isFullScreen)
        {
            isPlayerFullScreen = isFullScreen;
            MainWindow.Instance?.SetFullScreen(isFullScreen);

            VideoActionsPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            UpNextPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            VideoInfoPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            DescriptionPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            CommentsPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            LiveChatPanel.Visibility =
                isFullScreen
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            PageScrollViewer.VerticalScrollBarVisibility =
                isFullScreen
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Auto;

            PageScrollViewer.HorizontalScrollBarVisibility =
                isFullScreen
                    ? ScrollBarVisibility.Disabled
                    : ScrollBarVisibility.Disabled;

            MainLayoutGrid.Padding =
                isFullScreen
                    ? new Thickness(0)
                    : new Thickness(40);

            MainLayoutGrid.ColumnSpacing =
                isFullScreen
                    ? 0
                    : 24;

            MainContentPanel.Spacing =
                isFullScreen
                    ? 0
                    : 24;

            SidebarColumn.Width =
                isFullScreen
                    ? new GridLength(0)
                    : new GridLength(340);

            PlayerBorder.CornerRadius =
                isFullScreen
                    ? new CornerRadius(0)
                    : new CornerRadius(16);

            if (isFullScreen && XamlRoot != null)
            {
                Grid.SetColumn(MainContentPanel, 0);
                Grid.SetColumnSpan(MainContentPanel, 2);
                Grid.SetRowSpan(MainContentPanel, 2);
                MainContentPanel.Width = XamlRoot.Size.Width;
                MainContentPanel.Height = XamlRoot.Size.Height;
                MainLayoutGrid.Width = XamlRoot.Size.Width;
                MainLayoutGrid.Height = XamlRoot.Size.Height;
                PageScrollViewer.Width = XamlRoot.Size.Width;
                PageScrollViewer.Height = XamlRoot.Size.Height;
                PlayerBorder.Width = XamlRoot.Size.Width;
                PlayerBorder.Height = XamlRoot.Size.Height;
                PlayerWebView.Width = XamlRoot.Size.Width;
                PlayerWebView.Height = XamlRoot.Size.Height;
            }
            else
            {
                Grid.SetColumnSpan(MainContentPanel, 1);
                Grid.SetRowSpan(MainContentPanel, 1);
                MainContentPanel.Width = double.NaN;
                MainContentPanel.Height = double.NaN;
                MainLayoutGrid.Width = double.NaN;
                MainLayoutGrid.Height = double.NaN;
                PageScrollViewer.Width = double.NaN;
                PageScrollViewer.Height = double.NaN;
                PlayerBorder.Width = double.NaN;
                PlayerWebView.Width = double.NaN;
                PlayerWebView.Height = double.NaN;
                UpdateLayoutForWidth();
            }
        }

        private void StopPlayback()
        {
            if (isStoppingPlayback)
            {
                return;
            }

            isStoppingPlayback = true;

            try
            {
                progressTimer?.Stop();
                progressTimer = null;
                RecordApproximatePlaybackProgress();
                _ = RequestProgressSnapshotAsync();

                if (PlayerWebView.CoreWebView2 != null)
                {
                    _ = PlayerWebView.ExecuteScriptAsync(
                        "if (window.__smoothTubeReportProgress) { window.__smoothTubeReportProgress(); } document.querySelector('video')?.pause();");

                    PlayerWebView.CoreWebView2.Navigate("about:blank");
                }
                else
                {
                    PlayerWebView.Source = new Uri("about:blank");
                }
            }
            catch (InvalidOperationException)
            {
            }
            finally
            {
                isStoppingPlayback = false;
            }

            ApplyPlayerFullScreen(false);
        }

        private async void PlayerWebView_NavigationCompleted(
            WebView2 sender,
            CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!args.IsSuccess ||
                PlayerWebView.CoreWebView2 == null ||
                PlayerWebView.Source?.ToString().Contains(
                    "smoothtube.local/youtube-player.html",
                    StringComparison.OrdinalIgnoreCase) != true)
            {
                return;
            }

            await InstallPlayerProgressBridgeAsync();
            StartProgressTimer();
        }

        private async Task InstallPlayerProgressBridgeAsync()
        {
            if (PlayerWebView.CoreWebView2 == null ||
                CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere)
            {
                return;
            }

            double resumeSeconds =
                Math.Max(
                    CurrentVideo.ResumeSeconds,
                    WatchHistoryService.GetResumeSeconds(CurrentVideo.Id));

            string resumeText =
                resumeSeconds.ToString(CultureInfo.InvariantCulture);

            string script =
                $@"(() => {{
                    const resumeSeconds = {resumeText};

                    function getPlayer() {{
                        return window.player || window.ytPlayer || window.youtubePlayer || null;
                    }}

                    function reportProgress(type) {{
                        try {{
                            const player = getPlayer();

                            if (!player || typeof player.getCurrentTime !== 'function') {{
                                return;
                            }}

                            const currentTime = Number(player.getCurrentTime() || 0);
                            const duration =
                                typeof player.getDuration === 'function'
                                    ? Number(player.getDuration() || 0)
                                    : 0;

                            if (window.chrome && window.chrome.webview) {{
                                window.chrome.webview.postMessage(JSON.stringify({{
                                    type: type || 'progress',
                                    currentTime,
                                    duration
                                }}));
                            }}
                        }} catch (error) {{
                        }}
                    }}

                    function applyResume(seconds) {{
                        try {{
                            if (!seconds || seconds < 5 || window.__smoothTubeResumeApplied) {{
                                return false;
                            }}

                            const player = getPlayer();

                            if (!player || typeof player.seekTo !== 'function') {{
                                return false;
                            }}

                            player.seekTo(seconds, true);
                            window.__smoothTubeResumeApplied = true;
                            reportProgress('resumed');
                            return true;
                        }} catch (error) {{
                            return false;
                        }}
                    }}

                    window.__smoothTubeReportProgress = () => reportProgress('progress');
                    window.__smoothTubeApplyResume = applyResume;

                    if (!window.__smoothTubeProgressBridgeInstalled) {{
                        window.__smoothTubeProgressBridgeInstalled = true;

                        window.addEventListener('beforeunload', () => {{
                            reportProgress('progress');
                        }});

                        setInterval(() => {{
                            reportProgress('progress');
                        }}, 5000);
                    }}

                    const resumeInterval = setInterval(() => {{
                        if (applyResume(resumeSeconds)) {{
                            clearInterval(resumeInterval);
                        }}
                    }}, 500);

                    setTimeout(() => {{
                        clearInterval(resumeInterval);
                    }}, 15000);
                }})();";

            try
            {
                await PlayerWebView.ExecuteScriptAsync(script);
            }
            catch (InvalidOperationException)
            {
            }
            catch (COMException)
            {
            }
        }

        private void StartProgressTimer()
        {
            progressTimer?.Stop();

            if (CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere)
            {
                return;
            }

            progressTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(5)
            };

            progressTimer.Tick += async (_, _) =>
            {
                await RequestProgressSnapshotAsync();
                RecordApproximatePlaybackProgress();
            };

            progressTimer.Start();
        }

        private void RecordApproximatePlaybackProgress(bool forceVisibleProgress = false)
        {
            if (CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere ||
                playbackStartedAt == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            double elapsedSeconds =
                Math.Max(0, (DateTimeOffset.Now - playbackStartedAt.Value).TotalSeconds);

            double currentSeconds =
                Math.Max(0, playbackStartedResumeSeconds + elapsedSeconds);

            if (currentSeconds < 5 && !forceVisibleProgress)
            {
                return;
            }

            if (forceVisibleProgress && currentSeconds < 5)
            {
                currentSeconds = 5;
            }

            double durationSeconds =
                CurrentVideo.DurationSeconds > 0
                    ? CurrentVideo.DurationSeconds
                    : ParseDurationSeconds(CurrentVideo.Duration);

            System.Diagnostics.Debug.WriteLine(
                $"SmoothTube approximate progress | Video: {CurrentVideo.Id} | Current: {currentSeconds} | Duration: {durationSeconds}");

            WatchHistoryService.RecordProgress(
                CurrentVideo,
                currentSeconds,
                durationSeconds);
        }

        private static double ParseDurationSeconds(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return 0;
            }

            string[] parts =
                value
                    .Trim()
                    .Split(':', StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0 || parts.Length > 3)
            {
                return 0;
            }

            double totalSeconds = 0;

            foreach (string part in parts)
            {
                if (!int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
                {
                    return 0;
                }

                totalSeconds = totalSeconds * 60 + parsed;
            }

            return totalSeconds;
        }

        private async Task RequestProgressSnapshotAsync()
        {
            if (PlayerWebView?.CoreWebView2 == null ||
                CurrentVideo.IsLive ||
                CurrentVideo.IsPremiere)
            {
                return;
            }

            try
            {
                string result = await PlayerWebView.ExecuteScriptAsync(
                    "JSON.stringify(window.__smoothTubeGetProgress ? window.__smoothTubeGetProgress('snapshot') : null)");

                if (!string.IsNullOrWhiteSpace(result) &&
                    result != "null")
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"SmoothTube snapshot result for {CurrentVideo.Id}: {result}");

                    HandlePlayerProgressMessage(result);
                    return;
                }

                await PlayerWebView.ExecuteScriptAsync(
                    "if (window.__smoothTubeReportProgress) { window.__smoothTubeReportProgress(); }");
            }
            catch (InvalidOperationException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SmoothTube snapshot skipped: {ex.Message}");
            }
            catch (COMException ex)
            {
                System.Diagnostics.Debug.WriteLine($"SmoothTube snapshot failed: {ex.Message}");
            }
        }


        private void MockThumbnailImage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Image image)
            {
                return;
            }

            string thumbnail = "";

            if (image.DataContext is VideoItem video)
            {
                thumbnail = video.Thumbnail;
            }

            if (string.IsNullOrWhiteSpace(thumbnail))
            {
                thumbnail = CurrentVideo?.Thumbnail ?? "";
            }

            if (thumbnail.StartsWith("//", StringComparison.Ordinal))
            {
                thumbnail = "https:" + thumbnail;
            }

            if (Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uri) &&
                (uri.Scheme == "https" ||
                 uri.Scheme == "http" ||
                 uri.Scheme == "ms-appx" ||
                 uri.Scheme == "file"))
            {
                image.Source = new BitmapImage(uri);
            }
        }

        private async void RefreshLiveChat_Click(
            object sender,
            RoutedEventArgs e)
        {
            await LoadLiveChatAsync();
        }

        private void DescriptionToggleButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            descriptionExpanded = !descriptionExpanded;

            DescriptionTextBlock.MaxLines =
                descriptionExpanded
                    ? 0
                    : 3;

            DescriptionToggleButton.Content =
                descriptionExpanded
                    ? "Show less"
                    : "Show more";
        }

        private async void LikeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool success =
                await ServiceLocator.YouTube.RateVideoAsync(
                    CurrentVideo.Id,
                    "like");

            ToolTipService.SetToolTip(
                LikeButton,
                success ? "Liked" : "Sign in again to like this video");

            if (success)
            {
                ApplyRatingState("like");
            }
        }

        private async void DislikeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            bool success =
                await ServiceLocator.YouTube.RateVideoAsync(
                    CurrentVideo.Id,
                    "dislike");

            ToolTipService.SetToolTip(
                DislikeButton,
                success ? "Disliked" : "Sign in again to dislike this video");

            if (success)
            {
                ApplyRatingState("dislike");
            }
        }

        private async Task UpdateRatingStateAsync()
        {
            if (LikeButton == null ||
                DislikeButton == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                return;
            }

            string rating =
                await ServiceLocator.YouTube.GetVideoRatingAsync(
                    CurrentVideo.Id);

            ApplyRatingState(rating);
        }

        private void ApplyRatingState(string rating)
        {
            Brush accentBrush =
                Application.Current.Resources.TryGetValue(
                    "AccentButtonBackground",
                    out object accentResource) &&
                accentResource is Brush brush
                    ? brush
                    : new SolidColorBrush(
                        ColorHelper.FromArgb(255, 210, 140, 230));

            LikeButton.Background =
                rating == "like"
                    ? accentBrush
                    : null;

            DislikeButton.Background =
                rating == "dislike"
                    ? accentBrush
                    : null;

            LikeButton.Opacity =
                rating == "dislike"
                    ? 0.65
                    : 1;

            DislikeButton.Opacity =
                rating == "like"
                    ? 0.65
                    : 1;
        }

        private async void SubscribeButton_Click(
            object sender,
            RoutedEventArgs e)
        {
            SubscribeButton.IsEnabled = false;
            SubscribeButton.Content = "Subscribing...";

            if (string.IsNullOrWhiteSpace(CurrentVideo.ChannelId) &&
                !await TryResolveCurrentChannelAsync())
            {
                SubscribeButton.Content = "Channel unavailable";
                SubscribeButton.IsEnabled = true;
                return;
            }

            bool success =
                await ServiceLocator.YouTube.SubscribeToChannelAsync(
                    CurrentVideo.ChannelId);

            SubscribeButton.Content =
                success
                    ? "Subscribed"
                    : "Sign in to subscribe";

            SubscribeButton.IsEnabled = !success;
        }

        private async Task UpdateSubscriptionButtonAsync()
        {
            if (SubscribeButton == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.ChannelId))
            {
                return;
            }

            bool isSubscribed =
                await ServiceLocator.YouTube.IsSubscribedToChannelAsync(
                    CurrentVideo.ChannelId);

            SubscribeButton.Content =
                isSubscribed
                    ? "Subscribed"
                    : "Subscribe";

            SubscribeButton.IsEnabled = !isSubscribed;
        }

        private void VideoThumbnail_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            if (sender is not Image image ||
                image.DataContext is not VideoItem video ||
                !Uri.TryCreate(video.Thumbnail, UriKind.Absolute, out Uri? uri))
            {
                return;
            }

            image.Source = new BitmapImage(uri);
        }

        private async Task EnrichCurrentVideoAsync()
        {
            if (CurrentVideo.Category != "YouTube" ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id) ||
                !NeedsVideoDetails(CurrentVideo))
            {
                return;
            }

            VideoItem? enrichedVideo =
                await ServiceLocator.YouTube.GetVideoAsync(CurrentVideo.Id);

            if (enrichedVideo == null)
                return;

            // Merge enriched metadata without wiping the data that came from the card/history.
            // Some enrichment paths can legitimately return only duration/thumbnail, so direct
            // assignment here can blank the title/channel/description on the video page.
            if (!string.IsNullOrWhiteSpace(enrichedVideo.Title))
            {
                CurrentVideo.Title = enrichedVideo.Title;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Channel))
            {
                CurrentVideo.Channel = enrichedVideo.Channel;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.ChannelId))
            {
                CurrentVideo.ChannelId = enrichedVideo.ChannelId;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Views))
            {
                CurrentVideo.Views = enrichedVideo.Views;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Likes))
            {
                CurrentVideo.Likes = enrichedVideo.Likes;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Duration))
            {
                CurrentVideo.Duration = enrichedVideo.Duration;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.PublishedAt))
            {
                CurrentVideo.PublishedAt = enrichedVideo.PublishedAt;
            }

            if (enrichedVideo.PublishedAtSort != null)
            {
                CurrentVideo.PublishedAtSort = enrichedVideo.PublishedAtSort;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Thumbnail))
            {
                CurrentVideo.Thumbnail = enrichedVideo.Thumbnail;
            }

            if (!string.IsNullOrWhiteSpace(enrichedVideo.Description))
            {
                CurrentVideo.Description = enrichedVideo.Description;
            }

            // Live/premiere flags are meaningful even when text fields are empty.
            CurrentVideo.IsLive = enrichedVideo.IsLive;
            CurrentVideo.IsPremiere = enrichedVideo.IsPremiere;
            CurrentVideo.LiveChatId = enrichedVideo.LiveChatId;

            if (enrichedVideo.IsEmbeddable)
            {
                CurrentVideo.IsEmbeddable = true;
            }

            // Preserve local continue-watching progress after metadata enrichment.
            WatchHistoryService.ApplySavedProgress(CurrentVideo);

            EnsureCurrentVideoDefaults();
            RefreshCurrentVideoBindings();
            _ = UpdateSubscriptionButtonAsync();

            WatchHistoryService.UpdateMetadata(CurrentVideo);

            if (CurrentVideo.IsLive)
            {
                await LoadLiveChatAsync();
            }
        }

        private static bool NeedsVideoDetails(VideoItem video)
        {
            return string.IsNullOrWhiteSpace(video.Title) ||
                video.Title == "YouTube video" ||
                video.Title == "Loading..." ||
                string.IsNullOrWhiteSpace(video.Channel) ||
                string.IsNullOrWhiteSpace(video.Description) ||
                string.IsNullOrWhiteSpace(video.Duration) ||
                video.IsLive && string.IsNullOrWhiteSpace(video.LiveChatId);
        }

        private void NavigateToVideo(VideoItem video)
        {
            StopPlayback();

            Frame.Navigate(
                typeof(VideoPage),
                new VideoNavigationData
                {
                    CurrentVideo = video,
                    AllVideos = AllVideos.Count > 0
                        ? AllVideos
                        : [video]
                });
        }

        private async Task<bool> TryResolveCurrentChannelAsync()
        {
            if (string.IsNullOrWhiteSpace(CurrentVideo.Channel))
                return false;

            List<SearchResultItem> results =
                await ServiceLocator.YouTube.SearchAllAsync(CurrentVideo.Channel);

            ChannelItem? channel =
                results
                    .Where(result => result.Kind == "Channel")
                    .Select(result => result.Channel)
                    .FirstOrDefault(item =>
                        item != null &&
                        item.Title.Equals(
                            CurrentVideo.Channel,
                            StringComparison.OrdinalIgnoreCase));

            channel ??=
                results
                    .Where(result => result.Kind == "Channel")
                    .Select(result => result.Channel)
                    .FirstOrDefault(item => item != null);

            if (channel == null ||
                string.IsNullOrWhiteSpace(channel.Id))
            {
                return false;
            }

            CurrentVideo.ChannelId = channel.Id;
            return true;
        }

        private static string ExtractVideoId(Uri uri)
        {
            string path =
                uri.AbsolutePath.Trim('/');

            if (path.StartsWith("embed/", StringComparison.OrdinalIgnoreCase))
                return path["embed/".Length..].Split('/')[0];

            if (path.StartsWith("shorts/", StringComparison.OrdinalIgnoreCase))
                return path["shorts/".Length..].Split('/')[0];

            if (path.StartsWith("live/", StringComparison.OrdinalIgnoreCase))
                return path["live/".Length..].Split('/')[0];

            string query =
                uri.Query.TrimStart('?');

            foreach (string part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] pair =
                    part.Split('=', 2);

                if (pair.Length == 2 &&
                    pair[0] == "v")
                {
                    return Uri.UnescapeDataString(pair[1]);
                }
            }

            return "";
        }

        private static string ExtractChannelId(Uri uri)
        {
            string path =
                uri.AbsolutePath.Trim('/');

            if (path.StartsWith("channel/", StringComparison.OrdinalIgnoreCase))
                return path["channel/".Length..].Split('/')[0];

            return "";
        }

        private async Task LoadSocialDataAsync()
        {
            Comments = [];
            LiveChatMessages = [];

            CommentsStatus =
                CurrentVideo.Category == "YouTube"
                    ? "Loading comments..."
                    : "Comments are available for YouTube API videos.";

            LiveChatStatus =
                CurrentVideo.IsLive
                    ? "Loading live chat..."
                    : "This video is not an active livestream.";

            Bindings.Update();
            UpdateLiveChatStatusVisibility();

            await LoadCommentsAsync();
            await LoadLiveChatAsync();
        }

        private async Task LoadCommentsAsync()
        {
            if (CurrentVideo.Category != "YouTube" ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                CommentsStatus = "Comments are available for YouTube API videos.";
                Bindings.Update();
                return;
            }

            Comments =
                await ServiceLocator.YouTube.GetCommentsAsync(CurrentVideo.Id);

            CommentsStatus =
                Comments.Count == 0
                    ? "No comments loaded. Comments may be disabled, unavailable, or the API key may be missing."
                    : $"{Comments.Count} comments";

            Bindings.Update();
        }

        private async Task LoadLiveChatAsync()
        {
            if (!CurrentVideo.IsLive)
            {
                HideLiveChatEmbed();
                LiveChatStatus = "This video is not an active livestream.";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
                return;
            }

            if (string.IsNullOrWhiteSpace(CurrentVideo.LiveChatId))
            {
                LiveChatStatus = "Loading YouTube live chat...";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
                await ShowLiveChatEmbedAsync();
                return;
            }

            HideLiveChatEmbed();

            LiveChatMessages =
                await ServiceLocator.YouTube.GetLiveChatMessagesAsync(CurrentVideo.LiveChatId);

            LiveChatStatus =
                LiveChatMessages.Count == 0
                    ? "No live chat messages loaded. Chat may be unavailable, ended, or the API key may be missing."
                    : $"{LiveChatMessages.Count} live chat messages";

            Bindings.Update();
            UpdateLiveChatStatusVisibility();
        }

        private async Task ShowLiveChatEmbedAsync()
        {
            if (LiveChatWebView == null ||
                string.IsNullOrWhiteSpace(CurrentVideo.Id))
            {
                LiveChatStatus = "This livestream does not expose an active live chat.";
                Bindings.Update();
                return;
            }

            try
            {
                LiveChatMessagesScrollViewer.Visibility = Visibility.Collapsed;
                LiveChatWebViewFrame.Visibility = Visibility.Visible;

                await LiveChatWebView.EnsureCoreWebView2Async();

                string videoId =
                    Uri.EscapeDataString(CurrentVideo.Id);

                LiveChatWebView.CoreWebView2.Navigate(
                    $"https://www.youtube.com/live_chat?v={videoId}&embed_domain=smoothtube.local");

                LiveChatStatus = "";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
            }
            catch (Exception ex) when (ex is InvalidOperationException ||
                ex is COMException)
            {
                HideLiveChatEmbed();
                LiveChatStatus = "Live chat could not be opened for this stream.";
                Bindings.Update();
                UpdateLiveChatStatusVisibility();
            }
        }

        private void HideLiveChatEmbed()
        {
            if (LiveChatWebView != null)
            {
                LiveChatWebViewFrame.Visibility = Visibility.Collapsed;
            }

            if (LiveChatMessagesScrollViewer != null)
            {
                LiveChatMessagesScrollViewer.Visibility = Visibility.Visible;
            }
        }

        private void UpdateLiveChatStatusVisibility()
        {
            if (LiveChatStatusTextBlock == null)
                return;

            LiveChatStatusTextBlock.Visibility =
                string.IsNullOrWhiteSpace(LiveChatStatus)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

            if (SidebarLiveChatStatusTextBlock != null)
            {
                SidebarLiveChatStatusTextBlock.Visibility =
                    LiveChatStatusTextBlock.Visibility;
            }
        }
    }
}
