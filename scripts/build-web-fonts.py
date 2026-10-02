"""Package the desktop's Microsoft YaHei UI faces as complete WOFF2 fonts.

Requires fonttools and brotli. No subsetting or outline edits: user device names
retain the desktop's character coverage. Run from any directory.
"""
from pathlib import Path
from fontTools.ttLib import TTCollection, TTFont

root = Path(__file__).resolve().parents[1] / "src" / "LanDeviceMonitor.App"
output = root / "Web" / "fonts"
output.mkdir(parents=True, exist_ok=True)
for source, name in [("msyhl.ttc", "light"), ("msyh.ttc", "regular"), ("msyhbd.ttc", "bold")]:
    collection = TTCollection(root / "Assets" / "Fonts" / source)
    font = next(f for f in collection.fonts
                if (f["name"].getDebugName(16) or f["name"].getDebugName(1)) == "Microsoft YaHei UI")
    cmap = font.getBestCmap()
    metrics = dict(font["hmtx"].metrics)
    font.flavor = "woff2"
    path = output / f"app-text-{name}.woff2"
    font.save(path)
    restored = TTFont(path)
    assert restored.getBestCmap() == cmap, f"Character coverage changed: {source}"
    assert restored["hmtx"].metrics == metrics, f"Advance widths changed: {source}"
    print(f"{source} -> {path.name}: {len(cmap)} code points, {path.stat().st_size} bytes")
