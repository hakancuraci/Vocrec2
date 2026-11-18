using System.Drawing.Drawing2D;

namespace SesKayit;

internal sealed class StudioTextField : Panel
{
    private readonly TextBox input;
    public StudioTextField(TextBox input)
    {
        this.input = input;
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = StudioTheme.Field; Padding = new Padding(11, 9, 11, 7);
        input.BorderStyle = BorderStyle.None; input.BackColor = StudioTheme.Field; input.ForeColor = StudioTheme.Ink;
        input.Font = new Font("Segoe UI", 10); input.Dock = DockStyle.Fill;
        input.Enter += (_, _) => Invalidate(); input.Leave += (_, _) => Invalidate(); input.EnabledChanged += (_, _) => Invalidate();
        Controls.Add(input);
    }
    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.Clear(StudioTheme.Surface(Parent)); e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = StudioTheme.Round(new RectangleF(.5f, .5f, Width - 1, Height - 1), 8);
        using var fill = new SolidBrush(StudioTheme.Field);
        using var border = new Pen(input.Focused ? StudioTheme.Purple : StudioTheme.Border, input.Focused ? 1.5f : 1);
        e.Graphics.FillPath(fill, path); e.Graphics.DrawPath(border, path);
    }
}
