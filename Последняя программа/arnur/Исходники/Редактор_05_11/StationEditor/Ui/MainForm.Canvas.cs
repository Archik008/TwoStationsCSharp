using StationEditor.Model;

namespace StationEditor.Ui;

// =====================================================================
//  ХОЛСТ: камера, мышь, действия над клетками
// =====================================================================
public sealed partial class MainForm
{
    sealed class Drag
    {
        public string Kind;                 // pan | erase | place | brush | band | move
        public int Sx, Sy;
        public float Cx, Cy;
        public Point Last, First, Start;
        public int Placed;
        public int Label;                   // [10.11.2026] перетаскиваемый свободный текст
        public double Lx, Ly;               //   и где он был в начале
    }

    Drag drag;

    float CS => Core.Cell * ui.Cam.Z;

    Point CellAt(float mx, float my) =>
        new((int)Math.Floor((mx - ui.Cam.X) / CS), (int)Math.Floor((my - ui.Cam.Y) / CS));

    SigRef? SignalAt(float mx, float my)
    {
        var p = CellAt(mx, my);
        float c = CS;
        for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                var k = Core.Key(p.X + dx, p.Y + dy);
                if (!st.Tiles.TryGetValue(k, out var t) || !t.HasSigs) continue;
                for (int i = 0; i < t.Sigs.Count; i++)
                {
                    // [ИЗМЕНЕНО 09.11.2026] с учётом поворота светофора вдоль наклонного пути
                    if (Renderer.GetSigGeom(t, t.Sigs[i], ui.Cam.X + (p.X + dx) * c, ui.Cam.Y + (p.Y + dy) * c, c).Hit(mx, my))
                        return new SigRef(k, i);
                }
            }
        return null;
    }

    void ZoomAt(float mx, float my, float z)
    {
        z = Math.Clamp(z, 0.2f, 3f);
        float wx = (mx - ui.Cam.X) / ui.Cam.Z, wy = (my - ui.Cam.Y) / ui.Cam.Z;
        ui.Cam.Z = z;
        ui.Cam.X = mx - wx * z;
        ui.Cam.Y = my - wy * z;
        Draw();
    }

    void FitView()
    {
        var keys = st.Tiles.Keys.Concat(st.Texts.Keys).ToList();
        int W = canvas.ClientSize.Width, H = canvas.ClientSize.Height;
        if ((keys.Count == 0 && st.Objects.Count == 0) || W < 10 || H < 10) { ui.Cam = new Camera(); Draw(); return; }
        int x1 = int.MaxValue, y1 = int.MaxValue, x2 = int.MinValue, y2 = int.MinValue;
        foreach (var k in keys)
        {
            var (x, y) = Core.Parse(k);
            x1 = Math.Min(x1, x); y1 = Math.Min(y1, y); x2 = Math.Max(x2, x); y2 = Math.Max(y2, y);
        }
        foreach (var (k, o) in st.Objects)
        {
            var (x, y) = Core.Parse(k);
            x1 = Math.Min(x1, x); y1 = Math.Min(y1, y); x2 = Math.Max(x2, x + o.W - 1); y2 = Math.Max(y2, y + o.H - 1);
        }
        float bw = (x2 - x1 + 3) * Core.Cell, bh = (y2 - y1 + 3) * Core.Cell;
        float z = Math.Clamp(Math.Min(W / bw, H / bh), 0.2f, 2f);
        ui.Cam.Z = z;
        ui.Cam.X = (W - (x2 - x1 + 1) * Core.Cell * z) / 2 - x1 * Core.Cell * z;
        ui.Cam.Y = (H - (y2 - y1 + 1) * Core.Cell * z) / 2 - y1 * Core.Cell * z;
        Draw();
    }

    void CenterOn(string key)
    {
        var (x, y) = Core.Parse(key);
        ui.Cam.X = canvas.ClientSize.Width / 2f - (x + 0.5f) * CS;
        ui.Cam.Y = canvas.ClientSize.Height / 2f - (y + 0.5f) * CS;
        Draw();
    }

    // =================================================================
    //  ДЕЙСТВИЯ НАД КЛЕТКАМИ
    // =================================================================
    int CurVariant => ui.Variant.TryGetValue(ui.PlaceType, out int v) ? v : 0;

    Tile MakeTile(string type, int v, Tile old)
    {
        var t = new Tile { T = type, V = v, Sec = old != null ? old.Sec : ui.BrushSec };
        if (old?.Sigs != null) t.Sigs = old.Sigs;
        if (type == "switch")
        {
            t.Name = old != null && old.T == "switch" ? old.Name : "";
            if (!string.IsNullOrEmpty(old?.Group)) t.Group = old.Group;
            if (old?.Plus == false) t.Plus = false;
        }
        return t;
    }

    void PlaceAt(int x, int y, int? v = null)
    {
        var k = Core.Key(x, y);
        st.Tiles.TryGetValue(k, out var old);
        st.Tiles[k] = MakeTile(ui.PlaceType, v ?? CurVariant, old);
    }

    void EraseAt(int x, int y)
    {
        var k = Core.Key(x, y);
        st.Tiles.Remove(k);
        st.Texts.Remove(k);
        ui.Sel.Remove(k);
        if (ui.SelSig?.Key == k) ui.SelSig = null;
        // [ДОБАВЛЕНО 05.11.2026] ластик стирает и объект, который накрывает клетку
        var ok = Core.ObjectAt(st, x, y);
        if (ok != null)
        {
            st.Objects.Remove(ok);
            if (ui.SelObj == ok) ui.SelObj = null;
        }
        // [ДОБАВЛЕНО 10.11.2026] и свободный текст, который начинается в этой клетке
        if (st.Labels.RemoveAll(l => (int)Math.Floor(l.X) == x && (int)Math.Floor(l.Y + 0.25) == y) > 0) ui.SelLabel = null;
    }

    // =================================================================
    //  [ДОБАВЛЕНО 05.11.2026] ОБЪЕКТЫ: амперметр, вокзал, часы
    // =================================================================
    void PlaceObject(int x, int y)
    {
        var ot = Core.ObjTypeOf(ui.ObjType);
        if (!Core.ObjectFits(st, x, y, ot.W, ot.H)) { Msg("Здесь уже стоит другой объект — выберите свободное место", true); return; }
        var k = Core.Key(x, y);
        string name = ot.Id == "building" ? (string.IsNullOrWhiteSpace(st.Name) ? "Вокзал" : st.Name) : ot.DefaultName;
        Edit(() =>
        {
            st.Objects[k] = new StationObject { Type = ot.Id, Name = name, W = ot.W, H = ot.H };
            ui.Sel.Clear();
            ui.SelSig = null;
            ui.SelObj = k;
        });
        ShowTab("props");
        Msg($"Поставлен объект «{ot.Name}». Название и размер — на вкладке «Свойства»");
    }

    void SelectObject(string k)
    {
        ui.Sel.Clear();
        ui.SelSig = null;
        ui.SelLabel = null;
        ui.SelObj = k;
        ShowTab("props");
        Draw();
    }

    void MoveObject(int dx, int dy)
    {
        if (ui.SelObj == null || !st.Objects.TryGetValue(ui.SelObj, out var o) || (dx == 0 && dy == 0)) return;
        var (x, y) = Core.Parse(ui.SelObj);
        if (!Core.ObjectFits(st, x + dx, y + dy, o.W, o.H, ui.SelObj)) { Msg("Туда не помещается — мешает другой объект", true); return; }
        Edit(() =>
        {
            st.Objects.Remove(ui.SelObj);
            ui.SelObj = Core.Key(x + dx, y + dy);
            st.Objects[ui.SelObj] = o;
        });
    }

    // =================================================================
    //  [ДОБАВЛЕНО 10.11.2026] СВОБОДНЫЙ ТЕКСТ — подпись в любом месте, двигается мышью не по клеткам
    // =================================================================
    int? LabelAt(float mx, float my)
    {
        for (int i = st.Labels.Count - 1; i >= 0; i--)
        {
            var r = Renderer.LabelRect(st.Labels[i], ui.Cam.X, ui.Cam.Y, CS, ui.Cam.Z);
            r.Inflate(4, 3);
            if (r.Contains(mx, my)) return i;
        }
        return null;
    }

    void SelectLabel(int i)
    {
        ui.Sel.Clear();
        ui.SelSig = null;
        ui.SelObj = null;
        ui.SelLabel = i;
        ShowTab("props");
        RefreshPanels();
        Draw();
    }

    void StartLabelDrag(int i, MouseEventArgs e)
    {
        SelectLabel(i);
        Begin();
        drag = new Drag { Kind = "label", Sx = e.X, Sy = e.Y, Label = i, Lx = st.Labels[i].X, Ly = st.Labels[i].Y };
    }

    void AddLabel(float mx, float my)
    {
        var s = InputBox.Show(this, "Свободный текст", "Текст подписи:", "");
        if (string.IsNullOrWhiteSpace(s)) return;
        var l = new FreeLabel { Text = s.Trim() };
        // щёлкнули — там будет левый край текста, по высоте — его середина
        float h = Renderer.LabelRect(l, 0, 0, CS, ui.Cam.Z).Height / CS;
        l.X = Math.Round((mx - ui.Cam.X) / CS, 3);
        l.Y = Math.Round((my - ui.Cam.Y) / CS - h / 2, 3);
        Edit(() =>
        {
            st.Labels.Add(l);
            ui.Sel.Clear(); ui.SelSig = null; ui.SelObj = null;
            ui.SelLabel = st.Labels.Count - 1;
        });
        ShowTab("props");
        Msg("Свободный текст поставлен. Тяните мышью — подвинуть, двойной щелчок — поправить");
    }

    void EditLabel(int i)
    {
        if (i < 0 || i >= st.Labels.Count) return;
        var s = InputBox.Show(this, "Свободный текст", "Текст подписи (пусто — удалить):", st.Labels[i].Text);
        if (s == null) return;
        Edit(() =>
        {
            if (string.IsNullOrWhiteSpace(s)) { st.Labels.RemoveAt(i); ui.SelLabel = null; }
            else st.Labels[i].Text = s.Trim();
        });
    }

    void MoveLabel(double dx, double dy)
    {
        if (ui.SelLabel is not int i || i >= st.Labels.Count) return;
        Edit(() => { st.Labels[i].X = Math.Round(st.Labels[i].X + dx, 3); st.Labels[i].Y = Math.Round(st.Labels[i].Y + dy, 3); }, false);
    }

    void PaintAt(int x, int y)
    {
        if (st.Tiles.TryGetValue(Core.Key(x, y), out var t)) t.Sec = ui.BrushSec;
    }

    /// <summary>Клетки на отрезке (для протягивания мышью без пропусков).</summary>
    static List<Point> LineCells(Point a, Point b)
    {
        var res = new List<Point>();
        int x = a.X, y = a.Y, x2 = b.X, y2 = b.Y;
        int dx = Math.Abs(x2 - x), dy = -Math.Abs(y2 - y), sx = x < x2 ? 1 : -1, sy = y < y2 ? 1 : -1;
        int err = dx + dy;
        for (; ; )
        {
            res.Add(new Point(x, y));
            if (x == x2 && y == y2) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x += sx; }
            if (e2 <= dx) { err += dx; y += sy; }
        }
        return res;
    }

    void DeleteSelection()
    {
        if (ui.SelLabel is int li && li < st.Labels.Count)    // [ДОБАВЛЕНО 10.11.2026]
        {
            Edit(() => { st.Labels.RemoveAt(li); ui.SelLabel = null; });
            Msg("Свободный текст удалён");
            return;
        }
        if (ui.SelObj != null)
        {
            var ok = ui.SelObj;
            Edit(() => { st.Objects.Remove(ok); ui.SelObj = null; });
            Msg("Объект удалён");
            return;
        }
        if (ui.SelSig is SigRef sr)
        {
            Edit(() =>
            {
                if (st.Tiles.TryGetValue(sr.Key, out var t) && t.Sigs != null && sr.Idx < t.Sigs.Count)
                {
                    t.Sigs.RemoveAt(sr.Idx);
                    if (t.Sigs.Count == 0) t.Sigs = null;
                }
                ui.SelSig = null;
            });
            return;
        }
        if (ui.Sel.Count == 0) return;
        int n = ui.Sel.Count;
        Edit(() => { foreach (var k in ui.Sel.ToList()) { var (x, y) = Core.Parse(k); EraseAt(x, y); } });
        Msg($"Удалено клеток: {n}");
    }

    void MoveSelection(int dx, int dy)
    {
        if (ui.Sel.Count == 0 || (dx == 0 && dy == 0)) return;
        Edit(() =>
        {
            var tiles = new Dictionary<string, Tile>();
            var texts = new Dictionary<string, TextItem>();
            foreach (var k in ui.Sel)
            {
                if (st.Tiles.Remove(k, out var t)) tiles[k] = t;
                if (st.Texts.Remove(k, out var tx)) texts[k] = tx;
            }
            string Shift(string k) { var (x, y) = Core.Parse(k); return Core.Key(x + dx, y + dy); }
            var nsel = new HashSet<string>();
            foreach (var (k, t) in tiles) { st.Tiles[Shift(k)] = t; nsel.Add(Shift(k)); }
            foreach (var (k, t) in texts) { st.Texts[Shift(k)] = t; nsel.Add(Shift(k)); }
            ui.Sel = nsel;
            if (ui.SelSig is SigRef sr) ui.SelSig = new SigRef(Shift(sr.Key), sr.Idx);
        });
    }

    void RotateSelected(int step)
    {
        if (ui.Tool == "place")
        {
            int n = Core.Types[ui.PlaceType].Variants.Length;
            ui.Variant[ui.PlaceType] = ((CurVariant + step) % n + n) % n;
            RefreshToolOpts();
            RefreshPalette();
            UpdateGhost();
            Draw();
            return;
        }
        var keys = ui.Sel.Where(st.Tiles.ContainsKey).ToList();
        if (keys.Count == 0) return;
        Edit(() =>
        {
            foreach (var k in keys)
            {
                var t = st.Tiles[k];
                int n = Core.Types[t.T].Variants.Length;
                t.V = ((t.V + step) % n + n) % n;
            }
        });
    }

    void AddSignal(string k, string dir, string kind)
    {
        if (!st.Tiles.TryGetValue(k, out var t)) { Msg("Светофор ставится на путь — сначала поставьте кубик пути", true); return; }
        int i = t.Sigs?.FindIndex(s => s.Dir == dir) ?? -1;
        if (i >= 0) { SelectSignal(k, i); Msg("Здесь уже есть светофор этого направления — выбран"); return; }
        // [ИЗМЕНЕНО 10.11.2026] вид «Авто» — по участку (путь → выходной, перегон → входной / проходной, тупик → маневровый);
        // имя — по шаблону из «Обозначений…», буква — от ближайшего светофора (Model\Naming.cs). Остальные светофоры не трогаются.
        if (kind == "auto") kind = Naming.DefaultKind(st, k, dir) ?? "exit";
        string note = null;
        Edit(() =>
        {
            var sig = new Signal { Dir = dir, Kind = kind, Id = Core.NextSignalId(st) };    // id — автоинкремент (09.11.2026)
            (t.Sigs ??= new List<Signal>()).Add(sig);
            note = Naming.NameNew(st, new SignalEntry { Key = k, Idx = t.Sigs.Count - 1, Sig = sig, Tile = t });
            ui.Sel = new HashSet<string> { k };
            ui.SelSig = new SigRef(k, t.Sigs.Count - 1);
        });
        ShowTab("props");
        var added = t.Sigs?.LastOrDefault(s => s.Dir == dir);
        if (added != null)
            Msg($"{Core.SigKindName(added.Kind)} {added.Name}" + (note != null ? " — " + note : ""), note != null);
    }

    void SelectSignal(string k, int i)
    {
        ui.Sel = new HashSet<string> { k };
        ui.SelSig = new SigRef(k, i);
        ui.SelLabel = null;
        ShowTab("props");
        Draw();
    }

    void EditText(string k)
    {
        st.Texts.TryGetValue(k, out var cur);
        var s = InputBox.Show(this, "Надпись", "Надпись (пусто — удалить):", cur?.Text ?? "");
        if (s == null) return;
        Edit(() =>
        {
            if (string.IsNullOrWhiteSpace(s)) st.Texts.Remove(k);
            else st.Texts[k] = new TextItem { Text = s.Trim(), Size = cur?.Size ?? 14 };
        });
    }

    string NewSection(bool thenSelect = true)
    {
        var name = (InputBox.Show(this, "Новый участок", "Название участка (например 1П, 2-4СП, НАУ):", "") ?? "").Trim();
        if (name.Length == 0) return "";
        Edit(() => { if (!st.Sections.ContainsKey(name)) st.Sections[name] = new SectionInfo { Type = "auto" }; });
        if (thenSelect) ui.BrushSec = name;
        RefreshPanels();
        return name;
    }

    // =================================================================
    //  МЫШЬ
    // =================================================================
    void WireCanvas()
    {
        canvas.MouseDown += Canvas_MouseDown;
        canvas.MouseMove += Canvas_MouseMove;
        canvas.MouseUp += (_, _) => FinishDrag();
        canvas.MouseCaptureChanged += (_, _) => { if (drag != null && !canvas.Capture) FinishDrag(); };
        canvas.MouseLeave += (_, _) =>
        {
            if (drag != null) return;
            ui.Hover = null;
            ui.Ghost = null;
            Draw();
        };
        canvas.MouseWheel += (_, e) => ZoomAt(e.X, e.Y, ui.Cam.Z * (e.Delta > 0 ? 1.15f : 1 / 1.15f));
        // [ДОБАВЛЕНО 10.11.2026] двойной щелчок по свободному тексту — поправить
        canvas.MouseDoubleClick += (_, e) =>
        {
            if (ui.IsTest || e.Button != MouseButtons.Left || ui.Tool is not ("select" or "label")) return;
            if (LabelAt(e.X, e.Y) is int i) EditLabel(i);
        };
    }

    void Canvas_MouseDown(object sender, MouseEventArgs e)
    {
        canvas.Focus();                 // применяет недописанное поле справа (как «change» в браузере)
        if (drag != null) return;
        var p = CellAt(e.X, e.Y);
        int gx = p.X, gy = p.Y;
        var k = Core.Key(gx, gy);
        bool shiftK = (ModifierKeys & Keys.Shift) != 0, ctrlK = (ModifierKeys & Keys.Control) != 0;

        if (e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && spaceDown))
        {
            drag = new Drag { Kind = "pan", Sx = e.X, Sy = e.Y, Cx = ui.Cam.X, Cy = ui.Cam.Y };
            return;
        }
        if (ui.IsTest)
        {
            if (e.Button == MouseButtons.Left) TestClick(gx, gy, SignalAt(e.X, e.Y));
            if (e.Button == MouseButtons.Right) TestOccupy(k);
            return;
        }
        if (e.Button == MouseButtons.Right)
        {
            Begin();
            EraseAt(gx, gy);
            drag = new Drag { Kind = "erase", Last = p };
            Draw();
            return;
        }
        if (e.Button != MouseButtons.Left) return;

        switch (ui.Tool)
        {
            case "select":
            {
                // [ДОБАВЛЕНО 10.11.2026] свободный текст — поверх всего: выбрать и тянуть (не по клеткам)
                if (!shiftK && LabelAt(e.X, e.Y) is int lb) { StartLabelDrag(lb, e); return; }
                ui.SelLabel = null;
                var sg = SignalAt(e.X, e.Y);
                if (sg is SigRef sr) { ui.SelObj = null; SelectSignal(sr.Key, sr.Idx); RefreshPanels(); return; }
                ui.SelSig = null;
                // [ДОБАВЛЕНО 05.11.2026] объект: выбрать и тянуть
                var ok = shiftK ? null : Core.ObjectAt(st, gx, gy);
                if (ok != null)
                {
                    SelectObject(ok);
                    drag = new Drag { Kind = "objmove", Start = p };
                    ui.ObjMove = Point.Empty;
                    break;
                }
                ui.SelObj = null;
                bool has = st.Tiles.ContainsKey(k) || st.Texts.ContainsKey(k);
                if (has && ui.Sel.Contains(k) && !shiftK)
                {
                    drag = new Drag { Kind = "move", Start = p };
                    ui.Moving = new Moving { Keys = new HashSet<string>(ui.Sel) };
                }
                else if (has && !shiftK)
                {
                    ui.Sel = new HashSet<string> { k };
                    drag = new Drag { Kind = "move", Start = p };
                    ui.Moving = new Moving { Keys = new HashSet<string>(ui.Sel) };
                    RefreshPanels();
                }
                else
                {
                    if (!shiftK) ui.Sel.Clear();
                    drag = new Drag { Kind = "band" };
                    ui.Band = new Band { X1 = gx, Y1 = gy, X2 = gx, Y2 = gy };
                    RefreshPanels();
                }
                break;
            }
            case "place":
                Begin();
                PlaceAt(gx, gy);
                drag = new Drag { Kind = "place", Last = p, First = p, Placed = 1 };
                if (ui.PlaceType == "switch") { ui.Sel = new HashSet<string> { k }; ui.SelSig = null; }
                break;
            case "erase":
                Begin();
                EraseAt(gx, gy);
                drag = new Drag { Kind = "erase", Last = p };
                break;
            case "brush":
                if (ctrlK)
                {
                    if (st.Tiles.TryGetValue(k, out var t))
                    {
                        ui.BrushSec = t.Sec ?? "";
                        RefreshToolOpts();
                        Msg(!string.IsNullOrEmpty(t.Sec) ? $"Взят участок «{t.Sec}»" : "Взято: без участка");
                    }
                    return;
                }
                if (string.IsNullOrEmpty(ui.BrushSec) &&
                    !Confirm("Участок не выбран — кисть будет СНИМАТЬ участок с клеток. Продолжить?")) return;
                Begin();
                PaintAt(gx, gy);
                drag = new Drag { Kind = "brush", Last = p };
                break;
            case "signal":
                AddSignal(k, ui.SigDir, ui.SigKind);
                break;
            case "text":
                EditText(k);
                break;
            case "object":
                PlaceObject(gx, gy);
                break;
            case "label":    // [ДОБАВЛЕНО 10.11.2026]
                if (LabelAt(e.X, e.Y) is int li) { StartLabelDrag(li, e); return; }
                AddLabel(e.X, e.Y);
                break;
        }
        Draw();
    }

    void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        var p = CellAt(e.X, e.Y);
        bool moved = ui.Hover is not Point h || h != p;
        ui.Hover = p;
        if (moved) { UpdateGhost(); ShowCellInfo(p.X, p.Y); }

        if (drag == null) { if (moved) Draw(); return; }
        if (drag.Kind == "pan")
        {
            ui.Cam.X = drag.Cx + e.X - drag.Sx;
            ui.Cam.Y = drag.Cy + e.Y - drag.Sy;
            Draw();
            return;
        }
        if (drag.Kind == "label")    // [ДОБАВЛЕНО 10.11.2026] плавно, за мышью, не по клеткам
        {
            if (drag.Label < st.Labels.Count)
            {
                st.Labels[drag.Label].X = Math.Round(drag.Lx + (e.X - drag.Sx) / CS, 3);
                st.Labels[drag.Label].Y = Math.Round(drag.Ly + (e.Y - drag.Sy) / CS, 3);
            }
            Draw();
            return;
        }
        if (!moved) return;
        if (drag.Kind == "objmove") ui.ObjMove = new Point(p.X - drag.Start.X, p.Y - drag.Start.Y);
        else if (drag.Kind == "band") { ui.Band.X2 = p.X; ui.Band.Y2 = p.Y; }
        else if (drag.Kind == "move") { ui.Moving.Dx = p.X - drag.Start.X; ui.Moving.Dy = p.Y - drag.Start.Y; }
        else
        {
            foreach (var c in LineCells(drag.Last, p).Skip(1))
            {
                if (drag.Kind == "place")
                {
                    // прямой путь сам поворачивается по направлению протягивания: ─ ╱ ╲
                    int? v = null;
                    if (ui.PlaceType == "straight")
                    {
                        int ddx = c.X - drag.Last.X, ddy = c.Y - drag.Last.Y;
                        v = ddy == 0 ? 0 : (ddx * ddy < 0 ? 1 : 2);
                        if (drag.Placed == 1) PlaceAt(drag.First.X, drag.First.Y, v);
                    }
                    PlaceAt(c.X, c.Y, v);
                    drag.Placed++;
                }
                else if (drag.Kind == "erase") EraseAt(c.X, c.Y);
                else if (drag.Kind == "brush") PaintAt(c.X, c.Y);
                drag.Last = c;
            }
        }
        Draw();
    }

    void FinishDrag()
    {
        if (drag == null) return;
        var d = drag;
        drag = null;
        if (d.Kind == "label")
        {
            End(false);
            RefreshPanels();
        }
        else if (d.Kind == "objmove")
        {
            var m = ui.ObjMove ?? Point.Empty;
            ui.ObjMove = null;
            if (m != Point.Empty) MoveObject(m.X, m.Y);
        }
        else if (d.Kind == "band")
        {
            var b = ui.Band;
            ui.Band = null;
            for (int x = Math.Min(b.X1, b.X2); x <= Math.Max(b.X1, b.X2); x++)
                for (int y = Math.Min(b.Y1, b.Y2); y <= Math.Max(b.Y1, b.Y2); y++)
                {
                    var k = Core.Key(x, y);
                    if (st.Tiles.ContainsKey(k) || st.Texts.ContainsKey(k)) ui.Sel.Add(k);
                }
            RefreshPanels();
            if (ui.Sel.Count > 1) Msg($"Выделено клеток: {ui.Sel.Count}. Тяните — перенести, Delete — удалить, R — повернуть");
        }
        else if (d.Kind == "move")
        {
            var m = ui.Moving;
            ui.Moving = null;
            if (m != null && (m.Dx != 0 || m.Dy != 0)) MoveSelection(m.Dx, m.Dy);
        }
        else if (d.Kind != "pan")
        {
            End();
            RefreshPanels();
        }
        Draw();
    }

    void UpdateGhost()
    {
        ui.Ghost = null;
        ui.HoverColor = null;
        if (ui.IsTest || ui.Hover == null) return;
        if (ui.Tool == "place") ui.Ghost = new Tile { T = ui.PlaceType, V = CurVariant };
        if (ui.Tool == "brush") ui.HoverColor = !string.IsNullOrEmpty(ui.BrushSec) ? Core.SecColor(st, ui.BrushSec) : Color.White;
        if (ui.Tool == "erase") ui.HoverColor = Pal.Bad;
    }

    void ShowCellInfo(int gx, int gy)
    {
        var s = $"Клетка {gx}, {gy}";
        if (st.Tiles.TryGetValue(Core.Key(gx, gy), out var t))
        {
            s += $" · {Core.Types[t.T].Name}";
            if (t.T == "switch") s += " " + (string.IsNullOrEmpty(t.Name) ? "?" : t.Name);
            s += !string.IsNullOrEmpty(t.Sec) ? $" · {t.Sec}" : " · без участка";
        }
        var ok = Core.ObjectAt(st, gx, gy);
        if (ok != null) s += " · " + Core.ObjTypeOf(st.Objects[ok].Type).Name;
        sCell.Text = s;
    }
}
