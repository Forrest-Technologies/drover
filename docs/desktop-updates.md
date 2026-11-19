# Desktop update verification

Desktop updates need a dedicated Ed25519 key pair. Keep `UPDATE_SIGNING_PRIVATE_KEY` in the protected `release`
environment. Set the matching public value as the repository variable `UPDATE_SIGNING_PUBLIC_KEY`. The private key
is separate from every operating system signing credential.

Without both values, a version release still publishes installers but publishes no automatic update feed. A build
without the public value does not show update controls or make update requests. Each release has a signed feed for
each desktop operating system, so a package never offers an installer for a different system.

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

Repeat the install step on Windows, macOS, and Linux. Use only preview tags for this verification.
