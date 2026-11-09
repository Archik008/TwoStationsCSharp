using StationApp.Core;

namespace StationApp.Algabas;

// =====================================================================
//  "ПРОВОДА" СТАНЦИИ АЛГАБАС — перенесено 1:1 из Python ArduinoCode.py (V0.9.0)
//
//  Лампы:   HW_MAP_40       → (светофор, лампа) → (байт, бит) в пакете 'L' + 5 байт (0 = горит)
//  Стрелки: SWITCH_HW_MAP   → канал сервопривода на PCA9685, команда "W <канал> <1|2>"
//           (1 = "+" / "left", 2 = "−" / "right"; спаренная 4/6 — разъёмы 5 и 6 вместе)
//  Датчики: файл датчики.txt рядом с Algabas.exe → номер входа = символ строки "Data:"
//           СЛЕВА, считая с 0 ('0' = занято). Формат прошивки StationMain от 09.10.2026.
// =====================================================================

public static class AlgabasHardware
{
    public static HardwareMap Build()
    {
        var m = new HardwareMap();
        void L(string sig, string lamp, int b, int bit) => m.Lamps[(sig, lamp)] = (b, bit);

        // ---- байт 0 (reg5) ----
        L("Ч", "yellow", 0, 0);       // верхний жёлтый
        L("Ч", "green", 0, 1);
        L("Ч", "red", 0, 2);
        L("Ч", "yellow1", 0, 3);      // нижний жёлтый
        L("Ч", "white", 0, 4);        // пригласительный
        L("М2", "white", 0, 5);
        L("М2", "blue", 0, 6);
        L("Н1", "yellow", 0, 7);

        // ---- байт 1 (reg4) ----
        L("Н1", "green", 1, 0);
        L("Н1", "red", 1, 1);
        L("Н1", "white", 1, 2);
        L("М8", "white", 1, 3);
        L("М8", "red", 1, 4);
        L("М1", "white", 1, 5);
        L("М1", "red", 1, 6);

        // ---- байт 2 (reg3) ----
        L("Н2", "yellow", 2, 1);
        L("Н2", "green", 2, 2);
        L("Н2", "red", 2, 3);
        L("Н2", "white", 2, 4);
        L("Н3", "yellow", 2, 5);
        L("Н3", "green", 2, 6);
        L("Н3", "red", 2, 7);

        // ---- байт 3 (reg2) ----
        L("Н3", "white", 3, 0);
        L("Н4", "white", 3, 3);
        L("Н4", "green", 3, 4);
        L("Н4", "red", 3, 5);
        L("Н4", "yellow", 3, 6);
        L("2-ОП", "red", 3, 7);       // в Python: "ALB_Sect1-2_2" — проходной 2 (к Алгабасу)

        // ---- байт 4 (reg1) ----
        L("2-ОП", "yellow", 4, 0);
        L("2-ОП", "green", 4, 1);
        L("М6", "red", 4, 2);
        L("М6", "white", 4, 3);
        L("М10", "red", 4, 4);
        L("М10", "white", 4, 5);

        // ---- стрелки (сервоприводы) ----
        // [ИЗМЕНЕНО 27.10.2026] разъёмы подряд с нуля (PCA9685):
        //   стрелка | разъём на шилде
        //   1       | 0               ← первый путь: маршрут на 1 → физика на пин 0
        //   2       | 1
        //   4/6     | 2 (стрелка 4) и 3 (стрелка 6)
        //   8       | 4
        // Плюс/минус: "W <разъём> 1" / "W <разъём> 2". Калибровка — в StationMain Switches.h.
        m.Servos["1"] = new[] { 0 };
        m.Servos["4/6"] = new[] { 2, 3 };
        m.Servos["8"] = new[] { 4 };
        m.Servos["2"] = new[] { 1 };

        // ---- датчики занятости (строка "Data:", плата 74HC165) ----
        // [ИЗМЕНЕНО 09.10.2026] Карта датчиков больше не зашита здесь — она в файле датчики.txt
        // рядом с Algabas.exe (см. LoadSensors ниже). Её можно править без пересборки программы.
        // Старая карта из Python (биты 0..8, справа налево) указывала на 3-й регистр 74HC165,
        // к которому, по маске новой прошивки, ничего не подключено, — поэтому биты были «рандомные».

        return m;
    }

    // =================================================================
    //  ФАЙЛ датчики.txt — какой вход 74HC165 к какому участку
    // =================================================================

    public static string SensorsFile => Path.Combine(AppContext.BaseDirectory, "датчики.txt");

    /// <summary>Прочитать датчики.txt в m.Sensors. Файла нет — создать заготовку со всеми участками.
    /// Возвращает сообщения для журнала (ошибки в строках, итог).</summary>
    public static List<string> LoadSensors(HardwareMap m, IEnumerable<TrackSection> sections)
    {
        var msgs = new List<string>();
        var byLabel = sections.ToDictionary(s => s.Label, s => s.Id);
        m.Sensors.Clear();

        if (!File.Exists(SensorsFile))
        {
            try
            {
                File.WriteAllText(SensorsFile, Template(sections), System.Text.Encoding.UTF8);
                msgs.Add($"Создан файл карты датчиков {SensorsFile}. Впишите в него номера входов (как — написано в самом файле).");
            }
            catch (Exception ex) { msgs.Add("Не удалось создать датчики.txt: " + ex.Message); }
            return msgs;
        }

        string[] lines;
        try { lines = File.ReadAllLines(SensorsFile); }
        catch (Exception ex) { msgs.Add("Не удалось прочитать датчики.txt: " + ex.Message); return msgs; }

        for (int n = 0; n < lines.Length; n++)
        {
            var line = lines[n];
            int hash = line.IndexOf('#');
            if (hash >= 0) line = line.Substring(0, hash);
            line = line.Trim();
            if (line.Length == 0) continue;

            int eq = line.IndexOf('=');
            if (eq < 0) { msgs.Add($"датчики.txt, строка {n + 1}: нет знака «=» — строка пропущена"); continue; }
            var label = line.Substring(0, eq).Trim();
            var num = line.Substring(eq + 1).Trim();
            if (num.Length == 0) continue;                       // у участка нет датчика

            if (!byLabel.TryGetValue(label, out var id))
            { msgs.Add($"датчики.txt, строка {n + 1}: участка «{label}» на схеме нет — строка пропущена"); continue; }
            if (!int.TryParse(num, out int bit) || bit < 0 || bit >= ArduinoLink.SensorBits)
            { msgs.Add($"датчики.txt, строка {n + 1}: «{num}» — нужен номер входа от 0 до {ArduinoLink.SensorBits - 1}"); continue; }

            var other = m.Sensors.FirstOrDefault(kv => kv.Value == bit).Key;
            if (other != null) msgs.Add($"датчики.txt: вход {bit} указан и у {other}, и у {label} — оба участка будут заниматься вместе");
            m.Sensors[id] = bit;
        }

        msgs.Add(m.Sensors.Count == 0
            ? "Карта датчиков пуста: в датчики.txt не вписан ни один вход — занятость с макета на табло не попадёт."
            : "Карта датчиков: " + string.Join(", ", m.Sensors.OrderBy(kv => kv.Value).Select(kv => $"{kv.Key} = вход {kv.Value}")));
        return msgs;
    }

    static string Template(IEnumerable<TrackSection> sections)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("# КАРТА ДАТЧИКОВ ЗАНЯТОСТИ СТАНЦИИ АЛГАБАС");
        sb.AppendLine("#");
        sb.AppendLine("# Arduino шлёт строку \"Data: 011111111111111111111111\" — 24 символа, '0' = занято.");
        sb.AppendLine("# Номер входа = номер символа СЛЕВА, считая с 0 — так же, как «индексы 0..23»");
        sb.AppendLine("# в мониторе порта по команде S:  входы 0..7 — 1-й регистр 74HC165, 8..15 — 2-й, 16..23 — 3-й.");
        sb.AppendLine("#");
        sb.AppendLine("# Как заполнить: подключите Arduino, поставьте вагон на участок — в журнале программы");
        sb.AppendLine("# появится «Вход 5: ЗАНЯТ — к участку не привязан». Впишите 5 после «=» у этого участка");
        sb.AppendLine("# и сохраните файл. Программа перечитает его сама, перезапускать не нужно.");
        sb.AppendLine("# Пусто после «=» — у участка нет датчика (занимается только виртуальными поездами).");
        sb.AppendLine("# Всё после # — комментарий.");
        sb.AppendLine();
        foreach (var s in sections)
            sb.AppendLine($"{s.Label} =        # {s.Title}");
        return sb.ToString();
    }
}
