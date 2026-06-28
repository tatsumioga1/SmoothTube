using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SmoothTube.Services;
using Windows.Storage;

namespace SmoothTube
{
    public sealed partial class SettingsPage : Page
    {
        private static readonly ApplicationDataContainer LocalSettings =
            ApplicationData.Current.LocalSettings;

        private const string AllowAv1Key = "Playback.AllowAv1";
        private const string AllowVp9Key = "Playback.AllowVp9";
        private const string AllowH264Key = "Playback.AllowH264";
        private const string AllowVp8Key = "Playback.AllowVp8";

        private bool isLoadingCodecPreferences;

        public SettingsPage()
        {
            InitializeComponent();

            Loaded += SettingsPage_Loaded;
        }

        private void SettingsPage_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            ApiKeyBox.Password = AppSettings.YouTubeApiKey;
            OAuthClientIdBox.Text = AppSettings.GoogleOAuthClientId;
            OAuthClientSecretBox.Password = AppSettings.GoogleOAuthClientSecret;
            LoadCodecPreferences();
            UpdateStatus();
        }

        private void SaveApiKey_Click(
            object sender,
            RoutedEventArgs e)
        {
            AppSettings.YouTubeApiKey = ApiKeyBox.Password;
            UpdateStatus();
        }

        private void ClearApiKey_Click(
            object sender,
            RoutedEventArgs e)
        {
            ApiKeyBox.Password = "";
            AppSettings.YouTubeApiKey = "";
            UpdateStatus();
        }

        private void UpdateStatus()
        {
            ApiKeyStatusText.Text =
                string.IsNullOrWhiteSpace(AppSettings.YouTubeApiKey)
                    ? "Using local sample data."
                    : "API key saved. Search will use YouTube metadata when the network is available.";

            OAuthStatusText.Text =
                ServiceLocator.GoogleOAuth.IsSignedIn
                    ? "Signed in. SmoothTube can load subscriptions and account-backed YouTube data."
                    : "Not signed in. Add a Google OAuth desktop client ID, then sign in to enable subscriptions.";
        }

        private void SaveOAuthClientId_Click(
            object sender,
            RoutedEventArgs e)
        {
            AppSettings.GoogleOAuthClientId = OAuthClientIdBox.Text;
            AppSettings.GoogleOAuthClientSecret = OAuthClientSecretBox.Password;
            UpdateStatus();
        }

        private async void SignIn_Click(
            object sender,
            RoutedEventArgs e)
        {
            AppSettings.GoogleOAuthClientId = OAuthClientIdBox.Text;
            AppSettings.GoogleOAuthClientSecret = OAuthClientSecretBox.Password;

            bool signedIn =
                await ServiceLocator.GoogleOAuth.SignInAsync();

            OAuthStatusText.Text =
                signedIn
                    ? "Signed in. Subscriptions are now available."
                    : $"Sign-in did not complete. {ServiceLocator.GoogleOAuth.LastError}";
        }

        private void SignOut_Click(
            object sender,
            RoutedEventArgs e)
        {
            ServiceLocator.GoogleOAuth.SignOut();
            UpdateStatus();
        }

        private async void ClearContinueWatching_Click(
            object sender,
            RoutedEventArgs e)
        {
            ContentDialog dialog = new()
            {
                XamlRoot = XamlRoot,
                Title = "Clear Continue Watching?",
                Content = "This removes local playback progress and clears the Continue Watching section. Your YouTube account, API key, and OAuth settings will not be changed.",
                PrimaryButtonText = "Clear",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Close
            };

            ContentDialogResult result = await dialog.ShowAsync();

            if (result != ContentDialogResult.Primary)
            {
                return;
            }

            WatchHistoryService.Clear();
            ClearContinueWatchingInfoBar.IsOpen = true;
        }

        private void LoadCodecPreferences()
        {
            isLoadingCodecPreferences = true;

            AllowAv1Toggle.IsOn = GetBoolSetting(AllowAv1Key, false);
            AllowVp9Toggle.IsOn = GetBoolSetting(AllowVp9Key, true);
            AllowH264Toggle.IsOn = GetBoolSetting(AllowH264Key, true);
            AllowVp8Toggle.IsOn = GetBoolSetting(AllowVp8Key, false);
            CodecWarningInfoBar.IsOpen = false;

            isLoadingCodecPreferences = false;
        }

        private void CodecToggle_Toggled(
            object sender,
            RoutedEventArgs e)
        {
            if (isLoadingCodecPreferences)
            {
                return;
            }

            bool allowAv1 = AllowAv1Toggle.IsOn;
            bool allowVp9 = AllowVp9Toggle.IsOn;
            bool allowH264 = AllowH264Toggle.IsOn;
            bool allowVp8 = AllowVp8Toggle.IsOn;

            if (!allowAv1 && !allowVp9 && !allowH264 && !allowVp8)
            {
                allowH264 = true;

                isLoadingCodecPreferences = true;
                AllowH264Toggle.IsOn = true;
                isLoadingCodecPreferences = false;

                CodecWarningInfoBar.IsOpen = true;
            }
            else
            {
                CodecWarningInfoBar.IsOpen = false;
            }

            SetBoolSetting(AllowAv1Key, allowAv1);
            SetBoolSetting(AllowVp9Key, allowVp9);
            SetBoolSetting(AllowH264Key, allowH264);
            SetBoolSetting(AllowVp8Key, allowVp8);
        }

        private static bool GetBoolSetting(
            string key,
            bool defaultValue)
        {
            if (LocalSettings.Values.TryGetValue(key, out object? value) &&
                value is bool boolValue)
            {
                return boolValue;
            }

            return defaultValue;
        }

        private static void SetBoolSetting(
            string key,
            bool value)
        {
            LocalSettings.Values[key] = value;
        }
    }
}
