namespace StationApp.Yard;

// =====================================================================
//  ПОДВИЖНОЙ СОСТАВ: РОДЫ ВАГОНОВ, ЛОКОМОТИВЫ, ВАГОН, ОТЦЕП  [ДОБАВЛЕНО 12.11.2026]
//
//  Род вагона — по первой цифре номера (как в натурном листе): 2 крытый, 4 платформа, 6 полувагон, 7 цистерна,
//  8 изотермический (рефрижератор), 9 прочие (хоппер, служебный).
//  Длина по осям автосцепок, тара и грузоподъёмность — типовые для колеи 1520 мм.
//  Основное удельное сопротивление движению w0 (кгс/т = ‰): гружёный вагон — «хороший бегун» (1,0–1,6),
//  порожний — «плохой бегун» (2,4–3,8). От этого зависит, как далеко покатится отцеп с горки.
//  Отметки сортировочного листка, при которых роспуск с горки ЗАПРЕЩЁН (вагон спускают только с локомотивом):
//  «Л» — вагон с людьми (проводники, сопровождающие), «Е» — цистерна со сжиженным газом.
// =====================================================================

public sealed class WagonKind
{
    public string Code = "", Name = "", Short = "";
    public char Digit;
    public double Length, Tare, MaxLoad, W0Loaded, W0Empty;
    public Color Body, Dark;
    /// <summary>Отметка «запрещено распускать с горки» («Л», «Е») — пусто, если можно.</summary>
    public string Mark = "";
}

public sealed class LocoKind
{
    public string Series = "", Kind = "";
    public double Length;
    public int Sections = 1;
    public bool Electric, Shunting;
    public Color Body, Stripe;
}

public static class Rolling
{
    public static readonly WagonKind Covered = new()
    {
        Code = "кр", Name = "крытый", Short = "Кр", Digit = '2', Length = 15.0, Tare = 24, MaxLoad = 68,
        W0Loaded = 1.3, W0Empty = 3.0, Body = Color.FromArgb(150, 82, 45), Dark = Color.FromArgb(95, 50, 25)
    };
    public static readonly WagonKind Gondola = new()
    {
        Code = "пв", Name = "полувагон", Short = "Пв", Digit = '6', Length = 13.9, Tare = 23, MaxLoad = 69,
        W0Loaded = 1.1, W0Empty = 2.8, Body = Color.FromArgb(92, 104, 120), Dark = Color.FromArgb(55, 62, 72)
    };
    public static readonly WagonKind Flat = new()
    {
        Code = "пл", Name = "платформа", Short = "Пл", Digit = '4', Length = 14.6, Tare = 21, MaxLoad = 70,
        W0Loaded = 1.4, W0Empty = 3.6, Body = Color.FromArgb(176, 140, 90), Dark = Color.FromArgb(110, 85, 50)
    };
    public static readonly WagonKind Container = new()
    {
        Code = "кп", Name = "контейнерная платформа", Short = "Кп", Digit = '4', Length = 19.6, Tare = 22, MaxLoad = 60,
        W0Loaded = 1.4, W0Empty = 3.4, Body = Color.FromArgb(60, 110, 175), Dark = Color.FromArgb(35, 65, 110)
    };
    public static readonly WagonKind Tank = new()
    {
        Code = "цс", Name = "цистерна", Short = "Цс", Digit = '7', Length = 12.0, Tare = 25, MaxLoad = 66,
        W0Loaded = 1.0, W0Empty = 2.6, Body = Color.FromArgb(40, 40, 44), Dark = Color.FromArgb(15, 15, 18)
    };
    public static readonly WagonKind Hopper = new()
    {
        Code = "хп", Name = "хоппер-зерновоз", Short = "Хп", Digit = '9', Length = 14.7, Tare = 22, MaxLoad = 70,
        W0Loaded = 1.2, W0Empty = 2.9, Body = Color.FromArgb(150, 160, 95), Dark = Color.FromArgb(95, 102, 55)
    };
    public static readonly WagonKind Reefer = new()
    {
        Code = "рф", Name = "рефрижератор", Short = "Рф", Digit = '8', Length = 21.0, Tare = 37, MaxLoad = 46,
        W0Loaded = 1.5, W0Empty = 2.6, Body = Color.FromArgb(225, 228, 232), Dark = Color.FromArgb(150, 155, 162)
    };
    public static readonly WagonKind GasTank = new()
    {
        Code = "цг", Name = "цистерна для газа", Short = "Цг", Digit = '7', Length = 12.0, Tare = 36, MaxLoad = 40,
        W0Loaded = 1.2, W0Empty = 2.6, Body = Color.FromArgb(205, 205, 200), Dark = Color.FromArgb(200, 40, 40), Mark = "Е"
    };
    public static readonly WagonKind Service = new()
    {
        Code = "сл", Name = "вагон с людьми", Short = "Сл", Digit = '9', Length = 16.0, Tare = 30, MaxLoad = 5,
        W0Loaded = 1.6, W0Empty = 1.6, Body = Color.FromArgb(40, 120, 70), Dark = Color.FromArgb(20, 70, 40), Mark = "Л"
    };

    /// <summary>Обычные роды (могут идти через горку).</summary>
    public static readonly WagonKind[] Normal = { Covered, Gondola, Flat, Container, Tank, Hopper, Reefer };
    /// <summary>Запрещённые к роспуску с горки.</summary>
    public static readonly WagonKind[] Forbidden = { GasTank, Service };
    public static IEnumerable<WagonKind> All => Normal.Concat(Forbidden);

    public static readonly LocoKind TE33A = new()
    {
        Series = "ТЭ33А", Kind = "тепловоз", Length = 22.3, Body = Color.FromArgb(30, 90, 165), Stripe = Color.FromArgb(245, 200, 40)
    };
    public static readonly LocoKind KZ8A = new()
    {
        Series = "KZ8A", Kind = "электровоз", Length = 33.0, Sections = 2, Electric = true,
        Body = Color.FromArgb(70, 150, 200), Stripe = Color.FromArgb(240, 240, 240)
    };
    public static readonly LocoKind TEM18 = new()
    {
        Series = "ТЭМ18", Kind = "маневровый тепловоз", Length = 17.0, Shunting = true,
        Body = Color.FromArgb(40, 130, 70), Stripe = Color.FromArgb(240, 200, 40)
    };
    public static readonly LocoKind[] Train = { TE33A, KZ8A };
}

public sealed class Wagon
{
    public string Number = "";
    public WagonKind Kind;
    public bool Loaded;
    /// <summary>Масса брутто, т.</summary>
    public double Mass;
    /// <summary>Основное удельное сопротивление, ‰ (кгс/т).</summary>
    public double W0;
    /// <summary>Путь сортировочного парка по плану формирования (1–8).</summary>
    public int Dest;
    public double Length => Kind.Length;
    public string Mark => Kind.Mark;
    public override string ToString() => $"{Number} {Kind.Name} {(Loaded ? "гружёный" : "порожний")}";
}

/// <summary>Ходовые свойства отцепа: «хороший / средний / плохой бегун».</summary>
public enum Runner { Good, Mid, Bad }

public enum CutState
{
    /// <summary>Ещё в составе (не надвинут на горб).</summary>
    InTrain,
    /// <summary>Скатывается свободно.</summary>
    Rolling,
    /// <summary>Остановился (на пути или — плохо — в стрелочной зоне / на замедлителе).</summary>
    Stopped,
    /// <summary>Двигают локомотивом (протолкнуть, сжать путь, спуск с локомотивом).</summary>
    Forced,
    /// <summary>Сцепился с вагонами на пути — больше не отдельный отцеп.</summary>
    Merged,
    /// <summary>Выведен с пути (сформирован в поезд).</summary>
    Gone,
}

/// <summary>Отцеп — группа вагонов, которая скатывается с горки одна (строка сортировочного листка).</summary>
public sealed class Cut
{
    public int No;
    public readonly List<Wagon> Wagons = new();
    /// <summary>Путь по сортировочному листку.</summary>
    public int Dest;
    /// <summary>Маршрут, заданный в накопитель ГАЦ (0 — не задан). В режиме ГАЦ = Dest, в ручном — кнопкой.</summary>
    public int Route;
    public CutState State = CutState.InTrain;
    /// <summary>Положение головы отцепа (м от вершины горки; до горба — отрицательное) и скорость, м/с.</summary>
    public double Head, V;
    /// <summary>Решения на стрелках, пройденных головой: номер стрелки → по плюсу (к меньшим путям).</summary>
    public readonly Dictionary<int, bool> Passed = new();
    /// <summary>Диапазон путей, куда отцеп ещё может попасть (сужается на каждой стрелке).</summary>
    public int Lo = 1, Hi = 8;
    /// <summary>На какой путь въехал (0 — ещё не въехал).</summary>
    public int Track;
    public double ForcedV;
    /// <summary>[ДОБАВЛЕНО 26.11.2026] Наехал на тормозной башмак: где остановится (0 — не наезжал).</summary>
    public double ShoeStop;
    public bool Reported, StopChecked, Escort;
    /// <summary>Порядковый номер поезда, из которого отцеп.</summary>
    public int TrainNo;

    public double Length => Wagons.Sum(w => w.Length);
    public double Tail => Head - Length;
    public double Mass => Wagons.Sum(w => w.Mass);
    public double W0 => Wagons.Count == 0 ? 2 : Wagons.Sum(w => w.W0 * w.Mass) / Mass;
    public string Mark => string.Join("", Wagons.Select(w => w.Mark).Where(m => m != "").Distinct());
    public bool Forbidden => Mark != "";
    public Runner Runner => W0 <= 1.6 ? Runner.Good : W0 >= 2.5 ? Runner.Bad : Runner.Mid;
    public bool Active => State is CutState.Rolling or CutState.Stopped or CutState.Forced;
    public string KindsText => string.Join(", ", Wagons.GroupBy(w => w.Kind.Short).Select(g => g.Count() > 1 ? $"{g.Key}×{g.Count()}" : g.Key));
}
