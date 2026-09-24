using System.Globalization;

namespace WacomStuMouseBridge;

/// <summary>
/// Calibration saved to wacom-bridge.conf next to the EXE, as plain key=value lines,
/// so it survives restarts. Missing or malformed values keep their defaults.
/// </summary>
internal sealed class BridgeSettings
{
    public const string FileName = "wacom-bridge.conf";

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, FileName);

    // Signature area in screen pixels (F8 / F9).
    public int Left { get; set; } = 300;
    public int Top { get; set; } = 300;
    public int Right { get; set; } = 900;
    public int Bottom { get; set; } = 600;

    // Website button positions in screen pixels (F5 / F6); null = not set.
    public (int X, int Y)? ClearButton { get; set; }
    public (int X, int Y)? SaveButton { get; set; }

    public static BridgeSettings Load(out bool loaded)
    {
        var settings = new BridgeSettings();
        loaded = File.Exists(FilePath);
        if (!loaded)
            return settings;

        foreach (var raw in File.ReadAllLines(FilePath))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;

            string key = line[..eq].Trim().ToLowerInvariant();
            string value = line[(eq + 1)..].Trim();

            switch (key)
            {
                case "area.left": if (TryInt(value, out int l)) settings.Left = l; break;
                case "area.top": if (TryInt(value, out int t)) settings.Top = t; break;
                case "area.right": if (TryInt(value, out int r)) settings.Right = r; break;
                case "area.bottom": if (TryInt(value, out int b)) settings.Bottom = b; break;
                case "button.clear": settings.ClearButton = TryPoint(value); break;
                case "button.save": settings.SaveButton = TryPoint(value); break;
            }
        }

        return settings;
    }

    public void Save()
    {
        var lines = new[]
        {
            "# wacom-bridge calibration (screen pixels). Written by the bridge on F5/F6/F8/F9.",
            "# Delete this file to reset. button.* = x,y or empty if not set.",
            $"area.left={Left}",
            $"area.top={Top}",
            $"area.right={Right}",
            $"area.bottom={Bottom}",
            $"button.clear={FormatPoint(ClearButton)}",
            $"button.save={FormatPoint(SaveButton)}",
        };

        File.WriteAllLines(FilePath, lines);
    }

    private static bool TryInt(string s, out int value) =>
        int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);

    private static (int X, int Y)? TryPoint(string s)
    {
        var parts = s.Split(',');
        return parts.Length == 2 && TryInt(parts[0].Trim(), out int x) && TryInt(parts[1].Trim(), out int y)
            ? (x, y)
            : null;
    }

    private static string FormatPoint((int X, int Y)? p) => p is { } v ? $"{v.X},{v.Y}" : "";
}
