using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace StationApp.Ai;

// =====================================================================
//  ЖИ: НЕЙРОСЕТЬ OLLAMA (НЕОБЯЗАТЕЛЬНА)  [ДОБАВЛЕНО 28.10.2026, перенесено из Station LAB, Ui/Aiassistant.cs]
//  Без нейросети ЖИ отвечает готовыми текстами (справка, инструкция, глоссарий — на двух языках).
//  С нейросетью — пересказывает найденное коротко и своими словами.
//  Настройки: файл ai_settings.ini рядом со Station.exe (создаётся сам) или переменные среды AKZHT_LLM_URL, AKZHT_LLM_MODEL.
//  Ollama можно положить рядом с программой: папка Ollama\ollama.exe (+ Ollama\models) — запустится сама.
// =====================================================================

public sealed class AiLlm : IDisposable
{
    const string SettingsFile = "ai_settings.ini";
    const string DefaultIni =
        "# Настройки ЖИ-помощника. Поменяйте, сохраните файл и перезапустите программу.\r\n" +
        "# Удалите файл — он создастся заново со значениями по умолчанию.\r\n\r\n" +
        "# Где работает нейросеть Ollama (необязательна: без неё ЖИ отвечает готовыми текстами).\r\n" +
        "# На этом же ПК — оставьте как есть. На другом ПК колледжа — его адрес, например 192.168.8.20 (порт 11434 добавится сам).\r\n" +
        "LlmUrl = http://127.0.0.1:11434\r\n\r\n" +
        "# Модель (должна быть скачана в Ollama). Для слабых ПК — qwen2.5:3b.\r\n" +
        "Model = qwen2.5:7b\r\n\r\n" +
        "# Википедия: 1 — искать ответы по железной дороге в интернете, 0 — нет.\r\n" +
        "Wikipedia = 1\r\n";

    static readonly Dictionary<string, string> Ini = ReadIni();
    static readonly string LlmBase = NormalizeBase(Env("AKZHT_LLM_URL", IniGet("LlmUrl", "http://127.0.0.1:11434")));
    static readonly bool IsLocal = new Uri(LlmBase).IsLoopback;
    public static readonly string Model = Env("AKZHT_LLM_MODEL", IniGet("Model", "qwen2.5:7b"));
    public static readonly bool WikipediaOn = IniGet("Wikipedia", "1") != "0";

    static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(120) };
    Process ollama;
    bool triedStart;
    DateTime checkedAt = DateTime.MinValue;

    public bool Online { get; private set; }

    static Dictionary<string, string> ReadIni()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, SettingsFile);
            if (!File.Exists(path)) { File.WriteAllText(path, DefaultIni, new UTF8Encoding(false)); return d; }
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Split('#')[0];
                int eq = line.IndexOf('=');
                if (eq > 0) d[line[..eq].Trim()] = line[(eq + 1)..].Trim();
            }
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

    public string Where => !IsLocal ? "сервер " + new Uri(LlmBase).Host : Bundled() != null ? "Ollama из папки программы" : "этот ПК";

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

    /// <summary>Есть ли нейросеть. Проверяем не чаще раза в минуту; локальную Ollama один раз пробуем запустить.</summary>
    public async Task<bool> EnsureAsync(bool force = false)
    {
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
        var body = JsonSerializer.Serialize(new { model = Model, prompt, stream = false, options = new { num_ctx = 8192, temperature = 0.2 } });
        using var req = new HttpRequestMessage(HttpMethod.Post, LlmBase + "/api/generate") { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var resp = await Http.SendAsync(req);
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.TryGetProperty("response", out var r) ? (r.GetString() ?? "").Trim() : "";
    }

    /// <summary>Ответ нейросети на нужном языке; не тот язык — одна строгая попытка; не вышло — null.</summary>
    public async Task<string> AskAsync(string prompt, AiLang lang)
    {
        if (!Online) return null;
        try
        {
            var a = await Call(prompt);
            if (a.Length > 0 && !AiText.LangOk(a, lang))
                a = await Call((lang == AiLang.Kk ? "ЕСКЕРТУ: тек қазақ тілінде жауап бер.\n" : "ВНИМАНИЕ: отвечай только по-русски.\n") + prompt);
            if (a.Length == 0 || !AiText.LangOk(a, lang)) return null;
            return AiText.Cut(a.Replace("**", ""), 900);
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
