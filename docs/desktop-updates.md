# Desktop update verification

Desktop updates need a dedicated Ed25519 key pair. Keep `UPDATE_SIGNING_PRIVATE_KEY` in the protected `release`
environment. Set the matching public value as the repository variable `UPDATE_SIGNING_PUBLIC_KEY`. The private key
is separate from every operating system signing credential.

If the private key is lost or exposed, treat it as public. Generate a new pair, store the private key in the `release`
environment, and replace `UPDATE_SIGNING_PUBLIC_KEY`. Publish new installers. An older install keeps the previous
public key and will not accept a feed signed by the new key, so it needs a new package from the release page. Announce
that outside the update feed. A signed feed cannot tell a client to trust a key the client does not already have, and
the feed has no expiry. Ship a newer signed release instead of trying to recall an older file. The app remembers
the newest release version in a signed feed it has verified, one record for each release channel (an install that has the single record an earlier build kept moves it to the channel it checks, so the floor is not lost), and refuses a later
feed that offers an older version in the same channel. A manual check reports a stale feed. An automatic check offers
nothing and shows nothing.
A feed this install has never verified is still accepted, including one that is not the newest release published,
because the app has no other copy of a later version. Losing the key still cannot make an already trusted feed expire.

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

Repeat the install step on Windows and macOS. On Linux, confirm the signature check and that Drover shows the checked
local file instead of starting a privileged installer. Use only preview tags for this verification. Linux has no trusted
privileged install yet, so it stays manual.

## What the install step does on each system

Drover hands a package to the operating system installer only after its own signature check passes. The update library
checks the file again, starts the installer, and only then asks Drover to close its window. A failed start leaves the
window open and shows the problem. Two limits follow from the library, and neither has been run on a real machine yet:

- On macOS the signed file is a disk image. The library opens it and closes Drover, and it does not relaunch for a disk
  image, so the person drags the new app into place and opens it. This is an installer handoff, not an in-place update.
- On Windows the signed file is an MSIX package. The library runs the file by path after Drover exits. If the machine
  does not trust the package, the installer can fail after Drover has closed, and the relaunch step can start the old version.

The tests drive this decision through a recording installer: an install starts only for a verified package on Windows and
macOS, a failed start leaves the window open, and the window closes only after the installer started.

## Run record

Record each manual run in the pull request or the release notes: the date, the operating system, the two package file
names, the installed version, the version offered, and what happened at each step above. No run has been recorded for the
current head.
