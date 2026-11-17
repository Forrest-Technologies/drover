# Drover on Android

Download the APK from the [GitHub prereleases](https://github.com/Forrest-Technologies/drover/releases) and open it on Android 7 or later. Enter the HTTPS address of your Drover server and sign in with your existing account.

The preview remembers the server address. Sign in again if Android stops the process. To change servers, clear Drover Preview’s storage in Android Settings and reopen it. Native push is not included. Use the browser for integrations that need an OAuth return or file downloads.

## Build and release

The Android project lives in `Drover.Apps.slnx`. Install JDK 21 and the pinned workloads and SDK packages in the Android preview workflow. Then run `dotnet publish src/Drover.App/Drover.App.csproj -c Release`.

Maintainers publish previews by pushing a protected tag like `v0.1.0-android-preview.1` at a commit on `main`:

```bash
git fetch origin main
git tag -a v0.1.0-android-preview.1 origin/main -m 'Android preview 1'
git push origin refs/tags/v0.1.0-android-preview.1
```

The workflow builds on GitHub-hosted runners, signs in the tag-only `android-preview` environment, checks the certificate and alignment, then publishes the APK and SHA-256 checksum as a prerelease. Only the signing job receives the preview keystore and password. Keep the package ID and signing certificate for future preview updates. The ordinary release environment is separate.
