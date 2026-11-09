using System.Drawing.Drawing2D;
using System.Drawing.Text;
using StationEditor.Model;

namespace StationEditor.Ui;

// =====================================================================
//  РИСОВАНИЕ СХЕМЫ (GDI+). Цвета — как у классической палитры программ АКЖТ / Алгабас
//  (Ui/SchemePalette.cs): зелёно-серый фон, белые пути, жёлтый маршрут, красная занятость.
// =====================================================================
public static class Pal
{
    static Color H(string s) => ColorTranslator.FromHtml(s);
    public static readonly Color Bg = H("#7aa69b");
    public static readonly Color Grid = Color.FromArgb(31, 255, 255, 255);
    public static readonly Color GridMajor = Color.FromArgb(71, 255, 255, 255);
    public static readonly Color Track = Color.White;
    public static readonly Color Route = H("#ffeb00");
    public static readonly Color Occ = H("#e11919");
    public static readonly Color Text = H("#233732");
    public static readonly Color Dark = H("#1b2a26");
    public static readonly Color Bad = H("#d4141c");
    public static readonly Color Sel = H("#ffeb00");
    public static readonly Color LampRed = H("#e11919");
    public static readonly Color LampBlue = H("#2b63d9");
    public static readonly Color LampGreen = H("#2bd65a");
    public static readonly Color LampWhite = Color.White;
    public static readonly Color LampOther = H("#6b7472");
    public static readonly Color Road = H("#3c4b47");
    public static readonly Color SecName = H("#16302a");
    public static readonly Color Ghost = Color.FromArgb(140, 0xff, 0xfb, 0xd0);
}

public sealed class SigGeom
{
    public float Ax, Ay, Sy, Side, Fwd, R;
    public PointF[] Lamps;
    public PointF Label;
    public RectangleF Box;
}

public static class Renderer
{
    static PointF Pnt(float x, float y, float cs, string d)
    {
        var D = Core.Dirs[d];
        return new PointF(x + D.Px * cs, y + D.Py * cs);
    }

    static void Poly(Graphics g, Pen p, params PointF[] pts)
    {
        if (pts.Length == 2) g.DrawLine(p, pts[0], pts[1]);
        else g.DrawLines(p, pts);
    }

    static Color A(Color c, int alpha) => alpha >= 255 ? c : Color.FromArgb(alpha * c.A / 255, c);

    // ---------------- шрифты (кэш по размеру) ----------------
    static readonly Dictionary<(int, bool), Font> fonts = new();

    public static Font GetFont(float px, bool bold)
    {
        int s = Math.Max(1, (int)Math.Round(px));
        var key = (s, bold);
        if (!fonts.TryGetValue(key, out var f))
            fonts[key] = f = new Font("Segoe UI", s, bold ? FontStyle.Bold : FontStyle.Regular, GraphicsUnit.Pixel);
        return f;
    }

    static void TrimFonts()
    {
        if (fonts.Count < 80) return;
        foreach (var f in fonts.Values) f.Dispose();
        fonts.Clear();
    }

    static readonly StringFormat Center = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
    };
    static readonly StringFormat LeftMid = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
    };

    // ---------------------------------------------------------------------
    //  КУБИК ПУТИ
    // ---------------------------------------------------------------------
    /// <summary>Нарисовать путь кубика. color — цвет пути, width — толщина.</summary>
    public static void DrawTrack(Graphics g, Tile t, float x, float y, float cs, Color? color = null, float? width = null, bool crossingClosed = false)
    {
        var col = color ?? Pal.Track;
        float w = width ?? Math.Max(2f, cs * 0.13f);
        var c = new PointF(x + cs / 2, y + cs / 2);
        var v = Core.VariantOf(t);
        using var pen = new Pen(col, w) { StartCap = LineCap.Flat, EndCap = LineCap.Flat, LineJoin = LineJoin.Round };

        if (t.T == "switch")
        {
            // остряк и включённое направление — сплошные, второе направление — с разрывом у остряка
            var tr = Pnt(x, y, cs, v.Trunk);
            var pl = Pnt(x, y, cs, v.Plus);
            var mi = Pnt(x, y, cs, v.Minus);
            bool plusOn = t.Plus != false;
            var on = plusOn ? pl : mi;
            var off = plusOn ? mi : pl;
            Poly(g, pen, tr, c, on);
            var gp = new PointF(c.X + (off.X - c.X) * 0.45f, c.Y + (off.Y - c.Y) * 0.45f);
            Poly(g, pen, gp, off);
        }
        else if (t.T == "buffer")
        {
            var e = Pnt(x, y, cs, v.Ends[0]);
            Poly(g, pen, e, c);
            pen.Width = w * 1.2f;
            Poly(g, pen, new PointF(c.X, c.Y - cs * 0.24f), new PointF(c.X, c.Y + cs * 0.24f));
        }
        else if (t.T == "exit")
        {
            // внутрь станции — сплошная линия, наружу — пунктир со стрелкой «уходит дальше»
            var inn = Pnt(x, y, cs, v.Ends[0]);
            var outer = Pnt(x, y, cs, Core.Opp[v.Ends[0]]);
            Poly(g, pen, inn, c);
            pen.DashPattern = new[] { Math.Max(0.1f, cs * 0.1f / w), Math.Max(0.1f, cs * 0.08f / w) };   // в GDI+ штрих — в толщинах пера
            Poly(g, pen, c, outer);
            pen.DashStyle = DashStyle.Solid;
            float s = outer.X < c.X ? -1 : 1;
            pen.Width = Math.Max(1.5f, w * 0.6f);
            Poly(g, pen, new PointF(outer.X - s * cs * 0.18f, outer.Y - cs * 0.16f), outer, new PointF(outer.X - s * cs * 0.18f, outer.Y + cs * 0.16f));
        }
        else
        {
            foreach (var e in Core.EdgesOf(t))
            {
                var pa = Pnt(x, y, cs, e.A);
                var pb = Pnt(x, y, cs, e.B);
                if (Core.Opp[e.A] == e.B) Poly(g, pen, pa, pb);
                else Poly(g, pen, pa, c, pb);
            }
            if (t.T == "crossing")
            {
                using var rp = new Pen(A(Pal.Road, col.A), Math.Max(1f, cs * 0.05f));
                foreach (var k in new[] { 0.28f, 0.72f })
                    g.DrawLine(rp, x + cs * k, y + cs * 0.1f, x + cs * k, y + cs * 0.9f);
                if (crossingClosed)
                {
                    using var b = new SolidBrush(Pal.Occ);
                    g.FillRectangle(b, x + cs * 0.4f, y + cs * 0.08f, cs * 0.2f, cs * 0.14f);
                }
            }
        }
    }

    // ---------------------------------------------------------------------
    //  СВЕТОФОРЫ: «→» — под путём, «←» — над путём (как в SchemeGeometry.Sig);
    //  мачта — на краю клетки по ходу движения, лампы уходят от мачты по ходу движения
    // ---------------------------------------------------------------------
    public static PointF SigAnchor(Tile t, float x, float y, float cs, string dir)
    {
        string want = dir == "R" ? "E" : "W";
        var ends = Core.EndsOf(t).Where(d => (dir == "R") == Core.IsRight(d)).ToList();
        string d = ends.Contains(want) ? want : (ends.Count > 0 ? ends[0] : want);
        return Pnt(x, y, cs, d);
    }

    public static SigGeom GetSigGeom(Tile t, Signal sig, float x, float y, float cs)
    {
        var a = SigAnchor(t, x, y, cs, sig.Dir);
        float side = sig.Dir == "R" ? 1 : -1, fwd = side;
        float r = cs * 0.12f, leg = cs * 0.12f;
        float sy = a.Y + side * cs * 0.4f;
        int n = sig.Kind == "entry" ? 2 : 1;
        var lamps = new PointF[n];
        for (int i = 0; i < n; i++) lamps[i] = new PointF(a.X + fwd * (leg + r + i * 2 * r), sy);
        float minX = a.X, maxX = a.X;
        foreach (var l in lamps)
        {
            float lx = l.X + fwd * r;
            minX = Math.Min(minX, lx); maxX = Math.Max(maxX, lx);
        }
        var label = new PointF(a.X + fwd * (leg + r * n), sy + side * (r + cs * 0.22f));
        return new SigGeom
        {
            Ax = a.X, Ay = a.Y, Sy = sy, Side = side, Fwd = fwd, R = r, Lamps = lamps, Label = label,
            Box = RectangleF.FromLTRB(minX - 3, Math.Min(a.Y, label.Y) - cs * 0.15f, maxX + 3, Math.Max(a.Y, label.Y) + cs * 0.15f),
        };
    }

    public static Color LampColor(Signal sig, bool open)
    {
        if (sig.Kind == "other") return Pal.LampOther;
        if (sig.Kind == "shunt") return open ? Pal.LampWhite : Pal.LampBlue;
        return open ? Pal.LampGreen : Pal.LampRed;
    }

    public static void DrawSignal(Graphics g, Tile t, Signal sig, float x, float y, float cs,
        bool open = false, bool label = true, bool ring = false, Color? ringColor = null, int alpha = 255)
    {
        var gm = GetSigGeom(t, sig, x, y, cs);
        using var pen = new Pen(A(Pal.Dark, alpha), Math.Max(1f, cs * 0.045f));
        g.DrawLines(pen, new[]
        {
            new PointF(gm.Ax, gm.Ay + gm.Side * cs * 0.1f), new PointF(gm.Ax, gm.Sy), new PointF(gm.Ax + gm.Fwd * cs * 0.12f, gm.Sy),
        });
        using (var fill = new SolidBrush(A(LampColor(sig, open), alpha)))
        {
            foreach (var l in gm.Lamps)
            {
                var rc = new RectangleF(l.X - gm.R, l.Y - gm.R, 2 * gm.R, 2 * gm.R);
                g.FillEllipse(fill, rc);
                g.DrawEllipse(pen, rc);
            }
        }
        if (label && cs >= 14)
        {
            bool named = !string.IsNullOrEmpty(sig.Name);
            using var tb = new SolidBrush(A(named ? Pal.Text : Pal.Bad, alpha));
            g.DrawString(named ? sig.Name : "?", GetFont(cs * 0.27f, true), tb, gm.Label, Center);
        }
        if (ring)
        {
            using var rp = new Pen(ringColor ?? Pal.Sel, 2) { DashPattern = new[] { 2f, 1.5f } };
            g.DrawRectangle(rp, gm.Box.X, gm.Box.Y, gm.Box.Width, gm.Box.Height);
        }
    }

    /// <summary>Где написать номер стрелки: с той стороны, куда НЕ уходит отклонение.</summary>
    public static PointF SwitchLabelPos(Tile t, float x, float y, float cs)
    {
        bool below = Core.VariantOf(t).Minus.Contains('N');
        return new PointF(x + cs * 0.5f, y + (below ? cs * 0.9f : cs * 0.1f));
    }

    /// <summary>Чёрточка стыка на выходе dir.</summary>
    static void DrawJoint(Graphics g, Pen p, float x, float y, float cs, string dir)
    {
        var pt = Pnt(x, y, cs, dir);
        var D = Core.Dirs[dir];
        float len = MathF.Sqrt(D.Dx * D.Dx + D.Dy * D.Dy);
        float nx = -D.Dy / len, ny = D.Dx / len;
        float h = cs * 0.2f;
        g.DrawLine(p, pt.X - nx * h, pt.Y - ny * h, pt.X + nx * h, pt.Y + ny * h);
    }

    // ---------------------------------------------------------------------
    //  ЗНАЧКИ ДЛЯ ПАНЕЛИ
    // ---------------------------------------------------------------------
    public static void DrawTileIcon(Graphics g, Tile t, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, r);
        float size = Math.Min(r.Width, r.Height);
        float pad = size * 0.12f;
        DrawTrack(g, t, r.X + pad, r.Y + pad, size - 2 * pad, width: Math.Max(2, size * 0.09f));
    }

    public static void DrawSignalIcon(Graphics g, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, r);
        float size = Math.Min(r.Width, r.Height);
        var t = new Tile { T = "straight", V = 0 };
        float cs = size * 0.75f, x = r.X + size * 0.25f - cs, y = r.Y + size * 0.3f - cs / 2;
        var clip = g.Clip;
        g.SetClip(r);
        DrawTrack(g, t, x, y, cs, width: size * 0.08f);
        DrawTrack(g, t, x + cs, y, cs, width: size * 0.08f);
        DrawSignal(g, t, new Signal { Dir = "R", Kind = "entry", Name = "" }, x, y, cs, label: false);
        g.Clip = clip;
    }

    // ---------------------------------------------------------------------
    //  ВСЯ СХЕМА
    // ---------------------------------------------------------------------
    public static void Render(Graphics g, int W, int H, Station st, UiState ui)
    {
        TrimFonts();
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        float cs = Core.Cell * ui.Cam.Z;
        float ox = ui.Cam.X, oy = ui.Cam.Y;
        bool test = ui.IsTest;
        var T = ui.Test;
        float SX(float gx) => ox + gx * cs;
        float SY(float gy) => oy + gy * cs;

        g.Clear(Pal.Bg);

        int gx0 = (int)Math.Floor(-ox / cs) - 1, gx1 = (int)Math.Ceiling((W - ox) / cs) + 1;
        int gy0 = (int)Math.Floor(-oy / cs) - 1, gy1 = (int)Math.Ceiling((H - oy) / cs) + 1;
        bool Visible(int x, int y) => x >= gx0 && x <= gx1 && y >= gy0 && y <= gy1;

        // ---- сетка ----
        if (ui.ShowGrid && !test && cs >= 8)
        {
            g.SmoothingMode = SmoothingMode.None;
            using var pm = new Pen(Pal.Grid, 1);
            using var pM = new Pen(Pal.GridMajor, 1);
            for (int gx = gx0; gx <= gx1; gx++)
            {
                float px = MathF.Round(SX(gx));
                g.DrawLine(gx % 5 == 0 ? pM : pm, px, 0, px, H);
            }
            for (int gy = gy0; gy <= gy1; gy++)
            {
                float py = MathF.Round(SY(gy));
                g.DrawLine(gy % 5 == 0 ? pM : pm, 0, py, W, py);
            }
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }

        var entries = new List<(string K, int X, int Y, Tile T)>();
        foreach (var (k, t) in st.Tiles)
        {
            var (x, y) = Core.Parse(k);
            if (Visible(x, y)) entries.Add((k, x, y, t));
        }
        var hidden = ui.Moving?.Keys;    // клетки, которые сейчас перетаскивают
        bool IsHidden(string k) => hidden != null && hidden.Contains(k);

        // ---- цвета участков ----
        if (ui.ShowSecColors && !test)
        {
            g.SmoothingMode = SmoothingMode.None;
            var brushes = new Dictionary<string, SolidBrush>();
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.T.Sec) || IsHidden(e.K)) continue;
                if (!brushes.TryGetValue(e.T.Sec, out var b))
                    brushes[e.T.Sec] = b = new SolidBrush(Color.FromArgb(97, Core.SecColor(st, e.T.Sec)));
                g.FillRectangle(b, SX(e.X), SY(e.Y), cs, cs);
            }
            foreach (var b in brushes.Values) b.Dispose();
            g.SmoothingMode = SmoothingMode.AntiAlias;
        }
        if (ui.FlashSec != null && !test)
        {
            using var p = new Pen(Pal.Sel, 3);
            foreach (var e in entries)
                if (e.T.Sec == ui.FlashSec) g.DrawRectangle(p, SX(e.X) + 2, SY(e.Y) + 2, cs - 4, cs - 4);
        }

        // ---- пути ----
        foreach (var e in entries)
        {
            if (IsHidden(e.K)) continue;
            var color = Pal.Track;
            bool hasSec = !string.IsNullOrEmpty(e.T.Sec);
            if (test)
            {
                if (hasSec && T.Occ.Contains(e.T.Sec)) color = Pal.Occ;
                else if (T.RouteTiles.Contains(e.K)) color = Pal.Route;
            }
            else if (ui.Preview != null && ui.Preview.Tiles.Contains(e.K)) color = Pal.Route;
            DrawTrack(g, e.T, SX(e.X), SY(e.Y), cs, color,
                crossingClosed: test && e.T.T == "crossing" && hasSec && T.Occ.Contains(e.T.Sec));
        }

        // ---- стыки (где соседние клетки — разные участки) и разрывы ----
        using (var jp = new Pen(Pal.Dark, Math.Max(1.5f, cs * 0.06f)))
        using (var bad = new SolidBrush(Pal.Bad))
        {
            float rad = Math.Max(3, cs * 0.09f);
            foreach (var e in entries)
            {
                if (IsHidden(e.K)) continue;
                foreach (var d in Core.EndsOf(e.T))
                {
                    var nb = Core.NeighborOf(st, e.K, d);
                    if (nb != null)
                    {
                        if (Core.IsRight(d) && (e.T.Sec ?? "") != (nb.Tile.Sec ?? ""))
                            DrawJoint(g, jp, SX(e.X), SY(e.Y), cs, d);
                    }
                    else if (!test && e.T.T != "exit")
                    {
                        var p = Pnt(SX(e.X), SY(e.Y), cs, d);
                        g.FillEllipse(bad, p.X - rad, p.Y - rad, 2 * rad, 2 * rad);
                    }
                }
            }
        }

        // ---- номера стрелок ----
        if (cs >= 14)
        {
            var f = GetFont(cs * 0.3f, true);
            using var bt = new SolidBrush(Pal.Text);
            using var bb = new SolidBrush(Pal.Bad);
            foreach (var e in entries)
            {
                if (e.T.T != "switch" || IsHidden(e.K)) continue;
                var lp = SwitchLabelPos(e.T, SX(e.X), SY(e.Y), cs);
                bool named = !string.IsNullOrEmpty(e.T.Name);
                g.DrawString(named ? e.T.Name : "?", f, named ? bt : bb, lp, Center);
            }
        }

        // ---- названия участков ----
        if (ui.ShowSecNames && cs >= 14)
        {
            var f = GetFont(cs * 0.26f, false);
            using var b = new SolidBrush(Pal.SecName);
            foreach (var (sec, keys) in Core.TilesBySection(st))
            {
                var flat = keys.Where(k => { var t = st.Tiles[k]; return (t.T == "straight" && t.V == 0) || t.T == "crossing"; }).ToList();
                var pool = (flat.Count > 0 ? flat : keys).Select(Core.Parse).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
                var (x, y) = pool[(pool.Count - 1) / 2];
                if (!Visible(x, y)) continue;
                g.DrawString(sec, f, b, new PointF(SX(x) + cs / 2, SY(y) + cs * 0.88f), Center);
            }
        }

        // ---- надписи ----
        using (var b = new SolidBrush(Pal.Text))
        {
            foreach (var (k, tx) in st.Texts)
            {
                var (x, y) = Core.Parse(k);
                if (!Visible(x, y) || IsHidden(k)) continue;
                var f = GetFont(Math.Max(6, (float)Math.Round((tx.Size > 0 ? tx.Size : 14) * ui.Cam.Z)), true);
                g.DrawString(tx.Text, f, b, new PointF(SX(x) + 3, SY(y) + cs / 2), LeftMid);
            }
        }

        // ---- светофоры ----
        foreach (var e in entries)
        {
            if (!e.T.HasSigs || IsHidden(e.K)) continue;
            for (int i = 0; i < e.T.Sigs.Count; i++)
            {
                var r = new SigRef(e.K, i);
                bool pending = test && T.Pending == r;
                bool selected = !test && ui.SelSig == r;
                DrawSignal(g, e.T, e.T.Sigs[i], SX(e.X), SY(e.Y), cs,
                    open: test && T.OpenSigs.Contains(e.K + "#" + i),
                    ring: selected || pending,
                    ringColor: pending ? Color.White : Pal.Sel);
            }
        }

        // ---- перетаскивание выделения ----
        if (ui.Moving != null)
        {
            int dx = ui.Moving.Dx, dy = ui.Moving.Dy;
            using var b = new SolidBrush(Color.FromArgb(179, Pal.Text));
            foreach (var k in ui.Moving.Keys)
            {
                var (x, y) = Core.Parse(k);
                if (st.Tiles.TryGetValue(k, out var t))
                {
                    DrawTrack(g, t, SX(x + dx), SY(y + dy), cs, Color.FromArgb(179, Color.White));
                    if (t.Sigs != null)
                        foreach (var s in t.Sigs) DrawSignal(g, t, s, SX(x + dx), SY(y + dy), cs, alpha: 179);
                }
                if (st.Texts.TryGetValue(k, out var tx))
                {
                    var f = GetFont((float)Math.Round((tx.Size > 0 ? tx.Size : 14) * ui.Cam.Z), true);
                    g.DrawString(tx.Text, f, b, new PointF(SX(x + dx) + 3, SY(y + dy) + cs / 2), LeftMid);
                }
            }
        }

        // ---- выделение ----
        if (!test && ui.Sel.Count > 0)
        {
            using var p = new Pen(Pal.Sel, 2) { DashPattern = new[] { 2.5f, 1.5f } };
            int dx = ui.Moving?.Dx ?? 0, dy = ui.Moving?.Dy ?? 0;
            foreach (var k in ui.Sel)
            {
                var (x, y) = Core.Parse(k);
                g.DrawRectangle(p, SX(x + dx) + 1, SY(y + dy) + 1, cs - 2, cs - 2);
            }
        }
        if (ui.Band != null)
        {
            var bd = ui.Band;
            int x1 = Math.Min(bd.X1, bd.X2), y1 = Math.Min(bd.Y1, bd.Y2);
            float bw = (Math.Abs(bd.X2 - bd.X1) + 1) * cs, bh = (Math.Abs(bd.Y2 - bd.Y1) + 1) * cs;
            using var fb = new SolidBrush(Color.FromArgb(31, 255, 235, 0));
            using var p = new Pen(Pal.Sel, 1) { DashPattern = new[] { 4f, 3f } };
            g.FillRectangle(fb, SX(x1), SY(y1), bw, bh);
            g.DrawRectangle(p, SX(x1), SY(y1), bw, bh);
        }

        // ---- подсказка под курсором (что поставится) ----
        if (ui.Hover is Point hv && !test)
        {
            if (ui.Ghost != null) DrawTrack(g, ui.Ghost, SX(hv.X), SY(hv.Y), cs, Pal.Ghost);
            using var p = new Pen(ui.HoverColor ?? Color.FromArgb(204, 255, 255, 255), 2);
            g.DrawRectangle(p, SX(hv.X) + 1, SY(hv.Y) + 1, cs - 2, cs - 2);
        }
    }
}
