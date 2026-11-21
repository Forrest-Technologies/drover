"""Reads the theme files in src/Drover.Presentation/Theme/Themes."""

import json
import pathlib

ROOT = pathlib.Path(__file__).resolve().parents[2]
FOLDER = ROOT / "src/Drover.Presentation/Theme/Themes"
ORDER = ["night-yard", "signal-room", "paper-ledger", "workbench", "blueprint", "quiet-hours", "foundry"]
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
