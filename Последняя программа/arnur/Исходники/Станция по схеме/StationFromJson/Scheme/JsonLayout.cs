using System.Text.RegularExpressions;
using StationApp.Core;
using StationApp.Ui;
using Ed = StationEditor.Model;
using TC = StationEditor.Model.Core;

namespace StationApp.Scheme;

// =====================================================================
//  СТАНЦИЯ ИЗ ФАЙЛА .station.json  [ДОБАВЛЕНО 10.11.2026]
//
//  То же, что у Алгабаса пишется руками в Algabas/AlgabasLayout.cs, здесь строится из схемы редактора станций
//  (формат «Редактора_05_11», чтение — его же файлы Model/Station.cs, Core.cs, Routes.cs, подключены ссылками):
//
//    клетки и участки  → TrackSection (тип участка: путь / участок / перегон / тупик / стрелочный)
//                        клетки без участка — свой участок «уч1», «уч2»… на каждую связную группу
//    стрелки           → RailSwitch (спаренные — с одинаковым «съездом» — одна стрелка, переводятся вместе)
//    светофоры         → RailSignal: входной — 5 огней (Ж З К Ж Б), выходной и маршрутный — 4 (Ж З К Б),
//                        маневровый — 2 (Б С), проходной — 3 (Ж З К, автоблокировка), чужой — светофор соседа.
//                        Предупредительного, повторительного, заградительного и прикрытия в ядре станций
//                        пока нет — они не переносятся (сообщение в журнале при запуске).
//    маршруты          → автопоиск редактора (RouteFinder): поездные и маневровые, стрелки, участки, светофор конца
//    перегон           → связные участки типа «перегон»; если по нему ездят в обе стороны — однопутный,
//                        DirectionLine с кнопкой «Сменить направление»; проходные горят только при своём направлении
//    переезд           → LevelCrossing: закрыт, пока занят участок переезда; маршрут через переезд — с извещением
//    топология поездов → Next(): по клеткам с учётом положения стрелок (как Next() у Алгабаса, но сама)
//    рисунок           → SchemeGeometry: те же куски пути, ветки стрелок, стыки, упоры, светофоры, номера стрелок,
//                        что рисует общая панель схемы — поэтому станция выглядит так же, как АКЖТ и Алгабас.
//  Клетка редактора — 40 проектных единиц (у Алгабаса пути через 120 — то есть через 3 клетки).
// =====================================================================

public class JsonLayout
{
    // [ИЗМЕНЕНО 11.11.2026] масштаб — как у АКЖТ и Алгабаса (было: клетка 40×40, всё стояло слишком тесно).
    // По горизонтали клетка 70: светофоры через 2 клетки (Ч и М2) — через ~140 единиц, как у Алгабаса (680 и 815),
    // ширина схемы Алгабаса из файла — 2520 (у Algabas.exe 2480). По вертикали соседние пути — через 120 единиц,
    // как в обеих станциях, сколько бы клеток между ними ни было в редакторе (высота клетки = 120 / шаг путей).
    const float CX = 70;
    const float RowStep = 120;
    float CY = TC.Cell;

    // лампы сверху вниз по мачте — как у Алгабаса (AlgabasLayout.L_ENTRY и т.д.)
    static readonly string[] L_ENTRY = { "yellow", "green", "red", "yellow1", "white" };
    static readonly string[] L_EXIT = { "yellow", "green", "red", "white" };
    static readonly string[] L_SHUNT = { "white", "blue" };
    static readonly string[] L_BLOCK = { "yellow", "green", "red" };
    static readonly string[] L_NEIGHBOR = { "yellow", "red", "yellow1", "white" };

    public readonly Interlocking IL;
    public readonly StationView View = new();
    SchemeGeometry G => View.Geo;
    /// <summary>Виртуальные поезда — выставляет RailModel.</summary>
    public TrainSim Trains;

    public readonly Ed.Station St;
    /// <summary>Название станции (из файла; нет — по имени файла).</summary>
    public readonly string Name;
    /// <summary>Что не перенесено и что поправлено само — в журнал при запуске.</summary>
    public readonly List<string> Notes = new();

    readonly Dictionary<string, TrackSection> secs = new();          // имя участка → участок
    readonly Dictionary<string, string> secType = new();             // имя участка → path / plain / line / dead / switch
    readonly Dictionary<string, List<string>> tilesOf = new();       // имя участка → клетки
    readonly Dictionary<string, RailSwitch> swByGroup = new();       // группа стрелки редактора → стрелка
    readonly Dictionary<string, RailSignal> sigByRef = new();        // "клетка#номер" → светофор
    readonly Dictionary<string, List<RailSignal>> sigsOnTile = new(); // клетка → светофоры на ней
    readonly Dictionary<RailSignal, string> tileOfSig = new();
    readonly List<(Ed.SignalEntry e, RailSignal s)> sigs = new();
    readonly HashSet<string> ids = new();
    public readonly List<DirectionLine> Lines = new();
    readonly Dictionary<TrackSection, DirectionLine> lineOf = new();
    int minX, minY, maxX, maxY;

    public int TrainRoutes => View.Routes.Count(r => !r.Shunting);
    public int ShuntRoutes => View.Routes.Count(r => r.Shunting);

    public JsonLayout(Interlocking il, Ed.Station st, string fileName)
    {
        IL = il;
        St = st;
        Name = string.IsNullOrWhiteSpace(st.Name) ? Path.GetFileNameWithoutExtension(fileName).Replace(".station", "") : st.Name.Trim();
        string bare = Regex.Replace(Name, @"^(станция|ст\.)\s*", "", RegexOptions.IgnoreCase);
        View.Title = $"Станция {bare}";
        View.Caption = $"Ст. {bare}";

        if (St.Tiles.Count == 0) throw new InvalidDataException("в файле нет ни одной клетки пути (tiles)");
        Bounds();
        NameFreeTiles();
        BuildSections();
        BuildSwitches();
        BuildSignals();
        BuildRoutes();
        BuildLines();
        BuildAutoSignals();
        BuildCrossings();
        BuildGeometry();
        foreach (var r in View.Routes) r.Pieces = RoutePainter.Compute(G, r);
        BuildSim();
        if (St.Objects.Count > 0)
            Notes.Add($"Объекты схемы (вокзал, часы, амперметр: {St.Objects.Count}) в программе станции пока не показываются.");
    }

    string Unique(string id)
    {
        string r = id;
        for (int i = 2; ids.Contains(r); i++) r = id + "#" + i;
        ids.Add(r);
        return r;
    }

    static string RefKey(Ed.SigRef r) => r.Key + "#" + r.Idx;

    // =================================================================
    //  КЛЕТКИ
    // =================================================================
    void Bounds()
    {
        minX = minY = int.MaxValue; maxX = maxY = int.MinValue;
        foreach (var k in St.Tiles.Keys.Concat(St.Texts.Keys))
        {
            var (x, y) = TC.Parse(k);
            minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y);
        }
        // шаг путей: самое маленькое расстояние (в клетках) между строками, где идут горизонтальные пути
        var rows = St.Tiles.Where(p => p.Value.T is "switch" or "crossing" or "buffer" or "exit" || (p.Value.T == "straight" && p.Value.V == 0))
                           .Select(p => TC.Parse(p.Key).Y).Distinct().OrderBy(y => y).ToList();
        int gap = int.MaxValue;
        for (int i = 1; i < rows.Count; i++) gap = Math.Min(gap, rows[i] - rows[i - 1]);
        if (gap != int.MaxValue && gap > 0) CY = Math.Clamp(RowStep / gap, TC.Cell, 80f);
    }

    float X(int gx) => (gx - minX + 1) * CX;
    float Y(int gy) => (gy - minY + 1) * CY;
    PointF Pt(string key, string side) { var (x, y) = TC.Parse(key); var d = TC.Dirs[side]; return new PointF(X(x) + d.Px * CX, Y(y) + d.Py * CY); }
    PointF Ctr(string key) { var (x, y) = TC.Parse(key); return new PointF(X(x) + CX / 2, Y(y) + CY / 2); }
    static (float, float) T(PointF p) => (p.X, p.Y);

    /// <summary>Клетки без участка: каждой связной группе — свой участок «уч1», «уч2»… (без участка нет занятости и маршрутов).</summary>
    void NameFreeTiles()
    {
        var free = St.Tiles.Where(p => string.IsNullOrWhiteSpace(p.Value.Sec)).Select(p => p.Key).ToHashSet();
        int n = 0;
        var names = new List<string>();
        while (free.Count > 0)
        {
            string name;
            do name = "уч" + ++n; while (St.Tiles.Values.Any(t => t.Sec == name));
            names.Add(name);
            var stack = new Stack<string>();
            var first = free.First();
            free.Remove(first);
            stack.Push(first);
            while (stack.Count > 0)
            {
                var k = stack.Pop();
                St.Tiles[k].Sec = name;
                foreach (var d in TC.EndsOf(St.Tiles[k]))
                {
                    var nb = TC.NeighborOf(St, k, d);
                    if (nb != null && free.Remove(nb.Key)) stack.Push(nb.Key);
                }
            }
        }
        if (names.Count > 0)
            Notes.Add($"Клетки без участка — программа назвала их сама: {string.Join(", ", names)}. Лучше задать участки в редакторе.");
    }

    // =================================================================
    //  УЧАСТКИ
    // =================================================================
    void BuildSections()
    {
        foreach (var (name, keys) in TC.TilesBySection(St)) tilesOf[name] = keys;
        foreach (var name in TC.SectionNames(St))
        {
            if (!tilesOf.ContainsKey(name)) continue;          // описан в sections, но клеток на схеме нет
            var type = TC.SecType(St, name);
            string title = type switch
            {
                "path" => $"Путь {name}",
                "line" => $"Блок-участок перегона {name}",
                "dead" => $"Тупик {name}",
                "switch" => $"Стрелочный участок {name}",
                _ => $"Участок {name}",
            };
            var s = IL.AddSection(Unique(name), title, name);
            View.Sections.Add(s);
            secs[name] = s;
            secType[name] = type;
        }
    }

    TrackSection SecOfTile(string key) => secs.TryGetValue(St.Tiles[key].Sec ?? "", out var s) ? s : null;
    string TypeOf(TrackSection s) => s != null && secType.TryGetValue(s.Label, out var t) ? t : "";

    // =================================================================
    //  СТРЕЛКИ
    // =================================================================
    void BuildSwitches()
    {
        var groups = new Dictionary<string, List<string>>();
        foreach (var (k, t) in St.Tiles.OrderBy(p => TC.Parse(p.Key).X))
        {
            if (t.T != "switch") continue;
            var g = TC.SwitchGroup(k, t);
            if (!groups.TryGetValue(g, out var l)) groups[g] = l = new List<string>();
            l.Add(k);
        }
        int unnamed = 0;
        foreach (var (g, keys) in groups)
        {
            var t0 = St.Tiles[keys[0]];
            var names = keys.Select(k => St.Tiles[k].Name).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();
            string label = !string.IsNullOrWhiteSpace(t0.Group) ? t0.Group.Trim()
                         : names.Count > 0 ? names[0].Trim() : "?" + ++unnamed;
            var ss = keys.Select(SecOfTile).Where(s => s != null).Distinct().ToArray();
            string title = keys.Count > 1
                ? $"Съезд {label}: стрелки {string.Join(" и ", names)} переводятся вместе"
                : $"Стрелка {label}";
            var sw = IL.AddSwitchL(Unique(label), label, title, ss);
            sw.Plus = t0.IsPlus;
            View.Switches.Add(sw);
            swByGroup[g] = sw;
        }
        if (unnamed > 0) Notes.Add($"Стрелок без номера: {unnamed} — им даны номера «?1», «?2»…");
    }

    // =================================================================
    //  СВЕТОФОРЫ
    // =================================================================
    void BuildSignals()
    {
        foreach (var e in TC.AllSignals(St))
        {
            string name = string.IsNullOrWhiteSpace(e.Sig.Name) ? "?" : e.Sig.Name.Trim();
            var dir = e.Sig.Dir == "L" ? Dir.Left : Dir.Right;
            string kindName = TC.SigKindName(e.Sig.Kind) ?? "Светофор";
            SignalKind kind;
            string[] lamps;
            switch (e.Sig.Kind)
            {
                case "entry": kind = SignalKind.Controlled; lamps = L_ENTRY; break;
                case "exit":
                case "route": kind = SignalKind.Controlled; lamps = L_EXIT; break;
                case "shunt": kind = SignalKind.Controlled; lamps = L_SHUNT; break;
                case "block": kind = SignalKind.Automatic; lamps = L_BLOCK; break;
                case "other": kind = SignalKind.Neighbor; lamps = L_NEIGHBOR; break;
                default:
                    Notes.Add($"{kindName} светофор {name}: таких светофоров в программах станций пока нет — на схеме не показан.");
                    continue;
            }
            var s = IL.AddSignal(new RailSignal
            {
                Id = Unique(name), Label = name, Title = $"{kindName} {name}", Kind = kind, Dir = dir, Lamps = lamps
            });
            if (kind == SignalKind.Controlled) s.Approach = SecOfTile(e.Key);
            if (e.Sig.Kind == "entry") View.InviteSignals.Add(s);
            sigByRef[RefKey(e.Ref)] = s;
            if (!sigsOnTile.TryGetValue(e.Key, out var list)) sigsOnTile[e.Key] = list = new List<RailSignal>();
            list.Add(s);
            tileOfSig[s] = e.Key;
            sigs.Add((e, s));
        }
    }

    RailSignal SignalOn(string key, Dir d) =>
        sigsOnTile.TryGetValue(key, out var l) ? l.FirstOrDefault(s => s.Dir == d) : null;

    // =================================================================
    //  МАРШРУТЫ — автопоиск редактора
    // =================================================================
    static readonly Regex RePath = new(@"^(I|II|III|IV|V|VI|\d+)П$", RegexOptions.IgnoreCase);

    string DestName(string dest)
    {
        if (dest == null) return "?";
        var m = RePath.Match(dest);
        if (m.Success) return "путь " + m.Groups[1].Value;
        if (secType.TryGetValue(dest, out var t) && t == "dead") return $"тупик {dest}";
        return dest;
    }

    void BuildRoutes()
    {
        var found = Ed.RouteFinder.FindRoutes(St);
        var seen = new Dictionary<string, int>();
        int skipped = 0;
        foreach (var r in found)
        {
            if (!sigByRef.TryGetValue(RefKey(r.Start), out var start)) { skipped++; continue; }
            var endE = Ed.RouteFinder.RouteEndSignal(St, r);
            RailSignal end = endE != null && sigByRef.TryGetValue(RefKey(endE.Ref), out var es) ? es : null;
            secs.TryGetValue(r.Dest ?? "", out var dest);
            if (r.Exit && dest == null)
            {
                Notes.Add($"Маршрут {r.StartName} на перегон: за светофором путь уходит за край схемы без участка — маршрут не добавлен.");
                continue;
            }
            string bas = r.Exit ? $"{start.Label} → перегон {dest.Label}" : $"{start.Label} → {DestName(r.Dest)}";
            string key = (r.Shunt ? "М:" : "П:") + bas;
            seen[key] = seen.TryGetValue(key, out int c) ? c + 1 : 1;
            string name = seen[key] > 1 ? $"{bas} (вариант {seen[key]})" : bas;

            var rr = new RailRoute
            {
                Id = Unique(start.Label + ">" + (r.Shunt ? "м:" : "") + name),
                Name = name,
                Shunting = r.Shunt,
                Start = start,
                End = end,
                Dest = dest,
                // у маневрового «отклонения» нет: светофор открывается лунно-белым в любом случае
                Diverging = !r.Shunt && r.Diverging,
                ExitSection = r.Exit ? dest : null,
            };
            // как у Алгабаса: в маневровый маршрут путь назначения не входит — он может быть занят (подъезд к вагонам)
            rr.Sections.AddRange(r.Sections.Where(n => secs.ContainsKey(n) && !(r.Shunt && n == r.Dest)).Select(n => secs[n]));
            foreach (var x in r.Switches)
                if (swByGroup.TryGetValue(x.Group, out var sw)) rr.Switches.Add((sw, x.Plus));
            IL.AddRoute(rr);
            View.Routes.Add(rr);
        }
        if (skipped > 0) Notes.Add($"Маршрутов от непереносимых светофоров пропущено: {skipped}.");
        if (View.Routes.Count == 0) Notes.Add("Маршрутов не найдено — проверьте в редакторе светофоры и типы участков («путь», «перегон»).");
    }

    // =================================================================
    //  ПЕРЕГОНЫ: однопутный — если по нему ездят в обе стороны
    // =================================================================
    void BuildLines()
    {
        var lineNames = secType.Where(p => p.Value == "line").Select(p => p.Key).ToHashSet();
        var left = new HashSet<string>(lineNames);
        while (left.Count > 0)
        {
            // связная группа участков перегона (по клеткам)
            var comp = new HashSet<string>();
            var stack = new Stack<string>();
            var first = left.First();
            left.Remove(first);
            stack.Push(first);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                comp.Add(n);
                foreach (var k in tilesOf[n])
                    foreach (var d in TC.EndsOf(St.Tiles[k]))
                    {
                        var nb = TC.NeighborOf(St, k, d);
                        var ns = nb?.Tile.Sec;
                        if (ns != null && left.Remove(ns)) stack.Push(ns);
                    }
            }
            var compSecs = comp.Select(n => secs[n]).ToHashSet();
            var dirs = new HashSet<Dir>();
            foreach (var r in View.Routes.Where(r => !r.Shunting && r.ExitSection != null && compSecs.Contains(r.ExitSection))) dirs.Add(r.Start.Dir);
            foreach (var (e, s) in sigs)
                if (compSecs.Contains(SecOfTile(e.Key)) && (s.Kind == SignalKind.Automatic || e.Sig.Kind == "entry")) dirs.Add(s.Dir);
            if (dirs.Count < 2) continue;      // путь перегона только в одну сторону (двухпутный) — направление не меняется

            var ordered = comp.OrderBy(n => tilesOf[n].Min(k => TC.Parse(k).X)).ToList();
            var line = new DirectionLine
            {
                Id = Unique("ОП" + (Lines.Count + 1)),
                Title = "Перегон " + string.Join(" – ", ordered),
                Current = Dir.Right,
                // на пульте: «→ чётное» / «← нечётное»; [ИЗМЕНЕНО 11.11.2026] какое направление чётное — из схемы (Station.Even)
                RightName = Ed.Numbering.EvenRight(St) ? "чётное" : "нечётное",
                LeftName = Ed.Numbering.EvenRight(St) ? "нечётное" : "чётное",
            };
            foreach (var n in ordered) { line.Sections.Add(secs[n]); lineOf[secs[n]] = line; }
            IL.Lines.Add(line);
            View.Lines.Add(line);
            Lines.Add(line);
        }

        // приём с однопутного перегона и отправление на него — только при своём направлении
        foreach (var r in View.Routes.Where(r => !r.Shunting))
        {
            DirectionLine L = null;
            bool departure = r.ExitSection != null && lineOf.TryGetValue(r.ExitSection, out L);
            if (!departure && (r.Start.Approach == null || !lineOf.TryGetValue(r.Start.Approach, out L))) continue;
            var need = r.Start.Dir;
            var line = L;
            r.Precondition = () => line.Current == need ? null
                : departure ? $"направление перегона «{line.DirText(line.Current)}» — для отправления сначала смените направление"
                            : $"направление перегона «{line.DirText(line.Current)}» — приём невозможен, сначала смените направление";
            line.DependentRoutes.Add(r);
        }
    }

    // =================================================================
    //  ПРОХОДНЫЕ: какой блок-участок ограждают и какой светофор следующий
    // =================================================================
    void BuildAutoSignals()
    {
        foreach (var (e, s) in sigs)
        {
            if (s.Kind != SignalKind.Automatic) continue;
            var from = SecOfTile(e.Key);
            var step = Step(from, s.Dir, live: false);
            s.Protects = step?.Section;
            // следующий светофор по ходу
            var cur = step;
            for (int i = 0; i < 60 && cur != null && cur.Section != null; i++)
            {
                cur = Step(cur.Section, s.Dir, live: false);
                if (cur?.Signal != null && cur.Signal != s) { s.Next = cur.Signal; break; }
            }
            var guard = from != null && lineOf.TryGetValue(from, out var L) ? L
                      : s.Protects != null && lineOf.TryGetValue(s.Protects, out var L2) ? L2 : null;
            if (guard != null)
            {
                var line = guard;
                var d = s.Dir;
                s.IsActive = () => line.Current == d;   // однопутка: горит только при своём направлении
            }
        }
    }

    // =================================================================
    //  ПЕРЕЕЗДЫ
    // =================================================================
    readonly List<(LevelCrossing c, string key)> crossings = new();

    void BuildCrossings()
    {
        int n = 0;
        foreach (var (k, t) in St.Tiles.OrderBy(p => TC.Parse(p.Key).X))
        {
            if (t.T != "crossing") continue;
            var sec = SecOfTile(k);
            var c = new LevelCrossing
            {
                Id = Unique("ПЕР" + ++n),
                Title = sec != null ? $"Переезд (участок {sec.Label})" : "Переезд",
                ShouldClose = () => sec != null && sec.Occupied,   // закрыт, пока поезд на переезде
            };
            if (sec != null)
                foreach (var r in View.Routes.Where(r => r.Sections.Contains(sec))) c.Routes.Add(r);
            IL.Crossings.Add(c);
            View.Crossings.Add(c);
            crossings.Add((c, k));
        }
    }

    // =================================================================
    //  ТОПОЛОГИЯ: куда поезд поедет из участка (по клеткам и положению стрелок)
    // =================================================================
    public TrackStep Next(TrackSection s, Dir d) => Step(s, d, live: true);

    TrackStep Step(TrackSection s, Dir d, bool live)
    {
        if (s == null || !tilesOf.TryGetValue(s.Label, out var keys)) return null;
        bool right = d == Dir.Right;
        bool Fwd(string side) => TC.IsRight(side) == right;

        var cands = new List<TrackStep>();
        foreach (var k in keys.OrderBy(k => right ? TC.Parse(k).X : -TC.Parse(k).X))
        {
            var t = St.Tiles[k];
            var ends = TC.EndsOf(t);
            if (t.T is "exit" or "buffer")
            {
                // начало у торца: путь с этого кубика идёт в сторону движения
                if (ends.Count == 1 && Fwd(ends[0])) Walk(k, ends[0]);
                else if (ends.Count == 1 && t.T == "exit")
                    cands.Add(new TrackStep { Exit = true, ExitName = "за край схемы", Dir = d, Signal = SignalOn(k, d) });
                else if (ends.Count == 1) cands.Add(new TrackStep { Buffer = true, Dir = d });
                continue;
            }
            foreach (var back in ends.Where(e => !Fwd(e)))
            {
                var nb = TC.NeighborOf(St, k, back);
                if (nb != null && nb.Tile.Sec == s.Label) continue;     // вход только с границы участка
                foreach (var o in TC.Through(t, back))
                    if (Ok(k, t, o.Need)) Walk(k, o.Out);
            }
        }

        void Walk(string key, string outSide)
        {
            for (int guard = 0; guard < 400; guard++)
            {
                var nb = TC.NeighborOf(St, key, outSide);
                if (nb == null) return;                                   // разрыв пути
                var nsec = SecOfTile(nb.Key);
                if (nsec != s)
                {
                    // как у Алгабаса («стрелка 2 стоит на путь 3 — с пути 1 к Ч не проехать (взрез), не едем»):
                    // без светофора на границе въезжаем на участок, только если через него можно проехать при нынешних
                    // стрелках. Со светофором — можно: он закрыт, а маршрут, который его откроет, сам поставит стрелки
                    // («со станционного пути — всегда через маневровый светофор» у Алгабаса)
                    var sig = SignalOn(key, d);
                    if (sig == null && !PassesThrough(nb.Key, nb.InSide, nsec)) return;
                    cands.Add(new TrackStep { Section = nsec, Dir = d, Signal = sig });
                    return;
                }
                if (nb.Tile.T == "exit") { cands.Add(new TrackStep { Exit = true, ExitName = "за край схемы", Dir = d, Signal = SignalOn(nb.Key, d) ?? SignalOn(key, d) }); return; }
                if (nb.Tile.T == "buffer") { cands.Add(new TrackStep { Buffer = true, Dir = d }); return; }
                var outs = TC.Through(nb.Tile, nb.InSide).Where(o => Ok(nb.Key, nb.Tile, o.Need)).ToList();
                if (outs.Count == 0) return;                              // стрелка стоит не так (взрез) — дальше нельзя
                key = nb.Key;
                outSide = outs[0].Out;
            }
        }

        if (cands.Count == 0) return null;
        var step = cands.FirstOrDefault(c => c.Section != null || c.Exit || c.Buffer);
        // за пределы станции на перегон — только поездом по маршруту отправления (манёвры за границу не выходят),
        // как у Алгабаса: if (s == CH_M2) return CH_M2.LockedBy is { Shunting: false } ? To(B12) : null
        if (live && step?.Section != null && TypeOf(step.Section) == "line" && TypeOf(s) != "line" && s.LockedBy is not { Shunting: false })
            return null;
        return step;
    }

    /// <summary>Можно ли проехать участок sec насквозь, въехав в клетку key со стороны inSide (стрелки — как стоят сейчас).</summary>
    bool PassesThrough(string key, string inSide, TrackSection sec)
    {
        for (int guard = 0; guard < 400; guard++)
        {
            var t = St.Tiles[key];
            if (t.T is "exit" or "buffer") return true;
            var outs = TC.Through(t, inSide).Where(o => Ok(key, t, o.Need)).ToList();
            if (outs.Count == 0) return false;                        // стрелка стоит не так — взрез
            var nb = TC.NeighborOf(St, key, outs[0].Out);
            if (nb == null || SecOfTile(nb.Key) != sec) return true;   // вышли из участка (или разрыв — дальше не видно)
            key = nb.Key;
            inSide = nb.InSide;
        }
        return true;
    }

    bool Ok(string key, Ed.Tile t, bool? need)
    {
        if (need == null) return true;
        return swByGroup.TryGetValue(TC.SwitchGroup(key, t), out var sw) && sw.Plus == need.Value;
    }

    // =================================================================
    //  РИСУНОК — как BuildGeometry() у Алгабаса (и «Экспорт в C#» редактора)
    // =================================================================
    void BuildGeometry()
    {
        var g = G;
        g.DesignW = X(maxX) + 2 * CX;
        g.DesignH = Y(maxY) + 2 * CY;
        int pieceN = 0;
        string Pid() => "e" + ++pieceN;
        string Sid(Ed.Tile t) => secs.TryGetValue(t.Sec ?? "", out var s) ? s.Id : "";

        // горизонтальные пути одного участка подряд — одним куском
        var flat = St.Tiles.Where(p => (p.Value.T == "straight" && p.Value.V == 0) || p.Value.T == "crossing")
            .Select(p => (K: p.Key, T: p.Value, XY: TC.Parse(p.Key)))
            .OrderBy(a => a.XY.Y).ThenBy(a => a.XY.X).ToList();
        var done = new HashSet<string>();
        for (int i = 0; i < flat.Count;)
        {
            int j = i;
            while (j + 1 < flat.Count && flat[j + 1].XY.Y == flat[i].XY.Y && flat[j + 1].XY.X == flat[j].XY.X + 1 &&
                   (flat[j + 1].T.Sec ?? "") == (flat[i].T.Sec ?? "") && !SplitAfter(flat[j].K) && !SplitBefore(flat[j + 1].K)) j++;
            g.Piece(Pid(), Sid(flat[i].T), T(Pt(flat[i].K, "W")), T(Pt(flat[j].K, "E")));
            for (int q = i; q <= j; q++) done.Add(flat[q].K);
            i = j + 1;
        }
        foreach (var (k, t) in St.Tiles)
        {
            if (done.Contains(k)) continue;
            var c = Ctr(k);
            var v = TC.VariantOf(t);
            if (t.T == "switch")
            {
                var sw = swByGroup[TC.SwitchGroup(k, t)];
                g.Piece(Pid(), Sid(t), T(Pt(k, v.Trunk)), T(c));
                g.Branch(Pid(), Sid(t), sw.Id, true, true, T(c), T(Pt(k, v.Plus)));
                g.Branch(Pid(), Sid(t), sw.Id, false, true, T(c), T(Pt(k, v.Minus)));
            }
            else if (t.T == "buffer")
            {
                g.Piece(Pid(), Sid(t), T(Pt(k, v.Ends[0])), T(c));
                g.Buffers.Add(c);
            }
            else if (t.T == "exit")
            {
                g.Piece(Pid(), Sid(t), T(Pt(k, v.Ends[0])), T(Pt(k, TC.Opp[v.Ends[0]])));   // путь уходит за край схемы
            }
            else
            {
                foreach (var e in TC.EdgesOf(t))
                {
                    var pts = TC.Opp[e.A] == e.B ? new[] { T(Pt(k, e.A)), T(Pt(k, e.B)) } : new[] { T(Pt(k, e.A)), T(c), T(Pt(k, e.B)) };
                    g.Piece(Pid(), Sid(t), pts);
                }
            }
        }

        // изолирующие стыки — где соседние клетки относятся к разным участкам
        foreach (var (k, t) in St.Tiles)
            foreach (var d in TC.EndsOf(t))
            {
                var nb = TC.NeighborOf(St, k, d);
                if (nb != null && TC.IsRight(d) && (t.Sec ?? "") != (nb.Tile.Sec ?? "")) { var p = Pt(k, d); g.Joint(p.X, p.Y); }
            }

        // светофоры: «←» над путём, «→» под путём; мачта — на торце клетки в сторону движения
        foreach (var (e, s) in sigs)
        {
            var want = e.Sig.Dir == "R" ? "E" : "W";
            var ends = TC.EndsOf(e.Tile).Where(x => (e.Sig.Dir == "R") == TC.IsRight(x)).ToList();
            var p = Pt(e.Key, ends.Contains(want) ? want : ends.Count > 0 ? ends[0] : want);
            g.Sig(s, p.X, p.Y, e.Sig.Dir == "L", s.Kind == SignalKind.Neighbor ? " (сосед)" : "");
        }

        // номера стрелок
        foreach (var (k, t) in St.Tiles)
        {
            if (t.T != "switch") continue;
            var (x, y) = TC.Parse(k);
            bool below = TC.VariantOf(t).Minus.Contains('N');
            string label = string.IsNullOrWhiteSpace(t.Name) ? swByGroup[TC.SwitchGroup(k, t)].Label : t.Name.Trim();
            if (!g.SwitchLabels.ContainsKey(label))
                g.SwLabel(label, swByGroup[TC.SwitchGroup(k, t)], X(x) + CX / 2, Y(y) + (below ? CY * 0.9f : CY * 0.1f));
        }

        // надписи (название станции не повторяем — его пишет крупный заголовок схемы)
        string Norm(string s) => Regex.Replace(s ?? "", @"^(станция|ст\.)\s*", "", RegexOptions.IgnoreCase).Trim().ToLowerInvariant();
        foreach (var (k, tx) in St.Texts)
        {
            if (string.IsNullOrWhiteSpace(tx.Text) || Norm(tx.Text) == Norm(Name)) continue;
            var (x, y) = TC.Parse(k);
            g.Text(tx.Text, X(x) + 3, Y(y) + CY / 2 - 8, (float)((tx.Size > 0 ? tx.Size : 14) * 0.7), true, true);
        }

        // переезды
        foreach (var (c, k) in crossings)
        {
            var p = Ctr(k);
            g.Crossings.Add(new CrossingGeom { Crossing = c, X = p.X, Y1 = p.Y, Y2 = p.Y });
        }

        // направление однопутного перегона: стрелка и подпись под его путём — ниже всех клеток и надписей над этим местом
        foreach (var line in Lines)
        {
            var pts = line.Sections.SelectMany(s => tilesOf[s.Label]).Select(Ctr).ToList();
            float x1 = pts.Min(p => p.X), x2 = pts.Max(p => p.X);
            float low = St.Tiles.Keys.Concat(St.Texts.Keys).Select(k => Ctr(k))
                          .Where(p => p.X >= x1 - CX && p.X <= x2 + CX).Max(p => p.Y);
            float arrowY = low + 52, textY = low + 76;
            g.Directions.Add(new DirectionGeom
            {
                Line = line, Pos = new PointF((x1 + x2) / 2, textY), ArrowX1 = x1 + 10, ArrowX2 = Math.Max(x1 + 60, x2 - 10), ArrowY = arrowY,
                RightText = "направление: →", LeftText = "направление: ←"
            });
            g.DesignH = Math.Max(g.DesignH, textY + 40);
        }

        g.AutoSectionLabels();
    }

    /// <summary>Горизонтальный кусок пути разрезается на мачте светофора (иначе маршрут не подсветится от светофора):
    /// мачта «→» — на правом краю клетки, «←» — на левом.</summary>
    bool SplitAfter(string key) => sigsOnTile.TryGetValue(key, out var l) && l.Any(s => s.Dir == Dir.Right);
    bool SplitBefore(string key) => sigsOnTile.TryGetValue(key, out var l) && l.Any(s => s.Dir == Dir.Left);

    // =================================================================
    //  ИМИТАЦИЯ: поезда с краёв схемы, пути для постановки поезда
    // =================================================================
    void BuildSim()
    {
        foreach (var (k, t) in St.Tiles.OrderBy(p => TC.Parse(p.Key).Y).ThenBy(p => TC.Parse(p.Key).X))
        {
            if (t.T != "exit") continue;
            var sec = SecOfTile(k);
            if (sec == null) continue;
            var side = TC.EndsOf(t).FirstOrDefault();
            if (side == null) continue;
            var dir = TC.IsRight(side) ? Dir.Right : Dir.Left;
            string text = dir == Dir.Right ? $"Поезд слева → ({sec.Label})" : $"Поезд справа ← ({sec.Label})";
            lineOf.TryGetValue(sec, out var line);
            View.SpawnButtons.Add((text, () =>
            {
                if (line != null && line.Current != dir)
                {
                    IL.Say($"Поезд не может выйти на {sec.Label}: направление перегона «{line.DirText(line.Current)}». Сначала смените направление.", LogKind.Refuse);
                    return;
                }
                Trains?.Spawn(sec, dir);
            }));
        }
        foreach (var (n, s) in secs.OrderBy(p => p.Key, Ed.NaturalComparer.Instance))
        {
            var type = secType[n];
            if (type == "path") View.TrainPlaces.Add((DestName(n) is var dn && dn.StartsWith("путь ") ? "Путь " + dn[5..] : n, s));
            else if (type == "dead") View.TrainPlaces.Add(($"Тупик {n}", s));
        }
    }

    // =================================================================
    //  ДЛЯ ИИ-ПОМОЩНИКА: пример маршрута с названиями
    // =================================================================
    public (string ru, string kk, string helpRu, string helpKk) Examples()
    {
        var r = View.Routes.FirstOrDefault(x => !x.Shunting && x.Start.Has("yellow1") && x.Dest != null && TypeOf(x.Dest) == "path")
             ?? View.Routes.FirstOrDefault(x => !x.Shunting && x.Dest != null && TypeOf(x.Dest) == "path")
             ?? View.Routes.FirstOrDefault();
        if (r == null) return ("как задать маршрут", "маршрутты қалай беремін", "светофор начала, затем путь.", "басталу светофоры, содан кейін жол.");
        var dn = DestName(r.Dest?.Label);
        string num = dn.StartsWith("путь ") ? dn[5..] : r.Dest?.Label ?? "?";
        return ($"маршрут {r.Start.Label} на путь {num}", $"{r.Start.Label}-дан {num} жолға маршрут",
                $"светофор {r.Start.Label}, затем путь {num}.", $"{r.Start.Label} светофоры, содан кейін {num} жол.");
    }
}
