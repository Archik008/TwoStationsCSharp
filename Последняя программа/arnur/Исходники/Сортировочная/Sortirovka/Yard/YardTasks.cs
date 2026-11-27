namespace StationApp.Yard;

// =====================================================================
//  [ДОБАВЛЕНО 26.11.2026] ЗАДАНИЯ (УРОКИ) «СОРТИРОВОЧНОЙ»
//  Кнопка «Задания…»: выбрать задание и начать урок. У задания — вариант работы, время, что сделать (цели),
//  ограничения (АРС / ГАЦ нельзя включать, в поездах — вагоны «Л» / «Е») и номер набора поездов (seed): у всех
//  учеников одни и те же поезда — результаты можно сравнивать. Урок кончается, когда цели выполнены или время вышло;
//  итог (цели, нарушения, оценка) — окном и файлом в «Документы\Сортировочная — результаты».
// =====================================================================

public sealed class YardTask
{
    public string Id = "", Title = "", Text = "";
    public YardMode Mode = YardMode.Hump;
    /// <summary>Сколько минут модельного времени на задание.</summary>
    public int Minutes = 20;
    /// <summary>Цели: распустить поездов, отправить поездов (0 — не нужно).</summary>
    public int Trains, Departs, Forms;
    /// <summary>Ограничения: АРС / ГАЦ включены или выключены и не меняются (null — на выбор ученика).</summary>
    public bool? Ars, Gac;
    /// <summary>В каждом поезде — вагон, запрещённый к роспуску («Л» / «Е»).</summary>
    public bool Forbidden;
    /// <summary>Не больше стольких нарушений (−1 — не важно).</summary>
    public int MaxViolations = -1;
    public int Seed = 1;

    public static readonly YardTask[] All =
    {
        new()
        {
            Id = "intro", Title = "1. Первый роспуск", Mode = YardMode.Hump, Minutes = 20, Trains = 1, Ars = true, Gac = true, Seed = 11,
            Text = "Вариант «Только горка». Распустите один поезд: задайте надвиг, откройте горочный светофор, следите за схемой. " +
                   "АРС тормозит, ГАЦ ставит маршруты. Если впереди вагон «Л» или «Е» — остановите надвиг и спустите его с локомотивом.",
        },
        new()
        {
            Id = "brakes", Title = "2. Ручное торможение", Mode = YardMode.Hump, Minutes = 45, Trains = 2, Ars = false, Gac = true, MaxViolations = 3, Seed = 22,
            Text = "АРС выключена — замедлители 1ТП и 2ТП ведёте вы (кнопки «Отпустить / Слабо / Сильно»). Распустите два поезда " +
                   "не больше чем с тремя нарушениями: без ударов сильнее 5 км/ч, без недокатов и нагонов.",
        },
        new()
        {
            Id = "routes", Title = "3. Маршруты вручную", Mode = YardMode.Hump, Minutes = 30, Trains = 2, Ars = true, Gac = false, MaxViolations = 2, Seed = 33,
            Text = "ГАЦ не берёт маршруты из листка — перед каждым отцепом нажмите кнопку его пути (или клавишу 1–8). " +
                   "Распустите два поезда; не больше двух нарушений (отцеп не на свой путь, без маршрута).",
        },
        new()
        {
            Id = "forbidden", Title = "4. Вагоны, которые нельзя с горки", Mode = YardMode.Hump, Minutes = 45, Trains = 2, Ars = true, Gac = true, Forbidden = true, MaxViolations = 1, Seed = 44,
            Text = "В каждом поезде в хвосте — вагон с людьми «Л» или с газом «Е». Распускать его с горки нельзя: остановите надвиг " +
                   "перед горбом («СТОП») и нажмите «Спустить с локомотивом». Распустите два поезда, не больше одного нарушения.",
        },
        new()
        {
            Id = "shunt", Title = "5. Горка и маневры", Mode = YardMode.HumpShunt, Minutes = 45, Trains = 3, Forms = 1, Ars = true, Gac = true, Seed = 55,
            Text = "Вариант «Горка + маневры»: вы сами подаёте горочный локомотив под состав, возвращаете его после роспуска " +
                   "и выводите набранные составы в парк отправления. Распустите три поезда и выведите один состав (правая кнопка по пути или «Вывести состав…»).",
        },
        new()
        {
            Id = "full", Title = "6. Полный цикл (учебный)", Mode = YardMode.Training, Minutes = 50, Trains = 2, Departs = 1, Seed = 66,
            Text = "Всё сами: примите поезд на путь парка приёма, подайте горочный локомотив, надвиг, маршруты отцепам, торможение " +
                   "замедлителями, тормозные башмаки на свободные пути, вывод состава, заказ поездного локомотива и отправление. " +
                   "Распустите два поезда и отправьте один. Подсказки — в жёлтом блоке.",
        },
        new()
        {
            Id = "exam", Title = "7. Экзамен: смена ДСПГ", Mode = YardMode.Exam, Minutes = 70, Trains = 3, Departs = 2, Seed = 77,
            Text = "Экзамен: всё делаете сами, подсказок нет. За 70 минут распустите три поезда и отправьте два. " +
                   "Оценка — по нарушениям; не выполнили цели — «2».",
        },
    };

    // ---------------- ход урока ----------------
    public bool Reached(YardSim s) => s.TrainsRolled >= Trains && s.Departed >= Departs && s.Formed >= Forms;

    public bool GoalsMet(YardSim s) => Reached(s) && (MaxViolations < 0 || s.Violations.Count <= MaxViolations);

    /// <summary>Цели с отметками: (текст, выполнено).</summary>
    public IEnumerable<(string Text, bool Done)> Goals(YardSim s)
    {
        if (Trains > 0) yield return ($"распустить поездов: {Math.Min(s.TrainsRolled, Trains)} из {Trains}", s.TrainsRolled >= Trains);
        if (Forms > 0) yield return ($"вывести составов в парк отправления: {Math.Min(s.Formed, Forms)} из {Forms}", s.Formed >= Forms);
        if (Departs > 0) yield return ($"отправить поездов: {Math.Min(s.Departed, Departs)} из {Departs}", s.Departed >= Departs);
        if (MaxViolations >= 0) yield return ($"нарушений не больше {MaxViolations}: сейчас {s.Violations.Count}", s.Violations.Count <= MaxViolations);
    }

    /// <summary>Оценка за урок: цели не выполнены — «2», иначе — оценка смены.</summary>
    public string Grade(YardSim s) => GoalsMet(s) ? s.Grade : "2";
}
