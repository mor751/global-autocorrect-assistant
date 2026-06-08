using System.Drawing;
using System.Drawing.Drawing2D;

namespace Autocorrect.App;

internal static class TrayIconFactory
{
    // Draws a cyan-on-black rounded "A" so the app is recognizable in the Windows notification area.
    public static Icon Create()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            using var background = new SolidBrush(Color.FromArgb(7, 10, 14));
            using var accent = new SolidBrush(Color.FromArgb(34, 211, 238));
            using var border = new Pen(Color.FromArgb(34, 211, 238), 2f);
            using var shape = RoundedRectangle(new Rectangle(2, 2, 28, 28), 7);
            graphics.FillPath(background, shape);
            graphics.DrawPath(border, shape);

            using var font = new Font("Segoe UI", 18f, FontStyle.Bold, GraphicsUnit.Pixel);
            using var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            graphics.DrawString("A", font, accent, new RectangleF(0, -1, 32, 32), format);
        }

        return Icon.FromHandle(bitmap.GetHicon());
    }

    // Builds a rounded-rectangle path used for the icon background and border.
    private static GraphicsPath RoundedRectangle(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}
