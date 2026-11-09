using System.Text;
using System.Text.RegularExpressions;
using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: ЧТО ПРОИСХОДИТ НА СТАНЦИИ  [ДОБАВЛЕНО 28.10.2026, перенесено из Station LAB: StationContext, StationGuide, LabIntegration]
//  • журнал программы (что делал студент, отказы) и объект, на который студент показал мышью;
//  • описание светофора / стрелки / участка / перегона по модели программы — на двух языках;
//  • НОВОЕ: как задать конкретный маршрут («маршрут Н на путь 3», «Н-нан I жолға маршрут») — шаги по модели:
//    что нажать, какие стрелки переведутся, какие участки должны быть свободны и можно ли задать его прямо сейчас.
//  Логику станции не меняет: только читает ModelHost.Model и журнал ModelHost.Log.
// =====================================================================

public static class AiStation
{
    static Interlocking IL => ModelHost.Model?.IL;

    // =================================================================
    //  Журнал и «показал мышью»
    // =================================================================
    static readonly List<(DateTime t, string text, bool refusal)> journal = new();
    static readonly Dictionary<string, (object o, DateTime t)> pointed = new();
    static readonly object lk = new();
    static bool installed;

    /// <summary>Программа отказала студенту (красная строка журнала). Может прийти из любого потока.</summary>
    public static event Action<string> Refused;

    public static void Install()
    {
        if (installed) return;
        installed = true;
        ModelHost.Log += (msg, kind) =>
        {
            if (kind == LogKind.Train) return;
            bool refusal = kind == LogKind.Refuse;
            if (refusal) msg = Regex.Replace(msg, @"^ОТКАЗ\s*:?\s*", "", RegexOptions.IgnoreCase);
            lock (lk)
            {
                journal.Add((DateTime.Now, msg, refusal));
                if (journal.Count > 300) journal.RemoveAt(0);
            }
            if (refusal) try { Refused?.Invoke(msg); } catch { }
        };
        RailSchemePanel.Pointed += o => { lock (lk) pointed[o.GetType().Name] = (o, DateTime.Now); };
    }

    public static List<string> LastActions(int n)
    {
        lock (lk) return journal.Skip(Math.Max(0, journal.Count - n)).Select(e => $"{e.t:HH:mm:ss} {(e.refusal ? "[отказ] " : "")}{e.text}").ToList();
    }

    public static string RecentRefusal(TimeSpan maxAge)
    {
        lock (lk)
            for (int i = journal.Count - 1; i >= 0; i--)
            {
                if (DateTime.Now - journal[i].t > maxAge) return null;
                if (journal[i].refusal) return journal[i].text;
            }
        return null;
    }

    /// <summary>Объект, на который студент недавно навёл мышь (задержался) или кликнул: RailSignal, RailSwitch или TrackSection.</summary>
    public static object LastPointed(TimeSpan maxAge, Type only = null)
    {
        lock (lk)
            return pointed.Values.Where(p => DateTime.Now - p.t <= maxAge && (only == null || p.o.GetType() == only))
                          .OrderByDescending(p => p.t).Select(p => p.o).FirstOrDefault();
    }

    /// <summary>Состояние станции одним текстом (для нейросети и разбора ошибок).</summary>
    public static string State()
    {
        var il = IL;
        if (il == null) return "";
        var sb = new StringBuilder();
        var busy = il.Sections.Values.Where(s => s.Occupied).Select(s => s.Label + WhyBusy(s, AiLang.Ru)).ToList();
        sb.AppendLine("Занятые участки: " + (busy.Count == 0 ? "нет" : string.Join(", ", busy)));
        var routes = il.ActiveRoutes.Select(r => r.Name + (r.TrainEntered ? " (поезд вошёл)" : r.Releasing ? " (разделка)" : "")).ToList();
        sb.AppendLine("Заданные маршруты: " + (routes.Count == 0 ? "нет" : string.Join("; ", routes)));
        sb.AppendLine("Стрелки: " + string.Join(", ", il.Switches.Values.Select(w => $"{w.Label} {w.PosText}" + (!w.Controlled ? " (переводится)" : w.Locked ? " (заперта)" : ""))));
        var open = il.Signals.Values.Where(s => s.Kind == SignalKind.Controlled && s.Aspect is not (Aspect.Red or Aspect.Dark or Aspect.Blue))
                     .Select(s => $"{s.Label} — {AspectName(s.Aspect, AiLang.Ru)}").ToList();
        sb.AppendLine("Открытые светофоры: " + (open.Count == 0 ? "нет" : string.Join(", ", open)));
        foreach (var l in il.Lines) sb.AppendLine($"Перегон «{l.Title}»: направление {l.DirText(l.Current)}" + (l.Syncing ? ", идёт согласование" : ""));
        sb.AppendLine("Режим интерфейса: " + UiSettings.ModeName(UiSettings.Mode));
        sb.Append(ArduinoLink.IsConnected ? "Arduino: подключена" : "Arduino: нет соединения");
        return sb.ToString();
    }

    static string WhyBusy(TrackSection s, AiLang l) => s.TrainCount > 0 ? (l == AiLang.Kk ? " (пойыз)" : " (поезд)")
        : s.HwOcc ? (l == AiLang.Kk ? " (датчик)" : " (датчик)") : s.ManualOcc ? (l == AiLang.Kk ? " (қолмен)" : " (вручную)")
        : s.RemoteOcc ? (l == AiLang.Kk ? " (көрші станция)" : " (сосед)") : "";

    // =================================================================
    //  Поиск объектов в вопросе
    // =================================================================
    static string Norm(string s)
    {
        var sb = new StringBuilder();
        foreach (var ch in (s ?? "").Trim().ToUpperInvariant())
            sb.Append(ch switch
            {
                'A' => 'А', 'B' => 'В', 'C' => 'С', 'E' => 'Е', 'H' => 'Н', 'K' => 'К', 'M' => 'М', 'O' => 'О', 'P' => 'Р', 'T' => 'Т',
                'X' => 'Х', 'Y' => 'У', 'Ё' => 'Е', '−' => '-', '–' => '-', '—' => '-', _ => ch
            });
        return sb.ToString();
    }

    static readonly Regex Token = new(@"[\p{L}\p{Nd}]+(?:[-/][\p{L}\p{Nd}]+)*", RegexOptions.Compiled);
    // казахские окончания у названий: «Н-нан», «Н1-ден», «3-жолға» — отрезаем, чтобы найти «Н», «Н1»
    static readonly Regex KzSuffix = new(@"-?(нан|нен|дан|ден|тан|тен|ға|ге|қа|ке|на|не|ның|нің|дың|дің|тың|тің|ны|ні|ды|ді|ты|ті|сы|сі|ы|і|да|де|та|те|ғы|гі)$", RegexOptions.Compiled);

    /// <summary>Возможные названия объектов в вопросе. Слово без цифр годится, только если в нём есть заглавная буква:
    /// «Н», «НА», «Ч» — названия, а «на», «от» — предлоги.</summary>
    static IEnumerable<string> Names(string q)
    {
        foreach (Match m in Token.Matches(q))
        {
            var v = m.Value;
            if (!v.Any(char.IsDigit) && !v.Any(char.IsUpper)) continue;
            yield return Norm(v);
            var cut = KzSuffix.Replace(v, "");
            if (cut != v && cut.Length > 0) yield return Norm(cut);
        }
    }

    static readonly Regex SigNoun = new(@"светофор|сигнал|бағдарш", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SwNoun = new(@"стрелк|стрелоч|съезд|бағыттам", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex SecNoun = new(@"участ|учаск|рельсов", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Deixis = new(@"(?<![\p{L}])(этот|эта|эту|это|этом|этой|мына|мынау|осы|бұл)(?![\p{L}])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex DescribeRx = new(@"для чего|зачем|что это|что за|что такое|что значит|что означает|что делает|назначени|расскажи|объясни|не үшін|деген не|нені білдіреді|түсіндір|қандай|бұл не|не істейді",
                                           RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex StateRx = new(@"сейчас|горит|цвет|красн|зелен|зелён|желт|жёлт|мигает|почему|занят|свобод|заперт|қазір|жанып|қызыл|жасыл|сары|неге|бос",
                                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex RouteRx = new(@"маршрут", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // =================================================================
    //  1) Объект схемы: «для чего светофор Н1», «что за стрелка 5/7», «бұл не?» (по наведению мыши)
    // =================================================================
    public static string AboutObject(string q, AiLang lang)
    {
        var il = IL;
        if (il == null || RouteRx.IsMatch(q)) return null;
        bool nSig = SigNoun.IsMatch(q), nSw = SwNoun.IsMatch(q), nSec = SecNoun.IsMatch(q);
        var found = new List<object>();
        foreach (var n in Names(q))
        {
            if (n.Length == 0) continue;
            bool strong = n.Any(char.IsDigit) && n.Any(char.IsLetter) || n.Contains('/');      // «Н1», «1СП», «5/7» — без слова-подсказки
            if (nSig || strong) found.AddRange(il.Signals.Values.Where(s => Norm(s.Label) == n && s.Kind != SignalKind.Neighbor || nSig && Norm(s.Label) == n));
            if (nSw || strong) found.AddRange(il.Switches.Values.Where(s => Norm(s.Label) == n));
            if (nSec || strong) found.AddRange(il.Sections.Values.Where(s => Norm(s.Label) == n));
            if (found.Count > 0) break;
        }
        if (found.Count == 0 && Deixis.IsMatch(q) && (nSig || nSw || nSec || DescribeRx.IsMatch(q)) && q.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 7)
        {
            var only = nSig ? typeof(RailSignal) : nSw ? typeof(RailSwitch) : nSec ? typeof(TrackSection) : null;
            var o = LastPointed(TimeSpan.FromMinutes(3), only);
            if (o == null)
                return lang == AiLang.Kk
                    ? "Қай объектіні айтып тұрғаныңызды көрмедім. Тінтуірді сызбадағы светофорға, стрелкаға немесе учаскеге апарып (немесе басып), қайта сұраңыз. Не болмаса атын жазыңыз, мысалы: «Н1 светофоры»."
                    : "Не вижу, на какой объект вы показываете. Наведите мышь на светофор, стрелку или участок (или кликните) и спросите снова. Либо напишите название, например: «светофор Н1».";
            found.Add(o);
        }
        if (found.Count == 0) return null;
        bool state = StateRx.IsMatch(q);
        return string.Join("\n\n", found.Distinct().Take(2).Select(o => o switch
        {
            RailSignal s => DescribeSignal(s, il, lang, state),
            RailSwitch w => DescribeSwitch(w, il, lang),
            TrackSection c => DescribeSection(c, il, lang),
            _ => ""
        }));
    }

    static string StationOf(string id, AiLang l) => AiProfile.StationOf(id, l);   // [ИЗМЕНЕНО 06.11.2026] своё у АКЖТ и Алгабаса

    static string RouteList(IEnumerable<RailRoute> rs)
    {
        var n = rs.Select(r => r.Name).Distinct().ToList();
        return string.Join("; ", n.Take(5)) + (n.Count > 5 ? "…" : "");
    }

    static string DescribeSignal(RailSignal s, Interlocking il, AiLang lang, bool state)
    {
        bool kk = lang == AiLang.Kk;
        var routes = il.Routes.Where(r => r.Start == s).ToList();
        var sb = new StringBuilder(kk ? $"{s.Label} светофоры ({StationOf(s.Id, lang)}). " : $"Светофор {s.Label} ({StationOf(s.Id, lang)}): {s.Title}. ");
        switch (s.Kind)
        {
            case SignalKind.Controlled when s.IsShuntOnly:
                sb.Append(kk ? "Маневрлік светофор: маневрлік маршрут берілгенде ай-ақ от жанады (манёвр рұқсат), әйтпесе көк (тыйым). "
                             : "Маневровый: лунно-белый огонь, когда задан маневровый маршрут (манёвры разрешены), иначе синий (запрещены). ");
                break;
            case SignalKind.Controlled:
                sb.Append(kk ? "Маршрутты светофор: маршрут берілмейінше қызыл, тек берілген маршрут бойынша ашылады. "
                             : "Маршрутный: пока маршрут не задан — красный, открывается только по заданному маршруту. ");
                break;
            case SignalKind.Automatic:
                sb.Append(kk ? "Автоблокировканың өткел светофоры: алдағы блок-учаскелердің бос еместігіне қарай оты өзі ауысады. "
                             : "Проходной автоблокировки: сам меняет огни по занятости блок-участков впереди, вручную не открывается. ");
                if (s.Protects != null) sb.Append(kk ? $"{s.Protects.Label} блок-учаскесін қорғайды. " : $"Защищает блок-участок {s.Protects.Label}. ");
                break;
            case SignalKind.Neighbor:
                sb.Append(kk ? "Көрші станцияның светофоры: бізде тек көрсетіледі, оны көрші станцияның кезекшісі ашады. "
                             : "Светофор соседней станции: у нас только показывается, открывает его дежурный соседней станции. ");
                break;
        }
        if (routes.Count > 0) sb.Append(kk ? $"Осыдан берілетін маршруттар: {RouteList(routes)}. " : $"Отсюда задаются маршруты: {RouteList(routes)}. ");
        sb.Append(kk ? $"Қазір: {AspectName(s.Aspect, lang)}. {Meaning(s.Aspect, lang)}" : $"Сейчас: {AspectName(s.Aspect, lang)}. {Meaning(s.Aspect, lang)}");
        return sb.ToString().Trim();
    }

    static string DescribeSwitch(RailSwitch w, Interlocking il, AiLang lang)
    {
        bool kk = lang == AiLang.Kk;
        int n = il.Routes.Count(r => r.Switches.Any(x => x.sw == w));
        var sb = new StringBuilder(kk ? $"{w.Label} стрелкасы ({StationOf(w.Id, lang)}). " : $"Стрелка {w.Label} ({StationOf(w.Id, lang)}): {w.Title}. ");
        sb.Append(kk ? "Пойызды бір жолдан екінші жолға ауыстырады: «плюс» — түзу, «минус» — бұрылысқа. " : "Переводит поезд с пути на путь: «плюс» — прямо, «минус» — на отклонение. ");
        if (n > 0) sb.Append(kk ? $"{n} маршрутқа керек. " : $"Нужна для маршрутов: {n}. ");
        sb.Append(kk ? "Қазір: " + (w.Plus ? "плюс (түзу)" : "минус (бұрылысқа)") : "Сейчас: " + (w.Plus ? "плюс (прямо)" : "минус (на отклонение)"));
        if (!w.Controlled) sb.Append(kk ? ", ауысып жатыр" : ", переводится");
        if (w.Locked) sb.Append(kk ? ", құлыпталған" : ", заперта");
        sb.Append(". ");
        sb.Append(kk ? "Ауыстыру — сызбадағы нөмірін тінтуірдің сол жағымен басу." : "Перевести — левой кнопкой мыши по её номеру на схеме.");
        return sb.ToString();
    }

    static string DescribeSection(TrackSection c, Interlocking il, AiLang lang)
    {
        bool kk = lang == AiLang.Kk;
        int n = il.Routes.Count(r => r.Sections.Contains(c));
        var sb = new StringBuilder(kk ? $"{c.Label} учаскесі ({StationOf(c.Id, lang)}). " : $"Участок {c.Label} ({StationOf(c.Id, lang)}): {c.Title}. ");
        sb.Append(kk ? "Учаске — жолдың бір бөлігі, оның бос еместігін жүйе қадағалайды (рельс тізбегі). " : "Участок — часть пути, за занятостью которой следит система (рельсовая цепь). ");
        if (n > 0) sb.Append(kk ? $"{n} маршрутқа кіреді. " : $"Входит в маршруты: {n}. ");
        sb.Append(c.Occupied ? (kk ? "Қазір: бос емес" : "Сейчас: занят") + WhyBusy(c, lang) : kk ? "Қазір: бос" : "Сейчас: свободен");
        if (c.LockedBy != null) sb.Append(kk ? $", {c.LockedBy.Name} маршрутында тұйықталған" : $", замкнут в маршруте {c.LockedBy.Name}");
        return sb.Append('.').ToString();
    }

    // =================================================================
    //  2) Как задать конкретный маршрут
    // =================================================================
    static readonly Regex TrackRx = new(@"(?:путь|пути|путём|на)\s*([IVX]+|\d+)|([IVX]+|\d+)\s*-?\s*(?:жол|путь|п\b)|(?<![\p{L}\d/])([IVX]+|\d+)П(?![\p{L}])",
                                        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Dictionary<string, string> Roman = new() { ["I"] = "1", ["II"] = "2", ["III"] = "3", ["IV"] = "4", ["V"] = "5", ["VI"] = "6", ["VII"] = "7" };
    static string TrackKey(string s) { s = Norm(s).Replace("І", "I"); return Roman.TryGetValue(s, out var a) ? a : s; }

    /// <summary>Вопрос про маршрут с названиями («маршрут Н на путь 3»). null — не про конкретный маршрут.</summary>
    public static string HowToRoute(string q, AiLang lang)
    {
        var il = IL;
        if (il == null || !RouteRx.IsMatch(q)) return null;
        var names = Names(q).ToList();

        // светофоры из вопроса, в порядке упоминания
        var sigs = new List<RailSignal>();
        foreach (var n in names)
            foreach (var s in il.Signals.Values.Where(s => s.Kind == SignalKind.Controlled && Norm(s.Label) == n))
                if (!sigs.Contains(s)) sigs.Add(s);
        // пути: «путь 3», «3 жол», «3П», «I жолға»
        var tracks = new List<string>();
        foreach (Match m in TrackRx.Matches(q.Replace("І", "I")))
            foreach (var g in new[] { m.Groups[1], m.Groups[2], m.Groups[3] })
                if (g.Success && g.Value.Length > 0) tracks.Add(TrackKey(g.Value));
        if (sigs.Count == 0) return null;

        bool shunt = Regex.IsMatch(q, "маневр|манёвр", RegexOptions.IgnoreCase);
        var start = sigs[0];
        var cands = il.Routes.Where(r => sigs.Contains(r.Start)).ToList();
        if (cands.Count == 0) return null;
        // конец: второй светофор или путь
        var end = sigs.FirstOrDefault(s => s.Label != start.Label);     // у АКЖТ и поста есть одноимённые («Н»)
        var fit = cands.Where(r =>
                (end == null || r.End == end) &&
                (tracks.Count == 0 || tracks.Any(t => RouteTrack(r) == t)) &&
                (!shunt || r.Shunting)).ToList();
        if (end != null && fit.Count == 0) fit = cands.Where(r => r.Start == start && (r.End == end || r.Name.Contains(end.Label))).ToList();
        if (fit.Count == 0 && tracks.Count == 0) fit = cands.Where(r => r.Start == start).ToList();
        if (fit.Count == 0)
            return lang == AiLang.Kk
                ? $"{start.Label} светофорынан мұндай маршрут жоқ. Одан берілетіндері: {RouteList(cands.Where(r => r.Start == start))}."
                : $"Такого маршрута от светофора {start.Label} нет. От него задаются: {RouteList(cands.Where(r => r.Start == start))}.";
        if (fit.Count > 1 && (tracks.Count > 0 || end != null)) fit = fit.Where(r => !r.Shunting || shunt).DefaultIfEmpty(fit[0]).ToList();
        if (fit.Count > 3 || (fit.Count > 1 && tracks.Count == 0 && end == null))
            return (lang == AiLang.Kk
                ? $"{start.Label} светофорынан бірнеше маршрут бар: {RouteList(fit)}. Қайсысы керек екенін жазыңыз (мысалы: «{fit[0].Name}»). Жалпы тәртіп: басталу светофорын, содан соң баратын жолды немесе соңғы светофорды басыңыз."
                : $"От светофора {start.Label} несколько маршрутов: {RouteList(fit)}. Уточните, какой нужен (например: «{fit[0].Name}»). Порядок общий: щёлкните светофор начала, затем путь назначения или светофор конца.");
        return string.Join("\n\n", fit.Take(2).Select(r => RouteSteps(r, il, lang)));
    }

    /// <summary>Номер пути маршрута: «путь 3» в названии, путь назначения «3П» и т.п.</summary>
    static string RouteTrack(RailRoute r)
    {
        var m = Regex.Match(r.Name, @"путь\s+([IVX]+|\d+)", RegexOptions.IgnoreCase);
        if (m.Success) return TrackKey(m.Groups[1].Value);
        if (r.Dest != null)
        {
            var d = Regex.Match(r.Dest.Label, @"^([IVX]+|\d+)П$", RegexOptions.IgnoreCase);
            if (d.Success) return TrackKey(d.Groups[1].Value);
        }
        return null;
    }

    static string RouteSteps(RailRoute r, Interlocking il, AiLang lang)
    {
        bool kk = lang == AiLang.Kk;
        bool classic = UiSettings.Mode == UiMode.Classic;
        string target = r.Dest != null ? (kk ? $"баратын жолды ({r.Dest.Label})" : $"путь назначения ({r.Dest.Label})")
                      : r.End != null ? (kk ? $"соңғы светофорды {r.End.Label}" : $"светофор конца {r.End.Label}")
                      : (kk ? "маршрут соңын" : "конец маршрута");
        string kind = r.Shunting ? (kk ? "маневрлік" : "маневровый") : (kk ? "пойыз" : "поездной");
        var sb = new StringBuilder();
        sb.AppendLine(kk ? $"«{r.Name}» маршруты ({kind}):" : $"Маршрут «{r.Name}» ({kind}):");
        if (classic)
        {
            sb.AppendLine(kk ? $"1. Сызба астындағы {(r.Shunting ? "МАНЕВРОВЫЕ" : "ПОЕЗДНЫЕ")} қатарынан {r.Start.Label} батырмасын басыңыз (басталуы)."
                             : $"1. Под схемой в ряду {(r.Shunting ? "МАНЕВРОВЫЕ" : "ПОЕЗДНЫЕ")} нажмите кнопку {r.Start.Label} (начало).");
            sb.AppendLine(r.End != null
                ? (kk ? $"2. Содан соң {r.End.Label} батырмасын басыңыз (немесе ПУТИ қатарынан жолды)." : $"2. Затем кнопку {r.End.Label} (или путь в ряду ПУТИ, УЧАСТКИ).")
                : (kk ? $"2. Содан соң ПУТИ қатарынан {r.Dest?.Label} батырмасын басыңыз." : $"2. Затем кнопку {r.Dest?.Label} в ряду ПУТИ, УЧАСТКИ."));
        }
        else
        {
            sb.AppendLine(kk ? $"1. Төменнен {(r.Shunting ? "МАНЕВРОВЫЕ" : "ПОЕЗДНЫЕ")} батырмасын таңдаңыз." : $"1. Внизу выберите {(r.Shunting ? "МАНЕВРОВЫЕ" : "ПОЕЗДНЫЕ")}.");
            sb.AppendLine(kk ? $"2. Сызбадағы {r.Start.Label} светофорын тінтуірдің сол жағымен басыңыз." : $"2. Щёлкните левой кнопкой мыши светофор {r.Start.Label} на схеме.");
            sb.AppendLine(kk ? $"3. Содан соң {target} басыңыз. Немесе «Маршруты» тізімінен «{r.Name}» атауын басыңыз." : $"3. Затем щёлкните {target}. Или кликните «{r.Name}» в списке «Маршруты».");
        }
        if (r.Switches.Count > 0)
            sb.AppendLine((kk ? "Стрелкалар өздері ауысады: " : "Стрелки переведутся сами: ") + string.Join(", ", r.Switches.Select(x => $"{x.sw.Label} {(x.plus ? "+" : "−")}")) + ".");
        if (r.Sections.Count > 0)
            sb.AppendLine((kk ? "Бос болуы керек: " : "Должны быть свободны: ") + string.Join(", ", r.Sections.Select(x => x.Label)) + ".");
        string why = il.CheckRoute(r);
        if (r.Active) sb.Append(kk ? "Бұл маршрут қазір берілген." : "Этот маршрут сейчас задан.");
        else if (why == null) sb.Append(kk ? "Қазір бұл маршрутты беруге болады." : "Сейчас его можно задать.");
        else sb.Append(kk ? "Қазір берілмейді" + (AiAssistant.KkObjects(why) is { Length: > 0 } o ? " " + o.Trim() : "") + ": " + (AiAssistant.HintKk(why) ?? why) : "Сейчас не задастся: " + why);
        return sb.ToString().Trim();
    }

    // =================================================================
    //  Показания светофоров
    // =================================================================
    public static string AspectName(Aspect a, AiLang l) => l == AiLang.Kk
        ? a switch
        {
            Aspect.Dark => "сөндірулі", Aspect.Red => "қызыл", Aspect.Yellow => "сары", Aspect.YellowBlink => "жыпылықтайтын сары",
            Aspect.Green => "жасыл", Aspect.TwoYellow => "екі сары", Aspect.TwoYellowTopBlink => "екі сары, жоғарғысы жыпылықтайды",
            Aspect.Invite => "шақыру (қызыл және жыпылықтайтын ай-ақ)", Aspect.White => "ай-ақ", Aspect.Blue => "көк", _ => ""
        }
        : a switch
        {
            Aspect.Dark => "погашен", Aspect.Red => "красный", Aspect.Yellow => "жёлтый", Aspect.YellowBlink => "жёлтый мигающий",
            Aspect.Green => "зелёный", Aspect.TwoYellow => "два жёлтых", Aspect.TwoYellowTopBlink => "два жёлтых, верхний мигает",
            Aspect.Invite => "пригласительный", Aspect.White => "лунно-белый", Aspect.Blue => "синий", _ => ""
        };

    public static string Meaning(Aspect a, AiLang l) => l == AiLang.Kk
        ? a switch
        {
            Aspect.Dark => "Бір жолды перегонда бағыты сәйкес келмейтін өткел светофоры сөндірулі.",
            Aspect.Red => "Қызыл — ТОҚТА! Светофордан өтуге болмайды.",
            Aspect.Yellow => "Бір сары — тоқтауға дайын болып жүру: келесі светофор жабық.",
            Aspect.YellowBlink => "Жыпылықтайтын сары — келесі светофор ашық, бұрылысқа кішірейтілген жылдамдықпен.",
            Aspect.Green => "Жасыл — белгіленген жылдамдықпен жүру, алда екі және одан көп блок-учаске бос.",
            Aspect.TwoYellow => "Екі сары — стрелка бойынша бұрылысқа кішірейтілген жылдамдықпен; келесі светофор жабық.",
            Aspect.TwoYellowTopBlink => "Екі сары, жоғарғысы жыпылықтайды — бұрылысқа кішірейтілген жылдамдықпен; келесі светофор ашық.",
            Aspect.Invite => "Шақыру сигналы — сағатына 20 км-ден аспай, ерекше сақтықпен.",
            Aspect.White => "Ай-ақ — манёвр жасауға рұқсат.",
            Aspect.Blue => "Көк — манёвр жасауға тыйым.",
            _ => ""
        }
        : a switch
        {
            Aspect.Dark => "Погашен: проходной неправильного направления на однопутном перегоне.",
            Aspect.Red => "Красный — СТОЙ! Проезжать сигнал запрещено.",
            Aspect.Yellow => "Один жёлтый — движение с готовностью остановиться: следующий светофор закрыт.",
            Aspect.YellowBlink => "Жёлтый мигающий — следующий светофор открыт, по отклонению с уменьшенной скоростью.",
            Aspect.Green => "Зелёный — движение с установленной скоростью, впереди свободны два и более блок-участка.",
            Aspect.TwoYellow => "Два жёлтых — с уменьшенной скоростью по отклонению; следующий светофор закрыт.",
            Aspect.TwoYellowTopBlink => "Два жёлтых, верхний мигает — по отклонению с уменьшенной скоростью; следующий открыт.",
            Aspect.Invite => "Пригласительный — не более 20 км/ч, с особой бдительностью.",
            Aspect.White => "Лунно-белый — манёвры разрешены.",
            Aspect.Blue => "Синий — манёвры запрещены.",
            _ => ""
        };
}
