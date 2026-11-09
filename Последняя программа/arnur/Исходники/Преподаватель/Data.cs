using System.ComponentModel;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TeacherApp;

// =====================================================================
//  ДАННЫЕ ПРЕПОДАВАТЕЛЯ — папка «данные» рядом с Teacher.exe
//  (папку целиком можно перенести на ноутбук или другой ПК вместе с программой):
//    students.json   — студенты (пароль хранится только как хеш)
//    lessons.json    — уроки (деловые игры), правятся на вкладке «Уроки»
//    settings.json   — настройки
//    results\*.json  — результаты занятий (удалять нельзя — как в тренажёре ДСП)
//    отчёты\*.html   — сформированные отчёты для печати
// =====================================================================

public class Student
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
    public string Salt { get; set; } = "";
    public string Hash { get; set; } = "";
    public DateTime Created { get; set; } = DateTime.Now;
    public bool HasPassword => Hash.Length > 0;
    public override string ToString() => Group.Length > 0 ? $"{Name} ({Group})" : Name;

    public void SetPassword(string pwd)
    {
        if (string.IsNullOrEmpty(pwd)) { Salt = Hash = ""; return; }
        Salt = Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
        Hash = HashOf(pwd);
    }

    public bool Check(string pwd) => !HasPassword || HashOf(pwd ?? "") == Hash;

    string HashOf(string pwd) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Salt + pwd)));

    /// <summary>Что уходит на станции (без пароля).</summary>
    public object Public => new { id = Id, name = Name, group = Group, pwd = HasPassword };
}

/// <summary>Урок (деловая игра). Подписи — для редактора на вкладке «Уроки».</summary>
public class Lesson
{
    [Browsable(false)] public string Id { get; set; } = Guid.NewGuid().ToString("N")[..8];

    [Category("1. Урок"), DisplayName("Название")]
    public string Name { get; set; } = "Новый урок";
    [Category("1. Урок"), DisplayName("Уровень"), Description("Для порядка в списке: 1 — для начинающих … 5 — экзамен")]
    public int Level { get; set; } = 1;
    [Category("1. Урок"), DisplayName("Что делать студенту"), Editor("System.ComponentModel.Design.MultilineStringEditor, System.Design", "System.Drawing.Design.UITypeEditor, System.Drawing")]
    public string Description { get; set; } = "";

    [Category("2. Задание"), DisplayName("Принять поездов")]
    public int Receive { get; set; }
    [Category("2. Задание"), DisplayName("Отправить поездов")]
    public int Depart { get; set; }
    [Category("2. Задание"), DisplayName("Допустимо критических событий")]
    public int MaxCritical { get; set; }
    [Category("2. Задание"), DisplayName("Зачёт от, баллов"), Description("Из 100")]
    public int PassScore { get; set; } = 70;

    [Category("3. Поезда и время"), DisplayName("Подать поездов (виртуальных)"), Description("Программа станции сама выпустит столько поездов с подходов. 0 — поезда ставит преподаватель или едет настоящий поезд на макете")]
    public int AutoTrains { get; set; }
    [Category("3. Поезда и время"), DisplayName("Интервал поездов, мин")]
    public int EveryMin { get; set; } = 5;
    [Category("3. Поезда и время"), DisplayName("Длительность, мин игрового времени"), Description("0 — без ограничения. По окончании время встаёт на паузу")]
    public int Minutes { get; set; }
    [Category("3. Поезда и время"), DisplayName("Начало (игровое время)")]
    public string StartClock { get; set; } = "08:00";
    // [ДОБАВЛЕНО 21.10.2026, этап 6] пульт ДСП: выдержки (игровые секунды) и режим управления
    [Category("4. Пульт"), DisplayName("Отмена маршрута, приближение свободно (ОС), с"), Description("По правилам 6 с")]
    public double CancelFreeSec { get; set; } = 6;
    [Category("4. Пульт"), DisplayName("Отмена поездного при занятом приближении (ОП), с"), Description("По правилам 3 мин = 180 с")]
    public double CancelTrainSec { get; set; } = 180;
    [Category("4. Пульт"), DisplayName("Отмена маневрового при занятом приближении (ОМ), с"), Description("По правилам 1 мин = 60 с")]
    public double CancelShuntSec { get; set; } = 60;
    [Category("4. Пульт"), DisplayName("Искусственная разделка ИР, с"), Description("По правилам 3–4 мин")]
    public double IrSec { get; set; } = 180;
    [Category("4. Пульт"), DisplayName("Курбель, замок, заслонка (работник), с"), Description("Сколько монтёр пути выполняет указание")]
    public double CrankSec { get; set; } = 60;
    [Category("4. Пульт"), DisplayName("Раздельное управление (как «Енисей»)"), Description("Стрелки маршрутом не переводятся: студент ставит их сам, потом задаёт маршрут"),
     TypeConverter(typeof(YesNoConverter))]   // [ДОБАВЛЕНО 09.11.2026] «Да / Нет» на языке интерфейса вместо True / False
    public bool SeparateControl { get; set; }

    // [ДОБАВЛЕНО 22.10.2026, этап 5] учебное расписание (вкладка «Расписание»), поезда по нему появляются сами
    [Category("5. Расписание"), DisplayName("Расписание"), TypeConverter(typeof(ScheduleRefConverter)),
     Description("Поезда урока появляются по этому расписанию (вкладка «Расписание»). Пусто — без расписания")]
    public string ScheduleId { get; set; } = "";
    [Category("5. Расписание"), DisplayName("Поезд на подходе за, мин"), Description("За сколько игровых минут до времени у первого пункта поезд появляется у входного светофора")]
    public double ScheduleLeadMin { get; set; } = 1;
    [Category("5. Расписание"), DisplayName("Поезд проходит участок за, с"),
     Description("Скорость виртуальных поездов на уроке, игровых секунд на участок. 0 — как настроено на станции (2 с). Для расписания — 12 с: перегон ≈ 1 мин")]
    public double TrainStepSec { get; set; }

    // [ДОБАВЛЕНО 23.10.2026, этап 7] маневровая работа с вагонами, ПНП, восстановительный поезд
    [Category("6. Маневры и ПНП"), DisplayName("Подать вагонов на ПНП"), Description("План: сколько вагонов подать на грузовые фронты ПНП. 0 — не требуется. Недовыполнение: −2 балла за вагон (не больше −20 за все три пункта)")]
    public int PlanSupply { get; set; }
    [Category("6. Маневры и ПНП"), DisplayName("Убрать вагонов с ПНП"), Description("План: сколько вагонов убрать с грузовых фронтов")]
    public int PlanRemove { get; set; }
    [Category("6. Маневры и ПНП"), DisplayName("Отправить местных вагонов"), Description("План: сколько вагонов после грузовых операций отправить со станции в поездах")]
    public int PlanLocalDepart { get; set; }
    [Category("6. Маневры и ПНП"), DisplayName("Местных вагонов в грузовом поезде"), Description("Сколько вагонов на ПНП этой станции в каждом грузовом поезде (в голове, за локомотивом)")]
    public int LocalWagons { get; set; } = 4;
    [Category("6. Маневры и ПНП"), DisplayName("Погрузка / выгрузка, мин на вагон"), Description("Игровых минут на один вагон на грузовом фронте (по нормам 30, для урока — меньше)")]
    public double PnpMinPerWagon { get; set; } = 5;
    [Category("6. Маневры и ПНП"), DisplayName("Восстановительный поезд: подход, мин")]
    public double RecoveryArriveMin { get; set; } = 10;
    [Category("6. Маневры и ПНП"), DisplayName("Восстановительный поезд: работы, мин")]
    public double RecoveryWorkMin { get; set; } = 15;

    public override string ToString() => $"{Level}. {Name}";

    /// <summary>Что уходит на станции.</summary>
    [Browsable(false)]   // [ДОБАВЛЕНО 09.11.2026] служебное — было видно в списке свойств урока строкой «Misc»
    public object Wire => new { id = Id, name = Name, description = Description, receive = Receive, depart = Depart,
                                maxCritical = MaxCritical, passScore = PassScore, autoTrains = AutoTrains, everyMin = EveryMin,
                                minutes = Minutes, startClock = StartClock,
                                cancelFreeSec = CancelFreeSec, cancelTrainSec = CancelTrainSec, cancelShuntSec = CancelShuntSec,
                                irSec = IrSec, crankSec = CrankSec, separateControl = SeparateControl,
                                schedule = Db.FindSchedule(ScheduleId), scheduleLeadMin = ScheduleLeadMin, trainStepSec = TrainStepSec,   // [этап 5]
                                planSupply = PlanSupply, planRemove = PlanRemove, planLocalDepart = PlanLocalDepart, localWagons = LocalWagons,   // [этап 7]
                                pnpMinPerWagon = PnpMinPerWagon, recoveryArriveMin = RecoveryArriveMin, recoveryWorkMin = RecoveryWorkMin };
}

public class GameEvent
{
    public double Clock { get; set; }
    public bool Critical { get; set; }
    public string Code { get; set; } = "";
    public string Text { get; set; } = "";
    public int Penalty { get; set; }
}

/// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправность за занятие (для отчёта).</summary>
public class FaultReport
{
    public string Name { get; set; } = "";
    public string Label { get; set; } = "";
    public string Cond { get; set; } = "";
    public double Activated { get; set; } = -1;
    public double Ended { get; set; } = -1;
    public string State { get; set; } = "";
    public List<string> Log { get; set; } = new();
}

/// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Строка журнала осмотра ДУ-46.</summary>
public class Du46Row
{
    public double Clock { get; set; }
    public string Kind { get; set; } = "";
    public string Who { get; set; } = "";
    public string Name { get; set; } = "";
    public string Obj { get; set; } = "";
    public string ObjLabel { get; set; } = "";
    public string Text { get; set; } = "";
}

/// <summary>[ДОБАВЛЕНО 23.10.2026, этап 4] Таблица для отчёта: журналы ДУ-2/47/58/60, бланки, предупреждения, автопроверка.</summary>
public class DocTable
{
    public string Title { get; set; } = "";
    public List<string> Columns { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();
}

public class StudentRef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Group { get; set; } = "";
}

/// <summary>Результат одного студента на одной станции за одно занятие (присылает станция).</summary>
public class GameResult
{
    public string GameId { get; set; } = "";
    public string Station { get; set; } = "";
    public string StationName { get; set; } = "";
    public StudentRef Student { get; set; }
    public string LessonId { get; set; } = "";
    public string LessonName { get; set; } = "";
    public DateTime Started { get; set; }
    public DateTime Ended { get; set; }
    public double ClockStart { get; set; }
    public double ClockEnd { get; set; }
    public int Received { get; set; }
    public int Departed { get; set; }
    public int TaskReceive { get; set; }
    public int TaskDepart { get; set; }
    public List<GameEvent> Events { get; set; } = new();
    public int Score { get; set; }
    public bool Passed { get; set; }
    public List<string> Journal { get; set; } = new();
    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправности и журнал осмотра ДУ-46.</summary>
    public List<FaultReport> Faults { get; set; } = new();
    public List<Du46Row> Du46 { get; set; } = new();
    /// <summary>[ДОБАВЛЕНО 22.10.2026, этап 5] Прибытия / отправления поездов на станции (для ГИД) и расписание урока.</summary>
    public List<StationApp.Core.TrainEventRec> Trains { get; set; } = new();
    public string ScheduleName { get; set; } = "";
    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 4] Журналы, бланки, предупреждения, радиосвязь, автопроверка — таблицами.</summary>
    public List<DocTable> Docs { get; set; } = new();
    /// <summary>[ДОБАВЛЕНО 23.10.2026, этап 7] Подано вагонов на ПНП, убрано, отправлено местных.</summary>
    public int Supplied { get; set; }
    public int Removed { get; set; }
    public int LocalDeparted { get; set; }
    /// <summary>Руководитель занятий (для сводной ведомости).</summary>
    public string Leader { get; set; } = "";

    public string StudentName => Student?.Name ?? "(не вошёл)";
    public string StudentGroup => Student?.Group ?? "";
    public int CriticalCount => Events.Count(e => e.Critical);
    public int WrongCount => Events.Count(e => !e.Critical);
    public string Verdict => Passed ? "зачёт" : "незачёт";
    public TimeSpan Duration => Ended - Started;
    [System.Text.Json.Serialization.JsonIgnore] public string File { get; set; } = "";
}

public class Settings
{
    public bool AllowAdd { get; set; } = true;
    public bool StartServer { get; set; } = true;
    public string Server { get; set; } = "127.0.0.1";
    public string LeaderName { get; set; } = "";
    // [ДОБАВЛЕНО 22.10.2026, этап 5] условные длины перегонов для ГИД и скоростей, км
    public double LenAkzhtPost { get; set; } = 1.5;
    public double LenPostAlgabas { get; set; } = 2;
    /// <summary>[ДОБАВЛЕНО 23.10.2026] Учебные уроки этапов 4 и 7 уже добавлены (чтобы не добавлять снова после удаления).</summary>
    public bool Stage47Lessons { get; set; }
}

public static class Db
{
    public static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "данные");
    static string ResultsDir => Path.Combine(Dir, "results");
    public static string ReportsDir => Path.Combine(Dir, "отчёты");
    static readonly JsonSerializerOptions Opt = new() { WriteIndented = true, PropertyNameCaseInsensitive = true };
    public static readonly JsonSerializerOptions Wire = new() { PropertyNameCaseInsensitive = true };

    public static List<Student> Students = new();
    public static List<Lesson> Lessons = new();
    public static Settings Settings = new();
    public static List<GameResult> Results = new();
    public static List<StationApp.Core.ScheduleData> Schedules = new();   // [ДОБАВЛЕНО 22.10.2026, этап 5]

    public static void Load()
    {
        Directory.CreateDirectory(ResultsDir);
        Directory.CreateDirectory(ReportsDir);
        Students = Read<List<Student>>("students.json") ?? new();
        Settings = Read<Settings>("settings.json") ?? new();
        Lessons = Read<List<Lesson>>("lessons.json");
        if (Lessons == null || Lessons.Count == 0) { Lessons = DefaultLessons(); SaveLessons(); }
        LoadSchedules();   // [ДОБАВЛЕНО 22.10.2026, этап 5]
        if (!Settings.Stage47Lessons)   // [ДОБАВЛЕНО 23.10.2026, этапы 4, 7] учебные уроки на журналы, ПНП, сход — один раз
        {
            Lessons.AddRange(Stage47Lessons());
            Settings.Stage47Lessons = true;
            SaveLessons();
            SaveSettings();
        }
        Results = new();
        foreach (var f in Directory.GetFiles(ResultsDir, "*.json"))
        {
            try
            {
                var r = JsonSerializer.Deserialize<GameResult>(File.ReadAllText(f), Opt);
                if (r != null) { r.File = f; Results.Add(r); }
            }
            catch { /* испорченный файл пропускаем */ }
        }
    }

    static T Read<T>(string name) where T : class
    {
        var p = Path.Combine(Dir, name);
        try { return File.Exists(p) ? JsonSerializer.Deserialize<T>(File.ReadAllText(p), Opt) : null; }
        catch { File.Copy(p, p + ".bad", true); return null; }
    }

    static void Write(string name, object v)
    {
        var p = Path.Combine(Dir, name);
        File.WriteAllText(p + ".tmp", JsonSerializer.Serialize(v, Opt));
        File.Move(p + ".tmp", p, true);
    }

    public static void SaveStudents() => Write("students.json", Students);
    public static void SaveLessons() => Write("lessons.json", Lessons);
    public static void SaveSettings() => Write("settings.json", Settings);

    public static void SaveResult(GameResult r)
    {
        if (string.IsNullOrEmpty(r.File))
        {
            string name = $"{r.Ended:yyyy-MM-dd_HH-mm-ss}_{r.Station}_{Safe(r.StudentName)}.json";
            r.File = Path.Combine(ResultsDir, name);
            Results.Add(r);
        }
        File.WriteAllText(r.File, JsonSerializer.Serialize(r, Opt));
    }

    static string Safe(string s) => string.Concat(s.Select(c => Path.GetInvalidFileNameChars().Contains(c) || c == ' ' ? '_' : c));

    public static Student FindStudent(string id) => Students.FirstOrDefault(s => s.Id == id);

    // ---- [ДОБАВЛЕНО 22.10.2026, этап 5] учебные расписания: schedules.json ----
    public static void SaveSchedules() => Write("schedules.json", Schedules);

    public static StationApp.Core.ScheduleData FindSchedule(string id) =>
        string.IsNullOrEmpty(id) ? null : Schedules.FirstOrDefault(s => s.Id == id);

    /// <summary>Первый запуск с расписаниями: учебные расписания и уроки по ним добавляются к имеющимся урокам.</summary>
    static void LoadSchedules()
    {
        bool first = !File.Exists(Path.Combine(Dir, "schedules.json"));
        Schedules = Read<List<StationApp.Core.ScheduleData>>("schedules.json") ?? new();
        if (!first) return;
        Schedules = StationApp.Core.DefaultSchedules.Build();
        SaveSchedules();
        Lessons.AddRange(ScheduleLessons());
        SaveLessons();
    }

    static List<Lesson> ScheduleLessons() => new()
    {
        new() { Level = 3, Name = "Работа по расписанию", ScheduleId = "uch1", TrainStepSec = 12, Minutes = 60, MaxCritical = 1,
            Description = "Поезда идут по учебному расписанию №1 (кнопка «Расписание» в полосе занятия). Готовьте маршруты заранее,\n" +
                          "принимайте на пути по расписанию (ТРА), отправляйте вовремя: опоздание отправления больше 5 мин — критическое событие.\n" +
                          "Пассажирские поезда не уходят раньше времени. Сетевое занятие АКЖТ + Алгабас (можно и по одной станции)." },
        new() { Level = 3, Name = "АКЖТ и пост по расписанию", ScheduleId = "uch3", TrainStepSec = 12, Minutes = 60, MaxCritical = 1,
            Description = "Учебное расписание №3: поезда из тоннелей через АКЖТ и путевой пост, Алгабас не нужен.\n" +
                          "Следите за очерёдностью: пригородные стоят по расписанию, грузовые пропускайте без задержек." },
        new() { Level = 4, Name = "Интенсивный график, скрещение на однопутке", ScheduleId = "uch2", TrainStepSec = 12, Minutes = 60,
            MaxCritical = 0, PassScore = 75,
            Description = "Учебное расписание №2: 8 поездов за час, скрещение на Алгабасе, частая смена направления перегона пост — Алгабас.\n" +
                          "Согласуйте с соседом смену направления заранее. Зачёт — без критических событий." },
    };

    /// <summary>[ДОБАВЛЕНО 23.10.2026, этапы 4, 7] Уроки на журналы и бланки, маневровую работу с ПНП, сход и восстановительный поезд.</summary>
    static List<Lesson> Stage47Lessons() => new()
    {
        new() { Level = 3, Name = "Журналы, бланки, радиосвязь", Receive = 2, Depart = 2, AutoTrains = 2, EveryMin = 10, Minutes = 45,
            Description = "Ведите журнал движения поездов ДУ-2 по каждому поезду (кнопка «Журналы ▾»): номер, путь, время прибытия и отправления.\n" +
                          "Преподаватель сделает так, что выходной светофор не откроется: отправьте поезд по бланку ДУ-54 («Журналы ▾» → «Бланки…» → вручить машинисту).\n" +
                          "Выдайте предупреждение на путь (ПКМ по участку → «Предупреждение / окно…»), запишите его в ДУ-60 и вручите машинистам ДУ-61.\n" +
                          "В конце урока программа сверит журналы с фактом; окончательно решает преподаватель." },
        new() { Level = 4, Name = "Маневровая работа и ПНП", Receive = 1, Depart = 1, AutoTrains = 1, Minutes = 60,
            PlanSupply = 4, PlanRemove = 4, PlanLocalDepart = 4, LocalWagons = 4, PnpMinPerWagon = 2,
            Description = "Примите грузовой поезд: в его голове 4 местных вагона на путь необщего пользования (ПНП) станции.\n" +
                          "Расформируйте поезд (радиосвязь → «Маневровая работа»), отцепите транзитные вагоны и закрепите их башмаками по норме (1 на 10 вагонов).\n" +
                          "Уведомите ПНП, подайте вагоны на грузовые фронты (меню «ПНП ▾» на пульте), после погрузки / выгрузки уберите их,\n" +
                          "прицепите к составу, сформируйте поезд, предъявите к осмотру и отправьте." },
        new() { Level = 4, Name = "Сход и восстановительный поезд", Receive = 1, AutoTrains = 1, Minutes = 60, RecoveryArriveMin = 5, RecoveryWorkMin = 10,
            Description = "Преподаватель задаст сход подвижного состава у принятого поезда (экспресс-вводная «Сход»).\n" +
                          "Запишите в ДУ-46, сообщите ДНЦ и дорожному мастеру (ПД), вызовите восстановительный поезд (ПКМ по поезду),\n" +
                          "примите его и подведите маршрутами к месту схода. После доклада НВП выдайте поезду предупреждение 40 км/ч (радиосвязь)." },
    };

    /// <summary>Уроки «от простого к сложному» (по ТЗ: с самых основ до профессионала).</summary>
    static List<Lesson> DefaultLessons() => new()
    {
        new() { Level = 1, Name = "Знакомство с пультом", Minutes = 20,
            Description = "Найдите на схеме входные, выходные и маневровые светофоры, стрелки, пути и участки.\n" +
                          "1) Переведите каждую стрелку в «минус» и обратно (ЛКМ по номеру стрелки).\n" +
                          "2) Задайте маневровый маршрут (переключатель МАНЕВРОВЫЕ, ЛКМ по светофору начала, потом по пути) и отмените его.\n" +
                          "3) Посмотрите, как маршрут мигает, пока устанавливается, и горит ровно, когда открыт светофор.\n" +
                          "4) Попробуйте «Отмена набора». Ошибочные попытки снижают оценку." },
        new() { Level = 1, Name = "Приём поезда на станцию", Receive = 1, AutoTrains = 1, EveryMin = 5, Minutes = 20,
            Description = "К входному светофору подойдёт поезд. Заранее, до его подхода, задайте маршрут приёма на свободный путь\n" +
                          "(ПОЕЗДНЫЕ, ЛКМ по входному, потом по пути). Поезд не должен стоять у закрытого входного больше 3 минут." },
        new() { Level = 2, Name = "Отправление поезда", Receive = 1, Depart = 1, AutoTrains = 1, Minutes = 30,
            Description = "Примите поезд, затем отправьте его на перегон: при необходимости смените направление перегона\n" +
                          "(с согласия соседней станции), задайте маршрут отправления с выходного светофора." },
        new() { Level = 2, Name = "Маневровая работа", Minutes = 30,
            Description = "Поставьте поезд на путь (имитация → поставить поезд), сделайте его маневровым (ПКМ по поезду)\n" +
                          "и переставьте с пути на путь маневровыми маршрутами. Поездные маршруты для манёвров не задавайте." },
        new() { Level = 3, Name = "Приём и отправление нескольких поездов", Receive = 3, Depart = 3, AutoTrains = 3, EveryMin = 8, Minutes = 60,
            Description = "Поезда подходят каждые 8 минут. Принимайте их на свободные пути и отправляйте дальше, не допуская\n" +
                          "задержек у входного. Следите за направлением однопутного перегона." },
        new() { Level = 3, Name = "Пригласительный сигнал и искусственная разделка", Receive = 1, AutoTrains = 1, Minutes = 30,
            Description = "Преподаватель создаст ложную занятость участка в маршруте приёма. Примите поезд по пригласительному\n" +
                          "сигналу (ПКМ по входному → пригласительный). Не разомкнувшиеся секции разделайте искусственно." },
        new() { Level = 4, Name = "Работа с соседней станцией", Receive = 2, Depart = 2, AutoTrains = 2, EveryMin = 10, Minutes = 60,
            Description = "Сетевое занятие АКЖТ + Алгабас. Согласуйте смену направления перегона с дежурным соседней станции,\n" +
                          "отправляйте поезда соседу и принимайте от него." },
        new() { Level = 5, Name = "Экзамен: интенсивное движение", Receive = 5, Depart = 5, AutoTrains = 5, EveryMin = 5,
            Minutes = 60, MaxCritical = 0, PassScore = 80,
            Description = "Поезда идут плотно, каждые 5 минут. Ни одной задержки у входного. Зачёт — от 80 баллов без критических событий." },
    };
}
