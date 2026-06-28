using System;
using System.Collections.Generic;
using Windows.Storage;

namespace SmoothTube.Services
{
    public static class CodecPreferences
    {
        private static readonly ApplicationDataContainer LocalSettings =
            ApplicationData.Current.LocalSettings;

        private const string AllowAv1Key = "Playback.AllowAv1";
        private const string AllowVp9Key = "Playback.AllowVp9";
        private const string AllowH264Key = "Playback.AllowH264";
        private const string AllowVp8Key = "Playback.AllowVp8";

        public static bool AllowAv1
        {
            get => GetBool(AllowAv1Key, false);
            set => SetCodecValue(AllowAv1Key, value);
        }

        public static bool AllowVp9
        {
            get => GetBool(AllowVp9Key, true);
            set => SetCodecValue(AllowVp9Key, value);
        }

        public static bool AllowH264
        {
            get => GetBool(AllowH264Key, true);
            set => SetCodecValue(AllowH264Key, value);
        }

        public static bool AllowVp8
        {
            get => GetBool(AllowVp8Key, false);
            set => SetCodecValue(AllowVp8Key, value);
        }

        public static string BuildBlockedCodecParameter()
        {
            List<string> blockedCodecs = [];

            if (!AllowAv1)
                blockedCodecs.Add("av01");

            if (!AllowVp9)
                blockedCodecs.Add("vp9");

            if (!AllowH264)
                blockedCodecs.Add("avc1");

            if (!AllowVp8)
                blockedCodecs.Add("vp8");

            return string.Join(",", blockedCodecs);
        }

        public static bool HasAtLeastOneCodecEnabled()
        {
            return AllowAv1 || AllowVp9 || AllowH264 || AllowVp8;
        }

        public static void EnsureAtLeastOneCodecEnabled()
        {
            if (HasAtLeastOneCodecEnabled())
                return;

            AllowH264 = true;
        }

        private static bool GetBool(string key, bool defaultValue)
        {
            if (LocalSettings.Values.TryGetValue(key, out object? value) &&
                value is bool boolValue)
            {
                return boolValue;
            }

            return defaultValue;
        }

        private static void SetCodecValue(string key, bool value)
        {
            LocalSettings.Values[key] = value;

            if (!HasAtLeastOneCodecEnabled())
            {
                LocalSettings.Values[AllowH264Key] = true;
            }
        }
    }
}