using System.Text.RegularExpressions;

namespace StationEditor.Model;

// =====================================================================
//  НУМЕРАЦИЯ ПУТЕЙ И СТРЕЛОК ПО ПРАВИЛАМ  [ДОБАВЛЕНО 11.11.2026]
//
//  Источники: ВСН 56-78 «Инструкция по проектированию станций и узлов», приложение 2 «Правила нумерации путей
//  и стрелок»; РУ-30-80 «Указания по применению светофорной сигнализации», листы 89 (светофоры на станции)
//  и 90 (на перегоне). Файл не зависит от окон редактора — его можно перенести в другой редактор целиком.
//
//  1. НАПРАВЛЕНИЕ. Чётное / нечётное — направление движения поездов (задаёт дорога). В схеме — Station.Even:
//     "R" — чётные поезда идут вправо (→), "L" — влево (←).
//  2. ГЛАВНЫЕ ПУТИ — римскими цифрами: I — нечётный, II — чётный. Главный — путь, по которому прямо (стрелки
//     в плюсе) можно выехать за обе горловины на перегон. Однопутная линия — один главный I.
//     Движение правостороннее: нечётный главный — справа по ходу нечётного поезда (нечётные идут ← — значит
//     I выше II; идут → — I ниже), как на листе 89.
//  3. БОКОВЫЕ (приёмо-отправочные) — арабскими, со следующего номера после главных:
//       "parity" — со стороны чётного главного чётные (4, 6, 8…; на однопутке 2, 4…), со стороны нечётного —
//                  нечётные (3, 5, 7…), от главного пути наружу;
//       "row"    — подряд от пассажирского здания в сторону поля (3, 4, 5…; на однопутке 2, 3, 4…) —
//                  для малых станций, где пути принимают поезда обоих направлений.
//     Участок пути называется «<номер>П»: IП, IIП, 3П, 4П…
//  4. ОСЬ СТАНЦИИ — граница чётной и нечётной сторон: ось пассажирского здания (объект «Вокзал»), а если его
//     нет — поперечная ось в центре путевого развития (посередине между крайними стрелками).
//  5. СТРЕЛКИ — со стороны прибытия чётных поездов чётные (2, 4, 6…), нечётных — нечётные (1, 3, 5…),
//     от входных стрелок к оси станции; стрелки съезда — подряд (6/8).
//  Светофоры этот файл не трогает (откат 11.11.2026 по просьбе Мади) — имена светофоров как раньше (Core.SuggestSignalName).
// =====================================================================

public static class Numbering
{
    static readonly Regex RePathNum = new(@"^(I|II|III|IV|V|VI|\d+)П$", RegexOptions.Compiled);
    static readonly string[] Roman = { "", "I", "II", "III", "IV", "V", "VI" };

    public static bool EvenRight(Station st) => st.Even != "L";
    public static bool IsEven(Station st, string dir) => dir == (EvenRight(st) ? "R" : "L");
    public static string Letter(Station st, string dir) => IsEven(st, dir) ? "Ч" : "Н";

    // =================================================================
    //  ОСЬ СТАНЦИИ
    // =================================================================
    /// <summary>X оси станции (в клетках; центр клетки x — это x + 0.5).</summary>
    public static float AxisX(Station st)
    {
        var b = st.Objects.FirstOrDefault(o => o.Value.Type == "building");
        if (b.Key != null) { var (bx, _) = Core.Parse(b.Key); return bx + b.Value.W / 2f; }
        var xs = st.Tiles.Where(p => p.Value.T == "switch").Select(p => Core.Parse(p.Key).X).ToList();
        if (xs.Count == 0) xs = st.Tiles.Keys.Select(k => Core.Parse(k).X).ToList();
        if (xs.Count == 0) return 0;
        return (xs.Min() + xs.Max()) / 2f + 0.5f;
    }

    public static string AxisSource(Station st)
    {
        var b = st.Objects.Values.FirstOrDefault(o => o.Type == "building");
        if (b != null) return $"ось пассажирского здания{(string.IsNullOrWhiteSpace(b.Name) ? "" : " «" + b.Name + "»")}";
        return st.Tiles.Values.Any(t => t.T == "switch") ? "середина путевого развития (между крайними стрелками)" : "середина схемы";
    }

    static bool LeftOfAxis(string key, float axis) => Core.Parse(key).X + 0.5f < axis;
    /// <summary>Клетка — на стороне прибытия чётных поездов (чётные идут → — значит прибывают слева).</summary>
    public static bool EvenSide(Station st, string key, float axis) => LeftOfAxis(key, axis) == EvenRight(st);
    /// <summary>Порядок «от входа к оси»: слева — по возрастанию x, справа — по убыванию.</summary>
    static double FromEntrance(string key, float axis) { var c = Core.Parse(key).X + 0.5; return c < axis ? c : -c; }

    // =================================================================
    //  ПУТИ
    // =================================================================
    public sealed class PathInfo
    {
        public string Name;
        public double Y, X1, X2;
        public bool Main;
    }

    public static List<PathInfo> Paths(Station st)
    {
        var res = new List<PathInfo>();
        foreach (var (n, keys) in Core.TilesBySection(st))
        {
            if (Core.SecType(st, n) != "path") continue;
            var pts = keys.Select(Core.Parse).ToList();
            var y = pts.GroupBy(p => p.Y).OrderByDescending(g => g.Count()).First().Key;   // строка, где путь горизонтален
            res.Add(new PathInfo { Name = n, Y = y, X1 = pts.Min(p => p.X), X2 = pts.Max(p => p.X), Main = keys.Any(k => Reaches(st, k, true)) && keys.Any(k => Reaches(st, k, false)) });
        }
        return res;
    }

    /// <summary>Прямо (стрелки в плюсе) с этой клетки можно выехать за край схемы или на перегон.</summary>
    static bool Reaches(Station st, string start, bool east)
    {
        foreach (var side in Core.EndsOf(st.Tiles[start]).Where(s => Core.IsRight(s) == east))
        {
            string key = start, outSide = side;
            for (int g = 0; g < 2000; g++)
            {
                var nb = Core.NeighborOf(st, key, outSide);
                if (nb == null) break;
                if (nb.Tile.T == "exit" || Core.SecType(st, nb.Tile.Sec) == "line") return true;
                if (nb.Tile.T == "buffer") break;
                var outs = Core.Through(nb.Tile, nb.InSide).Where(o => o.Need != false).ToList();
                if (outs.Count == 0) break;
                key = nb.Key;
                outSide = (outs.FirstOrDefault(o => o.Out is "E" or "W").Out) ?? outs[0].Out;
            }
        }
        return false;
    }

    /// <summary>Номер пути по имени участка: «IП» → 1, «12П» → 12; не путь — 0.</summary>
    public static int TrackNumber(string sec)
    {
        var m = RePathNum.Match(sec ?? "");
        if (!m.Success) return 0;
        var v = m.Groups[1].Value;
        int r = Array.IndexOf(Roman, v);
        return r > 0 ? r : int.TryParse(v, out int n) ? n : 0;
    }

    /// <summary>Новые имена участков-путей: старое имя → новое (IП, IIП, 3П…).</summary>
    public static Dictionary<string, string> PathNames(Station st)
    {
        var ps = Paths(st);
        var res = new Dictionary<string, string>();
        if (ps.Count == 0) return res;
        var mains = ps.Where(p => p.Main).OrderBy(p => p.Y).ThenBy(p => p.X1).ToList();
        if (mains.Count == 0) mains = new() { ps.OrderByDescending(p => p.X2 - p.X1).First() };   // нет выхода — главный самый длинный
        bool evenR = EvenRight(st);
        PathInfo oddMain, evenMain = null;
        // главные — на разных строках; на одной строке (путь, разрезанный светофором) — первый
        var rows = mains.GroupBy(p => p.Y).Select(g => g.First()).ToList();
        if (rows.Count == 1) oddMain = rows[0];
        else
        {
            // нечётный главный — справа по ходу нечётного поезда: нечётные идут ← (чётные →) — справа от них верх схемы
            oddMain = evenR ? rows.First() : rows.Last();
            evenMain = evenR ? rows.Last() : rows.First();
        }
        res[oddMain.Name] = "IП";
        if (evenMain != null) res[evenMain.Name] = "IIП";
        var rest = ps.Where(p => p != oddMain && p != evenMain).ToList();
        double mid = evenMain == null ? oddMain.Y : (oddMain.Y + evenMain.Y) / 2;
        // нечётная сторона: двухпутка — за нечётным главным; однопутка — справа по ходу нечётного поезда
        int oddSign = evenMain != null ? Math.Sign(oddMain.Y - evenMain.Y) : (evenR ? -1 : 1);

        if (st.Numbering == "row")
        {
            int n = evenMain != null ? 3 : 2;
            var b = st.Objects.FirstOrDefault(o => o.Value.Type == "building");
            double by = b.Key != null ? Core.Parse(b.Key).Y + b.Value.H / 2.0 : double.NaN;
            var order = double.IsNaN(by) ? rest.OrderBy(p => Math.Abs(p.Y - mid)).ThenBy(p => p.Y).ThenBy(p => p.X1)
                                         : rest.OrderBy(p => Math.Abs(p.Y - by)).ThenBy(p => p.X1);
            foreach (var p in order) res[p.Name] = $"{n++}П";
        }
        else
        {
            int nOdd = 3, nEven = evenMain != null ? 4 : 2;
            bool OddSide(PathInfo p)
            {
                int s = Math.Sign(p.Y - mid);
                return s == 0 ? true : s == oddSign;    // на одной строке с главным однопутки — к нечётной стороне
            }
            foreach (var p in rest.Where(OddSide).OrderBy(p => Math.Abs(p.Y - mid)).ThenBy(p => p.X1)) { res[p.Name] = $"{nOdd}П"; nOdd += 2; }
            foreach (var p in rest.Where(p => !OddSide(p)).OrderBy(p => Math.Abs(p.Y - mid)).ThenBy(p => p.X1)) { res[p.Name] = $"{nEven}П"; nEven += 2; }
        }
        return res;
    }

    /// <summary>Переименовать участки (клетки и описание участков). Возвращает, что не удалось (имя занято другим участком).</summary>
    public static List<string> RenameSections(Station st, Dictionary<string, string> map)
    {
        var skipped = new List<string>();
        var others = Core.SectionNames(st).Where(n => !map.ContainsKey(n)).ToHashSet();
        var real = new Dictionary<string, string>();
        foreach (var (o, n) in map)
        {
            if (o == n) continue;
            if (others.Contains(n)) { skipped.Add($"{o} → {n}: имя занято другим участком"); continue; }
            real[o] = n;
        }
        if (real.Count == 0) return skipped;
        foreach (var t in st.Tiles.Values)
            if (t.Sec != null && real.TryGetValue(t.Sec, out var nn)) t.Sec = nn;
        var infos = real.Keys.ToDictionary(o => o, o => st.Sections.TryGetValue(o, out var i) ? i : null);
        foreach (var o in real.Keys) st.Sections.Remove(o);
        foreach (var (o, n) in real)
        {
            var i = infos[o] ?? new SectionInfo();
            i.Type = "path";
            st.Sections[n] = i;
        }
        return skipped;
    }

    // =================================================================
    //  СТРЕЛКИ
    // =================================================================
    public static void RenumberSwitches(Station st)
    {
        float axis = AxisX(st);
        bool evenR = EvenRight(st);
        var groups = st.Tiles.Where(p => p.Value.T == "switch").GroupBy(p => Core.SwitchGroup(p.Key, p.Value))
            .Select(g => g.Select(p => p.Key).OrderBy(k => FromEntrance(k, axis)).ThenBy(k => Core.Parse(k).Y).ToList()).ToList();
        foreach (var left in new[] { true, false })
        {
            bool even = left == evenR;
            int n = even ? 2 : 1;
            var gs = groups.Where(g => LeftOfAxis(g[0], axis) == left)
                           .OrderBy(g => FromEntrance(g[0], axis)).ThenBy(g => Core.Parse(g[0]).Y);
            foreach (var g in gs)
            {
                var nums = new List<string>();
                foreach (var _ in g) { nums.Add(n.ToString()); n += 2; }
                for (int i = 0; i < g.Count; i++)
                {
                    var t = st.Tiles[g[i]];
                    t.Name = nums[i];
                    t.Group = g.Count > 1 ? string.Join("/", nums) : null;
                }
            }
        }
    }

    /// <summary>Номер для новой стрелки: следующий свободный нужной чётности для её стороны станции.</summary>
    public static string NextSwitchNumber(Station st, string key)
    {
        float axis = AxisX(st);
        bool even = EvenSide(st, key, axis);
        var used = st.Tiles.Where(p => p.Key != key && p.Value.T == "switch").Select(p => p.Value.Name).ToHashSet();
        for (int n = even ? 2 : 1; ; n += 2) if (!used.Contains(n.ToString())) return n.ToString();
    }

    /// <summary>Номер для только что нарисованного пути (участок temp): по правилам, а если номер занят — следующий свободный.</summary>
    public static string NameNewPath(Station st, string temp)
    {
        var map = PathNames(st);
        if (!map.TryGetValue(temp, out var want)) return null;
        var taken = Core.SectionNames(st).Where(n => n != temp).ToHashSet();
        if (!taken.Contains(want)) return want;
        int num = TrackNumber(want);
        if (num <= 0) return null;
        int step = st.Numbering == "row" ? 1 : 2;
        for (int n = Math.Max(num, 2); n < 400; n += step)
            if (!taken.Contains(n + "П")) return n + "П";
        return null;
    }

    // =================================================================
    //  ВСЁ ЗАНОВО (кнопка «Перенумеровать»)
    // =================================================================
    public sealed class Report
    {
        public int Paths, Switches;
        public List<string> Skipped = new();
        public override string ToString() =>
            $"пути: {Paths}, стрелки: {Switches}" + (Skipped.Count > 0 ? ". Не переименовано: " + string.Join("; ", Skipped) : "");
    }

    public static Report RenumberAll(Station st)
    {
        var rep = new Report();
        var map = PathNames(st);
        rep.Skipped.AddRange(RenameSections(st, map));
        rep.Paths = map.Count(p => p.Key != p.Value);
        var before = st.Tiles.Where(p => p.Value.T == "switch").ToDictionary(p => p.Key, p => p.Value.Name);
        RenumberSwitches(st);
        rep.Switches = before.Count(p => st.Tiles[p.Key].Name != p.Value);
        return rep;
    }
}
