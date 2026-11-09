using System.Text;
using System.Text.RegularExpressions;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ-ПОМОЩНИК: ВОПРОС → ОТВЕТ  [ДОБАВЛЕНО 28.10.2026, по Station LAB Тұмара (WikiAssistant + StationGuide)]
//  Порядок:
//   1) «не задаётся / ошибка / қате / неге?» после отказа — разбор отказа (подсказки преподавателя + состояние станции);
//   2) маршрут с названиями («маршрут Н на путь 3», «Н-нан I жолға маршрут») — шаги по модели программы;
//   3) объект схемы («для чего светофор Н1», «бұл не?» по наведению мыши) — описание по модели;
//   4) база знаний (Knowledge + инструкция студента, на двух языках) — если нашлось уверенно;
//   5) Википедия (только железная дорога);
//   6) что-то из базы знаний, пусть не очень уверенно; иначе — «не знаю» и вопрос записывается
//      в Knowledge\_Вопросы_без_ответа.txt (преподаватель допишет ответ в справку).
//  Ответ всегда на языке вопроса. Нейросеть (Ollama) необязательна: с ней ответ короче и своими словами.
// =====================================================================

public sealed class AiAssistant : IDisposable
{
    readonly AiKnowledge kb = new();
    readonly AiWiki wiki = new();
    readonly AiLlm llm = new();

    public AiLang LastLang { get; private set; } = AiLang.Ru;
    public bool LlmOnline => llm.Online;
    public string LlmWhere => llm.Where;
    public int KnowledgeCount => kb.Count;
    public string KnowledgePath => kb.KnowledgePath;

    static readonly Regex ErrorWords = new(
        @"не задаётся|не задается|не работает|не получается|не могу|ошибк|отказ|что не так|не едет|не открыва|не переключ|не ставится|не переводится|" +
        @"жұмыс істемейді|қате|болмай|шықпай|ашылмай|берілмей|ауыспай|бас тарт",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex WhyWords = new(@"почему|неге|отчего|что случилось|не понимаю|не түсінбедім", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public async Task<bool> StartAsync() => await llm.EnsureAsync(true);
    public Task<bool> PingWikiAsync() => AiLlm.WikipediaOn ? wiki.PingAsync() : Task.FromResult(false);

    public async Task<string> AskAsync(string question)
    {
        question = (question ?? "").Trim();
        if (question.Length == 0) return "";
        var lang = AiText.DetectLang(question);
        LastLang = lang;
        await llm.EnsureAsync();

        // 1) ошибка в программе
        bool shortWhy = WhyWords.IsMatch(question) && question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6
                        && AiStation.RecentRefusal(TimeSpan.FromMinutes(3)) != null;
        if (ErrorWords.IsMatch(question) || shortWhy)
        {
            var route = AiStation.HowToRoute(question, lang);         // «почему не задаётся маршрут Н → 3» — сразу по маршруту
            return route ?? await AboutProgram(question, lang);
        }

        // 2) конкретный маршрут
        var r = AiStation.HowToRoute(question, lang);
        if (r != null) return r;

        // 3) объект схемы
        var o = AiStation.AboutObject(question, lang);
        if (o != null) return o;

        // 4) база знаний
        var hits = kb.Search(question);
        var best = hits.FirstOrDefault();
        if (best != null && best.Coverage >= 0.5 && best.Score >= 3.0)
            return await FromKnowledge(question, lang, hits);

        // 5) Википедия
        if (AiLlm.WikipediaOn)
        {
            var w = await FromWikipedia(question, lang);
            if (w != null) return w;
        }

        // 6) хоть что-то из базы знаний
        if (best != null && best.Score >= 1.5) return await FromKnowledge(question, lang, hits);

        LogUnanswered(question, lang);
        return lang == AiLang.Kk
            ? "Бұл сұраққа жауап таппадым. Басқаша сұрап көріңіз (мысалы: «маршрутты қалай беремін», «Н1 светофоры не үшін», «стрелка деген не») немесе оқытушыдан сұраңыз. Сұрағыңыз оқытушыға жазылып қойылды."
            : "Ответа на этот вопрос не нашёл. Спросите иначе (например: «как задать маршрут», «для чего светофор Н1», «что такое стрелка») или спросите преподавателя. Вопрос записан для преподавателя.";
    }

    // ---------------------------------------------------------------------
    //  База знаний
    // ---------------------------------------------------------------------
    async Task<string> FromKnowledge(string question, AiLang lang, List<AiKnowledge.Hit> hits)
    {
        var best = hits[0];
        var other = lang == AiLang.Kk ? AiLang.Ru : AiLang.Kk;

        // с нейросетью — коротко по 3 лучшим кускам (кроме простых готовых ответов справки)
        if (llm.Online && !(best.Chunk.Curated && best.Chunk.Text(lang).Length > 0))
        {
            var sb = new StringBuilder();
            sb.AppendLine("Ты — помощник студента железнодорожного колледжа в программе-тренажёре станции. Отвечай ТОЛЬКО по сведениям ниже.");
            sb.AppendLine("Ответь по сути в 2–5 коротких предложениях или шагах. Если ответа нет — так и скажи. Не упоминай файлы и программный код.");
            sb.AppendLine(AiLlm.LangRule(lang));
            sb.AppendLine("\nСведения:");
            foreach (var h in hits.Take(3)) sb.AppendLine("- " + (h.Chunk.Text(lang).Length > 0 ? h.Chunk.Text(lang) : h.Chunk.Text(other)));
            sb.AppendLine("\nВопрос: " + question);
            sb.AppendLine(AiLlm.LangRule(lang));
            var a = await llm.AskAsync(sb.ToString(), lang);
            if (!string.IsNullOrWhiteSpace(a)) return a;
        }

        string text = best.Chunk.Text(lang);
        if (text.Length == 0)
        {
            // на языке вопроса этого куска нет: ищем похожий кусок на нужном языке
            var alt = hits.Skip(1).FirstOrDefault(h => h.Chunk.Text(lang).Length > 0 && h.Score >= best.Score * 0.7);
            if (alt != null) text = alt.Chunk.Text(lang);
            else
                return (lang == AiLang.Kk ? "Бұл туралы материал тек орыс тілінде бар:\n" : "Этот материал есть только на казахском:\n") + AiText.Cut(best.Chunk.Text(other), 900);
        }
        // второй кусок той же темы (инструкция студента: соседние пункты)
        var second = hits.Skip(1).FirstOrDefault(h => h.Chunk != best.Chunk && h.Chunk.Source == best.Chunk.Source && h.Chunk.Title == best.Chunk.Title
                                                  && h.Score >= best.Score * 0.85 && h.Chunk.Text(lang).Length > 0);
        if (second != null && text.Length + second.Chunk.Text(lang).Length < 900) text += "\n" + second.Chunk.Text(lang);
        return AiText.Cut(text, 1200);
    }

    // ---------------------------------------------------------------------
    //  Википедия
    // ---------------------------------------------------------------------
    async Task<string> FromWikipedia(string question, AiLang lang)
    {
        string query = AiWiki.CleanQuery(question);
        if (query.Length == 0) query = question;
        bool kk = lang == AiLang.Kk;
        try
        {
            var pages = await wiki.FindAsync(kk ? "kk" : "ru", query);
            AiWiki.Page ruPage = null;
            if (kk && pages.Count == 0)
            {
                // казахский вопрос: русская статья → её казахская версия
                var ru = await wiki.FindAsync("ru", query);
                if (ru.Count > 0)
                {
                    ruPage = ru[0];
                    var kkPage = await wiki.KazakhOfAsync(ru[0]);
                    if (kkPage != null) pages.Add(kkPage);
                }
            }
            string note = kk ? "\n\n— Уикипедия бойынша, оқытушы тексермеген" : "\n\n— по Википедии, преподаватель не проверял";
            if (llm.Online && (pages.Count > 0 || ruPage != null))
            {
                var src = pages.Count > 0 ? pages : new List<AiWiki.Page> { ruPage };
                var sb = new StringBuilder();
                sb.AppendLine("Ты — помощник по железной дороге для студентов колледжа. Отвечай ТОЛЬКО по фрагментам Википедии ниже, 2–4 коротких предложения. " +
                              "Фрагменты могут быть на другом языке — передай смысл на языке вопроса. Ничего не выдумывай.");
                sb.AppendLine(AiLlm.LangRule(lang));
                foreach (var p in src.Take(2)) sb.AppendLine($"\nСтатья «{p.Title}»:\n{AiWiki.Paragraphs(p.Text, question, 900)}");
                sb.AppendLine("\nВопрос: " + question);
                sb.AppendLine(AiLlm.LangRule(lang));
                var a = await llm.AskAsync(sb.ToString(), lang);
                if (!string.IsNullOrWhiteSpace(a)) return a + note;
            }
            if (pages.Count > 0)
            {
                var body = AiWiki.Sentences(pages[0].Text, query, 2, 450);
                if (body.Length > 0) return body + note;
            }
            if (ruPage != null)
            {
                var body = AiWiki.Sentences(ruPage.Text, query, 2, 450);
                if (body.Length > 0)
                    return "Қазақ Уикипедиясында бұл туралы мақала жоқ. Орыс Уикипедиясында былай жазылған:\n" + body + note;
            }
        }
        catch { }
        return null;
    }

    // ---------------------------------------------------------------------
    //  Ошибки в программе
    // ---------------------------------------------------------------------
    async Task<string> AboutProgram(string question, AiLang lang)
    {
        var refusal = AiStation.RecentRefusal(TimeSpan.FromMinutes(10));
        if (llm.Online)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Ты — помощник студента в программе-тренажёре станции. Программа отказала студенту или он ошибся. " +
                          "Одной фразой назови причину, затем не больше 3–4 коротких шагов, что сделать. Опирайся ТОЛЬКО на сведения ниже.");
            sb.AppendLine(AiLlm.LangRule(lang));
            if (refusal != null) sb.AppendLine("\nОтказ программы: " + refusal + "\nСправка преподавателя: " + (Hint(refusal, AiLang.Ru) ?? "—"));
            sb.AppendLine("\nСостояние станции:\n" + AiStation.State());
            sb.AppendLine("\nПоследние действия:\n" + string.Join("\n", AiStation.LastActions(10)));
            sb.AppendLine("\nВопрос: " + question);
            sb.AppendLine(AiLlm.LangRule(lang));
            var a = await llm.AskAsync(sb.ToString(), lang);
            if (!string.IsNullOrWhiteSpace(a)) return a;
        }
        if (refusal == null)
        {
            // отказа не было — может, вопрос «почему не переводится стрелка?» есть в справке
            var hits = kb.Search(question);
            if (hits.Count > 0 && hits[0].Score >= 2.5) return await FromKnowledge(question, lang, hits);
            return lang == AiLang.Kk
                ? "Соңғы уақытта бағдарлама бас тартқан жоқ. Не істелмей жатқанын нақтырақ жазыңыз (мысалы: «" + AiProfile.RouteExampleKk + " неге берілмейді»)."
                : "Отказов программы в последнее время не было. Опишите точнее, что не получается (например: «почему не задаётся " + AiProfile.RouteExampleRu + "»).";
        }
        return RefusalHint(refusal, lang);
    }

    /// <summary>Подсказка, которую окно ЖИ показывает само, когда программа отказала.</summary>
    public string RefusalHint(string refusal) => RefusalHint(refusal, LastLang);

    static string RefusalHint(string refusal, AiLang lang)
    {
        var hint = Hint(refusal, lang) ?? (lang == AiLang.Kk
            ? "Сызбадан маршруттағы бос емес учаскелерді және стрелкалардың қалпын қараңыз."
            : "Посмотрите на схеме занятые участки и положение стрелок на пути маршрута.");
        return lang == AiLang.Kk ? "⚠ " + KkObjects(refusal) + hint : "⚠ " + refusal + "\n" + hint;
    }

    static readonly Regex ObjRx = new(@"(участ\w*|стрелк\w*|светофор\w*)\s+([^\s,:;]+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Объекты из русского текста отказа по-казахски: «участок 3СП занят» → «(3СП учаскесі) ».</summary>
    public static string KkObjects(string ru)
    {
        var parts = ObjRx.Matches(ru).Select(m =>
        {
            var noun = m.Groups[1].Value.ToLowerInvariant();
            var name = m.Groups[2].Value.Trim('.', '«', '»');
            return name + (noun.StartsWith("участ") ? " учаскесі" : noun.StartsWith("стрел") ? " стрелкасы" : " светофоры");
        }).Distinct().ToList();
        return parts.Count == 0 ? "" : "(" + string.Join(", ", parts) + ") ";
    }

    /// <summary>Казахская подсказка к русскому тексту отказа (для описания маршрута).</summary>
    public static string HintKk(string refusal) => Hint(refusal, AiLang.Kk);

    /// <summary>Подсказка преподавателя (Knowledge\Подсказки_по_ошибкам.txt): первая строка, где слова слева есть в тексте отказа.</summary>
    static string Hint(string refusal, AiLang lang)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Knowledge", AiDefaults.HintsFile);
            var text = File.Exists(path) ? AiText.DecodeSmart(File.ReadAllBytes(path)) : AiDefaults.Hints;
            var low = refusal.ToLowerInvariant();
            foreach (var raw in text.Replace("\r", "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith("#")) continue;
                var cols = line.Split('|');
                if (cols.Length < 2) continue;
                bool hit = cols[0].Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                                  .Any(k => k.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).All(p => low.Contains(p.ToLowerInvariant())));
                if (!hit) continue;
                if (lang == AiLang.Kk) return cols.Length > 2 && cols[2].Trim().Length > 0 ? cols[2].Trim() : null;
                return cols[1].Trim();
            }
        }
        catch { }
        return null;
    }

    void LogUnanswered(string q, AiLang lang)
    {
        try
        {
            File.AppendAllText(Path.Combine(kb.KnowledgePath, "_Вопросы_без_ответа.txt"),
                $"{DateTime.Now:dd.MM.yyyy HH:mm} | {(lang == AiLang.Kk ? "kk" : "ru")} | {q}\r\n", new UTF8Encoding(false));
        }
        catch { }
    }

    public void Dispose() => llm.Dispose();
}
