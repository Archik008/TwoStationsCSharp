namespace DncApp;

// =====================================================================
//  КАРТИНКА СХЕМЫ / ОКНА СТАНЦИИ. Вписывается в размер с сохранением пропорций.
//  Когда у ДНЦ управление (InputEnabled) — мышь и клавиатура над картинкой уходят на станцию:
//  координаты — доли картинки, плюс рамка Frame (какая часть окна станции на картинке).
//  (Сделано по образцу ScreenView программы «Преподаватель».)
// =====================================================================

public class ImageView : Control
{
    Image image;
    public bool InputEnabled;
    public string Placeholder = "";
    /// <summary>Какой части окна станции соответствует картинка (доли окна).</summary>
    public RectangleF Frame = new(0, 0, 1, 1);
    /// <summary>Ввод для станции (data события t_input).</summary>
    public event Action<object> Input;
    /// <summary>Колесо мыши, когда ввод не уходит на станцию (прокрутка ленты схем).</summary>
    public event Action<int> FreeWheel;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Своё сообщение мыши для станции (act, x, y доли картинки, кнопка, колесо) —
    /// у плитки ленты: точка чертежа схемы. null — доли рамки Frame (снимок окна).</summary>
    public Func<string, double, double, string, int, object> MakeInput;
    /// <summary>Колесо всегда прокручивает ленту (и когда управление у ДНЦ).</summary>
    public bool WheelAlwaysFree;
    long lastMove;

    public ImageView()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint |
                 ControlStyles.Selectable | ControlStyles.ResizeRedraw, true);
        TabStop = true;
        BackColor = Color.FromArgb(40, 44, 52);
        ForeColor = Color.Gainsboro;
    }

    public Image Image => image;

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Лента схем: картинка рисуется полосой во всю ширину, верх — PlaceTop, высота — PlaceHeight
    /// (не вписывается с полями). Остальное — цветом фона самой схемы, чтобы соседние схемы сливались в одну полосу.
    /// PlaceTop &lt; 0 — как раньше (вписать по центру).</summary>
    public int PlaceTop = -1, PlaceHeight;
    Color fill;

    public void SetImage(Image img)
    {
        var old = image;
        image = img;
        old?.Dispose();
        if (img is Bitmap b && b.Width > 2 && b.Height > 2) fill = b.GetPixel(1, 1);
        Invalidate();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { image?.Dispose(); image = null; }
        base.Dispose(disposing);
    }

    RectangleF ImageRect()
    {
        if (image == null) return RectangleF.Empty;
        if (PlaceTop >= 0) return new RectangleF(0, PlaceTop, Width, PlaceHeight > 0 ? PlaceHeight : image.Height);
        float k = Math.Min(Width / (float)image.Width, Height / (float)image.Height);
        float w = image.Width * k, h = image.Height * k;
        return new RectangleF((Width - w) / 2, (Height - h) / 2, w, h);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        var g = e.Graphics;
        g.Clear(PlaceTop >= 0 && image != null && fill.A > 0 ? fill : BackColor);
        if (image == null)
        {
            TextRenderer.DrawText(g, Placeholder, Font, ClientRectangle, ForeColor,
                                  TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            return;
        }
        var r = ImageRect();
        // картинка пришла ровно в размер — рисуем 1:1, без размытия
        bool exact = Math.Abs(r.Width - image.Width) < 1.5f && Math.Abs(r.Height - image.Height) < 1.5f;
        g.InterpolationMode = exact ? System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor
                                    : System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
        g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.Half;
        if (exact) g.DrawImageUnscaled(image, (int)Math.Round(r.X), (int)Math.Round(r.Y));
        else g.DrawImage(image, r);
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
        Input?.Invoke(MakeInput?.Invoke(act, x, y, btn, e.Delta) ??
                      new { kind = "t_input", act, x, y, btn, delta = e.Delta, rx = Frame.X, ry = Frame.Y, rw = Frame.Width, rh = Frame.Height });
    }

    protected override void OnMouseDown(MouseEventArgs e) { Focus(); Mouse("down", e); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { Mouse("up", e); base.OnMouseUp(e); }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        if (InputEnabled && !WheelAlwaysFree) Mouse("wheel", e);
        else FreeWheel?.Invoke(e.Delta);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // наведение нужно для меню и подсказок станции; не чаще 8 раз в секунду
        if (Environment.TickCount64 - lastMove < 120) return;
        lastMove = Environment.TickCount64;
        Mouse("move", e);
    }

    // ---- клавиатура: в формате SendKeys (как у преподавателя) ----
    protected override bool IsInputKey(Keys k) => InputEnabled || base.IsInputKey(k);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (!InputEnabled) { base.OnKeyDown(e); return; }
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
