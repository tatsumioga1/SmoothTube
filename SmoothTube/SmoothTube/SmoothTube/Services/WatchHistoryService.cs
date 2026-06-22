using SmoothTube.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Windows.Storage;

namespace SmoothTube.Services
{
    public static class WatchHistoryService
    {
        private const string ContinueWatchingFileName = "continue-watching.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false
        };

        public static void RecordStarted(VideoItem video)
        {
            if (video == null || string.IsNullOrWhiteSpace(video.Id))
            {
                return;
            }

            List<VideoItem> videos = GetContinueWatching();

            videos.RemoveAll(item => item.Id == video.Id);

            videos.Insert(
                0,
                new VideoItem
                {
                    Id = video.Id,
                    Title = video.Title,
                    Channel = video.Channel,
                    Views = video.Views,
                    Duration = video.Duration,
                    PublishedAt = video.PublishedAt,
                    Thumbnail = NormalizeVideoThumbnailUrl(video.Thumbnail),
                    Category = video.Category,
                    ChannelId = video.ChannelId,
                    IsEmbeddable = video.IsEmbeddable,
                    IsLive = video.IsLive,
                    IsPremiere = video.IsPremiere,
                    IsShort = video.IsShort,
                    Progress = NormalizeProgress(video.Progress)

                    // Important:
                    // Do NOT save Description, Likes, LiveChatId, etc. here.
                    // Continue Watching only needs lightweight card data.
                });

            Save(videos.Take(10).ToList());
        }

        public static List<VideoItem> GetContinueWatching()
        {
            try
            {
                string path = GetContinueWatchingFilePath();

                if (!File.Exists(path))
                {
                    return [];
                }

                string rawValue = File.ReadAllText(path);

                if (string.IsNullOrWhiteSpace(rawValue))
                {
                    return [];
                }

                List<VideoItem> videos =
                    JsonSerializer.Deserialize<List<VideoItem>>(rawValue, JsonOptions) ?? [];

                foreach (VideoItem video in videos)
                {
                    video.Thumbnail = NormalizeVideoThumbnailUrl(video.Thumbnail);
                    video.Progress = NormalizeProgress(video.Progress);
                }

                return videos;
            }
            catch
            {
                return [];
            }
        }

        public static void Clear()
        {
            try
            {
                string path = GetContinueWatchingFilePath();

                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Ignore clear failures so the app never crashes from history cleanup.
            }
        }

        private static void Save(List<VideoItem> videos)
        {
            try
            {
                string path = GetContinueWatchingFilePath();

                string folder = Path.GetDirectoryName(path) ?? ApplicationData.Current.LocalFolder.Path;

                Directory.CreateDirectory(folder);

                string json = JsonSerializer.Serialize(videos, JsonOptions);

                File.WriteAllText(path, json);
            }
            catch
            {
                // Do not crash the app if Continue Watching cannot be saved.
            }
        }

        private static string GetContinueWatchingFilePath()
        {
            return Path.Combine(
                ApplicationData.Current.LocalFolder.Path,
                ContinueWatchingFileName);
        }

        private static double NormalizeProgress(double progress)
        {
            if (progress is > 0 and < 95)
            {
                return progress;
            }

            return 35;
        }

        private static string NormalizeVideoThumbnailUrl(string value)
        {
            if (string.IsNullOrWhiteSpace(value) ||
                !value.Contains("ytimg.com/vi/", StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }

            return Regex.Replace(
                value,
                @"/(?:default|mqdefault|hqdefault|sddefault)\.jpg",
                "/hq720.jpg",
                RegexOptions.IgnoreCase);
        }
    }
}