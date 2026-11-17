# Seamless Mice 0.2

The product is a small utility for matching cursor crossings to the physical arrangement of a desk's displays. Its character is calm, precise and practical. The visual world is a desk-layout instrument; the main moment is the actual, editable monitor arrangement. A cursor crossing the seam between two unequal screens is the logo's identifying detail.

The UI keeps Segoe UI, the teal accent, and the separate editor process. The current editor pass uses a quiet header, flat white measurement and behavior cards, and a canvas that fills the remaining window height so the monitor arrangement stays the focus. Screen numbers use small badges; the selected screen carries the accent border and a soft teal fill, hovered screens use a stronger edge, and keyboard focus has its own visible outline. Controls and labels remain native WinForms/GDI rather than introducing another framework. The screen picker and numeric fields are small owner-drawn controls so the field surfaces follow the selected theme; the rest of the layout remains standard WinForms.

A light and dark palette are available from the header. The first launch follows the Windows app theme; the header button switches explicitly, and the choice is stored in `editor.ini` beside the calibration file. The dark theme also requests a dark DWM title bar on Windows 10/11. Theme colors are centralized in `Theme.cs`.

The layout supports mouse capture, snapping, Escape to cancel a drag, four arrow keys, and Shift for 10 mm steps. Dragging freezes the preview's transform so scale changes cannot move the target under the pointer. Fit view recomputes the transform on demand. Loss of capture ends the drag. No animation or recurring UI timer is used. The canvas caches its fonts, pens and brushes for painting and disposes them with the control.

The logo master is `assets/logo-source.png`, supplied as project artwork. `tools/DrawAssets.cs` resizes it for the larger icon sizes and draws a simplified cursor mark for 16/20/24 px, where the full artwork's motion trails would collapse into noise. `assets/logo.svg` is the flat vector companion. PNG and ICO exports are deterministic from the source.

The logo was drawn as project-native vector geometry in `assets/logo.svg`, with deterministic PNG and nine-size ICO exports from `tools/DrawAssets.cs`. It is embedded in the executable, settings window and installer. Its shape is two unequal display outlines, a short connecting path and a cursor. No image-generation service or additional runtime asset package was used.

The polish workflow follows the unslop skill by Damir Sokolovsky (https://heydamir.design/unslop), licensed CC BY 4.0. Its applicable principles were adapted to an existing Windows desktop interface; no website framework or skill runtime is shipped with the app.
