using System.Text.Json.Nodes;
using StationApp.Yard;
using Ed = StationEditor.Model;
using TC = StationEditor.Model.Core;

namespace StationApp.Scheme;

// =====================================================================
//  [ДОБАВЛЕНО 24.11.2026] СОРТИРОВОЧНАЯ ГОРКА ИЗ ФАЙЛА РЕДАКТОРА СТАНЦИЙ (.station.json)
//
//  Файл с пометкой  "kind": "yard"  (Артем ставит её в редакторе) «Станция по схеме» открывает не пультом ДСП,
//  а окном «Сортировочной» (пульт дежурного по горке) с той же моделью: надвиг, роспуск, физика скатывания,
//  замедлители и АРС, ГАЦ, листок, нарушения. Горка берётся из рисунка кубиками:
//
//    горочный светофор (вид "hump")      — где кончается путь надвига; направление роспуска — его направление (→ / ←)
//    вперёд от него по кубикам           — горка: стрелки (обычные или симметричные) расходятся деревом,
//                                          концы веток — сортировочные пути 1, 2, 3… сверху вниз
//    назад от него                       — путь надвига; стрелки расходятся на пути парка приёма П1, П2… сверху вниз
//    кубики «Тормозная позиция» (brake)  — замедлители (подпись ТП1, ТП2…; одинаковые подписи — «-А», «-Б»)
//    остальные кубики                    — рисуются серым (депо, обходные, вытяжки — как нарисовал автор)
//  Стрелки нумеруются сами по порядку от горба (1, 2, 3…), пути — сверху вниз; номера и названия на рисунке —
//  только для вида. Длина клетки подбирается сама: от светофора до путей ≈ 246 м (или "cellMeters" в разделе "yard"); пути и пути парка приёма по
//  длине — как в «Сортировочной» (314 и 300 м), как бы коротко они ни были нарисованы. Уклоны — как в «Сортировочной»:
//  подъём 10‰ перед горбом, 45‰ скоростной участок, 14‰ на первой ТП, 8‰ до второй, 9‰ на второй, 1,5‰ в стрелочной
//  зоне, 0,6‰ на путях и встречный подъём в конце. Необязательный раздел "yard" в файле — см. ФОРМАТ_ГОРКИ.txt.
// =====================================================================

public static class YardFromJson
{
    /// <summary>Файл — сортировочная горка (в корне "kind": "yard" / "sorting" / "сортировочная")?</summary>
    public static bool IsYard(string path)
    {
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions
            { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var k = (root?["kind"] as JsonValue)?.ToString()?.Trim().ToLowerInvariant();
            return k is "yard" or "sorting" or "hump" or "сортировочная" or "горка";
        }
        catch { return false; }
    }

    // ---------------- обход кубиков ----------------
    /// <summary>Шаг по кубику: точки (в клетках) от входа до выхода и тормозная позиция, если это она.</summary>
    sealed class Step
    {
        public string Key;
        public Ed.Tile Tile;
        public PointF[] Pts;
        public double Len;   // клеток
    }

    /// <summary>Ветка дерева: шаги до стрелки (Fork) или до конца.</summary>
    sealed class Branch
    {
        public readonly List<Step> Steps = new();
        public Branch Plus, Minus;       // у развилки — ветки за стрелкой
        public string ForkKey;           // кубик стрелки (развилки) в конце ветки
        public double S0, S1;            // метры по пути (s), у первой ветки — от светофора
        public int Lo, Hi;               // какие пути (листья) под веткой
        public int Leaf;                 // у листа — номер пути
        public double Len => Steps.Sum(x => x.Len);
        public bool IsLeaf => Plus == null;
    }

    static PointF Side(int x, int y, string d) { var D = TC.Dirs[d]; return new PointF(x + D.Px, y + D.Py); }
    static PointF Center(int x, int y) => new(x + 0.5f, y + 0.5f);
    static double Dist(PointF a, PointF b) => Math.Sqrt((a.X - b.X) * (a.X - b.X) + (a.Y - b.Y) * (a.Y - b.Y));
    static double PolyLen(PointF[] p) { double l = 0; for (int i = 1; i < p.Length; i++) l += Dist(p[i - 1], p[i]); return l; }

    static PointF[] Through(int x, int y, string a, string b) =>
        TC.Opp[a] == b ? new[] { Side(x, y, a), Side(x, y, b) } : new[] { Side(x, y, a), Center(x, y), Side(x, y, b) };

    /// <summary>Идти от кубика key (въехав через inSide) вперёд до развилки или конца.</summary>
    static Branch Walk(Ed.Station st, string key, string inSide, HashSet<string> seen, List<string> notes)
    {
        var br = new Branch();
        for (int guard = 0; guard < 2000; guard++)
        {
            if (key == null || !st.Tiles.TryGetValue(key, out var t)) break;
            var (x, y) = TC.Parse(key);
            if (!seen.Add(key + "|" + inSide)) { notes.Add($"петля на кубике {key} — обход остановлен"); break; }
            if (t.T == "buffer") { br.Steps.Add(Mk(key, t, new[] { Side(x, y, inSide), Center(x, y) })); break; }
            if (t.T == "exit") { br.Steps.Add(Mk(key, t, new[] { Side(x, y, inSide), Side(x, y, TC.Opp[inSide]) })); break; }
            if (t.T == "switch")
            {
                var v = TC.VariantOf(t);
                if (inSide == v.Trunk)
                {
                    // развилка: до остряка (центр клетки) — эта ветка, дальше две
                    br.Steps.Add(Mk(key, t, new[] { Side(x, y, inSide), Center(x, y) }));
                    br.ForkKey = key;
                    br.Plus = Child(st, key, t, v.Plus, seen, notes);
                    br.Minus = Child(st, key, t, v.Minus, seen, notes);
                    return br;
                }
                // въехали в стрелку с ответвления (схождение) — к остряку
                br.Steps.Add(Mk(key, t, Through(x, y, inSide, v.Trunk)));
                var nb0 = TC.NeighborOf(st, key, v.Trunk);
                if (nb0 == null) break;
                key = nb0.Key; inSide = nb0.InSide;
                continue;
            }
            var outs = TC.Through(t, inSide);
            if (outs.Count == 0) break;
            var o = outs[0].Out;
            br.Steps.Add(Mk(key, t, Through(x, y, inSide, o)));
            var nb = TC.NeighborOf(st, key, o);
            if (nb == null) break;
            key = nb.Key; inSide = nb.InSide;
        }
        return br;
    }

    static Branch Child(Ed.Station st, string key, Ed.Tile t, string side, HashSet<string> seen, List<string> notes)
    {
        var (x, y) = TC.Parse(key);
        var first = Mk(key, t, new[] { Center(x, y), Side(x, y, side) });
        var nb = TC.NeighborOf(st, key, side);
        var b = nb != null ? Walk(st, nb.Key, nb.InSide, seen, notes) : new Branch();
        b.Steps.Insert(0, first);
        return b;
    }

    static Step Mk(string key, Ed.Tile t, PointF[] pts) => new() { Key = key, Tile = t, Pts = pts, Len = PolyLen(pts) };

    /// <summary>Листья сверху вниз: у развилки первой идёт ветка, которая уходит выше.</summary>
    static void Order(Branch b, List<Branch> leaves)
    {
        if (b.IsLeaf) { leaves.Add(b); return; }
        float yP = FirstY(b.Plus), yM = FirstY(b.Minus);
        if (yP <= yM) { Order(b.Plus, leaves); Order(b.Minus, leaves); }
        else { Order(b.Minus, leaves); Order(b.Plus, leaves); }
    }
    static float FirstY(Branch b) => b.Steps.Count > 0 ? b.Steps[0].Pts[^1].Y : 0;

    static void Ranges(Branch b)
    {
        if (b.IsLeaf) { b.Lo = b.Hi = b.Leaf; return; }
        Ranges(b.Plus); Ranges(b.Minus);
        b.Lo = Math.Min(b.Plus.Lo, b.Minus.Lo);
        b.Hi = Math.Max(b.Plus.Hi, b.Minus.Hi);
    }

    static IEnumerable<Branch> All(Branch b)
    {
        yield return b;
        if (b.IsLeaf) yield break;
        foreach (var x in All(b.Plus)) yield return x;
        foreach (var x in All(b.Minus)) yield return x;
    }

    /// <summary>Путь от корня до листа.</summary>
    static List<Branch> PathTo(Branch root, Branch leaf)
    {
        var path = new List<Branch>();
        bool Find(Branch b)
        {
            path.Add(b);
            if (b == leaf) return true;
            if (!b.IsLeaf && (Find(b.Plus) || Find(b.Minus))) return true;
            path.RemoveAt(path.Count - 1);
            return false;
        }
        Find(root);
        return path;
    }

    static bool Horizontal(Step s) => s.Pts.Length == 2 && Math.Abs(s.Pts[0].Y - s.Pts[1].Y) < 0.01 && s.Tile.T is "straight" or "brake" or "crossing" or "buffer" or "exit";

    // =================================================================
    //  ПОСТРОЕНИЕ
    // =================================================================
    public static YardGeo Build(string path, out List<string> notes) => Build(path, out notes, null);

    /// <summary>Длина от горочного светофора до начала сортировочных путей, когда клетка не задана (как в «Сортировочной»: 60 + 186 м).</summary>
    const double AutoSignalToTracks = 246;

    static YardGeo Build(string path, out List<string> notes, double? cellOverride)
    {
        notes = new List<string>();
        var st = Ed.StationFile.Load(path);
        var opt = ReadOptions(path);
        double cellM = cellOverride ?? opt.CellMeters ?? 12;

        // ---- горочный светофор ----
        var humps = st.Tiles.Where(p => p.Value.Sigs != null).SelectMany(p => p.Value.Sigs.Select(s => (p.Key, p.Value, Sig: s))).Where(x => x.Sig.Kind == "hump").ToList();
        if (humps.Count == 0) throw new InvalidDataException("в схеме нет горочного светофора (вид «Горочный») — не видно, где горка");
        if (humps.Count > 1) notes.Add($"горочных светофоров {humps.Count} — взят первый ({humps[0].Sig.Name})");
        var (hk, ht, hs) = humps[0];
        bool right = hs.Dir == "R";
        var ends = TC.EndsOf(ht);
        string front = ends.FirstOrDefault(e => TC.IsRight(e) == right && TC.Opp.ContainsKey(e) && (e == "E" || e == "W")) ?? ends.FirstOrDefault(e => TC.IsRight(e) == right);
        string back = ends.FirstOrDefault(e => TC.IsRight(e) != right);
        if (front == null || back == null) throw new InvalidDataException($"горочный светофор {hs.Name} стоит на кубике, через который не проехать");
        var (hx, hy) = TC.Parse(hk);
        var sigPt = Side(hx, hy, front);

        // ---- вперёд: горка и сортировочные пути ----
        var seen = new HashSet<string>();
        var nbF = TC.NeighborOf(st, hk, front);
        if (nbF == null) throw new InvalidDataException("за горочным светофором нет пути");
        var root = Walk(st, nbF.Key, nbF.InSide, seen, notes);
        var leaves = new List<Branch>();
        Order(root, leaves);
        if (leaves.Count < 2) throw new InvalidDataException("за горочным светофором нет стрелок: сортировочных путей должно быть два и больше");
        for (int i = 0; i < leaves.Count; i++) leaves[i].Leaf = i + 1;
        Ranges(root);
        int N = leaves.Count;

        // ---- назад: путь надвига и парк приёма ----
        var backSteps = new List<Step> { Mk(hk, ht, Through(hx, hy, front, back)) };   // от светофора назад
        var nbB = TC.NeighborOf(st, hk, back);
        Branch broot = nbB != null ? Walk(st, nbB.Key, nbB.InSide, seen, notes) : new Branch();
        broot.Steps.InsertRange(0, backSteps);
        // у первого шага точки — от светофора назад
        var arrLeaves = new List<Branch>();
        Order(broot, arrLeaves);
        for (int i = 0; i < arrLeaves.Count; i++) arrLeaves[i].Leaf = i + 1;
        Ranges(broot);
        int A = arrLeaves.Count;

        // ---- метры вдоль пути вперёд (от светофора) ----
        void SetS(Branch b, double s0)
        {
            b.S0 = s0; b.S1 = s0 + b.Len * cellM;
            if (!b.IsLeaf) { SetS(b.Plus, b.S1); SetS(b.Minus, b.S1); }
        }
        SetS(root, 0);
        // тормозные позиции (группы кубиков подряд с одной подписью) — по веткам
        var rets = new List<(string Name, double S0, double S1, int Lo, int Hi)>();
        foreach (var b in All(root))
        {
            double s = b.S0;
            (string name, double s0, double s1)? cur = null;
            foreach (var step in b.Steps)
            {
                double s1 = s + step.Len * cellM;
                if (step.Tile.T == "brake")
                {
                    string nm = step.Tile.Brk?.Label ?? "ТП";
                    if (cur is { } c && c.name == nm) cur = (nm, c.s0, s1);
                    else { if (cur is { } c2) rets.Add((c2.name, c2.s0, c2.s1, b.Lo, b.Hi)); cur = (nm, s, s1); }
                }
                else if (cur is { } c3) { rets.Add((c3.name, c3.s0, c3.s1, b.Lo, b.Hi)); cur = null; }
                s = s1;
            }
            if (cur is { } c4) rets.Add((c4.name, c4.s0, c4.s1, b.Lo, b.Hi));
        }
        // горб: между светофором и первой тормозной позицией (или первой стрелкой)
        double firstRet = rets.Count > 0 ? rets.Min(r => r.S0) : double.MaxValue;
        double horb = opt.Horb ?? Math.Clamp(Math.Min(firstRet, root.S1) * 0.75, Math.Min(15, root.S1 * 0.5), 60);
        if (horb >= root.S1) horb = root.S1 * 0.6;
        // теперь s — от горба
        foreach (var b in All(root)) { b.S0 -= horb; b.S1 -= horb; }
        rets = rets.Select(r => (r.Name, r.S0 - horb, r.S1 - horb, r.Lo, r.Hi)).ToList();
        double sSignal = -horb;

        // начало сортировочного пути — первый горизонтальный кубик после последней стрелки (не раньше 0,5 клетки)
        var startReal = new double[N + 1];
        var endReal = new double[N + 1];
        foreach (var lf in leaves)
        {
            double s = lf.S0, found = double.NaN;
            for (int i = 0; i < lf.Steps.Count; i++)
            {
                var step = lf.Steps[i];
                if (i > 0 && Horizontal(step) && step.Tile.T != "switch" && s - lf.S0 >= 0.5 * cellM) { found = s; break; }
                s += step.Len * cellM;
            }
            startReal[lf.Leaf] = double.IsNaN(found) ? lf.S0 + Math.Min(lf.Len * cellM * 0.3, cellM) : found;
            endReal[lf.Leaf] = lf.S1;
        }
        double sTrack = Math.Ceiling(Enumerable.Range(1, N).Max(k => startReal[k]));
        // клетка не задана — подобрать так, чтобы от светофора до путей было, как в «Сортировочной» (рисунок бывает любой длины)
        if (cellOverride == null && opt.CellMeters == null && sTrack - sSignal > 1)
            return Build(path, out notes, Math.Clamp(cellM * AutoSignalToTracks / (sTrack - sSignal), 3, 50));
        double trackLen = opt.TrackLength;
        double sEnd = sTrack + trackLen;

        // ---- метры назад (парк приёма): от светофора к хвостам ----
        void SetB(Branch b, double d0)
        {
            b.S0 = d0; b.S1 = d0 + b.Len * cellM;
            if (!b.IsLeaf) { SetB(b.Plus, b.S1); SetB(b.Minus, b.S1); }
        }
        SetB(broot, 0);
        // голова поезда в парке — первый горизонтальный кубик пути после последней стрелки
        var frontReal = new double[A + 1];
        foreach (var lf in arrLeaves)
        {
            double d = lf.S0, found = double.NaN;
            for (int i = 0; i < lf.Steps.Count; i++)
            {
                var step = lf.Steps[i];
                if ((i > 0 || lf == broot) && Horizontal(step) && step.Tile.T != "switch" && (lf == broot || d - lf.S0 >= 0.5 * cellM)) { found = d; break; }
                d += step.Len * cellM;
            }
            frontReal[lf.Leaf] = double.IsNaN(found) ? lf.S0 : found;
        }
        double dFront = Math.Ceiling(Enumerable.Range(1, A).Max(a => frontReal[a])) + 5;   // от светофора назад, м
        double sArrFront = sSignal - dFront;
        double sArrTail = sArrFront - opt.ArrLength;
        double sPush = broot.IsLeaf ? sArrFront : sSignal - broot.S1;

        // ---- геометрия в клетках: ломаные с отметками s ----
        var treeCells = new (double s, PointF p)[N + 1][];
        foreach (var lf in leaves)
        {
            var list = new List<(double, PointF)>();
            foreach (var b in PathTo(root, lf))
            {
                double s = b.S0;
                foreach (var step in b.Steps)
                {
                    double ss = s;
                    for (int i = 0; i < step.Pts.Length; i++)
                    {
                        if (i > 0) ss += Dist(step.Pts[i - 1], step.Pts[i]) * cellM;
                        if (list.Count == 0 || (i > 0)) list.Add((ss, step.Pts[i]));
                    }
                    s += step.Len * cellM;
                }
            }
            // растянуть: от последней стрелки до начала пути — к общему sTrack, сам путь — на trackLen
            double s0 = lf.S0, a = startReal[lf.Leaf], e = Math.Max(endReal[lf.Leaf], a + 1);
            double Map(double s) => s <= s0 ? s : s <= a ? s0 + (s - s0) * (sTrack - s0) / Math.Max(1e-6, a - s0) : sTrack + (s - a) * trackLen / (e - a);
            treeCells[lf.Leaf] = Dedup(list.Select(p => (Map(p.Item1), p.Item2)));
        }
        var pushCells = new (double s, PointF p)[A + 1][];
        // вперёд от светофора до горба — общий кусок пути надвига (по первой ветке горки)
        var fwd = new List<(double, PointF)> { (sSignal, sigPt) };
        {
            double s = root.S0;
            foreach (var step in root.Steps)
            {
                double ss = s;
                for (int i = 1; i < step.Pts.Length; i++) { ss += Dist(step.Pts[i - 1], step.Pts[i]) * cellM; fwd.Add((ss, step.Pts[i])); }
                s += step.Len * cellM;
            }
        }
        foreach (var lf in arrLeaves)
        {
            var list = new List<(double d, PointF p)>();
            foreach (var b in PathTo(broot, lf))
            {
                double d = b.S0;
                foreach (var step in b.Steps)
                {
                    double dd = d;
                    for (int i = 0; i < step.Pts.Length; i++)
                    {
                        if (i > 0) dd += Dist(step.Pts[i - 1], step.Pts[i]) * cellM;
                        if (list.Count == 0 || i > 0) list.Add((dd, step.Pts[i]));
                    }
                    d += step.Len * cellM;
                }
            }
            double d0 = lf == broot ? 0 : lf.S0, fr = frontReal[lf.Leaf], tail = Math.Max(list[^1].d, fr + 1);
            double MapD(double d) => d <= d0 ? d : d <= fr ? d0 + (d - d0) * (dFront - d0) / Math.Max(1e-6, fr - d0) : dFront + (d - fr) * opt.ArrLength / (tail - fr);
            var pts = list.Select(p => (sSignal - MapD(p.d), p.p)).Reverse().ToList();
            pts.Insert(0, (sArrTail - 40, pts[0].p));
            pts.AddRange(fwd.Where(f => f.Item1 > sSignal + 1e-6 && f.Item1 < 0));
            pts.Add((0, InterpPts(fwd, 0)));
            pushCells[lf.Leaf] = Dedup(pts);
        }
        // у первого (корневого) участка горки — с горба
        for (int k = 1; k <= N; k++)
        {
            var l = treeCells[k].Where(p => p.s > 0).ToList();
            l.Insert(0, (0, InterpPts(treeCells[k].ToList(), 0)));
            treeCells[k] = Dedup(l);
        }

        // ---- в условные точки окна (1600 × 448, справа — парк отправления, снизу — обходной путь и депо) ----
        var g = new YardFromJsonGeo();
        var allPts = treeCells.Skip(1).SelectMany(x => x.Select(p => p.p)).Concat(pushCells.Skip(1).SelectMany(x => x.Select(p => p.p))).ToList();
        var decorCells = DecorTiles(st, seen, hk);
        allPts.AddRange(decorCells.SelectMany(x => x));
        float minX = allPts.Min(p => p.X), maxX = allPts.Max(p => p.X), minY = allPts.Min(p => p.Y), maxY = allPts.Max(p => p.Y);
        // по x — по участкам, как в «Сортировочной»: парк приёма и путь надвига — до 470, горка — до 1000, пути — до 1380
        // (нарисованы они бывают какой угодно длины: длина в метрах всё равно своя); по y — один масштаб
        float U(float x) => right ? x : -x;                        // роспуск влево — зеркально: горка всегда слева направо
        float uA = allPts.Min(p => U(p.X)), uE = allPts.Max(p => U(p.X)), uS = U(sigPt.X);
        float uT = Enumerable.Range(1, N).Min(k => U(InterpPts(treeCells[k].ToList(), sTrack).X));
        float uF = Enumerable.Range(1, A).Min(a => U(InterpPts(pushCells[a].ToList(), sArrFront).X));   // головы путей парка приёма
        uS = Math.Clamp(uS, uA + 1, uE - 1); uT = Math.Clamp(uT, uS + 0.5f, uE - 0.5f); uF = Math.Clamp(uF, uA + 0.25f, uS - 0.5f);
        float[] ku = { uA, uF, uS, uT, uE }, kx = { 30, 290, 470, 1000, 1380 };
        float X(float u)
        {
            for (int q = 1; q < ku.Length; q++)
                if (u <= ku[q] || q == ku.Length - 1) return kx[q - 1] + (u - ku[q - 1]) * (kx[q] - kx[q - 1]) / Math.Max(1e-3f, ku[q] - ku[q - 1]);
            return kx[^1];
        }
        const float T0 = 40, B0 = 330;
        float sy = Math.Min((B0 - T0) / Math.Max(1, maxY - minY), 34);
        float offY = T0 + ((B0 - T0) - (maxY - minY) * sy) / 2;
        PointF S(PointF p) => new(X(U(p.X)), offY + (p.Y - minY) * sy);
        (double, PointF)[] SL((double s, PointF p)[] a) => a.Select(x => (x.s, S(x.p))).ToArray();

        g.Title = string.IsNullOrWhiteSpace(st.Name) ? Path.GetFileNameWithoutExtension(path).Replace(".station", "") : st.Name.Trim();
        g.Tracks = N; g.ArrTracks = A; g.DepTracks = opt.DepTracks;
        g.STrack = sTrack; g.SEnd = sEnd; g.SSignal = sSignal; g.SArrFront = sArrFront; g.SArrTail = sArrTail; g.SPushTrack = Math.Min(sPush, sSignal - 10);
        g.LocoLimit = -3; g.SUncouple = sSignal * 0.58;
        g.Tree = new[] { ((double, PointF)[])null }.Concat(Enumerable.Range(1, N).Select(k => SL(treeCells[k]))).ToArray();
        g.Push = new[] { ((double, PointF)[])null }.Concat(Enumerable.Range(1, A).Select(a => SL(pushCells[a]))).ToArray();

        // ---- рельсовые цепи и стрелки ----
        int swNo = 0;
        var forks = All(root).Where(b => !b.IsLeaf).OrderBy(b => b.S1).ThenBy(b => b.Lo).ToList();
        var swEdge = new Dictionary<Branch, (double a, double b)>();
        foreach (var f in forks)
        {
            double half = 0.5 * cellM;
            double a = Math.Max(f.S1 - half, f.S0 + (f.S1 - f.S0) * 0.5);
            double b = Math.Min(f.S1 + half, Math.Min(f.Plus.S0 + (Math.Min(f.Plus.S1, f.Plus.IsLeaf ? sTrack : f.Plus.S1) - f.Plus.S0) * 0.5,
                                                      f.Minus.S0 + (Math.Min(f.Minus.S1, f.Minus.IsLeaf ? sTrack : f.Minus.S1) - f.Minus.S0) * 0.5));
            swEdge[f] = (Math.Max(a, 0.5), Math.Max(b, f.S1 + 1));
            swNo++;
            var name = Sec(f.Steps[^1].Tile) ?? $"{swNo}СП";
            g.EdgeDefs.Add(new EdgeDef
            {
                Id = name, S0 = swEdge[f].a, S1 = swEdge[f].b, Lo = f.Lo, Hi = f.Hi, SwNo = swNo, SwS = f.S1,
                PlusLo = f.Plus.Lo, PlusHi = f.Plus.Hi, MinusLo = f.Minus.Lo, MinusHi = f.Minus.Hi,
            });
        }
        foreach (var b in All(root))
        {
            double s0 = b == root ? 0 : b.S0, s1 = b.IsLeaf ? sTrack : b.S1;
            // родитель — развилка: начало после её стрелочной цепи
            var parent = forks.FirstOrDefault(f => f.Plus == b || f.Minus == b);
            if (parent != null) s0 = swEdge[parent].b;
            if (!b.IsLeaf) s1 = swEdge[b].a;
            if (s1 - s0 > 0.5)
                g.EdgeDefs.Add(new EdgeDef { Id = Sec(b.Steps.FirstOrDefault(x => x.Tile.T != "switch")?.Tile) ?? (b.Lo == b.Hi ? $"{b.Lo}П-гор" : $"{b.Lo}-{b.Hi}"), S0 = s0, S1 = s1, Lo = b.Lo, Hi = b.Hi });
        }
        for (int k = 1; k <= N; k++) g.EdgeDefs.Add(new EdgeDef { Id = k + "П", S0 = sTrack, S1 = sEnd, Lo = k, Hi = k });
        // ---- замедлители ----
        foreach (var grp in rets.GroupBy(r => r.Name))
        {
            var list = grp.OrderBy(r => r.Lo).ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                string nm = list.Count == 1 ? r.Name : r.Name + "-" + "АБВГДЕЖЗИКЛМНОПРСТ"[Math.Min(i, 17)];
                // кубик замедлителя после начала пути — замедлитель на пути (ТП3): держим в пределах пути
                double s0 = r.S0, s1 = Math.Max(r.S1, r.S0 + 5);
                if (r.Lo == r.Hi && s0 >= startReal[r.Lo] - 0.1)
                {
                    double a = startReal[r.Lo], e = Math.Max(endReal[r.Lo], a + 1);
                    s0 = sTrack + (s0 - a) * trackLen / (e - a); s1 = sTrack + (s1 - a) * trackLen / (e - a);
                }
                g.RetDefs.Add(new RetDef { Name = nm, S0 = s0, S1 = s1, Lo = r.Lo, Hi = r.Hi });
            }
        }
        g.RetDefs.Sort((a, b) => a.S0 != b.S0 ? a.S0.CompareTo(b.S0) : a.Lo.CompareTo(b.Lo));
        if (g.RetDefs.Count == 0) notes.Add("в схеме нет тормозных позиций — отцепы не тормозятся (АРС не работает)");
        else
        {
            // у части путей две тормозные позиции, у части — одна: на этих АРС тормозит отцеп сразу под вагоны, он идёт
            // медленно, и следующий его нагоняет в стрелочной зоне
            int Cnt(int k) => g.RetDefs.Count(r => r.Lo <= k && k <= r.Hi && r.S1 < sTrack);
            int most = Enumerable.Range(1, N).Max(Cnt);
            var few = Enumerable.Range(1, N).Where(k => Cnt(k) < most).ToList();
            if (most >= 2 && few.Count > 0)
                notes.Add($"у путей {string.Join(", ", few)} нет второй тормозной позиции — отцепы на них пойдут медленно, следующие будут их нагонять");
        }

        // ---- профиль ----
        var first = g.RetDefs.Where(r => r.Lo == 1 && r.Hi == N && r.S1 < sTrack).ToList();
        var second = g.RetDefs.Where(r => !(r.Lo == 1 && r.Hi == N) && r.S1 < sTrack).ToList();
        void G(double s1, double gr) { if (g.Grades.Count == 0 || s1 > g.Grades[^1].S1 + 0.1) g.Grades.Add((s1, gr)); }
        G(sSignal, 0);
        G(0, -10);
        if (first.Count > 0)
        {
            G(first.Min(r => r.S0), 45);
            G(first.Max(r => r.S1), 14);
            if (second.Count > 0) { G(second.Min(r => r.S0), 8); G(second.Max(r => r.S1), 9); }
        }
        else if (second.Count > 0) { G(second.Min(r => r.S0), 45); G(second.Max(r => r.S1), 12); }
        else G(Math.Min(30, sTrack / 3), 45);
        G(sTrack, 1.5);
        G(sEnd - 60, 0.6);
        g.LastGrade = -2.5;
        foreach (var e in g.EdgeDefs.Where(e => e.SwNo > 0).OrderBy(e => e.S0))
        {
            if (g.SwitchZones.Count > 0 && e.S0 <= g.SwitchZones[^1].S1 + 0.1) g.SwitchZones[^1] = (g.SwitchZones[^1].S0, Math.Max(g.SwitchZones[^1].S1, e.S1));
            else g.SwitchZones.Add((e.S0, e.S1));
        }
        g.ProfileFrom = sSignal - 90;
        g.ProfileTo = sTrack + 74;
        foreach (var grp in g.EdgeDefs.Where(e => e.SwNo > 0).GroupBy(e => Math.Round(e.SwS / 4)).OrderBy(x => x.Key))
        {
            var nos = grp.Select(e => e.SwNo).OrderBy(x => x).ToList();
            string txt = nos.Count == 1 ? $"стр. {nos[0]}" : nos.Count == 2 ? $"стр. {nos[0]}, {nos[1]}" : $"стр. {nos[0]}–{nos[^1]}";
            g.ProfileMarks.Add((grp.Average(e => e.SwS), txt, 0));
        }
        g.ProfileMarks.Add((sTrack, N == 1 ? "путь 1" : $"пути 1–{N}", 14));

        // ---- назначения путей ----
        g.TrackNames = new[] { "" }.Concat(Enumerable.Range(1, N).Select(k => k - 1 < opt.TrackNames.Count && !string.IsNullOrWhiteSpace(opt.TrackNames[k - 1]) ? opt.TrackNames[k - 1].Trim()
                                                                             : k - 1 < DefaultNames.Length ? DefaultNames[k - 1] : $"Направление {k}")).ToArray();
        g.Norms = new[] { 0 }.Concat(Enumerable.Range(1, N).Select(k => k - 1 < opt.Norms.Count && opt.Norms[k - 1] > 0 ? opt.Norms[k - 1] : YardSim.NormMain)).ToArray();

        // ---- что вокруг горки ----
        g.Decor.AddRange(decorCells.Select(d => d.Select(S).ToArray()));
        g.ArrBody = new PointF[A + 1][]; g.ArrLead = new PointF[A + 1][];
        g.ArrLabel = new PointF[A + 1]; g.ShuntSig = new PointF[A + 1];
        for (int a = 1; a <= A; a++)
        {
            g.ArrBody[a] = g.Line(sArrTail, sArrFront, 1, a);
            g.ArrLead[a] = g.Line(sArrFront, g.SPushTrack, 1, a);
            var t = g.At(sArrTail, 1, a);
            g.ArrLabel[a] = new PointF(t.X + 2, t.Y - 19);
            var m = g.At(sArrFront + 6, 1, a);
            g.ShuntSig[a] = new PointF(m.X, m.Y - 9);
        }
        g.PushTrack = g.Line(g.SPushTrack, 0, 1, 1);
        g.TrackLabel = new PointF[N + 1];
        for (int k = 1; k <= N; k++) { var p = g.At(sTrack, k, 1); g.TrackLabel[k] = new PointF(p.X + 4, p.Y - 19); }
        // парк отправления — справа, по высоте сортировочного парка
        float yTop = Enumerable.Range(1, N).Min(k => g.At(sEnd, k, 1).Y), yBot = Enumerable.Range(1, N).Max(k => g.At(sEnd, k, 1).Y);
        float xEnd = Enumerable.Range(1, N).Max(k => g.At(sEnd, k, 1).X);
        g.DepX0 = Math.Min(Math.Max(xEnd + 40, 1400), 1430); g.DepX1 = g.DepX0 + 158;
        int D = g.DepTracks;
        float span = Math.Max(yBot - yTop, 60 * (D - 1));
        float y0 = (yTop + yBot) / 2 - span / 2;
        g.DepYs = new[] { 0f }.Concat(Enumerable.Range(1, D).Select(d => D == 1 ? (yTop + yBot) / 2 : y0 + span * (d - 1) / (D - 1))).ToArray();
        g.DepLabel = Enumerable.Range(0, D + 1).Select(d => d == 0 ? default : new PointF(g.DepX0 + 2, g.DepYs[d] - 19)).ToArray();
        // обходной путь и депо — под парком приёма, стоянка горочного локомотива — под путём надвига
        float arrBottom = Enumerable.Range(1, A).Max(a => g.At(sArrTail, 1, a).Y);
        float pushY = g.SignalPoint.Y;
        g.BypassY = Math.Min(Math.Max(arrBottom, pushY) + 46, 380);
        var arrTailX = Enumerable.Range(1, A).Min(a => g.At(sArrTail, 1, a).X);
        g.Depot = new RectangleF(Math.Max(10, arrTailX), g.BypassY + 30, 180, 40);
        g.DepotGate = new PointF(g.Depot.Right, g.Depot.Y + 18);
        g.HumpLocoPark = new PointF(g.SignalPoint.X - 10, Math.Min(pushY + 40, g.BypassY - 14));
        var front1 = g.At(sArrFront, 1, 1);
        g.Repeater = A > 0 ? new PointF(front1.X - 50, Enumerable.Range(1, A).Min(a => g.At(sArrFront, 1, a).Y) - 22) : null;
        g.ArrScale = (float)(Mover.Dist(g.At(sArrTail, 1, 1), g.At(sArrFront, 1, 1)) / opt.ArrLength);
        g.SceneScale = Math.Max(0.4f, (float)(Mover.Dist(g.At(sTrack, 1, 1), g.At(sEnd, 1, 1)) / trackLen));
        // подписи парков и надписи схемы
        var arrMid = g.At((sArrTail + sArrFront) / 2, 1, 1);
        g.Labels.Add(("Парк приёма", new PointF(arrMid.X, Enumerable.Range(1, A).Min(a => g.At(sArrTail, 1, a).Y) - 46), true));
        var hp = g.At(0, 1, 1);
        g.Labels.Add(("Горка", new PointF(hp.X, hp.Y - 44), true));
        var trMid = g.At((sTrack + sEnd) / 2, 1, 1);
        g.Labels.Add(("Сортировочный парк", new PointF(trMid.X, Math.Max(4, yTop - 42)), true));
        g.Labels.Add(("Парк отправления", new PointF((g.DepX0 + g.DepX1) / 2, Math.Max(4, g.DepYs[1] - 40)), true));
        foreach (var (k, tx) in st.Texts)
        {
            if (string.IsNullOrWhiteSpace(tx.Text) || tx.Text.Trim() == g.Title) continue;
            var (x, y) = TC.Parse(k);
            g.Labels.Add((tx.Text.Trim(), S(new PointF(x + 0.5f, y + 0.5f)), false));
        }

        g.Finish();
        notes.Add($"горка из файла: сортировочных путей {N}, путей парка приёма {A}, стрелок {swNo}, тормозных позиций {g.RetDefs.Count}, клетка {cellM:0.#} м");
        return g;
    }

    static readonly string[] DefaultNames = { "Северная", "Восточная", "Южная", "Западная", "Заводская", "Портовая", "Сборный (местные)", "Ремонт (ВЧД)" };

    static string Sec(Ed.Tile t) => t != null && !string.IsNullOrWhiteSpace(t.Sec) ? t.Sec.Trim() : null;

    static (double s, PointF p)[] Dedup(IEnumerable<(double s, PointF p)> pts)
    {
        var res = new List<(double s, PointF p)>();
        foreach (var x in pts.OrderBy(p => p.s))
        {
            if (res.Count > 0 && Math.Abs(res[^1].s - x.s) < 1e-6) { res[^1] = x; continue; }
            res.Add(x);
        }
        return res.ToArray();
    }

    static PointF InterpPts(List<(double s, PointF p)> pts, double s)
    {
        pts = pts.OrderBy(p => p.s).ToList();
        if (s <= pts[0].s) return pts[0].p;
        for (int i = 1; i < pts.Count; i++)
            if (s <= pts[i].s)
            {
                double ds = pts[i].s - pts[i - 1].s;
                float t = ds <= 0 ? 1 : (float)((s - pts[i - 1].s) / ds);
                return new(pts[i - 1].p.X + (pts[i].p.X - pts[i - 1].p.X) * t, pts[i - 1].p.Y + (pts[i].p.Y - pts[i - 1].p.Y) * t);
            }
        return pts[^1].p;
    }

    /// <summary>Кубики, которые не вошли в горку и парк приёма, — серые линии (депо, вытяжки, обходные…).</summary>
    static List<PointF[]> DecorTiles(Ed.Station st, HashSet<string> seen, string humpKey)
    {
        var used = new HashSet<string>(seen.Select(s => s.Split('|')[0])) { humpKey };
        var res = new List<PointF[]>();
        foreach (var (k, t) in st.Tiles)
        {
            if (used.Contains(k)) continue;
            var (x, y) = TC.Parse(k);
            foreach (var e in TC.EdgesOf(t))
            {
                if (e.B == null) { res.Add(new[] { Side(x, y, e.A), Center(x, y) }); continue; }
                res.Add(Through(x, y, e.A, e.B));
            }
        }
        return res;
    }

    // ---------------- необязательный раздел "yard" ----------------
    sealed class Options
    {
        public double? CellMeters;
        public double TrackLength = 314, ArrLength = 300;
        public double? Horb;
        public int DepTracks = 4;
        public List<string> TrackNames = new();
        public List<int> Norms = new();
    }

    static Options ReadOptions(string path)
    {
        var o = new Options();
        try
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new System.Text.Json.JsonDocumentOptions
            { CommentHandling = System.Text.Json.JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (root?["yard"] is not JsonObject y) return o;
            double D(string k, double def, double lo, double hi) => y[k] is JsonValue v && v.TryGetValue(out double d) ? Math.Clamp(d, lo, hi) : def;
            if (y["cellMeters"] is JsonValue cv && cv.TryGetValue(out double cm)) o.CellMeters = Math.Clamp(cm, 3, 50);
            o.TrackLength = D("trackLength", o.TrackLength, 150, 1200);
            o.ArrLength = D("arrLength", o.ArrLength, 150, 1200);
            if (y["horb"] is JsonValue hv && hv.TryGetValue(out double h)) o.Horb = Math.Clamp(h, 5, 150);
            o.DepTracks = (int)D("depTracks", o.DepTracks, 1, 8);
            if (y["trackNames"] is JsonArray names) o.TrackNames = names.Select(n => n?.ToString() ?? "").ToList();
            if (y["norms"] is JsonArray norms) o.Norms = norms.Select(n => n is JsonValue v && v.TryGetValue(out int i) ? Math.Clamp(i, 1, 60) : 0).ToList();
        }
        catch { }
        return o;
    }
}

/// <summary>Горка из файла: в остальном — как YardGeo (сцены, парк отправления, депо — по умолчанию).</summary>
public sealed class YardFromJsonGeo : YardGeo { }
