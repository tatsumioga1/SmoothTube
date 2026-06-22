# SmoothTube

SmoothTube is a native Windows 11 YouTube client built with WinUI 3. It explores a cleaner desktop-first YouTube experience with a focused navigation shell, signed-in subscriptions, channel pages, search, watch history, comments, and official YouTube playback surfaces.

> SmoothTube is an independent client project. It is not affiliated with, endorsed by, or sponsored by YouTube or Google.

## Overview

SmoothTube was built as a WinUI 3 desktop app with a modern Windows layout, local settings, WebView2 playback, YouTube Data API integration, and public metadata fallbacks through Invidious/Piped-style endpoints. The app is designed around a simple idea: keep the browsing experience native and fast, while using official YouTube surfaces for playback and user-controlled Google credentials for account-specific API access.

The app is still under development and can be unstable, but it is stable enough for experimentation and personal use. Some features may still present bugs. Proceed with patience xD.

## Features

- Native Windows 11 UI built with WinUI 3.
- Left-side navigation for Home, Search, Library, Subscriptions, Channels, and Settings.
- Home page with recommendations and continue-watching content.
- Search page with mixed video and channel results.
- Video page with embedded YouTube playback, metadata, recommendations, comments, and like/dislike UI state.
- Channel pages with channel artwork, recent uploads, livestream sections, and load-more behavior.
- Signed-in subscriptions view with recent uploads, livestream/premiere grouping, shorts filtering, refresh support, and cached subscription metadata.
- Sidebar channel shortcuts for subscribed channels.
- Local watch history and continue-watching support.
- Continue-watching progress overlays and resume playback support.
- Google OAuth sign-in using PKCE and a localhost callback.
- YouTube Data API support for search, metadata, comments, subscriptions, channels, and live chat where available.
- Invidious/Piped-style public endpoint fallbacks for non-authenticated home/search metadata when available.
- WebView2 fallback option for videos that cannot be embedded by owner choice.

## Project Structure

```text
SmoothTube-WinUI-/
|-- README.md
|-- LICENSE
|-- .gitignore
`-- SmoothTube/
    |-- SmoothTube.slnx
    `-- SmoothTube/
        |-- SmoothTube/
        |   |-- Assets/
        |   |   `-- youtube-player.html
        |   |-- controls/
        |   |-- Models/
        |   |-- Services/
        |   |-- App.xaml
        |   |-- MainWindow.xaml
        |   |-- HomePage.xaml
        |   |-- SearchPage.xaml
        |   |-- SubscriptionsPage.xaml
        |   |-- ChannelPage.xaml
        |   |-- VideoPage.xaml
        |   `-- SmoothTube.csproj
        `-- SmoothTube (Package)/
            |-- Images/
            |-- Package.appxmanifest
            `-- SmoothTube (Package).wapproj
```

## How It Was Built

SmoothTube is built around a small set of layers:

- **WinUI 3 shell**: `MainWindow.xaml` hosts the desktop navigation and page frame.
- **Pages**: Home, Search, Library, Subscriptions, Channel, Video, and Settings are separate XAML pages.
- **Reusable cards**: video cards are implemented as shared controls so thumbnails, duration badges, live/premiere tags, and progress state are consistent across the app.
- **Service layer**: `IYouTubeService` and `YouTubeService` centralize YouTube Data API calls, Invidious/Piped-style fallback queries, metadata enrichment, fallback parsing, subscription loading, comments, live chat, and channel data.
- **Settings layer**: `AppSettings` stores API keys, OAuth client details, and tokens in local Windows app settings.
- **OAuth layer**: `GoogleOAuthService` implements Google sign-in using PKCE and a localhost redirect.
- **Playback layer**: videos play through WebView2 using official YouTube embed/player surfaces.
- **Watch history layer**: `WatchHistoryService` tracks local continue-watching progress, resume position, duration, and last watched state.

The app intentionally avoids downloading or bypassing YouTube playback restrictions. Videos that cannot be embedded can be opened through YouTube instead.

## Requirements

- Windows 11.
- Visual Studio 2022 or later.
- .NET 8 SDK.
- Windows App SDK / WinUI 3 workload.
- Microsoft WebView2 Runtime.
- A Google Cloud project with the YouTube Data API enabled, if you want live YouTube data.
- A Google OAuth Desktop client, if you want signed-in features such as subscriptions.

## Getting Started

1. Clone the repository.

```bash
git clone https://github.com/tatsumioga1/SmoothTube.git
cd SmoothTube
```

2. Open the solution:

```text
SmoothTube/SmoothTube.slnx
```

3. In Visual Studio, select the package project or app startup profile.
4. Restore NuGet packages if Visual Studio does not restore them automatically.
5. Build the solution.
6. Run the app.
7. Open Settings inside the app.
8. Add your own YouTube Data API key.
9. Optional: add your OAuth Desktop client ID and secret, then sign in to enable subscriptions and signed-in API features.

## Visual Studio Setup

Install the following workloads/components in Visual Studio:

- **.NET desktop development**.
- **Windows application development** / Windows App SDK tooling.
- **WinUI application development tools**, if shown as an individual component.
- .NET 8 SDK.
- Windows App SDK runtime/tooling.
- WebView2 Runtime.

If the solution does not launch immediately, make sure the packaged app project is selected as the startup project.

## Google Cloud Setup

SmoothTube does not ship with API credentials. To use live YouTube data, create your own Google Cloud project and credentials.

### 1. Create or select a Google Cloud project

1. Open Google Cloud Console.
2. Create a new project or select an existing one.
3. Make sure billing/quota settings are appropriate for your use.

### 2. Enable YouTube Data API

1. Go to **APIs & Services**.
2. Open **Library**.
3. Search for **YouTube Data API v3**.
4. Enable it for your project.

### 3. Create an API key

1. Go to **APIs & Services** > **Credentials**.
2. Create an **API key**.
3. Copy the key.
4. Paste it into SmoothTube Settings.

The API key is used for public metadata features such as search, video metadata, comments, channel data, and other non-private YouTube Data API requests.

### 4. Create OAuth credentials for signed-in features

1. Go to **APIs & Services** > **Credentials**.
2. Create an OAuth client.
3. Select **Desktop app** as the application type.
4. Copy the client ID and client secret.
5. Paste them into SmoothTube Settings.
6. Use the app's sign-in option to authorize access.

OAuth is used for account-related features such as subscriptions and signed-in YouTube API access.

## Running the App

After credentials are added:

1. Open SmoothTube.
2. Go to **Settings**.
3. Save the YouTube Data API key.
4. Optional: save OAuth client details.
5. Optional: sign in with Google.
6. Use Home, Search, Subscriptions, Channels, and Video pages normally.

Some data may take a little time to load because subscription feeds, metadata enrichment, duration lookup, and fallback endpoints are loaded progressively.

## Credentials and Security

This repository does **not** include personal Google credentials.

Each developer must provide their own:

- YouTube Data API key.
- OAuth Desktop client ID.
- OAuth Desktop client secret.

These values are entered in the app Settings page and stored locally by Windows app settings at runtime. Access tokens and refresh tokens are also local runtime data.

The following are intentionally ignored by `.gitignore`:

- Visual Studio workspace cache.
- Build output such as `bin/` and `obj/`.
- Packaged app output.
- Local user files such as `*.user`.
- Local credential-style files such as `.env`, `client_secret*.json`, `token*.json`, and `credentials*.json`.

Before publishing, the source tree was checked for common Google API key, OAuth client ID, client secret, access token, and refresh token patterns.

## YouTube and API Notes

SmoothTube uses a combination of:

- YouTube Data API for official metadata and authenticated data.
- OAuth for user-authorized subscription and account-related access.
- Invidious/Piped-style public endpoints for limited unauthenticated home/search fallback metadata.
- WebView2 for official YouTube playback surfaces.
- Local caching to reduce repeated subscription loading.

Some YouTube data is dependent on API availability, quota, video owner settings, public endpoint availability, and what YouTube exposes through official endpoints. Public fallback endpoints can change, go offline, rate-limit requests, or return incomplete metadata. Videos with embedding disabled are handled through a YouTube watch option rather than bypassing restrictions.

### API quota notes

The YouTube Data API has quota limits. Subscription, livestream, premiere, search, comments, and metadata requests may be limited by your API project's daily quota. SmoothTube includes caching and fallback behavior, but it cannot bypass Google API quota limits.

If subscription livestream or premiere scanning stops, it may be because the YouTube Search API quota has been exhausted for the day.

## Continue Watching and Resume Playback

SmoothTube stores continue-watching progress locally. The app tracks:

- Video ID.
- Title and channel metadata.
- Thumbnail and duration.
- Resume timestamp.
- Duration in seconds.
- Progress percentage.
- Last watched time.

Progress is displayed as a thumbnail overlay on supported video cards. When a video is reopened, SmoothTube attempts to resume from the saved watch position.

The watch history data is local runtime app data and is not committed to the repository.

## Current Limitations

- Playback uses YouTube's official embedded/player surfaces, so some controls and behaviors are governed by YouTube.
- Some videos cannot be embedded because the owner disables playback on other websites.
- Live chat and comment functionality is read-oriented where available.
- API quota and Google account permissions affect which features are available.
- Invidious/Piped-style fallback data is best-effort and may vary by public instance availability.
- Downloads are not implemented because SmoothTube should not bypass YouTube restrictions or unsupported offline flows.
- Livestream and premiere detection can be limited by API quota and public metadata availability.
- Running under the Visual Studio debugger can make WebView2/player UI feel heavier than normal execution.

## Troubleshooting

### The app builds, but live data does not load

- Confirm your YouTube Data API key is saved in Settings.
- Confirm YouTube Data API v3 is enabled in Google Cloud.
- Check whether your API quota has been exhausted.
- Restart the app after changing credentials.

### Subscriptions do not load

- Confirm OAuth client ID and secret are saved.
- Sign in again from the app Settings page.
- Confirm the Google account has YouTube subscriptions.
- Check whether the OAuth consent screen and scopes are configured correctly.

### Some videos do not play inside the app

Some videos have embedding disabled by the owner. SmoothTube does not bypass that restriction. Use the **Open on YouTube** option for those videos.

### Continue Watching does not update immediately

- Watch a video for at least a few seconds.
- Navigate back using the app back button.
- Restart the app if local state appears stale.
- Make sure the video is not a livestream or premiere, because those are treated differently.

### Player controls feel laggy while debugging

This can happen when running under the Visual Studio debugger because of debug output, XAML diagnostics, and WebView2 debugging overhead. Test outside the debugger before assuming it is a runtime performance issue.

## Roadmap

- Improve subscription freshness and livestream/premiere detection.
- Refine fullscreen playback behavior.
- Improve channel pages with playlists and richer channel metadata.
- Expand Library with better watch history and saved video workflows.
- Add more robust visual loading states and offline/error handling.
- Package and sign the app for local installation.

## Contributing

Feedback, bug reports, and suggestions are welcome.

This project is source-available for viewing and reference, but it is not open source. Please do not copy, modify, redistribute, publish, rebrand, or reuse the code, assets, design, or project structure without prior written permission.

If you want to contribute or collaborate, please contact the project owner first.

## AI Assistance Disclosure

Parts of SmoothTube were developed with AI-assisted coding support for debugging, refactoring, documentation, and implementation guidance. The project direction, feature decisions, testing, integration, and final code review were handled by the project maintainer.

AI assistance does not change the project license, ownership, or usage restrictions.

## License

SmoothTube is released as **source-available / all rights reserved**.

You may view the repository for learning and reference. You may not copy, modify, redistribute, publish, rebrand, sublicense, or use the project commercially without prior written permission from the copyright holder.

See [`LICENSE`](LICENSE) for the full license terms.
