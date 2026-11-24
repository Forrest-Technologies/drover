"""The character parts of src/Drover.Presentation/Components/BotLook.cs, read from that file so they never drift."""

import pathlib
import re

ROOT = pathlib.Path(__file__).resolve().parents[2]
SOURCE = ROOT / "src/Drover.Presentation/Components/BotLook.cs"


def _array(text, name):
    body = re.search(rf"{name} =\s*\[(.*?)\];", text, re.S).group(1)
    raw = re.findall(r'"""(.*?)"""|"(.*?)"|(string\.Empty)', body, re.S)
    return ["" if empty else (triple or plain) for triple, plain, empty in raw]


_text = SOURCE.read_text()
HEADS = _array(_text, "heads")
VISORS = _array(_text, "visors")
TOPS = _array(_text, "tops")
MARKS = _array(_text, "marks")
TILTS = [int(value) for value in re.search(r"tilts = \[(.*?)\]", _text).group(1).split(",")]


def _mix(value):
    value ^= value >> 16
    value = (value * 0x7FEB352D) & 0xFFFFFFFF
    value ^= value >> 15
    value = (value * 0x846CA68B) & 0xFFFFFFFF
    return value ^ (value >> 16)


def look(seed):
    """The parts a seed picks, as BotLook.From does."""
    mixed = _mix(seed)
    return (HEADS[mixed % 6], VISORS[mixed // 6 % 6], TOPS[mixed // 36 % 6], MARKS[mixed // 216 % 6],
            TILTS[mixed // 1296 % 4])
