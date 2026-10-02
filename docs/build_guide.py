#!/usr/bin/env python3
"""Build the user guide PDF from docs/USER-GUIDE.md.

    python docs/build_guide.py [--version 0.1.0] [--out dist/MOPAChroma-Atlas-User-Guide.pdf]

docs/USER-GUIDE.md is the authoritative guide: edit that, never the PDF. This script turns it
into HTML (Python-Markdown) and prints the HTML to PDF with a Chromium-family browser in
headless mode: Chrome or Chromium on Linux and macOS (GitHub's runners have Chrome), Microsoft
Edge on Windows. Set BROWSER to a browser's path to choose one. The release workflow runs this
and ships the PDF inside every zip and as a release asset.

Needs: Python 3.9+, `pip install markdown`, and one of those browsers.
"""

from __future__ import annotations

import argparse
import base64
import datetime
import html
import os
import re
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SOURCE = os.path.join(ROOT, "docs", "USER-GUIDE.md")
ICON = os.path.join(ROOT, "src", "MOPAChromaAtlas", "Assets", "icon-256.png")
PROPS = os.path.join(ROOT, "Directory.Build.props")
NAME = "MOPAChroma-Atlas-User-Guide.pdf"

CSS = """
@page { size: A4; margin: 18mm 17mm 20mm 17mm; }
html { font-family: "Segoe UI", "Inter", "Helvetica Neue", "DejaVu Sans", Arial, sans-serif;
       font-size: 10.5pt; line-height: 1.5; color: #1d2026; }
body { margin: 0; }
h1, h2, h3 { color: #182040; line-height: 1.25; break-after: avoid; }
h1 { font-size: 21pt; margin: 0 0 6pt; }
h2 { font-size: 15pt; margin: 22pt 0 6pt; padding-bottom: 3pt; border-bottom: 1.5pt solid #9fb4ff; }
h3 { font-size: 12pt; margin: 15pt 0 4pt; }
h2.part { break-before: page; margin-top: 0; }
p, li { orphans: 3; widows: 3; }
ul, ol { padding-left: 18pt; margin: 4pt 0 8pt; }
li { margin: 2pt 0; }
code { font-family: "Cascadia Mono", Consolas, Menlo, "DejaVu Sans Mono", monospace; font-size: 9pt;
       background: #eef1f8; padding: 0 2pt; border-radius: 2pt; }
table { border-collapse: collapse; width: 100%; margin: 6pt 0 10pt; font-size: 9.5pt; break-inside: auto; }
th, td { border: 0.6pt solid #c4cad6; padding: 4pt 6pt; text-align: left; vertical-align: top; }
th { background: #eef1f8; }
tr { break-inside: avoid; }
hr { display: none; }
a { color: #2b4bb8; text-decoration: none; }
.cover { height: 245mm; display: flex; flex-direction: column; justify-content: center; align-items: center;
         text-align: center; break-after: page; }
.cover img { width: 46mm; height: 46mm; margin-bottom: 10mm; }
.cover .title { font-size: 30pt; font-weight: 700; color: #182040; }
.cover .sub { font-size: 13pt; color: #4b5366; margin-top: 4mm; }
.cover .meta { font-size: 10pt; color: #6b7385; margin-top: 18mm; line-height: 1.7; }
.toc { break-after: page; }
.toc h2 { border: none; }
.toc ul { list-style: none; padding-left: 0; }
.toc li.l3 { padding-left: 14pt; font-size: 9.8pt; }
.toc li.l2 { font-weight: 600; margin-top: 7pt; }
.source-note { font-size: 8.5pt; color: #6b7385; margin-top: 18pt; border-top: 0.6pt solid #c4cad6; padding-top: 6pt; }
"""


def version_from_props() -> str:
    try:
        text = open(PROPS, encoding="utf-8").read()
        m = re.search(r"<Version>([^<]+)</Version>", text)
        return m.group(1).strip() if m else "dev"
    except OSError:
        return "dev"


def find_browser() -> str:
    env = os.environ.get("BROWSER")
    if env and os.path.exists(env):
        return env
    names = ["google-chrome", "google-chrome-stable", "chromium", "chromium-browser", "chrome", "msedge"]
    for n in names:
        p = shutil.which(n)
        if p:
            return p
    candidates = [
        r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
        r"C:\Program Files\Google\Chrome\Application\chrome.exe",
        "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome",
        "/Applications/Microsoft Edge.app/Contents/MacOS/Microsoft Edge",
        "/opt/pw-browsers/chromium/chrome-linux/chrome",
    ]
    for c in candidates:
        if os.path.exists(c):
            return c
    # Playwright's bundled Chromium, wherever it was installed.
    base = os.environ.get("PLAYWRIGHT_BROWSERS_PATH", "")
    if base and os.path.isdir(base):
        for d in sorted(os.listdir(base), reverse=True):
            p = os.path.join(base, d, "chrome-linux", "chrome")
            if d.startswith("chromium-") and os.path.exists(p):
                return p
    raise SystemExit("No Chrome, Chromium or Edge found to print the PDF. Set BROWSER to one's path.")


def slug(text: str) -> str:
    return re.sub(r"[^a-z0-9]+", "-", text.lower()).strip("-")


LIST_ITEM = re.compile(r"^\s*(?:[-*+]|\d+[.)])\s+")


def github_lists(md_text: str) -> str:
    """GitHub starts a list right after a paragraph line; Python-Markdown needs a blank line first."""
    out: list[str] = []
    for line in md_text.replace("\r\n", "\n").split("\n"):
        if LIST_ITEM.match(line) and out and out[-1].strip() and not LIST_ITEM.match(out[-1]) \
                and not out[-1].startswith(("  ", "\t", "|", "#")):
            out.append("")
        out.append(line)
    return "\n".join(out)


def build_html(md_text: str, version: str) -> str:
    import markdown  # pip install markdown

    md_text = github_lists(md_text)

    body = markdown.markdown(md_text, extensions=["tables", "sane_lists", "toc"],
                             extension_configs={"toc": {"slugify": lambda v, _sep: slug(v)}})
    # The Markdown's own title becomes the cover; parts start on a new page.
    body = re.sub(r"<h1[^>]*>.*?</h1>", "", body, count=1, flags=re.S)
    body = re.sub(r'<h2 id="(part-[^"]*)">', r'<h2 id="\1" class="part">', body)

    toc = []
    for level, ident, title in re.findall(r'<h([23])[^>]*id="([^"]+)"[^>]*>(.*?)</h\1>', body, flags=re.S):
        plain = re.sub(r"<[^>]+>", "", title)
        toc.append(f'<li class="l{level}"><a href="#{ident}">{plain}</a></li>')

    icon = ""
    if os.path.exists(ICON):
        icon = '<img src="data:image/png;base64,%s" alt="">' % base64.b64encode(open(ICON, "rb").read()).decode()
    today = datetime.date.today().isoformat()
    return f"""<!doctype html>
<html lang="en"><head><meta charset="utf-8"><title>MOPAChroma Atlas — User Guide</title>
<style>{CSS}</style></head><body>
<div class="cover">{icon}
  <div class="title">MOPAChroma Atlas</div>
  <div class="sub">User Guide</div>
  <div class="meta">Version {html.escape(version)} · {today}<br>
  An Isotope NW tool · free and open source (MIT)<br>
  github.com/LeeGillie/MOPAChromaAtlas</div>
</div>
<div class="toc"><h2>Contents</h2><ul>{''.join(toc)}</ul></div>
{body}
<p class="source-note">This PDF is generated from docs/USER-GUIDE.md in the MOPAChroma Atlas repository,
which is the authoritative version of this guide.</p>
</body></html>"""


def print_pdf(browser: str, html_path: str, out: str) -> None:
    os.makedirs(os.path.dirname(os.path.abspath(out)) or ".", exist_ok=True)
    with tempfile.TemporaryDirectory() as profile:
        args = [browser, "--headless=new", "--disable-gpu", "--no-sandbox", "--no-first-run",
                f"--user-data-dir={profile}", "--no-pdf-header-footer", "--print-to-pdf-no-header",
                f"--print-to-pdf={os.path.abspath(out)}", "file://" + os.path.abspath(html_path).replace("\\", "/")]
        if os.name == "nt":
            args[-1] = "file:///" + os.path.abspath(html_path).replace("\\", "/")
        subprocess.run(args, check=True, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=180)
    if not os.path.exists(out) or os.path.getsize(out) < 10_000:
        raise SystemExit("The browser did not produce the PDF.")


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    ap.add_argument("--version", default=None, help="version printed on the cover (default: Directory.Build.props)")
    ap.add_argument("--out", default=os.path.join(ROOT, "dist", NAME))
    ap.add_argument("--html", help="also keep the intermediate HTML here")
    args = ap.parse_args()

    version = args.version or version_from_props()
    page = build_html(open(SOURCE, encoding="utf-8").read(), version)
    with tempfile.TemporaryDirectory() as tmp:
        html_path = args.html or os.path.join(tmp, "guide.html")
        with open(html_path, "w", encoding="utf-8") as fh:
            fh.write(page)
        print_pdf(find_browser(), html_path, args.out)
    print("%s  %.0f KB" % (args.out, os.path.getsize(args.out) / 1024))
    return 0


if __name__ == "__main__":
    sys.exit(main())
