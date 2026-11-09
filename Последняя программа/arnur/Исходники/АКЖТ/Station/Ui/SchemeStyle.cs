using System.Globalization;

namespace StationApp.Ui;

// =====================================================================
//  НАСТРОЙКИ ВИДА СХЕМЫ — размеры надписей и т.п. в одном месте.
//  [ДОБАВЛЕНО 08.10.2026]
//
//  Значения по умолчанию заданы ниже. Их можно поменять БЕЗ пересборки:
//  рядом с Station.exe лежит файл scheme_style.ini (создаётся сам при первом
//  запуске) — поправьте число, сохраните и перезапустите программу.
//  Строки "ключ = значение", '#' — комментарий. Неизвестные ключи и ошибки
//  в числах пропускаются (остаётся значение по умолчанию).
// =====================================================================

public static class SchemeStyle
{
    public const string FileName = "scheme_style.ini";

    // ---------- названия участков (секций): 1П, 4СП, Н-М1 ... ----------
    /// <summary>Размер шрифта названий участков (было 9.5).</summary>
    public static float SectionLabelSize = 13f;
    /// <summary>Жирный шрифт названий участков.</summary>
    public static bool SectionLabelBold = true;
    /// <summary>Минимальный зазор от линии пути до края надписи: при крупном шрифте
    /// надпись сама отодвигается от пути, чтобы не наезжать на него.</summary>
    public static float SectionLabelGap = 3f;
    /// <summary>Цвет названий участков (R,G,B).</summary>
    public static Color SectionLabelColor = Color.FromArgb(40, 60, 55);

    // ---------- остальные подписи ----------
    /// <summary>Размер шрифта названий светофоров (маршрутных).</summary>
    public static float SignalLabelSize = 13f;
    /// <summary>Размер шрифта названий проходных и маневровых светофоров.</summary>
    public static float SignalLabelSmallSize = 10f;
    /// <summary>Размер шрифта номеров стрелок.</summary>
    public static float SwitchLabelSize = 11f;

    // ---------- упрощённый режим ----------
    /// <summary>У светофоров, где огней не меньше этого числа (входные: Ж З К Ж Б),
    /// в упрощённом режиме рисуется вторая лампа: нижний жёлтый (два жёлтых)
    /// и пригласительный. 0 — не рисовать вторую лампу совсем.</summary>
    public static int SimpleSecondLampMinLamps = 5;

    static readonly (string key, string comment)[] Keys =
    {
        ("SectionLabelSize", "размер шрифта названий участков (1П, 4СП, Н-М1...), было 9.5"),
        ("SectionLabelBold", "жирный шрифт названий участков: 1 — да, 0 — нет"),
        ("SectionLabelGap", "зазор от пути до надписи участка (надпись сама отодвигается от пути)"),
        ("SectionLabelColor", "цвет названий участков: R,G,B"),
        ("SignalLabelSize", "размер шрифта названий маршрутных светофоров"),
        ("SignalLabelSmallSize", "размер шрифта названий проходных и маневровых светофоров"),
        ("SwitchLabelSize", "размер шрифта номеров стрелок"),
        ("SimpleSecondLampMinLamps", "упрощённый режим: вторая лампа у светофоров, где огней не меньше N (0 — выключить)"),
    };

    static string PathNearExe => System.IO.Path.Combine(AppContext.BaseDirectory, FileName);

    /// <summary>Прочитать scheme_style.ini рядом с программой; если файла нет — создать его со значениями по умолчанию.</summary>
    public static void Load()
    {
        string path = PathNearExe;
        try
        {
            if (!File.Exists(path)) { Save(path); return; }
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw;
                int hash = line.IndexOf('#');
                if (hash >= 0) line = line[..hash];
                int eq = line.IndexOf('=');
                if (eq <= 0) continue;
                Set(line[..eq].Trim(), line[(eq + 1)..].Trim());
            }
        }
        catch { /* нет доступа к файлу — работаем со значениями по умолчанию */ }
    }

    static void Set(string key, string value)
    {
        var inv = CultureInfo.InvariantCulture;
        value = value.Replace(',', '.');
        bool F(out float f) => float.TryParse(value, NumberStyles.Float, inv, out f) && f > 0 && f < 200;
        switch (key)
        {
            case "SectionLabelSize": if (F(out var a)) SectionLabelSize = a; break;
            case "SectionLabelGap": if (float.TryParse(value, NumberStyles.Float, inv, out var gap)) SectionLabelGap = gap; break;
            case "SectionLabelBold": SectionLabelBold = value is "1" or "true" or "да"; break;
            case "SignalLabelSize": if (F(out var b)) SignalLabelSize = b; break;
            case "SignalLabelSmallSize": if (F(out var c)) SignalLabelSmallSize = c; break;
            case "SwitchLabelSize": if (F(out var d)) SwitchLabelSize = d; break;
            case "SimpleSecondLampMinLamps": if (int.TryParse(value, out var n) && n >= 0) SimpleSecondLampMinLamps = n; break;
            case "SectionLabelColor":
                // value уже с точками вместо запятых: "40.60.55"
                var parts = value.Split('.', ' ', ';');
                var rgb = parts.Where(p => p.Length > 0).Select(p => int.TryParse(p, out var x) ? x : -1).ToArray();
                if (rgb.Length == 3 && rgb.All(x => x is >= 0 and <= 255))
                    SectionLabelColor = Color.FromArgb(rgb[0], rgb[1], rgb[2]);
                break;
        }
    }

    static string Value(string key)
    {
        var inv = CultureInfo.InvariantCulture;
        return key switch
        {
            "SectionLabelSize" => SectionLabelSize.ToString(inv),
            "SectionLabelBold" => SectionLabelBold ? "1" : "0",
            "SectionLabelGap" => SectionLabelGap.ToString(inv),
            "SectionLabelColor" => $"{SectionLabelColor.R},{SectionLabelColor.G},{SectionLabelColor.B}",
            "SignalLabelSize" => SignalLabelSize.ToString(inv),
            "SignalLabelSmallSize" => SignalLabelSmallSize.ToString(inv),
            "SwitchLabelSize" => SwitchLabelSize.ToString(inv),
            "SimpleSecondLampMinLamps" => SimpleSecondLampMinLamps.ToString(inv),
            _ => ""
        };
    }

    static void Save(string path)
    {
        var lines = new List<string>
        {
            "# Настройки вида схемы. Поменяйте число, сохраните файл и перезапустите программу.",
            "# Удалите файл — он создастся заново со значениями по умолчанию.",
            ""
        };
        foreach (var (key, comment) in Keys)
        {
            lines.Add("# " + comment);
            lines.Add($"{key} = {Value(key)}");
            lines.Add("");
        }
        File.WriteAllLines(path, lines);
    }
}
