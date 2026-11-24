using System.Text;
using System.Text.RegularExpressions;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ-ПОМОЩНИК: ВОПРОС → ОТВЕТ  [ДОБАВЛЕНО 28.10.2026, по Station LAB Тұмара (WikiAssistant + StationGuide)]
//  [ИЗМЕНЕНО 24.11.2026] На вопросы отвечает ТОЛЬКО нейросеть (Google Gemini; при Provider = ollama — Ollama), коротко.
//  Готовые тексты больше не выдаются — они идут нейросети как сведения:
//   • справка программы (база знаний Knowledge + инструкция студента, на двух языках) — 3 ближайших куска;
//   • маршрут с названиями («маршрут Н на путь 3») — шаги по модели программы (AiStation.HowToRoute);
//   • объект схемы («для чего светофор Н1», «что это?» по наведению мыши) — описание по модели (AiStation.AboutObject);
//   • вопрос «не задаётся / ошибка / почему?» — последний отказ программы, подсказка преподавателя, последние действия;
//   • состояние станции (поезда, стрелки, все светофоры, маршруты) и, у Gemini, подходящие куски кода программы.
//  Википедии нет. ИИ только консультирует — ничего не меняет на станции и в программе.
//  ИИ не подключён (нет ключа, нет связи) — сообщение об этом; лимит Gemini исчерпан — сообщение о лимите.
//  Вопрос без ответа записывается в Knowledge\_Вопросы_без_ответа.txt (преподаватель допишет справку).
// =====================================================================

public sealed class AiAssistant : IDisposable
{
    readonly AiKnowledge kb = new();
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

    /// <summary>Ответ на вопрос — только нейросетью (сведения программы ей в помощь).</summary>
    public async Task<string> AskAsync(string question)
    {
        question = (question ?? "").Trim();
        if (question.Length == 0) return "";
        var lang = AiText.DetectLang(question);
        LastLang = lang;
        bool kk = lang == AiLang.Kk;
        llm.LimitHit = false;
        await llm.EnsureAsync();
        if (!llm.Online)
            return kk ? "ЖИ қосылмаған: бағдарлама жанындағы ai_settings.ini файлына Gemini кілтін (GeminiKey) жазыңыз немесе интернетті тексеріңіз."
                      : "ИИ не подключён: впишите ключ Gemini (GeminiKey) в файл ai_settings.ini рядом с программой или проверьте интернет.";

        var a = await llm.AskAsync(Prompt(question, lang), lang);
        if (llm.LimitHit)
            return kk ? "⚠ ЖИ-ді пайдалану лимиті таусылды. Біраз уақыттан кейін қайта сұраңыз."
                      : "⚠ Лимит использования ИИ исчерпан. Попробуйте спросить позже.";
        if (!string.IsNullOrWhiteSpace(a)) return a;
        LogUnanswered(question, lang);
        return llm.Online
            ? (kk ? "ЖИ жауап бере алмады. Сұрақты басқаша қойып көріңіз." : "ИИ не смог ответить. Попробуйте спросить иначе.")
            : (kk ? "ЖИ-мен байланыс жоқ. Интернетті тексеріп, кейінірек сұраңыз." : "Нет связи с ИИ. Проверьте интернет и спросите позже.");
    }

    /// <summary>Запрос нейросети: всё, что программа знает по вопросу, — сведения; отвечает нейросеть, коротко.</summary>
    string Prompt(string question, AiLang lang)
    {
        var other = lang == AiLang.Kk ? AiLang.Ru : AiLang.Kk;
        var sb = new StringBuilder();
        sb.AppendLine("Ответь студенту коротко и по сути (1–4 предложения или до 4 шагов). Ты только консультируешь — ничего не меняешь. " +
                      "Опирайся на сведения ниже; сведения, не относящиеся к вопросу, не пересказывай.");
        sb.AppendLine(AiLlm.LangRule(lang));

        // ошибка / «почему?» после отказа программы
        bool shortWhy = WhyWords.IsMatch(question) && question.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 6
                        && AiStation.RecentRefusal(TimeSpan.FromMinutes(3)) != null;
        string refusal = null;
        if (ErrorWords.IsMatch(question) || shortWhy)
        {
            refusal = AiStation.RecentRefusal(TimeSpan.FromMinutes(10));
            if (refusal != null)
                sb.AppendLine("\nПоследний отказ программы: " + refusal + "\nПодсказка преподавателя к нему: " + (Hint(refusal, AiLang.Ru) ?? "—"));
            sb.AppendLine("\nПоследние действия студента:\n" + string.Join("\n", AiStation.LastActions(10)));
        }

        // маршрут с названиями и объект схемы — сведения по модели программы
        var route = AiStation.HowToRoute(question, lang);
        if (route != null) sb.AppendLine("\nКак задаётся этот маршрут (по модели программы):\n" + route);
        var obj = AiStation.AboutObject(question, lang);
        if (obj != null) sb.AppendLine("\nОб объекте схемы, о котором спрашивают (по модели программы):\n" + obj);

        // справка программы
        var hits = kb.Search(question);
        if (hits.Count > 0)
        {
            sb.AppendLine("\nСправка программы (может не подходить к вопросу):");
            foreach (var h in hits.Take(3)) sb.AppendLine("- " + (h.Chunk.Text(lang).Length > 0 ? h.Chunk.Text(lang) : h.Chunk.Text(other)));
        }

        var state = AiStation.State();
        if (state.Length > 0) sb.AppendLine("\nСостояние станции " + AiProfile.Station + " сейчас:\n" + state);
        if (AiLlm.UseGemini) AppendCode(sb, question + (refusal != null ? " " + refusal : ""));
        sb.AppendLine("\nВопрос: " + question);
        sb.AppendLine(AiLlm.LangRule(lang));
        return sb.ToString();
    }

    /// <summary>[ДОБАВЛЕНО 24.11.2026] Подходящие куски кода программы (вшиты в exe) — в запрос Gemini.</summary>
    static void AppendCode(StringBuilder sb, string question)
    {
        var code = AiCode.Find(question);
        if (code.Length == 0) return;
        sb.AppendLine("\nФрагменты исходного кода программы (C#) — только чтобы понять, как она работает; код не показывай, правок не предлагай:");
        sb.AppendLine(code);
    }

    // ---------------------------------------------------------------------
    //  Отказ программы — строка в окне ЖИ сама (без готовых советов: «почему?» объяснит нейросеть)
    // ---------------------------------------------------------------------
    /// <summary>Что показать в окне ЖИ, когда программа отказала: сам отказ (по-казахски — с названиями объектов).</summary>
    public string RefusalHint(string refusal) => LastLang == AiLang.Kk ? "⚠ " + KkObjects(refusal) + refusal : "⚠ " + refusal;

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
