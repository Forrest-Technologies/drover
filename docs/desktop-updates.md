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
feed that offers an older version in the same channel. A stable install never takes a preview version as its floor:
one an earlier build recorded, in the shared record or in the stable one, is ignored and not imported, so it can
never refuse a legitimate stable release. A manual check reports a stale feed. An automatic check offers
nothing and shows nothing.
A feed this install has never verified is still accepted, including one that is not the newest release published,
because the app has no other copy of a later version. Losing the key still cannot make an already trusted feed expire. A newer offer is recorded before it is offered, and an offer whose version cannot be recorded is refused, so a version signed earlier cannot be accepted again after a restart because the record was lost.

Without both values, a version release still publishes installers but publishes no automatic update feed. A build
without the public value does not show update controls or make update requests. Each release has a signed feed for
each desktop operating system, so a package never offers an installer for a different system.

The app and signed feed keep the full release version, including `-preview.N`. Operating system package metadata
uses numeric versions where required. The numeric revision keeps preview order and reserves its largest value for the
stable package. Debian packages use `~preview.N`, which has the same order.

Each install considers only entries from its own release channel, before the version record or the installer sees
them. An entry is on a channel when the channel its version names matches and any channel the entry declares matches
too. A stable install never takes a preview entry, even one signed by the same key and served under the stable feed
address, so a preview version never becomes the stable record or hides a later stable release.

Nothing an update endpoint sends is read without a limit, because none of it is trusted until its signature is
checked. The signed feed and its detached signature are each read up to 1 MiB, and the preview release list up to
8 MiB. Each read must finish within 30 seconds. A declared length over the limit is refused before the body is read,
and a body that runs past it is refused at that point.

A package download runs on the thread pool, so a callback during the download can never block the call that started
it, and its time limit covers the whole transfer even when the response is already buffered and every read completes
at once. A package download stops
at 512 MiB while it streams. A declared length over the limit is refused before the file is
created. A download that runs past the limit, fails, or is cancelled is stopped and its partial file removed before the
update library checks its signature. A partial file that cannot be removed is reported together with the reason the
download stopped. A download that runs past its time limit is cancelled, and the timeout is reported once the download
has closed and removed its file, waiting at most 10 seconds for that. If it has not stopped by then, or stopping
failed, the timeout and that failure are reported and logged together.

Each download has a new directory of its own inside the private update folder, readable only by the user (mode 0700
outside Windows; inside the user's local application data on Windows). A retry, or a second check, therefore never
shares a file with an earlier download that is still stopping. A download that fails, including a package rejected for
a bad signature, has its attempt directory removed once the update library and the download are done with it, and a
directory that cannot be removed is logged. A download that ran past its time limit is never used again. The update
library's verdict on its file may still arrive while it stops; a rejection that arrives within the 10 second wait is
reported with the timeout, and one that arrives later is logged. Its attempt directory is removed once the download
and the update library are done with it, whatever the library decided about the file. Whether that happened within the
10 second wait is decided once, by whichever comes first. If it came later, the caller is told of the timeout at once,
and the removal is logged with the attempt and how the download ended; a directory that cannot be removed is logged
with why.

Each check removes, as far as it can, entries of the update folder last written more than 24 hours earlier, and any
link it finds there, which is removed and never followed. It never removes an attempt of the same process whose
download has not stopped, or the one that holds a verified package still shown to the person. Another running copy of
Drover may be using a newer attempt, and no download or installer handoff lasts a day, so the age alone bounds what is
left behind without a lock shared between copies.

A check that resumes after a newer check began, after the checks setting changed, or after the app's update service
was disposed, does nothing more: it does not replace the updater, clean the update folder, record a version, or change
what is shown. Once disposed, no new check starts. Turning update checks off or on, or failing to save that setting,
leaves a download or install in progress running and shown, and the account menu keeps showing its progress.

The package file is created only where nothing exists yet, shared with no one. Every file the update library checks (a
fresh download, and the file again before the installer starts) is refused if its path is a link or reparse point, then
opened once; the length comes from that open file, and at most 512 MiB is read from it before the strict Ed25519 check
runs. The package is then read again through a bounded buffer in the same way, and one that fails any of these checks is
never handed to the installer.

## Threat model

In scope: the release feed, its signature, the release list, and the package delivery endpoint, which may send anything
of any size or take any time, and an attacker on the network between Drover and them. The signing key, the channel
filter, the version record, and the size and time limits above are the defenses against them.

Out of scope: another process running as the same user. The update folder sits in that user's application data and is
private to them, and such a process can already replace the installed app itself, so checks against it would add no
protection. The link refusal and the exclusive create above are defense in depth, not a guarantee against a process
that swaps names between a check and an open.

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
