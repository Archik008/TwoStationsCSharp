using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: ВИКИПЕДИЯ  [ДОБАВЛЕНО 28.10.2026, перенесено из Station LAB, Ui/WikiAssistant.cs]
//  Только статьи про железную дорогу. Для казахского вопроса: казахская Википедия; если там не нашлось —
//  русская статья и её казахская версия (межъязыковая ссылка). Нет казахской версии: с нейросетью — пересказ
//  по-казахски, без неё — честно говорим и даём русский текст отдельным абзацем.
// =====================================================================

public sealed class AiWiki
{
    public sealed record Page(string Lang, string Title, string Text);

    static readonly HttpClient Http = Create();
    static HttpClient Create()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("StationApp-AKZHT/1.0 (college training project)");
        return h;
    }

    static readonly Regex RailWords = new(
        @"железн|рельс|поезд|локомотив|тепловоз|электровоз|вагон|перегон|стрелочн|светофор|семафор|путев|депо|сцб|централизац|блокировк|метрополитен|темір жол|пойыз|вагон|бағдарш|станция",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex LeadNoise = new(@"^\s*(что такое|что значит|что означает|кто такой|кто такая|расскажи(те)?( мне)?( про| о| об)?|объясни(те)?( мне)?|скажи(те)?|как называется)\s+",
                                          RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex TailNoise = new(@"\s*(деген не|деген кім|деген нәрсе|дегеніміз не|туралы айтып бер|түсіндір(ші)?|не)\s*[?!.]*\s*$",
                                          RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex SentenceSplit = new(@"(?<=[.!?…])\s+(?=[A-ZА-ЯЁӘҒҚҢӨҰҮҺІ«""])", RegexOptions.Compiled);
    static readonly Regex Parens = new(@"\s*\([^()]*\)", RegexOptions.Compiled);
    readonly Dictionary<string, string> cache = new();

    public static string CleanQuery(string q)
    {
        q = LeadNoise.Replace(q.Trim(), "");
        q = TailNoise.Replace(q, "");
        return q.Trim(' ', '?', '!', '.', ',', ';', ':');
    }

    public static bool IsDefinition(string q) => LeadNoise.IsMatch(q.Trim()) || TailNoise.IsMatch(q.Trim());

    public async Task<bool> PingAsync()
    {
        try
        {
            using var cts = new CancellationTokenSource(5000);
            using var r = await Http.GetAsync("https://ru.wikipedia.org/w/api.php?action=query&meta=siteinfo&format=json", cts.Token);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Статьи про железную дорогу на языке wl (до 2).</summary>
    public async Task<List<Page>> FindAsync(string wl, string query)
    {
        var pages = await Find(wl, query);
        if (pages.Count == 0 && !RailWords.IsMatch(query))
            pages = await Find(wl, query + (wl == "kk" ? " темір жол" : " железная дорога"));
        return pages;
    }

    async Task<List<Page>> Find(string wl, string query)
    {
        var titles = (await Search(wl, query)).Take(3).ToList();
        var texts = await Task.WhenAll(titles.Select(t => Extract(wl, t)));
        var res = new List<Page>();
        for (int i = 0; i < titles.Count; i++)
        {
            var text = texts[i];
            if (string.IsNullOrWhiteSpace(text)) continue;
            var head = text.Length > 3000 ? text[..3000] : text;
            if (RailWords.IsMatch(titles[i]) || RailWords.Matches(head).Count >= 3) res.Add(new Page(wl, titles[i], text));
            if (res.Count >= 2) break;
        }
        return res;
    }

    /// <summary>Казахская версия русской статьи (межъязыковая ссылка), если она есть.</summary>
    public async Task<Page> KazakhOfAsync(Page ru)
    {
        try
        {
            var url = $"https://ru.wikipedia.org/w/api.php?action=query&prop=langlinks&lllang=kk&titles={Uri.EscapeDataString(ru.Title)}&format=json&utf8=1";
            using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
            foreach (var p in doc.RootElement.GetProperty("query").GetProperty("pages").EnumerateObject())
                if (p.Value.TryGetProperty("langlinks", out var ll))
                    foreach (var l in ll.EnumerateArray())
                        if (l.TryGetProperty("*", out var t) && !string.IsNullOrWhiteSpace(t.GetString()))
                        {
                            var text = await Extract("kk", t.GetString());
                            if (text.Length > 40) return new Page("kk", t.GetString(), text);
                        }
        }
        catch { }
        return null;
    }

    static async Task<List<string>> Search(string wl, string query)
    {
        var url = $"https://{wl}.wikipedia.org/w/api.php?action=query&list=search&srsearch={Uri.EscapeDataString(query)}&srlimit=5&format=json&utf8=1";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        var res = new List<string>();
        if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("search", out var arr))
            foreach (var it in arr.EnumerateArray())
                if (it.TryGetProperty("title", out var t) && !string.IsNullOrWhiteSpace(t.GetString())) res.Add(t.GetString());
        return res;
    }

    async Task<string> Extract(string wl, string title)
    {
        string key = wl + "|" + title;
        lock (cache) if (cache.TryGetValue(key, out var c)) return c;
        var url = $"https://{wl}.wikipedia.org/w/api.php?action=query&prop=extracts&explaintext=1&redirects=1&titles={Uri.EscapeDataString(title)}&format=json&utf8=1";
        using var doc = JsonDocument.Parse(await Http.GetStringAsync(url));
        string text = "";
        if (doc.RootElement.TryGetProperty("query", out var q) && q.TryGetProperty("pages", out var pages))
            foreach (var p in pages.EnumerateObject())
                if (p.Value.TryGetProperty("extract", out var e)) { text = e.GetString() ?? ""; break; }
        lock (cache) { if (cache.Count > 40) cache.Clear(); cache[key] = text; }
        return text;
    }

    // ---------- выбор текста ----------
    static IEnumerable<string> Tok(string s) => AiText.Words(s).Where(w => w.Length >= 3).Select(AiText.Stem);

    /// <summary>Для нейросети: до трёх подходящих абзацев.</summary>
    public static string Paragraphs(string text, string question, int max)
    {
        var qt = Tok(question).ToHashSet();
        var paras = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length >= 40 && !l.StartsWith("=")).ToList();
        if (paras.Count == 0) return "";
        var chosen = paras.Select((p, i) => (i, s: Tok(p).Distinct().Count(qt.Contains) + (i == 0 ? 1.5 : 0)))
                          .Where(x => x.s > 0).OrderByDescending(x => x.s).Take(3).Select(x => x.i).OrderBy(i => i).DefaultIfEmpty(0).ToList();
        var sb = new StringBuilder();
        foreach (var i in chosen)
        {
            var p = Clean(AiText.Cut(paras[i], 600));
            if (sb.Length > 0 && sb.Length + p.Length > max) break;
            sb.AppendLine(p).AppendLine();
        }
        return sb.ToString().Trim();
    }

    /// <summary>Без нейросети: 1–2 самых подходящих предложения (на «что такое…» — определение из начала статьи).</summary>
    public static string Sentences(string text, string question, int maxSentences, int maxChars)
    {
        var paras = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length >= 40 && !l.StartsWith("=")).Take(30).ToList();
        var sentences = paras.SelectMany((p, pi) => SentenceSplit.Split(p).Select(s => (pi, s: Clean(s)))).Where(x => x.s.Length >= 25).ToList();
        if (sentences.Count == 0) return "";
        List<string> chosen;
        if (IsDefinition(question)) chosen = sentences.Take(maxSentences).Select(x => x.s).ToList();
        else
        {
            var qt = Tok(question).ToHashSet();
            chosen = sentences.Select((x, i) => (i, x.s, sc: Tok(x.s).Distinct().Count(qt.Contains) + (x.pi == 0 ? 0.5 : 0)))
                              .Where(x => x.sc > 0.5).OrderByDescending(x => x.sc).ThenBy(x => x.i).Take(maxSentences)
                              .OrderBy(x => x.i).Select(x => x.s).ToList();
            if (chosen.Count == 0) chosen = sentences.Take(1).Select(x => x.s).ToList();
        }
        return AiText.Cut(string.Join(" ", chosen), maxChars);
    }

    static string Clean(string s)
    {
        s = s.Replace("́", "");
        s = Parens.Replace(s, "");
        s = Regex.Replace(s, @"\s{2,}", " ");
        s = Regex.Replace(s, @"\s+([,.;:!?])", "$1");
        return s.Trim();
    }
}
