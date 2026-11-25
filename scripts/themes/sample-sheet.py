"""Writes docs/design/themes/sample-sheet.html: every theme of issue 421 in both modes, drawn from its theme file.

The sheet is for the owner's approval on issue 421. It shows the conversation with a running tool and an approval
card, the crew, and the bot editor, at desktop width and at 360 px, and twenty-four characters per theme. Every
name and message in it is a sample and is labeled so. It is not the app: the app draws these pieces natively from
the same files once the pipeline of issue 422 applies them.

Run: python3 scripts/themes/sample-sheet.py [--fonts FOLDER]
FOLDER holds faces a theme names that are not bundled yet (Literata.ttf, AtkinsonHyperlegibleNext.ttf).
"""

import argparse
import html
import os
import pathlib
import re
import sys

sys.path.insert(0, str(pathlib.Path(__file__).parent))

from color import mix  # noqa: E402
from parts import look  # noqa: E402
from themes import FIRST_BATCH, ROOT, load, modes  # noqa: E402

TARGET = ROOT / "docs/design/themes/sample-sheet.html"
FONTS = ROOT / "src/Drover.Native/Assets/Fonts"
FAMILY = {"inter": "Inter", "jetbrains-mono": "JetBrains Mono", "literata": "Literata",
          "atkinson-hyperlegible-next": "Atkinson Hyperlegible Next"}
FACES = {
    "Inter": [("Inter-Regular.ttf", 400), ("Inter-Medium.ttf", 500), ("Inter-SemiBold.ttf", 600)],
    "JetBrains Mono": [("JetBrainsMono-Regular.ttf", 400), ("JetBrainsMono-Medium.ttf", 500)],
}
EXTRA = {"Literata": "Literata.ttf", "Atkinson Hyperlegible Next": "AtkinsonHyperlegibleNext.ttf"}
SPACE = [4, 8, 12, 16, 24, 32, 48]
BOTS = [("Marshal", 9, 0, "waiting", "Waiting for you"), ("Scout", 6, 1, "thinking", ""),
        ("Ledger", 2, 1, "failed", "Failed"), ("Quill", 11, 0, "rest", "New")]


# ---------- characters ----------

def character(theme, mode, seed, hue, size, state="rest", key=""):
    colors = theme[mode]["color"]
    shape = theme["characters"]
    head_path, visor, top, mark, tilt = look(seed)
    head = colors[f"head-{hue}"]
    on_head, dark, lit, failed = colors["on-head"], colors["visor"], colors["visor-lit"], colors["visor-failed"]
    top_color, mark_color = mix(head, dark, 0.78), mix(head, on_head, 0.40)
    ident = f"c{key}{seed}{size}{state}"
    detail = size >= 24

    def drawn(markup, color, dot):
        markup = markup.replace('class="dv-character-line"',
                                f'fill="none" stroke="{color}" stroke-width="{shape["line"]}" stroke-linecap="round"')
        markup = markup.replace('class="dv-character-dot"', f'fill="{dot}"')
        return re.sub(r"<(path|rect|circle)(?![^>]*\b(fill|stroke)=)", rf'<\1 fill="{color}"', markup)

    band = {"rest": dark, "thinking": lit, "writing": lit, "tool": lit, "failed": failed}[state]
    opacity = 0.45 if state == "tool" else 1
    visor_fill = f'<g opacity="{opacity}">{drawn(visor, band, band)}</g>'
    if state != "rest":
        visor_fill = f'{drawn(visor, dark, dark)}{visor_fill}'
    if shape["visor"] == "outlined":
        edge = lit if state in ("thinking", "writing", "tool") else on_head
        outline = re.sub(r'fill="[^"]*"', 'fill="none"', drawn(visor, edge, edge))
        visor_fill += f'<g stroke="{edge}" stroke-width="1.6" opacity="0.9">{outline}</g>'
    if state == "tool":
        visor_fill += f'<rect x="25.6" y="0" width="16" height="64" fill="{on_head}"/>'
    return (
        f'<svg class="head" width="{size}" height="{size}" viewBox="0 0 64 64" aria-hidden="true">'
        f'<defs><clipPath id="h{ident}"><path d="{head_path}"/></clipPath>'
        f'<clipPath id="i{ident}"><path d="{head_path}" transform="matrix(.8 0 0 .8 6.4 6.4)"/></clipPath></defs>'
        f'<g transform="rotate({tilt} 32 36)">{drawn(top, top_color, mark_color) if detail else ""}'
        f'<path d="{head_path}" fill="{head}"/>'
        f'<g clip-path="url(#h{ident})"><rect width="32" height="64" fill="{on_head}" opacity="{shape["facet"]}"/>'
        f'{drawn(mark, mark_color, mark_color) if detail else ""}</g>'
        f'<g clip-path="url(#i{ident})">{visor_fill}</g></g></svg>')


# ---------- one theme and mode as custom properties ----------

def variables(theme, mode):
    colors = theme[mode]["color"]
    kind, density, shape, motion, surfaces = (theme[group] for group in ("type", "density", "shape", "motion", "surfaces"))
    scale = kind["scale"]
    faces = {"interface": FAMILY[kind["interface"]], "machine": FAMILY[kind["machine"]]}
    values = {f"--dv-{name}": value for name, value in colors.items()}
    values.update({f"--dv-space-{step + 1}": f"{max(2, round(base * density['spacing']))}px" for step, base in enumerate(SPACE)})
    values.update({f"--dv-size-{step}": f"{size}px" for step, size in enumerate(scale)})
    values.update({
        "--dv-font-ui": f'"{faces["interface"]}", system-ui, sans-serif',
        "--dv-font-mono": f'"{faces["machine"]}", ui-monospace, monospace',
        "--dv-font-reading": f'"{FAMILY[kind["reading"]]}", Georgia, serif' if kind["reading"] else "var(--dv-font-ui)",
        "--dv-font-heading": "var(--dv-font-mono)" if kind["headings"] == "machine" else "var(--dv-font-ui)",
        "--dv-font-numerals": "var(--dv-font-mono)" if kind["machineNumerals"] else "var(--dv-font-ui)",
        "--dv-weight-body": kind["weights"]["body"], "--dv-weight-label": kind["weights"]["label"],
        "--dv-weight-heading": kind["weights"]["heading"],
        "--dv-leading-text": kind["leading"]["text"], "--dv-leading-heading": kind["leading"]["heading"],
        "--dv-tracking": f'{kind["tracking"]}em', "--dv-tracking-heading": f'{kind["headingTracking"]}em',
        "--dv-measure": f'{kind["measure"]}ch',
        "--dv-control": f'{density["control"]["pointer"]}px', "--dv-row": f'{density["row"]["pointer"]}px',
        "--dv-cut-s": f'{shape["cut"]["control"]}px', "--dv-cut": f'{shape["cut"]["frame"]}px',
        "--dv-radius-control": f'{shape["radius"]["control"]}px', "--dv-radius-card": f'{shape["radius"]["card"]}px',
        "--dv-radius-sheet": f'{shape["radius"]["sheet"]}px', "--dv-border": f'{shape["line"]}px',
        "--dv-bloom": f'{motion["bloom"]}px', "--dv-travel-line": f'{motion["travelLine"]}px',
        "--dv-list": colors["bg-1"] if surfaces["levels"] == 3 else colors["bg-0"],
    })
    return ";".join(f"{name}:{value}" for name, value in values.items())


def yard(theme, mode):
    colors = theme[mode]["color"]
    texture, strength, step = (theme["surfaces"]["yard"][key] for key in ("texture", "strength", "step"))
    line, half = colors["line"].replace("#", "%23"), step / 2
    drawing = {
        "dots": f"<circle cx='{half}' cy='{half}' r='1' fill='{line}' fill-opacity='{strength}'/>",
        "lines": f"<rect y='{step - 1}' width='{step}' height='1' fill='{line}' fill-opacity='{strength}'/>",
        "grid": (f"<rect y='{step - 1}' width='{step}' height='1' fill='{line}' fill-opacity='{strength}'/>"
                 f"<rect x='{step - 1}' width='1' height='{step}' fill='{line}' fill-opacity='{strength}'/>"),
    }.get(texture)
    if drawing is None:
        return "none"
    return f"url(\"data:image/svg+xml,<svg xmlns='http://www.w3.org/2000/svg' width='{step}' height='{step}'>{drawing}</svg>\")"


# ---------- interface pieces, with sample words ----------

def frame(theme, content, kind="card", extra="", style=""):
    corners = theme["shape"]["corners"]
    registration = " registration" if theme["shape"]["registration"] else ""
    return f'<div class="frame {kind} {corners}{registration} {extra}" style="{style}">{content}</div>'


def crew(theme, mode):
    rows = []
    for index, (name, hue, depth, state, word) in enumerate(BOTS):
        word_class = {"waiting": "warn", "failed": "danger"}.get(state, "")
        live = state == "thinking"
        head = character(theme, mode, index * 7 + 3, hue, 32, "thinking" if live else ("failed" if state == "failed" else "rest"), "crew")
        light = ' live-head' if live and theme["motion"]["bloom"] > 0 else ""
        said = f'<span class="word {word_class}">{word}</span>' if word else ('<span class="word ion">Working</span>' if live and theme["motion"]["live"] == "fade" else "")
        rows.append(frame(theme, f'<span class="headbox{light}">{head}</span><span class="name">{name}</span>{said}',
                          "row", f"depth{depth}{' chosen' if index == 0 else ''}"))
    return f'<section class="crew"><h3>Crew</h3>{"".join(rows)}</section>'


def conversation(theme, mode, narrow):
    head = character(theme, mode, 3, 9, 48, "rest", "talk")
    fade = theme["motion"]["live"] == "fade"
    travel = '<div class="travel full"></div>' if fade else '<div class="travel"></div>'
    tool = frame(theme, f'{travel}<div class="tool-title"><span>Ran a command</span><span class="state ion">Running</span></div>'
                        '<code>dotnet test --filter Build</code>', "card tool")
    done = frame(theme, '<div class="tool-title"><span>Read a file</span><span class="state ok">Done</span></div>'
                        '<code>logs/build-0412.txt</code>', "card tool done")
    ask = frame(theme, '<p class="strong">Marshal asks to run a command</p><code>git revert 4f2a9c1 --no-edit</code>'
                       '<div class="actions">' + frame(theme, "Approve", "control primary") + frame(theme, "Deny", "control") + '</div>',
                "card ask")
    composer = frame(theme, '<span class="placeholder">Write a message</span>' + frame(theme, "Send", "control primary focus"), "card composer")
    return (
        f'<section class="talk"><header>{frame(theme, head, "plate")}<h2>Marshal</h2><span class="time">08:14</span></header>'
        '<div class="you">' + frame(theme, "Can you check last night's build and tell me what failed?", "bubble") + '</div>'
        '<div class="reply"><p>Two tests failed in the build from 02:10. Both read the same settings file, which a change '
        'the evening before had moved. I can put it back, or point the tests at the new place.</p></div>'
        f'{done}{tool}{ask}{composer}</section>')


def editor(theme):
    def field(label, value, tall=False):
        return f'<label>{label}</label>' + frame(theme, value, f"control field{' tall' if tall else ''}")
    sections = "".join(frame(theme, f'<span class="label">{name}</span><span class="muted">{summary}</span>', "control section")
                       for name, summary in [("Model", "Sample model"), ("Tools", "4 tools, 1 asks first"),
                                              ("Rules", "No rules"), ("Memory", "On")])
    return (f'<section class="editor"><h3>Marshal</h3>{field("Name", "Marshal")}'
            f'{field("What this bot is for", "Watches the builds and reports what broke.", True)}'
            f'{field("Instructions", "Read the newest build log first. Ask before changing anything.", True)}{sections}</section>')


def screen(theme, mode, narrow):
    style = html.escape(f'{variables(theme, mode)};--yard:{yard(theme, mode)}')
    if narrow:
        return (f'<div class="screens" style="{style}">'
                f'<div class="phone yard">{conversation(theme, mode, True)}</div>'
                f'<div class="phone list">{crew(theme, mode)}</div>'
                f'<div class="phone yard">{editor(theme)}</div></div>')
    return (f'<div class="desktop" style="{style}"><div class="list">{crew(theme, mode)}</div>'
            f'<div class="yard">{conversation(theme, mode, False)}</div><div class="rail">{editor(theme)}</div></div>')


def characters(theme, mode):
    style = html.escape(variables(theme, mode))
    heads = "".join(f'<span class="cell">{character(theme, mode, seed, seed % 12, 48, "rest", "set")}</span>' for seed in range(24))
    states = "".join(f'<span class="cell state-cell">{character(theme, mode, 5, 8, 48, state, "st")}'
                     f'{character(theme, mode, 5, 8, 20, state, "sm")}<small>{state}</small></span>'
                     for state in ("rest", "thinking", "writing", "tool", "failed"))
    return f'<div class="characters" style="{style}"><div class="set">{heads}</div><div class="set">{states}</div></div>'


CSS = """
body{margin:0;background:#000;color:#ddd;font:14px/1.5 Inter,system-ui,sans-serif}
.sheet-head{padding:24px 32px;background:#111}.sheet-head p{max-width:72ch}
.theme{padding:24px 32px;border-top:1px solid #333}.theme>h1{margin:0;font-size:24px}
.sample-label{display:inline-block;margin-left:8px;font-size:12px;color:#000;background:#ddd;padding:0 6px}
.mode{margin:16px 0 32px}.mode>h2{font-size:16px;margin:8px 0}
.desktop,.screens,.characters{font-family:var(--dv-font-ui);font-size:var(--dv-size-2);line-height:var(--dv-leading-text);
 letter-spacing:var(--dv-tracking);color:var(--dv-text);background:var(--dv-bg-0);font-weight:var(--dv-weight-body)}
.desktop{display:grid;grid-template-columns:288px 1fr 400px;width:1280px;min-height:760px}
.screens{display:flex;gap:16px;padding:16px;background:#000}
.phone{width:360px;min-height:720px;background:var(--dv-bg-0);overflow:hidden}
.yard{background-color:var(--dv-bg-0);background-image:var(--yard)}
.list{background:var(--dv-list);border-right:var(--dv-border) solid var(--dv-line)}
.rail{background:var(--dv-bg-1);border-left:var(--dv-border) solid var(--dv-line)}
section{display:flex;flex-direction:column;gap:var(--dv-space-3);padding:var(--dv-space-4)}
h2,h3{font-family:var(--dv-font-heading);font-weight:var(--dv-weight-heading);letter-spacing:var(--dv-tracking-heading);
 line-height:var(--dv-leading-heading);margin:0}h2{font-size:var(--dv-size-4)}h3{font-size:var(--dv-size-3)}
.talk header{display:flex;align-items:center;gap:var(--dv-space-3)}
.time,.muted,.word{color:var(--dv-text-3);font-size:var(--dv-size-1)}.time{font-family:var(--dv-font-numerals);margin-left:auto}
.reply p{font-family:var(--dv-font-reading);max-width:var(--dv-measure);margin:0}
.you{display:flex;justify-content:flex-end}
code{font-family:var(--dv-font-mono);font-size:var(--dv-size-1);color:var(--dv-text-2);letter-spacing:0}
.frame{position:relative;background:var(--dv-bg-1);border:var(--dv-border) solid var(--dv-line);padding:var(--dv-space-3) var(--dv-space-4)}
.frame.bubble{background:var(--dv-bg-2);max-width:80%}
.frame.plate{padding:4px;background:var(--dv-bg-2);line-height:0}
.frame.control{min-height:var(--dv-control);box-sizing:border-box;padding:var(--dv-space-1) var(--dv-space-3);display:flex;
 align-items:center;gap:var(--dv-space-2);border-color:var(--dv-text-3);background:var(--dv-bg-1);font-weight:var(--dv-weight-label)}
.frame.primary{background:var(--dv-ember);border-color:var(--dv-ember);color:var(--dv-on-accent)}
.frame.focus{outline:2px solid var(--dv-ember);outline-offset:2px}
.frame.field{font-weight:var(--dv-weight-body)}.frame.tall{min-height:calc(var(--dv-control) * 2);align-items:flex-start}
.frame.section{justify-content:space-between}
.frame.row{min-height:var(--dv-row);padding:var(--dv-space-2) var(--dv-space-3);display:flex;align-items:center;gap:var(--dv-space-3)}
.frame.row.depth1{margin-left:var(--dv-space-5)}.frame.row.chosen{background:var(--dv-bg-3)}
.row .name{font-weight:var(--dv-weight-label)}.row .word{margin-left:auto}
.warn{color:var(--dv-warn)!important}.danger{color:var(--dv-danger)!important}.ion{color:var(--dv-ion)!important}.ok{color:var(--dv-ok)!important}
.tool-title{display:flex;justify-content:space-between;font-weight:var(--dv-weight-label)}
.state{font-weight:var(--dv-weight-body);font-size:var(--dv-size-1)}
.tool.done{border-top-color:var(--dv-ok)}
.ask{border-color:var(--dv-warn)}.ask .strong{margin:0;font-weight:var(--dv-weight-label)}
.actions{display:flex;gap:var(--dv-space-2);margin-top:var(--dv-space-2)}
.composer{display:flex;justify-content:space-between;align-items:center;background:var(--dv-bg-1)}
.placeholder{color:var(--dv-text-3)}label{color:var(--dv-text-2);font-size:var(--dv-size-1);font-weight:var(--dv-weight-label);margin-bottom:-8px}
.travel{position:absolute;top:-1px;left:30%;width:25%;height:var(--dv-travel-line);background:var(--dv-ion);box-shadow:0 0 var(--dv-bloom) var(--dv-ion)}
.travel.full{left:0;width:100%;box-shadow:none;opacity:.6}
.headbox{line-height:0}.live-head .head{filter:drop-shadow(0 0 calc(var(--dv-bloom) / 2) var(--dv-ion))}
.faceted{border:none;background:var(--dv-line);padding:0}
.faceted>*{position:relative}
.frame.faceted{clip-path:polygon(var(--cut-now) 0,100% 0,100% calc(100% - var(--cut-now)),calc(100% - var(--cut-now)) 100%,0 100%,0 var(--cut-now));
 --cut-now:var(--dv-cut);padding:var(--dv-space-3) var(--dv-space-4);background:var(--dv-bg-1);
 box-shadow:inset 0 0 0 var(--dv-border) var(--dv-line)}
.frame.faceted.control{--cut-now:var(--dv-cut-s);box-shadow:inset 0 0 0 var(--dv-border) var(--dv-text-3);padding:var(--dv-space-1) var(--dv-space-3)}
.frame.faceted.primary{background:var(--dv-ember);box-shadow:none}
.frame.faceted.bubble{background:var(--dv-bg-2)}.frame.faceted.plate{background:var(--dv-bg-2);padding:4px}
.frame.faceted.row{padding:var(--dv-space-2) var(--dv-space-3)}.frame.faceted.row.chosen{background:var(--dv-bg-3)}
.frame.faceted.done{box-shadow:inset 0 0 0 var(--dv-border) var(--dv-line),inset 0 2px 0 0 var(--dv-ok)}
.frame.faceted.ask{box-shadow:inset 0 0 0 var(--dv-border) var(--dv-warn)}
.frame.faceted.focus{outline:none;box-shadow:inset 0 0 0 2px var(--dv-ember),inset 0 0 0 4px var(--dv-on-accent)}
.square .frame,.frame.square{border-radius:var(--dv-radius-card)}.frame.square.control{border-radius:var(--dv-radius-control)}
.frame.ticked{border-radius:0}
.frame.ticked::before,.frame.ticked::after{content:"";position:absolute;width:var(--dv-cut);height:var(--dv-cut);pointer-events:none}
.frame.ticked::before{top:-1px;left:-1px;border-top:2px solid var(--dv-text-3);border-left:2px solid var(--dv-text-3)}
.frame.ticked::after{bottom:-1px;right:-1px;border-bottom:2px solid var(--dv-text-3);border-right:2px solid var(--dv-text-3)}
.frame.ticked.control::before,.frame.ticked.control::after{width:var(--dv-cut-s);height:var(--dv-cut-s)}
.frame.ticked.registration::before{top:-5px;left:-5px}.frame.ticked.registration::after{bottom:-5px;right:-5px}
.characters{padding:16px;display:flex;flex-direction:column;gap:16px}
.set{display:flex;flex-wrap:wrap;gap:12px}.cell{background:var(--dv-bg-1);padding:6px;line-height:0}
.state-cell{display:flex;align-items:center;gap:8px;line-height:1.2;padding:8px 12px}.state-cell small{color:var(--dv-text-2)}
"""


def page(fonts, target):
    bundled = os.path.relpath(FONTS, target.parent)
    faces = [f'@font-face{{font-family:"{family}";src:url("{bundled}/{file}");font-weight:{weight}}}'
             for family, files in FACES.items() for file, weight in files]
    if fonts:
        faces += [f'@font-face{{font-family:"{family}";src:url("{pathlib.Path(fonts).resolve() / file}");font-weight:100 900}}'
                  for family, file in EXTRA.items()]
    parts = [
        '<!doctype html><html lang="en"><head><meta charset="utf-8"><title>Drover themes: sample sheet</title>',
        f'<style>{"".join(faces)}{CSS}</style></head><body>',
        '<div class="sheet-head"><h1>Drover themes: sample sheet <span class="sample-label">Samples</span></h1>',
        '<p>Every theme in both modes, drawn from its file in <code>src/Drover.Presentation/Theme/Themes</code>. '
        'All names, messages and commands here are samples. This page is a drawing for approval, not the app; the '
        'app draws the same pieces natively once issue 422 applies the files. Live states are shown as one still frame.</p></div>',
    ]
    for theme in (theme for theme in load() if theme["id"] in FIRST_BATCH):
        parts.append(f'<div class="theme"><h1>{html.escape(theme["name"])}<span class="sample-label">Sample</span></h1>'
                     f'<p>{html.escape(theme["intent"])}</p>')
        for mode in modes(theme):
            parts.append(f'<div class="mode"><h2>{theme["name"]}, {mode}, desktop</h2>{screen(theme, mode, False)}'
                         f'<h2>{theme["name"]}, {mode}, 360 px</h2>{screen(theme, mode, True)}'
                         f'<h2>{theme["name"]}, {mode}, characters</h2>{characters(theme, mode)}</div>')
        parts.append("</div>")
    parts.append("</body></html>")
    return "".join(parts)


def main():
    options = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    options.add_argument("--fonts", help="a folder with faces that are not bundled yet")
    options.add_argument("--out", default=str(TARGET))
    arguments = options.parse_args()
    target = pathlib.Path(arguments.out)
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(page(arguments.fonts, target.resolve()))
    print(f"wrote {target}")


if __name__ == "__main__":
    main()
