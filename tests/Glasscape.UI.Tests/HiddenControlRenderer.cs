using System.Runtime.InteropServices;
using System.Reflection;

internal static class HiddenControlRenderer
{
    private const int StyleIndex = -16;
    private const long VisibleStyle = 0x10000000;

    internal static bool IsOwnVisible(Control control) => (GetWindowLongPtr(control.Handle, StyleIndex).ToInt64() & VisibleStyle) != 0;

    internal static Bitmap Render(Form window)
    {
        if (window.Visible) throw new InvalidOperationException("Capture forms must remain hidden.");
        CreateHiddenHandles(window);
        LayoutTree(window);
        var bitmap = new Bitmap(window.ClientSize.Width, window.ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(window.BackColor);
        PaintChildren(window, window, graphics, window.ClientRectangle);
        if (window.Visible) throw new InvalidOperationException("Rendering must not make the capture form visible.");
        return bitmap;
    }

    private static void CreateHiddenHandles(Control control)
    {
        // Child WS_VISIBLE expresses its own requested visibility, even while the
        // top-level form stays hidden. Creating child handles cannot show that form.
        _ = control.Handle;
        foreach (Control child in control.Controls) CreateHiddenHandles(child);
    }

    private static void LayoutTree(Control control)
    {
        control.PerformLayout();
        foreach (Control child in control.Controls) LayoutTree(child);
    }

    private static void PaintChildren(Form window, Control parent, Graphics graphics, Rectangle parentClip)
    {
        // Controls collection index 0 is the front of the z-order.
        for (var index = parent.Controls.Count - 1; index >= 0; index--)
        {
            var control = parent.Controls[index];
            if (!IsOwnVisible(control) || control.Width <= 0 || control.Height <= 0) continue;
            var origin = window.PointToClient(parent.PointToScreen(control.Location));
            var bounds = new Rectangle(origin, control.Size);
            var clip = Rectangle.Intersect(parentClip, bounds);
            if (clip.IsEmpty) continue;
            using var rendered = new Bitmap(control.Width, control.Height);
            control.DrawToBitmap(rendered, new Rectangle(Point.Empty, control.Size));
            if (control is ComboBox { DrawMode: DrawMode.OwnerDrawFixed, SelectedIndex: >= 0 } combo)
            {
                var item = PaintSelectedItem(combo, rendered);
                ValidateOwnerDrawContent(combo, rendered, item);
            }
            var state = graphics.Save();
            graphics.SetClip(clip);
            graphics.DrawImageUnscaled(rendered, origin);
            graphics.Restore(state);
            var clientOrigin = window.PointToClient(control.PointToScreen(Point.Empty));
            var childClip = Rectangle.Intersect(clip, new Rectangle(clientOrigin, control.ClientSize));
            PaintChildren(window, control, graphics, childClip);
        }
    }

    private static Rectangle PaintSelectedItem(ComboBox combo, Bitmap rendered)
    {
        var info = new ComboBoxInfo { Size = Marshal.SizeOf<ComboBoxInfo>() };
        if (!GetComboBoxInfo(combo.Handle, ref info)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var item = Rectangle.FromLTRB(info.Item.Left, info.Item.Top, info.Item.Right, info.Item.Bottom);
        using var graphics = Graphics.FromImage(rendered);
        var draw = new DrawItemEventArgs(graphics, combo.Font, item, combo.SelectedIndex,
            DrawItemState.ComboBoxEdit | (combo.Enabled ? DrawItemState.Default : DrawItemState.Disabled), combo.ForeColor, combo.BackColor);
        // Hidden WM_PRINT does not consistently dispatch the owner's selected-item
        // callback. Invoke the real GlassCombo OnDrawItem at the native item rect.
        typeof(ComboBox).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(combo, [draw]);
        return item;
    }

    private static void ValidateOwnerDrawContent(ComboBox combo, Bitmap rendered, Rectangle item)
    {
        var background = 0;
        var text = 0;
        item.Intersect(new Rectangle(Point.Empty, rendered.Size));
        for (var y = item.Top; y < item.Bottom; y++)
        for (var x = item.Left; x < item.Right; x++)
        {
            var pixel = rendered.GetPixel(x, y);
            if (pixel.ToArgb() == combo.BackColor.ToArgb()) background++;
            if (pixel.R > 130 && pixel.G > 145 && pixel.B > 160) text++;
        }
        if (background < item.Width * item.Height / 2 || text < 12) throw new InvalidDataException($"Hidden owner-drawn control '{combo.Name}' did not render its selected item '{combo.Text}'.");
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ComboBoxInfo { internal int Size; internal NativeRect Item, Button; internal uint ButtonState; internal nint Combo, ItemWindow, List; }
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetComboBoxInfo(nint combo, ref ComboBoxInfo info);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint window, int index);
}
