"""Package the desktop's complete text and Segoe Fluent Icons WOFF2 fonts.

Requires fonttools and brotli. No subsetting or outline edits: user device names
retain the desktop's character coverage. Run from any directory. Use
--icons-only to package the icon face, or --spacing-only to rebuild the blank
spacing glyphs, without rebuilding the large text faces.
"""
import argparse
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen

parser = argparse.ArgumentParser(description=__doc__)
selection = parser.add_mutually_exclusive_group()
selection.add_argument("--icons-only", action="store_true")
selection.add_argument("--spacing-only", action="store_true")
args = parser.parse_args()

root = Path(__file__).resolve().parents[1] / "src" / "LanDeviceMonitor.App"
output = root / "Web" / "fonts"
output.mkdir(parents=True, exist_ok=True)
def package(font, source, filename):
    cmap = font.getBestCmap()
    metrics = dict(font["hmtx"].metrics)
    font.flavor = "woff2"
    path = output / filename
    font.save(path)
    restored = TTFont(path)
    assert restored.getBestCmap() == cmap, f"Character coverage changed: {source}"
    assert restored["hmtx"].metrics == metrics, f"Advance widths changed: {source}"
    assert restored.getGlyphOrder() == font.getGlyphOrder(), f"Glyph order changed: {source}"
    # Preserve the face's source metadata, including family, vendor, version,
    # copyright and licensing records. Only the transport container changes.
    names = lambda f: [(n.nameID, n.platformID, n.platEncID, n.langID, n.string) for n in f["name"].names]
    assert names(restored) == names(font), f"Font metadata changed: {source}"
    for table in ["hhea", "OS/2"]:
        assert restored[table].compile(restored) == font[table].compile(font), f"{table} metrics changed: {source}"
    assert restored["head"].unitsPerEm == font["head"].unitsPerEm, f"Em size changed: {source}"
    if "CFF " in font:
        # The complete CFF table includes every outline, hint and subroutine.
        # Byte equality checks them together without expanding 49,519 glyphs.
        assert restored.getTableData("CFF ") == font.getTableData("CFF "), f"CFF data changed: {source}"
    else:
        original_glyphs, restored_glyphs = font.getGlyphSet(), restored.getGlyphSet()
        for glyph in font.getGlyphOrder():
            original_pen = DecomposingRecordingPen(original_glyphs)
            restored_pen = DecomposingRecordingPen(restored_glyphs)
            original_glyphs[glyph].draw(original_pen)
            restored_glyphs[glyph].draw(restored_pen)
            assert original_pen.value == restored_pen.value, f"Outline changed: {source}, {glyph}"
    print(f"{source} -> {path.name}: {len(cmap)} code points, {path.stat().st_size} bytes")
    print("PASS WOFF2 roundtrip: cmap, all outlines, glyph order, hmtx, hhea, OS/2, em size, name metadata")
    return restored

if not args.icons_only:
    # PingFang SC has no U+2009. Supply only blank spacing glyphs so both
    # platforms keep the one-fifth-em unit separator without a system font.
    builder = FontBuilder(1000, isTTF=True)
    order = [".notdef", "space", "thinspace"]
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({0x20: "space", 0x2009: "thinspace"})
    builder.setupGlyf({name: TTGlyphPen(None).glyph() for name in order})
    builder.setupHorizontalMetrics({".notdef": (0, 0), "space": (250, 0), "thinspace": (200, 0)})
    builder.setupHorizontalHeader(ascent=1060, descent=-340)
    builder.setupNameTable({
        "familyName": "App Text Spacing", "styleName": "Regular",
        "uniqueFontIdentifier": "LanDeviceMonitor-AppTextSpacing-1.0",
        "fullName": "App Text Spacing", "psName": "AppTextSpacing",
        "version": "Version 1.0", "licenseDescription": "GPL-3.0-only",
        "licenseInfoURL": "https://www.gnu.org/licenses/gpl-3.0.html",
    })
    builder.setupOS2(sTypoAscender=860, sTypoDescender=-140, sTypoLineGap=400,
                     usWinAscent=1060, usWinDescent=340, fsType=0)
    builder.setupPost()
    builder.font.recalcTimestamp = False
    builder.font["head"].created = builder.font["head"].modified = 3786912000
    builder.save(root / "Assets" / "Fonts" / "AppTextSpacing.ttf")
    package(TTFont(root / "Assets" / "Fonts" / "AppTextSpacing.ttf", recalcTimestamp=False),
            "AppTextSpacing.ttf", "app-text-spacing.woff2")

if not args.icons_only and not args.spacing_only:
    for style, name in [("Light", "light"), ("Regular", "regular"),
                        ("Medium", "medium"), ("Semibold", "semibold")]:
        source = f"PingFangSC-{style}.otf"
        font = TTFont(root / "Assets" / "Fonts" / source, recalcTimestamp=False)
        package(font, source, f"app-text-{name}.woff2")

if not args.spacing_only:
    icons = TTFont(root / "Assets" / "Fonts" / "SegoeIcons.ttf", recalcTimestamp=False)
    icons = package(icons, "SegoeIcons.ttf", "app-icons.woff2")
    print(f"Icon family: {icons['name'].getDebugName(16) or icons['name'].getDebugName(1)}")
    for point in [0xE713, 0xE74D, 0xE777]:
        glyph = icons.getBestCmap()[point]
        print(f"U+{point:04X}: {glyph}; advance, left bearing = {icons['hmtx'].metrics[glyph]}")
