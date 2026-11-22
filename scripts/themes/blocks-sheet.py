"""Draws the proposed blocks group of issue 783 for each theme and mode, and checks its tones.

The values are in scripts/themes/blocks.json until the owner approves them on issue 783; then they move into the
theme files. The sheet shows a code block, a shell command, a diff and a log line in each theme, drawn the way
the native code block draws them (pull request 695), with every word a labeled sample. The check prints each quiet
tone's lowest contrast on the block surfaces and its hue distance from the accents and the failure color.

Run: python3 scripts/themes/blocks-sheet.py [--out FILE]
"""

import argparse
import html
import json
import math
import os
import pathlib
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))

from color import luminance, ratio  # noqa: E402,F401
from themes import ROOT, load, modes  # noqa: E402

BLOCKS = json.loads((pathlib.Path(__file__).parent / "blocks.json").read_text())
TARGET = ROOT / "docs/design/themes/blocks-sheet.html"
FONTS = ROOT / "src/Drover.Native/Assets/Fonts"
SPACE = [4, 8, 12, 16, 24, 32, 48]
ACCENT_DISTANCE = 45
DANGER_DISTANCE = 30
QUOTED_CHROMA = 0.08
COMMENT_CHROMA = 0.03


def hue(hex_color):
    from color import _oklab  # noqa: PLC0415

    _, a, b = _oklab(hex_color)
    return math.degrees(math.atan2(b, a)) % 360


def chroma(hex_color):
    from color import _oklab  # noqa: PLC0415

    _, a, b = _oklab(hex_color)
    return math.hypot(a, b)


def distance(first, second):
    gap = abs(first - second) % 360
    return min(gap, 360 - gap)


def tones(theme, mode):
    colors = theme[mode]["color"]
    block = BLOCKS[theme["id"]]
    quiet = block.get(mode, {})
    return {
        "keyword": (colors["text"], "heading"),
        "key": (colors["text"], "label"),
        "quoted": (quiet.get("code-quoted", colors["text-2"]), "body"),
        "comment": (quiet.get("code-comment", colors["text-3"]), "body"),
        "number": (colors["text"], "body"),
        "punctuation": (colors["text-2"], "body"),
        "plain": (colors["text"], "body"),
    }


def span(theme, mode, kind, words):
    color, weight = tones(theme, mode)[kind]
    weights = theme["type"]["weights"]
    return f'<span style="color:{color};font-weight:{weights[weight]}">{html.escape(words)}</span>'


def code(theme, mode):
    parts = [
        ("punctuation", "{"), ("plain", "\n  "), ("key", '"bot"'), ("punctuation", ": "), ("quoted", '"Sample bot"'),
        ("punctuation", ","), ("plain", "\n  "), ("key", '"retries"'), ("punctuation", ": "), ("number", "3"),
        ("punctuation", ","), ("plain", "\n  "), ("key", '"watch"'), ("punctuation", ": "), ("keyword", "true"),
        ("plain", "\n"), ("punctuation", "}"),
    ]
    return "".join(span(theme, mode, kind, words) for kind, words in parts)


def shell(theme, mode):
    colors = theme[mode]["color"]
    parts = [("comment", "# sample: rerun the failed tests\n"), ("keyword", "dotnet"), ("plain", " test "),
             ("plain", "--filter "), ("quoted", '"Category=Build"')]
    return f'<span style="color:{colors["text-3"]}">$ </span>' + "".join(span(theme, mode, kind, words) for kind, words in parts)


def diff(theme, mode):
    colors = theme[mode]["color"]
    lines = [("text-3", "@@ -12,3 +12,3 @@"), ("text", " settings: ./build"), ("danger", "-path: ./old/settings.json"),
             ("ok", "+path: ./config/settings.json")]
    return "\n".join(f'<span style="color:{colors[token]}">{html.escape(words)}</span>' for token, words in lines)


def log(theme, mode):
    colors = theme[mode]["color"]
    rows = [("02:10:04", "danger", "error", "Sample: two tests read a file that moved"),
            ("02:10:05", "warn", "warn", "Sample: retrying once"), ("02:10:07", "text-2", "info", "Sample: run ended")]
    return "\n".join(
        f'<span style="color:{colors["text-3"]}">{time}</span> <span style="color:{colors[token]}">{level}</span> '
        f'<span style="color:{colors["text"]}">{html.escape(words)}</span>' for time, token, level, words in rows)


def block(theme, mode, label, body):
    colors = theme[mode]["color"]
    settings = BLOCKS[theme["id"]]
    pad = round(SPACE[settings["padding"] - 1] * theme["density"]["spacing"])
    radius = theme["shape"]["radius"]["card"] if theme["shape"]["corners"] != "faceted" else 10
    wrap = "pre-wrap" if settings["wrap"] else "pre"
    edge = f'{settings["line"]}px solid {colors["line"]}'
    return (
        f'<div class="block" style="background:{colors[settings["surface"]]};border:{edge};border-radius:{radius}px">'
        f'<div class="bar" style="border-bottom:{edge};padding:4px {pad}px;color:{colors["text-2"]}">{label}'
        f'<span style="color:{colors["text-3"]}">{"Wrap on" if settings["wrap"] else "Wrap off"}</span></div>'
        f'<pre style="padding:{pad}px;font-size:{settings["machineSize"]}px;white-space:{wrap}">{body}</pre></div>')


def panel(theme, mode):
    colors = theme[mode]["color"]
    settings = BLOCKS[theme["id"]]
    words = (f'{settings["highlighting"]} highlighting, {settings["surface"]}, {settings["machineSize"]} px, '
             f'{settings["outputLines"]} lines in a card')
    return (
        f'<div class="panel" style="background:{colors["bg-0"]};color:{colors["text"]}">'
        f'<h3>{html.escape(theme["name"])}, {mode} <small style="color:{colors["text-3"]}">{words}</small></h3>'
        f'{block(theme, mode, "JSON", code(theme, mode))}{block(theme, mode, "bash", shell(theme, mode))}'
        f'{block(theme, mode, "Diff", diff(theme, mode))}{block(theme, mode, "Log", log(theme, mode))}</div>')


def check():
    failures = []
    for theme in load():
        settings = BLOCKS[theme["id"]]
        for mode in modes(theme):
            colors = theme[mode]["color"]
            if settings["highlighting"] != "quiet":
                continue
            accents = [hue(colors[name]) for name in ("ember", "ion", "violet")]
            for name, value in settings[mode].items():
                low = min(ratio(value, colors[surface]) for surface in ("bg-1", "bg-2"))
                gap = min(distance(hue(value), accent) for accent in accents)
                danger = distance(hue(value), hue(colors["danger"]))
                strength = chroma(value)
                line = (f"{theme['id']} {mode} {name} {value}: {low:.2f}:1, chroma {strength:.3f}, "
                        f"{gap:.0f} from the accents, {danger:.0f} from danger")
                print(line)
                quoted_wrong = name == "code-quoted" and (
                    gap < ACCENT_DISTANCE or danger < DANGER_DISTANCE or strength > QUOTED_CHROMA)
                comment_wrong = name == "code-comment" and strength > COMMENT_CHROMA
                if low < 4.5 or quoted_wrong or comment_wrong:
                    failures.append(line)
    return failures


def main():
    options = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    options.add_argument("--out", default=str(TARGET))
    target = pathlib.Path(options.parse_args().out).resolve()
    fonts = os.path.relpath(FONTS, target.parent)
    style = (
        f'@font-face{{font-family:"JetBrains Mono";src:url("{fonts}/JetBrainsMono-Regular.ttf");font-weight:400}}'
        f'@font-face{{font-family:"JetBrains Mono";src:url("{fonts}/JetBrainsMono-Medium.ttf");font-weight:500 700}}'
        f'@font-face{{font-family:"Inter";src:url("{fonts}/Inter-Regular.ttf");font-weight:400}}'
        "body{margin:0;background:#000;font:13px Inter,sans-serif}.grid{display:flex;flex-direction:column;gap:8px;padding:8px}.theme{display:flex;gap:8px}"
        ".panel{width:560px;box-sizing:border-box;padding:12px;display:flex;flex-direction:column;gap:8px}.panel h3{margin:0;font-size:14px}"
        ".bar{display:flex;justify-content:space-between;font-size:12px}pre{margin:0;font-family:'JetBrains Mono',monospace;line-height:1.5}"
    )
    panels = "".join(f'<div class="theme">{"".join(panel(theme, mode) for mode in modes(theme))}</div>' for theme in load())
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(
        f'<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Blocks group: samples</title><style>{style}</style>'
        f'</head><body><div class="grid">{panels}</div></body></html>')
    failures = check()
    print(f"wrote {target}; {len(failures)} tones fall short")
    return 1 if failures else 0


if __name__ == "__main__":
    sys.exit(main())
