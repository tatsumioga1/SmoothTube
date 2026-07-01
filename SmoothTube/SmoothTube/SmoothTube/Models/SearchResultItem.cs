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

        public string TitleLeadingEmoji =>
            GetLeadingEmojiCluster(Title.TrimStart());

        public Visibility TitleLeadingEmojiVisibility =>
            string.IsNullOrWhiteSpace(TitleLeadingEmoji)
                ? Visibility.Collapsed
                : Visibility.Visible;

        public string TitleDisplayText
        {
            get
            {
                string title =
                    Title ?? "";

                string trimmedTitle =
                    title.TrimStart();

                string leadingEmoji =
                    GetLeadingEmojiCluster(trimmedTitle);

                return string.IsNullOrWhiteSpace(leadingEmoji)
                    ? title
                    : trimmedTitle[leadingEmoji.Length..].TrimStart();
            }
        }

        public Thickness TitleTextMargin =>
            TitleLeadingEmojiVisibility == Visibility.Visible
                ? new Thickness(8, 0, 0, 0)
                : new Thickness(0);

        private static string GetLeadingEmojiCluster(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            char first = text[0];

            bool isSurrogateEmoji =
                char.IsHighSurrogate(first) &&
                text.Length > 1 &&
                char.IsLowSurrogate(text[1]);

            bool isBmpEmoji =
                first is >= '\u2600' and <= '\u27BF';

            if (!isSurrogateEmoji && !isBmpEmoji)
                return "";

            int length =
                isSurrogateEmoji
                    ? 2
                    : 1;

            if (text.Length > length &&
                text[length] == '\uFE0F')
            {
                length++;
            }

            return text[..length];
        }

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

            value = value.Trim();

            if (value.StartsWith("Assets/", System.StringComparison.OrdinalIgnoreCase) ||
                value.StartsWith("Assets\\", System.StringComparison.OrdinalIgnoreCase))
            {
                return "ms-appx:///" + value.Replace('\\', '/');
            }

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
            // Do not manufacture hq720.jpg URLs: many valid YouTube videos do not
            // have that rendition. The source-selected image is the reliable one.
            return value;
        }
    }
}
