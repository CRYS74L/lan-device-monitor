"""Package Noto Sans SC variable text and Segoe Fluent Icons for desktop/web.

Requires fonttools and brotli. Preserve complete character coverage and web
variation data; derive the desktop's exact 600 instance from the same source.
Run from any directory. Use --icons-only, --spacing-only or --desktop-only
to rebuild just the corresponding resources.
"""
import argparse
from pathlib import Path
from fontTools.ttLib import TTFont
from fontTools.pens.recordingPen import DecomposingRecordingPen
from fontTools.fontBuilder import FontBuilder
from fontTools.pens.ttGlyphPen import TTGlyphPen
from fontTools.varLib.instancer import instantiateVariableFont

parser = argparse.ArgumentParser(description=__doc__)
selection = parser.add_mutually_exclusive_group()
selection.add_argument("--icons-only", action="store_true")
selection.add_argument("--spacing-only", action="store_true")
selection.add_argument("--desktop-only", action="store_true")
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
    elif "fvar" in font:
        for glyph in font.getGlyphOrder():
            original = font["glyf"][glyph].getCoordinates(font["glyf"])
            converted = restored["glyf"][glyph].getCoordinates(restored["glyf"])
            assert original == converted, f"Outline changed: {source}, {glyph}"
        for table in ["fvar", "avar", "gvar", "HVAR", "STAT", "GPOS", "GSUB", "GDEF", "BASE"]:
            if table in font:
                assert restored.getTableData(table) == font.getTableData(table), f"Variation/layout table changed: {source}, {table}"
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

if not args.icons_only and not args.desktop_only:
    # Noto Sans SC has no U+2009. Supply only blank spacing glyphs so both
    # platforms keep the one-fifth-em unit separator without a system font.
    builder = FontBuilder(1000, isTTF=True)
    order = [".notdef", "space", "thinspace"]
    builder.setupGlyphOrder(order)
    builder.setupCharacterMap({0x20: "space", 0x2009: "thinspace"})
    glyphs = {name: TTGlyphPen(None).glyph() for name in order}
    # OTS requires a nonempty glyf table, even when all encoded characters
    # are blanks. The unencoded missing-glyph marker supplies a valid outline.
    missing = TTGlyphPen(None)
    missing.moveTo((100, 0))
    missing.lineTo((500, 0))
    missing.lineTo((500, 700))
    missing.lineTo((100, 700))
    missing.closePath()
    glyphs[".notdef"] = missing.glyph()
    builder.setupGlyf(glyphs)
    builder.setupHorizontalMetrics({".notdef": (600, 100), "space": (250, 0), "thinspace": (200, 0)})
    text_source = TTFont(root / "Assets" / "Fonts" / "NotoSansSC-VF.ttf", lazy=True)
    builder.setupHorizontalHeader(ascent=text_source["hhea"].ascent, descent=text_source["hhea"].descent)
    builder.setupNameTable({
        "familyName": "App Text Spacing", "styleName": "Regular",
        "uniqueFontIdentifier": "LanDeviceMonitor-AppTextSpacing-1.0",
        "fullName": "App Text Spacing", "psName": "AppTextSpacing",
        "version": "Version 1.0", "licenseDescription": "GPL-3.0-only",
        "licenseInfoURL": "https://www.gnu.org/licenses/gpl-3.0.html",
    })
    builder.setupOS2(sTypoAscender=text_source["OS/2"].sTypoAscender,
                     sTypoDescender=text_source["OS/2"].sTypoDescender,
                     sTypoLineGap=text_source["OS/2"].sTypoLineGap,
                     usWinAscent=text_source["OS/2"].usWinAscent,
                     usWinDescent=text_source["OS/2"].usWinDescent, fsType=0)
    builder.setupPost()
    builder.font.recalcTimestamp = False
    builder.font["head"].created = builder.font["head"].modified = 3786912000
    builder.save(root / "Assets" / "Fonts" / "AppTextSpacing.ttf")
    package(TTFont(root / "Assets" / "Fonts" / "AppTextSpacing.ttf", recalcTimestamp=False),
            "AppTextSpacing.ttf", "app-text-spacing.woff2")

if not args.icons_only and not args.spacing_only:
    source = "NotoSansSC-VF.ttf"
    font = TTFont(root / "Assets" / "Fonts" / source, recalcTimestamp=False)
    # WPF selects named instances from a variable font. This source has no
    # named 600 instance, so provide its exact 600 outlines alongside the VF.
    semibold = instantiateVariableFont(font, {"wght": 600}, inplace=False)
    # A pinned font has no variable axes. Leaving the source STAT fallback
    # and localized Thin names makes DirectWrite misclassify this static face.
    if "STAT" in semibold:
        del semibold["STAT"]
    locales = {(n.platformID, n.platEncID, n.langID) for n in semibold["name"].names}
    names = {1: "Noto Sans SC", 2: "Regular", 4: "Noto Sans SC SemiBold",
             6: "NotoSansSC-SemiBold", 16: "Noto Sans SC", 17: "SemiBold"}
    for locale in locales:
        for identifier, value in names.items():
            semibold["name"].setName(value, identifier, *locale)
    semibold.save(root / "Assets" / "Fonts" / "NotoSansSC-SemiBold.ttf")
    assert semibold.getBestCmap() == font.getBestCmap(), "600 instance character coverage changed"
    assert semibold["OS/2"].usWeightClass == 600, "600 instance weight metadata changed"
    print("PASS desktop 600 instance: full character coverage from the installed variable font")
    if not args.desktop_only:
        package(font, source, "app-text-variable.woff2")

if not args.spacing_only and not args.desktop_only:
    icons = TTFont(root / "Assets" / "Fonts" / "SegoeIcons.ttf", recalcTimestamp=False)
    icons = package(icons, "SegoeIcons.ttf", "app-icons.woff2")
    print(f"Icon family: {icons['name'].getDebugName(16) or icons['name'].getDebugName(1)}")
    for point in [0xE713, 0xE74D, 0xE777]:
        glyph = icons.getBestCmap()[point]
        print(f"U+{point:04X}: {glyph}; advance, left bearing = {icons['hmtx'].metrics[glyph]}")
