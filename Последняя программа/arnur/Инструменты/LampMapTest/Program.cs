using StationApp;
using StationApp.Core;
using StationApp.Model;

// =====================================================================
//  [ДОБАВЛЕНО 28.10.2026] Тест карты ламп АКЖТ + поста: файл светофоры.txt создаётся по таблице распиновки,
//  все лампы таблицы привязаны к светофорам модели, кадр 13 байт, красные огни горят в нужных битах.
//  Запуск: dotnet run --project tools\LampMapTest   (печатает карту в tools\LampMapTest\bin\...\светофоры.txt)
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

if (File.Exists(AkzhtHardware.LampsFile)) File.Delete(AkzhtHardware.LampsFile);
ModelHost.Start(false);
var hw = ModelHost.Model.Hw;
foreach (var m in ModelHost.Model.HwMessages) Console.WriteLine("        " + m);
Check(File.Exists(AkzhtHardware.LampsFile), "файл светофоры.txt создан");
int expected = AkzhtHardware.Table.Count(r => r.Signal != "М9");
Check(hw.Lamps.Count == expected, $"привязано ламп: {hw.Lamps.Count} из {expected} (без М9 — депо скрыто)");
Check(ArduinoLink.FrameBytes == 13, "кадр — 13 байт");
var probes = new (string sig, string lamp, int b, int bit)[]
{
    ("А:М6", "white", 0, 0), ("А:Н5", "yellow", 0, 6), ("А:Н5", "red", 1, 0), ("А:Н4", "white", 3, 1), ("А:Ч4", "white", 5, 5),
    ("А:М3", "blue", 6, 3), ("А:М1", "white", 6, 4), ("А:Н", "yellow", 6, 6), ("А:Н", "yellow1", 7, 1), ("А:Н", "white", 7, 2), ("1-I", "red", 7, 3),
    ("6-II", "green", 8, 0), ("5-I", "red", 9, 1), ("2-II", "green", 9, 6), ("Ч", "yellow", 9, 7), ("Ч", "white", 10, 3),
    ("Н", "white", 10, 7), ("1-ОП", "red", 11, 0), ("НА", "yellow", 11, 6), ("НА", "yellow1", 11, 7), ("НА", "white", 12, 1),
    ("5-I", "yellow", 9, 2), ("2-II", "red", 9, 4), ("2-ОП", "red", 11, 3), ("2-ОП", "green", 11, 5),   // [ДОБАВЛЕНО 28.10.2026]
};
foreach (var (sig, lamp, b, bit) in probes)
    Check(hw.Lamps.TryGetValue((sig, lamp), out var p) && p == (b, bit), $"{sig} {lamp} → {b}.{bit}");

// кадр при закрытых светофорах: красные лампы горят (бит 0), остальные погашены (1)
var il = ModelHost.Model.IL;
il.ComputeAspects();
var frame = ArduinoLink.BuildFrame();
Check(frame.Length == 13, "длина кадра 13");
bool redOn(string sig) => hw.Lamps.TryGetValue((sig, "red"), out var p) && (frame[p.b] & (1 << p.bit)) == 0;
Check(redOn("А:Н5") && redOn("НА"), "закрытые Н5 и НА: красные лампы горят");
Check(hw.Lamps.TryGetValue(("А:Н5", "green"), out var g) && (frame[g.b] & (1 << g.bit)) != 0, "зелёный Н5 погашен");
ArduinoLink.OnlyLamp = (7, 3);
var one = ArduinoLink.BuildFrame();
Check(one.Count(x => x != 0xFF) == 1 && one[7] == unchecked((byte)~(1 << 3)), "«лампы по одной»: горит только бит 7.3");
ArduinoLink.OnlyLamp = null;

// [ДОБАВЛЕНО 28.10.2026] каналы стрелок — скетч StationMain_AKZHT (документ «Серво станции АКЖТ»)
bool Servo(string sw, params int[] ch) => hw.Servos.TryGetValue(sw, out var c) && c.SequenceEqual(ch);
Check(Servo("А:5/7", 5, 4) && Servo("А:1/3", 3) && Servo("А:19", 7) && Servo("А:13/15", 8, 10) && Servo("А:17", 11) && Servo("А:10/12", 6, 13),
      "каналы стрелок АКЖТ: 5/7 → 5,4; 1/3 → 3; 19 → 7; 13/15 → 8,10; 17 → 11; 10/12 → 6,13");
Check(Servo("1/2", 14, 1) && Servo("3/4", 0, 2), "каналы стрелок поста: 1/2 → 14,1; 3/4 → 0,2");

// [ДОБАВЛЕНО 28.10.2026] строки скетча StationMain_AKZHT разбираются (без платы — прямой вызов разбора строки)
var handle = typeof(ArduinoLink).GetMethod("HandleLine", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;
bool[] gotOcc = null;
var said = new List<string>();
Action<bool[]> onOcc = o => gotOcc = o;
Action<string> onLog = said.Add;
ArduinoLink.PinSensorsReceived += onOcc;
ArduinoLink.Log += onLog;
foreach (var line in new[] { "SYSTEM: AKZHT KERNEL (как Алгабас, 13 байт ламп)", "SERVO_SHIELD_FAIL SDA=A4 SCL=A5 addr=0x40", "GOT W 5 1", "ACK_W 5 1", "Occ: 011" })
    handle.Invoke(null, new object[] { line });
ArduinoLink.PinSensorsReceived -= onOcc;
ArduinoLink.Log -= onLog;
Check(gotOcc != null && gotOcc.Length == 3 && gotOcc[0] && !gotOcc[1] && !gotOcc[2], "«Occ: 011»: пин 10 занят, пины 8 и 9 свободны");
Check(said.Any(x => x.Contains("сервошилд PCA9685 не отвечает")), "«SERVO_SHIELD_FAIL» — понятное сообщение в журнале");

// [ДОБАВЛЕНО 28.10.2026] светофоры.txt старой версии (без 2-ОП) обновляется сам, прежний — светофоры.старый.txt
var oldLines = File.ReadAllLines(AkzhtHardware.LampsFile).Where(l => !l.StartsWith("версия") && !l.Contains("2-ОП"));
File.WriteAllLines(AkzhtHardware.LampsFile, oldLines);
var m2 = new HardwareMap();
var msgs2 = AkzhtHardware.LoadLamps(m2, il, out _);
var oldFile = Path.Combine(AppContext.BaseDirectory, "светофоры.старый.txt");
Check(File.Exists(oldFile) && m2.Lamps.ContainsKey(("2-ОП", "red")) && msgs2.Any(x => x.Contains("обновлена")),
      "светофоры.txt старой версии обновлён (2-ОП есть), прежний сохранён в светофоры.старый.txt");
ModelHost.Stop();
Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
