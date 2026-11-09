using System.Text;
using StationApp.Core;

namespace StationApp;

// =====================================================================
//  КАРТА "ПРОВОДОВ" СТАНЦИИ АКЖТ И ПУТЕВОГО ПОСТА (одна плата Arduino на обе схемы)
//
//  [ИЗМЕНЕНО 28.10.2026] Лампы светофоров — по таблице «Распределение светофоров по битам регистров
//  (байтов), ВСЕ СТАНЦИИ, АКЖТ», вариант «АКЖТ + промежуточная»: 13 регистров 74HC595 = 13 байт =
//  104 лампы. 0 в бите = лампа ГОРИТ (Active-Low). Байт 0 — первый регистр в цепочке.
//  Карта лежит в файле светофоры.txt рядом с Station.exe (создаётся сам из таблицы ниже) — её можно
//  поправить без пересборки программы: поменять цифры, сохранить, перезапустить программу.
//  Стрелки и датчики — ниже в коде, как раньше.
//  [ИЗМЕНЕНО 28.10.2026] Прошивка платы — StationMain_AKZHT (Arduino\StationMain_AKZHT, как у Алгабаса):
//  'L' + 13 байт, стрелки "W канал 1|2", датчики "Occ: 101". Каналы стрелок — по документу
//  «Серво станции АКЖТ» (пакет светофоров от 27.10.2026), калибровка серво — в Switches.h скетча.
// =====================================================================

public static class AkzhtHardware
{
    public const string LampsFileName = "светофоры.txt";
    public static string LampsFile => Path.Combine(AppContext.BaseDirectory, LampsFileName);

    /// <summary>Сколько байт ламп по таблице (регистров 74HC595 на плате АКЖТ).</summary>
    public const int TableBytes = 13;

    /// <summary>[ДОБАВЛЕНО 28.10.2026] Версия карты в светофоры.txt («версия = 2»). Файл старой версии
    /// сохраняется как светофоры.старый.txt и создаётся заново (в версии 2 добавлен 2-ОП).</summary>
    public const int MapVersion = 2;

    /// <summary>Одна строка таблицы: схема (АКЖТ / ПОСТ), светофор (id в программе без «А:»), лампа, байт, бит, пояснение.</summary>
    public record LampRow(string Scheme, string Signal, string Lamp, int Byte, int Bit, string Note = "");

    /// <summary>Таблица «АКЖТ + промежуточная» (лист 1 Excel, строки 15–21).</summary>
    public static readonly LampRow[] Table = BuildTable();

    static LampRow[] BuildTable()
    {
        var t = new List<LampRow>();
        int pos = 0;
        void S(string scheme, string sig, string note, params string[] lamps)
        {
            foreach (var l in lamps) { t.Add(new(scheme, sig, l, pos / 8, pos % 8, note)); pos++; }
        }
        const string A = "АКЖТ", P = "ПОСТ";
        S(A, "М6", "маневровый", "white", "blue");
        S(A, "М10", "маневровый", "red", "white");
        S(A, "М8", "маневровый", "white", "blue");
        S(A, "Н5", "выходной", "yellow", "green", "red", "white");
        S(A, "Н3", "выходной", "yellow", "green", "red", "white");
        S(A, "Н1", "выходной", "yellow", "green", "red", "white");
        S(A, "Н2", "выходной", "yellow", "green", "red", "white");
        S(A, "Н4", "выходной", "yellow", "green", "red", "white");
        S(A, "Ч5", "выходной", "yellow", "green", "red", "white");
        S(A, "Ч3", "выходной", "yellow", "green", "red", "white");
        S(A, "Ч1", "выходной", "yellow", "green", "red", "white");
        S(A, "Ч2", "выходной", "yellow", "green", "red", "white");
        S(A, "Ч4", "выходной", "yellow", "green", "red", "white");
        S(A, "М7", "маневровый", "white", "blue");
        S(A, "М5", "маневровый", "white", "blue");
        // [ИЗМЕНЕНО 28.10.2026] М3 и М1 поменяны местами по проверке на макете (правка в светофоры.txt на флешке):
        // в таблице М1 = 6.2–6.3, М3 = 6.4–6.5, на макете наоборот
        S(A, "М3", "маневровый", "white", "blue");
        S(A, "М1", "маневровый", "white", "blue");
        S(A, "Н", "входной Н (жёлтый — верхний, жёлтый2 — нижний)", "yellow", "green", "red", "yellow1", "white");
        S(P, "1-I", "проходной 1 пути I", "red", "yellow", "green");
        S(P, "6-II", "проходной 6 пути II", "red", "yellow", "green");
        S(P, "3-I", "проходной 3 пути I", "red", "yellow", "green");
        S(P, "4-II", "проходной 4 пути II", "red", "yellow", "green");
        S(A, "М9", "маневровый депо (в таблице «под вопросом»; работает, только если депо показано)", "red", "white");
        S(P, "5-I", "проходной 5 пути I", "red", "yellow", "green");
        S(P, "2-II", "проходной 2 пути II", "red", "yellow", "green");
        S(P, "Ч", "входной Ч поста (жёлтый — верхний, жёлтый2 — нижний)", "yellow", "green", "red", "yellow1", "white");
        S(P, "Н", "светофор Н поста", "yellow", "green", "red", "white");
        S(P, "1-ОП", "проходной 1 однопутного перегона", "red", "yellow", "green");
        // [ИЗМЕНЕНО 28.10.2026] «before_002 светофор (без названия)» (11.3–11.5) — проходной 2 однопутного перегона
        S(P, "2-ОП", "проходной 2 однопутного перегона (в таблице «before_002 светофор (без названия)»)", "red", "yellow", "green");
        S(P, "НА", "входной НА (жёлтый — верхний, жёлтый2 — нижний)", "yellow", "yellow1", "red", "white");
        // «1_снизу»: 12.2–12.4 — в программе такого светофора нет
        return t.ToArray();
    }

    public static HardwareMap Build(bool depot)
    {
        var m = new HardwareMap();

        // ---------- стрелки: пара → каналы PCA9685 ----------
        // [ИЗМЕНЕНО 28.10.2026] по документу «Серво станции АКЖТ» (пакет 27.10.2026, скетч StationMain_AKZHT):
        //   канал 3 → стрелка 1, 4 → 7, 5 → 5, 6 → 10 (в документе «11»), 7 → 19, 8 → 13, 10 → 15, 11 → 17, 13 → 12.
        //   Стрелка 3 по документу стоит у поста (канал 0) — см. Post/PostHardware.cs. Было (до 28.10): 5/7 → 4,5; 1/3 → 6,3; 13/15 → 8,15.
        m.Servos["А:5/7"] = new[] { 5, 4 };
        m.Servos["А:1/3"] = new[] { 3 };
        m.Servos["А:19"] = new[] { 7 };
        m.Servos["А:13/15"] = new[] { 8, 10 };
        m.Servos["А:17"] = new[] { 11 };
        m.Servos["А:10/12"] = new[] { 6, 13 };
        // без сервоприводов на макете (переводятся только на экране):
        m.Servos["А:2/4"] = Array.Empty<int>();
        m.Servos["А:6/8"] = Array.Empty<int>();
        m.Servos["А:14"] = Array.Empty<int>();
        m.Servos["А:16"] = Array.Empty<int>();

        // ---------- датчики занятости ----------
        // [ИЗМЕНЕНО 06.10.2026] Сейчас на АКЖТ работают 2 детектора, их выходы "Занятость"
        // подключены ПРЯМО к пинам Arduino (плата 74HC165 пока не подключена):
        //     пин 10 → участок Н-М1 (между входным Н и М1)
        //     пин 8  → участок УII  (от М3 до проходного 6)
        //     пин 9  → резерв (3-й символ)
        // Прошивка StationMain_AKZHT присылает "Occ: 101": 1-й символ — пин 10, 2-й — пин 8, 3-й — пин 9.
        m.PinSensors["А:Н-М1"] = 0;   m.PinNames[0] = "пин 10";
        m.PinSensors["А:УII"] = 1;    m.PinNames[1] = "пин 8";
        // Когда плату 74HC165 подключат, записать сюда: m.Sensors["А:<участок>"] = <номер бита>;

        return m;
    }

    // =================================================================
    //  ФАЙЛ светофоры.txt
    // =================================================================

    static readonly (string code, string ru)[] LampNames =
    {
        ("yellow", "жёлтый"), ("yellow1", "жёлтый2"), ("green", "зелёный"), ("red", "красный"), ("white", "белый"), ("blue", "синий"),
    };

    static string Ru(string code) => LampNames.First(x => x.code == code).ru;

    static string Code(string ru)
    {
        ru = ru.Trim().ToLowerInvariant().Replace('ё', 'е');
        foreach (var (code, name) in LampNames)
            if (name.Replace('ё', 'е') == ru || code == ru) return code;
        return null;
    }

    /// <summary>Прочитать светофоры.txt в m.Lamps (ключ — id светофора в модели). Файла нет — создать его по таблице.
    /// Возвращает сообщения для журнала. В FrameBytes — сколько байт слать плате.</summary>
    public static List<string> LoadLamps(HardwareMap m, Interlocking il, out int frameBytes)
    {
        var msgs = new List<string>();
        frameBytes = TableBytes;
        string[] lines;
        try
        {
            // [ДОБАВЛЕНО 28.10.2026] файл старой версии карты — в светофоры.старый.txt, новый — по таблице
            if (File.Exists(LampsFile) && FileVersion(File.ReadAllLines(LampsFile)) < MapVersion)
            {
                string old = Path.Combine(AppContext.BaseDirectory, "светофоры.старый.txt");
                File.Copy(LampsFile, old, true);
                File.Delete(LampsFile);
                msgs.Add($"Карта ламп обновлена до версии {MapVersion} (добавлен проходной 2-ОП). Прежний файл сохранён: {old}");
            }
            if (!File.Exists(LampsFile))
            {
                File.WriteAllText(LampsFile, Template(), new UTF8Encoding(true));
                msgs.Add($"Создан файл карты ламп {LampsFile} (по таблице «АКЖТ + промежуточная», 13 байт).");
            }
            lines = File.ReadAllLines(LampsFile);
        }
        catch (Exception ex)
        {
            msgs.Add($"Файл {LampsFileName} не прочитан ({ex.Message}) — лампы по встроенной таблице.");
            lines = Template().Split('\n');
        }

        var used = new Dictionary<(int, int), string>();
        int n = 0;
        foreach (var raw in lines)
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0) continue;
            int eq = line.IndexOf('=');
            if (eq < 0) { msgs.Add($"{LampsFileName}: непонятная строка «{raw.Trim()}»"); continue; }
            var left = line[..eq].Trim();
            var right = line[(eq + 1)..].Trim();
            if (left.Equals("версия", StringComparison.OrdinalIgnoreCase)) continue;
            if (left.Equals("байт", StringComparison.OrdinalIgnoreCase))
            {
                if (int.TryParse(right, out var fb) && fb is > 0 and <= 32) frameBytes = fb;
                else msgs.Add($"{LampsFileName}: «байт = {right}» — нужно число 1…32");
                continue;
            }
            var parts = left.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != 3) { msgs.Add($"{LampsFileName}: нужно «схема светофор лампа = байт.бит» — «{raw.Trim()}»"); continue; }
            string scheme = parts[0].ToUpperInvariant(), sig = parts[1], lamp = Code(parts[2]);
            string id = scheme == "АКЖТ" ? "А:" + sig : scheme == "ПОСТ" ? sig : null;
            if (id == null) { msgs.Add($"{LampsFileName}: схема «{parts[0]}» — должно быть АКЖТ или ПОСТ"); continue; }
            if (lamp == null) { msgs.Add($"{LampsFileName}: лампа «{parts[2]}» — жёлтый, жёлтый2, зелёный, красный, белый, синий"); continue; }
            if (right.Length == 0 || right == "-") continue;   // лампы на макете нет
            var bb = right.Split('.', ',');
            if (bb.Length != 2 || !int.TryParse(bb[0], out var b) || !int.TryParse(bb[1], out var bit) || b < 0 || bit is < 0 or > 7)
            { msgs.Add($"{LampsFileName}: «{right}» — нужно байт.бит, например 6.6"); continue; }
            if (!il.Signals.TryGetValue(id, out var s))
            {
                if (!(id == "А:М9")) msgs.Add($"{LampsFileName}: светофора {parts[0]} {sig} в программе нет — строка пропущена");
                continue;
            }
            if (!s.Has(lamp)) { msgs.Add($"{LampsFileName}: у светофора {parts[0]} {sig} нет лампы «{parts[2]}» — строка пропущена"); continue; }
            if (used.TryGetValue((b, bit), out var other))
                msgs.Add($"{LampsFileName}: бит {b}.{bit} уже занят ({other}) — теперь и {parts[0]} {sig} {parts[2]}");
            used[(b, bit)] = $"{parts[0]} {sig} {parts[2]}";
            m.Lamps[(id, lamp)] = (b, bit);
            n++;
        }
        int fbs = frameBytes;
        if (m.Lamps.Any(kv => kv.Value.b >= fbs))
            msgs.Add($"{LampsFileName}: есть лампы в байтах ≥ {fbs} — они не отправляются (увеличьте «байт = …»)");
        msgs.Add($"Карта ламп: {n} ламп, {frameBytes} байт ({LampsFileName}).");
        return msgs;
    }

    /// <summary>Содержимое светофоры.txt по встроенной таблице.</summary>
    /// <summary>Номер из строки «версия = N» (нет строки — 1).</summary>
    static int FileVersion(string[] lines)
    {
        foreach (var raw in lines)
        {
            var line = raw.Split('#')[0].Trim();
            int eq = line.IndexOf('=');
            if (eq > 0 && line[..eq].Trim().Equals("версия", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line[(eq + 1)..].Trim(), out var v)) return v;
        }
        return 1;
    }

    public static string Template()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# КАРТА ЛАМП СВЕТОФОРОВ: станция АКЖТ и путевой пост (одна плата Arduino, регистры 74HC595)");
        sb.AppendLine("# По таблице «Распределение светофоров по битам регистров», вариант «АКЖТ + промежуточная».");
        sb.AppendLine("#");
        sb.AppendLine("# Строка:  <схема> <светофор> <лампа> = <байт>.<бит>     байт и бит считаются с нуля, как в таблице.");
        sb.AppendLine("#   схема — АКЖТ или ПОСТ (путевой пост);");
        sb.AppendLine("#   лампа — жёлтый, жёлтый2 (нижний жёлтый), зелёный, красный, белый, синий;");
        sb.AppendLine("#   0 в бите = лампа горит. «= -» или пусто — лампы на макете нет (горит только на экране).");
        sb.AppendLine("#   Светофоры поста: проходные 1-I, 3-I, 5-I (путь I), 6-II, 4-II, 2-II (путь II), 1-ОП и 2-ОП (однопутный перегон),");
        sb.AppendLine("#   входные Ч, НА, светофор Н.");
        sb.AppendLine("# Поменяли цифры — сохраните файл и перезапустите Station.exe. Удалите файл — он создастся заново по таблице.");
        sb.AppendLine("# Проверить проводку: режим «Инженерный» → кнопка «Лампы по одной».");
        sb.AppendLine();
        sb.AppendLine("# версия карты (программа сама обновляет файл старой версии, прежний — светофоры.старый.txt)");
        sb.AppendLine($"версия = {MapVersion}");
        sb.AppendLine("# сколько байт (регистров) слать плате; в скетче StationMain_AKZHT — LIGHT_FRAME_BYTES (Lights.h), должно совпадать");
        sb.AppendLine($"байт = {TableBytes}");
        string last = "";
        foreach (var r in Table)
        {
            string head = $"{r.Scheme} {r.Signal}";
            if (head != last) { sb.AppendLine(); sb.AppendLine($"# {head} — {r.Note}"); last = head; }
            sb.AppendLine($"{head} {Ru(r.Lamp)} = {r.Byte}.{r.Bit}");
        }
        sb.AppendLine();
        sb.AppendLine("# В таблице есть ещё светофор, которого в программе нет (его лампы не отправляются):");
        sb.AppendLine("#   «1_снизу»: красный 12.2, жёлтый 12.3, зелёный 12.4");
        sb.AppendLine("# Светофоры АКЖТ без строк (входной Ч, М2, М4) на макете отсутствуют.");
        return sb.ToString();
    }
}
