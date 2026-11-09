using System.Collections.Concurrent;
using System.IO.Ports;
using System.Text;

namespace StationApp.Core;

// =====================================================================
//  СВЯЗЬ С ARDUINO — ОДНА НА ВСЮ ПРОГРАММУ
//
//  Окно АКЖТ и окно путевого поста работают через ОДНУ плату Arduino,
//  поэтому порт один и пакет ламп один (в нём лампы обоих окон).
//
//  Протокол — как у прошивки StationMain.ino (та же, что у Алгабаса):
//    ПК → Arduino, светофоры:  'L' + N байт (1 бит = 1 лампа, 0 = ГОРИТ, 1 = не горит)
//    ПК → Arduino, стрелки:    "W <канал> <1|2>\n"  (1 = плюс, 2 = минус), канал 0..8 на PCA9685
//    Arduino → ПК, датчики:    "Data: 1011...\n"   (24 бита с 74HC165, '0' = участок ЗАНЯТ)
//    Arduino → ПК, датчики:    "Occ: 101\n"        (прошивка AKZHT_Main: детекторы, подключённые
//                                                   ПРЯМО к пинам Arduino; 1 символ = 1 пин по порядку
//                                                   списка SENSOR_PINS, '0' = участок ЗАНЯТ;
//                                                   старая прошивка АКЖТ шлёт то же без "Occ: ")
// =====================================================================

/// <summary>Карта "проводов" одной схемы: какой бит = какая лампа, канал = стрелка, бит датчика = участок.</summary>
public class HardwareMap
{
    /// <summary>(светофор, лампа) → (номер байта, номер бита). Лампы без записи горят только на экране.</summary>
    public readonly Dictionary<(string signal, string lamp), (int b, int bit)> Lamps = new();

    /// <summary>Стрелка → каналы сервоприводов (для спаренной может быть два).</summary>
    public readonly Dictionary<string, int[]> Servos = new();

    /// <summary>Участок → номер бита датчика (0 = младший бит строки "Data:").</summary>
    public readonly Dictionary<string, int> Sensors = new();

    /// <summary>Участок → номер символа в строке "Occ:" (0 = первый символ = первый пин в PinSensors.h).
    /// Это детекторы, подключённые прямо к пинам Arduino, без платы 74HC165.</summary>
    public readonly Dictionary<string, int> PinSensors = new();

    /// <summary>Подписи для экрана: номер символа "Occ:" → какой это пин Arduino (например 0 → "пин 8").</summary>
    public readonly Dictionary<int, string> PinNames = new();
}

public static class ArduinoLink
{
    /// <summary>Сколько байт ламп ждёт прошивка. В StationMain.ino сейчас ровно 5 (40 ламп).
    /// Если на плате станет больше регистров 74HC595 — поменять здесь И в прошивке (Lights.h).</summary>
    public static int FrameBytes = 5;

    /// <summary>Сколько бит датчиков присылает прошивка (Sensors.h читает 3 регистра 74HC165 = 24 бита).</summary>
    public static int SensorBits = 24;

    /// <summary>[ДОБАВЛЕНО 09.10.2026] Как нумеровать символы строки "Data:".
    /// false (по умолчанию, старая прошивка) — справа налево: последний символ = бит 0.
    /// true (прошивка Алгабаса от 09.10.2026) — слева направо: первый символ = вход 0,
    /// ровно как «индексы 0..23» в мониторе порта по команде S.</summary>
    public static bool DataLeftToRight;

    public const int BaudRate = 9600;

    static SerialPort port;
    // [ИЗМЕНЕНО 06.10.2026] Запись в порт — в отдельном потоке. Раньше port.Write вызывался прямо
    // из окна: если плата не принимала данные, каждая запись ждала до 0,5 с, а лампы шлются
    // ~3 раза в секунду — окно «висло» насовсем. Теперь окно только кладёт команды в очередь.
    static BlockingCollection<byte[]> txQueue;
    static Thread txThread;
    static DateTime lastTxError;
    static string lastRxLogged;
    static DateTime lastRxAt = DateTime.MinValue;   // когда от платы последний раз что-то пришло
    static int rxRepeat;
    static SynchronizationContext ui;
    static DateTime openedAt;
    static readonly StringBuilder rx = new();
    static readonly Queue<string> pending = new();
    static byte[] lastFrame;
    static DateTime lastSent;

    static readonly List<Func<IEnumerable<(int b, int bit)>>> lampProviders = new();

    /// <summary>Проверка ламп: все биты "горят".</summary>
    public static bool LampTest;

    public static event Action<string> Log;
    public static event Action StatusChanged;
    /// <summary>Пришла строка датчиков. occupied[i] = true — бит i говорит "занято".</summary>
    public static event Action<bool[]> SensorsReceived;
    /// <summary>Пришла строка "Occ:" (детекторы прямо на пинах). occupied[i] = true — i-й пин говорит "занято".</summary>
    public static event Action<bool[]> PinSensorsReceived;

    /// <summary>Последняя строка датчиков как есть (для проверки на экране). null — ещё не приходила.</summary>
    public static string LastDataLine { get; private set; }
    public static string LastOccLine { get; private set; }

    public static bool IsConnected => port != null && port.IsOpen;
    public static string PortName => IsConnected ? port.PortName : null;

    public static void RegisterLamps(Func<IEnumerable<(int b, int bit)>> provider)
    {
        if (!lampProviders.Contains(provider)) lampProviders.Add(provider);
    }

    public static void UnregisterLamps(Func<IEnumerable<(int b, int bit)>> provider) =>
        lampProviders.Remove(provider);

    static void Say(string msg) => Log?.Invoke(msg);

    public static void Connect(string name)
    {
        if (IsConnected && port.PortName == name) return;
        Disconnect();
        if (string.IsNullOrEmpty(name)) { StatusChanged?.Invoke(); return; }

        ui = SynchronizationContext.Current;
        try
        {
            // DtrEnable/RtsEnable = true — как делает Python (pyserial) и монитор порта Arduino IDE.
            // Без DTR некоторые платы (оригинальный Uno с чипом 16U2) не присылают данные и
            // не принимают команды — тогда "Датчики: данных нет" и зависшие записи в порт.
            port = new SerialPort(name, BaudRate)
            {
                NewLine = "\n", WriteTimeout = 1000, ReadTimeout = 500, Encoding = Encoding.ASCII,
                DtrEnable = true, RtsEnable = true, Handshake = Handshake.None
            };
            port.DataReceived += OnData;
            port.Open();
            openedAt = DateTime.Now;
            lastFrame = null;
            lastRxLogged = null;
            rxRepeat = 0;
            lastRxAt = DateTime.MinValue;
            StartWriter(port);
            Say($"Arduino: подключено к {name}. Плата перезагружается ~2 с, команды подождут.");
        }
        catch (Exception ex)
        {
            port = null;
            Say("Arduino: ошибка порта — " + ex.Message);
        }
        StatusChanged?.Invoke();
    }

    public static void Disconnect()
    {
        if (port == null) return;
        var p = port;
        port = null;
        try { txQueue?.CompleteAdding(); } catch { }
        try
        {
            p.DataReceived -= OnData;
            if (p.IsOpen) p.Close();
        }
        catch { }
        try { p.Dispose(); } catch { }
        pending.Clear();
        LastDataLine = null;
        LastOccLine = null;
        Say("Arduino: отключено");
        StatusChanged?.Invoke();
    }

    /// <summary>Перевести стрелку на макете. plus = true → "W ch 1", иначе "W ch 2".</summary>
    public static void SendSwitch(int channel, bool plus)
    {
        string cmd = $"W {channel} {(plus ? 1 : 2)}\n";
        if (!IsConnected) { Say($"(нет связи) {cmd.Trim()}"); return; }
        if ((DateTime.Now - openedAt).TotalSeconds < 2.0) { pending.Enqueue(cmd); return; }
        Write(cmd);
    }

    static void Write(string cmd)
    {
        Enqueue(Encoding.ASCII.GetBytes(cmd));
        Say("TX: " + cmd.Trim());
    }

    static bool Enqueue(byte[] data)
    {
        var q = txQueue;
        if (q == null || q.IsAddingCompleted) return false;
        try { q.Add(data); return true; } catch { return false; }
    }

    /// <summary>Поток записи: берёт команды из очереди и пишет в порт. Окно при этом не ждёт.</summary>
    static void StartWriter(SerialPort p)
    {
        var q = new BlockingCollection<byte[]>();
        txQueue = q;
        txThread = new Thread(() =>
        {
            try
            {
                foreach (var data in q.GetConsumingEnumerable())
                {
                    try { p.Write(data, 0, data.Length); }
                    catch (Exception ex)
                    {
                        if (!p.IsOpen) break;
                        if ((DateTime.Now - lastTxError).TotalSeconds > 3)
                        {
                            lastTxError = DateTime.Now;
                            string hint = lastRxAt == DateTime.MinValue
                                ? " От платы не пришло ни одной строки — скорее всего выбран НЕ ТОТ COM-порт " +
                                  "(например, Bluetooth). Посмотрите подпись порта в списке или Диспетчер устройств → «Порты (COM и LPT)»."
                                : " Проверьте кабель и прошивку.";
                            Post(() => Say("Arduino: плата не принимает данные (" + ex.Message + ")." + hint));
                        }
                    }
                }
            }
            catch { }
        }) { IsBackground = true, Name = "ArduinoWriter" };
        txThread.Start();
    }

    /// <summary>Собрать кадр ламп из всех окон и отправить, если он изменился
    /// (или раз в секунду — на случай, если Arduino перезагрузилась). Вызывать по таймеру.</summary>
    public static void PumpLights()
    {
        if (!IsConnected) return;
        if ((DateTime.Now - openedAt).TotalSeconds < 2.0) return;

        while (pending.Count > 0) Write(pending.Dequeue());

        var frame = BuildFrame();
        bool same = lastFrame != null && frame.AsSpan().SequenceEqual(lastFrame);
        if (same && (DateTime.Now - lastSent).TotalSeconds < 1.0) return;

        // если плата не успевает принимать — не копим старые кадры ламп (команды стрелок не выбрасываем)
        if (txQueue != null && txQueue.Count > 10) return;

        var packet = new byte[frame.Length + 1];
        packet[0] = (byte)'L';
        Array.Copy(frame, 0, packet, 1, frame.Length);
        if (Enqueue(packet))
        {
            lastFrame = frame;
            lastSent = DateTime.Now;
        }
    }

    /// <summary>Кадр ламп: 0xFF = всё погашено; бит в 0 = лампа горит (Active-Low, как в Python).</summary>
    public static byte[] BuildFrame()
    {
        var frame = new byte[FrameBytes];
        for (int i = 0; i < frame.Length; i++) frame[i] = LampTest ? (byte)0x00 : (byte)0xFF;
        if (LampTest) return frame;

        foreach (var p in lampProviders)
            foreach (var (b, bit) in p())
                if (b >= 0 && b < frame.Length && bit >= 0 && bit < 8)
                    frame[b] &= (byte)~(1 << bit);
        return frame;
    }

    static void OnData(object sender, SerialDataReceivedEventArgs e)
    {
        string chunk;
        try { chunk = port?.ReadExisting() ?? ""; } catch { return; }
        if (chunk.Length > 0) lastRxAt = DateTime.Now;
        lock (rx)
        {
            rx.Append(chunk);
            while (true)
            {
                var text = rx.ToString();
                int nl = text.IndexOf('\n');
                if (nl < 0) break;
                var line = text.Substring(0, nl).Trim();
                rx.Remove(0, nl + 1);
                HandleLine(line);
            }
        }
    }

    static void HandleLine(string line)
    {
        if (line.Length == 0) return;
        if (line.StartsWith("Occ:"))
        {
            var occ = ParseOcc(line);
            if (occ == null) return;
            Post(() => { LastOccLine = line; PinSensorsReceived?.Invoke(occ); });
            return;
        }
        // Старая прошивка АКЖТ (и резервная копия .hex) шлёт занятость просто строкой "101"
        // без приставки: 1-й символ — пин 10, 2-й — пин 8, 3-й — пин 9. Понимаем её как "Occ:".
        if (line.Length <= 8 && line.All(c => c == '0' || c == '1'))
        {
            var occOld = ParseOcc("Occ: " + line);
            Post(() => { LastOccLine = line; PinSensorsReceived?.Invoke(occOld); });
            return;
        }
        if (!line.StartsWith("Data:"))
        {
            Post(() => LogRx(line));
            return;
        }
        // [ИЗМЕНЕНО 09.10.2026] Раньше строки "Data:" в журнал не попадали вовсе (видно было только
        // изменение участков из карты), а неправильная строка молча выбрасывалась — при «тихих»
        // датчиках журнал был пустой и непонятно, шлёт ли плата хоть что-то.
        // Теперь в журнал идёт каждая НОВАЯ строка "Data:" и каждая строка, которую не удалось разобрать.
        var bits = ParseBits(line, SensorBits);
        if (bits == null) { Post(() => LogRx(line + "   ← не разобрана: после \"Data:\" должны быть только 0 и 1")); return; }
        Post(() =>
        {
            if (line != LastDataLine) Say("RX: " + line);
            LastDataLine = line;
            SensorsReceived?.Invoke(bits);
        });
    }

    /// <summary>"Data: 1011" → массив занятости. Arduino печатает число в двоичном виде без ведущих нулей,
    /// поэтому дополняем нулями слева до SensorBits и разворачиваем (как zfill + [::-1] в Python):
    /// индекс 0 = младший бит. '0' = занято. При DataLeftToRight — индекс 0 = первый символ слева.</summary>
    public static bool[] ParseBits(string line, int count)
    {
        // [ИЗМЕНЕНО 27.10.2026] прошивка Алгабаса от 22.10.2026 разделяет байты пробелами
        // ("Data: 11111111 11111111 11111111") — пробелы между байтами пропускаем
        var raw = string.Concat(line.Replace("Data:", "").Where(c => !char.IsWhiteSpace(c)));
        if (raw.Length == 0 || raw.Any(c => c != '0' && c != '1')) return null;
        if (DataLeftToRight)
        {
            // первый символ = вход 0; недостающие справа символы считаем свободными
            if (raw.Length > count) raw = raw.Substring(0, count);
            raw = raw.PadRight(count, '1');
            return raw.Select(c => c == '0').ToArray();
        }
        if (raw.Length > count) raw = raw.Substring(raw.Length - count);
        raw = raw.PadLeft(count, '0');
        var res = new bool[count];
        for (int i = 0; i < count; i++)
            res[i] = raw[count - 1 - i] == '0';
        return res;
    }

    /// <summary>"Occ: 10" → массив занятости по порядку символов (слева направо = пины по порядку
    /// в PinSensors.h). '0' = занято, '1' = свободно. Неправильная строка → null.</summary>
    public static bool[] ParseOcc(string line)
    {
        var raw = line.Replace("Occ:", "").Trim();
        if (raw.Length == 0 || raw.Any(c => c != '0' && c != '1')) return null;
        return raw.Select(c => c == '0').ToArray();
    }

    /// <summary>Строку от Arduino — в журнал, но одинаковые подряд не повторяем
    /// (иначе плата, которая шлёт одно и то же 10 раз в секунду, забьёт журнал и затормозит окно).</summary>
    static void LogRx(string line)
    {
        if (line == lastRxLogged) { rxRepeat++; return; }
        if (rxRepeat > 0) Say($"RX: (предыдущая строка повторилась ещё {rxRepeat} раз)");
        lastRxLogged = line;
        rxRepeat = 0;
        Say("RX: " + line);
    }

    // =================================================================
    //  ПОДПИСИ COM-ПОРТОВ — чтобы не перепутать Arduino с Bluetooth и т.п.
    //  Windows хранит, какой драйвер создал какой COM-порт, в реестре: HARDWARE\DEVICEMAP\SERIALCOMM.
    // =================================================================

    /// <summary>Список портов для выпадающего списка: "COM5 — USB (Arduino)".</summary>
    public static List<string> PortItems()
    {
        var map = new Dictionary<string, string>();
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"HARDWARE\DEVICEMAP\SERIALCOMM");
                if (k != null)
                    foreach (var n in k.GetValueNames())
                        if (k.GetValue(n) is string com) map[com] = DescribeDriver(n);
            }
        }
        catch { }
        return SerialPort.GetPortNames().Distinct().OrderBy(x => x.Length).ThenBy(x => x)
            .Select(p => map.TryGetValue(p, out var d) && d.Length > 0 ? $"{p} — {d}" : p).ToList();
    }

    /// <summary>Из строки списка ("COM5 — USB (Arduino)") достать имя порта ("COM5").</summary>
    public static string PortFromItem(string item)
    {
        if (string.IsNullOrEmpty(item)) return null;
        int sp = item.IndexOf(' ');
        return sp < 0 ? item : item.Substring(0, sp);
    }

    static string DescribeDriver(string device)
    {
        var d = device.ToUpperInvariant();
        if (d.Contains("BTH")) return "Bluetooth (это НЕ Arduino)";
        if (d.Contains("USBSER")) return "USB (Arduino)";
        if (d.Contains("CH34") || d.Contains("WCH")) return "USB CH340 (Arduino)";
        if (d.Contains("SILAB")) return "USB CP210x";
        if (d.Contains("VCP") || d.Contains("FTDI")) return "USB FTDI";
        if (d.StartsWith("\\DEVICE\\SERIAL")) return "встроенный порт (не Arduino)";
        return device.Replace("\\Device\\", "");
    }

    static void Post(Action a)
    {
        if (ui != null) ui.Post(_ => a(), null);
        else a();
    }
}
