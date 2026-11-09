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
    public static readonly Color LampYellow = H("#ffd21f");     // [ДОБАВЛЕНО 05.11.2026] показания в «Испытании»
    public static readonly Color LampOff = H("#2f3b38");
    public static readonly Color LampPaper = H("#f6f3e8");     // [ДОБАВЛЕНО 09.11.2026] «бумага» огня в знаках ГОСТ 2.749-84
    public static readonly Color Building = H("#f3eddc");
    public static readonly Color Stone = H("#d8cdb0");
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
    /// <summary>[ДОБАВЛЕНО 09.11.2026] Мачтовый в виде «Огни по ГОСТ»: знак мачты «⊢», ножка, огни в ряд (РУ-30-80)</summary>
    public bool MastRow;
    /// <summary>Промежуток перед второй лампой (пригласительный входного — отдельно, через чёрточку)</summary>
    public float Gap;
    /// <summary>[ДОБАВЛЕНО 09.11.2026] Карликовый в виде ГОСТ: огни блоком у основания «|», основание — от Bar0 до Bar1 по Y</summary>
    public bool DwarfBlock;
    public float Bar0, Bar1;
    /// <summary>[ДОБАВЛЕНО 09.11.2026] Поворот светофора вокруг точки (Ax, Ay), градусы: светофор лежит параллельно пути
    /// у края клетки (на наклонном пути и на изгибе — тоже, как Ч22 на стр. 89 РУ-30-80). Box и всё остальное — до поворота.</summary>
    public float Angle;

    /// <summary>Попадает ли точка экрана в светофор (с учётом поворота).</summary>
    public bool Hit(float mx, float my)
    {
        if (Angle != 0)
        {
            double a = -Angle * Math.PI / 180, dx = mx - Ax, dy = my - Ay;
            mx = (float)(Ax + dx * Math.Cos(a) - dy * Math.Sin(a));
            my = (float)(Ay + dx * Math.Sin(a) + dy * Math.Cos(a));
        }
        return Box.Contains(mx, my);
    }
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
    static readonly StringFormat RightBottom = new(StringFormat.GenericTypographic)
    {
        Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Far,
        FormatFlags = StringFormatFlags.NoWrap | StringFormatFlags.NoClip,
    };

    // ---------------------------------------------------------------------
    //  КУБИК ПУТИ
    // ---------------------------------------------------------------------
    /// <summary>Мигающие огни горят в первую половину секунды.</summary>
    public static bool BlinkOn => Environment.TickCount64 / 500 % 2 == 0;

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Огни светофоров — графическими знаками ГОСТ 2.749-84 (табл. 6) вместо цвета.</summary>
    public static bool GostLamps;

    /// <summary>Нарисовать путь кубика. color — цвет пути, width — толщина.
    /// noControl — стрелка без контроля положения (переводится): оба направления с разрывом;
    /// lost — потеряла контроль после работы на фрикцию: ещё и мигающая красная метка.</summary>
    public static void DrawTrack(Graphics g, Tile t, float x, float y, float cs, Color? color = null, float? width = null, bool crossingClosed = false,
                                 bool noControl = false, bool lost = false)
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
            PointF Gap(PointF p) => new(c.X + (p.X - c.X) * 0.45f, c.Y + (p.Y - c.Y) * 0.45f);
            if (noControl)
            {
                // [ДОБАВЛЕНО 05.11.2026] стрелка без контроля: остряк ни к одному направлению не прижат
                Poly(g, pen, tr, c);
                Poly(g, pen, Gap(pl), pl);
                Poly(g, pen, Gap(mi), mi);
                if (lost && BlinkOn)
                {
                    using var rb = new SolidBrush(Pal.Occ);
                    float rr = cs * 0.16f;
                    g.FillEllipse(rb, c.X - rr, c.Y - rr, 2 * rr, 2 * rr);
                }
            }
            else
            {
                Poly(g, pen, tr, c, on);
                Poly(g, pen, Gap(off), off);
            }
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
    /// <summary>Выход клетки, у которого стоит светофор (по ходу движения).</summary>
    static string SigOutDir(Tile t, string dir)
    {
        string want = dir == "R" ? "E" : "W";
        var ends = Core.EndsOf(t).Where(d => (dir == "R") == Core.IsRight(d)).ToList();
        return ends.Contains(want) ? want : (ends.Count > 0 ? ends[0] : want);
    }

    public static PointF SigAnchor(Tile t, float x, float y, float cs, string dir) => Pnt(x, y, cs, SigOutDir(t, dir));

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Угол пути у светофора, градусы (0 — горизонталь; ±45 — наклонный путь).</summary>
    public static float SigAngle(Tile t, string dir)
    {
        var d = Core.Dirs[SigOutDir(t, dir)];
        double a = Math.Atan2(d.Dy, d.Dx) - Math.Atan2(0, dir == "R" ? 1 : -1);    // путь против «горизонтального» хода
        double deg = a * 180 / Math.PI;
        while (deg > 180) deg -= 360;
        while (deg <= -180) deg += 360;
        return (float)deg;
    }

    public static SigGeom GetSigGeom(Tile t, Signal sig, float x, float y, float cs, bool diamond = false)
    {
        var gm = GetSigGeomFlat(t, sig, x, y, cs, diamond);
        gm.Angle = SigAngle(t, sig.Dir);
        return gm;
    }

    static SigGeom GetSigGeomFlat(Tile t, Signal sig, float x, float y, float cs, bool diamond)
    {
        var a = SigAnchor(t, x, y, cs, sig.Dir);
        float side = sig.Dir == "R" ? 1 : -1, fwd = side;
        float r = cs * 0.12f, leg = cs * 0.12f;
        bool dwarf = Aspects.IsDwarf(sig, t);
        // [ИЗМЕНЕНО 09.11.2026] в виде «Огни по ГОСТ» — все лампы головки вплотную, как цельные светофоры в РУ-30-80:
        // мачтовый — «⊢» и ножка, огни в ряд вдоль пути («1 ⊢●○⊘», «Н1 ⊢◎⊘●○⊘», «◆—⊣З», стр. 9, 49);
        // у входного пригласительный отделён чёрточкой («Н ⊢◎-⊘●○⊘», стр. 9, 49); карликовый — см. ниже
        int n = GostLamps ? Aspects.FullHead(sig, t, diamond).Length : Aspects.LampCount(sig, t);
        float gap = GostLamps && Aspects.InvitationApart(sig) && n > 1 ? r * 0.8f : 0;
        float Off(int i) => (diamond ? r * 1.3f : r) + i * 2 * r + (i >= 1 ? gap : 0);    // от конца ножки до центра i-й лампы
        var lamps = new PointF[n];
        if (GostLamps && dwarf)
        {
            // [ДОБАВЛЕНО 09.11.2026] карликовый: огни сразу у основания «|», без ножки.
            // 1–3 огня — в ряд («НД |○○» стр. 9, «М1 |◎◎», «Нд |◎●⊘» стр. 49); 4 — квадратом 2×2 (как Н3 на стр. 9:
            // у пути красный и зелёный, дальше лунно-белый и жёлтый, у основания красный и лунно-белый); 5 — три слева, два справа.
            // (столбец от основания, ряд от пути) для каждой лампы головки
            (int C, int Rw)[] cells = n switch
            {
                1 => new[] { (0, 0) },
                2 => new[] { (0, 0), (1, 0) },
                3 => new[] { (0, 0), (1, 0), (2, 0) },
                4 => new[] { (0, 1), (0, 0), (1, 0), (1, 1) },
                5 => new[] { (0, 0), (0, 1), (0, 2), (1, 0), (1, 1) },
                _ => Enumerable.Range(0, n).Select(i => (i, 0)).ToArray(),
            };
            float start = a.Y + side * cs * 0.15f;    // край огней, ближний к пути
            int cols = cells.Max(c => c.C) + 1, rows = cells.Max(c => c.Rw) + 1;
            for (int i = 0; i < n; i++)
                lamps[i] = new PointF(a.X + fwd * (r + cells[i].C * 2 * r), start + side * (r + cells[i].Rw * 2 * r));
            float end = start + side * rows * 2 * r;
            var lab = new PointF(a.X + fwd * cols * r, end + side * cs * 0.2f);
            float bx0 = Math.Min(a.X, a.X + fwd * cols * 2 * r), bx1 = Math.Max(a.X, a.X + fwd * cols * 2 * r);
            return new SigGeom
            {
                Ax = a.X, Ay = a.Y, Sy = start, Side = side, Fwd = fwd, R = r, Lamps = lamps, Label = lab,
                DwarfBlock = true, Bar0 = start, Bar1 = end,
                Box = RectangleF.FromLTRB(bx0 - 3, Math.Min(a.Y, lab.Y) - cs * 0.15f, bx1 + 3, Math.Max(a.Y, lab.Y) + cs * 0.15f),
            };
        }
        // карликовый — ниже, у самого пути; мачтовый в виде ГОСТ — ряд огней на уровне знака мачты «⊢»
        float sy = a.Y + side * cs * (dwarf ? 0.27f : GostLamps ? 0.34f : 0.4f);
        for (int i = 0; i < n; i++) lamps[i] = new PointF(a.X + fwd * (leg + Off(i)), sy);
        float minX = a.X, maxX = a.X;
        foreach (var l in lamps)
        {
            float lx = l.X + fwd * r;
            minX = Math.Min(minX, lx); maxX = Math.Max(maxX, lx);
        }
        var label = new PointF(a.X + fwd * (leg + (Off(0) + Off(n - 1)) / 2), sy + side * (r + cs * 0.22f));
        return new SigGeom
        {
            Ax = a.X, Ay = a.Y, Sy = sy, Side = side, Fwd = fwd, R = r, Lamps = lamps, Label = label,
            MastRow = GostLamps && !dwarf, Gap = gap,
            Box = RectangleF.FromLTRB(minX - 3, Math.Min(a.Y, label.Y) - cs * 0.15f, maxX + 3, Math.Max(a.Y, label.Y) + cs * 0.15f),
        };
    }

    public static Color LampColor(Lamp l) => l switch
    {
        Lamp.Red => Pal.LampRed, Lamp.Yellow => Pal.LampYellow, Lamp.Green => Pal.LampGreen, Lamp.White => Pal.LampWhite,
        Lamp.Blue => Pal.LampBlue, Lamp.Gray => Pal.LampOther, _ => Pal.LampOff,
    };

    /// <summary>Светофор. aspect — показание в «Испытании» (null — цвет по виду светофора, как в редакторе).
    /// [ИЗМЕНЕНО 05.11.2026] огни по показанию (мигающие мигают), мачта заградительного — в чёрно-белые полосы,
    /// у повторительного одна, у предупредительного две поперечные черты на мачте.</summary>
    public static void DrawSignal(Graphics g, Tile t, Signal sig, float x, float y, float cs,
        Aspect aspect = null, bool label = true, bool ring = false, Color? ringColor = null, int alpha = 255, bool? diamond = null)
    {
        // огонь ромбом: по умолчанию — заградительный и повторительный; предупредительный к заградительному
        // зависит от схемы (что впереди), поэтому его передаёт Render (Aspects.IsDiamond)
        bool dia = diamond ?? (sig.Kind is "barrier" or "repeater");
        var gm = GetSigGeom(t, sig, x, y, cs, dia);
        // [ДОБАВЛЕНО 09.11.2026] светофор лежит параллельно пути: поворачиваем всё (огни, мачту, подпись) вокруг точки привязки
        var turned = gm.Angle != 0 ? g.Save() : null;
        if (turned != null)
        {
            g.TranslateTransform(gm.Ax, gm.Ay);
            g.RotateTransform(gm.Angle);
            g.TranslateTransform(-gm.Ax, -gm.Ay);
        }
        aspect ??= Aspects.Static(sig, t);
        // в виде «Огни по ГОСТ» — полная головка: горящие огни раскладываются по её лампам
        var fullHead = GostLamps ? Aspects.FullHead(sig, t, dia) : null;
        var shown = GostLamps ? Aspects.OnFullHead(fullHead, aspect.Lamps) : aspect.Lamps;
        float lw = Math.Max(1f, cs * 0.045f);
        using var pen = new Pen(A(Pal.Dark, alpha), lw);
        var foot = new PointF(gm.Ax, gm.Ay + gm.Side * cs * 0.1f);
        var top = new PointF(gm.Ax, gm.Sy);
        if (gm.MastRow)
        {
            // мачтовый как в РУ-30-80: знак мачты «⊢» (поперечная черта) и ножка до огней; пригласительный — через чёрточку
            g.DrawLine(pen, gm.Ax, gm.Sy - gm.R, gm.Ax, gm.Sy + gm.R);
            g.DrawLine(pen, gm.Ax, gm.Sy, gm.Ax + gm.Fwd * cs * 0.12f, gm.Sy);
            if (gm.Gap > 0 && gm.Lamps.Length > 1)
                g.DrawLine(pen, gm.Lamps[0].X + gm.Fwd * gm.R, gm.Sy, gm.Lamps[1].X - gm.Fwd * gm.R, gm.Sy);
        }
        else if (sig.Kind == "barrier")
        {
            using var white = new Pen(A(Color.White, alpha), lw * 2.2f);
            using var black = new Pen(A(Pal.Dark, alpha), lw * 2.2f) { DashPattern = new[] { 1.2f, 1.2f } };
            g.DrawLine(white, foot, top);
            g.DrawLine(black, foot, top);
            g.DrawLine(pen, top, new PointF(gm.Ax + gm.Fwd * cs * 0.12f, gm.Sy));
        }
        else if (gm.DwarfBlock)
            g.DrawLine(pen, gm.Ax, gm.Bar0, gm.Ax, gm.Bar1);    // основание во всю высоту огней, без ножки
        else if (Aspects.IsDwarf(sig, t))
        {
            // [ДОБАВЛЕНО 09.11.2026] карликовый — без мачты: основание «|» у ламп, как на схемах РУ-30-80
            g.DrawLine(pen, gm.Ax, gm.Sy - gm.R, gm.Ax, gm.Sy + gm.R);
            g.DrawLine(pen, gm.Ax, gm.Sy, gm.Ax + gm.Fwd * cs * 0.12f, gm.Sy);
        }
        else
            g.DrawLines(pen, new[] { foot, top, new PointF(gm.Ax + gm.Fwd * cs * 0.12f, gm.Sy) });
        // черты на мачте — только в цветном виде (в РУ-30-80 и ГОСТ предупредительный и повторительный без черт)
        int ticks = gm.MastRow ? 0 : sig.Kind == "repeater" ? 1 : sig.Kind == "warning" ? 2 : 0;
        for (int i = 0; i < ticks; i++)
        {
            float ty = foot.Y + (top.Y - foot.Y) * (0.45f + 0.25f * i);
            g.DrawLine(pen, gm.Ax - cs * 0.07f, ty, gm.Ax + cs * 0.07f, ty);
        }
        for (int i = 0; i < gm.Lamps.Length; i++)
        {
            var l = gm.Lamps[i];
            var lit = i < shown.Length ? shown[i] : new LampLit(Lamp.Off);
            if (GostLamps)
            {
                // негорящая лампа рисуется бледно знаком своего цвета из полной головки
                bool on = lit.Color != Lamp.Off;
                DrawGostLamp(g, on ? lit.Color : fullHead[i], on, lit.Blink, l, gm.R, dia, lw, alpha);
                continue;
            }
            var col = lit.Blink && !BlinkOn ? Pal.LampOff : LampColor(lit.Color);
            using var fill = new SolidBrush(A(col, alpha));
            if (dia)
            {
                // [ИЗМЕНЕНО 09.11.2026] заградительный и повторительный — ромб: «◆⊣З» в РУ-30-80, стр. 49;
                // «фоновые щиты повторительных и заградительных светофоров имеют форму ромба» (caredenis.ru, les03)
                float d = gm.R * 1.3f;
                var rh = new[] { new PointF(l.X - d, l.Y), new PointF(l.X, l.Y - d * 0.8f), new PointF(l.X + d, l.Y), new PointF(l.X, l.Y + d * 0.8f) };
                g.FillPolygon(fill, rh);
                g.DrawPolygon(pen, rh);
                continue;
            }
            var rc = new RectangleF(l.X - gm.R, l.Y - gm.R, 2 * gm.R, 2 * gm.R);
            g.FillEllipse(fill, rc);
            g.DrawEllipse(pen, rc);
        }
        if (label && cs >= 14)
        {
            bool named = !string.IsNullOrEmpty(sig.Name);
            using var tb = new SolidBrush(A(named ? Pal.Text : Pal.Bad, alpha));
            // [ДОБАВЛЕНО 09.11.2026] «только чтение» (показание с другой станции) — значок ↔ у имени
            var text = (named ? sig.Name : "?") + (sig.IsReadOnly ? " ↔" : "");
            g.DrawString(text, GetFont(cs * 0.27f, true), tb, gm.Label, Center);
        }
        if (ring)
        {
            using var rp = new Pen(ringColor ?? Pal.Sel, 2) { DashPattern = new[] { 2f, 1.5f } };
            g.DrawRectangle(rp, gm.Box.X, gm.Box.Y, gm.Box.Width, gm.Box.Height);
        }
        if (turned != null) g.Restore(turned);
    }

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Огонь знаком ГОСТ 2.749-84, табл. 6 (как на чертежах РУ-30-80):
    /// красный — залитый круг; зелёный — пустой круг; жёлтый — заштрихованный; лунно-белый — круг с меньшим кругом внутри;
    /// синий — круг с залитым меньшим кругом внутри; мигающий — четыре коротких луча от круга.
    /// Ромб (заградительный, повторительный — табл. 5, п. 7, 9) заполняется так же.
    /// Горящий огонь — чёрным по светлому, негорящий — бледно (видно, какая это лампа).</summary>
    static void DrawGostLamp(Graphics g, Lamp c, bool on, bool blink, PointF p, float r, bool diamond, float lw, int alpha)
    {
        int a = on ? alpha : alpha * 40 / 100;
        lw = Math.Max(1f, lw * 0.6f);    // знаки — тонкой линией, иначе круг в круге и точка в круге сливаются
        using var ink = new SolidBrush(A(Pal.Dark, a));
        using var pen = new Pen(A(Pal.Dark, a), lw);
        using var paper = new SolidBrush(A(Pal.LampPaper, on ? alpha : alpha * 55 / 100));
        using var path = new GraphicsPath();
        float d = r * 1.3f;
        if (diamond) path.AddPolygon(new[] { new PointF(p.X - d, p.Y), new PointF(p.X, p.Y - d * 0.8f), new PointF(p.X + d, p.Y), new PointF(p.X, p.Y + d * 0.8f) });
        else path.AddEllipse(p.X - r, p.Y - r, 2 * r, 2 * r);
        g.FillPath(paper, path);
        float ir = r * 0.52f;    // меньший круг внутри (у синего — залитая точка поменьше)
        switch (c)
        {
            case Lamp.Red:
                g.FillPath(ink, path);
                break;
            case Lamp.Yellow:
            {
                var clip = g.Clip;
                g.SetClip(path, CombineMode.Intersect);
                using var hp = new Pen(A(Pal.Dark, a), Math.Max(1f, lw * 0.8f));
                float step = Math.Max(2.5f, r * 0.55f), span = diamond ? d : r;
                for (float k = -2 * span; k <= 2 * span; k += step)
                    g.DrawLine(hp, p.X + k - span, p.Y + span, p.X + k + span, p.Y - span);
                g.Clip = clip;
                break;
            }
            case Lamp.White:
                g.DrawEllipse(pen, p.X - ir, p.Y - ir, 2 * ir, 2 * ir);
                break;
            case Lamp.Blue:
            {
                float br = r * 0.4f;
                g.FillEllipse(ink, p.X - br, p.Y - br, 2 * br, 2 * br);
                break;
            }
            case Lamp.Gray:    // соседний — неизвестно, что горит
                pen.DashPattern = new[] { 1.5f, 1.5f };
                break;
        }
        g.DrawPath(pen, path);
        if (blink)
        {
            using var rp = new Pen(A(Pal.Dark, a), lw);
            float r1 = (diamond ? d * 0.75f : r) * 1.12f, r2 = r1 + r * 0.5f;
            foreach (var (dx, dy) in new[] { (1, 1), (1, -1), (-1, 1), (-1, -1) })
            {
                float k = 0.7071f;
                g.DrawLine(rp, p.X + dx * r1 * k, p.Y + dy * r1 * k, p.X + dx * r2 * k, p.Y + dy * r2 * k);
            }
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

    /// <summary>[ДОБАВЛЕНО 05.11.2026] Значок объекта для палитры.</summary>
    public static void DrawObjectIcon(Graphics g, string type, RectangleF r)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        using (var b = new SolidBrush(Pal.Bg)) g.FillRectangle(b, r);
        var ot = Core.ObjTypeOf(type);
        float k = Math.Min(r.Width / ot.W, r.Height / ot.H) * 0.86f;
        float w = ot.W * k, h = ot.H * k;
        var o = new StationObject { Type = type, W = ot.W, H = ot.H, Name = type == "building" ? "Вокзал" : "" };
        DrawObject(g, o, new RectangleF(r.X + (r.Width - w) / 2, r.Y + (r.Height - h) / 2, w, h), type == "ammeter" ? 1.8 : 0, icon: true);
    }

    // ---------------------------------------------------------------------
    //  [ДОБАВЛЕНО 05.11.2026] ОБЪЕКТЫ: АМПЕРМЕТР, ВОКЗАЛ, ЧАСОВАЯ БАШНЯ
    // ---------------------------------------------------------------------
    public static void DrawObject(Graphics g, StationObject o, RectangleF r, double amp, bool icon = false)
    {
        float lw = Math.Max(1f, Math.Min(r.Width, r.Height) * 0.03f);
        using var dark = new Pen(Pal.Dark, lw);
        switch (o.Type)
        {
            case "ammeter": DrawAmmeter(g, o, r, amp, dark, icon); break;
            case "clock": DrawClock(g, o, r, dark, icon); break;
            default: DrawBuilding(g, o, r, dark, icon); break;
        }
    }

    static void DrawAmmeter(Graphics g, StationObject o, RectangleF r, double amp, Pen dark, bool icon)
    {
        // корпус прибора
        using (var body = new SolidBrush(Color.FromArgb(0x26, 0x2e, 0x2c))) g.FillRectangle(body, r);
        var face = RectangleF.Inflate(r, -r.Width * 0.05f, -r.Height * 0.07f);
        using (var fb = new SolidBrush(Color.FromArgb(0xfb, 0xf8, 0xee))) g.FillRectangle(fb, face);
        // шкала 0..5 А дугой 100°
        var c = new PointF(face.X + face.Width / 2, face.Bottom - face.Height * 0.12f);
        float rad = Math.Min(face.Width * 0.46f, face.Height * 0.78f);
        const float a0 = -140f, sweep = 100f;
        double max = SwitchDrive.ScaleMax;
        PointF At(double v, float rr)
        {
            double ang = (a0 + sweep * Math.Clamp(v / max, 0, 1)) * Math.PI / 180;
            return new PointF(c.X + (float)Math.Cos(ang) * rr, c.Y + (float)Math.Sin(ang) * rr);
        }
        using var thin = new Pen(Pal.Dark, Math.Max(1f, rad * 0.025f));
        g.DrawArc(thin, c.X - rad, c.Y - rad, 2 * rad, 2 * rad, a0, sweep);
        // зона рабочего тока — зелёная, тока фрикции — жёлтая
        using (var zw = new Pen(Color.FromArgb(0x2b, 0xa8, 0x55), Math.Max(2f, rad * 0.06f)))
            g.DrawArc(zw, c.X - rad * 0.93f, c.Y - rad * 0.93f, rad * 1.86f, rad * 1.86f,
                a0 + sweep * (float)(1.2 / max), sweep * (float)((2.0 - 1.2) / max));
        using (var zf = new Pen(Color.FromArgb(0xe0, 0xa8, 0x1a), Math.Max(2f, rad * 0.06f)))
            g.DrawArc(zf, c.X - rad * 0.93f, c.Y - rad * 0.93f, rad * 1.86f, rad * 1.86f,
                a0 + sweep * (float)(2.2 / max), sweep * (float)((2.8 - 2.2) / max));
        for (int i = 0; i <= 10; i++)
        {
            double v = i * 0.5;
            g.DrawLine(thin, At(v, rad), At(v, rad * (i % 2 == 0 ? 0.84f : 0.9f)));
            if (i % 2 == 0 && !icon && rad > 18)
                using (var tb = new SolidBrush(Pal.Dark))
                    g.DrawString(((int)v).ToString(), GetFont(rad * 0.16f, false), tb, At(v, rad * 0.7f), Center);
        }
        // стрелка прибора
        using (var np = new Pen(Pal.Occ, Math.Max(1.5f, rad * 0.035f)))
            g.DrawLine(np, c, At(amp, rad * 0.95f));
        float hub = Math.Max(2f, rad * 0.06f);
        using (var hb = new SolidBrush(Pal.Dark)) g.FillEllipse(hb, c.X - hub, c.Y - hub, 2 * hub, 2 * hub);
        if (icon) return;
        using var tx = new SolidBrush(Pal.Dark);
        g.DrawString("А", GetFont(rad * 0.22f, true), tx, new PointF(c.X, c.Y - rad * 0.42f), Center);
        g.DrawString(amp.ToString("0.00") + " А", GetFont(Math.Max(7, face.Height * 0.16f), true), tx,
            new PointF(face.Right - face.Width * 0.04f, face.Bottom - face.Height * 0.1f), RightBottom);
        if (!string.IsNullOrEmpty(o.Name))
            using (var wt = new SolidBrush(Color.White))
                g.DrawString(o.Name, GetFont(Math.Max(7, r.Height * 0.12f), false), wt, new PointF(r.X + r.Width / 2, r.Bottom + r.Height * 0.1f), Center);
    }

    static void DrawBuilding(Graphics g, StationObject o, RectangleF r, Pen dark, bool icon)
    {
        // пассажирское здание: прямоугольник с крышей-карнизом и названием внутри
        using (var fb = new SolidBrush(Pal.Building)) g.FillRectangle(fb, r);
        float roof = r.Height * 0.18f;
        using (var rb = new SolidBrush(Color.FromArgb(0x8a, 0x5a, 0x44))) g.FillRectangle(rb, r.X, r.Y, r.Width, roof);
        g.DrawRectangle(dark, r.X, r.Y, r.Width, r.Height);
        // окна
        int win = Math.Max(2, (int)(r.Width / (r.Height * 0.55f)));
        using (var wb = new SolidBrush(Color.FromArgb(0x9c, 0xc3, 0xd6)))
            for (int i = 0; i < win; i++)
            {
                float wx = r.X + r.Width * (i + 0.5f) / win - r.Height * 0.08f;
                g.FillRectangle(wb, wx, r.Y + roof + r.Height * 0.12f, r.Height * 0.16f, r.Height * 0.16f);
            }
        if (icon) return;
        var name = string.IsNullOrWhiteSpace(o.Name) ? "Вокзал" : o.Name;
        float fs = Math.Min(r.Height * 0.3f, r.Width / Math.Max(4, name.Length) * 1.5f);
        using var tb = new SolidBrush(Pal.Dark);
        g.DrawString(name, GetFont(Math.Max(7, fs), true), tb, new PointF(r.X + r.Width / 2, r.Y + roof + (r.Height - roof) * 0.66f), Center);
    }

    static void DrawClock(Graphics g, StationObject o, RectangleF r, Pen dark, bool icon)
    {
        // башня: шпиль, тело и циферблат; время — часов компьютера
        float spire = Math.Min(r.Height * 0.2f, r.Width * 0.7f);
        var body = new RectangleF(r.X + r.Width * 0.12f, r.Y + spire, r.Width * 0.76f, r.Height - spire);
        using (var sb = new SolidBrush(Pal.Stone)) g.FillRectangle(sb, body);
        g.DrawRectangle(dark, body.X, body.Y, body.Width, body.Height);
        var roof = new[] { new PointF(r.X + r.Width * 0.06f, r.Y + spire), new PointF(r.X + r.Width / 2, r.Y), new PointF(r.Right - r.Width * 0.06f, r.Y + spire) };
        using (var rb = new SolidBrush(Color.FromArgb(0x8a, 0x5a, 0x44))) g.FillPolygon(rb, roof);
        g.DrawPolygon(dark, roof);

        float fr = body.Width * 0.4f;
        var c = new PointF(body.X + body.Width / 2, body.Y + fr + body.Width * 0.12f);
        using (var fb = new SolidBrush(Color.White)) g.FillEllipse(fb, c.X - fr, c.Y - fr, 2 * fr, 2 * fr);
        g.DrawEllipse(dark, c.X - fr, c.Y - fr, 2 * fr, 2 * fr);
        using var thin = new Pen(Pal.Dark, Math.Max(1f, fr * 0.05f));
        for (int i = 0; i < 12; i++)
        {
            double a = i * Math.PI / 6;
            float k = i % 3 == 0 ? 0.78f : 0.86f;
            g.DrawLine(thin, c.X + (float)Math.Sin(a) * fr * k, c.Y - (float)Math.Cos(a) * fr * k,
                c.X + (float)Math.Sin(a) * fr * 0.95f, c.Y - (float)Math.Cos(a) * fr * 0.95f);
        }
        var now = DateTime.Now;
        double sec = now.Second, min = now.Minute + sec / 60, hr = now.Hour % 12 + min / 60;
        if (icon) { hr = 10.2; min = 10; sec = 30; }
        void Hand(double frac, float len, float width, Color col)
        {
            double a = frac * 2 * Math.PI;
            using var hp = new Pen(col, width) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            g.DrawLine(hp, c.X, c.Y, c.X + (float)Math.Sin(a) * fr * len, c.Y - (float)Math.Cos(a) * fr * len);
        }
        Hand(hr / 12, 0.5f, Math.Max(1.5f, fr * 0.11f), Pal.Dark);
        Hand(min / 60, 0.78f, Math.Max(1.2f, fr * 0.07f), Pal.Dark);
        if (o.Seconds != false) Hand(sec / 60, 0.85f, Math.Max(1f, fr * 0.03f), Pal.Occ);
        if (icon) return;
        // цифровое время под циферблатом и подпись
        using var tb = new SolidBrush(Pal.Dark);
        float ty = c.Y + fr + (body.Bottom - c.Y - fr) * 0.35f;
        g.DrawString(now.ToString(o.Seconds != false ? "HH:mm:ss" : "HH:mm"), GetFont(Math.Max(7, body.Width * 0.2f), true), tb, new PointF(c.X, ty), Center);
        if (!string.IsNullOrWhiteSpace(o.Name))
            g.DrawString(o.Name, GetFont(Math.Max(7, body.Width * 0.16f), false), tb, new PointF(c.X, body.Bottom - (body.Bottom - c.Y - fr) * 0.25f), Center);
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
        bool gost = GostLamps;
        GostLamps = false;    // значок инструмента — всегда в прежнем виде (столбик ГОСТ в него не помещается)
        try { DrawSignal(g, t, new Signal { Dir = "R", Kind = "entry", Name = "" }, x, y, cs, label: false); }
        finally { GostLamps = gost; }
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

        // ---- [ДОБАВЛЕНО 05.11.2026] объекты станции (под путями: путь, если наложен, виден поверх) ----
        foreach (var (k, o) in st.Objects)
        {
            var (bx, by) = Core.Parse(k);
            bool movingThis = ui.ObjMove != null && ui.SelObj == k;
            if (movingThis) continue;
            if (bx + o.W < gx0 || bx > gx1 || by + o.H < gy0 || by > gy1) continue;
            DrawObject(g, o, new RectangleF(SX(bx), SY(by), o.W * cs, o.H * cs), test ? T.Drive.Shown : 0);
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
                crossingClosed: test && e.T.T == "crossing" && hasSec && T.Occ.Contains(e.T.Sec),
                noControl: test && T.NoControl.Contains(e.K), lost: test && T.Drive.Lost.Contains(e.K));
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
                    aspect: test && T.Aspects.TryGetValue(e.K + "#" + i, out var asp) ? asp : null,
                    ring: selected || pending,
                    ringColor: pending ? Color.White : Pal.Sel,
                    diamond: Aspects.IsDiamond(st, new SignalEntry { Key = e.K, Idx = i, Sig = e.T.Sigs[i], Tile = e.T }));
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

        // ---- [ДОБАВЛЕНО 05.11.2026] выбранный / перетаскиваемый объект ----
        if (!test && ui.SelObj != null && st.Objects.TryGetValue(ui.SelObj, out var so))
        {
            var (bx, by) = Core.Parse(ui.SelObj);
            if (ui.ObjMove is Point om) { bx += om.X; by += om.Y; }
            var rr = new RectangleF(SX(bx), SY(by), so.W * cs, so.H * cs);
            if (ui.ObjMove != null)
            {
                var state = g.Save();
                DrawObject(g, so, rr, 0);
                g.Restore(state);
            }
            using var p = new Pen(Pal.Sel, 2) { DashPattern = new[] { 2.5f, 1.5f } };
            g.DrawRectangle(p, rr.X - 2, rr.Y - 2, rr.Width + 4, rr.Height + 4);
        }
        if (!test && ui.Tool == "object" && ui.Hover is Point oh)
        {
            var ot = Core.ObjTypeOf(ui.ObjType);
            bool fits = Core.ObjectFits(st, oh.X, oh.Y, ot.W, ot.H);
            using var p = new Pen(fits ? Color.White : Pal.Bad, 2) { DashPattern = new[] { 3f, 2f } };
            g.DrawRectangle(p, SX(oh.X), SY(oh.Y), ot.W * cs, ot.H * cs);
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
