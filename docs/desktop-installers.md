# Desktop installers

Each version release includes desktop packages and SHA-256 checksum files. The package filename says `signed`, `signed-notarized`, or `unsigned`. Check that label before installation.

## Windows

Download the x64 MSIX. Open the file and choose **Install**. Windows verifies a signed package before installation. An unsigned dry-run package is for pipeline review and requires local developer installation settings.

## macOS

Download the DMG for the architecture named in its filename. Open it, then open Drover. A production package is signed, notarized, and stapled. An unsigned dry-run package is for pipeline review and macOS will block its normal installation.

## Linux

Download the x64 deb and its optional armored signature. Verify the checksum, then install it.

```bash
sha256sum --check drover-linux-x64-1.2.3-signed.deb.sha256
sudo apt install ./drover-linux-x64-1.2.3-signed.deb
```

The application is available as `drover` and in the desktop application menu. An unsigned package is clearly named and can still be inspected and installed.

## Verify provenance

Each package has a GitHub artifact attestation. Verify it with the GitHub CLI and the repository name.

```bash
gh attestation verify drover-windows-x64-1.2.3-signed.msix --repo Forrest-Technologies/drover
```

## Maintainer release setup

The `release` workflow builds on GitHub-hosted runners. A manual `workflow_dispatch` run builds and attests unsigned packages. It cannot publish a GitHub release. Version tags at commits on `main` may publish releases after the signing jobs pass through the protected `release` environment.

Add these environment secrets when signing material exists.

* `WINDOWS_CERTIFICATE_BASE64`
* `WINDOWS_CERTIFICATE_PASSWORD`
* `MACOS_CERTIFICATE_BASE64`
* `MACOS_CERTIFICATE_PASSWORD`
* `APPLE_NOTARY_KEY_BASE64`
* `APPLE_NOTARY_KEY_ID`
* `APPLE_NOTARY_ISSUER_ID`
* `LINUX_GPG_PRIVATE_KEY_BASE64`
* `LINUX_GPG_PASSPHRASE`

The Windows certificate subject must be `CN=Forrest Technologies`, which matches the MSIX identity. The macOS certificate must contain a Developer ID Application identity. The Apple notary secrets belong to a dedicated App Store Connect API key. Encode each binary certificate or key file as one base64 string.

Protect the `release` environment so only version tags can deploy and a required reviewer must approve access. Pull request jobs and manual dry runs do not enter it. If a platform's complete secret set is missing on a protected tag, that platform publishes an artifact whose filename says `unsigned`.

GitHub registers manual dispatch from the workflow on the default branch. Before this workflow is present there, the first branch dry run may not appear in the workflow picker. After registration, choose the intended branch and enter a numeric three-part version. Never create a public test tag.
