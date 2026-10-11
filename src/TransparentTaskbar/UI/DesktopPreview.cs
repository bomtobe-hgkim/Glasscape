using System.Drawing.Drawing2D;
using TransparentTaskbar.Localization;
namespace TransparentTaskbar.UI;

internal sealed class DesktopPreview : Control
{
    internal int Opacity; internal bool TaskbarEnabled = true;
    internal string SceneTitle = ""; internal bool Paused; private Image? scene;
    internal DesktopPreview() { DoubleBuffered = true; Height = 284; Dock = DockStyle.Fill; AccessibleRole = AccessibleRole.Graphic; AccessibleName = AppText.Get("PreviewAccessible"); }
    internal void SetScene(Image? image) { scene?.Dispose(); scene = image; Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias; if (Width < 40 || Height < 60) return;
        int D(int value) => LogicalToDeviceUnits(value);
        using var clip = GlassTheme.Rounded(new RectangleF(0, 0, Width - 1, Height - 1), D(10)); g.SetClip(clip);
        if (scene is not null) { var scale = Math.Max((float)Width / scene.Width, (float)Height / scene.Height); g.DrawImage(scene, (Width - scene.Width * scale) / 2, (Height - scene.Height * scale) / 2, scene.Width * scale, scene.Height * scale); }
        else
        {
            using var sky = new LinearGradientBrush(ClientRectangle, Color.FromArgb(73, 119, 140), Color.FromArgb(18, 41, 65), 90); g.FillRectangle(sky, ClientRectangle);
            using var sun = new SolidBrush(Color.FromArgb(50, 190, 222, 219)); g.FillEllipse(sun, Width * .65f, Height * .12f, Height * .23f, Height * .23f);
            using var back = new SolidBrush(Color.FromArgb(43, 82, 103)); g.FillPolygon(back, new PointF[] { new(0, Height * .57f), new(Width * .18f, Height * .29f), new(Width * .43f, Height * .66f), new(Width * .75f, Height * .37f), new(Width, Height * .6f), new(Width, Height), new(0, Height) });
            using var front = new SolidBrush(Color.FromArgb(23, 57, 76)); g.FillPolygon(front, new PointF[] { new(0, Height * .75f), new(Width * .27f, Height * .5f), new(Width * .54f, Height * .76f), new(Width, Height * .55f), new(Width, Height), new(0, Height) });
        }
        using var shade = new LinearGradientBrush(ClientRectangle, Color.Transparent, Color.FromArgb(125, 5, 17, 30), 90); g.FillRectangle(shade, ClientRectangle);
        using var title = new Font(Font.FontFamily, 17 * DeviceDpi / 72f, FontStyle.Bold, GraphicsUnit.Pixel);
        using var caption = new Font(Font.FontFamily, 8 * DeviceDpi / 72f, FontStyle.Regular, GraphicsUnit.Pixel);
        TextRenderer.DrawText(g, AppText.Get(scene is null ? "PreviewExample" : "PreviewThumbnailCaption"), caption, new Rectangle(D(24), Height - D(104), Width - D(48), D(18)), Color.FromArgb(184, 216, 224), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        TextRenderer.DrawText(g, SceneTitle, title, new Rectangle(D(22), Height - D(81), Width - D(44), D(34)), Color.White, TextFormatFlags.EndEllipsis);
        using var taskbar = new SolidBrush(Color.FromArgb(TaskbarEnabled ? (int)(Math.Clamp(Opacity, 0, 100) * 2.55) : 235, 22, 34, 48)); g.FillRectangle(taskbar, 0, Height - D(37), Width, D(37));
        for (var i = 0; i < 4; i++) { using var app = new SolidBrush(i == 1 ? Color.FromArgb(224, 188, 107) : i == 2 ? Color.FromArgb(103, 185, 205) : Color.FromArgb(202, 224, 231)); g.FillRectangle(app, Width / 2 - D(58) + i * D(32), Height - D(25), D(14), D(14)); }
        if (Paused) TextRenderer.DrawText(g, AppText.Get("PreviewPaused"), caption, new Rectangle(Width - D(108), D(16), D(98), D(24)), GlassTheme.Text);
        g.ResetClip(); using var border = new Pen(GlassTheme.Line); g.DrawPath(border, clip);
    }
    protected override void Dispose(bool disposing) { if (disposing) scene?.Dispose(); base.Dispose(disposing); }
}
