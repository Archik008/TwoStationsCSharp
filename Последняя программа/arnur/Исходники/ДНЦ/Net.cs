using System.Text.Json;
using StationApp.Core;

namespace DncApp;

// =====================================================================
//  СВЯЗЬ ДНЦ СО СТАНЦИЯМИ через сервер PromServer (DispatchLink — тот же файл, что у станций).
//  Что спрашиваем и что приходит — см. Core/Training.cs программ станций, раздел «Программа ДНЦ».
// =====================================================================

/// <summary>Схема станции: название (оно же ключ), ширина/высота рисунка, где она на экране станции (доли окна).</summary>
public class SchemeInfo
{
    public string Name = "";
    public double Aspect = 3;
    public RectangleF Rect;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Размер чертежа (единицы схемы станции).</summary>
    public float Dw, Dh;
    /// <summary>Общие с соседом участки: имя в сети и концы на чертеже — по ним стыкуются схемы (Ribbon).</summary>
    public readonly List<SharedPiece> Shared = new();
    /// <summary>Схемы одной программы в общей системе координат (АКЖТ и пост): ключ группы и порядок слева направо.</summary>
    public string Lvl = "";
    public int LvlPos;
}

public record SharedPiece(string N, float X1, float Y1, float X2, float Y2);

/// <summary>Кнопка (t = "b"), галочка ("c") или надпись ("l") пульта станции.</summary>
public class UiItem
{
    public string P = "", Id = "", G = "", T = "", Text = "";
    public int Gw, W, H, Bg, Fg;
    public bool En, Chk, Nl, Bold;
    public float F = 9;
}

/// <summary>[ДОБАВЛЕНО 18.11.2026] Перегон станции: Id, название, направление (Right / Left и подпись), общий ли с соседом (Wire).</summary>
public record LineInfo(string Id, string Title, string Dir, string Text, string Arrow, string Wire, bool Sync, bool Aux, string P);

/// <summary>[ДОБАВЛЕНО 18.11.2026] Маршрут станции: № (индекс у станции), название, маневровый, схема, состояние, почему нельзя.</summary>
public record RouteInfo(int I, string N, bool Sh, string P, string St, string Why)
{
    public bool Active => St != "free";
}

/// <summary>[ДОБАВЛЕНО 18.11.2026] Ответ станции на команду ДНЦ (s_result).</summary>
public record CallResult(bool Ok, string Reason, int I = -1, string Name = "", bool? DirOk = null);

/// <summary>[ДОБАВЛЕНО 18.11.2026] Запрос станции: дать ли согласие соседу на смену направления.</summary>
public record ConsentAsk(string Station, int Id, string Wire, string Title, string Text, double Seconds, string StationName)
{
    public readonly long Until = Environment.TickCount64 + (long)(Seconds * 1000);
}

public class StationRt
{
    public string Id = "", Name = "";
    public bool Online;
    public bool InfoReceived;
    public readonly List<SchemeInfo> Schemes = new();
    /// <summary>На станции открыто окно-вопрос или меню.</summary>
    public bool Popup;
    /// <summary>Управление пультом станции у ДНЦ.</summary>
    public bool Control;
    public Color Bg = Color.FromArgb(120, 165, 155);
    /// <summary>Режим интерфейса станции (Work, Classic…), как она сообщила; когда последний раз просили сменить.</summary>
    public string Mode = "";
    public long ModeSentAt;
    /// <summary>Кнопки пульта (последний ответ с ui), null — не просили.</summary>
    public List<UiItem> Ui;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Набор маршрута начат: схема и светофор начала.</summary>
    public List<(string P, string S)> Sel = new();
    public List<LineInfo> Lines = new();
    /// <summary>Маршруты (последний ответ с routes), null — не просили.</summary>
    public List<RouteInfo> Routes;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Общие с соседом участки (имена в сети), где сейчас поезд этой станции.</summary>
    public HashSet<string> TrainsOn = new();
    public string Title => Name.Length > 0 ? Name : Id;
    public SchemeInfo Scheme(string name) => Schemes.FirstOrDefault(s => s.Name == name);
}

public static class Net
{
    public const string Me = "dnc";
    const string Teacher = "teacher";

    public static readonly Dictionary<string, StationRt> Stations = new();

    /// <summary>Станции на связи / их схемы изменились.</summary>
    public static event Action Changed;
    /// <summary>Пришёл ответ s_info станции.</summary>
    public static event Action<string> InfoReceived;
    /// <summary>Пришла нарисованная схема: станция, схема, метка запроса, картинка (её забирает получатель).</summary>
    public static event Action<string, string, string, Image> SchemeReceived;
    /// <summary>Пришёл снимок окна станции: станция, метка запроса, картинка.</summary>
    public static event Action<string, string, Image> ScreenReceived;
    public static event Action<string> Log;
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Станция спрашивает согласие на смену направления / вопрос снят.</summary>
    public static event Action<ConsentAsk> ConsentAsked;
    public static event Action<string, int> ConsentEnded;

    public static void Say(string m) => Log?.Invoke($"{DateTime.Now:HH:mm:ss}  {m}");

    static bool started;

    public static void Start(string server)
    {
        if (!started)
        {
            started = true;
            DispatchLink.StationId = Me;
            DispatchLink.StationName = "ДНЦ";
            DispatchLink.Log += (m, _) => Say(m);
            DispatchLink.PeersChanged += OnPeers;
            DispatchLink.StatusChanged += () => Changed?.Invoke();
            DispatchLink.EventReceived += OnEvent;
            // станции спрашиваем раз в секунду: схемы, перегоны и где поезда на общих участках (стык ленты следует за поездом)
            // [ИЗМЕНЕНО 18.11.2026] было раз в 5 с — поезд, переходящий на соседнюю станцию, на стыке пропадал до 5 с
            var t = new System.Windows.Forms.Timer { Interval = 1000 };
            t.Tick += (_, _) => { foreach (var st in Stations.Values.Where(s => s.Online)) RequestInfo(st.Id, false); };
            t.Start();
        }
        DispatchLink.Stop(true);
        DispatchLink.Start(server);
    }

    public static string StatusText =>
        !DispatchLink.Enabled ? "связь выключена" :
        DispatchLink.IsConnected ? $"сервер на связи ({DispatchLink.Url})" : $"нет связи: {DispatchLink.LastError}";

    static void OnPeers()
    {
        foreach (var (id, p) in DispatchLink.Peers)
        {
            if (id == Me || id == Teacher) continue;
            if (!Stations.TryGetValue(id, out var st)) Stations[id] = st = new StationRt { Id = id };
            if (p.Name.Length > 0) st.Name = p.Name;
            if (st.Online != p.Online)
            {
                Say($"Станция «{st.Title}» {(p.Online ? "на связи" : "отключилась")}");
                st.Online = p.Online;
                if (st.Online) { SendMode(st); RequestInfo(id, false); }
                else st.Control = false;
            }
        }
        foreach (var st in Stations.Values)
            if (st.Online && !DispatchLink.Peers.ContainsKey(st.Id)) { st.Online = false; st.Control = false; }
        Changed?.Invoke();
    }

    public static StationRt Get(string id) => Stations.TryGetValue(id, out var st) ? st : null;

    static void Send(string id, object data)
    {
        if (Get(id) is { Online: true }) DispatchLink.SendEvent(id, data);
    }

    public static void RequestInfo(string id, bool ui, bool routes = false) => Send(id, new { kind = "t_info", ui, routes });

    // =================================================================
    //  [ДОБАВЛЕНО 18.11.2026] КОМАНДЫ С ОТВЕТОМ (s_result): маршрут, направление, сквозной маршрут
    // =================================================================
    static readonly Dictionary<string, TaskCompletionSource<CallResult>> waits = new();
    static int callSeq;

    /// <summary>Отправить команду и дождаться ответа станции (не дольше 6 с).</summary>
    public static async Task<CallResult> Call(string id, string kind, Dictionary<string, object> args = null)
    {
        if (Get(id) is not { Online: true }) return new CallResult(false, "станция не на связи");
        string tag = "c" + (++callSeq);
        var tcs = new TaskCompletionSource<CallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        waits[tag] = tcs;
        var data = new Dictionary<string, object>(args ?? new()) { ["kind"] = kind, ["tag"] = tag };
        DispatchLink.SendEvent(id, data);
        var done = await Task.WhenAny(tcs.Task, Task.Delay(6000));
        waits.Remove(tag);
        return done == tcs.Task ? tcs.Task.Result : new CallResult(false, "станция не ответила");
    }

    public static Task<CallResult> Route(string id, int i, bool on) => Call(id, "t_route", new() { ["i"] = i, ["on"] = on });
    public static Task<CallResult> Direction(string id, string line, bool aux = false) => Call(id, "t_dir", new() { ["line"] = line, ["aux"] = aux });
    public static void Unselect(string id) => Send(id, new { kind = "t_unselect" });

    // ---- согласие на смену направления: ДНЦ решает за станции ----
    static readonly Dictionary<string, long> expected = new();

    /// <summary>ДНЦ сам запустил смену направления на перегоне wire — согласие соседа дать без вопроса (25 с).</summary>
    public static void ExpectConsent(string wire) => expected[wire] = Environment.TickCount64 + 25000;

    public static void Consent(ConsentAsk a, bool ok, string reason = "")
    {
        Send(a.Station, new { kind = "t_consent", id = a.Id, ok, reason });
        Say($"«{Get(a.Station)?.Title}»: {(ok ? "согласие дано" : "отказ")} — {a.Title}");
    }
    /// <summary>Кусок схемы name по X от x0 до x1 (единицы чертежа) в масштабе k пикселей на единицу.</summary>
    public static void RequestScheme(string id, string name, float k, float x0, float x1, string tag) =>
        Send(id, new { kind = "t_scheme", name, k, x0, x1, tag });

    public static void RequestScreen(string id, int width, RectangleF r, string tag) =>
        Send(id, new { kind = "t_screen", w = width, rx = r.X, ry = r.Y, rw = r.Width, rh = r.Height, tag });

    public static void Take(string id, bool on)
    {
        Send(id, new { kind = "t_take", on });
        if (!on && Get(id) is { } st) st.Control = false;
    }

    /// <summary>[ДОБАВЛЕНО 18.11.2026] Режим интерфейса — один у всех станций (Settings.Mode).</summary>
    static void SendMode(StationRt st)
    {
        st.ModeSentAt = Environment.TickCount64;
        Send(st.Id, new { kind = "t_mode", mode = Settings.Mode });
    }

    /// <summary>Сменить режим интерфейса у всех станций.</summary>
    public static void SetModeAll(string mode)
    {
        Settings.Mode = mode;
        Settings.Save();
        foreach (var st in Stations.Values.Where(s => s.Online)) SendMode(st);
        Say($"Режим станций: {ModeName(mode)}");
    }

    public static string ModeName(string mode) => mode == "Classic" ? "Павлодарский" : "Медеуский (рабочий)";

    public static void Input(string id, object data) { if (Get(id) is { Control: true }) Send(id, data); }
    public static void Click(string id, string uiId, bool right) => Send(id, new { kind = "t_click", id = uiId, btn = right ? "right" : "left" });

    // =================================================================
    //  ОТВЕТЫ СТАНЦИЙ
    // =================================================================
    static void OnEvent(string from, JsonElement d)
    {
        if (!Stations.TryGetValue(from, out var st)) return;
        try
        {
            switch (Str(d, "kind"))
            {
                case "s_info":
                    ReadInfo(st, d);
                    InfoReceived?.Invoke(from);
                    break;
                case "s_scheme":
                    if (Decode(Str(d, "png")) is { } img) SchemeReceived?.Invoke(from, Str(d, "name"), Str(d, "tag"), img);
                    break;
                case "s_result":
                {
                    string tag = Str(d, "tag");
                    if (waits.Remove(tag, out var w))
                        w.TrySetResult(new CallResult(Bool(d, "ok"), Str(d, "reason"),
                            d.TryGetProperty("i", out var ie) && ie.ValueKind == JsonValueKind.Number && ie.TryGetInt32(out var iv) ? iv : -1, Str(d, "name"),
                            d.TryGetProperty("dirOk", out var de) && de.ValueKind is JsonValueKind.True or JsonValueKind.False ? de.GetBoolean() : null));
                    break;
                }
                case "s_consent":
                {
                    var a = new ConsentAsk(from, Int(d, "id"), Str(d, "wire"), Str(d, "title"), Str(d, "text"), Dbl(d, "seconds", 20), Str(d, "station"));
                    if (expected.TryGetValue(a.Wire, out var until) && Environment.TickCount64 < until)
                    {
                        expected.Remove(a.Wire);
                        Consent(a, true);   // смену направления запустил сам ДНЦ — соглашаемся
                    }
                    else ConsentAsked?.Invoke(a);
                    break;
                }
                case "s_consent_end":
                    ConsentEnded?.Invoke(from, Int(d, "id"));
                    break;
                case "s_screen":
                    if (Decode(Str(d, "jpg")) is { } shot) ScreenReceived?.Invoke(from, Str(d, "tag"), shot);
                    break;
            }
        }
        catch (Exception ex) { Say($"«{st.Title}»: не разобрал ответ — {ex.Message}"); }
    }

    static void ReadInfo(StationRt st, JsonElement d)
    {
        bool first = !st.InfoReceived;
        st.InfoReceived = true;
        string before = string.Join("|", st.Schemes.Select(s => s.Name));
        st.Schemes.Clear();
        if (d.TryGetProperty("schemes", out var se) && se.ValueKind == JsonValueKind.Array)
            foreach (var s in se.EnumerateArray())
            {
                var si = new SchemeInfo
                {
                    Name = Str(s, "name"), Aspect = Math.Clamp(Dbl(s, "aspect", 3), 0.3, 30),
                    Rect = new RectangleF((float)Dbl(s, "x"), (float)Dbl(s, "y"), (float)Dbl(s, "w"), (float)Dbl(s, "h")),
                    Dw = (float)Dbl(s, "dw"), Dh = (float)Dbl(s, "dh"), Lvl = Str(s, "lvl"), LvlPos = Int(s, "lvlpos")
                };
                if (s.TryGetProperty("shared", out var sh) && sh.ValueKind == JsonValueKind.Array)
                    foreach (var p in sh.EnumerateArray())
                        si.Shared.Add(new SharedPiece(Str(p, "n"), (float)Dbl(p, "x1"), (float)Dbl(p, "y1"), (float)Dbl(p, "x2"), (float)Dbl(p, "y2")));
                st.Schemes.Add(si);
            }
        st.Popup = Bool(d, "popup");
        // режим у станции другой (поменяли на станции или она только подключилась) — ставим режим ДНЦ, не чаще раза в 3 с
        st.Mode = Str(d, "mode");
        st.TrainsOn = d.TryGetProperty("trains", out var tr) && tr.ValueKind == JsonValueKind.Array
            ? tr.EnumerateArray().Select(x => x.GetString() ?? "").ToHashSet() : new();
        st.Sel = d.TryGetProperty("sel", out var sl) && sl.ValueKind == JsonValueKind.Array
            ? sl.EnumerateArray().Select(x => (Str(x, "p"), Str(x, "s"))).ToList() : new();
        if (d.TryGetProperty("lines", out var ls) && ls.ValueKind == JsonValueKind.Array)
            st.Lines = ls.EnumerateArray().Select(x => new LineInfo(Str(x, "id"), Str(x, "title"), Str(x, "dir"), Str(x, "text"),
                Str(x, "arrow"), Str(x, "wire"), Bool(x, "sync"), Bool(x, "aux"), Str(x, "p"))).ToList();
        if (d.TryGetProperty("routes", out var rs) && rs.ValueKind == JsonValueKind.Array)
            st.Routes = rs.EnumerateArray().Select(x => new RouteInfo(Int(x, "i"), Str(x, "n"), Bool(x, "sh"), Str(x, "p"),
                Str(x, "st"), Str(x, "why"))).ToList();
        if (st.Mode.Length > 0 && st.Mode != Settings.Mode && Environment.TickCount64 - st.ModeSentAt > 3000) SendMode(st);
        st.Control = Bool(d, "control");
        if (d.TryGetProperty("bg", out var bg) && bg.TryGetInt32(out var bgv)) st.Bg = Color.FromArgb(255, Color.FromArgb(bgv));
        if (d.TryGetProperty("ui", out var ue) && ue.ValueKind == JsonValueKind.Array)
            st.Ui = ue.EnumerateArray().Select(u => new UiItem
            {
                P = Str(u, "p"), Id = Str(u, "id"), G = Str(u, "g"), T = Str(u, "t"), Text = Str(u, "text"),
                Gw = Int(u, "gw"), W = Int(u, "w"), H = Int(u, "h"), Bg = Int(u, "bg"), Fg = Int(u, "fg"),
                En = Bool(u, "en"), Chk = Bool(u, "chk"), Nl = Bool(u, "nl"), Bold = Bool(u, "bold"), F = (float)Dbl(u, "f", 9)
            }).ToList();
        if (first || before != string.Join("|", st.Schemes.Select(s => s.Name))) Changed?.Invoke();
    }

    static Image Decode(string b64)
    {
        if (string.IsNullOrEmpty(b64)) return null;
        try
        {
            using var ms = new MemoryStream(Convert.FromBase64String(b64));
            using var img = Image.FromStream(ms);
            return new Bitmap(img);   // своя копия: поток можно закрыть
        }
        catch { return null; }
    }

    static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var x) ? x : 0;
    static double Dbl(JsonElement e, string n, double def = 0) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var x) ? x : def;
}
