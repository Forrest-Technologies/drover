"""Reads the theme files in src/Drover.Presentation/Theme/Themes."""

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
FOLDER = ROOT / "src/Drover.Presentation/Theme/Themes"
ORDER = ["front-office", "night-yard", "signal-room", "paper-ledger", "workbench", "blueprint", "quiet-hours", "foundry"]
# The themes of issue 421, which its sample sheet draws.
FIRST_BATCH = ORDER[1:]
SURFACES = ["bg-0", "bg-1", "bg-2", "bg-3"]
TEXT = ["text", "text-2", "text-3"]
ACCENTS = ["ember", "ion", "violet"]
STATUS = ["ok", "warn", "danger"]
HUES = [f"hue-{step}" for step in range(12)]
HEADS = [f"head-{step}" for step in range(12)]
VISORS = ["visor", "visor-lit", "visor-failed"]


def load():
    found = {path.stem: json.loads(path.read_text()) for path in FOLDER.glob("*.json")}
    return [found[name] for name in ORDER if name in found] + [found[name] for name in sorted(found) if name not in ORDER]


def modes(theme):
    first = theme["firstMode"]
    return [first, "light" if first == "dark" else "dark"]


def colors(theme, mode):
    """The colors of a mode. Black is not stored: it is the dark mode, one surface level down, on black."""
    if mode != "black":
        return theme[mode]["color"]
    dark = theme["dark"]["color"]
    return {**dark, "bg-0": "#000000", **{SURFACES[level]: dark[SURFACES[level - 1]] for level in (1, 2, 3)}}


def bubble(theme, mode):
    """The surface of a person's message, as ThemeResolution.Bubble works it out."""
    from color import mix
    found = colors(theme, mode)
    if theme["surfaces"]["conversation"] == "bubbles":
        return mix(found["ember"], found["bg-1"], 0.3)
    return found["bg-2"]
