using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: КОД ПРОГРАММЫ ДЛЯ GEMINI  [ДОБАВЛЕНО 24.11.2026]
//  Исходники программы (*.cs) вшиты в exe при сборке (цель EmbedSourcesForAi в .csproj, ресурсы «aisrc/…»).
//  Здесь — поиск кусков кода, подходящих к вопросу: код режется на куски по ~50 строк, слова вопроса (по-русски,
//  по-казахски) сравниваются со словами кода и комментариев; русские слова ещё и переводятся в имена из кода
//  («маршрут» → route, «стрелка» → switch…). Целиком код не отправляется — он большой (≈300 тыс. токенов).
//  Только читает: ничего в программе не меняет.
// =====================================================================

public static class AiCode
{
    sealed class Chunk
    {
        public string File;
        public int From, To;              // строки, с 1
        public string Text;
        public HashSet<string> Terms;
    }

    static List<Chunk> chunks;
    static Dictionary<string, int> df;    // в скольких кусках встречается слово
    static readonly object lk = new();

    /// <summary>Сколько файлов кода вшито в программу (0 — нет: собрано без исходников).</summary>
    public static int FileCount { get { Load(); return files; } }
    static int files;

    static readonly Regex Word = new(@"[A-Za-zА-Яа-яЁёӘәҒғҚқҢңӨөҰұҮүҺһІі]{3,}", RegexOptions.Compiled);
    static readonly Regex Camel = new(@"(?<=[a-zа-яё])(?=[A-ZА-ЯЁ])", RegexOptions.Compiled);

    static readonly HashSet<string> Stop = new(StringComparer.Ordinal)
    {
        "что", "как", "это", "для", "где", "или", "при", "нет", "так", "его", "она", "они", "все", "всё", "уже", "там", "тут",
        "почему", "зачем", "когда", "какой", "какая", "какие", "есть", "можно", "нужно", "если", "чтобы", "только", "программ",
        "the", "and", "var", "new", "return", "public", "static", "void", "string", "int", "bool", "using", "class", "private",
        "readonly", "this", "null", "true", "false", "else", "foreach", "summary", "param", "get", "set", "out", "float", "double",
        "қалай", "неге", "деген", "және", "бұл", "үшін",
    };

    /// <summary>Русские / казахские слова вопроса → имена из кода.</summary>
    static readonly (string Ru, string[] Code)[] Syn =
    {
        ("маршру", new[] { "route", "routes" }), ("стрелк", new[] { "switch", "switches" }), ("светоф", new[] { "signal", "signals", "aspect" }),
        ("участ", new[] { "section", "sections" }), ("поезд", new[] { "train", "trains" }), ("пойыз", new[] { "train", "trains" }),
        ("занят", new[] { "occupied" }), ("перего", new[] { "line", "lines" }), ("отказ", new[] { "refuse", "refused" }),
        ("пригла", new[] { "invite" }), ("автобл", new[] { "automatic" }), ("ардуин", new[] { "arduino" }),
        ("размык", new[] { "release", "releasing" }), ("разделк", new[] { "release", "artificial" }), ("замык", new[] { "lock", "locked" }),
        ("полос", new[] { "strip" }), ("горочн", new[] { "hump" }), ("маневр", new[] { "shunt", "shunting" }), ("манёвр", new[] { "shunt", "shunting" }),
        ("входн", new[] { "entry" }), ("выходн", new[] { "exit" }), ("показа", new[] { "aspect" }), ("огон", new[] { "lamp", "aspect" }),
        ("бағдар", new[] { "signal", "signals" }), ("бағытт", new[] { "switch", "switches" }), ("жол", new[] { "track", "section" }),
        ("перевод", new[] { "move", "switch" }), ("неиспра", new[] { "fault", "faults" }), ("журнал", new[] { "log", "journal" }),
        ("расписа", new[] { "schedule" }), ("соседн", new[] { "neighbor", "dispatch" }), ("диспетч", new[] { "dispatch" }),
        ("скорост", new[] { "speed" }), ("отправ", new[] { "depart", "exit" }), ("приём", new[] { "entry", "arrival" }), ("прием", new[] { "entry", "arrival" }),
    };

    static string Stem(string w) => w.Length > 6 && Regex.IsMatch(w, "^[а-яёәғқңөұүһі]") ? w[..6] : w;

    static IEnumerable<string> Terms(string text)
    {
        foreach (Match m in Word.Matches(text))
            foreach (var part in Camel.Split(m.Value).Append(m.Value))
            {
                var w = part.ToLowerInvariant();
                if (w.Length < 3) continue;
                w = Stem(w);
                if (!Stop.Contains(w)) yield return w;
            }
    }

    static void Load()
    {
        lock (lk)
        {
            if (chunks != null) return;
            chunks = new List<Chunk>();
            df = new Dictionary<string, int>();
            var seen = new HashSet<string>();
            foreach (var asm in new[] { Assembly.GetEntryAssembly(), typeof(AiCode).Assembly }.Where(a => a != null).Distinct())
                foreach (var name in asm.GetManifestResourceNames().Where(n => n.StartsWith("aisrc/", StringComparison.Ordinal)))
                {
                    if (!seen.Add(name)) continue;
                    try
                    {
                        using var st = asm.GetManifestResourceStream(name);
                        if (st == null) continue;
                        using var rd = new StreamReader(st, Encoding.UTF8);
                        var lines = rd.ReadToEnd().Replace("\r", "").Split('\n');
                        string file = name["aisrc/".Length..].Replace('\\', '/');
                        files++;
                        for (int i = 0; i < lines.Length; i += 40)
                        {
                            int to = Math.Min(lines.Length, i + 50);
                            var text = string.Join("\n", lines[i..to]);
                            var c = new Chunk { File = file, From = i + 1, To = to, Text = text, Terms = Terms(text + " " + file).ToHashSet() };
                            chunks.Add(c);
                            foreach (var t in c.Terms) df[t] = df.TryGetValue(t, out var n) ? n + 1 : 1;
                            if (to >= lines.Length) break;
                        }
                    }
                    catch { }
                }
        }
    }

    /// <summary>Куски кода, подходящие к вопросу, — не длиннее maxChars; кода нет или ничего не нашлось — пустая строка.</summary>
    public static string Find(string question, int maxChars = 8000)
    {
        Load();
        if (chunks.Count == 0) return "";
        var q = Terms(question).Distinct().ToList();
        foreach (var w in q.ToList())
            foreach (var (ru, code) in Syn)
                if (w.StartsWith(ru, StringComparison.Ordinal)) q.AddRange(code);
        q = q.Distinct().Where(w => df.ContainsKey(w)).ToList();
        if (q.Count == 0) return "";
        double N = chunks.Count;
        double Idf(string w) => Math.Log(1 + N / df[w]);
        var best = chunks
            .Select(c => (c, score: q.Where(c.Terms.Contains).Sum(Idf) + (q.Any(w => c.File.Contains(w, StringComparison.OrdinalIgnoreCase)) ? 1.5 : 0)))
            .Where(x => x.score > 0)
            .OrderByDescending(x => x.score)
            .Take(12)
            .ToList();
        var sb = new StringBuilder();
        foreach (var (c, _) in best)
        {
            var part = $"// файл {c.File}, строки {c.From}–{c.To}\n{c.Text}\n\n";
            if (sb.Length + part.Length > maxChars) { if (sb.Length == 0) sb.Append(part[..maxChars]); break; }
            sb.Append(part);
        }
        return sb.ToString();
    }
}
