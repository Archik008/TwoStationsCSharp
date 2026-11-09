namespace StationApp.Core;

// =====================================================================
//  ОБЩЕЕ ЯДРО ЖД-ЗАВИСИМОСТЕЙ — МОДЕЛЬ
//
//  Здесь только "кирпичики": участок, стрелка, светофор, маршрут.
//  Ни одной строчки про конкретную станцию! Конкретная схема
//  (путевой пост и т.д.) описывается отдельно — см. Post/PostLayout.cs.
//  Это первый шаг к универсальной программе: ядро одно, схемы разные.
// =====================================================================

/// <summary>Направление движения по экрану.</summary>
public enum Dir
{
    /// <summary>Слева направо (у нас — чётное: от АКЖТ к посту и к Алгабасу).</summary>
    Right,
    /// <summary>Справа налево (у нас — нечётное: к АКЖТ).</summary>
    Left
}

/// <summary>
/// Сигнальные показания по Инструкции по сигнализации (ПТЭ РК / СНГ).
/// </summary>
public enum Aspect
{
    /// <summary>Светофор погашен (проходной неправильного направления на однопутном перегоне).</summary>
    Dark,
    /// <summary>Красный — стой.</summary>
    Red,
    /// <summary>Один жёлтый — следующий светофор закрыт.</summary>
    Yellow,
    /// <summary>Один жёлтый мигающий — следующий светофор открыт с отклонением по стрелке.</summary>
    YellowBlink,
    /// <summary>Зелёный — свободны два и более блок-участка.</summary>
    Green,
    /// <summary>Два жёлтых — по отклонению, следующий закрыт.</summary>
    TwoYellow,
    /// <summary>Два жёлтых, верхний мигающий — по отклонению, следующий открыт.</summary>
    TwoYellowTopBlink,
    /// <summary>Красный + лунно-белый мигающий — пригласительный сигнал.</summary>
    Invite,
    /// <summary>Лунно-белый — маневры разрешены.</summary>
    White,
    /// <summary>Синий — маневры запрещены (маневровый светофор).</summary>
    Blue
}

/// <summary>Вид светофора.</summary>
public enum SignalKind
{
    /// <summary>Маршрутный (входной/выходной): открывается только по заданному маршруту.</summary>
    Controlled,
    /// <summary>Проходной автоблокировки: меняет огни сам по занятости блок-участков.</summary>
    Automatic,
    /// <summary>Светофор соседней станции: на нашем пульте только показывается (имитация дежурного соседа).</summary>
    Neighbor
}

public static class AspectInfo
{
    /// <summary>Разрешает ли показание проехать светофор.</summary>
    public static bool IsPermissive(Aspect a) =>
        a is Aspect.Yellow or Aspect.YellowBlink or Aspect.Green
          or Aspect.TwoYellow or Aspect.TwoYellowTopBlink or Aspect.Invite or Aspect.White;

    /// <summary>"Запрещающее" для следующего светофора: красный, погашен или пригласительный.</summary>
    public static bool IsRestrictive(Aspect a) =>
        a is Aspect.Red or Aspect.Dark or Aspect.Invite or Aspect.Blue or Aspect.White;

    /// <summary>Короткое название показания.</summary>
    public static string Short(Aspect a) => a switch
    {
        Aspect.Dark => "погашен",
        Aspect.Red => "красный",
        Aspect.Yellow => "жёлтый",
        Aspect.YellowBlink => "жёлтый мигающий",
        Aspect.Green => "зелёный",
        Aspect.TwoYellow => "два жёлтых",
        Aspect.TwoYellowTopBlink => "два жёлтых, верхний мигающий",
        Aspect.Invite => "пригласительный",
        Aspect.White => "лунно-белый (маневры)",
        Aspect.Blue => "синий",
        _ => a.ToString()
    };

    /// <summary>Что означает показание — для обучения (по Инструкции по сигнализации).</summary>
    public static string Meaning(Aspect a) => a switch
    {
        Aspect.Dark => "Погашен: проходной светофор неправильного направления на однопутном перегоне.",
        Aspect.Red => "Красный — СТОЙ! Запрещается проезжать сигнал.",
        Aspect.Yellow => "Один жёлтый — разрешается движение с готовностью остановиться: следующий светофор закрыт.",
        Aspect.YellowBlink => "Жёлтый мигающий — следующий светофор открыт и требует проследования по отклонению с уменьшенной скоростью.",
        Aspect.Green => "Зелёный — разрешается движение с установленной скоростью, впереди свободны два и более блок-участка.",
        Aspect.TwoYellow => "Два жёлтых — движение с уменьшенной скоростью с отклонением по стрелке; следующий светофор закрыт.",
        Aspect.TwoYellowTopBlink => "Два жёлтых, верхний мигающий — с уменьшенной скоростью по отклонению; следующий светофор открыт.",
        Aspect.Invite => "Красный + лунно-белый мигающий (пригласительный) — проследовать со скоростью не более 20 км/ч с особой бдительностью.",
        Aspect.White => "Лунно-белый — маневры разрешены (маневровый маршрут задан).",
        Aspect.Blue => "Синий — маневры запрещены.",
        _ => ""
    };
}

// ---------------------------------------------------------------------
//  УЧАСТОК (рельсовая цепь / блок-участок)
// ---------------------------------------------------------------------
public class TrackSection
{
    public string Id = "";             // внутреннее уникальное имя (для АКЖТ с приставкой "А:")
    public string Label = "";          // как подписано на схеме: "1СП"
    public string Title = "";          // пояснение для людей: "1-2СП — стрелочный участок стрелок 1 и 3"

    public bool ManualOcc;             // занят вручную (ПКМ) — имитация, в т.ч. "ложная занятость"
    public bool HwOcc;                 // занят по датчику Arduino
    public int TrainCount;             // сколько виртуальных поездов стоит на участке
    /// <summary>[ДОБАВЛЕНО 12.10.2026] Занят по данным соседней станции (общий участок перегона, связь диспетчеров).</summary>
    public bool RemoteOcc;

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправности от преподавателя (Core/Faults.cs):
    /// FaultOcc — ложная занятость, FaultFree — ложная свободность (поезд и датчик не занимают участок).</summary>
    public bool FaultOcc, FaultFree;

    /// <summary>Поезд (виртуальный или по датчику) занимает участок — если рельсовая цепь исправна.</summary>
    bool RealOcc => !FaultFree && (HwOcc || TrainCount > 0);

    public bool Occupied => ManualOcc || FaultOcc || RealOcc || RemoteOcc;

    /// <summary>Занят по НАШИМ данным (без соседа) — это и отправляем соседу, иначе занятость «зациклится».</summary>
    public bool LocalOccupied => ManualOcc || FaultOcc || RealOcc;

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Участок перегона входит в поездной маршрут на перегон — НАШ (LineRouteOwn)
    /// или соседней станции (LineRouteRemote). На схеме подсвечивается как маршрут, до входного светофора соседа.
    /// Участок при этом не замыкается: перегон защищают направление и автоблокировка.</summary>
    public bool LineRouteOwn, LineRouteRemote;
    public bool LineRoute => LineRouteOwn || LineRouteRemote;

    /// <summary>Занят, но не виртуальным поездом (то есть вручную или датчиком) —
    /// для вспомогательных команд это "возможно ложная занятость".</summary>
    public bool OccupiedWithoutTrain => (ManualOcc || HwOcc || FaultOcc) && TrainCount == 0;

    /// <summary>Каким маршрутом участок замкнут (null — свободен от замыкания).</summary>
    public RailRoute LockedBy;
    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] &gt; 0 — идёт искусственная разделка этой секции (кнопка ИР).</summary>
    public double IrTimer;

    // ---- [ДОБАВЛЕНО 23.10.2026, этап 4] предупреждения и «окна» (Core/Radio.cs) ----
    /// <summary>Действующее предупреждение: скорость пассажирских и грузовых, км/ч (0 — без ограничения).</summary>
    public int WarnPass, WarnFreight;
    public bool Warning => WarnPass > 0 || WarnFreight > 0;
    /// <summary>«Окно»: путь закрыт для работ — маршрут не задаётся, поезд не вступает.</summary>
    public bool Window;
    /// <summary>Номер настоящего поезда на макете, занимающего участок по датчику (0 — не задан).</summary>
    public int HwTrainNo;

    public override string ToString() => Label;
}

// ---------------------------------------------------------------------
//  СТРЕЛКА (может быть спаренной: "1/2" = стрелки 1 и 2 съезда переводятся вместе)
// ---------------------------------------------------------------------
public class RailSwitch
{
    public string Id = "";             // внутреннее уникальное имя: "1/2" или "А:5/7"
    public string Label = "";          // как на схеме: "5/7"
    public string Title = "";

    public bool Plus = true;           // + прямо (нормальное положение), − на отклонение
    public bool Controlled = true;     // есть контроль положения (не в процессе перевода)
    public double MoveTimer;           // сколько секунд ещё переводится

    /// <summary>Стрелочные участки, на которых лежит стрелка (для спаренной — оба).</summary>
    public List<TrackSection> Sections = new();

    /// <summary>Маршруты, которые заперли стрелку (могут быть несколько, если им нужно одно положение).</summary>
    public HashSet<RailRoute> LockedBy = new();

    /// <summary>Сколько пригласительных сигналов держат стрелку запертой.</summary>
    public int InviteLocks;

    public bool Locked => LockedBy.Count > 0 || InviteLocks > 0;

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправности (Core/Faults.cs): нет контроля вообще
    /// (потеря контроля, отжим) или стрелка не доходит в «+» / в «−».</summary>
    public bool FaultNoControl, FaultNoPlus, FaultNoMinus;
    /// <summary>Перевод не удался из-за неисправности, команда на привод (Arduino) не ушла — уйдёт после устранения.</summary>
    public bool PendingCommand;
    /// <summary>Контроль в текущем положении сейчас получить нельзя.</summary>
    public bool FaultBlocked => FaultNoControl || (Plus ? FaultNoPlus : FaultNoMinus);

    // ---- [ДОБАВЛЕНО 21.10.2026, этап 6] пульт ДСП (Core/Panel.cs) ----
    /// <summary>Красный колпачок на кнопке стрелки: с пульта не переводится.</summary>
    public bool RedCap;
    /// <summary>Курбельная заслонка опущена: электропривод отключён, контроля нет, можно переводить курбелем.</summary>
    public bool FlapDown;
    /// <summary>Заперта на закладку и навесной замок («замок») или на типовую скобу («скоба»); пусто — нет.</summary>
    public string Clamp = "";
    /// <summary>Выключена из централизации с сохранением пользования сигналами.</summary>
    public bool Excluded;
    /// <summary>Положение, установленное на макете (только у выключенной стрелки); null — макета нет.</summary>
    public bool? Maket;
    /// <summary>Взрез: остряки отжаты поездом, контроля нет до восстановления.</summary>
    public bool Cut;
    /// <summary>Что видит пульт и зависимости: у выключенной стрелки на макете — положение макета, иначе фактическое (Plus).</summary>
    public bool ShownPlus => Excluded && Maket is bool m ? m : Plus;

    public string PosText => ShownPlus ? "+" : "−";   // [ИЗМЕНЕНО 21.10.2026, этап 6] у стрелки на макете — положение макета

    public override string ToString() => Label;
}

// ---------------------------------------------------------------------
//  СВЕТОФОР
// ---------------------------------------------------------------------
public class RailSignal
{
    public string Id = "";             // внутреннее уникальное имя: "1-ОП"
    public string Label = "";          // подпись на схеме, как на чертеже: "1"
    public string Title = "";          // пояснение: "Проходной 1 однопутного перегона"
    public SignalKind Kind;
    public Dir Dir;                    // для какого направления движения

    /// <summary>Какие лампы физически есть, СВЕРХУ ВНИЗ по мачте (на схеме нижняя лампа — ближе всех к мачте):
    /// "yellow" (верхний жёлтый), "green", "red", "yellow1" (нижний жёлтый), "white" (пригласительный).
    /// Названия такие же, как в Python-версии Алгабаса.</summary>
    public string[] Lamps = Array.Empty<string>();

    public Aspect Aspect = Aspect.Red;

    // --- проходной (Automatic) ---
    public TrackSection Protects;      // блок-участок за светофором
    public RailSignal Next;            // следующий светофор по ходу
    public Func<bool> IsActive;        // null = всегда; для однопутки — только при своём направлении

    // --- соседней станции (Neighbor) ---
    public bool NeighborOpen;

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Показание задаёт другая станция по сети (связь диспетчеров).
    /// null — считается здесь, как раньше. Не null — светофор только показывает присланное.</summary>
    public Aspect? Remote;

    /// <summary>[ДОБАВЛЕНО 12.10.2026] Упрощённый режим: рисовать вторую лампу (нижний жёлтый / пригласительный),
    /// даже если огней меньше порога SimpleSecondLampMinLamps (например, Н поста — 4 огня).</summary>
    public bool SimpleSecondLamp;

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправность: светофор не открывается (или перекрылся сам).</summary>
    public bool FaultClosed;

    // --- маршрутный (Controlled) ---
    public TrackSection Approach;      // участок приближения (перед светофором)
    public double InviteTimer;         // > 0 — горит пригласительный
    public RailRoute InviteRoute;      // по положению каких стрелок открыт пригласительный
    public bool InviteOn => InviteTimer > 0;

    public bool Has(string lamp) => Array.IndexOf(Lamps, lamp) >= 0;

    /// <summary>Чисто маневровый светофор (нет ни зелёного, ни жёлтого).</summary>
    public bool IsShuntOnly => !Has("green") && !Has("yellow");

    /// <summary>Какие лампы горят при текущем показании (и мигают ли).</summary>
    public IEnumerable<(string lamp, bool blink)> LitLamps()
    {
        switch (Aspect)
        {
            case Aspect.Red: yield return ("red", false); break;
            case Aspect.Yellow: yield return ("yellow", false); break;
            case Aspect.YellowBlink: yield return ("yellow", true); break;
            case Aspect.Green: yield return ("green", false); break;
            case Aspect.TwoYellow: yield return ("yellow", false); yield return ("yellow1", false); break;
            case Aspect.TwoYellowTopBlink: yield return ("yellow", true); yield return ("yellow1", false); break;
            case Aspect.Invite: yield return ("red", false); yield return ("white", true); break;
            case Aspect.White: yield return ("white", false); break;
            case Aspect.Blue: yield return ("blue", false); break;
        }
    }

    /// <summary>Горит ли конкретная лампа прямо сейчас (с учётом фазы мигания).</summary>
    public bool IsLampOn(string lamp, bool blinkPhase)
    {
        foreach (var (l, blink) in LitLamps())
            if (l == lamp) return !blink || blinkPhase;
        return false;
    }

    public override string ToString() => Id;
}

// ---------------------------------------------------------------------
//  МАРШРУТ
// ---------------------------------------------------------------------
public class RailRoute
{
    public string Id = "";
    public string Name = "";           // "Ч → путь 3"
    public bool Shunting;              // маневровый маршрут (белый огонь, путь назначения может быть занят)
    public TrackSection Dest;          // путь назначения — по нему можно кликнуть вторым кликом
    public RailSignal Start;           // светофор, который открывается
    public RailSignal End;             // следующий светофор (конец маршрута) — от него зависит показание

    /// <summary>Участки маршрута ПО ПОРЯДКУ движения — они замыкаются.</summary>
    public List<TrackSection> Sections = new();

    /// <summary>Нужные положения стрелок.</summary>
    public List<(RailSwitch sw, bool plus)> Switches = new();

    /// <summary>Первый блок-участок перегона за маршрутом (для отправления). Должен быть свободен,
    /// иначе светофор не откроется. Может быть null.</summary>
    public TrackSection ExitSection;

    /// <summary>Маршрут с отклонением по стрелкам → на светофоре два жёлтых.</summary>
    public bool Diverging;

    /// <summary>Доп. условие (например, направление однопутного перегона). Вернуть текст причины или null.</summary>
    public Func<string> Precondition;

    /// <summary>Кусочки рисунка, которые подсвечиваются жёлтым (это забота схемы, ядру не важно).</summary>
    public List<string> Pieces = new();

    // ---------- состояние ----------
    public bool Active;
    public bool TrainEntered;          // поезд вступил в маршрут → светофор перекрыт навсегда
    public bool SignalWasOpen;         // светофор хоть раз открывался (для выдержки времени)
    public double ArtificialTimer;     // > 0 — идёт искусственная разделка
    // [ДОБАВЛЕНО 20.10.2026] > 0 — маршрут устанавливается (как в Python TrainSt: 2 с мигает, потом светофор)
    public double SetupTimer;
    public readonly HashSet<TrackSection> WasOccupied = new();
    public readonly HashSet<TrackSection> Released = new();

    public bool Releasing => ArtificialTimer > 0;
    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] Какая выдержка идёт: «ОС» (отмена, приближение свободно), «ОП» / «ОМ»
    /// (отмена поездного / маневрового при занятом приближении).</summary>
    public string CancelKind = "";
    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] Маршрут на автодействии (АДЧ / АДН): после проследования поезда задаётся сам.</summary>
    public bool Auto;
    /// <summary>Маршрут задан, но светофор ещё ни разу не открылся — на схеме маршрут мигает.</summary>
    public bool Setting => Active && !SignalWasOpen && !TrainEntered && !Releasing;
    public long SetOrder;              // порядковый номер задания (для "Отменить маршрут" — последний заданный)

    internal void ResetState()
    {
        Active = false;
        TrainEntered = false;
        SignalWasOpen = false;
        ArtificialTimer = 0;
        SetupTimer = 0;
        WasOccupied.Clear();
        Released.Clear();
    }

    public override string ToString() => Name;
}

// ---------------------------------------------------------------------
//  ОДНОПУТНЫЙ ПЕРЕГОН С ДВУСТОРОННЕЙ АВТОБЛОКИРОВКОЙ (смена направления)
// ---------------------------------------------------------------------
public class DirectionLine
{
    public string Id = "";
    public string Title = "";
    public Dir Current = Dir.Right;
    public string RightName = "вправо", LeftName = "влево";   // для пульта: "на Алгабас (чётное)"
    public List<TrackSection> Sections = new();

    /// <summary>Маршруты, которые зависят от направления (их нельзя иметь заданными при смене).</summary>
    public List<RailRoute> DependentRoutes = new();

    public double AuxTimer;            // > 0 — идёт вспомогательная смена направления
    public bool AuxRunning => AuxTimer > 0;

    /// <summary>[ДОБАВЛЕНО 12.10.2026] > 0 — идёт согласование смены направления с соседней станцией
    /// по сети: менять направление и задавать маршруты на перегон нельзя.</summary>
    public double SyncTimer;
    public bool Syncing => SyncTimer > 0;
    /// <summary>Что написать в журнал, если согласование не закончилось за SyncTimer.</summary>
    public string SyncTimeoutText = "";

    public string DirText(Dir d) => d == Dir.Right ? "→" : "←";

    /// <summary>[ДОБАВЛЕНО 20.10.2026, этап 3] Неисправность: направление не меняется.</summary>
    public bool FaultNoChange;

    /// <summary>[ДОБАВЛЕНО 21.10.2026, этап 6] Ключ-жезл изъят из пульта (лампа мигает); KeyTrain — номер локомотива,
    /// которому выдан (0 — у дежурного); KeyTrips — сколько единиц отправлено на перегон, пока ключ изъят.</summary>
    public bool KeyOut;
    public int KeyTrain, KeyTrips;
}

// ---------------------------------------------------------------------
//  ПЕРЕЕЗД
// ---------------------------------------------------------------------
public class LevelCrossing
{
    public string Id = "";
    public string Title = "";
    public Func<bool> ShouldClose;     // условие закрытия задаёт схема (участки приближения и т.д.)
    public bool Closed;
    /// <summary>[ДОБАВЛЕНО 20.10.2026] Закрыт дежурным кнопкой (временное ручное управление).
    /// Пока true — переезд закрыт; автоматика (поезд на переезде) закрывает его и без кнопки.</summary>
    public bool ManualClosed;

    // ---- [ДОБАВЛЕНО 21.10.2026, этап 6] извещение и лампа ЗП ----
    /// <summary>Извещение на переезд дано кнопкой (до «Отмены извещения»).</summary>
    public bool Notice;
    /// <summary>&gt; 0 — переезд закрывается (лампа ЗП мигает), секунды до закрытия.</summary>
    public double ClosingTimer;
    public double ClosingSeconds = 10;
    public bool Closing => ClosingTimer > 0;
    /// <summary>Переезд закрылся по извещению (выдержка прошла).</summary>
    public bool NoticeReady;
    /// <summary>Маршруты через переезд: заданный маршрут даёт извещение, светофор открывается только при закрытом переезде.</summary>
    public readonly List<RailRoute> Routes = new();
}

public enum LogKind { Info, Refuse, Aux, Train }
