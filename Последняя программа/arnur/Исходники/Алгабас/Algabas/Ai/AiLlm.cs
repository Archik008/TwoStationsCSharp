using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: НЕЙРОСЕТЬ (НЕОБЯЗАТЕЛЬНА)  [ДОБАВЛЕНО 28.10.2026, перенесено из Station LAB, Ui/Aiassistant.cs]
//  [ИЗМЕНЕНО 24.11.2026] На вопросы отвечает только нейросеть: справка, инструкция, глоссарий — ей как сведения.
//  Без нейросети ЖИ сообщает, что она не подключена.
//  Настройки: файл ai_settings.ini рядом со Station.exe (создаётся сам) или переменные среды AKZHT_LLM_URL, AKZHT_LLM_MODEL.
//  Ollama можно положить рядом с программой: папка Ollama\ollama.exe (+ Ollama\models) — запустится сама.
//
//  [ДОБАВЛЕНО 24.11.2026] Google Gemini API: если задан ключ (GeminiKey в ai_settings.ini или переменная среды
//  GEMINI_API_KEY) — ЖИ спрашивает Gemini вместо Ollama. Gemini только консультирует: ничего не меняет ни на станции,
//  ни в программе, отвечает коротко (системная инструкция + предел длины ответа). Исчерпан лимит запросов (HTTP 429) —
//  LimitHit = true, AiAssistant пишет в чат «лимит использования ИИ исчерпан» и отвечает по справке.
// =====================================================================

public sealed class AiLlm : IDisposable
{
    const string SettingsFile = "ai_settings.ini";
    const string DefaultIni =
        "# Настройки ЖИ-помощника. Поменяйте, сохраните файл и перезапустите программу.\r\n" +
        "# Удалите файл — он создастся заново со значениями по умолчанию.\r\n\r\n" +
        "# Где работает нейросеть Ollama (нужна, только если нет ключа Gemini ниже или Provider = ollama).\r\n" +
        "# На этом же ПК — оставьте как есть. На другом ПК колледжа — его адрес, например 192.168.8.20 (порт 11434 добавится сам).\r\n" +
        "LlmUrl = http://127.0.0.1:11434\r\n\r\n" +
        "# Модель (должна быть скачана в Ollama). Для слабых ПК — qwen2.5:3b.\r\n" +
        "Model = qwen2.5:7b\r\n" +
        GeminiIni;

    // [ДОБАВЛЕНО 24.11.2026] раздел Gemini — дописывается и в уже созданный файл, если его там нет
    const string GeminiIni =
        "\r\n# ---- Google Gemini API (вместо Ollama) ----\r\n" +
        "# Ключ: https://aistudio.google.com/apikey — вставьте после «=». Пусто — Gemini не используется.\r\n" +
        "# Вместо файла можно задать переменную среды Windows GEMINI_API_KEY.\r\n" +
        "GeminiKey =\r\n" +
        "# Модель Gemini.\r\n" +
        "GeminiModel = gemini-2.5-flash\r\n" +
        "# Нейросеть: auto — Gemini, если есть ключ, иначе Ollama; ollama — только Ollama.\r\n" +
        "Provider = auto\r\n";

    static readonly Dictionary<string, string> Ini = ReadIni();
    static readonly string LlmBase = NormalizeBase(Env("AKZHT_LLM_URL", IniGet("LlmUrl", "http://127.0.0.1:11434")));
    static readonly bool IsLocal = new Uri(LlmBase).IsLoopback;
    public static readonly string Model = Env("AKZHT_LLM_MODEL", IniGet("Model", "qwen2.5:7b"));
    // [24.11.2026] Википедии больше нет: на вопросы отвечает только нейросеть (строка Wikipedia в старом ai_settings.ini не читается)

    // ---- [ДОБАВЛЕНО 24.11.2026] Gemini ----
    static readonly string GeminiKey = Env("GEMINI_API_KEY", IniGet("GeminiKey", ""));
    public static readonly string GeminiModel = IniGet("GeminiModel", "gemini-2.5-flash");
    static readonly string GeminiUrl = IniGet("GeminiUrl", "https://generativelanguage.googleapis.com").TrimEnd('/');
    /// <summary>Спрашивать Gemini (есть ключ и Provider не «ollama»).</summary>
    public static readonly bool UseGemini = GeminiKey.Length > 0 && !IniGet("Provider", "auto").Equals("ollama", StringComparison.OrdinalIgnoreCase);

    /// <summary>Системная инструкция Gemini: только консультирует, ничего не меняет, отвечает коротко.</summary>
    const string GeminiRules =
        "Ты — ИИ-помощник в учебной программе-тренажёре дежурного по железнодорожной станции (колледж, Казахстан). " +
        "Ты только консультируешь: объясняешь по справке программы, по состоянию станции (поезда, стрелки, светофоры, маршруты) " +
        "и по фрагментам исходного кода программы, которые тебе передают в вопросе. Код читай, чтобы понять и своими словами объяснить, " +
        "как работает программа и почему она так поступила; можно назвать класс или метод, но код не переписывай и не цитируй длинно. " +
        "Ты ничего не меняешь — ни на станции, ни в программе, ни в её коде: не задаёшь маршруты, не переводишь стрелки, " +
        "не пишешь и не предлагаешь программный код, не обещаешь что-то сделать. " +
        "Отвечай коротко и по сути: 1–4 предложения или до 4 коротких шагов, без вступлений, повторов и общих слов. " +
        "Не выдумывай: если сведений не хватает — скажи это одной фразой. Вопросы не про железную дорогу и не про программу — " +
        "вежливо одной фразой скажи, что помогаешь только с ними.";

    /// <summary>Предел длины ответа Gemini (токены) — ответ короткий даже без инструкции.</summary>
    const int GeminiMaxTokens = 320;

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    Process ollama;
    bool triedStart;
    DateTime checkedAt = DateTime.MinValue;
    DateTime limitUntil = DateTime.MinValue;    // [24.11.2026] до этого времени Gemini не спрашиваем — лимит исчерпан

    public bool Online { get; private set; }

    /// <summary>[ДОБАВЛЕНО 24.11.2026] Последний вопрос упёрся в лимит запросов Gemini (HTTP 429). Сбрасывает AiAssistant перед вопросом.</summary>
    public bool LimitHit { get; set; }

    static Dictionary<string, string> ReadIni()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SettingsFile);
            if (!File.Exists(path)) { File.WriteAllText(path, DefaultIni, new UTF8Encoding(false)); return d; }
            var lines = File.ReadAllLines(path);
            foreach (var raw in lines)
            {
                var line = raw.Split('#')[0];
                int eq = line.IndexOf('=');
                if (eq > 0) d[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
            // [24.11.2026] в старом файле нет раздела Gemini — дописываем (остальное не трогаем)
            if (!d.ContainsKey("GeminiKey") && !lines.Any(l => l.TrimStart().StartsWith("GeminiKey", StringComparison.OrdinalIgnoreCase)))
                File.AppendAllText(path, GeminiIni, new UTF8Encoding(false));
        }
        catch { }
        return d;
    }

    static string IniGet(string key, string def) => Ini.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : def;

    static string Env(string key, string def)
    {
        var v = Environment.GetEnvironmentVariable(key);
        return string.IsNullOrWhiteSpace(v) ? def : v.Trim();
    }

    static string NormalizeBase(string s)
    {
        const string fallback = "http://127.0.0.1:11434";
        s = (s ?? "").Trim();
        if (s.Length == 0) return fallback;
        if (!s.Contains("://")) s = "http://" + s;
        if (!Uri.TryCreate(s, UriKind.Absolute, out var u)) return fallback;
        bool hasPort = s[(s.IndexOf("://", StringComparison.Ordinal) + 3)..].Split('/')[0].Contains(':');
        return $"{u.Scheme}://{u.Host}:{(hasPort ? u.Port : 11434)}";
    }

    static string Bundled()
    {
        var p = Path.Combine(AppContext.BaseDirectory, "Ollama", "ollama.exe");
        return File.Exists(p) ? p : null;
    }

    public string Where => UseGemini ? "Google Gemini, " + GeminiModel
        : !IsLocal ? "сервер " + new Uri(LlmBase).Host : Bundled() != null ? "Ollama из папки программы" : "этот ПК";

    async Task<bool> Probe()
    {
        try
        {
            using var cts = new CancellationTokenSource(2000);
            using var r = await Http.GetAsync(LlmBase + "/api/tags", cts.Token);
            return r.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    /// <summary>Есть ли нейросеть. Проверяем не чаще раза в минуту; локальную Ollama один раз пробуем запустить.
    /// [24.11.2026] С ключом Gemini — сеть есть, пока ключ не отвергнут и сеть не пропала (проверять заранее не нужно: каждый
    /// пробный запрос тратил бы лимит).</summary>
    public async Task<bool> EnsureAsync(bool force = false)
    {
        if (UseGemini)
        {
            if (Online && !force) return true;
            if (!force && (DateTime.Now - checkedAt).TotalSeconds < 60) return false;
            checkedAt = DateTime.Now;
            return Online = true;
        }
        if (Online && !force) return true;
        if (!force && (DateTime.Now - checkedAt).TotalSeconds < 60) return false;
        checkedAt = DateTime.Now;
        if (await Probe()) return Online = true;
        if (!triedStart && IsLocal)
        {
            triedStart = true;
            try
            {
                var b = Bundled();
                var psi = new ProcessStartInfo(b ?? "ollama", "serve") { UseShellExecute = false, CreateNoWindow = true };
                if (b != null)
                {
                    var models = Path.Combine(Path.GetDirectoryName(b)!, "models");
                    if (Directory.Exists(models)) psi.Environment["OLLAMA_MODELS"] = models;
                }
                ollama = Process.Start(psi);
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(1000);
                    if (await Probe()) return Online = true;
                }
            }
            catch { }
        }
        return Online = false;
    }

    async Task<string> Call(string prompt)
    {
        if (UseGemini) return await CallGemini(prompt);
        var body = JsonSerializer.Serialize(new { model = Model, prompt, stream = false, options = new { num_ctx = 8192, temperature = 0.2 } });
        using var req = new HttpRequestMessage(HttpMethod.Post, LlmBase + "/api/generate") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("response", out var r) ? (r.GetString() ?? "").Trim() : "";
    }

    /// <summary>Лимит запросов Gemini исчерпан (HTTP 429 / RESOURCE_EXHAUSTED).</summary>
    sealed class LimitException : Exception { }

    /// <summary>[ДОБАВЛЕНО 24.11.2026] Запрос к Gemini API (generateContent): системная инструкция, короткий ответ.</summary>
    async Task<string> CallGemini(string prompt)
    {
        // у Gemini 2.5 Flash «размышления» отключаем: иначе они съедают предел длины и ответ приходит пустым.
        // [24.11.2026] У других моделей (Gemini 3 и новее) их может быть не отключить — им запас 2048 токенов,
        // краткость держат инструкция и обрезка ответа в чате
        bool thinkingOff = GeminiModel.Contains("flash", StringComparison.OrdinalIgnoreCase) && GeminiModel.Contains("2.5");
        var gen = new Dictionary<string, object> { ["temperature"] = 0.2, ["maxOutputTokens"] = thinkingOff ? GeminiMaxTokens : 2048 };
        if (thinkingOff) gen["thinkingConfig"] = new { thinkingBudget = 0 };
        var body = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["system_instruction"] = new { parts = new[] { new { text = GeminiRules } } },
            ["contents"] = new[] { new { role = "user", parts = new[] { new { text = prompt } } } },
            ["generationConfig"] = gen,
        });
        var url = $"{GeminiUrl}/v1beta/models/{Uri.EscapeDataString(GeminiModel)}:generateContent";
        using var req = new HttpRequestMessage(HttpMethod.Post, url) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        req.Headers.Add("x-goog-api-key", GeminiKey);    // ключ — в заголовке, не в адресе (не попадает в журналы прокси)
        using var resp = await Http.SendAsync(req);
        var text = await resp.Content.ReadAsStringAsync();
        if (resp.StatusCode == (HttpStatusCode)429 || text.Contains("RESOURCE_EXHAUSTED"))
        {
            // Google пишет, через сколько повторить («retryDelay»: «37s»); нет — ждём минуту
            var m = System.Text.RegularExpressions.Regex.Match(text, "\"retryDelay\"\\s*:\\s*\"(\\d+)s\"");
            limitUntil = DateTime.Now.AddSeconds(m.Success ? Math.Clamp(int.Parse(m.Groups[1].Value), 5, 3600) : 60);
            throw new LimitException();
        }
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(text);
        if (!doc.RootElement.TryGetProperty("candidates", out var cands) || cands.GetArrayLength() == 0) return "";
        if (!cands[0].TryGetProperty("content", out var content) || !content.TryGetProperty("parts", out var parts)) return "";
        var sb = new StringBuilder();
        foreach (var p in parts.EnumerateArray())
            if (p.TryGetProperty("text", out var t) && !(p.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True))
                sb.Append(t.GetString());
        return sb.ToString().Trim();
    }

    /// <summary>Ответ нейросети на нужном языке; не тот язык — одна строгая попытка; не вышло — null.</summary>
    public async Task<string> AskAsync(string prompt, AiLang lang)
    {
        if (!Online) return null;
        if (UseGemini && DateTime.Now < limitUntil) { LimitHit = true; return null; }    // [24.11.2026] лимит ещё не восстановился
        try
        {
            var a = await Call(prompt);
            if (a.Length > 0 && !AiText.LangOk(a, lang))
                a = await Call((lang == AiLang.Kk ? "ЕСКЕРТУ: тек қазақ тілінде жауап бер.\n" : "ВНИМАНИЕ: отвечай только по-русски.\n") + prompt);
            if (a.Length == 0 || !AiText.LangOk(a, lang)) return null;
            return AiText.Cut(a.Replace("**", ""), UseGemini ? 700 : 900);
        }
        catch (LimitException)
        {
            LimitHit = true;    // сеть есть, просто лимит — Online не сбрасываем
            return null;
        }
        catch
        {
            Online = false;
            checkedAt = DateTime.Now;
            return null;
        }
    }

    public static string LangRule(AiLang lang) => lang == AiLang.Kk
        ? "ТІЛ: жауапты тек қазақ тілінде жаз. Светофор, стрелка, учаске атаулары (Н1, 5/7, 1СП) өзгермейді."
        : "ЯЗЫК: отвечай только по-русски.";

    public void Dispose()
    {
        try { if (ollama is { HasExited: false }) ollama.Kill(); } catch { }
    }
}
