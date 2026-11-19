# Desktop update verification

Desktop updates need a dedicated Ed25519 key pair. Keep `UPDATE_SIGNING_PRIVATE_KEY` in the protected `release`
environment. Set the matching public value as the repository variable `UPDATE_SIGNING_PUBLIC_KEY`. The private key
is separate from every operating system signing credential.

If the private key is lost or exposed, treat it as public. Generate a new pair, store the private key in the `release`
environment, and replace `UPDATE_SIGNING_PUBLIC_KEY`. Publish new installers. An older install keeps the previous
public key and will not accept a feed signed by the new key, so it needs a new package from the release page. Announce
that outside the update feed. A signed feed cannot tell a client to trust a key the client does not already have, and
the feed has no expiry. Ship a newer signed release instead of trying to recall an older file.

Without both values, a version release still publishes installers but publishes no automatic update feed. A build
without the public value does not show update controls or make update requests. Each release has a signed feed for
each desktop operating system, so a package never offers an installer for a different system.

The app and signed feed keep the full release version, including `-preview.N`. Operating system package metadata
uses numeric versions where required. The numeric revision keeps preview order and reserves its largest value for the
stable package. Debian packages use `~preview.N`, which has the same order.

## Manual run

1. Generate a throwaway Ed25519 key pair with the pinned appcast generator.
2. Put the private value in the protected environment and the public value in the repository variable.
3. Publish a preview tag such as `v0.1.1-preview.1` from the protected release path.
4. Install the preceding preview package and open Account, then Updates.
5. Check that the new preview is offered. Choose Download and restart.
6. Confirm the operating system installer opens and the relaunched app reports the new version.
7. Change one byte in a downloaded package and serve that file under a test appcast with its original signature.
   Confirm Drover reports that the package signature is invalid and does not open it.
8. Turn Check for desktop updates off, restart, and confirm no release request is made.

Repeat the install step on Windows and macOS. On Linux, confirm the signature check and that Drover shows the download
link instead of starting a privileged installer. Use only preview tags for this verification. Linux has no trusted
privileged install yet, so it stays manual.
