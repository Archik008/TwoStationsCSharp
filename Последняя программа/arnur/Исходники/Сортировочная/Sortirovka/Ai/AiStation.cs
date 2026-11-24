using System.Text;
using System.Text.RegularExpressions;
using StationApp.Yard;

namespace StationApp.Ai;

// =====================================================================
//  ИИ-ПОМОЩНИК: ЧТО ПРОИСХОДИТ НА СОРТИРОВОЧНОЙ  [ДОБАВЛЕНО 12.11.2026]
//  Тот же набор, что у AiStation АКЖТ и Алгабаса (его вызывают общие AiAssistant и AiChatWindow), но по модели горки:
//  журнал программы и отказы, объект под мышью, описание стрелки / замедлителя / пути / отцепа / горочного светофора,
//  «как задать маршрут отцепу на путь N» — шаги по режиму (ГАЦ или ручной) и по состоянию горки.
// =====================================================================

public static class AiStation
{
    public static YardSim Sim;

    static readonly List<(DateTime t, string text, bool refusal)> journal = new();
    static (string id, DateTime t) pointed;
    static readonly object lk = new();

    public static event Action<string> Refused;

    public static void Install() { }

    /// <summary>Запись журнала окна (bad — отказ или нарушение).</summary>
    public static void Note(string text, bool bad)
    {
        if (bad) text = Regex.Replace(text, @"^(Нельзя|НАРУШЕНИЕ)\s*:\s*", "");
        lock (lk)
        {
            journal.Add((DateTime.Now, text, bad));
            if (journal.Count > 300) journal.RemoveAt(0);
        }
        if (bad) try { Refused?.Invoke(text); } catch { }
    }

    public static void Point(string id) { lock (lk) pointed = (id, DateTime.Now); }

    public static List<string> LastActions(int n)
    {
        lock (lk) return journal.Skip(Math.Max(0, journal.Count - n)).Select(e => $"{e.t:HH:mm:ss} {(e.refusal ? "[отказ/нарушение] " : "")}{e.text}").ToList();
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

    public static string State()
    {
        var s = Sim;
        if (s == null) return "";
        var sb = new StringBuilder();
        sb.AppendLine($"Время {s.Clock:HH:mm:ss}. Вариант: {(s.AutoOps ? "только горка" : "горка + маневры")}. Маршруты: {(s.GacMode ? "ГАЦ из листка" : "ручной набор")}. АРС: {(s.Ars ? "вкл" : "выкл")}.");
        sb.AppendLine("Горочный светофор: " + YardSim.AspectName(s.Hump) + ".");
        if (s.Pushing != null) sb.AppendLine($"Надвиг поезда {s.Pushing.Name} с пути П{s.Pushing.Arr}, {s.Pushing.V * 3.6:0.0} км/ч, в составе {s.Pushing.Cuts.Sum(c => c.Wagons.Count)} ваг.");
        for (int a = 1; a <= Lay.ArrTracks; a++) sb.AppendLine($"П{a}: {s.Arr[a].State}" + (s.Arr[a].Train != null ? $" ({s.Arr[a].Train.Name})" : ""));
        foreach (var r in s.Retarders) sb.AppendLine($"{r.Name}: ступень {r.Step}" + (r.Cut != null ? $", отцеп {r.Cut.No} {r.Cut.V * 3.6:0.0} км/ч, рек. {r.Target * 3.6:0.0}" : ""));
        for (int k = 1; k <= Lay.Tracks; k++) sb.AppendLine($"Путь {k} ({YardSim.TrackNames[k]}): {s.OnTrack(k).Sum(c => c.Wagons.Count)} ваг.");
        sb.AppendLine($"Оценка {s.Grade} ({s.Score}), нарушений {s.Violations.Count}.");
        return sb.ToString();
    }

    // =================================================================
    //  ОБЪЕКТ СХЕМЫ
    // =================================================================
    static readonly Regex ReSwitch = new(@"стрелк\w*\s*(?:№\s*)?(\d)|(\d)\s*стрелк", RegexOptions.IgnoreCase);
    static readonly Regex ReTrack = new(@"(?:путь|пути|пути?\s*№)\s*(\d)\b|(\d)\s*(?:-?\s*)?(?:путь|жол)", RegexOptions.IgnoreCase);
    static readonly Regex ReCut = new(@"отцеп\w*\s*(?:№\s*)?(\d+)", RegexOptions.IgnoreCase);
    static readonly Regex ReArr = new(@"\bП\s*([1-4])\b");
    static readonly Regex ReWhat = new(@"что это|это что|что такое это|бұл не|бұл қандай", RegexOptions.IgnoreCase);

    public static string AboutObject(string q, AiLang lang)
    {
        var s = Sim;
        if (s == null) return null;
        string id = null;
        if (ReWhat.IsMatch(q))
            lock (lk) if (pointed.id != null && DateTime.Now - pointed.t < TimeSpan.FromMinutes(2)) id = pointed.id;
        if (id == null)
        {
            Match m;
            if ((m = ReCut.Match(q)).Success) id = "отцеп " + m.Groups[1].Value;
            else if ((m = ReSwitch.Match(q)).Success) id = "стрелка " + (m.Groups[1].Success && m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[2].Value);
            else if (Regex.IsMatch(q, @"1\s*ТП", RegexOptions.IgnoreCase)) id = "1ТП";
            else if (Regex.IsMatch(q, @"2\s*ТП\s*-?\s*Б", RegexOptions.IgnoreCase)) id = "2ТП-Б";
            else if (Regex.IsMatch(q, @"2\s*ТП", RegexOptions.IgnoreCase)) id = "2ТП-А";
            else if (Regex.IsMatch(q, @"горочн\w*\s+светофор|светофор\w*\s+Г\b|төбе светофор", RegexOptions.IgnoreCase)) id = "горочный светофор";
            else if ((m = ReArr.Match(q)).Success) id = "П" + m.Groups[1].Value;
            else if ((m = ReTrack.Match(q)).Success && !Regex.IsMatch(q, "маршрут", RegexOptions.IgnoreCase)) id = "путь " + (m.Groups[1].Value != "" ? m.Groups[1].Value : m.Groups[2].Value);
        }
        return id == null ? null : Describe(id, lang);
    }

    static string Describe(string id, AiLang lang)
    {
        var s = Sim;
        bool kk = lang == AiLang.Kk;
        if (id.StartsWith("стрелка ") && int.TryParse(id[8..], out int n) && n is >= 1 and <= 7)
        {
            var sw = s.Switches[n];
            string pos = sw.Throwing ? (kk ? "қазір ауысып жатыр" : "сейчас переводится")
                : sw.Plus ? (kk ? $"плюста — {sw.Range(true)} жолдарға" : $"в плюсе — на пути {sw.Range(true)}")
                : (kk ? $"минуста — {sw.Range(false)} жолдарға" : $"в минусе — на пути {sw.Range(false)}");
            bool busy = s.Occupied(sw.Section);
            return kk
                ? $"{n}-стрелка — төбенің стрелкасы ({sw.Section.Id} учаскесі): {pos}. Оны ГАЦ әр отцептің маршруты бойынша 0,6 секундта өзі ауыстырады; қолмен — схемадағы нөмірін басу. " +
                  (busy ? "Қазір стрелкалық учаске бос емес — ауыстыруға болмайды." : "Учаске бос.") + " Отцептер арасындағы аралық қысқа болса, стрелка ауысып үлгермейді — отцеп басқа жолға кетеді."
                : $"Стрелка {n} — горочная стрелка (участок {sw.Section.Id}): {pos}. Её переводит ГАЦ под маршрут каждого отцепа за 0,6 с; вручную — щелчок по номеру на схеме. " +
                  (busy ? "Сейчас стрелочный участок занят — переводить нельзя." : "Участок свободен.") + " Если интервал между отцепами мал, стрелка не успевает — отцеп уходит не на свой путь.";
        }
        var r = s.Retarders.FirstOrDefault(x => x.Name == id);
        if (r != null)
        {
            string cut = r.Cut == null ? "" : kk
                ? $" Қазір {(r.CutOn ? "тежегіште" : "жақындап келе жатыр")} {r.Cut.No}-отцеп, {r.Cut.V * 3.6:0.0} км/сағ, шығудағы ұсынылатын жылдамдық {r.Target * 3.6:0.0} км/сағ."
                : $" Сейчас {(r.CutOn ? "на замедлителе" : "подходит")} отцеп {r.Cut.No}, {r.Cut.V * 3.6:0.0} км/ч, рекомендуемая скорость на выходе {r.Target * 3.6:0.0} км/ч.";
            bool first = r.Lo == 1 && r.Hi == 8;
            return kk
                ? $"{r.Name} — {(first ? "1-тежегіш позиция: отцептер арасында стрелкаларға жететін аралық жасайды" : $"2-тежегіш позиция ({r.Lo}–{r.Hi} жолдар): отцептің жылдамдығын жолдағы вагондарға 5 км/сағ-тан аспай жететіндей етіп реттейді")}. " +
                  $"Сатысы: {(r.Step == 0 ? "О (тежеуден босату)" : r.Step.ToString())}. Сатылар 1–4 — тежеу күші; АРС қосулы болса, өзі тежейді." + cut
                : $"{r.Name} — {(first ? "первая тормозная позиция: создаёт интервалы между отцепами, чтобы успевали стрелки" : $"вторая тормозная позиция (пути {r.Lo}–{r.Hi}): регулирует скорость так, чтобы отцеп подошёл к вагонам на пути не быстрее 5 км/ч")}. " +
                  $"Ступень: {(r.Step == 0 ? "О (оттормозить)" : r.Step.ToString())}. Ступени 1–4 — сила торможения; с включённой АРС тормозит сама." + cut;
        }
        if (id == "горочный светофор" || id == "повторитель горочного светофора")
            return kk
                ? "Г — төбе светофоры (ПГ — оның қайталағышы). Жасыл — белгіленген жылдамдықпен тарату (6 км/сағ), сары мен жасыл — аралық (4,5), сары — азайтылған (3), қызыл — тоқта, тарату тыйым салынған, қызыл және ақ «Н» әрпі — вагондарды төбеден қабылдау паркіне кері итеру. Қазір: " + YardSim.AspectName(s.Hump) + "."
                : "Г — горочный светофор (ПГ — его повторитель над парком приёма). Зелёный — роспуск с установленной скоростью (6 км/ч), жёлтый и зелёный — с промежуточной (4,5), жёлтый — с уменьшенной (3), красный — стой, роспуск запрещён, красный и белая «Н» — осадить вагоны с горки в парк приёма. Сейчас: " + YardSim.AspectName(s.Hump) + ".";
        if (id.StartsWith("путь ") && int.TryParse(id[5..], out int k) && k is >= 1 and <= 8)
        {
            int wagons = s.OnTrack(k).Sum(c => c.Wagons.Count);
            return kk
                ? $"{k}-жол — сұрыптау паркінің жолы, «{YardSim.TrackNames[k]}» бағыты бойынша құрам жиналады. Қазір {wagons} вагон (норма {YardSim.Norm(k)}). Құрам жиналғанда оны жөнелту паркіне шығарады."
                : $"Путь {k} — сортировочный путь, копится состав на «{YardSim.TrackNames[k]}». Сейчас {wagons} ваг. (норма {YardSim.Norm(k)}). Набрался — состав выводят в парк отправления.";
        }
        if (id.StartsWith("П") && int.TryParse(id[1..], out int a) && a is >= 1 and <= 4)
        {
            var at = s.Arr[a];
            return kk
                ? $"П{a} — қабылдау паркінің жолы. Мұнда пойыз келеді, пойыз локомотиві ағытылады, ПТО қарауы, содан кейін құйрыққа төбе локомотиві тіркеледі. Күйі: {at.State}."
                : $"П{a} — путь парка приёма. Сюда приходит поезд, отцепляется поездной локомотив, идёт осмотр ПТО, затем в хвост заходит горочный локомотив. Сейчас: {at.State}" + (at.Train != null ? $", поезд {at.Train.Name}" : "") + ".";
        }
        if (id.StartsWith("отцеп ") && int.TryParse(id[6..], out int no))
        {
            var c = s.Cuts.FirstOrDefault(x => x.No == no && x.Active) ?? s.ListokTrain?.AllCuts.FirstOrDefault(x => x.No == no);
            if (c == null) return null;
            string runner = c.Runner switch { Runner.Good => kk ? "жақсы жүргіш (жүкті)" : "хороший бегун (гружёный)", Runner.Bad => kk ? "нашар жүргіш (бос)" : "плохой бегун (порожний)", _ => kk ? "орташа" : "средний" };
            return kk
                ? $"{c.No}-отцеп: {c.Wagons.Count} вагон ({c.KindsText}), {c.Mass:0} т, {runner}. Парақ бойынша — {c.Dest}-жол, маршрут {(c.Route > 0 ? c.Route + "-жолға" : "берілмеген")}." + (c.Forbidden ? $" «{c.Mark}» белгісі — төбеден таратуға болмайды, тек локомотивпен түсіріледі." : "")
                : $"Отцеп {c.No}: {c.Wagons.Count} ваг. ({c.KindsText}), {c.Mass:0} т, {runner}. По листку — путь {c.Dest}, маршрут {(c.Route > 0 ? "на путь " + c.Route : "не задан")}." + (c.Forbidden ? $" Отметка «{c.Mark}» — распускать с горки нельзя, только спуск с локомотивом." : "");
        }
        return null;
    }

    // =================================================================
    //  «КАК ЗАДАТЬ МАРШРУТ ОТЦЕПУ НА ПУТЬ N», «КАК РАСПУСТИТЬ СОСТАВ»
    // =================================================================
    static readonly Regex ReRoute = new(@"(маршрут|отцеп)\w*.*?(?:на\s+)?(?:путь|пути)\s*(\d)|(\d)\s*-?\s*жолға", RegexOptions.IgnoreCase);
    static readonly Regex ReHowRoll = new(@"как\s+(распустить|начать роспуск|надвинуть|роспуск)|қалай тарат|таратуды қалай", RegexOptions.IgnoreCase);

    public static string HowToRoute(string q, AiLang lang)
    {
        var s = Sim;
        if (s == null) return null;
        bool kk = lang == AiLang.Kk;
        var m = ReRoute.Match(q);
        if (m.Success)
        {
            int k = int.Parse(m.Groups[2].Value != "" ? m.Groups[2].Value : m.Groups[3].Value);
            if (k is < 1 or > 8) return null;
            var next = s.ListokTrain?.AllCuts.FirstOrDefault(c => c.State == CutState.InTrain && c.Route == 0);
            if (s.GacMode)
                return kk
                    ? $"ГАЦ режимінде маршруттар сұрыптау парағынан өзі қойылады. Бір отцептің маршрутын {k}-жолға өзгерту үшін: парақтан отцепті таңдаңыз, содан кейін «{k}» батырмасын басыңыз (немесе {k} пернесін). Стрелкаларды ГАЦ отцеп жақындағанда өзі ауыстырады."
                    : $"В режиме «ГАЦ» маршруты ставятся сами из сортировочного листка. Чтобы отцепу дать путь {k}: выберите отцеп в листке и нажмите кнопку «{k}» (или клавишу {k}). Стрелки ГАЦ переведёт сама, когда отцеп подойдёт.";
            return kk
                ? $"Қолмен теру: әр отцеп горбқа жеткенше оның жолының батырмасын басыңыз. «{k}» батырмасы (немесе {k} пернесі) маршрутты келесі маршруты жоқ отцепке береді" + (next != null ? $" — қазір бұл {next.No}-отцеп (парақ бойынша {next.Dest}-жол)." : ".") + " Басқа отцепке — алдымен оны парақтан таңдаңыз. Қате болса — «Соңғы маршрутты өшіру»."
                : $"Ручной набор: перед каждым отцепом, пока он не дошёл до горба, нажмите кнопку его пути. Кнопка «{k}» (или клавиша {k}) задаёт маршрут следующему отцепу без маршрута" + (next != null ? $" — сейчас это отцеп {next.No} (по листку путь {next.Dest})." : ".") + " Другому отцепу — сначала выберите его в листке. Ошиблись — «Сбросить последний маршрут».";
        }
        if (ReHowRoll.IsMatch(q))
            return kk
                ? "Тарату: ең оңайы — сары «Қазір не істеу керек» блогының батырмасын басу. Қадамдар: 1) жасыл П1…П4 батырмасы (1-қадам); 2) төбе бағдаршамы — «Қалыпты» (2-қадам); 3) отцептер горбтан асып өзі домалайды — маршруттарды (қолмен режимде) және тежегіштерді (АРС-сыз) бақылаңыз; 4) «Л», «Е» белгісі бар вагондар алдында — «■ ТОҚТА», содан кейін «Локомотивпен түсіру»."
                : "Роспуск: проще всего — нажимать кнопку в жёлтом блоке «Что делать сейчас». По шагам: 1) зелёная кнопка П1…П4 (шаг 1); 2) горочный светофор — «Обычно» (шаг 2); 3) отцепы, перевалив горб, покатятся сами — следите за маршрутами (в ручном режиме) и замедлителями (без АРС); 4) перед вагонами с отметкой «Л», «Е» — «■ СТОП» и «Спустить с локомотивом».";
        return null;
    }
}
