using System.Drawing.Drawing2D;
namespace Glasscape.UI;

internal static class GlassTheme
{
    internal static readonly Color Canvas = Color.FromArgb(17, 27, 41), Surface = Color.FromArgb(23, 35, 52), Raised = Color.FromArgb(28, 43, 62), Text = Color.FromArgb(237, 242, 245), Muted = Color.FromArgb(143, 158, 174), Teal = Color.FromArgb(102, 196, 206), Line = Color.FromArgb(44, 61, 77);
    internal static Font Font(float size = 9.5f, FontStyle style = FontStyle.Regular) => new("Segoe UI Variable Text", size, style);
    internal static GraphicsPath Rounded(RectangleF r, float radius = 10)
    {
        var p = new GraphicsPath(); var d = radius * 2; p.AddArc(r.Left, r.Top, d, d, 180, 90); p.AddArc(r.Right - d, r.Top, d, d, 270, 90); p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90); p.AddArc(r.Left, r.Bottom - d, d, d, 90, 90); p.CloseFigure(); return p;
    }
    internal static Image? ImageResource(string name)
    {
        using var stream = typeof(GlassTheme).Assembly.GetManifestResourceStream(name); if (stream is null) return null;
        using var image = Image.FromStream(stream); return new Bitmap(image);
    }
}
internal sealed class GlassPanel : Panel
{
    internal Color BorderColor = GlassTheme.Line;
    internal GlassPanel() { DoubleBuffered = true; BackColor = GlassTheme.Surface; Padding = new(16); SetStyle(ControlStyles.Selectable, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias; if (Width < 24 || Height < 24) return;
        using var path = GlassTheme.Rounded(new RectangleF(.5f, .5f, Width - 1, Height - 1)); using var pen = new Pen(Focused ? GlassTheme.Teal : BorderColor, Focused ? 2 : 1); e.Graphics.DrawPath(pen, path);
    }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Enter or Keys.Space || base.IsInputKey(keyData);
}
internal sealed class GlassButton : Button
{
    internal bool Accent; internal bool Selected; internal bool WrapText; private bool hovered;
    internal GlassButton(string text, bool accent = false)
    {
        Text = text; Accent = accent; Height = 38; Width = 140; Font = GlassTheme.Font(); FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.PushButton;
        SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? GlassTheme.Canvas); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = GlassTheme.Rounded(new RectangleF(1, 1, Width - 2, Height - 2), 7);
        using var brush = new SolidBrush(!Enabled ? GlassTheme.Surface : Accent ? GlassTheme.Teal : Selected ? Color.FromArgb(27, 48, 62) : hovered ? GlassTheme.Raised : GlassTheme.Surface); e.Graphics.FillPath(brush, path);
        using var pen = new Pen(Focused || Selected ? GlassTheme.Teal : GlassTheme.Line); e.Graphics.DrawPath(pen, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, !Enabled ? GlassTheme.Muted : Accent ? GlassTheme.Canvas : Selected ? GlassTheme.Teal : GlassTheme.Text, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | (WrapText ? TextFormatFlags.WordBreak : TextFormatFlags.Default));
    }
}
internal sealed class GlassToggle : CheckBox
{
    internal GlassToggle(string name) { Name = name; Size = new(46, 30); AutoSize = false; Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.CheckButton; SetStyle(ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(Parent?.BackColor ?? GlassTheme.Surface); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var state = e.Graphics.Save();
        var scale = DeviceDpi / 96f; e.Graphics.ScaleTransform(scale, scale);
        using var path = GlassTheme.Rounded(new RectangleF(2, 5, 40, 20), 10); using var fill = new SolidBrush(Checked && Enabled ? GlassTheme.Teal : GlassTheme.Line); e.Graphics.FillPath(fill, path);
        using var thumb = new SolidBrush(Checked ? GlassTheme.Canvas : GlassTheme.Muted); e.Graphics.FillEllipse(thumb, Checked ? 24 : 5, 8, 14, 14);
        e.Graphics.Restore(state);
        if (Focused) { using var pen = new Pen(GlassTheme.Teal); e.Graphics.DrawRectangle(pen, 0, 1, Width - 2, Height - 3); }
    }
}

internal sealed class GlassCombo : ComboBox
{
    internal GlassCombo()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        BackColor = GlassTheme.Raised;
        ForeColor = GlassTheme.Text;
        Font = GlassTheme.Font();
        ItemHeight = 24;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Bounds.Width <= 0 || e.Bounds.Height <= 0) return;
        using var background = new SolidBrush(e.State.HasFlag(DrawItemState.Selected) ? Color.FromArgb(27, 48, 62) : GlassTheme.Raised);
        e.Graphics.FillRectangle(background, e.Bounds);
        var text = e.Index >= 0 && e.Index < Items.Count ? Items[e.Index]?.ToString() ?? "" : Text;
        TextRenderer.DrawText(e.Graphics, text, Font, Rectangle.Inflate(e.Bounds, -5, 0), Enabled ? GlassTheme.Text : GlassTheme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (e.State.HasFlag(DrawItemState.Focus)) e.DrawFocusRectangle();
    }
}
