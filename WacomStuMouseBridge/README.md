# wacom-bridge

Experimental bridge for a Wacom STU-430 signature pad.

It reads pen data with Wacom's STU SDK and turns pen contact into normal Windows
mouse movement + left-button drag. It is meant for signing on an existing
mouse-driven web signature canvas when you cannot modify the website.

The STU screen shows a signing area with **Clear** and **Save** buttons. Tapping them
clicks the website's own Clear / Save buttons.

## Important

Use this only on a workstation where you are authorized to use OS-level input
emulation. The bridge does not modify any website and does not read browser data,
cookies or page contents. It only emits normal Windows mouse input.

## Requirements

- Windows 10/11, 64-bit
- Wacom STU-430 connected by USB
- [Wacom STU SDK](https://developer.wacom.com/) installed
- .NET 7 SDK or newer

## Wacom SDK files

Wacom's SDK binaries are **not included** in this repository. Copy both x64 files
from your STU SDK install into `WacomStuMouseBridge\lib\`:

    C:\Program Files (x86)\Wacom STU SDK\COM\bin\x64\Interop.wgssSTU.dll
    C:\Program Files (x86)\Wacom STU SDK\COM\bin\x64\wgssSTU.dll

No `regsvr32` is needed. `app.manifest` uses registration-free COM, so the bridge
loads `wgssSTU.dll` from its own folder.

## Build

In PowerShell, from the `WacomStuMouseBridge` folder:

    .\build.ps1

or `dotnet build -c Release`. The EXE is:

    bin\Release\net7.0-windows\WacomStuMouseBridge.exe

Keep `wgssSTU.dll` next to the EXE if you copy it elsewhere.

## Keys

F5–F10 are global hotkeys: they work while the browser has focus, and the browser
does not receive them while the bridge is running.

| Key | Action |
|-----|--------|
| F5  | Capture mouse position as the website's **Clear** button |
| F6  | Capture mouse position as the website's **Save** button |
| F7  | Wipe the ink on the STU screen |
| F8  | Capture mouse position as **top-left** of the signature canvas |
| F9  | Capture mouse position as **bottom-right** of the signature canvas |
| F10 | Enable / disable the bridge |
| Esc | Quit (only in the bridge window) |

## Usage

1. Close Wacom DemoButtons or any other application using the STU.
2. Start `WacomStuMouseBridge.exe`.
3. Open the website's signature popup.
4. Point the mouse at the top-left inside corner of the drawable area and press **F8**,
   then the bottom-right inside corner and press **F9**.
5. Point at the website's Clear button and press **F5**, then its Save button and press **F6**.
6. Press **F10** to enable the bridge and sign on the STU-430.
7. Tap **Save** on the STU. The bridge clicks the website's Save button, wipes the
   STU screen and disables itself. **Clear** clicks the website's Clear button and
   wipes the STU screen.

## Test page

`test/signature-test.html` is a local popup with a mouse-driven signature canvas,
calibration corner marks and an event log. Use it to try the bridge before the
real website.

## Safety behavior

- The bridge is OFF at startup.
- F10 explicitly enables/disables it; tapping Save on the STU disables it.
- Disabling releases the left mouse button if it is still down.
- The STU signing area maps only to the rectangle you calibrate; the button strip
  never draws on the website.

## Troubleshooting

### "No Wacom STU USB device was found"

- Close DemoButtons, unplug/replug the STU, and make sure no other program has it open.

### Nothing happens when using the pen

The console title shows live pen data (`pen events=…  x=…  y=…  sw=…`).

- Count stays at 0: the bridge is not receiving pen data from the tablet.
- Count increases but the cursor does not move: press F10, the bridge starts disabled.

### The signature is offset

Repeat calibration (F8 / F9) on the actual white drawable area, not the whole popup.

### The website does not draw even though the cursor moves

Some signature components use Pointer Events in a way that ignores synthetic mouse
input. Check with the test page's "Use Pointer Events" option. If the site still
ignores it, use the site's supported input method.

## Technical notes

- Wacom flow: `UsbDevices` → `Tablet.usbConnect` → `onPenData`, scaled against
  `ICapability.tabletMaxX/tabletMaxY`.
- `onPenData` is delivered as window messages to the STA main thread, so the main
  loop pumps messages instead of blocking in `Console.ReadKey`.
- The STU screen image is drawn with System.Drawing, converted with
  `ProtocolHelper.flattenMonochrome` and sent with `Tablet.writeImage`.
- Mouse output uses Windows `SetCursorPos` and `SendInput`; hotkeys use `RegisterHotKey`.
