using System.Drawing.Drawing2D;
using StationEditor.Model;

namespace StationEditor.Ui;

/// <summary>Холст схемы: рисует станцию, мышь обрабатывает MainForm.</summary>
public sealed class SchemeCanvas : Control
{
    public Func<Station> GetStation;
    public UiState Ui;

    public SchemeCanvas()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
        TabStop = true;
        BackColor = Pal.Bg;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var st = GetStation?.Invoke();
        if (st == null || Ui == null) { e.Graphics.Clear(Pal.Bg); return; }
        Renderer.Render(e.Graphics, ClientSize.Width, ClientSize.Height, st, Ui);
    }

    protected override bool IsInputKey(Keys keyData) => true;
}

/// <summary>Кнопка палитры: значок + подпись + клавиша.</summary>
public sealed class PaletteItem : Control
{
    public string Caption = "", KeyHint = "";
    public bool On;
    public Action<Graphics, RectangleF> DrawIcon;
    bool hot;

    public PaletteItem()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
    }

    protected override void OnMouseEnter(EventArgs e) { hot = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hot = false; Invalidate(); base.OnMouseLeave(e); }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        var r = new Rectangle(0, 0, Width - 1, Height - 1);
        var back = On ? Color.FromArgb(0xd8, 0xef, 0xe9) : hot ? Color.FromArgb(0xf2, 0xf7, 0xf5) : Color.White;
        var border = On ? Color.FromArgb(0x1f, 0x7a, 0x68) : Color.FromArgb(0xc5, 0xd2, 0xce);
        using (var b = new SolidBrush(back)) g.FillRectangle(b, r);
        using (var p = new Pen(border, On ? 2 : 1)) g.DrawRectangle(p, On ? new Rectangle(1, 1, Width - 3, Height - 3) : r);

        int icon = Math.Min(Width - 16, Height - 26);
        var ir = new RectangleF((Width - icon) / 2f, 5, icon, icon);
        DrawIcon?.Invoke(g, ir);
        g.SmoothingMode = SmoothingMode.None;

        TextRenderer.DrawText(g, Caption, Font, new Rectangle(1, (int)ir.Bottom + 1, Width - 2, Height - (int)ir.Bottom - 3),
            Color.FromArgb(0x23, 0x37, 0x32), TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis);
        if (!string.IsNullOrEmpty(KeyHint))
        {
            using var kf = new Font(Font.FontFamily, Font.Size * 0.85f);
            TextRenderer.DrawText(g, KeyHint, kf, new Rectangle(0, 1, Width - 3, 16), Color.FromArgb(0x7a, 0x8a, 0x86),
                TextFormatFlags.Right | TextFormatFlags.Top);
        }
    }
}

/// <summary>Значок варианта кубика (повороты); выбранный — в жёлтой рамке.</summary>
public sealed class VariantIcon : Control
{
    public Tile Tile;
    public bool On;

    public VariantIcon()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        SetStyle(ControlStyles.Selectable, false);
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(Parent?.BackColor ?? SystemColors.Control);
        Renderer.DrawTileIcon(g, Tile, new RectangleF(2, 2, Width - 4, Height - 4));
        if (On)
        {
            g.SmoothingMode = SmoothingMode.None;
            using var p = new Pen(Pal.Sel, 3);
            g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }
}
