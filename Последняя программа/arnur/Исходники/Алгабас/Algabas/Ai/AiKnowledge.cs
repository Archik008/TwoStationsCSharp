using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: БАЗА ЗНАНИЙ  [ДОБАВЛЕНО 28.10.2026]
//  Откуда ЖИ берёт ответы про программу и железную дорогу (без интернета и без нейросети):
//   1) папка Knowledge рядом со Station.exe:
//        • строки «слова | по-русски | қазақша» (Справка_по_интерфейсу.txt, Глоссарий.txt и свои файлы преподавателя);
//        • любые другие .txt / .md — по абзацам (язык абзаца определяется сам);
//        • файлы, имя которых начинается с «_», — заметки преподавателя, ЖИ их не читает;
//   2) инструкция студента готовая_программа\Инструкции\student_ru.html и student_kk.html — по пунктам,
//      русский и казахский пункты идут парами, поэтому ответ всегда есть на обоих языках.
//  Поиск — по «понятиям» (AiText.ConceptsOf), а не по точной фразе: синонимы и окончания не мешают.
// =====================================================================

public sealed class AiKnowledge
{
    public sealed class Chunk
    {
        public string Source = "", Title = "", Ru = "", Kk = "";
        public bool Curated;                          // строка «слова | ru | kk» — написана для ЖИ, ей доверяем больше
        public HashSet<string> Concepts = new(), TitleConcepts = new();
        public string Text(AiLang lang) => lang == AiLang.Kk ? Kk : Ru;
    }

    public sealed record Hit(Chunk Chunk, double Score, double Coverage);

    public string KnowledgePath { get; }
    List<Chunk> chunks = new();
    Dictionary<string, int> df = new();
    DateTime stamp;

    public AiKnowledge()
    {
        KnowledgePath = Path.Combine(AppContext.BaseDirectory, "Knowledge");
        try { Directory.CreateDirectory(KnowledgePath); } catch { }
        Ensure(AiDefaults.HelpFile, AiDefaults.Help, AiDefaults.HelpVersion);
        Ensure(AiDefaults.GlossaryFile, AiDefaults.Glossary, AiDefaults.GlossaryVersion);
        Ensure(AiDefaults.HintsFile, AiDefaults.Hints, AiDefaults.HintsVersion);
        Reload();
    }

    public int Count => chunks.Count;
    public string HintsPath => Path.Combine(KnowledgePath, AiDefaults.HintsFile);

    /// <summary>Файл знаний по умолчанию: нет — создать; старая версия — сохранить копию «…_старая_копия.txt» и создать новый.</summary>
    void Ensure(string name, string content, string version)
    {
        try
        {
            var path = Path.Combine(KnowledgePath, name);
            if (File.Exists(path))
            {
                var text = AiText.DecodeSmart(File.ReadAllBytes(path));
                if (text.Contains(version)) return;
                File.Copy(path, Path.Combine(KnowledgePath, "_" + Path.GetFileNameWithoutExtension(name) + "_старая_копия.txt"), true);
            }
            File.WriteAllText(path, content.Replace("\r\n", "\n").Replace("\n", "\r\n"), new UTF8Encoding(false));
        }
        catch { }
    }

    /// <summary>Перечитать, если файлы в Knowledge поменялись (вызывается перед каждым поиском, дёшево).</summary>
    public void RefreshIfChanged()
    {
        try
        {
            var last = Directory.EnumerateFiles(KnowledgePath).Select(File.GetLastWriteTime).DefaultIfEmpty().Max();
            if (last > stamp) Reload();
        }
        catch { }
    }

    public void Reload()
    {
        var list = new List<Chunk>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(KnowledgePath).OrderBy(x => x))
            {
                var name = Path.GetFileName(f);
                if (name.StartsWith("_") || !(name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".md", StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (name == AiDefaults.HintsFile) continue;     // подсказки по отказам — отдельно (AiAssistant)
                LoadText(list, name, AiText.DecodeSmart(File.ReadAllBytes(f)));
            }
            stamp = Directory.EnumerateFiles(KnowledgePath).Select(File.GetLastWriteTime).DefaultIfEmpty().Max();
        }
        catch { }
        LoadGuide(list);
        foreach (var c in list)
        {
            c.TitleConcepts = AiText.ConceptsOf(c.Title);
            c.Concepts = AiText.ConceptsOf(c.Title + " " + c.Ru + " " + c.Kk);
        }
        var d = new Dictionary<string, int>();
        foreach (var c in list)
            foreach (var k in c.Concepts) d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1;
        chunks = list;
        df = d;
    }

    static void LoadText(List<Chunk> list, string source, string text)
    {
        var para = new StringBuilder();
        string title = "";
        void Flush()
        {
            var p = para.ToString().Trim();
            para.Clear();
            if (p.Length < 15) return;
            var c = new Chunk { Source = source, Title = title };
            if (AiText.DetectLang(p) == AiLang.Kk) c.Kk = p; else c.Ru = p;
            list.Add(c);
        }
        foreach (var raw in text.Replace("\r", "").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("#")) continue;
            var cols = line.Split('|');
            if (cols.Length >= 2)                                 // «слова | по-русски | қазақша»
            {
                Flush();
                list.Add(new Chunk
                {
                    Source = source, Title = cols[0].Trim(), Ru = cols[1].Trim(), Kk = cols.Length > 2 ? cols[2].Trim() : "", Curated = true
                });
                continue;
            }
            if (line.Length == 0) { Flush(); continue; }
            // заголовок: строка ЗАГЛАВНЫМИ или «Термин: …»
            if (line.Length < 80 && line == line.ToUpperInvariant() && line.Any(char.IsLetter)) { Flush(); title = line; continue; }
            para.AppendLine(line);
        }
        Flush();
    }

    // ---------- инструкция студента (HTML) ----------
    static readonly Regex Item = new(@"<(h2|li|p)\b[^>]*>(.*?)</\1>", RegexOptions.Singleline | RegexOptions.IgnoreCase | RegexOptions.Compiled);
    static readonly Regex Tag = new(@"<[^>]+>", RegexOptions.Compiled);

    static string FindGuideDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 7 && dir != null; i++, dir = dir.Parent)
        {
            foreach (var cand in new[] { Path.Combine(dir.FullName, "Инструкции"), Path.Combine(dir.FullName, "готовая_программа", "Инструкции") })
                if (File.Exists(Path.Combine(cand, "student_ru.html"))) return cand;
        }
        return null;
    }

    static List<(string section, string text)> GuideItems(string path)
    {
        var res = new List<(string, string)>();
        string html = File.ReadAllText(path, Encoding.UTF8);
        int main = html.IndexOf("<main", StringComparison.OrdinalIgnoreCase);
        if (main > 0) html = html[main..];
        string section = "";
        foreach (Match m in Item.Matches(html))
        {
            var t = WebUtility.HtmlDecode(Tag.Replace(m.Groups[2].Value, "")).Trim();
            t = Regex.Replace(t, @"\s+", " ");
            if (m.Groups[1].Value.Equals("h2", StringComparison.OrdinalIgnoreCase)) { section = Regex.Replace(t, @"^\d+\.\s*", ""); continue; }
            if (t.Length >= 20 && section.Length > 0) res.Add((section, t));
        }
        return res;
    }

    static void LoadGuide(List<Chunk> list)
    {
        try
        {
            var dir = FindGuideDir();
            if (dir == null) return;
            var ru = GuideItems(Path.Combine(dir, "student_ru.html"));
            var kkPath = Path.Combine(dir, "student_kk.html");
            var kk = File.Exists(kkPath) ? GuideItems(kkPath) : new();
            bool paired = kk.Count == ru.Count;
            for (int i = 0; i < ru.Count; i++)
                list.Add(new Chunk
                {
                    Source = "Инструкция студента", Title = ru[i].section + (paired ? " " + kk[i].section : ""),
                    Ru = ru[i].text, Kk = paired ? kk[i].text : ""
                });
            if (!paired)
                foreach (var (s, t) in kk) list.Add(new Chunk { Source = "Инструкция студента", Title = s, Kk = t });
        }
        catch { }
    }

    // ---------- поиск ----------
    static readonly Regex DefinitionRx = new(@"что такое|что значит|что означает|это что|что это|кто такой|деген не|дегеніміз|нені білдіреді|деген кім|не деген",
                                             RegexOptions.IgnoreCase | RegexOptions.Compiled);

    double Idf(string c) => Math.Log(1.0 + chunks.Count / (double)(df.TryGetValue(c, out var n) ? n : 1));

    /// <summary>Лучшие куски знаний для вопроса. Coverage — какая доля «смысловых» понятий вопроса нашлась.</summary>
    public List<Hit> Search(string question, int take = 5)
    {
        RefreshIfChanged();
        var q = AiText.ConceptsOf(question);
        string low = question.ToLowerInvariant().Replace('ё', 'е');
        var core = q.Where(c => !AiText.IsGeneric(c)).ToList();
        if (core.Count == 0) return new();
        // «как …?» — нужен порядок действий (справка, инструкция), «что такое …?» — определение (глоссарий)
        bool how = q.Contains("HOW") || q.Contains("SET");
        bool what = DefinitionRx.IsMatch(question);
        var hits = new List<Hit>();
        foreach (var ch in chunks)
        {
            double s = 0;
            int coreHit = 0;
            foreach (var c in q)
            {
                if (!ch.Concepts.Contains(c)) continue;
                bool generic = AiText.IsGeneric(c);
                if (!generic) coreHit++;
                // служебные понятия («как», «задать») почти не весят: иначе «как отменить» уводит к «как задать»
                s += generic ? Idf(c) * AiText.GenericWeight(c) * (ch.TitleConcepts.Contains(c) ? 1.6 : 1.0)
                             : Idf(c) * (ch.TitleConcepts.Contains(c) ? 1.6 : 1.0);
            }
            if (coreHit == 0) continue;
            // слова справки целиком есть в вопросе («убрать всё», «отменить маршрут») — сильный признак
            if (ch.Curated)
            {
                int best = ch.Title.Split(',').Select(k => k.Trim().ToLowerInvariant().Replace('ё', 'е'))
                                   .Where(k => (k.Contains(' ') || k.Length >= 9) && low.Contains(k)).Select(k => k.Length).DefaultIfEmpty(0).Max();
                if (best > 0) s += 2 + best / 4.0;
            }
            double cov = coreHit / (double)core.Count;
            s *= (0.4 + 0.6 * cov) * (ch.Curated ? 1.25 : 1.0);
            bool glossary = ch.Source == AiDefaults.GlossaryFile;
            if (glossary && how && !what) s *= 0.75;
            else if (glossary && what) s *= 1.25;
            hits.Add(new Hit(ch, s, cov));
        }
        return hits.OrderByDescending(h => h.Score).Take(take).ToList();
    }
}
