using System.Linq;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SmoothTube.Models
{
    public class SearchResultItem
    {
        private ImageSource? thumbnailSource;
        private ImageSource? fallbackThumbnailSource;

        public string Kind { get; set; } = "Video";

        public VideoItem? Video { get; set; }

        public ChannelItem? Channel { get; set; }

        public string Title =>
            Kind == "Channel"
                ? Channel?.Title ?? ""
                : Video?.Title ?? "";

        public string Subtitle =>
            Kind == "Channel"
                ? "Channel"
                : Video?.Channel ?? "";

        public Visibility ChannelThumbnailVisibility =>
            Kind == "Channel"
                ? Visibility.Visible
                : Visibility.Collapsed;

        public Visibility VideoThumbnailVisibility =>
            Kind == "Channel"
                ? Visibility.Collapsed
                : Visibility.Visible;

        public string Detail
        {
            get
            {
                if (Kind == "Channel")
                    return Channel?.Description ?? "";

                return string.Join(
                    " - ",
                    new[]
                    {
                        string.IsNullOrWhiteSpace(Video?.Duration)
                            ? ""
                            : $"Duration: {Video.Duration}",
                        string.IsNullOrWhiteSpace(Video?.Views)
                            ? ""
                            : $"Views: {Video.Views}",
                        string.IsNullOrWhiteSpace(Video?.PublishedAt)
                            ? ""
                            : $"Posted: {Video.PublishedAt}"
                    }.Where(value => !string.IsNullOrWhiteSpace(value)));
            }
        }

        public string Thumbnail
        {
            get
            {
                string normalized = NormalizeThumbnail(
                    Kind == "Channel"
                        ? Channel?.Thumbnail ?? ""
                        : Video?.Thumbnail ?? "");

                return Kind == "Channel"
                    ? normalized
                    : PreferWideYouTubeThumbnail(normalized);
            }
        }

        public string FallbackThumbnail =>
            NormalizeThumbnail(
                Kind == "Channel"
                    ? Channel?.Thumbnail ?? ""
                    : Video?.Thumbnail ?? "");

        public ImageSource? ThumbnailSource
        {
            get
            {
                if (thumbnailSource != null)
                    return thumbnailSource;

                if (!System.Uri.TryCreate(Thumbnail, System.UriKind.Absolute, out System.Uri? uri))
                    return null;

                thumbnailSource =
                    new BitmapImage(uri)
                    {
                        DecodePixelWidth = Kind == "Channel" ? 128 : 320,
                        DecodePixelHeight = Kind == "Channel" ? 128 : 180
                    };

                return thumbnailSource;
            }
        }

        public ImageSource? FallbackThumbnailSource
        {
            get
            {
                if (fallbackThumbnailSource != null)
                    return fallbackThumbnailSource;

                if (!System.Uri.TryCreate(FallbackThumbnail, System.UriKind.Absolute, out System.Uri? uri))
                    return null;

                fallbackThumbnailSource =
                    new BitmapImage(uri)
                    {
                        DecodePixelWidth = Kind == "Channel" ? 128 : 320,
                        DecodePixelHeight = Kind == "Channel" ? 128 : 180
                    };

                return fallbackThumbnailSource;
            }
        }

        private static string NormalizeThumbnail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (value.StartsWith("//", System.StringComparison.Ordinal))
                value = "https:" + value;

            return System.Uri.TryCreate(value, System.UriKind.Absolute, out System.Uri? uri) &&
                (uri.Scheme == "https" ||
                    uri.Scheme == "http" ||
                    uri.Scheme == "ms-appx" ||
                    uri.Scheme == "file")
                    ? value
                    : "";
        }

        private static string PreferWideYouTubeThumbnail(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "";

            if (!System.Uri.TryCreate(value, System.UriKind.Absolute, out System.Uri? uri))
                return value;

            string host = uri.Host.ToLowerInvariant();
            if (!host.Contains("ytimg.com"))
                return value;

            string[] segments = uri.AbsolutePath
                .Split('/', System.StringSplitOptions.RemoveEmptyEntries);

            int videoIdIndex = -1;

            for (int i = 0; i < segments.Length - 1; i++)
            {
                if (segments[i] == "vi" ||
                    segments[i] == "vi_webp")
                {
                    videoIdIndex = i + 1;
                    break;
                }
            }

            if (videoIdIndex < 0 || videoIdIndex >= segments.Length)
                return value;

            string videoId = segments[videoIdIndex];

            return $"https://i.ytimg.com/vi/{videoId}/hq720.jpg";
        }
    }
}
