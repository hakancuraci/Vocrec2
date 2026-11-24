using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace SesKayit;

internal static class StudioTheme
{
    public static readonly Color Background = Color.FromArgb(12, 15, 23);
    public static readonly Color Card = Color.FromArgb(22, 27, 39);
    public static readonly Color Field = Color.FromArgb(14, 19, 29);
    public static readonly Color Border = Color.FromArgb(43, 50, 68);
    public static readonly Color Ink = Color.FromArgb(239, 242, 250);
    public static readonly Color Muted = Color.FromArgb(148, 160, 183);
    public static readonly Color Purple = Color.FromArgb(163, 145, 255);
    public static readonly Color Mint = Color.FromArgb(91, 226, 184);
    public static Color Surface(Control? parent)
    {
        for (var current = parent; current != null; current = current.Parent)
            if (current.BackColor.A == 255) return current.BackColor;
        return Background;
    }

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(r.X, r.Y, d, d, 180, 90);
        path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); return path;
    }
}

internal sealed class StudioCard : Panel
{
    public StudioCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = StudioTheme.Card;
        Padding = new Padding(18);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(StudioTheme.Surface(Parent));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = StudioTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 14);
        using var brush = new SolidBrush(BackColor); using var pen = new Pen(StudioTheme.Border);
        e.Graphics.FillPath(brush, path); e.Graphics.DrawPath(pen, path);
    }
}

internal sealed class StudioButton : Button
{
    [DefaultValue(false)]
    public bool Primary { get; set; }
    [DefaultValue(false)]
    public bool Accent { get; set; }
    [DefaultValue(false)]
    public bool Selected { get; set; }
    private bool hovered;
    public StudioButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        FlatStyle = FlatStyle.Flat; FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand; Font = new Font("Segoe UI", 10, FontStyle.Bold);
        Height = 42; Margin = new Padding(4); UseVisualStyleBackColor = false;
    }
    protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(StudioTheme.Surface(Parent));
        Color fill = Selected ? Color.FromArgb(47, 40, 72) : Primary ? StudioTheme.Mint : (hovered && Enabled ? Color.FromArgb(42, 48, 66) : StudioTheme.Field);
        if (!Enabled) fill = Color.FromArgb(27, 32, 43);
        Color ink = !Enabled ? Color.FromArgb(108, 120, 140) : Primary ? StudioTheme.Background : (Accent || Selected) ? StudioTheme.Purple : StudioTheme.Ink;
        using var path = StudioTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 9);
        using var brush = new SolidBrush(fill); using var pen = new Pen((Accent || Selected) && Enabled ? StudioTheme.Purple : StudioTheme.Border);
        g.FillPath(brush, path); if (!Primary) g.DrawPath(pen, path);
        TextRenderer.DrawText(g, Text, Font, ClientRectangle, ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Focused) { using var focusPen = new Pen(StudioTheme.Purple); g.DrawRectangle(focusPen, 4, 4, Width - 9, Height - 9); }
    }
}

internal sealed class VolumeSlider : Control
{
    private int value = 70;
    private bool dragging;
    public event EventHandler? ValueChanged;
    [DefaultValue(70)]
    public int Value
    {
        get => value;
        set { int next = Math.Clamp(value, 0, 100); if (this.value == next) return; this.value = next; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); }
    }
    public VolumeSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        Dock = DockStyle.Fill; TabStop = true; Cursor = Cursors.Hand; BackColor = StudioTheme.Card; Margin = new Padding(4, 0, 4, 0);
        AccessibleRole = AccessibleRole.Slider; Height = 18;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.SmoothingMode = SmoothingMode.AntiAlias;
        int y = Height / 2; float x = 7 + (Width - 14) * Value / 100f;
        using var background = new Pen(StudioTheme.Border, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var active = new Pen(StudioTheme.Purple, 4) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var thumb = new SolidBrush(StudioTheme.Ink);
        g.DrawLine(background, 7, y, Width - 7, y); g.DrawLine(active, 7, y, x, y); g.FillEllipse(thumb, x - 5, y - 5, 10, 10);
        if (Focused) { using var p = new Pen(StudioTheme.Purple); g.DrawEllipse(p, x - 7, y - 7, 14, 14); }
    }
    private void SetFromMouse(int x) => Value = (int)Math.Round((x - 7) * 100d / Math.Max(1, Width - 14));
    protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button != MouseButtons.Left) return; Focus(); dragging = true; Capture = true; SetFromMouse(e.X); }
    protected override void OnMouseMove(MouseEventArgs e) { base.OnMouseMove(e); if (dragging) SetFromMouse(e.X); }
    protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); dragging = false; Capture = false; }
    protected override bool IsInputKey(Keys keyData) => keyData is Keys.Left or Keys.Right or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Left) Value -= 1; else if (e.KeyCode == Keys.Right) Value += 1;
        else if (e.KeyCode == Keys.Home) Value = 0; else if (e.KeyCode == Keys.End) Value = 100;
    }
}

internal sealed class LevelMeter : Control
{
    private int value;
    [DefaultValue(0)]
    public int Value { get => value; set { int next = Math.Clamp(value, 0, 100); if (this.value == next) return; this.value = next; Invalidate(); } }
    public LevelMeter() { DoubleBuffered = true; Dock = DockStyle.Fill; Height = 7; BackColor = StudioTheme.Card; Margin = Padding.Empty; }
    protected override void OnPaint(PaintEventArgs e)
    {
        for (int n = 0; n < 30; n++)
        {
            using var b = new SolidBrush(n < Value * .3 ? (n > 26 ? Color.FromArgb(255, 188, 110) : StudioTheme.Mint) : StudioTheme.Border);
            e.Graphics.FillRectangle(b, n * Width / 30f, 1, Math.Max(1, Width / 30f - 3), Math.Max(2, Height - 2));
        }
    }
}

internal sealed class DevicePicker : ListBox
{
    private readonly HashSet<int> checks = [];
    public event EventHandler? ItemChecksChanged;
    public IEnumerable<object> CheckedItems => Items.Cast<object>().Where((_, i) => checks.Contains(i));
    public DevicePicker()
    {
        Dock = DockStyle.Fill; BackColor = StudioTheme.Card; ForeColor = StudioTheme.Ink;
        BorderStyle = BorderStyle.None; IntegralHeight = false; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 28;
        Font = new Font("Segoe UI", 9); AccessibleName = "Ses kaynakları"; Margin = Padding.Empty;
    }
    public void SetItemChecked(int index, bool selected)
    {
        bool changed = selected ? checks.Add(index) : checks.Remove(index);
        if (changed) { Invalidate(); ItemChecksChanged?.Invoke(this, EventArgs.Empty); }
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, e.Bounds);
        bool selected = checks.Contains(e.Index);
        var box = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 7, 13, 13);
        using var pen = new Pen(selected ? StudioTheme.Mint : StudioTheme.Muted, 1.4f);
        e.Graphics.DrawRectangle(pen, box);
        if (selected) e.Graphics.DrawLines(pen, new Point[] { new(box.X + 3, box.Y + 6), new(box.X + 6, box.Y + 9), new(box.X + 11, box.Y + 3) });
        var text = new Rectangle(e.Bounds.X + 25, e.Bounds.Y, e.Bounds.Width - 28, e.Bounds.Height);
        TextRenderer.DrawText(e.Graphics, Items[e.Index].ToString(), Font, text, StudioTheme.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        if (Enabled && (e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e); int index = IndexFromPoint(e.Location);
        if (e.Button == MouseButtons.Left && index >= 0) SetItemChecked(index, !checks.Contains(index));
    }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode == Keys.Space && SelectedIndex >= 0) { SetItemChecked(SelectedIndex, !checks.Contains(SelectedIndex)); e.Handled = true; }
    }
}

internal sealed class StudioSwitch : CheckBox
{
    [DefaultValue("Elle")]
    public string OffText { get; set; } = "Elle";
    public StudioSwitch()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        Dock = DockStyle.Fill; Cursor = Cursors.Hand; Checked = true; AccessibleName = "Otomatik kaynak seçimi"; BackColor = StudioTheme.Card;
    }
    protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        int x = Width - 48, y = Height / 2 - 12;
        using var path = StudioTheme.Round(new RectangleF(x, y, 46, 24), 12);
        using var fill = new SolidBrush(Checked ? StudioTheme.Purple : StudioTheme.Border);
        using var knob = new SolidBrush(StudioTheme.Ink);
        e.Graphics.FillPath(fill, path); e.Graphics.FillEllipse(knob, x + (Checked ? 25 : 4), y + 4, 16, 16);
        TextRenderer.DrawText(e.Graphics, Checked ? "Açık" : OffText, Font, new Rectangle(0, 0, Width - 58, Height), StudioTheme.Ink, TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        if (Focused) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, StudioTheme.Purple, BackColor);
    }
}

internal sealed class BrandMark : Control
{
    public BrandMark() { DoubleBuffered = true; Size = new Size(42, 42); BackColor = StudioTheme.Background; AccessibleName = "Vocrec2 logosu"; }
    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(StudioTheme.Surface(Parent));
        e.Graphics.DrawIcon(AppLogo.Icon, new Rectangle(0, 0, Width, Height));
    }
}

internal sealed class ActivityFeed : ListBox
{
    public ActivityFeed()
    {
        Dock = DockStyle.Fill; BackColor = StudioTheme.Card; ForeColor = StudioTheme.Muted;
        BorderStyle = BorderStyle.None; IntegralHeight = false; DrawMode = DrawMode.OwnerDrawFixed; ItemHeight = 26;
        Font = new Font("Segoe UI", 9); SelectionMode = SelectionMode.None; Margin = Padding.Empty;
    }
    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0 || e.Index >= Items.Count) return;
        using var background = new SolidBrush(BackColor); e.Graphics.FillRectangle(background, e.Bounds);
        string line = Items[e.Index].ToString() ?? "";
        bool active = line.Contains("ses var");
        using var dot = new SolidBrush(active ? StudioTheme.Mint : StudioTheme.Muted);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.FillEllipse(dot, e.Bounds.X + 3, e.Bounds.Y + 10, 5, 5);
        TextRenderer.DrawText(e.Graphics, line, Font, new Rectangle(e.Bounds.X + 18, e.Bounds.Y, e.Bounds.Width - 20, e.Bounds.Height), active ? StudioTheme.Mint : StudioTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }
}
