namespace StationEditor.Model;

// =====================================================================
//  [ДОБАВЛЕНО 10.11.2026, ПЕРЕДЕЛАНО 10.11.2026] ОБОЗНАЧЕНИЯ СВЕТОФОРОВ
//
//  Имя даётся только НОВОМУ светофору — в момент, когда его поставили (остальные не трогаются).
//  Перенумеровать всё — кнопкой в окне «Обозначения…» (закреплённые, Signal.Fixed, не меняются).
//
//  Буква Н / Ч нового светофора — прежде всего от входного: входной того же направления — его буква (стоит входной Н →,
//  выходной → тоже Н), есть только встречный входной — противоположная. Входных нет —
//  от ближайшего светофора с известной буквой: того же направления — та же буква,
//  встречного — противоположная (рядом с Ч→ новый ← будет Н). Светофоров ещё нет — из настройки станции «→ = Н / Ч».
//
//  Имя — по шаблону вида (окно «Обозначения…»): {Б} — буква Н/Ч, {П} — номер пути из названия участка,
//  {N} — порядковый номер, {О} — имя основного светофора впереди. По умолчанию (урок caredenis.ru les03, РУ-30-80 стр. 89–90):
//    входной {Б} (второй того же направления — +Д: НД / ЧД), выходной {Б}{П}, маршрутный {Б}М{П}, маневровый М{N}
//    (нечётные в горловине входного Н, чётные — Ч), проходной {N} (у входного, к которому идёт поезд, 1 / 2, дальше 3, 5… /
//    4, 6…), предупредительный и повторительный П{О}, заградительный З{N} (нечётные / чётные по направлению), прикрытия {Б}П{П}.
//
//  Вид нового светофора по умолчанию — по типу участка: путь → выходной; перегон → входной, если впереди станция,
//  иначе проходной; тупик, стрелочный участок, участок между светофорами → маневровый (меняется в «Обозначениях…»).
// =====================================================================

public static class Naming
{
    // [ИЗМЕНЕНО 10.11.2026] цифра пути — у входного {Б}{П} (на перегоне — просто Н, на пути 1П — Н1), выходного, маршрутного.
    // [ИЗМЕНЕНО 11.11.2026] маневровый — М{N}: порядковый, нечётный / чётный по направлению самого светофора (Н → М1, М3…;
    // Ч → М2, М4…); «по горловине» (урок caredenis.ru les03) — переключатель в «Обозначениях…» (NamingSettings.ShuntParity)
    public static readonly (string Kind, string Template)[] DefaultTemplates =
    {
        ("entry", "{Б}{П}"), ("exit", "{Б}{П}"), ("route", "{Б}М{П}"), ("shunt", "М{N}"), ("block", "{N}"),
        ("warning", "П{О}"), ("repeater", "П{О}"), ("barrier", "З{N}"), ("cover", "{Б}П{П}"),
    };

    /// <summary>"auto-line" — входной, если впереди станция, иначе проходной; "" — вид из панели.</summary>
    public static readonly (string Sec, string Kind)[] DefaultKindBySec =
    {
        ("path", "exit"), ("line", "auto-line"), ("dead", "shunt"), ("switch", "shunt"), ("plain", "shunt"),
    };

    public static string DefaultTemplate(string kind) =>
        DefaultTemplates.FirstOrDefault(t => t.Kind == kind).Template ?? "{Б}{N}";

    public static string Template(Station st, string kind) =>
        st.Naming?.Templates != null && st.Naming.Templates.TryGetValue(kind, out var t) && !string.IsNullOrWhiteSpace(t)
            ? t.Trim() : DefaultTemplate(kind);

    public static string KindForSec(Station st, string secType)
    {
        if (st.Naming?.KindBySec != null && st.Naming.KindBySec.TryGetValue(secType ?? "", out var k) && k != null) return k;
        return DefaultKindBySec.FirstOrDefault(p => p.Sec == secType).Kind ?? "";
    }

    /// <summary>Подставить в шаблон: {Б} буква, {П} номер пути, {N} номер, {О} основной (можно и латиницей: {B}, {P}, {O}).</summary>
    public static string Render(string tpl, string b, string p, string n, string o)
    {
        string r = tpl;
        foreach (var (keys, v) in new[] { (new[] { "{Б}", "{B}" }, b), (new[] { "{П}", "{P}" }, p), (new[] { "{N}" }, n), (new[] { "{О}", "{O}" }, o) })
            foreach (var key in keys) r = r.Replace(key, v ?? "", StringComparison.OrdinalIgnoreCase);
        return r;
    }

    static bool Has(string tpl, string token) =>
        tpl.Contains("{" + token + "}", StringComparison.OrdinalIgnoreCase)
        || token == "Б" && tpl.Contains("{B}", StringComparison.OrdinalIgnoreCase)
        || token == "П" && tpl.Contains("{P}", StringComparison.OrdinalIgnoreCase)
        || token == "О" && tpl.Contains("{O}", StringComparison.OrdinalIgnoreCase);

    static string Opp(string l) => l == "Н" ? "Ч" : "Н";

    /// <summary>Буква, которую видно по имени светофора (Н… / Ч…; у проходного — по чётности номера); null — не видно.</summary>
    public static string LetterOf(Signal s)
    {
        var n = s.Name ?? "";
        if (s.Kind == "block") return int.TryParse(n, out int v) && v > 0 ? (v % 2 == 1 ? "Н" : "Ч") : null;
        if (s.Kind is "shunt" or "barrier" or "warning" or "repeater") return null;
        return n.Length > 0 && (n[0] == 'Н' || n[0] == 'Ч') ? n[0].ToString() : null;
    }

    /// <summary>Буква для светофора направления dir в клетке key: от ближайшего светофора с известной буквой,
    /// иначе из настройки станции. (except — сам светофор, если он уже стоит.)</summary>
    public static string LetterFor(Station st, string key, string dir, SignalEntry except = null)
    {
        var (x, y) = Core.Parse(key);
        double Dist(SignalEntry s) { var (sx, sy) = Core.Parse(s.Key); return Math.Sqrt((sx - x) * (sx - x) + (sy - y) * (sy - y)); }
        var known = Core.AllSignals(st)
            .Where(s => !(except != null && s.Key == except.Key && s.Idx == except.Idx) && LetterOf(s.Sig) != null).ToList();

        // [ДОБАВЛЕНО 10.11.2026] входной задаёт букву своего направления: стоит входной Н → — все светофоры → тоже Н
        // (ближайший входной того же направления; есть только встречный — противоположная буква)
        var entries = known.Where(s => s.Sig.Kind == "entry").ToList();
        var sameEntry = entries.Where(s => s.Sig.Dir == dir).OrderBy(Dist).FirstOrDefault();
        if (sameEntry != null) return LetterOf(sameEntry.Sig);
        var oppEntry = entries.OrderBy(Dist).FirstOrDefault();
        if (oppEntry != null) return Opp(LetterOf(oppEntry.Sig));

        // входных нет — по ближайшему светофору с известной буквой
        var best = known.OrderBy(Dist).FirstOrDefault();
        if (best == null) return Core.DirLetter(st, dir);
        var bestL = LetterOf(best.Sig);
        return best.Sig.Dir == dir ? bestL : Opp(bestL);
    }

    /// <summary>Какое направление (→) у станции, судя по уже названным светофорам: true — → нечётное (Н); null — не понять.</summary>
    public static bool? InferRightOdd(Station st)
    {
        int odd = 0, even = 0;
        foreach (var s in Core.AllSignals(st))
        {
            var l = LetterOf(s.Sig);
            if (l == null) continue;
            bool rightIsN = (s.Sig.Dir == "R") == (l == "Н");
            if (rightIsN) odd++; else even++;
        }
        return odd == even ? null : odd > even;
    }

    // ---------------------------------------------------------------------
    //  ВИД НОВОГО СВЕТОФОРА
    // ---------------------------------------------------------------------
    /// <summary>Вид светофора по участку клетки; null — брать вид из панели.</summary>
    public static string DefaultKind(Station st, string key, string dir)
    {
        if (!st.Tiles.TryGetValue(key, out var t)) return null;
        string secType = string.IsNullOrEmpty(t.Sec) ? "" : Core.SecType(st, t.Sec);
        // участок назван как путь (1П, 3П, IIП) и тип ему не задан вручную — это путь, даже если на нём есть стрелки
        bool explicitType = st.Sections.TryGetValue(t.Sec ?? "", out var si) && !string.IsNullOrEmpty(si.Type) && si.Type != "auto";
        if (!explicitType && Core.GuessSecType(t.Sec) == "path") secType = "path";
        var k = KindForSec(st, secType);
        if (string.IsNullOrEmpty(k)) return null;
        if (k != "auto-line") return Core.SigKindName(k) != null ? k : null;
        // перегон: впереди (по ходу) станция — входной; но если впереди уже стоит входной того же направления — проходной
        var probe = new SignalEntry { Key = key, Idx = -1, Tile = t, Sig = new Signal { Dir = dir, Kind = "block" } };
        var w = Aspects.Go(st, probe, e => e.Sig.Kind == "entry",
            (_, nt) => nt.T != "exit" && !string.IsNullOrEmpty(nt.Sec) && Core.SecType(st, nt.Sec) != "line", maxSteps: 60);
        return w.Sig == null && w.StopTile != null ? "entry" : "block";
    }

    // ---------------------------------------------------------------------
    //  ИМЯ НОВОГО СВЕТОФОРА
    // ---------------------------------------------------------------------
    /// <summary>[ИЗМЕНЕНО 10.11.2026] Дать имя только что поставленному светофору s (он уже на схеме) и запомнить, как оно собрано
    /// (буква, порядковый номер / своя цифра) — чтобы потом пересобирать его само. Возвращает пояснение, если имя запасное.</summary>
    public static string NameNew(Station st, SignalEntry s)
    {
        var used = new HashSet<string>(Core.AllSignals(st)
            .Where(q => !(q.Key == s.Key && q.Idx == s.Idx) && !string.IsNullOrEmpty(q.Sig.Name)).Select(q => q.Sig.Name));
        var sig = s.Sig;
        sig.Num = null; sig.Seq = null; sig.Fixed = null;
        if (sig.Kind == "other") { sig.Ltr = null; sig.Name = Free(n => "С" + n, used, 1, 1); return null; }
        string kind = sig.Kind, tpl = Template(st, kind);
        sig.Ltr = LetterFor(st, s.Key, sig.Dir, s);
        string track = Has(tpl, "П") ? Core.TrackNo(s.Tile.Sec) : null;
        string note = null;
        if (Has(tpl, "О") && string.IsNullOrEmpty(Aspects.MainAhead(st, s, null)?.Sig.Name))
            note = "впереди нет основного светофора — имя запасное";

        // входной: цифра — только от пути; второй того же направления — +Д (НД / ЧД)
        if (kind == "entry")
        {
            Rebuild(st, s);
            if (used.Contains(sig.Name))
            {
                var baseName = sig.Name;
                var name = Free(n => baseName + "Д" + (n == 1 ? "" : n.ToString()), used, 1, 1);
                sig.Num = (track ?? "") + name.Substring(baseName.Length);
                Rebuild(st, s);
            }
            return note;
        }
        // по номеру пути
        if (track != null)
        {
            Rebuild(st, s);
            if (!used.Contains(sig.Name)) return note;
            if (kind is "exit" or "route" or "cover") return $"имя «{sig.Name}» уже есть на схеме — поменяйте цифру в «№» или имя";
            // маневровый и др.: на этом пути уже есть такой — порядковый номер, как вне пути
            note = $"имя «{sig.Name}» уже есть — дан порядковый номер";
        }
        else if (Has(tpl, "П") && kind is "exit" or "route" or "cover")
            note = $"у участка «{(string.IsNullOrEmpty(s.Tile.Sec) ? "—" : s.Tile.Sec)}» нет номера пути (3, 3П, IIП) — цифра порядковая";
        if (!Has(tpl, "П") && !Has(tpl, "N") && !Has(tpl, "О")) { Rebuild(st, s); return note; }

        // порядковый номер: чётность по смыслу вида
        int start = 1, step = 1;
        if (kind == "shunt") { start = (ShuntByNeck(st) ? NeckOdd(st, s) : sig.Ltr == "Н") ? 1 : 2; step = 2; }    // [11.11.2026]
        else if (kind is "block" or "barrier") { start = sig.Ltr == "Н" ? 1 : 2; step = 2; }
        if (kind == "block")
        {
            int ahead = 0;
            Aspects.Go(st, s, e => { if (e.Sig.Kind == "block") ahead++; return e.Sig.Kind == "entry"; });
            start += 2 * ahead;
        }
        int seq = start;
        for (int i = 0; i < 10000; i++, seq += step)
        {
            if (track != null) sig.Num = seq.ToString(); else sig.Seq = seq;
            Rebuild(st, s);
            if (!used.Contains(sig.Name)) break;
        }
        return note;
    }

    /// <summary>Какой была бы цифра без своей (Num): от пути или порядковая — для подсказки в поле «№».</summary>
    public static string AutoNumber(Station st, SignalEntry s, out string from)
    {
        var keep = s.Sig.Num;
        s.Sig.Num = null;
        try { return Number(st, s, out from); }
        finally { s.Sig.Num = keep; }
    }

    /// <summary>Цифра в имени: своя (Num) → от названия пути (если в шаблоне есть {П}) → порядковая (Seq).</summary>
    public static string Number(Station st, SignalEntry s, out string from)
    {
        if (!string.IsNullOrEmpty(s.Sig.Num)) { from = "своя"; return s.Sig.Num; }
        var tpl = Template(st, s.Sig.Kind);
        var track = Has(tpl, "П") ? Core.TrackNo(s.Tile.Sec) : null;
        if (track != null) { from = "путь"; return track; }
        if (s.Sig.Seq is int q) { from = "порядковая"; return q.ToString(); }
        // [11.11.2026] светофор назван по номеру пути, а шаблон его вида теперь без {П} (маневровый был М{П}) —
        // оставляем номер пути, чтобы имя не потерялось
        var oldTrack = Core.TrackNo(s.Tile.Sec);
        if (oldTrack != null) { from = "путь"; return oldTrack; }
        from = "";
        return "";
    }

    /// <summary>[ДОБАВЛЕНО 11.11.2026] Чётность номеров маневровых: true — по горловине (нечётные у входного Н),
    /// false (по умолчанию) — по направлению самого светофора (буква Н — нечётные, Ч — чётные).</summary>
    public static bool ShuntByNeck(Station st) => st.Naming?.ShuntParity == "neck";

    /// <summary>Собрать имя светофора заново по шаблону (если он «собранный»: есть буква Ltr и имя не закреплено).
    /// true — имя поменялось.</summary>
    public static bool Rebuild(Station st, SignalEntry s)
    {
        var sig = s.Sig;
        if (sig.IsFixed || string.IsNullOrEmpty(sig.Ltr) || sig.Kind == "other") return false;
        string tpl = Template(st, sig.Kind), num = Number(st, s, out _);
        string o = Has(tpl, "О") ? Aspects.MainAhead(st, s, null)?.Sig.Name : null;
        if (string.IsNullOrEmpty(o)) o = num;
        var name = Render(tpl, sig.Ltr, num, num, o);
        if (name == sig.Name) return false;
        sig.Name = name;
        return true;
    }

    /// <summary>Пересобрать имена всех «собранных» светофоров (после любого изменения: переименовали путь, поменяли шаблон,
    /// цифру, основной светофор). Возвращает, сколько имён поменялось.</summary>
    public static int RebuildAll(Station st)
    {
        int n = 0;
        // сначала основные, потом предупредительные / повторительные (их имя — по основному)
        foreach (var s in Core.AllSignals(st).OrderBy(s => s.Sig.Kind is "warning" or "repeater" ? 1 : 0))
            if (Rebuild(st, s)) n++;
        return n;
    }

    /// <summary>Запомнить, как собрано уже данное имя (после «Перенумеровать все» или когда сняли «имя вручную»):
    /// буква — по имени (или по направлению), цифра — от пути, иначе порядковая / своя, чтобы имя осталось тем же.</summary>
    public static void Bind(Station st, SignalEntry s)
    {
        var sig = s.Sig;
        if (sig.Kind == "other" || string.IsNullOrEmpty(sig.Name)) return;
        string want = sig.Name;
        sig.Ltr = LetterOf(sig) ?? Core.DirLetter(st, sig.Dir);
        sig.Num = null; sig.Seq = null; sig.Fixed = null;
        Rebuild(st, s);
        if (sig.Name == want) return;
        var m = System.Text.RegularExpressions.Regex.Match(want, @"(\d+|Д\d*|[IVX]+[А-Я]?)$");
        if (m.Success)
        {
            if (int.TryParse(m.Value, out int q)) { sig.Seq = q; Rebuild(st, s); if (sig.Name == want) return; sig.Seq = null; }
            sig.Num = m.Value;
            Rebuild(st, s);
            if (sig.Name == want) return;
        }
        // по шаблону это имя не собрать — оставляем как есть и закрепляем
        sig.Ltr = null; sig.Num = null; sig.Seq = null;
        sig.Name = want;
        sig.Fixed = true;
    }

    static string Free(Func<int, string> make, HashSet<string> used, int start, int step)
    {
        for (int n = start, i = 0; i < 10000; n += step, i++)
        {
            var name = make(n);
            if (!used.Contains(name)) return name;
        }
        return make(start);
    }

    /// <summary>Горловина маневрового: нечётная — у входного с буквой Н (по ближайшему входному);
    /// входных нет — по букве самого светофора.</summary>
    static bool NeckOdd(Station st, SignalEntry s)
    {
        int x = Core.Parse(s.Key).X;
        var e = Core.AllSignals(st).Where(q => q.Sig.Kind == "entry" && !(q.Key == s.Key && q.Idx == s.Idx))
            .OrderBy(q => Math.Abs(Core.Parse(q.Key).X - x)).FirstOrDefault();
        if (e == null) return LetterFor(st, s.Key, s.Sig.Dir, s) == "Н";
        string EL(SignalEntry q) => LetterOf(q.Sig) ?? Core.DirLetter(st, q.Sig.Dir);
        bool odd = EL(e) == "Н";
        // входные только с одной стороны (другая — тупики): дальняя от них половина станции — противоположная горловина
        // (так же считает «Перенумеровать все»)
        var entries = Core.AllSignals(st).Where(q => q.Sig.Kind == "entry" && !(q.Key == s.Key && q.Idx == s.Idx)).ToList();
        if (entries.Select(EL).Distinct().Count() == 1)
        {
            var xs = st.Tiles.Where(p => p.Value.T != "exit" && Core.SecType(st, p.Value.Sec) != "line").Select(p => Core.Parse(p.Key).X).ToList();
            if (xs.Count > 0)
            {
                double axis = (xs.Min() + xs.Max()) / 2.0, ex = entries.Average(q => Core.Parse(q.Key).X);
                if ((x < axis) != (ex < axis)) odd = !odd;
            }
        }
        return odd;
    }

    // ---------------------------------------------------------------------
    //  ПЕРЕНУМЕРОВАТЬ ВСЁ (кнопка в «Обозначениях…»): буква — из настройки станции
    // ---------------------------------------------------------------------
    public sealed class Change
    {
        public string Key;
        public int Idx;
        public string Old, New;
    }

    /// <summary>Имена всех незакреплённых светофоров по правилам: "клетка#номер" → имя.</summary>
    public static Dictionary<string, string> Compute(Station st)
    {
        var all = Core.AllSignals(st);
        var res = new Dictionary<string, string>();
        string Id(SignalEntry s) => s.Key + "#" + s.Idx;
        string L(SignalEntry s) => Core.DirLetter(st, s.Sig.Dir);
        int X(SignalEntry s) => Core.Parse(s.Key).X;
        int Y(SignalEntry s) => Core.Parse(s.Key).Y;
        bool Auto(SignalEntry s) => !s.Sig.IsFixed && s.Sig.Kind != "other";
        string T(SignalEntry s) => Template(st, s.Sig.Kind);

        var used = new HashSet<string>(all.Where(s => !Auto(s) && !string.IsNullOrEmpty(s.Sig.Name)).Select(s => s.Sig.Name));
        string Take(string name) { used.Add(name); return name; }
        string Seq(Func<int, string> make, int start = 1, int step = 1) => Take(Free(make, used, start, step));

        // ---- входные: основной — на «правильном» пути (правостороннее движение: → по нижнему, ← по верхнему) ----
        var entries = all.Where(s => s.Sig.Kind == "entry").ToList();
        foreach (var grp in entries.GroupBy(L))
        {
            var list = grp.ToList();
            string Base(SignalEntry s) { var tr = Core.TrackNo(s.Tile.Sec) ?? ""; return Render(T(s), grp.Key, tr, tr, ""); }
            var main = list.FirstOrDefault(s => !Auto(s) && s.Sig.Name == Base(s))
                       ?? list.OrderBy(s => s.Sig.Dir == "R" ? -Y(s) : Y(s)).ThenBy(X).First();
            string baseName = Base(main);
            if (Auto(main)) res[Id(main)] = used.Contains(baseName) ? Seq(n => baseName + n) : Take(baseName);
            foreach (var s in list.Where(s => s != main && Auto(s)).OrderBy(s => Math.Abs(X(s) - X(main))).ThenBy(X))
                res[Id(s)] = Seq(n => baseName + "Д" + (n == 1 ? "" : n.ToString()));
        }

        // горловина: по ближайшему входному. Входные только с одной стороны (другая — тупики, как у Алгабаса):
        // дальняя от них половина станции — противоположная горловина, её «край» — край станции.
        var stationXs = st.Tiles.Where(p => p.Value.T != "exit" && Core.SecType(st, p.Value.Sec) != "line")
                                .Select(p => Core.Parse(p.Key).X).ToList();
        int sx0 = stationXs.Count > 0 ? stationXs.Min() : 0, sx1 = stationXs.Count > 0 ? stationXs.Max() : 0;
        bool oneSided = entries.Count > 0 && entries.Select(L).Distinct().Count() == 1;
        (bool Odd, int EntryX)? Neck(SignalEntry s)
        {
            var e = entries.OrderBy(q => Math.Abs(X(q) - X(s))).FirstOrDefault();
            if (e == null) return null;
            if (oneSided)
            {
                double axis = (sx0 + sx1) / 2.0, ex = entries.Average(X);
                bool entrySide = (X(s) < axis) == (ex < axis);
                if (!entrySide) return (L(e) != "Н", ex < axis ? sx1 : sx0);
            }
            return (L(e) == "Н", X(e));
        }

        // ---- выходные, маршрутные, прикрытия — по номеру пути ----
        foreach (var s in all.Where(s => Auto(s) && s.Sig.Kind is "exit" or "route" or "cover").OrderBy(X).ThenBy(Y))
        {
            string track = Core.TrackNo(s.Tile.Sec), tpl = T(s), b = L(s);
            string name = track != null ? Render(tpl, b, track, track, "") : null;
            res[Id(s)] = name != null && !used.Contains(name) ? Take(name) : Seq(n => Render(tpl, b, n.ToString(), n.ToString(), ""));
        }

        // ---- маневровые: нечётные / чётные — по направлению светофора (по умолчанию) или по горловине; от края к оси станции ----
        bool byNeck = ShuntByNeck(st);
        var shunts = all.Where(s => Auto(s) && s.Sig.Kind == "shunt")
            .Select(s => (s, neck: Neck(s)))
            .Select(p => (p.s, odd: byNeck ? p.neck?.Odd ?? L(p.s) == "Н" : L(p.s) == "Н",
                          dist: p.neck is { } nk ? Math.Abs(X(p.s) - nk.EntryX) : X(p.s)))
            .ToList();
        // на пути (шаблон с {П}) — номер пути (М3), если такое имя свободно
        foreach (var p in shunts.ToList())
        {
            var tr = Has(T(p.s), "П") ? Core.TrackNo(p.s.Tile.Sec) : null;
            if (tr == null) continue;
            var name = Render(T(p.s), L(p.s), tr, tr, "");
            if (used.Contains(name)) continue;
            res[Id(p.s)] = Take(name);
            shunts.Remove(p);
        }
        foreach (var odd in new[] { true, false })
            foreach (var p in shunts.Where(p => p.odd == odd).OrderBy(p => p.dist).ThenBy(p => Y(p.s)))
                res[Id(p.s)] = Seq(n => Render(T(p.s), L(p.s), n.ToString(), n.ToString(), ""), odd ? 1 : 2, 2);

        // ---- проходные: сколько проходных того же направления впереди до входного или края схемы ----
        foreach (var s in all.Where(s => Auto(s) && s.Sig.Kind == "block"))
        {
            int ahead = 0;
            Aspects.Go(st, s, e => { if (e.Sig.Kind == "block") ahead++; return e.Sig.Kind == "entry"; });
            bool odd = L(s) == "Н";
            res[Id(s)] = Seq(n => Render(T(s), L(s), n.ToString(), n.ToString(), ""), 2 * (ahead + 1) - (odd ? 1 : 0), 2);
        }

        // ---- заградительные: нечётные / чётные по направлению, по порядку хода ----
        foreach (var odd in new[] { true, false })
            foreach (var s in all.Where(s => Auto(s) && s.Sig.Kind == "barrier" && (L(s) == "Н") == odd)
                                 .OrderBy(s => s.Sig.Dir == "R" ? X(s) : -X(s)).ThenBy(Y))
                res[Id(s)] = Seq(n => Render(T(s), L(s), n.ToString(), n.ToString(), ""), odd ? 1 : 2, 2);

        // ---- предупредительные и повторительные — по имени светофора впереди (уже посчитанному) ----
        foreach (var s in all.Where(s => Auto(s) && s.Sig.Kind is "warning" or "repeater").OrderBy(X))
        {
            var main = Aspects.MainAhead(st, s, null);
            string mainName = main == null ? null : res.TryGetValue(Id(main), out var mn) ? mn : main.Sig.Name;
            string name = !string.IsNullOrEmpty(mainName) ? Render(T(s), L(s), "", "", mainName) : null;
            res[Id(s)] = name != null && !used.Contains(name) ? Take(name) : Seq(n => Render(T(s), L(s), n.ToString(), n.ToString(), n.ToString()));
        }
        return res;
    }

    /// <summary>Что изменится, если перенумеровать (без изменения схемы).</summary>
    public static List<Change> Preview(Station st)
    {
        var names = Compute(st);
        var list = new List<Change>();
        foreach (var s in Core.AllSignals(st))
            if (names.TryGetValue(s.Key + "#" + s.Idx, out var n) && n != s.Sig.Name)
                list.Add(new Change { Key = s.Key, Idx = s.Idx, Old = s.Sig.Name, New = n });
        return list;
    }

    /// <summary>Перенумеровать незакреплённые светофоры. Возвращает, что поменялось.
    /// [10.11.2026] после этого имена «собраны» (Bind) — дальше следят за названием пути сами.</summary>
    public static List<Change> Apply(Station st)
    {
        var ch = Preview(st);
        foreach (var c in ch) st.Tiles[c.Key].Sigs[c.Idx].Name = c.New;
        foreach (var s in Core.AllSignals(st).Where(s => !s.Sig.IsFixed && s.Sig.Kind != "other")) Bind(st, s);
        return ch;
    }
}
