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
//
//  [ИЗМЕНЕНО 09.11.2026] по РУ-30-80 «Указания по применению светофорной сигнализации»:
//    - выходной и маршрутный с ГЛАВНОГО пути (римская цифра: IП, IIП, IIАП…, стр. 89) при маршруте
//      с отклонением по стрелкам — два жёлтых, два жёлтых с верхним мигающим, если следующий открыт (стр. 9, 11, 14, 20);
//      с бокового пути — только жёлтый / зелёный (выходной Н3, Н5 на стр. 9, 14);
//    - входной, выходной, маршрутный: один жёлтый мигающий, если следующий светофор показывает два жёлтых
//      (входной перед выходным «по варианту» — стр. 9, 14; выходной перед входным соседней станции — стр. 20);
//    - головки светофоров: входной и поездные с главного пути — 5 огней (от мачты: лунно-белый, жёлтый нижний,
//      красный, зелёный, жёлтый верхний — стр. 9, 49), выходной / маршрутный с бокового пути — 4 огня
//      (лунно-белый, красный, зелёный, жёлтый — стр. 9). Один жёлтый горит на верхней лампе.
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
    /// <summary>[ДОБАВЛЕНО 13.11.2026] Горит буква на световом указателе (горочный: «Н» — осаживание назад); null — указатель тёмный.</summary>
    public string Indicator;
    /// <summary>[ДОБАВЛЕНО 17.11.2026] Горит маршрутный указатель: номер пути и цвет (белый / зелёный); null — тёмный.</summary>
    public string RouteText;
    public Lamp RouteColor = Lamp.White;

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
    /// <summary>[ДОБАВЛЕНО 13.11.2026] Горочный светофор → режим (индекс в Aspects.HumpModes; нет — стоп).</summary>
    public Dictionary<string, int> HumpMode = new();
}

public static class Aspects
{
    public static string Id(SignalEntry s) => s.Key + "#" + s.Idx;

    static readonly HashSet<string> NextMain = new() { "entry", "exit", "route", "block", "cover", "other" };
    static readonly HashSet<string> WarningMain = new() { "entry", "block", "barrier", "cover" };
    static readonly HashSet<string> RepeaterMain = new() { "exit", "route" };
    static readonly HashSet<string> HumpMain = new() { "hump" };

    // ---- [ДОБАВЛЕНО 13.11.2026] СОРТИРОВОЧНАЯ ГОРКА: РУ-30-80 листы 45–47 (стр. 53–55), caredenis.ru урок 14 ----
    // Горочный Г (Г1, Г2…): от мачты — лунно-белый, зелёный, красный, жёлтый («Г ⊢⊡◎○●⊘», листы 46, 47) и указатель «Н».
    // Повторитель ПГ — те же огни (П1Г1, лист 46); на пути парка приёма вместо красного — синий (лист 46, прим. 2).
    static readonly Lamp[] HeadHump = { Lamp.White, Lamp.Green, Lamp.Red, Lamp.Yellow };
    static readonly Lamp[] HeadHumpPark = { Lamp.White, Lamp.Green, Lamp.Blue, Lamp.Yellow };

    /// <summary>Режимы горочного светофора (задаёт оператор горки): текст и огни. Роспуск: скорость по огням —
    /// жёлтый / жёлтый и зелёный / зелёный (лист 47, прим. 1; урок 14: «медленно», «нормально», «быстро»).</summary>
    public static readonly (string Text, bool Perm, string Indicator, Lamp[] Lit)[] HumpModes =
    {
        ("стоп — красный", false, null, new[] { Lamp.Red }),
        ("роспуск медленно — жёлтый", true, null, new[] { Lamp.Yellow }),
        ("роспуск нормально — жёлтый и зелёный", true, null, new[] { Lamp.Yellow, Lamp.Green }),
        ("роспуск быстро — зелёный", true, null, new[] { Lamp.Green }),
        ("осаживание назад — красный и «Н»", false, "Н", new[] { Lamp.Red }),
        ("маневры на пути сортировочного парка — лунно-белый", false, null, new[] { Lamp.White }),
    };

    // ---- [ДОБАВЛЕНО 17.11.2026] маршрутный (путевой) указатель: РУ-30-80 лист 10 (стр. 18); Инструкция по сигнализации РК
    // (приказ № 209 от 18.04.2011) п. 70 — белый, путь приёма или направление; п. 71 и ПТЭ РК п. 134 — зелёный на групповых
    // выходных и маршрутных, номер пути, с которого разрешено отправление (и маневры при лунно-белом) ----

    /// <summary>Каким светофорам можно поставить маршрутный указатель.</summary>
    public static bool CanHaveRouteInd(string kind) => kind is "entry" or "exit" or "route" or "shunt";

    /// <summary>Цвет указателя: выходной и маршрутный — зелёный (п. 71), входной и маневровый — белый (п. 70).</summary>
    public static Lamp RouteIndColor(string kind) => kind is "exit" or "route" ? Lamp.Green : Lamp.White;

    /// <summary>Номер пути, который показывает указатель при открытом маршруте route: у входного — путь приёма
    /// (путь назначения маршрута), у остальных — путь, с которого отправление (за светофором, по стрелкам как стоят);
    /// у маневрового без пути позади — путь назначения.</summary>
    public static string RouteIndText(Station st, SignalEntry s, Route route)
    {
        if (route == null) return null;
        string sec;
        if (s.Sig.Kind == "entry") sec = route.Exit ? null : route.Dest;
        else sec = TrackBehind(st, s);
        // маневровый в горловине (позади пути нет) — номер пути, на который маршрут
        if (sec == null && s.Sig.Kind == "shunt" && !route.Exit) sec = route.Dest;
        if (string.IsNullOrEmpty(sec)) return null;
        return Core.TrackNo(sec) ?? sec;
    }

    /// <summary>Путь (участок типа «путь»), с которого отправляется поезд от светофора s: свой участок, если это путь,
    /// иначе первый путь позади светофора (против его направления) по стрелкам в их текущем положении.</summary>
    public static string TrackBehind(Station st, SignalEntry s)
    {
        if (Core.SecType(st, s.Tile.Sec) == "path") return s.Tile.Sec;
        var back = new SignalEntry { Key = s.Key, Idx = s.Idx, Tile = s.Tile, Sig = new Signal { Dir = s.Sig.Dir == "R" ? "L" : "R", Kind = s.Sig.Kind } };
        var w = Go(st, back, null, (_, t) => Core.SecType(st, t.Sec) == "path", maxSteps: 120);
        return w.StopTile != null ? st.Tiles[w.StopTile].Sec : null;
    }

    /// <summary>Сколько путей (участков «путь») сходится позади выходного / маршрутного — по всем веткам стрелок.
    /// 2 и больше — групповой светофор: по ПТЭ РК п. 134 ему нужен маршрутный указатель.</summary>
    public static int TracksBehind(Station st, SignalEntry s)
    {
        if (Core.SecType(st, s.Tile.Sec) == "path") return 1;
        string dir = s.Sig.Dir == "R" ? "L" : "R";
        var outs = Core.EndsOf(s.Tile).Where(d => (dir == "R") == Core.IsRight(d)).ToList();
        var found = new HashSet<string>();
        var seen = new HashSet<string>();
        var stack = new Stack<(string Key, string Out, int Depth)>();
        foreach (var o in outs) stack.Push((s.Key, o, 0));
        while (stack.Count > 0)
        {
            var (key, outDir, depth) = stack.Pop();
            if (depth > 120) continue;
            var nb = Core.NeighborOf(st, key, outDir);
            if (nb == null || !seen.Add(nb.Key + "|" + nb.InSide)) continue;
            if (Core.SecType(st, nb.Tile.Sec) == "path") { found.Add(nb.Tile.Sec); continue; }
            foreach (var p in Core.Through(nb.Tile, nb.InSide)) stack.Push((nb.Key, p.Out, depth + 1));
        }
        return found.Count;
    }

    /// <summary>Есть ли у светофора световой указатель «Н» (горочный и его повторитель).</summary>
    public static bool HasIndicator(Signal s) => s.Kind is "hump" or "humprep";

    // [ИЗМЕНЕНО 09.11.2026] головки светофоров по РУ-30-80 (стр. 9, 49): цвета ламп от мачты наружу
    static readonly Lamp[] HeadMain = { Lamp.White, Lamp.Yellow, Lamp.Red, Lamp.Green, Lamp.Yellow };
    static readonly Lamp[] HeadSide = { Lamp.White, Lamp.Red, Lamp.Green, Lamp.Yellow };

    static readonly System.Text.RegularExpressions.Regex ReMain =
        new(@"^(I{1,3}|IV|VI{0,3}|IX|X)[А-ЯA-Z]?П?$", System.Text.RegularExpressions.RegexOptions.Compiled);    // [ИЗМЕНЕНО 10.11.2026] «П» необязательна

    /// <summary>Главный путь — номер римскими цифрами (IП, IIП, IIАП), как на стр. 89 РУ-30-80.</summary>
    public static bool IsMainTrack(string sec) => !string.IsNullOrEmpty(sec) && ReMain.IsMatch(sec.Trim().ToUpperInvariant());

    /// <summary>Лампы головки (от мачты) у входного, выходного и маршрутного; null — светофор с одной лампой.</summary>
    public static Lamp[] Head(Signal s, Tile t) => s.Kind switch
    {
        "entry" => HeadMain,
        "exit" or "route" => IsMainTrack(t?.Sec) ? HeadMain : HeadSide,
        "hump" => HeadHump,
        "humprep" => Core.GuessSecType(t?.Sec) == "path" ? HeadHumpPark : HeadHump,
        _ => null,
    };

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Все лампы головки (от мачты наружу) — для вида «Огни по ГОСТ»:
    /// входной / выходной / маршрутный — как Head; проходной — красный, зелёный, жёлтый (РУ-30-80 стр. 9, «1 ⊢●○⊘»);
    /// предупредительный — зелёный, жёлтый (к заградительному — один жёлтый ромб, ГОСТ 2.749-84 табл. 5 п. 8);
    /// маневровый — синий, лунно-белый; прикрытия — красный, зелёный; заградительный — красный; повторительный — зелёный.</summary>
    public static Lamp[] FullHead(Signal s, Tile t, bool diamond) => Head(s, t) ?? s.Kind switch
    {
        "block" => new[] { Lamp.Red, Lamp.Green, Lamp.Yellow },
        "warning" => diamond ? new[] { Lamp.Yellow } : new[] { Lamp.Green, Lamp.Yellow },
        "shunt" => new[] { s.IsRedShunt ? Lamp.Red : Lamp.Blue, Lamp.White },    // [17.11.2026] красный вместо синего (РК п. 98)
        "cover" => new[] { Lamp.Red, Lamp.Green },
        "barrier" => new[] { Lamp.Red },
        "repeater" => new[] { Lamp.Green },
        _ => new[] { Lamp.Gray },
    };

    /// <summary>Пригласительный — отдельно от остальных огней: у входного это нижняя (ближняя к мачте) лампа
    /// («пригласительный сигнал на входном светофоре всегда размещают в отдельной головке» — caredenis.ru les03;
    /// у выходного и маршрутного тот же белый огонь служит и для манёвров — РУ-30-80 п. 3.6).</summary>
    public static bool InvitationApart(Signal s) => s.Kind == "entry";

    /// <summary>Разложить горящие огни показания по лампам полной головки (у светофоров с одной лампой в цвете).</summary>
    public static LampLit[] OnFullHead(Lamp[] head, LampLit[] lit)
    {
        if (lit.Length == head.Length) return lit;
        var a = new LampLit[head.Length];
        for (int i = 0; i < a.Length; i++) a[i] = new LampLit(Lamp.Off);
        var used = new bool[head.Length];
        foreach (var l in lit)
        {
            if (l.Color == Lamp.Off) continue;
            for (int i = head.Length - 1; i >= 0; i--)
                if (!used[i] && head[i] == l.Color) { a[i] = l; used[i] = true; break; }
        }
        return a;
    }

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Карликовый (без мачты) — по уроку caredenis.ru les03 и РУ-30-80 стр. 9:
    /// выходной / маршрутный с бокового пути и маневровый. Мачтовые: на главных путях, входные, проходные,
    /// предупредительные, заградительные и повторительные, а также маневровый на выходе из тупика / вытяжки.</summary>
    public static bool IsDwarf(Signal s, Tile t) => s.Kind switch
    {
        "exit" or "route" => !IsMainTrack(t?.Sec),
        "shunt" => Core.GuessSecType(t?.Sec) != "dead",
        _ => false,
    };

    /// <summary>Сколько ламп рисовать.</summary>
    public static int LampCount(Signal s, Tile t) => Head(s, t)?.Length ?? 1;

    /// <summary>Зажечь огни на головке: каждый огонь — на свободную лампу его цвета, начиная с дальней от мачты
    /// (один жёлтый — верхний; у двух жёлтых первый в списке — верхний).</summary>
    static Aspect OnHead(Lamp[] head, string text, bool perm, bool div, params LampLit[] lit)
    {
        var a = new LampLit[head.Length];
        for (int i = 0; i < a.Length; i++) a[i] = new LampLit(Lamp.Off);
        var used = new bool[head.Length];
        foreach (var l in lit)
            for (int i = head.Length - 1; i >= 0; i--)
                if (!used[i] && head[i] == l.Color) { a[i] = l; used[i] = true; break; }
        return new Aspect { Lamps = a, Text = text, Permissive = perm, Diverging = div };
    }

    static Aspect RedOn(Lamp[] head) => head == null ? Aspect.Red() : OnHead(head, "красный", false, false, new LampLit(Lamp.Red));
    /// <summary>Закрыт: красный, а у повторителя горочного в парке приёма — синий.</summary>
    static Aspect ClosedOn(Lamp[] head) => head.Contains(Lamp.Blue) ? OnHead(head, "синий", false, false, new LampLit(Lamp.Blue)) : RedOn(head);

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
        var set = s.Sig.Kind == "warning" ? WarningMain : s.Sig.Kind == "repeater" ? RepeaterMain : s.Sig.Kind == "humprep" ? HumpMain : NextMain;
        return Go(st, s, e => set.Contains(e.Sig.Kind), noControl: noControl).Sig;
    }

    /// <summary>[ДОБАВЛЕНО 09.11.2026] Огонь ромбом: заградительный, повторительный и предупредительный к заградительному
    /// (ГОСТ 2.749-84, табл. 5, п. 7–9; РУ-30-80 стр. 49).</summary>
    public static bool IsDiamond(Station st, SignalEntry s) =>
        s.Sig.Kind is "barrier" or "repeater"
        || (s.Sig.Kind == "warning" && MainAhead(st, s, null)?.Sig.Kind == "barrier");

    /// <summary>Переезд впереди заградительного (не дальше 60 клеток): клетка переезда или null.</summary>
    public static string CrossingAhead(Station st, SignalEntry s) =>
        Go(st, s, null, (_, t) => t.T == "crossing", maxSteps: 60).StopTile;

    // ---------------------------------------------------------------------
    //  ПОКАЗАНИЯ
    // ---------------------------------------------------------------------
    /// <summary>Цвет ламп в редакторе (не в «Испытании»): каким огнём светофор «говорит».</summary>
    public static Aspect Static(Signal s, Tile t) => s.Kind switch
    {
        "entry" or "exit" or "route" => RedOn(Head(s, t)),
        "hump" or "humprep" => ClosedOn(Head(s, t)),
        "shunt" => s.IsRedShunt ? Aspect.Make("красный", false, 1, new LampLit(Lamp.Red)) : Aspect.Make("синий", false, 1, new LampLit(Lamp.Blue)),
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
            if (!visiting.Add(id)) return RedOn(Head(s.Sig, s.Tile));    // замкнутый круг светофоров — считаем закрытым
            a = Calc(s);
            visiting.Remove(id);
            memo[id] = a;
            return a;
        }

        Walk NextMainWalk(SignalEntry s) => Go(st, s, e => NextMain.Contains(e.Sig.Kind), noControl: w.NoControl);

        Aspect Calc(SignalEntry s)
        {
            var id = Id(s);
            w.OpenRoutes.TryGetValue(id, out var route);
            // [ДОБАВЛЕНО 09.11.2026] только чтение: показание присылает другая станция; в редакторе связи нет —
            // светофор погашен, как в программах станций при отсутствии данных от соседа (DispatchMirror)
            if (s.Sig.IsReadOnly)
            {
                var a = Aspect.Dark(Head(s.Sig, s.Tile)?.Length ?? 1);
                a.Text = $"погашен — показание присылает другая станция (id {s.Sig.Id})";
                return a;
            }
            switch (s.Sig.Kind)
            {
                case "entry":
                case "exit":
                case "route":
                {
                    var head = Head(s.Sig, s.Tile);
                    if (route == null) return RedOn(head);
                    if (route.Shunt) return OnHead(head, "лунно-белый", false, false, new LampLit(Lamp.White));
                    var nx = NextMainWalk(s);
                    var na = nx.Sig != null ? Get(nx.Sig) : null;
                    bool nextOpen = na != null && na.Permissive;
                    // два жёлтых: входной — при любом отклонении; выходной / маршрутный — только с главного пути (РУ-30-80 стр. 9, 14)
                    bool twoYellow = route.Diverging && (s.Sig.Kind == "entry" || IsMainTrack(s.Tile.Sec));
                    if (twoYellow)
                        return nextOpen
                            ? OnHead(head, "два жёлтых, верхний мигающий", true, true, new LampLit(Lamp.Yellow, true), new LampLit(Lamp.Yellow))
                            : OnHead(head, "два жёлтых", true, true, new LampLit(Lamp.Yellow), new LampLit(Lamp.Yellow));
                    // следующий показывает два жёлтых — у нас жёлтый мигающий (РУ-30-80 стр. 9, 14, 20)
                    if (nextOpen && na.Diverging)
                        return OnHead(head, "жёлтый мигающий (следующий — два жёлтых)", true, false, new LampLit(Lamp.Yellow, true));
                    return nextOpen
                        ? OnHead(head, "зелёный", true, false, new LampLit(Lamp.Green))
                        : OnHead(head, "жёлтый", true, false, new LampLit(Lamp.Yellow));
                }
                case "shunt":
                    return route != null
                        ? Aspect.Make("лунно-белый", false, 1, new LampLit(Lamp.White))
                        : s.Sig.IsRedShunt ? Aspect.Make("красный", false, 1, new LampLit(Lamp.Red))     // [17.11.2026] РК п. 98
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
                    if (ma.Diverging) return Aspect.Make("жёлтый мигающий (основной — два жёлтых)", true, 1, new LampLit(Lamp.Yellow, true));
                    return Aspect.Make("зелёный (основной открыт)", true, 1, new LampLit(Lamp.Green));
                }
                case "repeater":
                {
                    var main = MainAhead(st, s, w.NoControl);
                    if (main != null && Get(main).Permissive) return Aspect.Make("зелёный (выходной открыт)", true, 1, new LampLit(Lamp.Green));
                    return Aspect.Dark();
                }
                case "hump":
                {
                    // [ДОБАВЛЕНО 13.11.2026] режим задаёт оператор горки; маневровый маршрут от Г — лунно-белый
                    var head = Head(s.Sig, s.Tile);
                    if (route != null) return OnHead(head, "лунно-белый (маневры)", false, false, new LampLit(Lamp.White));
                    var m = HumpModes[w.HumpMode.TryGetValue(id, out var mi) && mi >= 0 && mi < HumpModes.Length ? mi : 0];
                    var a = OnHead(head, m.Text, m.Perm, false, m.Lit.Select(l => new LampLit(l)).ToArray());
                    a.Indicator = m.Indicator;
                    return a;
                }
                case "humprep":
                {
                    // повторяет горочный впереди (в парке приёма — синий вместо красного)
                    var head = Head(s.Sig, s.Tile);
                    var main = MainAhead(st, s, w.NoControl);
                    if (main == null) { var d = Aspect.Dark(head.Length); d.Text = "не горит — впереди нет горочного"; return d; }
                    var ma = Get(main);
                    var lit = ma.Lamps.Where(l => l.Color != Lamp.Off)
                                      .Select(l => l.Color == Lamp.Red && head.Contains(Lamp.Blue) ? l with { Color = Lamp.Blue } : l).ToArray();
                    var a = OnHead(head, $"как {main.Sig.Name}: {ma.Text}", ma.Permissive, false, lit);
                    a.Indicator = ma.Indicator;
                    return a;
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
        // [ДОБАВЛЕНО 17.11.2026] маршрутный указатель горит, пока маршрут от светофора открыт (показание разрешающее или лунно-белый)
        foreach (var s in all)
        {
            if (!s.Sig.HasRouteInd || !w.OpenRoutes.TryGetValue(Id(s), out var r)) continue;
            var a = memo[Id(s)];
            bool open = a.Permissive || a.Lamps.Any(l => l.Color == Lamp.White);
            var txt = open ? RouteIndText(st, s, r) : null;
            if (txt == null) continue;
            // показание может быть общим объектом (Aspect.Make) — указатель пишем в копию
            memo[Id(s)] = new Aspect { Lamps = a.Lamps, Text = a.Text + $", указатель «{txt}»", Permissive = a.Permissive, Diverging = a.Diverging,
                                       Indicator = a.Indicator, RouteText = txt, RouteColor = RouteIndColor(s.Sig.Kind) };
        }
        return memo;
    }
}
