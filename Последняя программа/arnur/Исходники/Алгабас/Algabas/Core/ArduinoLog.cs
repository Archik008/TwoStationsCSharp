using System.Reflection;
using System.Text;

namespace StationApp.Core;

// =====================================================================
//  [ДОБАВЛЕНО 23.11.2026] ЖУРНАЛ СВЯЗИ С ARDUINO В ТЕКСТОВЫХ ФАЙЛАХ
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса («Станция по схеме» берёт файл Алгабаса).
//
//  Рядом с exe — папка «Логи Arduino», в ней файл на каждый день: «2026-11-23 АКЖТ.txt».
//  Пишется: всё, что прислала плата (датчики Data / Occ, сообщения прошивки), подключение и отключение
//  порта, ошибки, команды стрелок и кадры ламп к плате (только когда кадр поменялся).
//  Одинаковые строки от платы подряд не дублируются: раз в минуту и при смене строки — «то же ещё N раз».
//  Файлы старше 30 дней удаляются сами. Нельзя писать рядом с exe — пишется в %LOCALAPPDATA%\StationArduinoLogs.
//  Запись — из любого потока (порт присылает данные в своём потоке), ошибки диска программу не роняют.
// =====================================================================

public static class ArduinoLog
{
    /// <summary>Писать журнал (по умолчанию — да).</summary>
    public static bool Enabled = true;
    /// <summary>Сколько дней хранить файлы.</summary>
    public static int KeepDays = 30;
    /// <summary>Название станции в имени файла; по умолчанию — по программе (АКЖТ, Алгабас, Станция по схеме).</summary>
    public static string Station = DefaultStation();
    /// <summary>Папка журналов (для проверок можно поменять).</summary>
    public static string Folder = Path.Combine(AppContext.BaseDirectory, "Логи Arduino");

    static readonly object sync = new();
    static StreamWriter file;
    static string fileDay;
    static string lastRx;
    static int repeat;
    static DateTime repeatSince;

    static string DefaultStation() => (Assembly.GetEntryAssembly()?.GetName().Name ?? "") switch
    {
        "Station" => "АКЖТ",
        "Algabas" => "Алгабас",
        "StationFromJson" => "Станция по схеме",
        var n when n.Length > 0 => n,
        _ => "Станция"
    };

    /// <summary>Строка от платы. Повторы подряд не пишутся, а считаются.</summary>
    public static void Rx(string line)
    {
        if (!Enabled) return;
        lock (sync)
        {
            var now = DateTime.Now;
            if (line == lastRx)
            {
                repeat++;
                if ((now - repeatSince).TotalSeconds >= 60) FlushRepeat(now);
                return;
            }
            FlushRepeat(now);
            lastRx = line;
            repeatSince = now;
            WriteLocked(now, "RX  " + line);
        }
    }

    /// <summary>Что ушло к плате (команда стрелки, изменившийся кадр ламп).</summary>
    public static void Tx(string text) => Event("TX  " + text);

    /// <summary>Событие связи: подключение, отключение, ошибка.</summary>
    public static void Event(string text)
    {
        if (!Enabled) return;
        lock (sync)
        {
            var now = DateTime.Now;
            FlushRepeat(now);
            WriteLocked(now, text);
        }
    }

    /// <summary>Порт закрыт — дописать счётчик повторов и закрыть файл.</summary>
    public static void Close()
    {
        lock (sync)
        {
            FlushRepeat(DateTime.Now);
            lastRx = null;
            try { file?.Dispose(); } catch { }
            file = null;
            fileDay = null;
        }
    }

    static void FlushRepeat(DateTime now)
    {
        if (repeat > 0)
        {
            WriteLocked(now, $"RX  (то же ещё {repeat} раз с {repeatSince:HH:mm:ss})");
            repeat = 0;
        }
        repeatSince = now;
    }

    static void WriteLocked(DateTime now, string text)
    {
        try
        {
            var day = now.ToString("yyyy-MM-dd");
            if (file == null || day != fileDay)
            {
                try { file?.Dispose(); } catch { }
                file = Open(day);
                fileDay = day;
            }
            file?.WriteLine(now.ToString("HH:mm:ss.fff") + "  " + text);
        }
        catch { file = null; }
    }

    static StreamWriter Open(string day)
    {
        foreach (var dir in new[] { Folder, Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationArduinoLogs") })
        {
            try
            {
                Directory.CreateDirectory(dir);
                Cleanup(dir);
                var path = Path.Combine(dir, $"{day} {Station}.txt");
                var w = new StreamWriter(new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true)) { AutoFlush = true };
                return w;
            }
            catch { }
        }
        return null;
    }

    /// <summary>Удалить журналы старше KeepDays (только свои файлы «гггг-мм-дд …txt»).</summary>
    static void Cleanup(string dir)
    {
        try
        {
            var edge = DateTime.Today.AddDays(-KeepDays);
            foreach (var f in Directory.GetFiles(dir, "????-??-?? *.txt"))
                if (DateTime.TryParseExact(Path.GetFileName(f)[..10], "yyyy-MM-dd", null, System.Globalization.DateTimeStyles.None, out var d) && d < edge)
                    try { File.Delete(f); } catch { }
        }
        catch { }
    }
}
