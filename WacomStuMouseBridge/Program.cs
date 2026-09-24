using System;
using System.Runtime.InteropServices;
using System.Threading;
using wgssSTU;

namespace WacomStuMouseBridge;

internal static class Program
{
    private static Tablet? _tablet;
    private static ICapability? _capability;
    private static ProtocolHelper? _protocolHelper;
    private static PadScreen? _pad;
    private static bool _padIsColor;

    private static volatile bool _enabled = false;
    private static volatile bool _penDown = false;

    // Default calibration rectangle. Use F8/F9 to overwrite.
    private static int _left = 300;
    private static int _top = 300;
    private static int _right = 900;
    private static int _bottom = 600;

    // Screen positions of the website's own Clear / Save buttons (F5 / F6).
    // Tapping Clear / Save on the STU clicks these.
    private static POINT? _webClearButton;
    private static POINT? _webSaveButton;

    // Pen state for STU button taps. Set by OnPenData, handled in the main loop,
    // because redrawing the STU screen from inside the pen event is not safe.
    private static bool _penTouching;
    private static bool _touchStartedOutsideSignArea;
    private static int _pendingPadButton;

    private static readonly object Sync = new();

    // Diagnostics shown in the console title.
    private static long _penEvents;
    private static volatile int _lastPenX, _lastPenY, _lastPenSw;
    private static long _lastTitleUpdate;

    // Global hotkeys, so the F-keys work while the browser has focus.
    private static readonly ConsoleKey[] HotKeys =
    {
        ConsoleKey.F5, ConsoleKey.F6, ConsoleKey.F7, ConsoleKey.F8, ConsoleKey.F9, ConsoleKey.F10,
    };

    [STAThread]
    private static void Main()
    {
        Console.Title = "Wacom STU Mouse Bridge";
        Console.WriteLine("Wacom STU-430 -> Windows Mouse Bridge");
        Console.WriteLine("--------------------------------------");
        Console.WriteLine("F-keys work from any window (the browser does not receive them):");
        Console.WriteLine("F5  = capture current mouse position as the website's CLEAR button");
        Console.WriteLine("F6  = capture current mouse position as the website's SAVE button");
        Console.WriteLine("F7  = erase the signature on the STU screen");
        Console.WriteLine("F8  = capture current mouse position as TOP-LEFT of the signature area");
        Console.WriteLine("F9  = capture current mouse position as BOTTOM-RIGHT of the signature area");
        Console.WriteLine("F10 = enable/disable bridge");
        Console.WriteLine("ESC = quit (only in this window)");
        Console.WriteLine();

        Console.WriteLine("Connect the STU-430 directly by USB.");
        Console.WriteLine("Close Wacom DemoButtons before continuing.");
        Console.WriteLine();

        try
        {
            ConnectTablet();
        }
        catch (Exception ex)
        {
            Console.WriteLine();
            Console.WriteLine("ERROR: Could not connect to the STU tablet.");
            Console.WriteLine(ex.Message);
            Console.WriteLine();
            Console.WriteLine("Press any key to exit...");
            Console.ReadKey(true);
            return;
        }

        Console.WriteLine();
        Console.WriteLine("Connected.");
        LoadSettings();

        Console.WriteLine("Calibrate the DOH signature popup (saved automatically):");
        Console.WriteLine("1) Put the mouse at the TOP-LEFT of the signature drawing area, press F8.");
        Console.WriteLine("2) Put the mouse at the BOTTOM-RIGHT of the signature drawing area, press F9.");
        Console.WriteLine("3) Put the mouse on the website's Clear button, press F5.");
        Console.WriteLine("4) Put the mouse on the website's Save button, press F6.");
        Console.WriteLine("5) Press F10 to ENABLE, then sign on the STU-430.");
        Console.WriteLine("6) Tap Clear or Save on the STU screen. Save also disables the bridge.");
        Console.WriteLine();

        RegisterHotKeys();
        try
        {
            RunHotkeyLoop();
        }
        finally
        {
            UnregisterHotKeys();
            DisconnectTablet();
        }
    }

    private static void ConnectTablet()
    {
        var devices = new UsbDevices();

        if (devices.Count == 0)
            throw new InvalidOperationException("No Wacom STU USB device was found.");

        var device = devices[0];

        _tablet = new Tablet();

        var ec = _tablet.usbConnect(device, true);
        if (ec.value != 0)
            throw new InvalidOperationException($"Wacom usbConnect failed: {ec.message} (code {ec.value})");

        _capability = _tablet.getCapability();
        var info = _tablet.getInformation();

        _protocolHelper = new ProtocolHelper();
        _padIsColor = _protocolHelper.encodingFlagSupportsColor(
            _protocolHelper.simulateEncodingFlag(device.idProduct, ReadEncodingFlag(_capability)));
        _pad = new PadScreen(_capability.screenWidth, _capability.screenHeight);

        _tablet.onPenData += new ITabletEvents2_onPenDataEventHandler(OnPenData);

        DrawPadScreen();

        Console.WriteLine($"Tablet: {info.modelName}");
        Console.WriteLine($"Tablet coordinate max: {_capability.tabletMaxX} x {_capability.tabletMaxY}");
        Console.WriteLine($"LCD: {_capability.screenWidth} x {_capability.screenHeight} ({(_padIsColor ? "colour" : "monochrome")})");
    }

    private static void LoadSettings()
    {
        try
        {
            var s = BridgeSettings.Load(out bool loaded);
            _left = s.Left;
            _top = s.Top;
            _right = s.Right;
            _bottom = s.Bottom;
            NormalizeRectangle();
            _webClearButton = s.ClearButton is { } c ? new POINT { X = c.X, Y = c.Y } : null;
            _webSaveButton = s.SaveButton is { } v ? new POINT { X = v.X, Y = v.Y } : null;

            if (!loaded)
            {
                Console.WriteLine($"No saved calibration yet ({BridgeSettings.FilePath}).");
                return;
            }

            Console.WriteLine($"Loaded calibration from {BridgeSettings.FilePath}:");
            Console.WriteLine($"  Signature area = ({_left},{_top}) -> ({_right},{_bottom})");
            Console.WriteLine($"  Website Clear  = {FormatPoint(_webClearButton)}");
            Console.WriteLine($"  Website Save   = {FormatPoint(_webSaveButton)}");
            Console.WriteLine("Only recalibrate if the browser window or popup has moved.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not read {BridgeSettings.FilePath}: {ex.Message}");
        }
        finally
        {
            Console.WriteLine();
        }
    }

    private static void SaveSettings()
    {
        try
        {
            new BridgeSettings
            {
                Left = _left,
                Top = _top,
                Right = _right,
                Bottom = _bottom,
                ClearButton = _webClearButton is { } c ? (c.X, c.Y) : null,
                SaveButton = _webSaveButton is { } v ? (v.X, v.Y) : null,
            }.Save();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not save {BridgeSettings.FilePath}: {ex.Message}");
        }
    }

    private static string FormatPoint(POINT? p) => p is { } v ? $"({v.X}, {v.Y})" : "not set";

    private static byte ReadEncodingFlag(ICapability capability)
    {
        // Older firmware has no encodingFlag; simulateEncodingFlag then infers it from the product id.
        try { return (capability as ICapability2)?.encodingFlag ?? 0; }
        catch { return 0; }
    }

    private static void RunHotkeyLoop()
    {
        while (true)
        {
            // The STU Tablet is an apartment-threaded COM object: its onPenData events
            // are delivered as window messages to this STA thread. Console.ReadKey would
            // block without pumping them, so wait for messages and poll the keyboard.
            PumpMessages();
            HandlePendingPadButton();
            UpdatePenStatusTitle();

            if (!Console.KeyAvailable)
            {
                MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 15, QS_ALLINPUT, MWMO_INPUTAVAILABLE);
                continue;
            }

            var key = Console.ReadKey(true).Key;

            if (key == ConsoleKey.Escape)
                return;

            // Only reached for F-keys whose global registration failed.
            HandleCommand(key);
        }
    }

    private static void HandleCommand(ConsoleKey key)
    {
        if (key == ConsoleKey.F5)
        {
            if (GetCursorPos(out POINT p))
            {
                _webClearButton = p;
                SaveSettings();
                Console.WriteLine($"Website CLEAR button set to ({p.X}, {p.Y})");
            }
        }
        else if (key == ConsoleKey.F6)
        {
            if (GetCursorPos(out POINT p))
            {
                _webSaveButton = p;
                SaveSettings();
                Console.WriteLine($"Website SAVE button set to ({p.X}, {p.Y})");
            }
        }
        else if (key == ConsoleKey.F7)
        {
            DrawPadScreen();
            Console.WriteLine("STU screen cleared.");
        }
        else if (key == ConsoleKey.F8)
        {
            if (GetCursorPos(out POINT p))
            {
                _left = p.X;
                _top = p.Y;
                SaveSettings();
                Console.WriteLine($"TOP-LEFT set to ({_left}, {_top})");
            }
        }
        else if (key == ConsoleKey.F9)
        {
            if (GetCursorPos(out POINT p))
            {
                _right = p.X;
                _bottom = p.Y;
                NormalizeRectangle();
                SaveSettings();
                Console.WriteLine($"BOTTOM-RIGHT set. Rectangle = ({_left},{_top}) -> ({_right},{_bottom})");
            }
        }
        else if (key == ConsoleKey.F10)
        {
            SetEnabled(!_enabled);
        }
    }

    private static void SetEnabled(bool enabled)
    {
        _enabled = enabled;

        // Safety: make sure no mouse button is left held when disabling.
        if (!_enabled)
            ReleaseMouse();

        Console.WriteLine(_enabled
            ? "BRIDGE ENABLED - STU pen now controls the calibrated signature area."
            : "BRIDGE DISABLED.");
    }

    private static void HandlePendingPadButton()
    {
        var button = (PadButton)Interlocked.Exchange(ref _pendingPadButton, (int)PadButton.None);
        if (button == PadButton.None)
            return;

        ReleaseMouse();

        var webButton = button == PadButton.Clear ? _webClearButton : _webSaveButton;
        string name = button == PadButton.Clear ? "Clear" : "Save";

        if (!_enabled)
            Console.WriteLine($"STU {name} tapped (bridge is off, website not clicked).");
        else if (webButton is null)
            Console.WriteLine($"STU {name} tapped, but the website {name} button is not set (press {(button == PadButton.Clear ? "F5" : "F6")}).");
        else
        {
            ClickAt(webButton.Value);
            Console.WriteLine($"STU {name} tapped -> clicked website {name} button.");
        }

        DrawPadScreen();

        if (button == PadButton.Save && _enabled)
            SetEnabled(false);
    }

    /// <summary>Draws the blank signing area plus Clear / Save buttons, which also wipes any ink.</summary>
    private static void DrawPadScreen()
    {
        if (_tablet is null || _pad is null || _protocolHelper is null)
            return;

        lock (Sync)
        {
            try
            {
                byte[] png = _pad.RenderPng();
                ushort w = (ushort)_pad.Width, h = (ushort)_pad.Height;

                Array data;
                encodingMode mode;
                if (_padIsColor)
                {
                    data = _protocolHelper.flattenColor16_565(png, w, h);
                    mode = encodingMode.EncodingMode_16bit;
                }
                else
                {
                    data = _protocolHelper.flattenMonochrome(png, w, h);
                    mode = encodingMode.EncodingMode_1bit;
                }

                _tablet.setInkingMode(0x00);
                _tablet.writeImage((byte)mode, data);
                RestrictInkToSignArea();
                // 0x01 shows ink on the STU LCD while signing.
                _tablet.setInkingMode(0x01);
            }
            catch (Exception ex)
            {
                // Fall back to a plain blank screen so signing still works.
                Console.WriteLine($"Could not draw STU buttons: {ex.Message}");
                try { _tablet.setClearScreen(); _tablet.setInkingMode(0x01); } catch { }
            }
        }
    }

    private static void RestrictInkToSignArea()
    {
        // Keeps ink off the buttons. Not every model/firmware supports it, so it's optional.
        try
        {
            var area = new wgssSTU.Rectangle();
            area.upperLeftXPixel = 0;
            area.upperLeftYPixel = 0;
            area.lowerRightXPixel = (ushort)(_pad!.SignArea.Right - 1);
            area.lowerRightYPixel = (ushort)(_pad.SignArea.Bottom - 1);
            _tablet!.setHandwritingDisplayArea(area);
        }
        catch
        {
        }
    }

    private static void PumpMessages()
    {
        while (PeekMessage(out MSG msg, IntPtr.Zero, 0, 0, PM_REMOVE))
        {
            if (msg.message == WM_HOTKEY && msg.hwnd == IntPtr.Zero)
            {
                HandleCommand((ConsoleKey)(int)msg.wParam);
                continue;
            }

            TranslateMessage(ref msg);
            DispatchMessage(ref msg);
        }
    }

    private static void RegisterHotKeys()
    {
        foreach (var key in HotKeys)
        {
            // ConsoleKey values are the Windows virtual-key codes; use them as the hotkey id too.
            if (!RegisterHotKey(IntPtr.Zero, (int)key, MOD_NOREPEAT, (uint)key))
                Console.WriteLine($"Warning: {key} is in use by another program; it only works in this window.");
        }
    }

    private static void UnregisterHotKeys()
    {
        foreach (var key in HotKeys)
            UnregisterHotKey(IntPtr.Zero, (int)key);
    }

    // Live diagnostics in the console title, so you can see whether pen data
    // arrives at all before worrying about calibration or the browser.
    private static void UpdatePenStatusTitle()
    {
        long now = Environment.TickCount64;
        if (now - _lastTitleUpdate < 150)
            return;
        _lastTitleUpdate = now;

        Console.Title = $"STU Bridge [{(_enabled ? "ON" : "OFF")}] pen events={Interlocked.Read(ref _penEvents)} " +
                        $"x={_lastPenX} y={_lastPenY} sw={_lastPenSw}";
    }

    private static void NormalizeRectangle()
    {
        if (_right < _left)
            (_left, _right) = (_right, _left);

        if (_bottom < _top)
            (_top, _bottom) = (_bottom, _top);

        // Avoid accidental zero-size rectangle.
        if (_right == _left) _right++;
        if (_bottom == _top) _bottom++;
    }

    private static void OnPenData(IPenData penData)
    {
        Interlocked.Increment(ref _penEvents);
        _lastPenX = penData.x;
        _lastPenY = penData.y;
        _lastPenSw = penData.sw;

        if (_capability is null || _pad is null)
            return;

        // Tablet coordinates -> STU LCD pixels.
        int px = penData.x * _pad.Width / _capability.tabletMaxX;
        int py = penData.y * _pad.Height / _capability.tabletMaxY;
        bool touching = penData.sw != 0;

        if (touching && !_penTouching)
        {
            // New touch: a tap on an STU button fires once, on pen-down.
            _touchStartedOutsideSignArea = py >= _pad.SignArea.Bottom;
            var button = _pad.HitTest(px, py);
            if (button != PadButton.None)
                Interlocked.Exchange(ref _pendingPadButton, (int)button);
        }
        _penTouching = touching;

        if (!_enabled)
            return;

        lock (Sync)
        {
            try
            {
                // Only the signing area drives the mouse; the button strip never draws.
                if (py >= _pad.SignArea.Bottom || (touching && _touchStartedOutsideSignArea))
                {
                    ReleaseMouse();
                    return;
                }

                double nx = Clamp01((double)penData.x / _capability.tabletMaxX);
                double ny = Clamp01((double)py / _pad.SignArea.Height);

                int x = _left + (int)Math.Round(nx * (_right - _left));
                int y = _top + (int)Math.Round(ny * (_bottom - _top));

                SetCursorPos(x, y);

                if (touching && !_penDown)
                {
                    SendLeftDown();
                    _penDown = true;
                }
                else if (!touching && _penDown)
                {
                    SendLeftUp();
                    _penDown = false;
                }
            }
            catch
            {
                // Keep the callback resilient. Any visible failures are handled by
                // disabling/restarting the bridge.
            }
        }
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static void ReleaseMouse()
    {
        if (_penDown)
        {
            SendLeftUp();
            _penDown = false;
        }
    }

    private static void ClickAt(POINT p)
    {
        SetCursorPos(p.X, p.Y);
        SendLeftDown();
        SendLeftUp();
    }

    private static void DisconnectTablet()
    {
        try
        {
            ReleaseMouse();

            if (_tablet is not null)
            {
                try { _tablet.setInkingMode(0x00); } catch { }
                try { _tablet.setClearScreen(); } catch { }
                try { _tablet.disconnect(); } catch { }
            }
        }
        finally
        {
            _tablet = null;
            _capability = null;
        }
    }

    private static void SendLeftDown()
    {
        INPUT[] inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_LEFTDOWN
                    }
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private static void SendLeftUp()
    {
        INPUT[] inputs = new[]
        {
            new INPUT
            {
                type = INPUT_MOUSE,
                U = new InputUnion
                {
                    mi = new MOUSEINPUT
                    {
                        dwFlags = MOUSEEVENTF_LEFTUP
                    }
                }
            }
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)]
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public UIntPtr dwExtraInfo;
    }

    private const uint PM_REMOVE = 0x0001;
    private const uint QS_ALLINPUT = 0x04FF;
    private const uint MWMO_INPUTAVAILABLE = 0x0004;
    private const uint WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    [StructLayout(LayoutKind.Sequential)]
    private struct MSG
    {
        public IntPtr hwnd;
        public uint message;
        public UIntPtr wParam;
        public IntPtr lParam;
        public uint time;
        public POINT pt;
        public uint lPrivate;
    }

    [DllImport("user32.dll")]
    private static extern bool PeekMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll")]
    private static extern bool TranslateMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern IntPtr DispatchMessage(ref MSG lpMsg);

    [DllImport("user32.dll")]
    private static extern uint MsgWaitForMultipleObjectsEx(uint nCount, IntPtr pHandles, uint dwMilliseconds, uint dwWakeMask, uint dwFlags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
