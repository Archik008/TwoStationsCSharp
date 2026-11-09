using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;

namespace StationApp.Core;

// =====================================================================
//  СВЯЗЬ ДИСПЕТЧЕРОВ — подключение к серверу PromServer по WebSocket
//  [ДОБАВЛЕНО 12.10.2026] Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса — правите в одной,
//  скопируйте в другую (как Core/ArduinoLink.cs).
//
//  Станция присылает показания СВОИХ светофоров и направление общих перегонов (Publish), сервер
//  пересылает их другой станции; от сервера приходит состояние соседей (событие PeersChanged, словарь Peers).
//  Разовые сообщения соседу (запрос смены направления и ответ) — SendEvent / EventReceived.
//  Связь сама переподключается каждые 3 с, окно никогда не ждёт сеть.
//
//  Протокол (подробно — PromServer\README.md):
//    → {"type":"hello","station":"akzht","name":"..."}   при каждом подключении
//    → {"type":"signals","signals":{"НА":"Red",...},"lines":{"ОП":{"dir":"Right","ver":3}},"sections":{"НАУ":true}}
//                                                         полный снимок: свои светофоры, перегоны, занятость общих участков
//    → {"type":"event","to":"algabas","data":{...}}      разовое сообщение соседу
//    → {"type":"ping"}                                    раз в 5 с; сервер отвечает pong
//    ← {"type":"welcome",...}
//    ← {"type":"state","station":"algabas","online":true,"signals":{...},"lines":{...}}
//    ← {"type":"event","from":"akzht","data":{...}}
//    ← {"type":"pong"} / {"type":"error","message":"..."}
// =====================================================================

/// <summary>Что известно о другой станции.</summary>
public class PeerState
{
    public string Station = "";
    public string Name = "";
    public bool Online;
    public Dictionary<string, string> Signals = new();
    /// <summary>Направление общих перегонов у соседа: имя → (направление, версия).</summary>
    public Dictionary<string, (Dir dir, long ver)> Lines = new();
    /// <summary>Занятость общих участков по данным соседа: имя → занят.</summary>
    public Dictionary<string, bool> Sections = new();
    /// <summary>Общие участки, по которым у соседа задан поездной маршрут на перегон: имя → вид ("train").</summary>
    public Dictionary<string, string> Routes = new();
}

public static class DispatchLink
{
    public const int DefaultPort = 8090;
    const int ReconnectSeconds = 3;
    const int PingSeconds = 5;
    const int SilenceSeconds = 15;      // от сервера ничего столько времени — связь потеряна

    /// <summary>Кто мы: имя станции для сервера (латиница) и подпись.</summary>
    public static string StationId = "", StationName = "";

    /// <summary>Адрес сервера; null — связь выключена.</summary>
    public static string Url { get; private set; }
    public static bool Enabled => Url != null;
    /// <summary>Сервер на связи (подключились и получили welcome).</summary>
    public static bool IsConnected { get; private set; }
    /// <summary>Почему нет связи (для подсказки на экране).</summary>
    public static string LastError { get; private set; } = "";

    /// <summary>Соседние станции по имени (обновляется только в потоке окна).</summary>
    public static readonly Dictionary<string, PeerState> Peers = new();

    public static event Action<string, LogKind> Log;
    /// <summary>Подключились / отключились / включили / выключили.</summary>
    public static event Action StatusChanged;
    /// <summary>Пришло новое состояние соседа.</summary>
    public static event Action PeersChanged;
    /// <summary>Пришло разовое сообщение от соседа: от кого, data.</summary>
    public static event Action<string, JsonElement> EventReceived;

    static SynchronizationContext ui;
    static CancellationTokenSource cts;
    static string snapshotJson;                         // последний снимок своих показаний (JSON)
    static Dictionary<string, string> snapshot;
    static Dictionary<string, (Dir dir, long ver)> snapshotLines;
    static Dictionary<string, bool> snapshotSections;
    static Dictionary<string, string> snapshotRoutes;
    static readonly Channel<string> outbox = Channel.CreateUnbounded<string>();   // разовые сообщения
    static readonly Channel<bool> wake = Channel.CreateBounded<bool>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    // =================================================================
    //  УПРАВЛЕНИЕ (из потока окна)
    // =================================================================

    /// <summary>Включить связь с сервером. address — "192.168.1.5", "192.168.1.5:8090" или "ws://…/ws".</summary>
    public static void Start(string address)
    {
        string url = NormalizeUrl(address);
        if (url == null) { Say($"Связь: неправильный адрес сервера «{address}»", LogKind.Refuse); return; }
        if (url == Url) return;
        Stop(quiet: true);

        ui = SynchronizationContext.Current;
        Url = url;
        LastError = "подключение…";
        cts = new CancellationTokenSource();
        var token = cts.Token;
        Task.Run(() => RunAsync(url, token));
        Say($"Связь: подключение к серверу {url}");
        StatusChanged?.Invoke();
    }

    /// <summary>Выключить связь. quiet — без записи в журнал (при выходе из программы).</summary>
    public static void Stop(bool quiet = false)
    {
        if (Url == null) return;
        cts?.Cancel();
        cts = null;
        Url = null;
        IsConnected = false;
        LastError = "";
        bool hadPeers = Peers.Count > 0;
        Peers.Clear();
        if (!quiet) Say("Связь с сервером выключена");
        StatusChanged?.Invoke();
        if (hadPeers) PeersChanged?.Invoke();
    }

    /// <summary>Показания своих светофоров и направление общих перегонов. Отправляется, только если снимок изменился.</summary>
    public static void Publish(Dictionary<string, string> signals, Dictionary<string, (Dir dir, long ver)> lines,
                               Dictionary<string, bool> sections, Dictionary<string, string> routes)
    {
        if (snapshot != null && Same(snapshot, signals) && Same(snapshotLines, lines) && Same(snapshotSections, sections) &&
            Same(snapshotRoutes, routes))
            return;
        snapshot = new Dictionary<string, string>(signals);
        snapshotLines = new Dictionary<string, (Dir dir, long ver)>(lines);
        snapshotSections = new Dictionary<string, bool>(sections);
        snapshotRoutes = new Dictionary<string, string>(routes);
        var json = JsonSerializer.Serialize(new
        {
            type = "signals",
            signals = snapshot,
            lines = snapshotLines.ToDictionary(kv => kv.Key, kv => new { dir = kv.Value.dir.ToString(), ver = kv.Value.ver }),
            sections = snapshotSections,
            routes = snapshotRoutes,          // сервер 1.3 пересылает это поле как есть
        });
        Volatile.Write(ref snapshotJson, json);
        wake.Writer.TryWrite(true);
    }

    static bool Same<T>(Dictionary<string, T> a, Dictionary<string, T> b) =>
        a.Count == b.Count && b.All(kv => a.TryGetValue(kv.Key, out var v) && EqualityComparer<T>.Default.Equals(v, kv.Value));

    /// <summary>Разовое сообщение соседней станции. false — нет связи с сервером, сообщение не ушло.</summary>
    public static bool SendEvent(string to, object data)
    {
        if (!IsConnected) return false;
        outbox.Writer.TryWrite(JsonSerializer.Serialize(new { type = "event", to, data }));
        wake.Writer.TryWrite(true);
        return true;
    }

    // =================================================================
    //  НАСТРОЙКИ: адрес сервера и «связь включена» запоминаются между запусками
    // =================================================================

    /// <summary>Файл настроек (задаёт программа станции).</summary>
    public static string SettingsFile;

    public static (string address, bool on) LoadSettings()
    {
        string address = "", on = "0";
        try
        {
            if (SettingsFile != null && File.Exists(SettingsFile))
                foreach (var line in File.ReadAllLines(SettingsFile))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0) continue;
                    var (k, v) = (line[..i].Trim(), line[(i + 1)..].Trim());
                    if (k == "server") address = v;
                    else if (k == "on") on = v;
                }
        }
        catch { }
        return (address, on == "1" && address.Length > 0);
    }

    public static void SaveSettings(string address, bool on)
    {
        if (SettingsFile == null) return;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsFile)!);
            File.WriteAllText(SettingsFile, $"server={address}\non={(on ? 1 : 0)}\n");
        }
        catch (Exception ex) { Say("Связь: не удалось сохранить настройки — " + ex.Message, LogKind.Refuse); }
    }

    /// <summary>Включить связь, если в прошлый раз она была включена. Вызывать из окна (нужен поток окна).</summary>
    public static void StartFromSettings()
    {
        var (address, on) = LoadSettings();
        if (on) Start(address);
    }

    /// <summary>Сосед на связи и прислал показания.</summary>
    public static PeerState Peer(string station) =>
        Peers.TryGetValue(station, out var p) && p.Online ? p : null;

    /// <summary>"192.168.1.5" → "ws://192.168.1.5:8090/ws". null — адрес не разобрать.</summary>
    public static string NormalizeUrl(string address)
    {
        address = (address ?? "").Trim();
        if (address.Length == 0) return null;
        if (!address.Contains("://")) address = "ws://" + address;
        if (!Uri.TryCreate(address, UriKind.Absolute, out var u) || (u.Scheme != "ws" && u.Scheme != "wss") || u.Host.Length == 0)
            return null;
        var b = new UriBuilder(u);
        if (u.IsDefaultPort && !address.Contains(":" + u.Port)) b.Port = DefaultPort;
        if (b.Path is "" or "/") b.Path = "/ws";
        return b.Uri.ToString();
    }

    // =================================================================
    //  СЕТЬ (в фоновом потоке)
    // =================================================================

    static async Task RunAsync(string url, CancellationToken ct)
    {
        string lastReported = null;
        while (!ct.IsCancellationRequested)
        {
            string error;
            try
            {
                await SessionAsync(url, ct);
                error = "сервер закрыл соединение";
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { error = Describe(ex); }

            Post(() =>
            {
                if (ct.IsCancellationRequested) return;
                bool was = IsConnected;
                IsConnected = false;
                LastError = error;
                bool hadPeers = Peers.Count > 0;
                Peers.Clear();
                // одинаковую ошибку раз в 3 с в журнал не пишем
                if (was || error != lastReported)
                    Say($"Связь: нет связи с сервером ({error}). Повтор каждые {ReconnectSeconds} с", LogKind.Refuse);
                lastReported = error;
                StatusChanged?.Invoke();
                if (hadPeers) PeersChanged?.Invoke();
            });
            try { await Task.Delay(TimeSpan.FromSeconds(ReconnectSeconds), ct); }
            catch (OperationCanceledException) { break; }
        }
    }

    static async Task SessionAsync(string url, CancellationToken ct)
    {
        using var ws = new ClientWebSocket();
        ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(10);
        using (var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            connectTimeout.CancelAfter(TimeSpan.FromSeconds(5));
            try { await ws.ConnectAsync(new Uri(url), connectTimeout.Token); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new TimeoutException("сервер не отвечает"); }
        }

        using var session = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var sendLock = new SemaphoreSlim(1, 1);
        async Task Send(string json)
        {
            var data = Encoding.UTF8.GetBytes(json);
            await sendLock.WaitAsync(session.Token);
            try { await ws.SendAsync(data, WebSocketMessageType.Text, true, session.Token); }
            finally { sendLock.Release(); }
        }

        await Send(JsonSerializer.Serialize(new { type = "hello", station = StationId, name = StationName }));

        long lastRx = Environment.TickCount64;
        var receiver = ReceiveLoop(ws, session.Token, () => lastRx = Environment.TickCount64);
        var sender = SendLoop(Send, session.Token);
        var pinger = PingLoop(Send, () => Environment.TickCount64 - Interlocked.Read(ref lastRx), session.Token);

        var first = await Task.WhenAny(receiver, sender, pinger);
        session.Cancel();
        try { await Task.WhenAll(receiver, sender, pinger); } catch { }
        if (ws.State == WebSocketState.Open)
        {
            using var t = new CancellationTokenSource(1000);
            try { await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "выход", t.Token); } catch { }
        }
        await first;   // пробросить причину обрыва
    }

    static async Task ReceiveLoop(ClientWebSocket ws, CancellationToken ct, Action alive)
    {
        var buf = new byte[16 * 1024];
        using var msg = new MemoryStream();
        while (true)
        {
            var r = await ws.ReceiveAsync(buf, ct);
            if (r.MessageType == WebSocketMessageType.Close)
                throw new IOException(string.IsNullOrEmpty(r.CloseStatusDescription)
                    ? "сервер закрыл соединение" : "сервер закрыл соединение: " + r.CloseStatusDescription);
            msg.Write(buf, 0, r.Count);
            // [ИЗМЕНЕНО 20.10.2026] 4 МБ: преподаватель получает снимки экрана станций
            if (msg.Length > 4 * 1024 * 1024) throw new IOException("слишком длинное сообщение от сервера");
            if (!r.EndOfMessage) continue;
            alive();
            var text = Encoding.UTF8.GetString(msg.GetBuffer(), 0, (int)msg.Length);
            msg.SetLength(0);
            Handle(text, ct);
        }
    }

    static async Task SendLoop(Func<string, Task> send, CancellationToken ct)
    {
        string sent = null;
        while (outbox.Reader.TryRead(out _)) { }   // сообщения, не ушедшие по прошлому соединению, устарели
        while (true)
        {
            if (outbox.Reader.TryRead(out var ev))
            {
                await send(ev);
                continue;
            }
            var json = Volatile.Read(ref snapshotJson);
            if (json != null && json != sent)
            {
                await send(json);
                sent = json;
                continue;
            }
            await wake.Reader.ReadAsync(ct);
        }
    }

    static async Task PingLoop(Func<string, Task> send, Func<long> silenceMs, CancellationToken ct)
    {
        const string ping = "{\"type\":\"ping\"}";
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(PingSeconds), ct);
            if (silenceMs() > SilenceSeconds * 1000)
                throw new TimeoutException($"сервер молчит больше {SilenceSeconds} с");
            await send(ping);
        }
    }

    /// <summary>Разобрать сообщение сервера. ct — токен ЭТОГО соединения: если оно уже закрыто
    /// (переподключились), опоздавшие сообщения от него в окно не попадают.</summary>
    static void Handle(string text, CancellationToken ct)
    {
        JsonElement root;
        try
        {
            using var doc = JsonDocument.Parse(text);
            root = doc.RootElement.Clone();
        }
        catch { Post(ct, () => Say("Связь: от сервера пришло не JSON: " + Cut(text), LogKind.Refuse)); return; }

        switch (Str(root, "type"))
        {
            case "welcome":
                string server = Str(root, "server");
                Post(ct, () =>
                {
                    IsConnected = true;
                    LastError = "";
                    Say($"Связь: сервер на связи ({server})");
                    StatusChanged?.Invoke();
                });
                break;

            case "state":
                var p = new PeerState
                {
                    Station = Str(root, "station"),
                    Name = Str(root, "name"),
                    Online = root.TryGetProperty("online", out var on) && on.ValueKind == JsonValueKind.True
                };
                if (root.TryGetProperty("signals", out var sig) && sig.ValueKind == JsonValueKind.Object)
                    foreach (var kv in sig.EnumerateObject())
                        if (kv.Value.ValueKind == JsonValueKind.String) p.Signals[kv.Name] = kv.Value.GetString();
                if (root.TryGetProperty("lines", out var lines) && lines.ValueKind == JsonValueKind.Object)
                    foreach (var kv in lines.EnumerateObject())
                        if (kv.Value.ValueKind == JsonValueKind.Object &&
                            Enum.TryParse<Dir>(Str(kv.Value, "dir"), out var dir) && Enum.IsDefined(dir) &&
                            kv.Value.TryGetProperty("ver", out var ver) && ver.TryGetInt64(out var v))
                            p.Lines[kv.Name] = (dir, v);
                if (root.TryGetProperty("sections", out var secs) && secs.ValueKind == JsonValueKind.Object)
                    foreach (var kv in secs.EnumerateObject())
                        if (kv.Value.ValueKind is JsonValueKind.True or JsonValueKind.False) p.Sections[kv.Name] = kv.Value.GetBoolean();
                if (root.TryGetProperty("routes", out var rts) && rts.ValueKind == JsonValueKind.Object)
                    foreach (var kv in rts.EnumerateObject())
                        if (kv.Value.ValueKind == JsonValueKind.String) p.Routes[kv.Name] = kv.Value.GetString();
                Post(ct, () =>
                {
                    Peers.TryGetValue(p.Station, out var old);
                    if (old == null || old.Online != p.Online)
                    {
                        string who = p.Name.Length > 0 ? p.Name : p.Station;
                        if (p.Online) Say($"Связь: {who} — на связи");
                        else if (old != null) Say($"Связь: {who} — нет связи с сервером", LogKind.Refuse);
                    }
                    Peers[p.Station] = p;
                    PeersChanged?.Invoke();
                });
                break;

            case "event":
                string from = Str(root, "from");
                if (from.Length == 0 || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) break;
                Post(ct, () => EventReceived?.Invoke(from, data));
                break;

            case "error":
                string message = Str(root, "message");
                Post(ct, () => Say("Связь: сервер сообщает об ошибке — " + message, LogKind.Refuse));
                break;
        }
    }

    static void Post(CancellationToken ct, Action a) => Post(() => { if (!ct.IsCancellationRequested) a(); });

    static string Str(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : "";

    static string Cut(string s) => s.Length > 80 ? s[..80] + "…" : s;

    static string Describe(Exception ex)
    {
        if (ex is WebSocketException { InnerException: { } inner }) ex = inner;
        if (ex is HttpRequestException { InnerException: System.Net.Sockets.SocketException se }) ex = se;
        if (ex is IOException { InnerException: System.Net.Sockets.SocketException ioe }) ex = ioe;
        return ex switch
        {
            System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionReset or System.Net.Sockets.SocketError.ConnectionAborted } =>
                "сервер отключился — окно сервера закрыто?",
            System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused } =>
                "сервер не запущен или порт закрыт брандмауэром",
            System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.HostNotFound } =>
                "неизвестный адрес",
            System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.TimedOut or System.Net.Sockets.SocketError.HostUnreachable or System.Net.Sockets.SocketError.NetworkUnreachable } =>
                "компьютер с сервером недоступен",
            _ => ex.Message
        };
    }

    static void Say(string msg, LogKind kind = LogKind.Info) => Log?.Invoke(msg, kind);

    static void Post(Action a)
    {
        if (ui != null) ui.Post(_ => a(), null);
        else a();
    }
}
