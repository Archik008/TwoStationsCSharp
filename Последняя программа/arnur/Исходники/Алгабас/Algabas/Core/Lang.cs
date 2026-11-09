using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace StationApp.Core;

// =====================================================================
//  ЯЗЫКИ ИНТЕРФЕЙСА: русский / казахский / английский  [ДОБАВЛЕНО 22.10.2026, этап 8]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса; программа «Преподаватель» подключает его ссылкой.
//
//  Как устроено (как gettext): в коде строки остаются РУССКИМИ — это ключи. Перевод — словари
//  Lang/Strings.kk.json и Lang/Strings.en.json {"русская строка": "перевод"}, вшиты в exe (EmbeddedResource,
//  LogicalName Strings.kk.json / Strings.en.json). Переводится то, что ПОКАЗЫВАЕТСЯ: подписи окон (Ui/LangUi.cs
//  делает это сам), журнал, окна сообщений, отчёты (L.Html). Логика программы русских строк не теряет.
//
//  L.T(текст) ищет перевод по порядку:
//   1) точное совпадение (без пробелов по краям; «Связь…» найдёт и «Связь»);
//   2) шаблон: ключ с дырками {0}, {1} — как в $"Поезд №{t.Number} ушёл" → «Поезд №{0} ушёл»; то, что попало
//      в дырки, переводится тоже (вложенные «маневровый», «занят»…); в переводе дырки можно переставлять;
//   3) приставки «08:01:05  », «[Занятие] » отделяются и переводятся отдельно;
//   4) многострочный текст — по строкам; составной — по разделителям «: », « — », «. », «; », «, »;
//   5) известные куски (из склейки "Студент: " + имя) заменяются внутри текста;
//   иначе текст остаётся русским. Новые строки без перевода — tools\i18n\check.py их покажет.
//  Для нового кода можно писать L.T("…") / L.F("… {0}", x) явно — ключ всё равно русский.
// =====================================================================

public static class L
{
    public static readonly string[] Codes = { "ru", "kk", "en" };
    /// <summary>Названия языков — на самих языках (не переводятся).</summary>
    public static readonly string[] Names = { "Русский", "Қазақша", "English" };

    public static string Current { get; private set; } = "ru";
    public static event Action Changed;
    static string settingsFile;

    class Template
    {
        public string Key, Value;
        public Regex Re;
        public int[] Holes;     // номер дырки по порядку групп
        public int Literal;     // длина текста без дырок (длиннее — проверяется раньше)
    }

    class Dict
    {
        public readonly Dictionary<string, string> Exact = new();
        public readonly List<Template> Templates = new();
        public (string key, string val)[] Fragments = Array.Empty<(string, string)>();
    }

    static readonly Dictionary<string, Dict> dicts = new();
    /// <summary>Последний перевод собран из кусков (шаг 5) — для шаблона такая дырка не годится.</summary>
    [ThreadStatic] static bool weak;
    static readonly Dictionary<string, string> cache = new();
    static readonly object sync = new();
    static readonly Regex Hole = new(@"\{(\d+)(?:[,:][^}]*)?\}");
    static readonly Regex Cyr = new("[А-Яа-яЁё]");
    /// <summary>Русское слово из строчных букв (не подпись вроде «Ч1», «НП», «2-3СП»).</summary>
    static readonly Regex Words = new("[а-яё]{2,}");
    static readonly Regex Prefix = new(@"^(\d{1,2}:\d\d(?::\d\d)?\s+|\[[^\]\n]{1,40}\]\s*|[•\-–—]\s+|\d+[.)]\s+)");
    static readonly string[] Separators = { "\n", ": ", " — ", ". ", "; ", ", ", " (" };

    /// <summary>Загрузить выбор языка (файл с кодом языка). Вызывать в начале Program.Main.</summary>
    public static void Init(string langFile)
    {
        settingsFile = langFile;
        try { if (langFile != null && File.Exists(langFile)) Current = Norm(File.ReadAllText(langFile).Trim()); } catch { }
    }

    static string Norm(string code) => Codes.Contains(code) ? code : "ru";

    public static void Set(string code)
    {
        code = Norm(code);
        if (code == Current) return;
        Current = code;
        lock (sync) cache.Clear();
        try
        {
            if (settingsFile != null)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settingsFile)!);
                File.WriteAllText(settingsFile, code);
            }
        }
        catch { }
        Changed?.Invoke();
    }

    public static string F(string ruFormat, params object[] args) => string.Format(T(ruFormat), args);

    /// <summary>Перевод на текущий язык (русский — как есть).</summary>
    public static string T(string s)
    {
        if (Current == "ru" || string.IsNullOrEmpty(s) || !Cyr.IsMatch(s)) return s;
        lock (sync)
        {
            if (cache.TryGetValue(s, out var r)) return r;
            var d = Load(Current);
            weak = false;
            r = Translate(d, s, 0) ?? s;
            if (cache.Count > 50000) cache.Clear();
            cache[s] = r;
            return r;
        }
    }

    /// <summary>Перевести текст HTML (между тегами, построчно); lang=… у &lt;html&gt;.</summary>
    public static string Html(string html)
    {
        if (Current == "ru" || string.IsNullOrEmpty(html)) return html;
        html = Regex.Replace(html, @"<html lang=['""]?ru['""]?", $"<html lang={Current}");
        return Regex.Replace(html, @"(?<=>)[^<]+(?=<)", m =>
        {
            var text = WebUtility.HtmlDecode(m.Value);
            if (!Cyr.IsMatch(text)) return m.Value;
            var lines = text.Split('\n').Select(T);
            return WebUtility.HtmlEncode(string.Join("\n", lines));
        });
    }

    // ------------------------------------------------------------------

    static Dict Load(string lang)
    {
        if (dicts.TryGetValue(lang, out var d)) return d;
        d = new Dict();
        dicts[lang] = d;
        try
        {
            using var st = Assembly.GetEntryAssembly()?.GetManifestResourceStream($"Strings.{lang}.json")
                        ?? typeof(L).Assembly.GetManifestResourceStream($"Strings.{lang}.json");
            if (st == null) return d;
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(st) ?? new();
            var frag = new List<(string, string)>();
            foreach (var (k, v) in map)
            {
                if (string.IsNullOrEmpty(v) || k.StartsWith("_")) continue;
                if (Hole.IsMatch(k)) AddTemplate(d, k.Trim(), v.Trim());
                else
                {
                    d.Exact[k.Trim()] = v.Trim();
                    if (k.Trim().Length >= 4 && Cyr.IsMatch(k)) frag.Add((k.Trim(), v.Trim()));
                }
            }
            d.Templates.Sort((a, b) => b.Literal.CompareTo(a.Literal));
            d.Fragments = frag.OrderByDescending(f => f.Item1.Length).ToArray();
        }
        catch { }
        return d;
    }

    static void AddTemplate(Dict d, string key, string value)
    {
        var holes = new List<int>();
        var sb = new StringBuilder("^");
        int pos = 0, literal = 0;
        foreach (Match m in Hole.Matches(key))
        {
            var lit = key[pos..m.Index];
            literal += lit.Count(char.IsLetter);
            sb.Append(Regex.Escape(lit));
            sb.Append("(.*?)");
            holes.Add(int.Parse(m.Groups[1].Value));
            pos = m.Index + m.Length;
        }
        var tail = key[pos..];
        literal += tail.Count(char.IsLetter);
        sb.Append(Regex.Escape(tail)).Append('$');
        if (literal < 2) return;   // шаблон почти без слов совпадал бы с чем угодно
        d.Templates.Add(new Template
        {
            Key = key, Value = Hole.Replace(value, m => "{" + m.Groups[1].Value + "}"), Holes = holes.ToArray(), Literal = literal,
            Re = new Regex(sb.ToString(), RegexOptions.Singleline | RegexOptions.CultureInvariant)
        });
    }

    static string Translate(Dict d, string s, int depth)
    {
        if (!Cyr.IsMatch(s)) return s;
        if (depth > 6) return null;
        // пробелы по краям сохраняются
        int a = 0, b = s.Length;
        while (a < b && char.IsWhiteSpace(s[a])) a++;
        while (b > a && char.IsWhiteSpace(s[b - 1])) b--;
        if (a > 0 || b < s.Length)
        {
            var inner = Translate(d, s[a..b], depth);
            return inner == null ? null : s[..a] + inner + s[b..];
        }
        if (d.Exact.TryGetValue(s, out var r)) return r;
        // «Связь…» ↔ «Связь», «Путь:» ↔ «Путь»
        foreach (var p in new[] { "…", "...", ":", ".", "!", "?" })
        {
            if (s.Length > p.Length && s.EndsWith(p) && d.Exact.TryGetValue(s[..^p.Length].TrimEnd(), out r)) return r + p;
            if (d.Exact.TryGetValue(s + p, out r)) return r.EndsWith(p) ? r[..^p.Length] : r;
        }
        // шаблоны
        foreach (var t in d.Templates)
        {
            var m = t.Re.Match(s);
            if (!m.Success) continue;
            var vals = new string[t.Holes.Length == 0 ? 0 : t.Holes.Max() + 1];
            bool ok = true, weakBefore = weak, weakAll = false;
            for (int i = 0; i < t.Holes.Length && ok; i++)
            {
                var g = m.Groups[i + 1].Value;
                weak = false;
                var tr = Translate(d, g, depth + 1);
                // в дырке русские слова, а перевести их не вышло или вышло только кусками — шаблон слишком общий
                // («Поезд №{0}», «{0} мин»), ищем дальше
                if (Words.IsMatch(g) && (tr == null || weak)) ok = false;
                weakAll |= weak;
                vals[t.Holes[i]] = tr ?? g;
            }
            weak = weakBefore || (ok && weakAll);
            if (!ok) continue;
            return Hole.Replace(t.Value, h => { int k = int.Parse(h.Groups[1].Value); return k < vals.Length ? vals[k] : h.Value; });
        }
        // приставка: время, [метка], маркер списка
        var pm = Prefix.Match(s);
        if (pm.Success && pm.Length < s.Length)
        {
            var head = pm.Value.StartsWith("[") ? "[" + (Translate(d, pm.Value.Trim().Trim('[', ']'), depth + 1) ?? pm.Value.Trim().Trim('[', ']')) + "]" +
                                                  pm.Value[(pm.Value.TrimEnd().Length)..] : pm.Value;
            var rest = Translate(d, s[pm.Length..], depth + 1);
            if (rest != null || head != pm.Value)
            {
                if (rest == null && Words.IsMatch(s[pm.Length..])) weak = true;   // переведена только приставка
                return head + (rest ?? s[pm.Length..]);
            }
        }
        // многострочный текст: самый длинный известный блок строк сверху («описание урока» + «\nЗадание: …»)
        if (s.Contains('\n'))
            for (int i = s.LastIndexOf('\n'); i > 0; i = s.LastIndexOf('\n', i - 1))
            {
                if (s.IndexOf('\n') == i || !d.Exact.TryGetValue(s[..i].Trim(), out var top)) continue;   // одна строка — обычным порядком ниже
                var rest = Translate(d, s[(i + 1)..], depth + 1);
                return top + "\n" + (rest ?? s[(i + 1)..]);
            }
        // составной текст — по разделителям
        foreach (var sep in Separators)
        {
            int i = s.IndexOf(sep, StringComparison.Ordinal);
            if (i <= 0 || i + sep.Length >= s.Length) continue;
            var left = Translate(d, s[..i], depth + 1);
            var right = Translate(d, s[(i + sep.Length)..], depth + 1);
            if (left != null || right != null)
            {
                if ((left == null && Words.IsMatch(s[..i])) || (right == null && Words.IsMatch(s[(i + sep.Length)..]))) weak = true;   // половина осталась русской
                return (left ?? s[..i]) + sep + (right ?? s[(i + sep.Length)..]);
            }
        }
        // известные куски внутри текста (длинные — первыми, без повторной замены внутри уже заменённого)
        var hits = new List<(int at, int len, string val)>();
        var covered = new bool[s.Length];
        foreach (var (k, v) in d.Fragments)
        {
            if (k.Length > s.Length) continue;
            for (int i = s.IndexOf(k, StringComparison.Ordinal); i >= 0; i = s.IndexOf(k, i + 1, StringComparison.Ordinal))
            {
                bool before = i == 0 || !char.IsLetter(s[i - 1]);
                bool after = i + k.Length >= s.Length || !char.IsLetter(s[i + k.Length]);
                if (!before || !after || Enumerable.Range(i, k.Length).Any(x => covered[x])) continue;
                for (int x = i; x < i + k.Length; x++) covered[x] = true;
                hits.Add((i, k.Length, v));
            }
        }
        if (hits.Count == 0) return null;
        weak = true;   // переведено только кусками
        var sbr = new StringBuilder();
        int at = 0;
        foreach (var (i, len, v) in hits.OrderBy(h => h.at))
        {
            sbr.Append(s, at, i - at).Append(v);
            at = i + len;
        }
        return sbr.Append(s, at, s.Length - at).ToString();
    }

    /// <summary>Есть ли перевод (для проверки словаря): null — нет.</summary>
    public static string TryTranslate(string lang, string s)
    {
        lock (sync) return Translate(Load(lang), s, 0);
    }
}
