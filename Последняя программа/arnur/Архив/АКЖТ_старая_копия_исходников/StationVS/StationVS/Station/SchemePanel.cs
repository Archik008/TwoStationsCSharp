using System.Drawing.Drawing2D;

namespace StationApp;

// =====================================================================
//  ПАНЕЛЬ С ОТРИСОВКОЙ СХЕМЫ
// =====================================================================

class SchemePanel : Panel
{
    readonly Station st;
    float scale = 1;
    PointF off;

    readonly Font fLabel = new("Bahnschrift", 12f, FontStyle.Bold);
    readonly Font fSwitch = new("Bahnschrift", 11f, FontStyle.Bold);
    readonly Font fHelp = new("Bahnschrift", 9f);
    readonly StringFormat center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

    static readonly Color TextColor = Color.FromArgb(55, 65, 65);

    public SchemePanel(Station station)
    {
        st = station;
        DoubleBuffered = true;
        ResizeRedraw = true;
        BackColor = Color.FromArgb(143, 191, 186);
        st.Changed += () => Invalidate();
    }

    void UpdateTransform()
    {
        scale = Math.Min(Width / Station.W, Height / Station.H);
        if (scale <= 0) scale = 1;
        off = new PointF((Width - Station.W * scale) / 2f, (Height - Station.H * scale) / 2f);
    }

    PointF ToDesign(Point p) => new((p.X - off.X) / scale, (p.Y - off.Y) / scale);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        UpdateTransform();
        g.TranslateTransform(off.X, off.Y);
        g.ScaleTransform(scale, scale);

        // --- путь ---
        using (var basePen = new Pen(Color.White, 4f))
        {
            foreach (var pts in st.Decor) g.DrawLines(basePen, pts);
            foreach (var pts in st.Segs.Values) g.DrawLines(basePen, pts);
        }

        // --- установленные маршруты ---
        foreach (var r in st.Active)
        {
            using var p = new Pen(r.Shunting ? Color.FromArgb(255, 235, 90) : Color.FromArgb(90, 255, 120), 5f);
            foreach (var id in r.Segs) g.DrawLines(p, st.Segs[id]);
        }

        // --- занятые участки ---
        using (var occ = new Pen(Color.FromArgb(230, 30, 30), 5f))
            foreach (var id in st.Occupied) g.DrawLines(occ, st.Segs[id]);

        // --- стрелки ---
        foreach (var sw in st.Switches.Values)
        {
            string txt = sw.Num + (sw.Plus ? "+" : "−");
            var size = g.MeasureString(txt, fSwitch);
            var rect = new RectangleF(sw.Label.X - size.Width / 2 - 2, sw.Label.Y - size.Height / 2,
                                      size.Width + 4, size.Height);
            if (sw.Locked)
                using (var b = new SolidBrush(Color.FromArgb(255, 225, 90))) g.FillRectangle(b, rect);
            using (var b = new SolidBrush(sw.Plus ? TextColor : Color.FromArgb(170, 60, 0)))
                g.DrawString(txt, fSwitch, b, sw.Label, center);
        }

        // --- светофоры ---
        foreach (var s in st.Signals)
        {
            if (s.Shunting)
            {
                bool on = s.Open || st.TestMode;
                Lamp(g, s.Lamp, on ? Color.White : Color.FromArgb(128, 128, 128));
            }
            else
            {
                bool redOn = !s.Open || st.TestMode;
                bool greenOn = s.Open || st.TestMode;
                Lamp(g, s.Lamp, redOn ? Color.Red : Color.FromArgb(100, 20, 20));
                Lamp(g, new PointF(s.Lamp.X + 21, s.Lamp.Y), greenOn ? Color.LimeGreen : Color.FromArgb(128, 128, 128));
            }
            using var tb = new SolidBrush(TextColor);
            g.DrawString(s.Name, fLabel, tb, s.Label, center);
        }

        // --- подсказка ---
        using (var hb = new SolidBrush(Color.FromArgb(70, 85, 85)))
            g.DrawString("ЛКМ по номеру стрелки — перевести   |   ПКМ по пути — занять / освободить участок",
                         fHelp, hb, 10, Station.H - 24);
    }

    static void Lamp(Graphics g, PointF c, Color color)
    {
        const float r = 9f;
        using var b = new SolidBrush(color);
        using var p = new Pen(Color.FromArgb(90, 90, 90), 1.5f);
        g.FillEllipse(b, c.X - r, c.Y - r, r * 2, r * 2);
        g.DrawEllipse(p, c.X - r, c.Y - r, r * 2, r * 2);
    }

    Switch SwitchAt(PointF p)
    {
        foreach (var sw in st.Switches.Values)
            if (Math.Abs(p.X - sw.Label.X) < 20 && Math.Abs(p.Y - sw.Label.Y) < 12) return sw;
        return null;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Cursor = SwitchAt(ToDesign(e.Location)) != null ? Cursors.Hand : Cursors.Default;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        var p = ToDesign(e.Location);
        if (e.Button == MouseButtons.Left)
        {
            var sw = SwitchAt(p);
            if (sw != null) st.ToggleSwitch(sw.Num);
        }
        else if (e.Button == MouseButtons.Right)
        {
            var id = st.HitSeg(p);
            if (id != null) st.ToggleOccupied(id);
        }
    }
}
