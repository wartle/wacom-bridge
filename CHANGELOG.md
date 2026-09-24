# Changelog

All notable changes to **wacom-bridge** are documented here.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] - 2026-09-24

First public release.

### Added

- **Pen to mouse bridge** for the Wacom STU-430. Pen contact becomes Windows left-button
  drag inside a calibrated signature box, so you can sign on mouse-only web signature
  canvases.
- **Clear and Save buttons on the pad screen.** Tapping them clicks the website's own Clear
  and Save buttons and wipes the pad. Save also disables the bridge.
- **Signing area limited to the top of the pad.** The button strip never draws on the
  website. Ink on the pad stays out of the buttons where the firmware supports it.
- **Global hotkeys F5–F10** that work while the browser has focus. The browser doesn't
  receive them, so F5 can no longer reload the page and lose a signature.
  - F5 / F6: set the website's Clear / Save button position
  - F7: wipe the pad
  - F8 / F9: set the signature box corners
  - F10: enable / disable the bridge
  - Esc: quit, from the bridge window only
- **Saved calibration.** The signature box and button positions are stored in
  `wacom-bridge.conf` next to the EXE and reloaded at every start.
- **Live pen diagnostics** in the console title: event count, x, y and pen contact.
- **Test page** (`test/signature-test.html`): a local signature popup with calibration
  corner marks, live readouts, an event log and a Pointer Events mode.
- **64-bit build without admin rights.** Registration-free COM (`app.manifest`) loads the
  x64 Wacom `wgssSTU.dll` from the EXE folder, so `regsvr32` isn't needed.
- `build.ps1` build script, which checks that the Wacom SDK files are present first.
- `release.ps1`: builds a self-contained single-file release zip without the Wacom DLLs.
  No .NET install needed on the target PC.
- Clear startup message that says which Wacom SDK files to copy, and where, when they
  are missing.

### Safety

- The bridge starts disabled every time. The on/off state is never saved.
- Disabling the bridge, tapping a pad button, lifting the pen or quitting always
  releases the left mouse button.

### Notes

- Wacom STU SDK files (`Interop.wgssSTU.dll`, `wgssSTU.dll`) are proprietary and are
  not included. Copy them from `C:\Program Files (x86)\Wacom STU SDK\COM\bin\x64\`.
- Requires Windows 10/11 x64 and a Wacom STU-430. Other STU models may work but have
  not been tested.

[1.0.0]: https://github.com/wartle/wacom-bridge/releases/tag/v1.0.0
