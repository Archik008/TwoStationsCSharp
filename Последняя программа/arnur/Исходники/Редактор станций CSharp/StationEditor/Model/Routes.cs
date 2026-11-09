namespace StationEditor.Model;

// =====================================================================
//  МАРШРУТЫ (автопоиск по кубикам) и ПРОВЕРКА СХЕМЫ
//
//  От каждого входного, выходного и маневрового светофора идём по путям в его сторону,
//  перебирая положения стрелок (спаренные — только вместе). Маршрут кончается:
//    поездной   — на пути приёма-отправления (тип участка «путь») или на перегоне (отправление);
//    маневровый — на первом нестрелочном участке (путь, участок между светофорами, тупик);
//                 на перегон маневровые не выходят.
//  Выходной светофор даёт и поездные, и маневровые маршруты (белый огонь).
//  Это то, что в программах АКЖТ и Алгабаса сейчас пишется руками в BuildRoutes().
// =====================================================================

public sealed class RouteSwitch
{
    public string Group;
    public bool Plus;
    public List<string> Keys;
    public string Name;
}

public sealed class Route
{
    public SigRef Start;
    public string StartName;
    public string Dir;
    public bool Shunt;
    public string Dest;
    public bool Exit;
    public List<string> Tiles;
    public List<string> Sections;
    public List<RouteSwitch> Switches;
    public bool Diverging;
    public string Name;
    public string Id;

    public string SwitchesText(string sep = " ") =>
        string.Join(sep, Switches.Select(x => x.Name + (x.Plus ? "+" : "−")));
}

public static class RouteFinder
{
    public static List<Route> FindRoutes(Station st)
    {
        var bySec = Core.TilesBySection(st);
        var types = new Dictionary<string, string>();
        string Stype(string n)
        {
            if (!types.TryGetValue(n, out var v)) types[n] = v = Core.SecType(st, n);
            return v;
        }

        // спаренные стрелки: группа → клетки
        var groupKeys = new Dictionary<string, List<string>>();
        foreach (var (k, t) in st.Tiles)
        {
            if (t.T != "switch") continue;
            var g = Core.SwitchGroup(k, t);
            if (!groupKeys.TryGetValue(g, out var l)) groupKeys[g] = l = new List<string>();
            l.Add(k);
        }

        var routes = new List<Route>();

        foreach (var s in Core.AllSignals(st))
        {
            var modes = new List<bool>();
            if (s.Sig.Kind == "entry" || s.Sig.Kind == "exit") modes.Add(false);
            if (s.Sig.Kind == "shunt" || s.Sig.Kind == "exit") modes.Add(true);
            foreach (var shunt in modes) Search(s, shunt);
        }

        void Search(SignalEntry s, bool shunt)
        {
            string startSec = s.Tile.Sec ?? "";
            string want = s.Sig.Dir == "R" ? "E" : "W";
            var outs = Core.EndsOf(s.Tile).Where(d => (s.Sig.Dir == "R") == Core.IsRight(d)).ToList();
            if (outs.Count == 0) return;
            string first = outs.Contains(want) ? want : outs[0];

            void Finish(IEnumerable<string> tiles, Dictionary<string, bool> sws, string dest, bool exit)
            {
                var uniq = new List<string>();
                var seenT = new HashSet<string>();
                foreach (var k in tiles) if (seenT.Add(k)) uniq.Add(k);
                var sections = new List<string>();
                foreach (var k in uniq)
                {
                    var sec = st.Tiles[k].Sec;
                    if (!string.IsNullOrEmpty(sec) && sec != startSec && !sections.Contains(sec)) sections.Add(sec);
                }
                if (exit) sections.Remove(dest);
                var switches = sws.Select(kv =>
                {
                    groupKeys.TryGetValue(kv.Key, out var keys);
                    keys ??= new List<string>();
                    var t0 = keys.Count > 0 ? st.Tiles[keys[0]] : null;
                    string nm = t0 == null ? "?" : (!string.IsNullOrEmpty(t0.Group) ? t0.Group : (!string.IsNullOrEmpty(t0.Name) ? t0.Name : "?"));
                    return new RouteSwitch { Group = kv.Key, Plus = kv.Value, Keys = keys, Name = nm };
                }).ToList();
                routes.Add(new Route
                {
                    Start = s.Ref, StartName = string.IsNullOrEmpty(s.Sig.Name) ? "?" : s.Sig.Name, Dir = s.Sig.Dir,
                    Shunt = shunt, Dest = dest, Exit = exit, Tiles = uniq, Sections = sections, Switches = switches,
                    Diverging = switches.Any(x => !x.Plus),
                });
            }

            void Dfs(string key, string outDir, List<string> path, Dictionary<string, bool> sws, HashSet<string> seen)
            {
                var nb = Core.NeighborOf(st, key, outDir);
                if (nb == null) return;                                   // разрыв пути — маршрута нет
                string vk = nb.Key + "|" + nb.InSide;
                if (seen.Contains(vk)) return;
                string sec = nb.Tile.Sec ?? "";
                string type = Stype(sec);

                if (sec != "" && sec != startSec)
                {
                    if (type == "line") { if (!shunt) Finish(path, sws, sec, true); return; }
                    if (!shunt && type == "path") { Finish(path.Concat(bySec[sec]), sws, sec, false); return; }
                    if (shunt && type != "switch") { Finish(path.Concat(bySec[sec]), sws, sec, false); return; }
                }
                if (nb.Tile.T == "exit") { if (!shunt) Finish(path.Append(nb.Key), sws, sec != "" ? sec : "перегон", true); return; }
                if (nb.Tile.T == "buffer") { if (shunt) Finish(path.Append(nb.Key), sws, sec != "" ? sec : "тупик", false); return; }

                var path2 = new List<string>(path) { nb.Key };
                var seen2 = new HashSet<string>(seen) { vk };
                foreach (var o in Core.Through(nb.Tile, nb.InSide))
                {
                    var sws2 = sws;
                    if (o.Need != null)
                    {
                        var g = Core.SwitchGroup(nb.Key, nb.Tile);
                        if (sws.TryGetValue(g, out bool prev) && prev != o.Need.Value) continue;   // спаренная стрелка уже нужна в другом положении
                        sws2 = new Dictionary<string, bool>(sws) { [g] = o.Need.Value };
                    }
                    Dfs(nb.Key, o.Out, path2, sws2, seen2);
                }
            }

            Dfs(s.Key, first, new List<string>(), new Dictionary<string, bool>(), new HashSet<string>());
        }

        // имена: «Ч → 1П», одинаковые — с номером варианта
        var count = new Dictionary<string, int>();
        foreach (var r in routes)
        {
            string bas = r.Exit ? $"{r.StartName} → {r.Dest} (перегон)" : $"{r.StartName} → {r.Dest}";
            string id = (r.Shunt ? "М:" : "П:") + bas;
            count[id] = count.TryGetValue(id, out int c) ? c + 1 : 1;
            r.Name = count[id] > 1 ? $"{bas} (вариант {count[id]})" : bas;
            r.Id = (r.Shunt ? "М:" : "П:") + r.Name;
        }
        return routes
            .OrderBy(r => r.Shunt)
            .ThenBy(r => r.StartName, NaturalComparer.Instance)
            .ThenBy(r => r.Name, NaturalComparer.Instance)
            .ToList();
    }

    /// <summary>Светофор конца маршрута — как End в RailRoute: для приёма и манёвров — светофор встречного
    /// направления на участке назначения, для отправления — первый светофор перегона по ходу.</summary>
    public static SignalEntry RouteEndSignal(Station st, Route r)
    {
        string dirWanted = r.Exit ? r.Dir : (r.Dir == "R" ? "L" : "R");
        var c = Core.AllSignals(st).Where(s => s.Tile.Sec == r.Dest && s.Sig.Dir == dirWanted).ToList();
        if (c.Count == 0) return null;
        int sx = Core.Parse(r.Start.Key).X;
        return c.OrderBy(s => Math.Abs(Core.Parse(s.Key).X - sx)).First();
    }
}

// ---------------------------------------------------------------------
//  ПРОВЕРКА СХЕМЫ: Level = err | warn | info
// ---------------------------------------------------------------------
public sealed class Problem
{
    public string Level, Text, Key;
}

public static class Validator
{
    public static List<Problem> Validate(Station st, List<Route> routes)
    {
        var res = new List<Problem>();
        void Add(string level, string text, string key = null) => res.Add(new Problem { Level = level, Text = text, Key = key });
        var tiles = st.Tiles.ToList();

        if (tiles.Count == 0) { Add("info", "Схема пустая: выберите кубик слева и щёлкните по сетке."); return res; }

        // разрывы
        foreach (var (k, t) in tiles)
        {
            if (t.T == "exit") continue;
            foreach (var d in Core.EndsOf(t))
            {
                if (Core.NeighborOf(st, k, d) != null) continue;
                Add("err", $"Разрыв пути: клетка {k}, выход {Core.DirArrow[d]} никуда не ведёт (поставьте кубик, тупик или «Выход»)", k);
            }
        }

        // клетки без участка
        var noSec = tiles.Where(p => string.IsNullOrEmpty(p.Value.Sec)).ToList();
        foreach (var (k, _) in noSec.Take(12)) Add("warn", $"Клетка {k} не отнесена ни к одному участку (инструмент «Участок»)", k);
        if (noSec.Count > 12) Add("warn", $"…и ещё {noSec.Count - 12} клеток без участка");

        // стрелки
        var groups = new Dictionary<string, List<string>>();
        foreach (var (k, t) in tiles)
        {
            if (t.T != "switch") continue;
            if (string.IsNullOrEmpty(t.Name)) Add("err", $"Стрелка в клетке {k} без номера", k);
            if (!string.IsNullOrEmpty(t.Group))
            {
                if (!groups.TryGetValue(t.Group, out var l)) groups[t.Group] = l = new List<string>();
                l.Add(k);
            }
        }
        foreach (var (g, ks) in groups)
        {
            if (ks.Count == 1) Add("warn", $"Съезд «{g}» из одной стрелки — вторую стрелку съезда не нашли", ks[0]);
            if (ks.Count > 2) Add("warn", $"В съезде «{g}» {ks.Count} стрелки — обычно их две", ks[0]);
        }

        // светофоры
        var sigs = Core.AllSignals(st);
        var names = new Dictionary<string, List<string>>();
        foreach (var s in sigs)
        {
            if (string.IsNullOrEmpty(s.Sig.Name)) Add("err", $"Светофор в клетке {s.Key} без названия", s.Key);
            else
            {
                if (!names.TryGetValue(s.Sig.Name, out var l)) names[s.Sig.Name] = l = new List<string>();
                l.Add(s.Key);
            }
            if (!Core.EndsOf(s.Tile).Any(d => (s.Sig.Dir == "R") == Core.IsRight(d)))
                Add("warn", $"Светофор {Nz(s.Sig.Name)} ({s.Key}) смотрит туда, где у клетки нет пути", s.Key);
        }
        foreach (var (n, ks) in names)
            if (ks.Count > 1) Add("err", $"Два светофора с названием «{n}»: {string.Join("; ", ks)}", ks[0]);

        // участок, разорванный на куски
        foreach (var (sec, keys) in Core.TilesBySection(st))
        {
            var left = new HashSet<string>(keys);
            int parts = 0;
            while (left.Count > 0)
            {
                parts++;
                var first = keys.First(left.Contains);
                left.Remove(first);
                var stack = new Stack<string>();
                stack.Push(first);
                while (stack.Count > 0)
                {
                    var k = stack.Pop();
                    foreach (var d in Core.EndsOf(st.Tiles[k]))
                    {
                        var nb = Core.NeighborOf(st, k, d);
                        if (nb != null && left.Remove(nb.Key)) stack.Push(nb.Key);
                    }
                }
            }
            if (parts > 1) Add("warn", $"Участок «{sec}» разорван на {parts} части — между ними чужие клетки или разрыв", keys[0]);
        }

        // маршруты
        if (sigs.Count == 0) Add("info", "Светофоров пока нет — маршрутов не будет (инструмент «Светофор»).");
        else
        {
            int train = routes.Count(r => !r.Shunt), shunt = routes.Count - train;
            Add("info", $"Найдено маршрутов: поездных {train}, маневровых {shunt}.");
            foreach (var s in sigs)
            {
                if (s.Sig.Kind != "entry" && s.Sig.Kind != "exit" && s.Sig.Kind != "shunt") continue;
                if (!routes.Any(r => r.Start == s.Ref))
                    Add("warn", $"От светофора {Nz(s.Sig.Name)} не найдено ни одного маршрута — проверьте разрывы и типы участков впереди (есть ли «путь»)", s.Key);
            }
        }
        return res;
    }

    static string Nz(string s) => string.IsNullOrEmpty(s) ? "?" : s;
}
