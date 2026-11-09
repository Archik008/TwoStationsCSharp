namespace TeacherApp;

// =====================================================================
//  ЭКРАН СТАНЦИИ: картинка с пульта студента. Когда преподаватель забрал управление,
//  мышь и клавиатура над картинкой уходят на станцию (координаты — доли 0..1 от снимка).
// =====================================================================

public class ScreenView : Control
{
    Image image;
    public bool InputEnabled;
    /// <summary>Ввод преподавателя для станции (data события t_input).</summary>
    public event Action<object> Input;
    long lastMove;

    public ScreenView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        BackColor = Color.FromArgb(40, 44, 52);
        ForeColor = Color.Gainsboro;
    }

    public string Placeholder = "Выберите станцию в таблице";

    public void SetImage(Image img)
    {
        var old = image;
        image = img;
        old?.Dispose();
        Invalidate();
    }

    public void Clear() { SetImage(null); }

    RectangleF ImageRect()
    {
        if (image == null) return RectangleF.Empty;
        float k = Math.Min(Width / (float)image.Width, Height / (float)image.Height);
        float w = image.Width * k, h = image.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(BackColor);
        if (image == null)
        {
            TextRenderer.DrawText(g, StationApp.Core.L.T(Placeholder), Font,   // [ДОБАВЛЕНО 22.10.2026, этап 8]
                 ClientRectangle, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }
        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBilinear;
        g.DrawImage(image, ImageRect());
        if (InputEnabled)
        {
            using var p = new Pen(Color.OrangeRed, 3);
            g.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }
    }

    bool Map(Point p, out double x, out double y)
    {
        var r = ImageRect();
        x = (p.X - r.X) / r.Width;
        y = (p.Y - r.Y) / r.Height;
        return image != null && x >= 0 && x <= 1 && y >= 0 && y <= 1;
    }

    void Mouse(string act, MouseEventArgs e)
    {
        if (!InputEnabled || !Map(e.Location, out var x, out var y)) return;
        string btn = e.Button == MouseButtons.Right ? "right" : "left";
        Input?.Invoke(new { kind = "t_input", act, x, y, btn, delta = e.Delta });
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); Mouse("down", e); }
    protected override void OnMouseUp(MouseEventArgs e) => Mouse("up", e);
    protected override void OnMouseWheel(MouseEventArgs e) => Mouse("wheel", e);
    protected override void OnMouseMove(MouseEventArgs e)
    {
        // наведение нужно для меню; не чаще 8 раз в секунду
        if (Environment.TickCount64 - lastMove < 120) return;
        lastMove = Environment.TickCount64;
        Mouse("move", e);
    }

    // ---- клавиатура: в формате SendKeys ----
    protected override bool IsInputKey(Keys k) => InputEnabled || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!InputEnabled) return;
        string s = e.KeyCode switch
        {
            Keys.Enter => "{ENTER}", Keys.Back => "{BACKSPACE}", Keys.Escape => "{ESC}", Keys.Tab => "{TAB}",
            Keys.Delete => "{DEL}", Keys.Left => "{LEFT}", Keys.Right => "{RIGHT}", Keys.Up => "{UP}", Keys.Down => "{DOWN}",
            Keys.Home => "{HOME}", Keys.End => "{END}", _ => null
        };
        if (s == null) return;
        e.Handled = e.SuppressKeyPress = true;
        Input?.Invoke(new { kind = "t_input", act = "text", text = s });
    }

    protected override void OnKeyPress(KeyPressEventArgs e)
    {
        if (!InputEnabled || char.IsControl(e.KeyChar)) return;
        string c = e.KeyChar.ToString();
        if ("+^%~(){}[]".Contains(e.KeyChar)) c = "{" + c + "}";
        Input?.Invoke(new { kind = "t_input", act = "text", text = c });
        e.Handled = true;
    }
}
