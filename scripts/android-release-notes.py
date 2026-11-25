import json
import re
import sys

INSTALL = ("Install the APK and sign in with the HTTPS address of your Drover server. See the "
           "[Android guide](https://github.com/Forrest-Technologies/drover/blob/main/docs/android-preview.md) "
           "for server requirements and notification setup.")
PULL = re.compile(r"^\* .+ in (https://github\.com/[^/\s]+/[^/\s]+/pull/\d+)\s*$")
LEGACY_PREVIEW = re.compile(r"v\d+\.\d+\.\d+-android-preview\.[1-9]\d*")


def flatten_releases(releases):
    if releases and all(isinstance(page, list) for page in releases):
        return [release for page in releases for release in page]
    return releases


def channel_of(release):
    tag = release.get("tag_name") or ""
    if release.get("draft"):
        return None
    if tag.startswith("android-v"):
        return "preview" if release.get("prerelease") else "stable"
    if LEGACY_PREVIEW.fullmatch(tag):
        return "preview"
    return None


def previous_tags(releases, channel, current):
    if channel not in ("preview", "stable"):
        raise ValueError("Unknown release channel.")
    same = [r for r in flatten_releases(releases)
            if (r.get("tag_name") or "") != current and channel_of(r) == channel]
    same.sort(key=lambda r: (r.get("published_at") or "", r.get("created_at") or ""), reverse=True)
    return [r["tag_name"] for r in same if r.get("tag_name")]


def previous_tag(releases, channel, current):
    tags = previous_tags(releases, channel, current)
    return tags[0] if tags else ""


def body(generated, channel, previous):
    pulls = set()
    for line in generated.splitlines():
        match = PULL.match(line)
        if match:
            pulls.add(match.group(1))
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


def fallback():
    return INSTALL + "\n"


if __name__ == "__main__":
    command = sys.argv[1]
    if command == "previous":
        with open(sys.argv[2], encoding="utf-8") as source:
            print(previous_tag(json.load(source), sys.argv[3], sys.argv[4]))
    elif command == "previous-tags":
        with open(sys.argv[2], encoding="utf-8") as source:
            for tag in previous_tags(json.load(source), sys.argv[3], sys.argv[4]):
                print(tag)
    elif command == "body":
        with open(sys.argv[2], encoding="utf-8") as source:
            sys.stdout.write(body(source.read(), sys.argv[3], sys.argv[4]))
    elif command == "fallback":
        sys.stdout.write(fallback())
    else:
        raise SystemExit("Usage: android-release-notes.py previous|previous-tags|body|fallback ...")
