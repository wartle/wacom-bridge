using System.Drawing;
using System.Drawing.Imaging;
using System.Drawing.Text;

namespace WacomStuMouseBridge;

internal enum PadButton
{
    None = 0,
    Clear = 1,
    Save = 2,
    Cancel = 3,
}

/// <summary>
/// Layout of the STU LCD: a signing area on top and a strip with
/// Cancel / Clear / Save buttons along the bottom. All coordinates are LCD pixels.
/// </summary>
internal sealed class PadScreen
{
    public int Width { get; }
    public int Height { get; }
    public Rectangle SignArea { get; }
    public Rectangle CancelButton { get; }
    public Rectangle ClearButton { get; }
    public Rectangle SaveButton { get; }

    public PadScreen(int width, int height)
    {
        Width = width;
        Height = height;

        int strip = Math.Max(30, height / 5); // 40 px on the 320x200 STU-430
        const int gap = 8;
        int buttonWidth = (width - gap * 4) / 3;
        int buttonTop = height - strip + 5;
        int buttonHeight = strip - 10;

        SignArea = new Rectangle(0, 0, width, height - strip);
        // Left to right: Cancel, Clear, Save (primary action on the right).
        CancelButton = new Rectangle(gap, buttonTop, buttonWidth, buttonHeight);
        ClearButton = new Rectangle(gap * 2 + buttonWidth, buttonTop, buttonWidth, buttonHeight);
        SaveButton = new Rectangle(gap * 3 + buttonWidth * 2, buttonTop, buttonWidth, buttonHeight);
    }

    public PadButton HitTest(int x, int y)
    {
        if (CancelButton.Contains(x, y)) return PadButton.Cancel;
        if (ClearButton.Contains(x, y)) return PadButton.Clear;
        if (SaveButton.Contains(x, y)) return PadButton.Save;
        return PadButton.None;
    }

    /// <summary>Renders the blank pad screen (no ink) as PNG bytes for the Wacom ProtocolHelper.</summary>
    public byte[] RenderPng()
    {
        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.Clear(Color.White);
            // The STU-430 is 1-bit, so avoid anti-aliasing that would dither into noise.
            g.TextRenderingHint = TextRenderingHint.SingleBitPerPixelGridFit;

            using var line = new Pen(Color.Black, 2);
            g.DrawLine(line, 0, SignArea.Bottom, Width, SignArea.Bottom);

            using var font = new Font("Segoe UI", 16f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var center = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            foreach (var (rect, label) in new[] { (CancelButton, "Cancel"), (ClearButton, "Clear"), (SaveButton, "Save") })
            {
                g.DrawRectangle(line, rect.X, rect.Y, rect.Width - 1, rect.Height - 1);
                g.DrawString(label, font, Brushes.Black, rect, center);
            }
        }

        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }
}
