using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmoothTube.Controls
{
    public sealed partial class VideoCardControl : UserControl
    {
        private int thumbnailFallbackIndex;
        private List<string> thumbnailFallbackUrls = [];

        public VideoCardControl()
        {
            InitializeComponent();

            Loaded += VideoCardControl_Loaded;
        }

        private void VideoCardControl_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            UpdateProgressVisibility();
            UpdateBadgeVisibility();
            UpdateThumbnailSource();
        }

        public string Title
        {
            get => (string)GetValue(TitleProperty);
            set => SetValue(TitleProperty, value);
        }

        public static readonly DependencyProperty TitleProperty =
            DependencyProperty.Register(
                nameof(Title),
                typeof(string),
                typeof(VideoCardControl),
                new PropertyMetadata(""));

        public string Channel
        {
            get => (string)GetValue(ChannelProperty);
            set => SetValue(ChannelProperty, value);
        }

        public static readonly DependencyProperty ChannelProperty =
            DependencyProperty.Register(
                nameof(Channel),
                typeof(string),
                typeof(VideoCardControl),
                new PropertyMetadata(""));

        public string Views
        {
            get => (string)GetValue(ViewsProperty);
            set => SetValue(ViewsProperty, value);
        }

        public static readonly DependencyProperty ViewsProperty =
            DependencyProperty.Register(
                nameof(Views),
                typeof(string),
                typeof(VideoCardControl),
                new PropertyMetadata(""));

        public string Thumbnail
        {
            get => (string)GetValue(ThumbnailProperty);
            set => SetValue(ThumbnailProperty, value);
        }

        public static readonly DependencyProperty ThumbnailProperty =
            DependencyProperty.Register(
                nameof(Thumbnail),
                typeof(string),
                typeof(VideoCardControl),
                new PropertyMetadata(
                    "",
                    OnThumbnailChanged));

        public string Duration
        {
            get => (string)GetValue(DurationProperty);
            set => SetValue(DurationProperty, value);
        }

        public static readonly DependencyProperty DurationProperty =
            DependencyProperty.Register(
                nameof(Duration),
                typeof(string),
                typeof(VideoCardControl),
                new PropertyMetadata(
                    "",
                    OnBadgePropertyChanged));

        public bool IsLive
        {
            get => (bool)GetValue(IsLiveProperty);
            set => SetValue(IsLiveProperty, value);
        }

        public static readonly DependencyProperty IsLiveProperty =
            DependencyProperty.Register(
                nameof(IsLive),
                typeof(bool),
                typeof(VideoCardControl),
                new PropertyMetadata(
                    false,
                    OnBadgePropertyChanged));

        public bool IsPremiere
        {
            get => (bool)GetValue(IsPremiereProperty);
            set => SetValue(IsPremiereProperty, value);
        }

        public static readonly DependencyProperty IsPremiereProperty =
            DependencyProperty.Register(
                nameof(IsPremiere),
                typeof(bool),
                typeof(VideoCardControl),
                new PropertyMetadata(
                    false,
                    OnBadgePropertyChanged));

        public double Progress
        {
            get => (double)GetValue(ProgressProperty);
            set => SetValue(ProgressProperty, value);
        }

        public static readonly DependencyProperty ProgressProperty =
            DependencyProperty.Register(
                nameof(Progress),
                typeof(double),
                typeof(VideoCardControl),
                new PropertyMetadata(
                    0.0,
                    OnProgressChanged));

        public bool ShowProgress
        {
            get => (bool)GetValue(ShowProgressProperty);
            set => SetValue(ShowProgressProperty, value);
        }

        public static readonly DependencyProperty ShowProgressProperty =
            DependencyProperty.Register(
                nameof(ShowProgress),
                typeof(bool),
                typeof(VideoCardControl),
                new PropertyMetadata(
                    true,
                    OnShowProgressChanged));

        public double CardWidth
        {
            get => (double)GetValue(CardWidthProperty);
            set => SetValue(CardWidthProperty, value);
        }

        public static readonly DependencyProperty CardWidthProperty =
            DependencyProperty.Register(
                nameof(CardWidth),
                typeof(double),
                typeof(VideoCardControl),
                new PropertyMetadata(300.0));

        public double CardHeight
        {
            get => (double)GetValue(CardHeightProperty);
            set => SetValue(CardHeightProperty, value);
        }

        public static readonly DependencyProperty CardHeightProperty =
            DependencyProperty.Register(
                nameof(CardHeight),
                typeof(double),
                typeof(VideoCardControl),
                new PropertyMetadata(265.0));

        public double ThumbnailHeight
        {
            get => (double)GetValue(ThumbnailHeightProperty);
            set => SetValue(ThumbnailHeightProperty, value);
        }

        public static readonly DependencyProperty ThumbnailHeightProperty =
            DependencyProperty.Register(
                nameof(ThumbnailHeight),
                typeof(double),
                typeof(VideoCardControl),
                new PropertyMetadata(168.75));

        public Stretch ThumbnailStretch
        {
            get => (Stretch)GetValue(ThumbnailStretchProperty);
            set => SetValue(ThumbnailStretchProperty, value);
        }

        public static readonly DependencyProperty ThumbnailStretchProperty =
            DependencyProperty.Register(
                nameof(ThumbnailStretch),
                typeof(Stretch),
                typeof(VideoCardControl),
                new PropertyMetadata(Stretch.UniformToFill));

        private static void OnProgressChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoCardControl control)
            {
                control.UpdateProgressVisibility();
            }
        }

        private static void OnShowProgressChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoCardControl control)
            {
                control.UpdateProgressVisibility();
            }
        }

        private static void OnBadgePropertyChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoCardControl control)
            {
                control.UpdateBadgeVisibility();
            }
        }

        private static void OnThumbnailChanged(
            DependencyObject d,
            DependencyPropertyChangedEventArgs e)
        {
            if (d is VideoCardControl control)
            {
                control.UpdateThumbnailSource();
            }
        }

        private void UpdateProgressVisibility()
        {
            if (ProgressBarControl == null)
                return;

            ProgressBarControl.Visibility =
                ShowProgress && Progress > 0 && Progress < 100
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void UpdateBadgeVisibility()
        {
            if (DurationBadge == null ||
                LiveBadge == null ||
                PremiereBadge == null)
            {
                return;
            }

            LiveBadge.Visibility =
                IsLive
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            PremiereBadge.Visibility =
                !IsLive && IsPremiere
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            DurationBadge.Visibility =
                !IsLive && !IsPremiere && !string.IsNullOrWhiteSpace(Duration)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        private void UpdateThumbnailSource()
        {
            if (ThumbnailImage == null)
            {
                return;
            }

            thumbnailFallbackIndex = 0;
            thumbnailFallbackUrls = BuildThumbnailFallbackUrls();

            SetThumbnailFromFallbackList();
        }

        private void SetThumbnailFromFallbackList()
        {
            if (ThumbnailImage == null)
            {
                return;
            }

            while (thumbnailFallbackIndex < thumbnailFallbackUrls.Count)
            {
                string thumbnail = thumbnailFallbackUrls[thumbnailFallbackIndex];

                if (Uri.TryCreate(thumbnail, UriKind.Absolute, out Uri? uri) &&
                    (uri.Scheme == "https" ||
                     uri.Scheme == "http" ||
                     uri.Scheme == "ms-appx" ||
                     uri.Scheme == "file"))
                {
                    ThumbnailImage.Source = new BitmapImage(uri)
                    {
                        DecodePixelWidth = Math.Max(1, (int)CardWidth)
                    };

                    return;
                }

                thumbnailFallbackIndex++;
            }

            ThumbnailImage.Source = null;
        }

        private List<string> BuildThumbnailFallbackUrls()
        {
            List<string> urls = [];

            string originalThumbnail = Thumbnail?.StartsWith("//", StringComparison.Ordinal) == true
                ? "https:" + Thumbnail
                : Thumbnail ?? "";

            originalThumbnail = NormalizeYouTubeThumbnailUrl(originalThumbnail);

            string videoId =
                ExtractYouTubeVideoIdFromThumbnail(originalThumbnail);

            bool looksLikeLiveThumbnail =
                originalThumbnail.Contains("_live.", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(videoId))
            {
                // Prefer true 16:9 YouTube thumbnails first.
                // Some feed/API thumbnails are 4:3 variants such as default.jpg/mqdefault.jpg.
                // Those load successfully, so ImageFailed never fires, but they create black bars.
                if (looksLikeLiveThumbnail)
                {
                    urls.Add($"https://i.ytimg.com/vi/{videoId}/hq720_live.jpg");
                    urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/hq720_live.webp");
                    urls.Add($"https://i.ytimg.com/vi/{videoId}/maxresdefault_live.jpg");
                    urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/maxresdefault_live.webp");
                }

                urls.Add($"https://i.ytimg.com/vi/{videoId}/maxresdefault.jpg");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/hq720.jpg");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/sddefault.jpg");

                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/maxresdefault.webp");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/hq720.webp");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/sddefault.webp");
            }

            if (!string.IsNullOrWhiteSpace(originalThumbnail))
            {
                urls.Add(originalThumbnail);
            }

            if (!string.IsNullOrWhiteSpace(videoId))
            {
                // Last-resort variants. These can be 4:3, so keep them after 16:9 options.
                urls.Add($"https://i.ytimg.com/vi/{videoId}/hqdefault.jpg");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/mqdefault.jpg");
                urls.Add($"https://i.ytimg.com/vi/{videoId}/default.jpg");

                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/hqdefault.webp");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/mqdefault.webp");
                urls.Add($"https://i.ytimg.com/vi_webp/{videoId}/default.webp");
            }

            return urls
                .Where(url => !string.IsNullOrWhiteSpace(url))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string NormalizeYouTubeThumbnailUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            value = value
                .Replace(@"\u0026", "&", StringComparison.Ordinal)
                .Replace(@"\u003d", "=", StringComparison.Ordinal)
                .Replace(@"\/", "/", StringComparison.Ordinal)
                .Replace(@"\u002F", "/", StringComparison.Ordinal);

            if (!value.Contains("ytimg.com/", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            // Live thumbnails often come as hqdefault_live.jpg, which is more 4:3-ish.
            // Prefer the 16:9 live version first, then fallbacks will handle missing variants.
            value = value
                .Replace("/default_live.jpg", "/hq720_live.jpg", StringComparison.OrdinalIgnoreCase)
                .Replace("/mqdefault_live.jpg", "/hq720_live.jpg", StringComparison.OrdinalIgnoreCase)
                .Replace("/hqdefault_live.jpg", "/hq720_live.jpg", StringComparison.OrdinalIgnoreCase)
                .Replace("/sddefault_live.jpg", "/hq720_live.jpg", StringComparison.OrdinalIgnoreCase)
                .Replace("/default_live.webp", "/hq720_live.webp", StringComparison.OrdinalIgnoreCase)
                .Replace("/mqdefault_live.webp", "/hq720_live.webp", StringComparison.OrdinalIgnoreCase)
                .Replace("/hqdefault_live.webp", "/hq720_live.webp", StringComparison.OrdinalIgnoreCase)
                .Replace("/sddefault_live.webp", "/hq720_live.webp", StringComparison.OrdinalIgnoreCase);

            return value;
        }

        private static string ExtractYouTubeVideoIdFromThumbnail(string thumbnail)
        {
            if (string.IsNullOrWhiteSpace(thumbnail))
            {
                return "";
            }

            Match match =
                Regex.Match(
                    thumbnail,
                    @"/vi(?:_webp)?/(?<id>[^/]+)/",
                    RegexOptions.IgnoreCase);

            return match.Success
                ? match.Groups["id"].Value
                : "";
        }

        private void ThumbnailImage_ImageFailed(
            object sender,
            ExceptionRoutedEventArgs e)
        {
            thumbnailFallbackIndex++;
            SetThumbnailFromFallbackList();
        }

        private void Card_PointerEntered(
            object sender,
            PointerRoutedEventArgs e)
        {
            AnimateThumbnail(1.065);
        }

        private void Card_PointerExited(
            object sender,
            PointerRoutedEventArgs e)
        {
            AnimateThumbnail(1);
        }

        private void AnimateThumbnail(double targetScale)
        {
            if (ThumbnailImage?.RenderTransform is not ScaleTransform scale)
                return;

            var animationX = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true
            };

            var animationY = new DoubleAnimation
            {
                To = targetScale,
                Duration = TimeSpan.FromMilliseconds(180),
                EnableDependentAnimation = true
            };

            Storyboard.SetTarget(animationX, scale);
            Storyboard.SetTargetProperty(animationX, "ScaleX");

            Storyboard.SetTarget(animationY, scale);
            Storyboard.SetTargetProperty(animationY, "ScaleY");

            var storyboard = new Storyboard();
            storyboard.Children.Add(animationX);
            storyboard.Children.Add(animationY);
            storyboard.Begin();
        }
    }
}
