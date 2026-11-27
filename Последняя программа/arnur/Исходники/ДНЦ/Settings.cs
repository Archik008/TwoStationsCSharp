namespace DncApp;

// =====================================================================
//  НАСТРОЙКИ ДНЦ: адрес сервера, сколько схем можно открыть, какие схемы в окне и в каком порядке,
//  режим интерфейса станций, схемы, которые диспетчер убрал (сами больше не добавляются).
//  Файл %LOCALAPPDATA%\StationDnc\dnc.txt, строки «ключ=значение»; схема в окне — «tile=станция|схема»,
//  убранная — «hide=станция|схема».
// =====================================================================

public static class Settings
{
    /// <summary>Папка настроек; переменная окружения DNC_DATA_DIR — другая папка (проверки, второй комплект настроек).</summary>
    public static readonly string Dir = Environment.GetEnvironmentVariable("DNC_DATA_DIR") is { Length: > 0 } d ? d
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationDnc");
    static string FilePath => Path.Combine(Dir, "dnc.txt");

    /// <summary>Адрес сервера PromServer ("192.168.1.5" или "192.168.1.5:8090").</summary>
    public static string Server = "127.0.0.1";
    /// <summary>Сколько схем можно открыть в окне (N).</summary>
    public static int Max = 8;
    /// <summary>Масштаб ленты: доля высоты окна, которую занимает схема (1 — во всю высоту).</summary>
    public static float Zoom = 1f;
    /// <summary>Схемы в окне по порядку слева направо: станция на сервере и название схемы.</summary>
    public static readonly List<(string station, string scheme)> Tiles = new();
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Схемы, убранные диспетчером (✕): при подключении станции сами не добавляются.</summary>
    public static readonly HashSet<string> Hidden = new();
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Режим интерфейса у всех станций: Work — «Медеуский» (по умолчанию), Classic — «Павлодарский», [26.11.2026] Training — «Учебный».</summary>
    public static string Mode = "Work";
    /// <summary>[ДОБАВЛЕНО 18.11.2026] Порядок схем задал диспетчер (перетаскивание, ◀ ▶); иначе — по стыкам путей.</summary>
    public static bool ManualOrder;
    /// <summary>[ДОБАВЛЕНО 19.11.2026] Где панель управления на двух мониторах: auto — на основном мониторе (как у АКЖТ
    /// «АКЖТ — на основном»), left / right — у левого / правого края окна (кнопка «⇄»).</summary>
    public static string PanelSide = "auto";

    public static string Key(string station, string scheme) => station + "|" + scheme;

    public static void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            foreach (var line in File.ReadAllLines(FilePath))
            {
                int i = line.IndexOf('=');
                if (i <= 0) continue;
                var (k, v) = (line[..i].Trim(), line[(i + 1)..].Trim());
                switch (k)
                {
                    case "server": Server = v; break;
                    case "max": if (int.TryParse(v, out var m)) Max = Math.Clamp(m, 1, 30); break;
                    case "zoom":
                        if (float.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var z))
                            Zoom = Math.Clamp(z, 0.05f, 1f);
                        break;
                    case "tile":
                        int j = v.IndexOf('|');
                        if (j > 0) Tiles.Add((v[..j], v[(j + 1)..]));
                        break;
                    case "hide": if (v.IndexOf('|') > 0) Hidden.Add(v); break;
                    case "mode": if (v is "Work" or "Classic" or "Training") Mode = v; break;
                    case "order": ManualOrder = v == "manual"; break;
                    case "panel": if (v is "auto" or "left" or "right") PanelSide = v; break;
                }
            }
        }
        catch { }
    }

    public static void Save()
    {
        try
        {
            Directory.CreateDirectory(Dir);
            var lines = new List<string> { $"server={Server}", $"max={Max}", "zoom=" + Zoom.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture), $"mode={Mode}", $"order={(ManualOrder ? "manual" : "auto")}", $"panel={PanelSide}" };
            lines.AddRange(Tiles.Select(t => $"tile={t.station}|{t.scheme}"));
            lines.AddRange(Hidden.Select(h => $"hide={h}"));
            File.WriteAllLines(FilePath, lines);
        }
        catch { }
    }
}
