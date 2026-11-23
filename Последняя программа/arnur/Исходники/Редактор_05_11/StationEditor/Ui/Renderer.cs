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
    public static readonly Color Brake = H("#141414");         // [ДОБАВЛЕНО 12.11.2026] вагонный замедлитель
    public static readonly Color BrakeShoe = H("#8c8c8c");     // его тормозные шины
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
    /// <summary>[ДОБАВЛЕНО 17.11.2026] Головки (размеры групп огней от мачты) — null, если огни в ряд.</summary>
    public int[] Groups;
    /// <summary>[ДОБАВЛЕНО 17.11.2026] «Окно» на мачте между пригласительным и основными огнями: центр маршрутного указателя
    /// и полоса (X вдоль ряда огней); NaN — нет.</summary>
    public float IndX = float.NaN, StripX0 = float.NaN, StripX1 = float.NaN, Half;
    /// <summary>Высота (Y) центра указателя и полосы: у мачтового — ряд огней, у карликового блока ГОСТ — свой ряд сбоку.</summary>
    public float IndY = float.NaN, StripY = float.NaN;
    /// <summary>[ДОБАВЛЕНО 20.11.2026] Зелёные полосы: сколько, ширина одной и промежуток (от StripX0 в сторону Fwd).</summary>
    public int Strips;
    public float StripW, StripGap;
    /// <summary>[ДОБАВЛЕНО 20.11.2026] Центр (X) светового указателя «Н» горочного / ПГ — у мачты, под огнями
    /// (РУ-30-80 листы 45–46: «Г ⊢□◎○●⊘», «П1Г1 ⊢□◎○●⊘»); NaN — нет.</summary>
    public float HumpX = float.NaN;
    /// <summary>[ДОБАВЛЕНО 23.11.2026] Тело светофора (огни, корпуса, указатели — без ножки у пути) и прямоугольник подписи
    /// — до поворота; по ним раскладка (LayoutScheme) ищет место, где светофор не лежит на путях и не закрывает надписи.</summary>
    public RectangleF Body, LabelRect;
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

    // ---------------- [ДОБАВЛЕНО 11.11.2026] название пути в разрыве линии (РУ-30-80, стр. 89–90) ----------------
    public sealed class TrackGap
    {
        public string Sec;
        /// <summary>Полоска, которую не рисуем (разрыв линии пути)</summary>
        public RectangleF Cut;
        public PointF Center;
        /// <summary>Половина ширины текста с полями — от центра до острия стрелки</summary>
        public float TextHalf;
        /// <summary>Длина стрелки; 0 — короткий путь, в разрыве только название (без стрелок)</summary>
        public float Arrow;
    }

    /// <summary>Где подписать пути (участки типа «путь»): посередине самого длинного горизонтального прямого куска без светофоров.
    /// Не помещается надпись со стрелками — [11.11.2026] разрыв только с названием; не помещается и оно — путь без разрыва.</summary>
    public static List<TrackGap> TrackGapList(Station st, float ox, float oy, float cs)
    {
        var res = new List<TrackGap>();
        var f = GetFont(cs * 0.3f, true);
        float arrow = cs * 0.2f, pad = cs * 0.07f, cut = Math.Max(4f, cs * 0.13f) + 4;
        foreach (var (sec, bY, bX0, bX1) in Core.TrackGapRuns(st))    // место — общее с «Станцией по схеме»
        {
            var best = (Y: bY, X0: bX0, X1: bX1);
            float txtW = measure.MeasureString(sec, f, PointF.Empty, StringFormat.GenericTypographic).Width;
            float textHalf = txtW / 2 + pad, a = arrow, half = textHalf + a, room = (best.X1 - best.X0 + 1) * cs;
            if (2 * half > room - cs * 0.3f)
            {
                a = 0; half = textHalf;                                  // короткий путь: без стрелок
                if (2 * half > room - cs * 0.1f) continue;               // и название не помещается
            }
            float cx = ox + (best.X0 + best.X1 + 1) / 2f * cs, cy = oy + (best.Y + 0.5f) * cs;
            res.Add(new TrackGap { Sec = sec, Center = new PointF(cx, cy), TextHalf = textHalf, Arrow = a, Cut = new RectangleF(cx - half, cy - cut / 2, 2 * half, cut) });
        }
        return res;
    }

    /// <summary>В разрыве — название пути, по краям — стрелки к нему: «——▶ 3П ◀——».</summary>
    static void DrawTrackGaps(Graphics g, List<TrackGap> gaps, float cs)
    {
        if (gaps.Count == 0) return;
        var f = GetFont(cs * 0.3f, true);
        float ah = Math.Max(2.5f, cs * 0.08f);
        using var tb = new SolidBrush(Pal.Text);
        using var ab = new SolidBrush(Pal.Track);
        foreach (var gp in gaps)
        {
            float cx = gp.Center.X, cy = gp.Center.Y, tl = cx - gp.TextHalf, tr = cx + gp.TextHalf, arrow = gp.Arrow;
            if (arrow > 0) g.FillPolygon(ab, new[] { new PointF(tl - arrow, cy - ah), new PointF(tl, cy), new PointF(tl - arrow, cy + ah) });
            if (arrow > 0) g.FillPolygon(ab, new[] { new PointF(tr + arrow, cy - ah), new PointF(tr, cy), new PointF(tr + arrow, cy + ah) });
            g.DrawString(gp.Sec, f, tb, gp.Center, Center);
        }
    }

    /// <summary>[ДОБАВЛЕНО 17.11.2026] Знаки у пути. Предельный столбик — белый столбик с чёрной верхушкой посреди клетки сбоку от пути
    /// (ПТЭ РК п. 114, 116); «Граница станции» (ГС, ПТЭ п. 133) и «Граница блок-участка» (ГБ, Инструкция п. 81) — белая табличка
    /// на стойке справа по ходу (ПТЭ п. 115): «→» — под путём, «←» — над путём.</summary>
    public static void DrawSigns(Graphics g, Tile t, float x, float y, float cs)
    {
        using var dark = new Pen(Pal.Dark, Math.Max(1f, cs * 0.03f));
        foreach (var s in t.Signs)
        {
            if (s.Type == "limit")
            {
                float w = cs * 0.09f, h = cs * 0.22f, cx = x + cs * 0.5f, top = y + cs * 0.62f;
                g.FillRectangle(Brushes.White, cx - w / 2, top, w, h);
                using (var bb = new SolidBrush(Pal.Dark)) g.FillRectangle(bb, cx - w / 2, top, w, h * 0.35f);
                g.DrawRectangle(dark, cx - w / 2, top, w, h);
                continue;
            }
            var info = Core.SignTypes.FirstOrDefault(q => q.Id == s.Type);
            bool below = s.Dir != "L";
            float pw = cs * 0.5f, ph = cs * 0.26f, px = x + cs * 0.5f - pw / 2;
            float py = below ? y + cs * 0.68f : y + cs * 0.06f;
            float poleFrom = below ? y + cs * 0.56f : y + cs * 0.44f, poleTo = below ? py : py + ph;
            g.DrawLine(dark, x + cs * 0.5f, poleFrom, x + cs * 0.5f, poleTo);
            g.FillRectangle(Brushes.White, px, py, pw, ph);
            g.DrawRectangle(dark, px, py, pw, ph);
            if (cs >= 14)
                using (var tb = new SolidBrush(Pal.Dark))
                    g.DrawString(info.Short, GetFont(cs * 0.16f, true), tb, new PointF(x + cs * 0.5f, py + ph / 2), Center);
        }
    }

    /// <summary>[ДОБАВЛЕНО 17.11.2026] Стрелочный указатель нецентрализованной стрелки (ПТЭ РК п. 107; Инструкция РК п. 72):
    /// по прямому (плюс) — узкий белый прямоугольник (ночью молочно-белый огонь), на боковой (минус) — широкая сторона,
    /// ночью жёлтый огонь. Рисуется у конца стрелки, со стороны, противоположной ответвлению.</summary>
    public static void DrawSwitchIndicator(Graphics g, Tile t, float x, float y, float cs)
    {
        var v = Core.VariantOf(t);
        // у конца стрелки (не у остряка — там часто светофор), со стороны, противоположной ответвлению
        float fx = v.Trunk is "W" or "NW" or "SW" ? 0.8f : 0.2f;
        float fy = v.Minus.Contains('N') ? 0.84f : 0.16f;
        bool plus = t.IsPlus;
        float w = plus ? cs * 0.08f : cs * 0.22f, h = cs * 0.2f;
        var r = new RectangleF(x + cs * fx - w / 2, y + cs * fy - h / 2, w, h);
        using (var b = new SolidBrush(plus ? Color.White : Pal.LampYellow)) g.FillRectangle(b, r);
        using (var p = new Pen(Pal.Dark, Math.Max(1f, cs * 0.025f))) g.DrawRectangle(p, r.X, r.Y, r.Width, r.Height);
    }

    /// <summary>[ДОБАВЛЕНО 12.11.2026] Где подписать тормозную позицию: сбоку от полосы, со стороны «вниз» по экрану.</summary>
    public static PointF BrakeLabelPos(Tile t, float x, float y, float cs)
    {
        var e = Core.EdgesOf(t).First();
        var pa = Pnt(x, y, cs, e.A);
        var pb = Pnt(x, y, cs, e.B);
        float dx = pb.X - pa.X, dy = pb.Y - pa.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
        float nx = -dy / len, ny = dx / len;
        if (ny < 0) { nx = -nx; ny = -ny; }
        float d = dy == 0 ? cs * 0.34f : cs * 0.4f;
        return new PointF(x + cs / 2 + nx * d, y + cs / 2 + ny * d);
    }

    // ---------------- [ДОБАВЛЕНО 10.11.2026] свободный текст ----------------
    static readonly Graphics measure = Graphics.FromImage(new Bitmap(1, 1));

    public static Font LabelFont(FreeLabel l, float zoom) => GetFont(Math.Max(6, (float)Math.Round((l.Size > 0 ? l.Size : 14) * zoom)), l.IsBold);

    /// <summary>Прямоугольник свободного текста на экране (ox, oy — сдвиг камеры, cs — размер клетки).</summary>
    public static RectangleF LabelRect(FreeLabel l, float ox, float oy, float cs, float zoom)
    {
        var sz = measure.MeasureString(string.IsNullOrEmpty(l.Text) ? " " : l.Text, LabelFont(l, zoom), PointF.Empty, StringFormat.GenericTypographic);
        return new RectangleF(ox + (float)l.X * cs, oy + (float)l.Y * cs, Math.Max(sz.Width, 6), Math.Max(sz.Height, 6));
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
                // [ИЗМЕНЕНО 23.11.2026] у перекрёстного съезда в плюсе обрубок диагонали не рисуем: видно либо два параллельных
                // пути, либо одну диагональ (ту пару, что в минусе) — а не крест «X» из четырёх обрубков
                if (!(plusOn && scissorsTiles.Contains(t))) Poly(g, pen, Gap(off), off);
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
            if (t.T == "brake")
            {
                // [ДОБАВЛЕНО 12.11.2026] тормозная позиция — вагонный замедлитель: толстая тёмная полоса на пути
                // («Проектирование сортировочных горок», рис. 2.1, 4.1, 4.2); каёмка цвета пути — видно занятость / маршрут
                var e = Core.EdgesOf(t).First();
                var pa = Pnt(x, y, cs, e.A);
                var pb = Pnt(x, y, cs, e.B);
                PointF At(float k) => new(pa.X + (pb.X - pa.X) * k, pa.Y + (pb.Y - pa.Y) * k);
                float bw = Math.Max(4f, cs * 0.24f);
                using (var rim = new Pen(col, bw + Math.Max(2f, cs * 0.06f)) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                    g.DrawLine(rim, At(0.14f), At(0.86f));
                using (var bar = new Pen(A(Pal.Brake, col.A), bw) { StartCap = LineCap.Flat, EndCap = LineCap.Flat })
                    g.DrawLine(bar, At(0.16f), At(0.84f));
                // шины замедлителя — две тонкие светлые черты вдоль полосы
                float dx = pb.X - pa.X, dy = pb.Y - pa.Y, len = (float)Math.Sqrt(dx * dx + dy * dy);
                float nx = -dy / len * bw * 0.22f, ny = dx / len * bw * 0.22f;
                using var sp = new Pen(A(Pal.BrakeShoe, col.A), Math.Max(1f, cs * 0.025f));
                foreach (var s in new[] { -1f, 1f })
                {
                    var p1 = At(0.22f); var p2 = At(0.78f);
                    g.DrawLine(sp, p1.X + s * nx, p1.Y + s * ny, p2.X + s * nx, p2.Y + s * ny);
                }
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

    // ---- [ДОБАВЛЕНО 17.11.2026] проверка огней: зажечь все лампы светофора (все — кнопка «Все огни», выбранные — галочка
    // в свойствах). Только вид: в файл не пишется, маршруты и показания не меняет ----
    public static bool LampTestAll;
    public static readonly HashSet<Signal> LampTestSigs = new();
    public static bool IsLampTest(Signal s) => LampTestAll || LampTestSigs.Contains(s);

    /// <summary>[ДОБАВЛЕНО 20.11.2026] Маневровый и в цветном виде рисуется обоими огнями в своей головке (горит один):
    /// иначе одна лампа — и не видно, как огни сгруппированы.</summary>
    public static bool FullInColor(Signal s) => s.Kind == "shunt";

    public static SigGeom GetSigGeom(Tile t, Signal sig, float x, float y, float cs, bool diamond = false) =>
        GetSigGeom(t, sig, x, y, cs, diamond, sigPlaces.TryGetValue(sig, out var pl) ? pl : null);

    static SigGeom GetSigGeom(Tile t, Signal sig, float x, float y, float cs, bool diamond, SigPlace pl)
    {
        var gm = GetSigGeomFlat(t, sig, x, y, cs, diamond, pl ?? SigPlace.Default);
        gm.Angle = SigAngle(t, sig.Dir);
        return gm;
    }

    static SigGeom GetSigGeomFlat(Tile t, Signal sig, float x, float y, float cs, bool diamond, SigPlace pl)
    {
        var a = SigAnchor(t, x, y, cs, sig.Dir);
        // [ИЗМЕНЕНО 23.11.2026] ряд огней — на одном уровне у всех светофоров (RowD); раскладка может только отодвинуть светофор
        // от пути (Out). Сторона пути и направление огней не меняются никогда — светофор сам не «разворачивается»
        float side = sig.Dir == "R" ? 1 : -1, fwd = side;
        float level = cs * (RowD + pl.Out);
        float r = cs * 0.12f, leg = cs * 0.12f;
        bool dwarf = Aspects.IsDwarf(sig, t);
        // [ИЗМЕНЕНО 09.11.2026] в виде «Огни по ГОСТ» — все лампы головки вплотную, как цельные светофоры в РУ-30-80:
        // мачтовый — «⊢» и ножка, огни в ряд вдоль пути («1 ⊢●○⊘», «Н1 ⊢◎⊘●○⊘», «◆—⊣З», стр. 9, 49);
        // у входного пригласительный отделён чёрточкой («Н ⊢◎-⊘●○⊘», стр. 9, 49); карликовый — см. ниже
        // [ИЗМЕНЕНО 17.11.2026] при проверке огней — все лампы полной головки и в цветном виде
        // [20.11.2026] и маневровый в цветном виде — оба огня (FullInColor)
        int n = GostLamps || IsLampTest(sig) || FullInColor(sig) ? Aspects.FullHead(sig, t, diamond).Length : Aspects.LampCount(sig, t);
        float gap = GostLamps && Aspects.InvitationApart(sig) && n > 1 ? r * 0.8f : 0;
        // [ДОБАВЛЕНО 17.11.2026] головки (группы огней, свойство «Головки») и «окно» на мачте: маршрутный указатель и зелёная
        // полоса — между пригласительным и основными огнями, а если пригласительного нет — у мачты (не над светофором)
        var groups = sig.HeadGroups(n);    // [20.11.2026] «в ряд» — без корпуса, и у маневрового
        float grpGap = groups != null ? r * 0.7f : 0;
        // [20.11.2026] маневровый «в ряд» (без головок) в цветном виде — огни отдельно на мачте, с промежутком:
        // иначе две лампы впритык не отличить от одной головки «2»
        float rowGap = !GostLamps && FullInColor(sig) && groups == null && n > 1 ? r * 0.6f : 0;
        float half = r * 0.95f, g0 = r * 0.35f, stripW = r * 0.7f;
        // [20.11.2026] «Н» горочного и ПГ — тоже в окне у мачты, первым (под огнями); полос может быть две
        bool slotHump = Aspects.HasIndicator(sig), slotInd = sig.RouteIndOnMast;
        int nStrips = sig.StripCount;
        float slotContent = (slotHump ? g0 + 2 * half : 0) + (slotInd ? g0 + 2 * half : 0) + nStrips * (g0 + stripW);
        float slotW = slotContent > 0 ? slotContent + g0 : 0;
        int slotAt = -1;
        if (slotW > 0)
        {
            var headArr = GostLamps ? Aspects.FullHead(sig, t, diamond) : Aspects.Head(sig, t);
            bool inv = sig.Kind is "entry" or "exit" or "route" && headArr != null && headArr.Length > 1 && headArr[0] == Lamp.White;
            slotAt = inv && !slotHump ? 1 : 0;
            if (groups != null && slotAt > 0)
            {
                // на границе головок: первая граница не ближе к мачте, чем нужно (но не над последней лампой)
                int acc = 0;
                foreach (var p in groups) { acc += p; if (acc >= slotAt && acc < n) { slotAt = acc; break; } }
            }
        }
        bool Boundary(int i)
        {
            if (groups == null || i <= 0) return false;
            int acc = 0;
            foreach (var p in groups) { acc += p; if (acc == i) return true; if (acc > i) return false; }
            return false;
        }
        float Off(int i)    // от конца ножки до центра i-й лампы
        {
            float o = (diamond ? r * 1.3f : r) + i * (2 * r + rowGap) + (i >= 1 ? gap : 0);
            for (int j = 1; j <= i; j++) if (Boundary(j)) o += grpGap;
            if (slotAt >= 0 && i >= slotAt) o += slotW;
            return o;
        }
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
            float start = a.Y + side * (level - r);    // край огней, ближний к пути: первый ряд — на общем уровне
            int cols = cells.Max(c => c.C) + 1, rows = cells.Max(c => c.Rw) + 1;
            for (int i = 0; i < n; i++)
                lamps[i] = new PointF(a.X + fwd * (r + cells[i].C * 2 * r), start + side * (r + cells[i].Rw * 2 * r));
            float end = start + side * rows * 2 * r;
            // [ДОБАВЛЕНО 17.11.2026] указатель и полоса у карликового — отдельным рядом у основания, за огнями (дальше от пути)
            float dIndX = float.NaN, dStripX0 = float.NaN, dStripX1 = float.NaN, rowY = float.NaN;
            float dh = r * 0.95f, dg = r * 0.35f;
            if (sig.RouteIndOnMast || nStrips > 0)
            {
                rowY = end + side * (dg + dh);
                float cur = 0;
                if (sig.RouteIndOnMast) { cur += dg; dIndX = a.X + fwd * (cur + dh); cur += 2 * dh; }
                if (nStrips > 0) { cur += dg; dStripX0 = a.X + fwd * cur; cur += nStrips * stripW + (nStrips - 1) * dg; dStripX1 = a.X + fwd * cur; }
                end = rowY + side * dh;
            }
            float bx0 = Math.Min(a.X, a.X + fwd * cols * 2 * r), bx1 = Math.Max(a.X, a.X + fwd * cols * 2 * r);
            var dgm = new SigGeom
            {
                Ax = a.X, Ay = a.Y, Sy = start, Side = side, Fwd = fwd, R = r, Lamps = lamps,
                DwarfBlock = true, Bar0 = start, Bar1 = end,
                IndX = dIndX, IndY = rowY, StripX0 = dStripX0, StripX1 = dStripX1, StripY = rowY, Half = dh,
                Strips = nStrips, StripW = stripW, StripGap = dg,
                Body = RectangleF.FromLTRB(bx0 - r * 0.2f, Math.Min(start, end), bx1 + r * 0.2f, Math.Max(start, end)),
            };
            return WithLabel(dgm, sig, cs, pl.Slot);
        }
        // [ИЗМЕНЕНО 23.11.2026] ряд огней на одном уровне у всех: мачтовых, карликовых, в цвете и в виде ГОСТ
        // (было 0,4 / 0,34 / 0,27 клетки — светофоры стояли «ступеньками»)
        float sy = a.Y + side * level;
        for (int i = 0; i < n; i++) lamps[i] = new PointF(a.X + fwd * (leg + Off(i)), sy);
        float minX = a.X, maxX = a.X;
        foreach (var l in lamps)
        {
            float lx = l.X + fwd * r;
            minX = Math.Min(minX, lx); maxX = Math.Max(maxX, lx);
        }
        float indX = float.NaN, stripX0 = float.NaN, stripX1 = float.NaN, humpX = float.NaN;
        if (slotAt >= 0)
        {
            float cur = slotAt == 0 ? leg : leg + Off(slotAt - 1) + r;    // вдоль ряда от точки привязки
            if (slotHump) { cur += g0; humpX = a.X + fwd * (cur + half); cur += 2 * half; }
            if (slotInd) { cur += g0; indX = a.X + fwd * (cur + half); cur += 2 * half; }
            if (nStrips > 0) { cur += g0; stripX0 = a.X + fwd * cur; cur += nStrips * stripW + (nStrips - 1) * g0; stripX1 = a.X + fwd * cur; }
        }
        float rh = r * 1.35f;    // половина высоты тела: корпус головки, ромб, полоса
        var body = RectangleF.FromLTRB(minX - r * 0.2f, sy - rh, maxX + r * 0.2f, sy + rh);
        if (sig.HasRouteInd && sig.RouteIndOwnMast == true)
        {
            // указатель на своей мачте позади светофора — тоже часть тела
            float mx = a.X - fwd * cs * 0.45f, mx2 = mx + fwd * (cs * 0.08f + 2 * half);
            body = RectangleF.Union(body, RectangleF.FromLTRB(Math.Min(mx, mx2) - 1, sy - half, Math.Max(mx, mx2) + 1, sy + half));
        }
        var gm = new SigGeom
        {
            Ax = a.X, Ay = a.Y, Sy = sy, Side = side, Fwd = fwd, R = r, Lamps = lamps,
            MastRow = GostLamps && !dwarf, Gap = gap,
            Groups = groups, IndX = indX, StripX0 = stripX0, StripX1 = stripX1, Half = half, IndY = sy, StripY = sy,
            Strips = nStrips, StripW = stripW, StripGap = g0, HumpX = humpX, Body = body,
        };
        return WithLabel(gm, sig, cs, pl.Slot);
    }

    // ---------------------------------------------------------------------
    //  [ДОБАВЛЕНО 23.11.2026] РАСКЛАДКА: светофоры не лежат на путях, подписи не налезают друг на друга
    // ---------------------------------------------------------------------
    /// <summary>Уровень ряда огней от оси пути (доля клетки) — один у всех светофоров.</summary>
    public const float RowD = 0.3f;

    /// <summary>Где стоит светофор: Out — отодвинут от пути (доля клетки),
    /// Slot — место подписи: 0 — снаружи под огнями, 1 — в ряд за огнями, 2 — в ряд позади мачты.</summary>
    public sealed class SigPlace
    {
        public float Out; public int Slot;
        public static readonly SigPlace Default = new();
    }

    static readonly Dictionary<Signal, SigPlace> sigPlaces = new();
    static readonly Dictionary<Tile, (float X, float Y)> swLabelAt = new();
    static readonly HashSet<Tile> scissorsTiles = new();
    static readonly Dictionary<string, PointF> secLabelAt = new();    // участок → центр подписи (в клетках)
    static readonly Dictionary<Signal, string> sigCovers = new();
     // [23.11.2026] светофор, которому не нашлось места, → что он закрывает

    /// <summary>[ДОБАВЛЕНО 23.11.2026] Что закрывает светофор (стрелку 12, изгиб пути…), если рядом не нашлось свободного места;
    /// null — светофор ничего не закрывает. Раскладка считается заново.</summary>
    public static string CoverText(Station st, Signal s)
    {
        LayoutScheme(st);
        return sigCovers.TryGetValue(s, out var w) ? w : null;
    }

    /// <summary>[ДОБАВЛЕНО 23.11.2026] Предупреждения для вкладки «Проверка»: светофоры, которые закрывают путь, стрелку, изгиб.</summary>
    public static List<Problem> CoverProblems(Station st)
    {
        LayoutScheme(st);
        var res = new List<Problem>();
        foreach (var e in Core.AllSignals(st))
            if (sigCovers.TryGetValue(e.Sig, out var w))
                res.Add(new Problem
                {
                    Level = "warn", Key = e.Key,
                    Text = $"Светофор {(string.IsNullOrEmpty(e.Sig.Name) ? "?" : e.Sig.Name)} закрывает {w}: рядом нет свободного места. " +
                           "Переставьте его на клетку дальше от стрелки или изгиба, или раздвиньте пути на клетку.",
                });
        return res;
    }

    static string TrackWhat(Tile t) => t.T switch
    {
        "switch" => "стрелку " + (string.IsNullOrEmpty(t.Name) ? "?" : t.Name),
        "curve" => "изгиб пути",
        "cross" => "пересечение путей",
        "crossing" => "переезд",
        "buffer" => "тупик",
        _ => "путь" + (string.IsNullOrEmpty(t.Sec) ? "" : " " + t.Sec),
    };

    static string SigText(Signal sig) => (string.IsNullOrEmpty(sig.Name) ? "?" : sig.Name) + (sig.IsReadOnly ? " ↔" : "");

    /// <summary>Подпись светофора в месте slot (до поворота) и рамка выделения / попадания мышью.</summary>
    static SigGeom WithLabel(SigGeom gm, Signal sig, float cs, int slot)
    {
        var sz = measure.MeasureString(SigText(sig), GetFont(cs * 0.27f, true), PointF.Empty, StringFormat.GenericTypographic);
        float tw = sz.Width + 2, th = sz.Height;
        var b = gm.Body;
        float rowY = gm.DwarfBlock ? gm.Bar0 + gm.Side * gm.R : gm.Sy;
        float far = gm.Fwd > 0 ? b.Right : b.Left;
        PointF c = slot switch
        {
            1 => new PointF(far + gm.Fwd * (cs * 0.05f + tw / 2), rowY),
            2 => new PointF(gm.Ax - gm.Fwd * (cs * 0.07f + tw / 2), rowY),
            _ => new PointF((b.Left + b.Right) / 2, (gm.Side > 0 ? b.Bottom : b.Top) + gm.Side * (th / 2 + cs * 0.02f)),
        };
        if (slot == 0 && !gm.DwarfBlock && gm.Lamps.Length > 0)
            c.X = (gm.Lamps[0].X + gm.Lamps[^1].X) / 2;    // по середине огней, как раньше
        gm.Label = c;
        gm.LabelRect = new RectangleF(c.X - tw / 2, c.Y - th / 2, tw, th);
        var anchor = new RectangleF(gm.Ax - 3, Math.Min(gm.Ay, gm.Sy) - 2, 6, Math.Abs(gm.Sy - gm.Ay) + 4);
        var box = RectangleF.Union(RectangleF.Union(b, gm.LabelRect), anchor);
        box.Inflate(3, 2);
        gm.Box = box;
        return gm;
    }

    /// <summary>Прямоугольник в кадре светофора — четырёхугольник на экране (с поворотом вокруг точки привязки).</summary>
    static PointF[] Quad(SigGeom gm, RectangleF r)
    {
        var q = RectQ(r);
        if (gm.Angle == 0) return q;
        double a = gm.Angle * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
        for (int i = 0; i < 4; i++)
        {
            double dx = q[i].X - gm.Ax, dy = q[i].Y - gm.Ay;
            q[i] = new PointF((float)(gm.Ax + dx * cos - dy * sin), (float)(gm.Ay + dx * sin + dy * cos));
        }
        return q;
    }

    static PointF[] RectQ(RectangleF r) =>
        new[] { new PointF(r.Left, r.Top), new PointF(r.Right, r.Top), new PointF(r.Right, r.Bottom), new PointF(r.Left, r.Bottom) };

    /// <summary>Отрезок пути толщиной 2·hw — четырёхугольником.</summary>
    static PointF[] SegQ(PointF p, PointF q, float hw)
    {
        float dx = q.X - p.X, dy = q.Y - p.Y, len = MathF.Sqrt(dx * dx + dy * dy);
        if (len < 0.01f) return RectQ(new RectangleF(p.X - hw, p.Y - hw, 2 * hw, 2 * hw));
        float nx = -dy / len * hw, ny = dx / len * hw;
        return new[] { new PointF(p.X + nx, p.Y + ny), new PointF(q.X + nx, q.Y + ny), new PointF(q.X - nx, q.Y - ny), new PointF(p.X - nx, p.Y - ny) };
    }

    /// <summary>Пересекаются ли два выпуклых четырёхугольника (теорема о разделяющей оси).</summary>
    static bool Overlap(PointF[] a, PointF[] b) => !Separated(a, b) && !Separated(b, a);

    static bool Separated(PointF[] a, PointF[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            var p = a[i]; var q = a[(i + 1) % a.Length];
            float nx = -(q.Y - p.Y), ny = q.X - p.X;
            float a0 = float.MaxValue, a1 = float.MinValue, b0 = float.MaxValue, b1 = float.MinValue;
            foreach (var v in a) { float d = v.X * nx + v.Y * ny; a0 = Math.Min(a0, d); a1 = Math.Max(a1, d); }
            foreach (var v in b) { float d = v.X * nx + v.Y * ny; b0 = Math.Min(b0, d); b1 = Math.Max(b1, d); }
            if (a1 <= b0 || b1 <= a0) return true;
        }
        return false;
    }

    /// <summary>Препятствие раскладки: четырёхугольник и его габарит (габариты сравниваются первыми — так быстро).</summary>
    sealed class Ob
    {
        public readonly PointF[] Q; public readonly RectangleF B;
        /// <summary>Что это (для предупреждения «светофор закрывает …») и клетка.</summary>
        public readonly string What, Key;
        public Ob(PointF[] q, string what = null, string key = null)
        {
            Q = q; What = what; Key = key;
            float x0 = q.Min(p => p.X), y0 = q.Min(p => p.Y), x1 = q.Max(p => p.X), y1 = q.Max(p => p.Y);
            B = RectangleF.FromLTRB(x0, y0, x1, y1);
        }
    }

    static int CountHits(Ob q, List<Ob> list)
    {
        int n = 0;
        foreach (var o in list) if (o.B.IntersectsWith(q.B) && Overlap(q.Q, o.Q)) n++;
        return n;
    }

    static bool HitsAny(Ob q, List<Ob> list, Ob except = null)
    {
        foreach (var o in list)
            if (!ReferenceEquals(o, except) && o.B.IntersectsWith(q.B) && Overlap(q.Q, o.Q)) return true;
        return false;
    }

    /// <summary>[ДОБАВЛЕНО 23.11.2026] Отметка «светофор закрывает путь»: оранжевая пунктирная рамка вокруг огней и кружок «!».</summary>
    static void DrawCoverMark(Graphics g, SigGeom gm, float cs)
    {
        var body = gm.Body;
        body.Inflate(cs * 0.04f, cs * 0.04f);
        var q = Quad(gm, body);
        var orange = Color.FromArgb(0xf0, 0x8a, 0x00);
        using (var p = new Pen(orange, Math.Max(1.5f, cs * 0.04f)) { DashPattern = new[] { 2f, 1.5f } }) g.DrawPolygon(p, q);
        if (cs < 12) return;
        float rr = Math.Max(5f, cs * 0.13f);
        var at = q.OrderBy(v => v.Y).ThenByDescending(v => v.X).First();    // верхний правый угол
        using (var b = new SolidBrush(orange)) g.FillEllipse(b, at.X - rr, at.Y - rr, 2 * rr, 2 * rr);
        g.DrawString("!", GetFont(rr * 1.5f, true), Brushes.White, at, Center);
    }

    /// <summary>Клетки участка для подписи: прямые горизонтальные и переезды (если есть), слева направо.</summary>
    static List<(int X, int Y)> SecLabelPool(Station st, List<string> keys)
    {
        var flat = keys.Where(k => { var t = st.Tiles[k]; return (t.T == "straight" && t.V == 0) || t.T == "crossing"; }).ToList();
        return (flat.Count > 0 ? flat : keys).Select(Core.Parse).OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
    }

    /// <summary>Где подписать стрелку (доли клетки): место из раскладки, иначе как раньше (Core.SwitchLabelAt).</summary>
    public static (float X, float Y) SwitchLabelFrac(Tile t) => swLabelAt.TryGetValue(t, out var p) ? p : Core.SwitchLabelAt(t);

    /// <summary>Раскладка схемы (в координатах станции, клетка = Core.Cell, поэтому от масштаба не зависит): для каждого светофора —
    /// первое место без наложений (по ходу на общем уровне → огни развёрнуты назад → дальше от пути на 0,15 и 0,3 клетки),
    /// затем место подписи (под огнями → в ряд за огнями → позади мачты), затем места номеров стрелок.
    /// Препятствия: линии путей, знаки, стрелочные указатели, надписи, названия путей в разрыве, подписи тормозных позиций,
    /// уже поставленные светофоры и подписи.</summary>
    public static void LayoutScheme(Station st)
    {
        float cs = Core.Cell;
        sigPlaces.Clear(); swLabelAt.Clear(); scissorsTiles.Clear(); secLabelAt.Clear(); sigCovers.Clear();
        foreach (var (sa, sb) in Core.Scissors(st))
            foreach (var k in sa.Concat(sb)) if (st.Tiles.TryGetValue(k, out var sct)) scissorsTiles.Add(sct);

        var tracks = new List<Ob>();
        var fixedQ = new List<Ob>();
        float hw = cs * 0.09f;    // полтолщины линии пути с запасом
        var sigs = new List<(Tile T, Signal S, float X, float Y, bool Dia)>();
        foreach (var (k, t) in st.Tiles)
        {
            var (gx, gy) = Core.Parse(k);
            float X = gx * cs, Y = gy * cs;
            var c = new PointF(X + cs / 2, Y + cs / 2);
            string what = TrackWhat(t);
            foreach (var e in Core.EdgesOf(t))
            {
                tracks.Add(new Ob(SegQ(Pnt(X, Y, cs, e.A), c, hw), what, k));
                if (e.B != null) tracks.Add(new Ob(SegQ(c, Pnt(X, Y, cs, e.B), hw), what, k));
            }
            if (t.Signs != null)
                foreach (var s in t.Signs)
                    fixedQ.Add(new Ob(RectQ(s.Type == "limit"
                        ? new RectangleF(X + cs * 0.45f, Y + cs * 0.62f, cs * 0.1f, cs * 0.22f)
                        : new RectangleF(X + cs * 0.25f, s.Dir != "L" ? Y + cs * 0.56f : Y + cs * 0.06f, cs * 0.5f, cs * 0.38f))));
            if (t.T == "switch" && t.IsManual)
            {
                var v = Core.VariantOf(t);
                float fx = v.Trunk is "W" or "NW" or "SW" ? 0.8f : 0.2f, fy = v.Minus.Contains('N') ? 0.84f : 0.16f;
                fixedQ.Add(new Ob(RectQ(new RectangleF(X + cs * (fx - 0.12f), Y + cs * (fy - 0.11f), cs * 0.24f, cs * 0.22f))));
            }
            if (t.HasSigs)
                for (int i = 0; i < t.Sigs.Count; i++)
                    sigs.Add((t, t.Sigs[i], X, Y, Aspects.IsDiamond(st, new SignalEntry { Key = k, Idx = i, Sig = t.Sigs[i], Tile = t })));
        }
        foreach (var (k, tx) in st.Texts)
        {
            var (gx, gy) = Core.Parse(k);
            var f = GetFont(Math.Max(6, (float)Math.Round(tx.Size > 0 ? tx.Size : 14)), true);
            var sz = measure.MeasureString(string.IsNullOrEmpty(tx.Text) ? " " : tx.Text, f, PointF.Empty, StringFormat.GenericTypographic);
            fixedQ.Add(new Ob(RectQ(new RectangleF(gx * cs + 3, gy * cs + cs / 2 - sz.Height / 2, sz.Width, sz.Height))));
        }
        foreach (var l in st.Labels) fixedQ.Add(new Ob(RectQ(LabelRect(l, 0, 0, cs, 1))));
        var gapList = st.TrackGaps != false ? TrackGapList(st, 0, 0, cs) : new List<TrackGap>();
        foreach (var gp in gapList)
                fixedQ.Add(new Ob(RectQ(new RectangleF(gp.Center.X - gp.TextHalf, gp.Center.Y - cs * 0.17f, 2 * gp.TextHalf, cs * 0.34f))));
        var bf = GetFont(cs * 0.24f, true);
        foreach (var k in Core.BrakeLabelTiles(st))
        {
            if (!st.Tiles.TryGetValue(k, out var t)) continue;
            var (gx, gy) = Core.Parse(k);
            var p = BrakeLabelPos(t, gx * cs, gy * cs, cs);
            var sz = measure.MeasureString(t.Brk.Label, bf, PointF.Empty, StringFormat.GenericTypographic);
            fixedQ.Add(new Ob(RectQ(new RectangleF(p.X - sz.Width / 2, p.Y - sz.Height / 2, sz.Width, sz.Height))));
        }

        // 1) тела светофоров: сверху вниз, слева направо
        // [ИЗМЕНЕНО 23.11.2026] на своём месте; занято — чуть дальше от пути. Огни не разворачиваются и на другую сторону пути
        // светофор не переносится (по просьбе пользователя: это выглядело так, будто светофор сам поменял направление)
        var cands = new[] { 0f, 0.15f, 0.3f, 0.45f };
        var bodies = new List<Ob>();
        var placed = new List<(Tile T, Signal S, float X, float Y, bool Dia, SigPlace P, Ob Body)>();
        foreach (var e in sigs.OrderBy(q => q.Y).ThenBy(q => q.X))
        {
            SigPlace best = null; Ob bestQ = null;
            foreach (var outv in cands)
            {
                var pl = new SigPlace { Out = outv };
                var gm = GetSigGeom(e.T, e.S, e.X, e.Y, cs, e.Dia, pl);
                var q = new Ob(Quad(gm, gm.Body));
                if (!HitsAny(q, tracks) && !HitsAny(q, bodies) && !HitsAny(q, fixedQ)) { best = pl; bestQ = q; break; }
            }
            if (best == null)
            {
                // [ИЗМЕНЕНО 23.11.2026] свободного места нет — светофор остаётся на обычном месте (не уезжает от пути),
                // рисуется полупрозрачным с «!»
                best = new SigPlace();
                var gm0 = GetSigGeom(e.T, e.S, e.X, e.Y, cs, e.Dia, best);
                bestQ = new Ob(Quad(gm0, gm0.Body));
                // что именно закрыто — для рамки «!», подсказки в «Свойствах» и вкладки «Проверка»
                var what = tracks.Where(o => o.B.IntersectsWith(bestQ.B) && Overlap(bestQ.Q, o.Q)).Select(o => o.What).Distinct().ToList();
                if (HitsAny(bestQ, bodies)) what.Add("другой светофор");
                if (HitsAny(bestQ, fixedQ)) what.Add("знак или надпись");
                sigCovers[e.S] = string.Join(", ", what);
            }
            bodies.Add(bestQ);
            placed.Add((e.T, e.S, e.X, e.Y, e.Dia, best, bestQ));
        }
        // 2) подписи светофоров
        var labels = new List<Ob>();
        foreach (var e in placed)
        {
            Ob first = null;
            bool ok = false;
            for (int slot = 0; slot <= 2 && !ok; slot++)
            {
                e.P.Slot = slot;
                var gm = GetSigGeom(e.T, e.S, e.X, e.Y, cs, e.Dia, e.P);
                var q = new Ob(Quad(gm, gm.LabelRect));
                first ??= q;
                if (!HitsAny(q, tracks) && !HitsAny(q, fixedQ) && !HitsAny(q, bodies, e.Body) && !HitsAny(q, labels)) { labels.Add(q); ok = true; }
            }
            if (!ok) { e.P.Slot = 0; labels.Add(first); }
            sigPlaces[e.S] = e.P;
        }
        // 3) номера стрелок: как раньше, а если место занято светофором или надписью — первое свободное в клетке
        var nf = GetFont(cs * 0.3f, true);
        var spots = new (float X, float Y)[] { (0.5f, 0.1f), (0.5f, 0.9f), (0.2f, 0.16f), (0.8f, 0.16f), (0.2f, 0.84f), (0.8f, 0.84f) };
        foreach (var (k, t) in st.Tiles)
        {
            if (t.T != "switch") continue;
            var (gx, gy) = Core.Parse(k);
            var sz = measure.MeasureString(string.IsNullOrEmpty(t.Name) ? "?" : t.Name, nf, PointF.Empty, StringFormat.GenericTypographic);
            Ob At((float X, float Y) f) =>
                new Ob(RectQ(new RectangleF(gx * cs + f.X * cs - sz.Width / 2, gy * cs + f.Y * cs - sz.Height / 2, sz.Width, sz.Height)));
            bool Free(Ob q, bool checkTracks) =>
                !HitsAny(q, bodies) && !HitsAny(q, labels) && !HitsAny(q, fixedQ) && (!checkTracks || !HitsAny(q, tracks));
            var choice = Core.SwitchLabelAt(t);
            var dq = At(choice);
            if (!Free(dq, false))
                foreach (var sp in spots)
                {
                    var q = At(sp);
                    if (Free(q, true)) { choice = sp; dq = q; break; }
                }
            labels.Add(dq);
            swLabelAt[t] = choice;
        }
        // 4) названия участков (кнопка «Названия»): клетка участка поближе к середине, под путём или над ним — где свободно
        if (st.SecLabels == true)
        {
            var sf = GetFont(cs * 0.26f, false);
            var gapSecs = new HashSet<string>(gapList.Select(gp => gp.Sec));
            foreach (var (sec, keys) in Core.TilesBySection(st))
            {
                if (gapSecs.Contains(sec)) continue;
                var pool = SecLabelPool(st, keys);
                int mid = (pool.Count - 1) / 2;
                var sz = measure.MeasureString(sec, sf, PointF.Empty, StringFormat.GenericTypographic);
                Ob Rect(float cx, float cy) => new Ob(RectQ(new RectangleF(cx * cs - sz.Width / 2, cy * cs - sz.Height / 2, sz.Width, sz.Height)));
                // под путём, над путём, потом углы клетки; свободного нет — где меньше всего наложений
                var fr = new (float X, float Y)[] { (0.5f, 0.88f), (0.5f, 0.12f), (0.25f, 0.85f), (0.75f, 0.85f), (0.25f, 0.15f), (0.75f, 0.15f) };
                PointF pick = new(pool[mid].X + 0.5f, pool[mid].Y + 0.88f);
                Ob pq = Rect(pick.X, pick.Y);
                int bestScore = int.MaxValue;
                foreach (var (x, y) in pool.Select((p, i) => (p, i)).OrderBy(q => Math.Abs(q.i - mid)).Take(12).Select(q => q.p))
                {
                    foreach (var (fx, fy) in fr)
                    {
                        var q = Rect(x + fx, y + fy);
                        int score = 3 * CountHits(q, bodies) + 3 * CountHits(q, labels) + 2 * CountHits(q, fixedQ) + CountHits(q, tracks);
                        if (score < bestScore) { bestScore = score; pick = new PointF(x + fx, y + fy); pq = q; }
                        if (score == 0) break;
                    }
                    if (bestScore == 0) break;
                }
                labels.Add(pq);
                secLabelAt[sec] = pick;
            }
        }
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
        bool lampTest = IsLampTest(sig);
        bool fullColor = FullInColor(sig);    // [20.11.2026] маневровый — оба огня и в цветном виде
        var fullHead = GostLamps || lampTest || fullColor ? Aspects.FullHead(sig, t, dia) : null;
        var shown = GostLamps || fullColor ? Aspects.OnFullHead(fullHead, aspect.Lamps) : aspect.Lamps;
        if (lampTest)
        {
            // [ДОБАВЛЕНО 17.11.2026] проверка огней: горят все лампы своими цветами, «Н», указатель (8) и полоса
            shown = fullHead.Select(c => new LampLit(c)).ToArray();
            aspect = new Aspect
            {
                Lamps = shown, Text = "проверка огней", Indicator = Aspects.HasIndicator(sig) ? "Н" : null,
                RouteText = sig.HasRouteInd ? "8" : null, RouteColor = Aspects.RouteIndColor(sig.Kind),
            };
        }
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
        // [ИЗМЕНЕНО 19.11.2026] головки: у каждой группы огней — свой скруглённый корпус (как на рисунках Инструкции по
        // сигнализации РК, приложения 1, 3); отдельная головка пригласительного у мачты — маленькая круглая. В виде ГОСТ — промежутки
        // [ДОБАВЛЕНО 20.11.2026] мачта между головками: когда огни разбиты на головки или на мачте есть «окно» (указатель, полоса),
        // линия мачты идёт от ножки до последней головки — головки не «висят» в воздухе
        if (!gm.DwarfBlock && gm.Lamps.Length > 0 && (gm.Groups is { Length: > 1 } || !float.IsNaN(gm.IndX) || !float.IsNaN(gm.StripX0) || !float.IsNaN(gm.HumpX)
                                                      || (!GostLamps && FullInColor(sig) && gm.Groups == null && gm.Lamps.Length > 1)))
            g.DrawLine(pen, gm.Ax + gm.Fwd * cs * 0.12f, gm.Sy, gm.Lamps[^1].X, gm.Sy);
        var headCols = fullHead ?? Aspects.Head(sig, t);
        bool smallInv = !GostLamps && !dia && gm.Groups != null && gm.Groups[0] == 1 && gm.Groups.Length > 1
                        && Aspects.InvitationAtMast(sig, headCols);
        if (!GostLamps && !dia && gm.Groups != null)
        {
            using var hb = new SolidBrush(A(Color.FromArgb(0x2a, 0x30, 0x2e), alpha));
            int i0 = 0;
            foreach (var p in gm.Groups)
            {
                var l0 = gm.Lamps[i0]; var l1 = gm.Lamps[i0 + p - 1];
                float pad = smallInv && i0 == 0 ? gm.R * 1.0f : gm.R * 1.25f;
                float hx0 = Math.Min(l0.X, l1.X) - pad, hx1 = Math.Max(l0.X, l1.X) + pad, hh = 2 * pad;
                using var path = new GraphicsPath();
                float rad = hh / 2;    // концы корпуса — полукруги
                path.AddArc(hx0, gm.Sy - rad, hh, hh, 90, 180);
                path.AddArc(hx1 - hh, gm.Sy - rad, hh, hh, 270, 180);
                path.CloseFigure();
                g.FillPath(hb, path);
                i0 += p;
            }
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
            float lr = smallInv && i == 0 ? gm.R * 0.75f : gm.R;    // [19.11.2026] пригласительный в своей головке — меньше
            var rc = new RectangleF(l.X - lr, l.Y - lr, 2 * lr, 2 * lr);
            g.FillEllipse(fill, rc);
            g.DrawEllipse(pen, rc);
        }
        // [ДОБАВЛЕНО 13.11.2026] горочный и его повторитель — световой указатель (РУ-30-80 листы 45–46, стр. 53–54: «Г ⊢□◎○●⊘»);
        // тёмный квадрат, при осаживании горит белая «Н».
        // [ИЗМЕНЕНО 20.11.2026] указатель у мачты, под огнями (на листах 45–46 квадрат сразу за знаком мачты «⊢»), а не над ними
        if (Aspects.HasIndicator(sig) && gm.Lamps.Length > 0)
        {
            var last = gm.Lamps[^1];
            float half = gm.R * 0.95f;
            var c = !float.IsNaN(gm.HumpX) ? new PointF(gm.HumpX, gm.Sy) : new PointF(last.X + gm.Fwd * (gm.R + half + gm.R * 0.35f), last.Y);
            var box = new RectangleF(c.X - half, c.Y - half, 2 * half, 2 * half);
            using (var bb = new SolidBrush(A(Pal.Dark, alpha))) g.FillRectangle(bb, box);
            g.DrawRectangle(pen, box.X, box.Y, box.Width, box.Height);
            if (aspect.Indicator != null && cs >= 10)
                using (var ib = new SolidBrush(A(Color.White, alpha)))
                    g.DrawString(aspect.Indicator, GetFont(half * 1.5f, true), ib, c, Center);
        }
        // [ИЗМЕНЕНО 17.11.2026] маршрутный (путевой) указатель — на мачте между пригласительным и основными огнями
        // (РУ-30-80 лист 10: «Ч ⊣◎□⊘●○⊘»), без пригласительного — у мачты; или на отдельной мачте рядом (Инструкция РК п. 70).
        // Тёмный, при открытом маршруте горит номер пути: белым (входной, маневровый) или зелёным (выходной, маршрутный)
        if (sig.HasRouteInd && gm.Lamps.Length > 0)
        {
            float half = gm.Half > 0 ? gm.Half : gm.R * 0.95f;
            PointF c;
            if (sig.RouteIndOwnMast == true)
            {
                // отдельная мачта позади светофора (против хода), ножка и указатель — в ту же сторону, что у светофора
                float mx = gm.Ax - gm.Fwd * cs * 0.45f;
                g.DrawLine(pen, mx, foot.Y, mx, gm.Sy);
                g.DrawLine(pen, mx, gm.Sy, mx + gm.Fwd * cs * 0.08f, gm.Sy);
                c = new PointF(mx + gm.Fwd * (cs * 0.08f + half), gm.Sy);
            }
            else if (!float.IsNaN(gm.IndX)) c = new PointF(gm.IndX, gm.IndY);
            else c = new PointF(gm.Lamps[^1].X + gm.Fwd * (gm.R + half + gm.R * 0.35f + (Aspects.HasIndicator(sig) ? 2 * half + gm.R * 0.35f : 0)), gm.Lamps[^1].Y);
            var box = new RectangleF(c.X - half, c.Y - half, 2 * half, 2 * half);
            using (var bb = new SolidBrush(A(Pal.Dark, alpha))) g.FillRectangle(bb, box);
            using (var bp = new Pen(A(LampColor(Aspects.RouteIndColor(sig.Kind)), alpha), Math.Max(1f, lw * 0.8f)))
                g.DrawRectangle(bp, box.X, box.Y, box.Width, box.Height);
            if (aspect.RouteText != null && cs >= 10)
                using (var ib = new SolidBrush(A(LampColor(aspect.RouteColor), alpha)))
                    g.DrawString(aspect.RouteText, GetFont(half * (aspect.RouteText.Length > 2 ? 0.9f : 1.4f), true), ib, c, Center);
        }
        // [ИЗМЕНЕНО 17.11.2026] зелёная светящаяся полоса (Инструкция РК п. 15, 19) — под основными огнями (над пригласительным),
        // поперёк ряда огней, как горизонтальная полоса на мачте; в редакторе не горит (сигналы с полосой — во второй программе)
        // [ИЗМЕНЕНО 20.11.2026] полос может быть две (свойство «Зелёные полосы», Инструкция РК п. 15 подп. 3) — рядом, через промежуток
        if (sig.HasStrip && gm.Lamps.Length > 0)
        {
            float x0, y0, w = gm.StripW > 0 ? gm.StripW : gm.R * 0.7f, sg = gm.StripGap > 0 ? gm.StripGap : gm.R * 0.35f;
            if (!float.IsNaN(gm.StripX0)) { x0 = gm.StripX0; y0 = gm.StripY; }
            else { var last = gm.Lamps[^1]; x0 = last.X + gm.Fwd * gm.R * 1.4f; y0 = last.Y; }
            using var bb = new SolidBrush(A(lampTest ? Pal.LampGreen : Color.FromArgb(0x1d, 0x4a, 0x2c), alpha));
            using var bp = new Pen(A(Pal.LampGreen, alpha), Math.Max(1f, lw * 0.8f));
            for (int j = 0; j < Math.Max(1, gm.Strips); j++)
            {
                float a0 = x0 + gm.Fwd * j * (w + sg), a1 = a0 + gm.Fwd * w;
                var bar = RectangleF.FromLTRB(Math.Min(a0, a1), y0 - gm.R * 1.2f, Math.Max(a0, a1), y0 + gm.R * 1.2f);
                g.FillRectangle(bb, bar);
                g.DrawRectangle(bp, bar.X, bar.Y, bar.Width, bar.Height);
            }
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
        var (fx, fy) = SwitchLabelFrac(t);    // [ИЗМЕНЕНО 23.11.2026] место из раскладки (не под светофором); по умолчанию — Core.SwitchLabelAt
        return new PointF(x + cs * fx, y + cs * fy);
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
        LayoutScheme(st);    // [ДОБАВЛЕНО 23.11.2026] места светофоров, подписей и номеров стрелок без наложений

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

        // ---- [ДОБАВЛЕНО 11.11.2026] названия путей в разрыве линии («→ 3П ←», РУ-30-80 стр. 89–90):
        // на время рисования путей вырезаем узкую полоску вдоль линии — цвета участков и стыки не трогаем ----
        var gaps = st.TrackGaps != false && cs >= 12 ? TrackGapList(st, ox, oy, cs) : new List<TrackGap>();
        var clipBeforeTracks = g.Clip;
        foreach (var gp in gaps) g.ExcludeClip(new Region(gp.Cut));

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
        g.Clip = clipBeforeTracks;
        DrawTrackGaps(g, gaps, cs);

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

        // ---- [ДОБАВЛЕНО 12.11.2026] подписи тормозных позиций (ТП1, ТП2, ТП3 или своя) — под полосой замедлителя;
        // несколько замедлителей подряд с одной подписью — одна подпись посередине ----
        if (cs >= 14)
        {
            var f = GetFont(cs * 0.24f, true);
            using var bt = new SolidBrush(Pal.Text);
            foreach (var k in Core.BrakeLabelTiles(st))
            {
                if (IsHidden(k) || !st.Tiles.TryGetValue(k, out var t)) continue;
                var (x, y) = Core.Parse(k);
                if (!Visible(x, y)) continue;
                g.DrawString(t.Brk.Label, f, bt, BrakeLabelPos(t, SX(x), SY(y), cs), Center);
            }
        }

        // ---- [ДОБАВЛЕНО 17.11.2026] знаки у пути и стрелочные указатели нецентрализованных стрелок ----
        if (cs >= 10)
            foreach (var e in entries)
            {
                if (IsHidden(e.K)) continue;
                if (e.T.Signs != null) DrawSigns(g, e.T, SX(e.X), SY(e.Y), cs);
                if (e.T.T == "switch" && e.T.IsManual) DrawSwitchIndicator(g, e.T, SX(e.X), SY(e.Y), cs);
            }

        // ---- названия участков ----
        if (st.SecLabels == true && cs >= 14)    // [ИЗМЕНЕНО 11.11.2026] настройка станции, по умолчанию выкл.
        {
            var f = GetFont(cs * 0.26f, false);
            using var b = new SolidBrush(Pal.SecName);
            var gapSecs = new HashSet<string>(gaps.Select(gp => gp.Sec));    // [11.11.2026] путь уже подписан в разрыве
            foreach (var (sec, keys) in Core.TilesBySection(st))
            {
                if (gapSecs.Contains(sec)) continue;
                // [ИЗМЕНЕНО 23.11.2026] место — из раскладки (не на светофоре, номере стрелки, другой подписи)
                if (!secLabelAt.TryGetValue(sec, out var at))
                {
                    var pool = SecLabelPool(st, keys);
                    var (px, py) = pool[(pool.Count - 1) / 2];
                    at = new PointF(px + 0.5f, py + 0.88f);
                }
                if (!Visible((int)at.X, (int)at.Y)) continue;
                g.DrawString(sec, f, b, new PointF(SX(at.X), SY(at.Y)), Center);
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
                bool dia = Aspects.IsDiamond(st, new SignalEntry { Key = e.K, Idx = i, Sig = e.T.Sigs[i], Tile = e.T });
                // [ДОБАВЛЕНО 23.11.2026] светофор, которому не нашлось места, — полупрозрачный (стрелку / изгиб под ним видно)
                bool covers = sigCovers.ContainsKey(e.T.Sigs[i]);
                DrawSignal(g, e.T, e.T.Sigs[i], SX(e.X), SY(e.Y), cs,
                    aspect: test && T.Aspects.TryGetValue(e.K + "#" + i, out var asp) ? asp : null,
                    ring: selected || pending,
                    ringColor: pending ? Color.White : Pal.Sel,
                    alpha: covers ? 150 : 255,
                    diamond: dia);
                if (covers && !test) DrawCoverMark(g, GetSigGeom(e.T, e.T.Sigs[i], SX(e.X), SY(e.Y), cs, dia), cs);
            }
        }

        // ---- [ДОБАВЛЕНО 10.11.2026] свободный текст (не по клеткам) ----
        for (int i = 0; i < st.Labels.Count; i++)
        {
            var l = st.Labels[i];
            var r = LabelRect(l, ox, oy, cs, ui.Cam.Z);
            if (r.Right < 0 || r.Bottom < 0 || r.Left > W || r.Top > H) continue;
            using var lb = new SolidBrush(Pal.Text);
            g.DrawString(l.Text, LabelFont(l, ui.Cam.Z), lb, r.Location, StringFormat.GenericTypographic);
            if (!test && ui.SelLabel == i)
            {
                using var lp = new Pen(Pal.Sel, 2) { DashPattern = new[] { 2.5f, 1.5f } };
                g.DrawRectangle(lp, r.X - 3, r.Y - 2, r.Width + 6, r.Height + 4);
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
