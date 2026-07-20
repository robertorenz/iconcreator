# IconCreator Studio

A professional multi-resolution icon editor for Windows — a modern, cleaner alternative to Axialis IconWorkshop. Built with **C# / .NET 9 / WPF**.

![IconCreator Studio](docs/screenshot-v1.2.png)

## Why it's better

| | IconCreator Studio | Axialis IconWorkshop |
|---|---|---|
| Price | Free & open | Paid licence |
| UI | Modern dark, DPI-aware | Dated |
| Multi-resolution `.ico` | ✅ 16 → 256 px in one file | ✅ |
| Live per-size thumbnails | ✅ update as you draw | Partial |
| Import any image → all sizes | ✅ one click | ✅ |
| True 32-bit alpha, PNG-compressed 256 px | ✅ | ✅ |
| Cross-tooling | 100% managed, no native deps | Native |

## Features

- **Multi-document tabs (MDI)** — open many icons at once, each in its own tab with its own undo history. `+` for a new document, `Ctrl+Tab` to cycle, `Ctrl+W` to close, middle-click a tab to close it; unsaved tabs show a ● and prompt before closing.
- **Multi-resolution documents** — edit 16, 24, 32, 48, 64, 128 and 256 px slices side by side; tick which ones ship in the exported `.ico`.
- **Full drawing toolset** — pencil, eraser, flood fill (with tolerance), colour picker, line, rectangle / filled rectangle, ellipse / filled ellipse, adjustable brush size — each with a crisp crafted vector icon.
- **Import as a movable layer** — **Import Image** (or a drop onto a raster tab) brings the image in as a floating placement you can **drag, resize (corner handles — hold `Shift` to keep proportions), Fit, Center or Reset** before committing. **Apply** bakes it into the current size, or tick **All sizes** to composite it into every resolution at once.
- **Real alpha** — every pixel is straight-alpha BGRA; optional alpha-blend mode composites brush strokes over existing pixels.
- **Colour picker** — RGBA sliders, hex entry, preset swatches, and a transparency-aware preview.
- **Import & drag-drop** — bring in any PNG/JPG/BMP/GIF/ICO/SVG. Drop onto a **raster** tab to place it on the canvas (resizable) and apply; drop onto a **vector** tab to add it as a resizable element; hold **Ctrl** to import into a brand-new vector tab. SVGs are rasterised from vector data, so they stay sharp.
- **Export** — write a proper multi-image Windows `.ico` (256 px stored PNG-compressed, smaller sizes as 32-bit DIB with an AND mask) or a single `.png`.
- **Recent files** — a **Recent ▾** menu quick-opens the last 20 icons you saved or opened (persisted between sessions).
- **Editor niceties** — zoom 1×–32× via slider, **Ctrl + mouse wheel** (anchored at the cursor) or fit-to-window; pixel grid overlay, transparency checkerboard, unlimited undo/redo, live status bar.
- **Multilingual** — switch the entire UI between **English** and **Español** on the fly via the 🌐 menu; the choice is remembered between sessions. New languages are a small dictionary away.
- **Vector tabs (author SVG)** — tabs can be **raster** *or* **vector**; the toolbars and side panel switch to match the active tab. **✒ New SVG** opens a vector document for drawing true shapes — rectangle, ellipse, line, multi-point path and text, each with fill / stroke / stroke-width. Select, move, resize (corner handles), delete, **Ctrl + wheel to zoom**, then **Save a real `.svg`** or export PNG. **Open** or **drag & drop** an `.svg` to load it straight back into an editable vector tab.
- **Modal dialogs** everywhere (no jarring system alerts) and a native dark title bar.

## Keyboard shortcuts

| Key | Action | Key | Action |
|-----|--------|-----|--------|
| `B` | Pencil | `L` | Line |
| `E` | Eraser | `R` / `Shift+R` | Rectangle / filled |
| `G` | Flood fill | `O` / `Shift+O` | Ellipse / filled |
| `I` | Colour picker | `Ctrl+Z` / `Ctrl+Y` | Undo / redo |
| `Ctrl+N` | New icon | `Ctrl+O` | Open |
| `Ctrl+S` | Save | `Ctrl+W` | Close tab |
| `Ctrl+Tab` | Next tab | `Ctrl+Shift+Tab` | Previous tab |

## Build & run

Requires the **.NET 9 SDK**.

```bash
dotnet build -c Release
dotnet run --project src/IconCreator
```

The compiled app is a single WPF desktop executable (`IconCreator.exe`).

## Project layout

```
src/IconCreator/
├─ Model/         PixelBuffer, IconSlice, IconDocument
├─ Editing/       Drawing primitives (line, rect, ellipse, flood fill)
├─ IO/            IcoEncoder, ImageIO (+SVG raster), SvgWriter/SvgReader, RecentFiles, AppSettings
├─ Localization/  Loc — string tables (English + Español) with runtime switching
├─ Vector/        VShape — vector primitives + transforms + SVG serialisation
├─ Views/         Modal/colour/new-icon dialogs, VectorEditor control, ToolIcons, dark chrome
├─ Theme/         Professional dark palette + control styles
└─ MainWindow     Editor shell, raster/vector tabs, tools, undo/redo, file commands
```

## Verification

A built-in round-trip self-test covers the core pipelines:

```bash
IconCreator.exe --selftest   # writes %TEMP%\iconcreator_selftest.txt
```

It builds a synthetic 16/32/48/256 icon, saves and reloads it (all four frames
verified), rasterises an SVG, authors shapes → `.svg` → parses them back, and
round-trips an embedded `<image>` element.

---

© Reddin Assessments — built with .NET 9 / WPF.
