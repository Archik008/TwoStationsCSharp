namespace StationEditor.Model;

// =====================================================================
//  [ДОБАВЛЕНО 05.11.2026] ПЕРЕВОД СТРЕЛОК И АМПЕРМЕТР ТАБЛО
//
//  На пульте-табло ЭЦ амперметр показывает ток перевода стрелок (пособие электромеханику ЭЦ, с. 3).
//  Стрелки переводятся по одной (очередь), пока стрелка переводится — у неё нет контроля положения.
//  Ток при переводе (типовые значения для электропривода с двигателем МСП, в пособии цифр нет):
//    пуск — бросок до StartAmp, затем рабочий ток WorkAmp, перевод ~ThrowTime с, в конце ток падает до 0;
//    препятствие между остряком и рамным рельсом (неисправность «не доходит») — остряк упирается,
//    двигатель работает на фрикцию: ток FrictionAmp (на 25–40 % больше рабочего), через FrictionCutoff с
//    привод отключается, стрелка остаётся БЕЗ КОНТРОЛЯ. Вернуть её в прежнее положение можно — препятствие
//    мешает только идти в другую сторону.
// =====================================================================
public sealed class SwitchThrow
{
    public string Group, Name;
    public List<string> Keys;
    public bool ToPlus, Jammed;
    public double T;
}

/// <summary>Что случилось за шаг таймера (для строки сообщений).</summary>
public readonly record struct DriveEvent(string Text, bool Bad);

public sealed class SwitchDrive
{
    public const double ThrowTime = 3.2, StartTime = 0.15, JamAt = 1.3, FrictionCutoff = 7.0;
    public const double WorkAmp = 1.8, StartAmp = 3.6, FrictionAmp = 2.4, ScaleMax = 5.0;

    public readonly List<SwitchThrow> Queue = new();
    public SwitchThrow Current;
    /// <summary>Группы стрелок с препятствием («не доходит»)</summary>
    public readonly HashSet<string> Jammed = new();
    /// <summary>Клетки стрелок, потерявших контроль после работы на фрикцию</summary>
    public readonly HashSet<string> Lost = new();
    /// <summary>Ток сейчас и то, что показывает стрелка прибора (с инерцией)</summary>
    public double Amp, Shown;
    readonly Random rnd = new();

    public bool Active => Current != null || Queue.Count > 0 || Shown > 0.01;

    /// <summary>Группа сейчас переводится или стоит в очереди.</summary>
    public bool Moving(string group) => Current?.Group == group || Queue.Any(q => q.Group == group);

    /// <summary>Клетки стрелок без контроля: переводится сейчас или потеряла контроль.</summary>
    public HashSet<string> NoControl()
    {
        var s = new HashSet<string>(Lost);
        if (Current != null) foreach (var k in Current.Keys) s.Add(k);
        return s;
    }

    public void Reset()
    {
        Queue.Clear();
        Current = null;
        Lost.Clear();
        Amp = Shown = 0;
    }

    /// <summary>Поставить стрелку в очередь на перевод. recordedPlus — где она стоит по контролю (до перевода).</summary>
    public void Enqueue(string group, List<string> keys, string name, bool toPlus, bool recordedPlus)
    {
        Queue.Add(new SwitchThrow
        {
            Group = group, Keys = keys, Name = name, ToPlus = toPlus,
            Jammed = Jammed.Contains(group) && toPlus != recordedPlus,
        });
    }

    /// <summary>Шаг времени. Возвращает событие (перевелась / не перевелась) или null.</summary>
    public DriveEvent? Tick(Station st, double dt)
    {
        DriveEvent? ev = null;
        if (Current == null && Queue.Count > 0)
        {
            Current = Queue[0];
            Queue.RemoveAt(0);
            Current.T = 0;
            foreach (var k in Current.Keys) Lost.Remove(k);
        }
        if (Current != null)
        {
            var c = Current;
            c.T += dt;
            double ripple = (rnd.NextDouble() - 0.5) * 0.08;
            if (c.T < StartTime) Amp = StartAmp - (StartAmp - WorkAmp) * (c.T / StartTime);
            else if (!c.Jammed) Amp = WorkAmp + ripple;
            else Amp = c.T < JamAt ? WorkAmp + ripple : FrictionAmp + ripple;

            if (!c.Jammed && c.T >= ThrowTime)
            {
                foreach (var k in c.Keys)
                    if (st.Tiles.TryGetValue(k, out var t)) t.Plus = c.ToPlus ? null : false;
                ev = new DriveEvent($"Стрелка {c.Name} переведена в {(c.ToPlus ? "плюс" : "минус")} ({ThrowTime:0.0} с)", false);
                Current = null;
                Amp = 0;
            }
            else if (c.Jammed && c.T >= FrictionCutoff)
            {
                foreach (var k in c.Keys) Lost.Add(k);
                ev = new DriveEvent($"Стрелка {c.Name} не дошла: работа на фрикцию {FrictionAmp:0.0} А, привод отключён — нет контроля положения. " +
                                    "Верните стрелку щелчком или уберите препятствие", true);
                Current = null;
                Amp = 0;
            }
        }
        else Amp = 0;
        // стрелка прибора догоняет ток с небольшой инерцией
        Shown += (Amp - Shown) * Math.Min(1, dt * 12);
        if (Shown < 0.005) Shown = 0;
        return ev;
    }
}
