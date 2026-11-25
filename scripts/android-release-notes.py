import json
import re
import sys

INSTALL = ("Install the APK and sign in with the HTTPS address of your Drover server. See the "
           "[Android guide](https://github.com/Forrest-Technologies/drover/blob/main/docs/android-preview.md) "
           "for server requirements and notification setup.")
PULL = re.compile(r"^\* .+ in https://github\.com/[^/\s]+/[^/\s]+/pull/\d+\s*$")
LEGACY_PREVIEW = re.compile(r"v\d+\.\d+\.\d+-android-preview\.[1-9]\d*")


def channel_of(release):
    tag = release.get("tag_name", "")
    if release.get("draft"):
        return None
    if tag.startswith("android-v"):
        return "preview" if release.get("prerelease") else "stable"
    if LEGACY_PREVIEW.fullmatch(tag):
        return "preview"
    return None


def previous_tag(releases, channel, current):
    if channel not in ("preview", "stable"):
        raise ValueError("Unknown release channel.")
    same = [r for r in releases if r.get("tag_name") != current and channel_of(r) == channel]
    same.sort(key=lambda r: r.get("created_at", ""), reverse=True)
    return same[0]["tag_name"] if same else ""


def body(generated, channel, previous):
    pulls = [line for line in generated.splitlines() if PULL.match(line)]
    if not previous:
        summary = f"First {channel} release in this series."
    elif not pulls:
        summary = f"No merged pull requests since {previous}."
    else:
        count = len(pulls)
        noun = "pull request" if count == 1 else "pull requests"
        summary = f"{count} merged {noun} since {previous}, listed below."
    parts = [summary]
    if previous and generated.strip():
        parts.append(generated.strip())
    parts.append(INSTALL)
    return "\n\n".join(parts) + "\n"


if __name__ == "__main__":
    command = sys.argv[1]
    if command == "previous":
        with open(sys.argv[2], encoding="utf-8") as source:
            print(previous_tag(json.load(source), sys.argv[3], sys.argv[4]))
    elif command == "body":
        with open(sys.argv[2], encoding="utf-8") as source:
            sys.stdout.write(body(source.read(), sys.argv[3], sys.argv[4]))
    else:
        raise SystemExit("Usage: android-release-notes.py previous|body ...")
