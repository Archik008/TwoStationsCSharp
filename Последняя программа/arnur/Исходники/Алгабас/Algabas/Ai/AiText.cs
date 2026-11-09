using System.Text;
using System.Text.RegularExpressions;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: ЯЗЫК ВОПРОСА И «СМЫСЛОВЫЕ СЛОВА»  [ДОБАВЛЕНО 28.10.2026, ЖИ-помощник Тұмара, перенесён из Station LAB]
//  Почему ЖИ не понимал «маршрутты қалай жасайды»: в справке искалась ТОЧНАЯ фраза («как задать маршрут»).
//  Теперь вопрос и справка разбираются на «понятия»: у каждого слова берётся основа (маршрутты → маршрут),
//  а слова-синонимы на двух языках сводятся к одному понятию (жасау = құру = беру = задать = сделать = поставить).
//  Совпадение считается по понятиям, поэтому вопрос можно задать любыми словами, по-русски или қазақша.
// =====================================================================

public enum AiLang { Ru, Kk }

public static class AiText
{
    static readonly Regex Word = new(@"[\p{L}\p{Nd}]+(?:[-/][\p{L}\p{Nd}]+)*", RegexOptions.Compiled);
    const string KzLetters = "әғқңөұүһіӘҒҚҢӨҰҮҺІ";

    /// <summary>Казахские слова без особых казахских букв — по ним тоже узнаём язык («маршрут деген не»).</summary>
    static readonly HashSet<string> KkWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "неге", "жоқ", "бар", "және", "туралы", "маған", "керек", "болады", "мен", "сен", "ол", "осы", "ма", "ме", "ба", "бе",
        "па", "пе", "деген", "кім", "деп", "бер", "беру", "айтып", "неден", "нені", "болса", "жасау", "жасайды", "жасаймын",
        "шығады", "түсіндір", "мына", "мынау", "немесе", "сосын", "содан", "кейін", "дейін", "жол", "жолға", "жолы", "басу",
        "басып", "бас", "тартады", "тартты", "береді", "беремін", "бер", "алу", "алып", "салу", "жабу", "ашу", "тоқта",
    };

    public static AiLang DetectLang(string s)
    {
        foreach (var ch in s)
            if (KzLetters.IndexOf(ch) >= 0) return AiLang.Kk;
        foreach (Match m in Word.Matches(s.ToLowerInvariant()))
            if (KkWords.Contains(m.Value)) return AiLang.Kk;
        return AiLang.Ru;
    }

    /// <summary>Слова-пустышки: по ним не ищем.</summary>
    static readonly HashSet<string> Stop = new(StringComparer.OrdinalIgnoreCase)
    {
        "что", "это", "где", "для", "при", "или", "такое", "такой", "какой", "какая", "какие", "каким", "мне", "мой", "моя",
        "его", "её", "они", "она", "оно", "был", "была", "было", "быть", "есть", "надо", "нужно", "можно", "чтобы", "если",
        "тоже", "уже", "ещё", "еще", "вот", "там", "тут", "так", "все", "всё", "весь", "этот", "эта", "эти", "этом", "этой",
        "про", "под", "над", "без", "через", "после", "перед", "между", "который", "которая", "которые", "скажи", "расскажи",
        "объясни", "пожалуйста", "подскажи", "хочу", "делает", "значит",
        "не", "бұл", "осы", "мына", "мынау", "және", "мен", "бен", "пен", "ма", "ме", "ба", "бе", "па", "пе", "туралы",
        "маған", "айтшы", "айтып", "бер", "берші", "керек", "деген", "дегеніміз", "қандай", "қай", "нені", "неге", "сол",
        "ол", "оны", "оның", "бар", "жоқ", "болады", "болса", "өтінемін", "түсіндірші",
    };

    /// <summary>
    /// Понятия: слова (начала слов) на двух языках, которые значат одно и то же. Слово вопроса относится к понятию,
    /// если начинается с одного из этих начал. Понятие «generic» — служебное (как, задать…): оно помогает,
    /// но одного его мало для ответа.
    /// </summary>
    static readonly (string id, bool generic, string[] starts)[] Concepts =
    {
        ("ROUTE", false, new[] { "маршрут" }),
        ("SET", true, new[] { "задат", "задаё", "задае", "задаю", "задал", "задан", "зада", "сдела", "делат", "созда", "постав", "ставит", "ставл", "набра",
                              "набир", "набор", "устан", "жаса", "құру", "құрад", "құрыл", "құрса", "бер", "беру", "берем", "берес", "береді", "берді",
                              "беріл", "берг", "берш", "қою", "қоям", "қояд", "қойы", "қойд", "қой", "орнат" }),
        ("CANCEL", false, new[] { "отмен", "снят", "сним", "убра", "убир", "болдырма", "жою", "жой", "алып таста", "өшір" }),
        ("HOW", true, new[] { "как", "қалай", "каким образом", "порядок", "тәртіб" }),
        ("WHY", true, new[] { "почему", "неге", "отчего", "зачем" }),
        ("WHAT", true, new[] { "что так", "что за", "означа", "білдір", "деген не", "что это" }),
        ("SIGNAL", false, new[] { "светофор", "сигнал", "бағдарш" }),
        ("SWITCH", false, new[] { "стрелк", "стрелоч", "бағыттам", "стрелка" }),
        ("SECTION", false, new[] { "участ", "учаск", "рельсов" }),
        ("TRACK", false, new[] { "путь", "пути", "путём", "путем", "жол" }),
        ("LESSON", false, new[] { "заняти", "занятий", "преподав", "оқытушы", "урок", "сабақ" }),
        ("OCC", false, new[] { "занят", "занима", "бос емес", "бос ем" }),
        ("FREE", false, new[] { "свобод", "освобод", "босат", "бос" }),
        ("MOVE", false, new[] { "перевест", "перевод", "перевед", "ауыстыр", "ауысты", "переключ" }),
        ("TRAIN", false, new[] { "поезд", "пойыз", "состав" }),
        ("SHUNT", false, new[] { "маневр", "манёвр" }),
        ("INVITE", false, new[] { "пригласит", "шақыру" }),
        ("DIR", false, new[] { "направлен", "бағыт" }),
        ("LINE", false, new[] { "перегон", "аралық" }),
        ("IR", false, new[] { "разделк", "ажырат", "ир" }),
        ("JOURNAL", false, new[] { "журнал", "ду-46", "ду46", "ду-2", "ду-47", "ду-58", "ду-60" }),
        ("MODE", false, new[] { "режим", "параметр" }),
        ("LAMP", false, new[] { "ламп", "шам", "огон", "огн" }),
        ("KEY", false, new[] { "ключ", "жезл", "кілт" }),
        ("CROSSING", false, new[] { "переезд" }),
        ("PNP", false, new[] { "пнп", "необщего" }),
        ("ERROR", false, new[] { "ошибк", "отказ", "қате", "бас тарт", "неисправ", "ақау" }),
        ("RADIO", false, new[] { "радио" }),
        ("WARNING", false, new[] { "предупрежд", "ескерт" }),
        ("BLANK", false, new[] { "бланк" }),
        ("SCHEDULE", false, new[] { "расписан", "кесте", "гид" }),
        ("SCORE", false, new[] { "оценк", "балл", "бағала" }),
        ("BOARD", false, new[] { "arduino", "ардуино", "макет", "плат" }),
        ("CHECK", false, new[] { "проверк", "провер", "тексер" }),
        ("OPEN", false, new[] { "открыт", "откро", "открыв", "ашу", "ашыл", "ашы" }),
        ("CLOSE", false, new[] { "закры", "закро", "жабу", "жабы", "жап" }),
        ("BUTTON", false, new[] { "кнопк", "батырма" }),
        ("CLICK", true, new[] { "клик", "щелк", "щёлк", "нажа", "нажм", "басу", "басы", "басың" }),
        ("SCHEME", false, new[] { "схем", "сызба" }),
        ("LOCK", false, new[] { "замыка", "замкн", "запер", "тұйықта", "құлыпта" }),
        ("AUTO", false, new[] { "автодейств", "автоблок", "адч", "адн" }),
        ("WAGON", false, new[] { "вагон" }),
        ("SHOE", false, new[] { "башмак", "закреп" }),
        ("RECOVERY", false, new[] { "восстанов", "сход", "қалпына" }),
        ("SPEED", false, new[] { "скорост", "жылдамд" }),
        ("WINDOW", false, new[] { "окно", "окна", "терезе" }),
        ("RED", false, new[] { "красн", "қызыл" }),
        ("GREEN", false, new[] { "зелен", "зелён", "жасыл" }),
        ("YELLOW", false, new[] { "желт", "жёлт", "сары" }),
        ("WHITE", false, new[] { "белы", "бело", "белом", "лунно", "ақ" }),
        ("BLUE", false, new[] { "сини", "синег", "синим", "көк" }),
        ("LANG", false, new[] { "язык", "тіл", "қазақ", "казах" }),
        ("AI", false, new[] { "жи", "ии", "помощник" }),   // «көмекші» нет: это и «көмекші ауыстыру» (вспомогательный)
    };

    /// <summary>Понятия, которые сами по себе не тема (их одних мало для ответа).</summary>
    public static bool IsGeneric(string concept) => Concepts.Any(c => c.id == concept && c.generic);

    /// <summary>Вес служебного понятия: вопросительные слова почти ничего не весят, «задать» — немного.</summary>
    public static double GenericWeight(string concept) => concept == "SET" ? 0.6 : 0.15;

    public static IEnumerable<string> Words(string s)
    {
        foreach (Match m in Word.Matches((s ?? "").ToLowerInvariant().Replace('ё', 'е')))
            yield return m.Value;
    }

    /// <summary>Основа слова: русские и казахские окончания отрезаются грубо — берутся первые 5 букв.</summary>
    public static string Stem(string w) => w.Length > 5 ? w[..5] : w;

    /// <summary>Понятия текста: каждое слово → понятие (если оно в словаре синонимов) или «w:основа».</summary>
    public static HashSet<string> ConceptsOf(string text)
    {
        var res = new HashSet<string>();
        string low = " " + (text ?? "").ToLowerInvariant().Replace('ё', 'е') + " ";
        // двухсловные начала («бос емес», «алып таста») ищем в тексте целиком
        foreach (var (id, _, starts) in Concepts)
            foreach (var st in starts)
                if (st.Contains(' ') && low.Contains(" " + st)) res.Add(id);
        foreach (var w in Words(text))
        {
            if (Stop.Contains(w)) continue;
            string? c = ConceptOf(w);
            if (c != null) { res.Add(c); continue; }
            if (w.Length < 3 && !w.Any(char.IsDigit)) continue;
            res.Add("w:" + Stem(w));
        }
        return res;
    }

    static string? ConceptOf(string w)
    {
        foreach (var (id, _, starts) in Concepts)
            foreach (var st in starts)
            {
                if (st.Contains(' ')) continue;
                // короткие начала («ир», «жол», «бос», «ақ») — только целым словом или с казахским окончанием
                if (st.Length <= 3 ? (w == st || (w.StartsWith(st) && w.Length <= st.Length + 4 && st.Length == 3 && KzTail(w[st.Length..])))
                                   : w.StartsWith(st))
                    return id;
            }
        return null;
    }

    static readonly string[] KzTails = { "ға", "ге", "қа", "ке", "да", "де", "та", "те", "ды", "ді", "ты", "ті", "ның", "нің", "дың", "дің",
                                         "тың", "тің", "дан", "ден", "тан", "тен", "нан", "нен", "ы", "і", "сы", "сі", "ын", "ін", "на", "не", "ына", "іне", "ында",
                                         "інде", "мен", "ымен", "імен", "дар", "дер", "тар", "тер", "у" };
    static bool KzTail(string rest) => rest.Length == 0 || KzTails.Contains(rest);

    /// <summary>Строка файла в нужной кодировке (UTF-8 с BOM / без, UTF-16, Windows-1251).</summary>
    public static string DecodeSmart(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return new UTF8Encoding(false).GetString(b, 3, b.Length - 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE) return Encoding.Unicode.GetString(b, 2, b.Length - 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(b, 2, b.Length - 2);
        try { return new UTF8Encoding(false, true).GetString(b); }
        catch (DecoderFallbackException)
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(1251).GetString(b);
        }
    }

    /// <summary>Похож ли текст на нужный язык (для ответов нейросети).</summary>
    public static bool LangOk(string text, AiLang lang)
    {
        int letters = text.Count(char.IsLetter);
        if (letters < 40) return true;
        int kz = text.Count(c => KzLetters.IndexOf(c) >= 0);
        if (lang == AiLang.Ru) return kz * 100.0 / letters <= 1.5;
        return kz * 100.0 / letters >= 3;
    }

    /// <summary>Обрезать по концу предложения.</summary>
    public static string Cut(string s, int max)
    {
        s = s.Trim();
        if (s.Length <= max) return s;
        int cut = s.LastIndexOf('.', max - 1);
        return cut > max / 2 ? s[..(cut + 1)] : s[..max] + "…";
    }
}
