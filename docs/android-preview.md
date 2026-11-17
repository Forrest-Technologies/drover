# Drover on Android

Download the APK from the [GitHub prereleases](https://github.com/Forrest-Technologies/drover/releases) and open it on Android 7 or later. Enter the HTTPS address of your Drover server and sign in with your existing account.

The preview remembers the server. With native sessions available on your server, the app stays signed in when Android stops the process. Older servers require signing in again. To change servers, sign out and choose **Use another server** on the sign-in screen; the same choice is offered when the server does not answer. Signing in to a tool server opens your browser and brings you back to the connection in the app; on an older server, finish it in the browser. Share text, links, images, or documents to **Drover Preview** from another app, choose the bot, then review the draft and send it. In a conversation’s file card, choose **Save** for Android’s file picker or **Share** for the Android Sharesheet.

## Notifications

Install and configure a [UnifiedPush distributor](https://unifiedpush.org/users/distributors/). In Drover, open **Account → Notifications**, then turn on notifications and allow the Android permission when asked. Choose **Send a test notification** to check delivery to this device.

Tap an alert to open the conversation. A finished bot reply has a **Reply** action in the Android notification shade on Android 12 or later, when the server supports it. A locked phone asks to be unlocked before the reply is sent. Approval requests open the conversation for review.

If alerts arrive late, open **Notifications arriving late? → Open battery settings**. Android takes you to settings for the distributor, where you can check whether it is allowed to run in the background.

## Build and release

The Android project lives in `Drover.Apps.slnx`. Install JDK 21 and the pinned workloads and SDK packages in the Android preview workflow. Then run `dotnet publish src/Drover.App/Drover.App.csproj -c Release`.

Maintainers publish previews by pushing a protected tag like `v0.1.0-android-preview.1` at a commit on `main`:

```bash
git fetch origin main
git tag -a v0.1.0-android-preview.1 origin/main -m 'Android preview 1'
git push origin refs/tags/v0.1.0-android-preview.1
```

The workflow builds on GitHub-hosted runners, signs in the tag-only `android-preview` environment, checks the certificate and alignment, then publishes the APK and SHA-256 checksum as a prerelease. Only the signing job receives the preview keystore and password. Keep the package ID and signing certificate for future preview updates. The ordinary release environment is separate.
