# Drover on Android

Download the APK from the [GitHub prereleases](https://github.com/Forrest-Technologies/drover/releases) and open it on Android 7 or later. Enter the HTTPS address of your Drover server and sign in with your existing account.

The preview remembers the server. With native sessions available on your server, the app stays signed in when Android stops the process. Older servers require signing in again. To change servers, sign out and choose **Use another server** on the sign-in screen; the same choice is offered when the server does not answer. Signing in to a tool server opens your browser and brings you back to the connection in the app; on an older server, finish it in the browser. Share text, links, images, or documents to **Drover Preview** from another app, choose the bot, then review the draft and send it. In a conversation’s file card, choose **Save** for Android’s file picker or **Share** for the Android Sharesheet.

## Notifications

Install and configure a [UnifiedPush distributor](https://unifiedpush.org/users/distributors/). In Drover, open **Account → Notifications**, then turn on notifications and allow the Android permission when asked. Choose **Send a test notification** to check delivery to this device.

Tap an alert to open the conversation. A finished bot reply has a **Reply** action in the Android notification shade on Android 12 or later, when the server supports it. A locked phone asks to be unlocked before the reply is sent. Approval requests open the conversation for review.

If alerts arrive late, open **Notifications arriving late? → Open battery settings**. Android takes you to settings for the distributor, where you can check whether it is allowed to run in the background.

## Build and release

The Uno Android app uses the `net11.0-android` target in `Drover.Native.slnx`. It installs alongside the earlier Android preview, so enter your server address and sign in on first launch.

Install JDK 21, the Android workload, and the SDK packages pinned in the Android preview workflow. Build the APK with:

```bash
dotnet publish src/Drover.Native -f net11.0-android -c Release \
  -p:DroverNativeTargetFrameworks=net11.0-android -p:AndroidPackageFormats=apk
```

Merging a reviewed pull request into `develop` publishes a signed Android prerelease. Promoting `develop` into `main` publishes a signed stable Android release. There is no separate `preview` branch.

Each run uses its workflow run number as the Android version code. The display versions are `0.1.<run>-preview.<run>` on `develop` and `0.1.<run>` on `main`; tags start with `android-v`. Keep the existing workflow file so its run numbers continue increasing. Both channels use the same package ID (`com.forresttechnologies.drover.client`) and certificate, so a later build can update an earlier installation.

Builds run on GitHub-hosted runners. Pull requests build unsigned APKs and never enter the signing environment. Release builds do not restore pull request caches. Only protected branch pushes or legacy protected preview tags can enter the `android-preview` signing environment. Despite its historical name, this environment signs both Android channels. The signing job receives only the keystore, password, alias and expected certificate digest; it verifies the certificate and alignment before publishing the APK and SHA-256 checksum.

Legacy `v0.1.0-android-preview.1` style tags remain supported when their commit is on `develop` or `main`. Automatic Android tags do not match the desktop release workflow. Android releases use `--latest=false`, including stable APK releases, so the desktop updater's latest release remains separate.

See [maintainer settings](maintainers.md#branches-and-android-releases) for the branch and environment restrictions required before enabling releases.
