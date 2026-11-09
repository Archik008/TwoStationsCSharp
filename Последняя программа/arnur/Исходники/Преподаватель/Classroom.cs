using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using StationApp.Core;

namespace TeacherApp;

// =====================================================================
//  КЛАСС: станции на связи, вход студентов, урок, игровое время.
//  Протокол со станциями — см. Core/Training.cs в программах станций.
// =====================================================================

public class StationRt
{
    public string Id = "";
    public string Name = "";
    public bool Online;
    public Student Student;
    public bool Take;
    public bool Active;
    public int Received, Departed, Critical, Wrong, Score = 100;
    public bool ResultReceived;
    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Объекты станции (ответ s_objects) и её неисправности (s_faults).</summary>
    public JsonElement? Objects;
    public List<FaultRow> Faults = new();
}

/// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправность на станции, как её прислала станция.</summary>
public class FaultRow
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Cond { get; set; } = "";
    public string State { get; set; } = "";
    public string StateText { get; set; } = "";
    public int Train { get; set; }
    public double Activated { get; set; } = -1;
}

public static class Classroom
{
    public const string Me = "teacher";
    public static readonly Dictionary<string, StationRt> Stations = new();

    public static bool Active { get; private set; }
    public static string GameId { get; private set; } = "";
    public static Lesson Lesson { get; private set; }
    public static double Clock { get; private set; }
    public static bool Running { get; private set; }
    public static int Speed { get; private set; } = 1;
    static double endClock;

    public static event Action Changed;
    public static event Action<string> Log;
    /// <summary>Пришёл кадр экрана: станция, кадр. [ИЗМЕНЕНО 21.10.2026] с меткой запроса и частями окна станции.</summary>
    public static event Action<string, ScreenFrame> Screen;

    static Process server;
    static double timeAcc;

    public static void Say(string m) => Log?.Invoke($"{DateTime.Now:HH:mm:ss}  {m}");

    // =================================================================
    //  ЗАПУСК: свой сервер на этом ПК + подключение
    // =================================================================

    public static void Start()
    {
        DispatchLink.StationId = Me;
        DispatchLink.StationName = "Преподаватель";
        DispatchLink.Log += (m, _) => Say(m);
        DispatchLink.PeersChanged += OnPeers;
        DispatchLink.StatusChanged += () => Changed?.Invoke();
        DispatchLink.EventReceived += OnEvent;

        if (Db.Settings.StartServer) StartServer();
        DispatchLink.Start(Db.Settings.Server);
        // [ДОБАВЛЕНО 22.10.2026, этап 8] сменил язык — станции переходят на него
        L.Changed += () => { foreach (var st in Online) Send(st, new { kind = "t_lang", lang = L.Current }); };

        var t = new System.Windows.Forms.Timer { Interval = 100 };
        t.Tick += (_, _) => Tick(0.1);
        t.Start();
    }

    public static void Reconnect()
    {
        DispatchLink.Stop(true);
        if (Db.Settings.StartServer) StartServer();
        DispatchLink.Start(Db.Settings.Server);
    }

    static void StartServer()
    {
        if (PortBusy(DispatchLink.DefaultPort)) { Say("Сервер связи уже работает на этом ПК"); return; }
        var exe = new[] { Path.Combine(AppContext.BaseDirectory, "PromServer.exe"),
                          Path.Combine(AppContext.BaseDirectory, "..", "Сервер", "PromServer.exe") }
                  .Select(Path.GetFullPath).FirstOrDefault(File.Exists);
        if (exe == null) { Say("PromServer.exe не найден рядом с программой (папка ..\\Сервер) — запустите сервер вручную"); return; }
        try
        {
            server = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true,
                                                               WorkingDirectory = Path.GetDirectoryName(exe)! });
            Say($"Запущен сервер связи: {exe}");
        }
        catch (Exception ex) { Say("Не удалось запустить сервер: " + ex.Message); }
    }

    public static void StopServer()
    {
        try { if (server is { HasExited: false }) server.Kill(); } catch { }
    }

    static bool PortBusy(int port)
    {
        try { using var c = new TcpClient(); return c.ConnectAsync(IPAddress.Loopback, port).Wait(300) && c.Connected; }
        catch { return false; }
    }

    /// <summary>Адреса этого ПК в сети — их вписывают в программы станций («Связь…»).</summary>
    public static string LocalAddresses() => string.Join(", ",
        NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(n => n.GetIPProperties().UnicastAddresses)
            .Where(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.Address.ToString()).Distinct());

    // =================================================================
    //  СТАНЦИИ
    // =================================================================

    static void OnPeers()
    {
        foreach (var (id, p) in DispatchLink.Peers)
        {
            if (id == Me) continue;
            if (!Stations.TryGetValue(id, out var st)) Stations[id] = st = new StationRt { Id = id };
            st.Name = p.Name.Length > 0 ? p.Name : id;
            if (st.Online != p.Online) Say($"Станция «{st.Name}» {(p.Online ? "на связи" : "отключилась")}");
            st.Online = p.Online;
            if (!p.Online) st.Take = false;
        }
        Changed?.Invoke();
    }

    static void Send(StationRt st, object data)
    {
        if (st.Online) DispatchLink.SendEvent(st.Id, data);
    }

    public static IEnumerable<StationRt> Online => Stations.Values.Where(s => s.Online);

    static object Hello => new { kind = "t_hello", allowAdd = Db.Settings.AllowAdd,
                                 students = Db.Students.OrderBy(s => s.Group).ThenBy(s => s.Name).Select(s => s.Public) };

    /// <summary>Список студентов изменился — разослать станциям.</summary>
    public static void BroadcastStudents()
    {
        foreach (var st in Online) Send(st, Hello);
    }

    static object Session(StationRt st) => new
    {
        kind = "t_session", active = Active, gameId = GameId, lesson = Lesson?.Wire,
        student = st.Student?.Public, clock = Clock, running = Running, speed = Speed, take = st.Take,
        lang = L.Current   // [ДОБАВЛЕНО 22.10.2026, этап 8] язык преподавателя — станциям
    };

    static void OnEvent(string from, JsonElement d)
    {
        if (!Stations.TryGetValue(from, out var st)) return;
        string kind = d.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
        switch (kind)
        {
            case "s_hello":
                // станция помнит, кто за пультом (например, преподаватель перезапустил программу)
                if (d.TryGetProperty("student", out var hs) && hs.ValueKind == JsonValueKind.Object &&
                    Db.FindStudent(Str(hs, "Id")) is { } known)
                    st.Student = known;
                Send(st, Hello);
                Send(st, Session(st));
                break;

            case "s_login":
            {
                var s = Db.FindStudent(Str(d, "id"));
                if (s == null) { Send(st, new { kind = "t_login", ok = false, msg = "Такого студента нет в списке" }); break; }
                if (!s.Check(Str(d, "pwd"))) { Send(st, new { kind = "t_login", ok = false, msg = "Неверный пароль" }); Say($"«{st.Name}»: неверный пароль ({s})"); break; }
                SetStudent(st, s, $"вошёл {s}");
                Send(st, new { kind = "t_login", ok = true, student = s.Public });
                break;
            }

            case "s_register":
            {
                if (!Db.Settings.AllowAdd) { Send(st, new { kind = "t_login", ok = false, msg = "Преподаватель запретил добавлять себя" }); break; }
                string name = Str(d, "name").Trim(), group = Str(d, "group").Trim();
                if (name.Length < 3 || group.Length == 0) { Send(st, new { kind = "t_login", ok = false, msg = "Нужны ФИО и группа" }); break; }
                if (Db.Students.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && s.Group == group))
                { Send(st, new { kind = "t_login", ok = false, msg = "Такой студент уже есть — выберите себя в списке" }); break; }
                var s = new Student { Id = Guid.NewGuid().ToString("N")[..10], Name = name, Group = group };
                s.SetPassword(Str(d, "pwd"));
                Db.Students.Add(s);
                Db.SaveStudents();
                Say($"Новый студент: {s} (зарегистрировался на «{st.Name}»)");
                SetStudent(st, s, $"вошёл {s}");
                Send(st, new { kind = "t_login", ok = true, student = s.Public });
                BroadcastStudents();
                break;
            }

            case "s_progress":
                st.Active = Bool(d, "active");
                st.Received = Int(d, "received"); st.Departed = Int(d, "departed");
                st.Critical = Int(d, "critical"); st.Wrong = Int(d, "wrong"); st.Score = Int(d, "score");
                break;

            case "s_result":
            {
                var r = d.Deserialize<GameResult>(Db.Wire);
                if (r == null) break;
                r.Leader = Db.Settings.LeaderName;
                Db.SaveResult(r);
                st.ResultReceived = true;
                st.Active = false;
                Say($"Результат «{st.Name}»: {r.StudentName} — {r.Score} баллов, {r.Verdict}");
                break;
            }

            case "s_screen":
            {
                var f = new ScreenFrame { Jpg = Str(d, "jpg"), Tag = Str(d, "tag") };
                if (d.TryGetProperty("parts", out var pe) && pe.ValueKind == JsonValueKind.Array)
                    foreach (var p in pe.EnumerateArray())
                        f.Parts.Add((Str(p, "name"), new RectangleF((float)Dbl(p, "x"), (float)Dbl(p, "y"), (float)Dbl(p, "w"), (float)Dbl(p, "h"))));
                Screen?.Invoke(st.Id, f);
                return;
            }

            // [ДОБАВЛЕНО 20.10.2026, этап 3] неисправности
            case "s_objects":
                st.Objects = d.Clone();
                break;
            // [ДОБАВЛЕНО 22.10.2026, этап 5] прибытие / отправление / проследование поезда — на ГИД
            case "s_train_event":
                Gid.Add(d);
                return;
            case "s_faults":
                st.Faults = d.TryGetProperty("list", out var fl) ? fl.Deserialize<List<FaultRow>>(Db.Wire) ?? new() : new();
                break;
        }
        Changed?.Invoke();
    }

    public static void SetStudent(StationRt st, Student s, string why)
    {
        st.Student = s;
        Say($"«{st.Name}»: {why}");
        Send(st, Session(st));
        Changed?.Invoke();
    }

    // =================================================================
    //  УРОК И ВРЕМЯ (управляет только преподаватель)
    // =================================================================

    public static void Begin(Lesson lesson)
    {
        Lesson = lesson;
        GameId = Guid.NewGuid().ToString("N")[..12];
        Active = true;
        Running = false;
        Clock = TimeSpan.TryParse(lesson.StartClock, out var start) ? start.TotalSeconds : 8 * 3600;
        endClock = lesson.Minutes > 0 ? Clock + lesson.Minutes * 60 : double.MaxValue;
        foreach (var st in Stations.Values) { st.ResultReceived = false; st.Take = false; st.Received = st.Departed = st.Critical = st.Wrong = 0; st.Score = 100; st.Faults.Clear(); }
        Gid.Begin(GameId, lesson, Clock);   // [ДОБАВЛЕНО 22.10.2026, этап 5] ГИД занятия
        foreach (var st in Online) Send(st, Session(st));
        Say($"Начат урок «{lesson.Name}» на станциях: {string.Join(", ", Online.Select(s => $"{s.Name} — {s.Student?.ToString() ?? "студент не вошёл"}"))}. Нажмите «Пуск»");
        Changed?.Invoke();
    }

    public static void SetRunning(bool run)
    {
        if (!Active || Running == run) return;
        Running = run;
        Say(run ? "Игровое время: ПУСК" : "Игровое время: ПАУЗА");
        BroadcastTime();
    }

    public static void SetSpeed(int speed)
    {
        Speed = Math.Clamp(speed, 1, 10);
        if (Active) BroadcastTime();
    }

    public static void Finish()
    {
        if (!Active) return;
        foreach (var st in Online) { st.Take = false; Send(st, new { kind = "t_stop" }); }
        Active = false;
        Running = false;
        Say("Урок закончен — станции присылают результаты (вкладка «Отчёты»)");
        Changed?.Invoke();
    }

    public static void SetTake(StationRt st, bool on)
    {
        st.Take = on;
        Send(st, new { kind = "t_take", on });
        Say(on ? $"Управление «{st.Name}» у преподавателя" : $"Управление «{st.Name}» возвращено студенту");
        Changed?.Invoke();
    }

    /// <summary>Запросить кадр. region — часть окна станции (доли 0..1), tag — кому кадр (маленький просмотр / большое окно).</summary>
    public static void RequestScreen(StationRt st, int width, RectangleF? region = null, string tag = "")
    {
        var r = region ?? new RectangleF(0, 0, 1, 1);
        Send(st, new { kind = "t_screen", w = width, rx = r.X, ry = r.Y, rw = r.Width, rh = r.Height, tag });
    }

    // [ДОБАВЛЕНО 20.10.2026, этап 3] неисправности («Экспресс-вводные»)
    public static void RequestObjects(StationRt st) => Send(st, new { kind = "t_objects" });

    public static void AddFault(StationRt st, object fault, string text)
    {
        Send(st, new { kind = "t_fault_add", fault });
        Say($"«{st.Name}»: задана неисправность — {text}");
    }

    public static void RemoveFault(StationRt st, FaultRow f)
    {
        Send(st, new { kind = "t_fault_remove", id = f.Id });
        Say($"«{st.Name}»: преподаватель снял неисправность — {f.Name}, {f.Label}");
    }
    public static void Input(StationRt st, object data) { if (st.Take) Send(st, data); }

    static void BroadcastTime()
    {
        foreach (var st in Online) Send(st, new { kind = "t_time", clock = Clock, running = Running, speed = Speed });
        Changed?.Invoke();
    }

    static void Tick(double dt)
    {
        if (!Active) return;
        if (Running)
        {
            Clock += dt * Speed;
            if (Clock >= endClock)
            {
                Clock = endClock;
                Running = false;
                Say("Время урока вышло — пауза. Нажмите «Закончить урок», чтобы получить результаты");
                BroadcastTime();
                return;
            }
        }
        timeAcc += dt;
        if (timeAcc >= 2) { timeAcc = 0; BroadcastTime(); }
    }

    static string Str(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    static bool Bool(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
    static int Int(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetInt32(out var i) ? i : 0;
    static double Dbl(JsonElement e, string n) => e.TryGetProperty(n, out var v) && v.TryGetDouble(out var x) ? x : 0;
}

/// <summary>[ДОБАВЛЕНО 21.10.2026] Кадр экрана станции.</summary>
public class ScreenFrame
{
    public string Jpg = "", Tag = "";
    /// <summary>Части окна станции (у АКЖТ — «АКЖТ», «Путевой пост»), рамки в долях окна.</summary>
    public readonly List<(string name, RectangleF rect)> Parts = new();
}
