#!/usr/bin/env python3
"""Generate a climate-grouped atlas: 8 Mediterranean, 8 Temperate, 8 Northern.

Each group forces the Caldera Bay "Climate (latitude)" map option, so you can
see the three regimes side by side. Reuses make_viewer's card/CSS/JS.

usage: python3 viewer/make_climate_atlas.py
"""
import glob
import os
import subprocess
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)
MAPS = os.path.join(HERE, "maps")
OW = os.path.join(ROOT, "..", "owmapgen", "owmapgen")
LAB = os.path.join(ROOT, "..", "owmapgen-lab", "scripts")
GAMEDIR = os.path.expanduser(
    "~/Library/Application Support/Steam/steamapps/common/Old World")

sys.path.insert(0, LAB)
sys.path.insert(0, HERE)
from render_pretty import render_pretty            # noqa: E402
import make_viewer as mv                           # noqa: E402

CLIMATES = [
    ("MAP_OPTION_CALDERA_MEDITERRANEAN", "Mediterranean (warm)"),
    ("MAP_OPTION_CALDERA_TEMPERATE", "Temperate"),
    ("MAP_OPTION_CALDERA_NORTHERN", "Northern (cold)"),
]
N = 8

def main():
    os.environ["PATH"] = "/opt/homebrew/bin:" + os.environ.get("PATH", "")
    print("== build ==")
    subprocess.run(["dotnet", "build", "src/CustomMapScript.csproj",
                    f"-p:GameDir={GAMEDIR}", "-v", "quiet", "-nologo"],
                   cwd=ROOT, check=True)
    subprocess.run(["cp", "src/bin/CustomMapScript.dll", "mod/CustomMapScript.dll"],
                   cwd=ROOT, check=True)

    os.makedirs(MAPS, exist_ok=True)
    for f in glob.glob(os.path.join(MAPS, "*.xml")) + glob.glob(os.path.join(MAPS, "*.png")):
        os.remove(f)

    sections = []
    for ci, (opt, label) in enumerate(CLIMATES):
        print(f"== {label}: {N} gens ==")
        cards = []
        for s in range(1, N + 1):
            tmp = os.path.join("/tmp", f"climgen_{ci}_{s}")
            os.makedirs(tmp, exist_ok=True)
            for old in glob.glob(os.path.join(tmp, "*")):
                os.remove(old)
            subprocess.run(
                [OW, "--mod", os.path.join(ROOT, "mod"), "--script", "CalderaBay",
                 "--size", "smallest", "--players", "2", "--seed", str(s),
                 "--mirror",
                 "--map-option", f"MAP_OPTIONS_MULTI_CALDERA_CLIMATE={opt}",
                 "--output", tmp], capture_output=True)
            src = sorted(glob.glob(os.path.join(tmp, "*.xml")))
            if not src:
                continue
            tag = label.split()[0]                       # Mediterranean / Temperate / Northern
            dst = os.path.join(MAPS, f"Caldera_Bay_smallest_{tag}-{s}.xml")
            os.replace(src[0], dst)
            render_pretty(dst, dst.replace(".xml", ".png"), 12, show_resources=False)
            cards.append(mv.card(dst))
        sections.append((label, cards))

    body = []
    for label, cards in sections:
        body.append(f'<h2 class="section">{label} — {len(cards)} gens</h2>')
        body.append(f'<div class="grid">{"".join(cards)}</div>')
    extra = ".section{margin:22px 22px 0;font:600 15px ui-monospace,Menlo,monospace;color:#8fd18f;}"
    doc = f"""<!doctype html><html lang="en"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Caldera Bay — Climate Atlas</title><style>{mv.CSS}{extra}</style></head><body>
<header><h1>Caldera Bay — Climate Atlas</h1>
<div class="sub">8 Mediterranean · 8 Temperate · 8 Northern (same seeds 1–8 per regime) ·
<code>Climate (latitude)</code> map option</div></header>
{''.join(body)}
<div id="lb"><img alt=""></div>
<script>{mv.JS}</script></body></html>"""
    out = os.path.join(HERE, "index.html")
    with open(out, "w") as f:
        f.write(doc)
    print(f"wrote {out} ({sum(len(c) for _, c in sections)} maps)")


if __name__ == "__main__":
    main()
