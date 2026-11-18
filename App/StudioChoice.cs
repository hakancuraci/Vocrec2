using System.Drawing.Drawing2D;

namespace SesKayit;

internal sealed class StudioChoice : Control
{
    private int selectedIndex = -1;
    public List<object> Items { get; } = [];
    public event EventHandler? SelectedIndexChanged;
    public int SelectedIndex
    {
        get => selectedIndex;
        set
        {
            int next = Math.Clamp(value, -1, Items.Count - 1);
            if (selectedIndex == next) return;
            selectedIndex = next; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty);
            AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
        }
    }
    public StudioChoice()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true; Cursor = Cursors.Hand; BackColor = StudioTheme.Field; ForeColor = StudioTheme.Ink;
        Font = new Font("Segoe UI", 10); AccessibleRole = AccessibleRole.ComboBox; Height = 36;
    }
    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics; g.Clear(StudioTheme.Surface(Parent)); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var shape = StudioTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8);
        using var fill = new SolidBrush(StudioTheme.Field); using var pen = new Pen(Focused ? StudioTheme.Purple : StudioTheme.Border);
        g.FillPath(fill, shape); g.DrawPath(pen, shape);
        string text = selectedIndex >= 0 ? Items[selectedIndex].ToString() ?? "" : "Kalite seçin";
        TextRenderer.DrawText(g, text, Font, new Rectangle(12, 0, Width - 44, Height), Enabled ? ForeColor : StudioTheme.Muted, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
        using var arrow = new Pen(StudioTheme.Purple, 1.5f);
        int x = Width - 24, y = Height / 2;
        g.DrawLines(arrow, new Point[] { new(x - 4, y - 2), new(x, y + 2), new(x + 4, y - 2) });
    }
    private void OpenMenu()
    {
        if (!Enabled || Items.Count == 0) return;
        var menu = new ContextMenuStrip { ShowImageMargin = false, BackColor = StudioTheme.Field, ForeColor = StudioTheme.Ink, Font = Font, Renderer = new ToolStripProfessionalRenderer(new MenuColors()) };
        for (int i = 0; i < Items.Count; i++)
        {
            int index = i;
            var item = new ToolStripMenuItem(Items[i].ToString()) { Checked = i == selectedIndex, ForeColor = StudioTheme.Ink, Padding = new Padding(10, 7, 10, 7) };
            item.Click += (_, _) => SelectedIndex = index; menu.Items.Add(item);
        }
        menu.Closed += (_, _) => { menu.Dispose(); Focus(); };
        menu.Show(this, new Point(0, Height));
    }
    protected override void OnMouseClick(MouseEventArgs e) { base.OnMouseClick(e); if (e.Button == MouseButtons.Left) { Focus(); OpenMenu(); } }
    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
    protected override bool IsInputKey(Keys keyData) => (keyData & Keys.KeyCode) is Keys.Up or Keys.Down or Keys.Home or Keys.End || base.IsInputKey(keyData);
    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.KeyCode is Keys.Space or Keys.Enter || (e.Alt && e.KeyCode == Keys.Down)) OpenMenu();
        else if (e.KeyCode == Keys.Down) SelectedIndex = Math.Min(Items.Count - 1, selectedIndex + 1);
        else if (e.KeyCode == Keys.Up) SelectedIndex = Math.Max(0, selectedIndex - 1);
        else if (e.KeyCode == Keys.Home) SelectedIndex = 0;
        else if (e.KeyCode == Keys.End) SelectedIndex = Items.Count - 1;
        else return;
        e.Handled = e.SuppressKeyPress = true;
    }
    protected override AccessibleObject CreateAccessibilityInstance() => new ChoiceAccessibility(this);
    private sealed class ChoiceAccessibility(StudioChoice owner) : ControlAccessibleObject(owner)
    {
        public override string? Value
        {
            get => owner.selectedIndex >= 0 ? owner.Items[owner.selectedIndex].ToString() : "";
            set { int index = owner.Items.FindIndex(i => i.ToString() == value); if (index >= 0) owner.SelectedIndex = index; }
        }
        public override string DefaultAction => "Kalite seçeneklerini aç";
        public override void DoDefaultAction() => owner.OpenMenu();
    }
    private sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => StudioTheme.Field;
        public override Color ImageMarginGradientBegin => StudioTheme.Field;
        public override Color ImageMarginGradientMiddle => StudioTheme.Field;
        public override Color ImageMarginGradientEnd => StudioTheme.Field;
        public override Color MenuItemSelected => Color.FromArgb(47, 40, 72);
        public override Color MenuItemBorder => StudioTheme.Purple;
        public override Color MenuBorder => StudioTheme.Border;
        public override Color CheckBackground => Color.FromArgb(47, 40, 72);
        public override Color CheckSelectedBackground => Color.FromArgb(47, 40, 72);
    }
}
