namespace StationEditor.Model;

// =====================================================================
//  [ДОБАВЛЕНО 05.11.2026] ПОКАЗАНИЯ СВЕТОФОРОВ В «ИСПЫТАНИИ»
//
//  По Инструкции по сигнализации (гл. 2.8 — заградительные, 2.9 — предупредительные и повторительные)
//  и статье «Железнодорожная светофорная сигнализация»:
//    входной     — красный; жёлтый / зелёный (на главный путь, следующий закрыт / открыт);
//                  два жёлтых / два жёлтых с верхним мигающим (на боковой путь, следующий закрыт / открыт);
//    выходной, маршрутный — красный; жёлтый / зелёный (следующий закрыт / открыт); лунно-белый — манёвры;
//    маневровый  — синий / лунно-белый;
//    проходной   — красный (блок-участок впереди занят), жёлтый (следующий закрыт),
//                  жёлтый мигающий (следующий — входной на боковой путь), зелёный;
//    предупредительный — зелёный (основной открыт), жёлтый (закрыт), жёлтый мигающий (входной на боковой путь);
//                  перед заградительным — жёлтый, когда тот красный, иначе не горит;
//    повторительный — зелёный, когда выходной / маршрутный впереди открыт, иначе не горит
//                  (погашенный не имеет сигнального значения);
//    заградительный — нормально не горит, красный — стой (переезд впереди занят или включён вручную);
//    прикрытия   — красный; зелёный, если открыт вручную и впереди свободно.
//  «Впереди» — по ходу светофора, по стрелкам в их ТЕКУЩЕМ положении.
// =====================================================================

public enum Lamp { Off, Red, Yellow, Green, White, Blue, Gray }

public readonly record struct LampLit(Lamp Color, bool Blink = false);

public sealed class Aspect
{
    public LampLit[] Lamps;
    public string Text;
    /// <summary>Разрешает поезду проследовать (жёлтый или зелёный горит)</summary>
    public bool Permissive;
    /// <summary>Входной открыт на боковой путь (два жёлтых)</summary>
    public bool Diverging;

    static Aspect Of(string text, bool perm, params LampLit[] l) => new() { Lamps = l, Text = text, Permissive = perm };
    public static Aspect Red(int n = 1) => Of("красный", false, Pad(n, new LampLit(Lamp.Red)));
    public static Aspect Dark(int n = 1) => Of("не горит", false, Pad(n));
    static LampLit[] Pad(int n, params LampLit[] l)
    {
        var a = new LampLit[Math.Max(n, l.Length)];
        for (int i = 0; i < a.Length; i++) a[i] = i < l.Length ? l[i] : new LampLit(Lamp.Off);
        return a;
    }
    public static Aspect Make(string text, bool perm, int n, params LampLit[] l) => Of(text, perm, Pad(n, l));
}

/// <summary>Что известно в «Испытании»: занятость, открытые маршруты, стрелки без контроля, ручные включения.</summary>
public sealed class TestInputs
{
    public HashSet<string> Occ = new();
    /// <summary>"клетка#номер" светофора → маршрут, по которому он открыт</summary>
    public Dictionary<string, Route> OpenRoutes = new();
    /// <summary>Клетки стрелок без контроля положения (переводятся или потеряли контроль)</summary>
    public HashSet<string> NoControl = new();
    public HashSet<string> BarrierOn = new();
    public HashSet<string> CoverOpen = new();
}

public static class Aspects
{
    public static string Id(SignalEntry s) => s.Key + "#" + s.Idx;

    static readonly HashSet<string> NextMain = new() { "entry", "exit", "route", "block", "cover", "other" };
    static readonly HashSet<string> WarningMain = new() { "entry", "block", "barrier", "cover" };
    static readonly HashSet<string> RepeaterMain = new() { "exit", "route" };

    /// <summary>Сколько ламп рисовать: у входного две (два жёлтых), у остальных одна.</summary>
    public static int LampCount(string kind) => kind == "entry" ? 2 : 1;

    // ---------------------------------------------------------------------
    //  ПРОХОД ПО ПУТИ ВПЕРЁД ОТ СВЕТОФОРА
    // ---------------------------------------------------------------------
    public sealed class Walk
    {
        /// <summary>Найденный светофор того же направления (null — не найден)</summary>
        public SignalEntry Sig;
        /// <summary>Клетка, на которой остановились по stopTile</summary>
        public string StopTile;
        /// <summary>Участки по пути (включая участок найденного светофора)</summary>
        public readonly List<string> Sections = new();
        /// <summary>Путь ушёл за край схемы (выход)</summary>
        public bool Exit;
        /// <summary>Дальше хода нет: разрыв, упор, стрелка не в том положении или без контроля</summary>
        public bool Blocked;
    }

    public static Walk Go(Station st, SignalEntry from, Func<SignalEntry, bool> stopAt, Func<string, Tile, bool> stopTile = null,
                           HashSet<string> noControl = null, int maxSteps = 400)
    {
        var res = new Walk();
        string dir = from.Sig.Dir;
        string want = dir == "R" ? "E" : "W";
        var outs = Core.EndsOf(from.Tile).Where(d => (dir == "R") == Core.IsRight(d)).ToList();
        if (outs.Count == 0) { res.Blocked = true; return res; }
        string key = from.Key, outDir = outs.Contains(want) ? want : outs[0];

        for (int step = 0; step < maxSteps; step++)
        {
            var nb = Core.NeighborOf(st, key, outDir);
            if (nb == null || (noControl != null && noControl.Contains(nb.Key))) { res.Blocked = true; return res; }
            var t = nb.Tile;
            if (!string.IsNullOrEmpty(t.Sec) && !res.Sections.Contains(t.Sec)) res.Sections.Add(t.Sec);
            if (stopTile != null && stopTile(nb.Key, t)) { res.StopTile = nb.Key; return res; }
            if (t.Sigs != null && stopAt != null)
                for (int i = 0; i < t.Sigs.Count; i++)
                {
                    if (t.Sigs[i].Dir != dir) continue;
                    var e = new SignalEntry { Key = nb.Key, Idx = i, Sig = t.Sigs[i], Tile = t };
                    if (stopAt(e)) { res.Sig = e; return res; }
                }
            if (t.T == "exit") { res.Exit = true; return res; }
            if (t.T == "buffer") { res.Blocked = true; return res; }
            var pass = Core.Through(t, nb.InSide).Where(p => p.Need == null || p.Need == t.IsPlus).ToList();
            if (pass.Count == 0) { res.Blocked = true; return res; }    // стрелка стоит не туда — взрез, дальше не идём
            key = nb.Key;
            outDir = pass[0].Out;
        }
        res.Blocked = true;
        return res;
    }

    /// <summary>Основной светофор впереди: для предупредительного — входной / проходной / заградительный / прикрытия,
    /// для повторительного — выходной / маршрутный, для остальных — следующий поездной.</summary>
    public static SignalEntry MainAhead(Station st, SignalEntry s, HashSet<string> noControl)
    {
        var set = s.Sig.Kind == "warning" ? WarningMain : s.Sig.Kind == "repeater" ? RepeaterMain : NextMain;
        return Go(st, s, e => set.Contains(e.Sig.Kind), noControl: noControl).Sig;
    }

    /// <summary>Переезд впереди заградительного (не дальше 60 клеток): клетка переезда или null.</summary>
    public static string CrossingAhead(Station st, SignalEntry s) =>
        Go(st, s, null, (_, t) => t.T == "crossing", maxSteps: 60).StopTile;

    // ---------------------------------------------------------------------
    //  ПОКАЗАНИЯ
    // ---------------------------------------------------------------------
    /// <summary>Цвет ламп в редакторе (не в «Испытании»): каким огнём светофор «говорит».</summary>
    public static Aspect Static(Signal s) => s.Kind switch
    {
        "entry" => Aspect.Red(2),
        "shunt" => Aspect.Make("синий", false, 1, new LampLit(Lamp.Blue)),
        "warning" => Aspect.Make("жёлтый", false, 1, new LampLit(Lamp.Yellow)),
        "repeater" => Aspect.Make("зелёный", false, 1, new LampLit(Lamp.Green)),
        "other" => Aspect.Make("соседний", false, 1, new LampLit(Lamp.Gray)),
        _ => Aspect.Red(),
    };

    public static Dictionary<string, Aspect> Compute(Station st, TestInputs w)
    {
        var all = Core.AllSignals(st);
        var memo = new Dictionary<string, Aspect>();
        var visiting = new HashSet<string>();

        Aspect Get(SignalEntry s)
        {
            var id = Id(s);
            if (memo.TryGetValue(id, out var a)) return a;
            if (!visiting.Add(id)) return Aspect.Red(LampCount(s.Sig.Kind));    // замкнутый круг светофоров — считаем закрытым
            a = Calc(s);
            visiting.Remove(id);
            memo[id] = a;
            return a;
        }

        Walk NextMainWalk(SignalEntry s) => Go(st, s, e => NextMain.Contains(e.Sig.Kind), noControl: w.NoControl);

        Aspect Calc(SignalEntry s)
        {
            var id = Id(s);
            int n = LampCount(s.Sig.Kind);
            w.OpenRoutes.TryGetValue(id, out var route);
            switch (s.Sig.Kind)
            {
                case "entry":
                case "exit":
                case "route":
                {
                    if (route == null) return Aspect.Red(n);
                    if (route.Shunt) return Aspect.Make("лунно-белый", false, n, new LampLit(Lamp.White));
                    var nx = NextMainWalk(s);
                    bool nextOpen = nx.Sig != null && Get(nx.Sig).Permissive;
                    if (s.Sig.Kind == "entry" && route.Diverging)
                        return nextOpen
                            ? new Aspect { Lamps = new[] { new LampLit(Lamp.Yellow, true), new LampLit(Lamp.Yellow) }, Text = "два жёлтых, верхний мигающий", Permissive = true, Diverging = true }
                            : new Aspect { Lamps = new[] { new LampLit(Lamp.Yellow), new LampLit(Lamp.Yellow) }, Text = "два жёлтых", Permissive = true, Diverging = true };
                    return nextOpen
                        ? Aspect.Make("зелёный", true, n, new LampLit(Lamp.Green))
                        : Aspect.Make("жёлтый", true, n, new LampLit(Lamp.Yellow));
                }
                case "shunt":
                    return route != null
                        ? Aspect.Make("лунно-белый", false, 1, new LampLit(Lamp.White))
                        : Aspect.Make("синий", false, 1, new LampLit(Lamp.Blue));
                case "block":
                {
                    var nx = NextMainWalk(s);
                    if (nx.Blocked || nx.Sections.Any(w.Occ.Contains)) return Aspect.Red();
                    if (nx.Sig == null) return Aspect.Make("жёлтый", true, 1, new LampLit(Lamp.Yellow));
                    var na = Get(nx.Sig);
                    if (!na.Permissive) return Aspect.Make("жёлтый", true, 1, new LampLit(Lamp.Yellow));
                    if (na.Diverging) return Aspect.Make("жёлтый мигающий", true, 1, new LampLit(Lamp.Yellow, true));
                    return Aspect.Make("зелёный", true, 1, new LampLit(Lamp.Green));
                }
                case "warning":
                {
                    var main = MainAhead(st, s, w.NoControl);
                    if (main == null) return Aspect.Dark();
                    var ma = Get(main);
                    if (main.Sig.Kind == "barrier")
                        return ma.Lamps[0].Color == Lamp.Red ? Aspect.Make("жёлтый (заградительный закрыт)", true, 1, new LampLit(Lamp.Yellow)) : Aspect.Dark();
                    if (!ma.Permissive) return Aspect.Make("жёлтый (основной закрыт)", true, 1, new LampLit(Lamp.Yellow));
                    if (ma.Diverging) return Aspect.Make("жёлтый мигающий (приём на боковой путь)", true, 1, new LampLit(Lamp.Yellow, true));
                    return Aspect.Make("зелёный (основной открыт)", true, 1, new LampLit(Lamp.Green));
                }
                case "repeater":
                {
                    var main = MainAhead(st, s, w.NoControl);
                    if (main != null && Get(main).Permissive) return Aspect.Make("зелёный (выходной открыт)", true, 1, new LampLit(Lamp.Green));
                    return Aspect.Dark();
                }
                case "barrier":
                {
                    bool on = w.BarrierOn.Contains(id);
                    var cr = CrossingAhead(st, s);
                    if (cr != null && st.Tiles.TryGetValue(cr, out var ct) && !string.IsNullOrEmpty(ct.Sec) && w.Occ.Contains(ct.Sec)) on = true;
                    return on ? Aspect.Make("красный — стой", false, 1, new LampLit(Lamp.Red)) : Aspect.Dark();
                }
                case "cover":
                {
                    if (!w.CoverOpen.Contains(id)) return Aspect.Red();
                    var nx = NextMainWalk(s);
                    if (nx.Blocked || nx.Sections.Any(w.Occ.Contains)) return Aspect.Red();
                    return Aspect.Make("зелёный", true, 1, new LampLit(Lamp.Green));
                }
                default:
                    return Aspect.Make("соседний (неизвестно)", false, 1, new LampLit(Lamp.Gray));
            }
        }

        foreach (var s in all) Get(s);
        return memo;
    }
}
