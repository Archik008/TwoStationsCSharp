using StationApp.Core;

// =====================================================================
//  [ДОБАВЛЕНО 23.11.2026] Тест журнала Arduino (Core/ArduinoLog.cs) — без платы, во временной папке.
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

var dir = Path.Combine(Path.GetTempPath(), "ArduinoLogTest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(dir);
ArduinoLog.Folder = dir;
ArduinoLog.Station = "Тест";
var today = Path.Combine(dir, DateTime.Today.ToString("yyyy-MM-dd") + " Тест.txt");

// старый журнал (40 дней назад) и чужой файл — первый удаляется при открытии, второй остаётся
var old = Path.Combine(dir, DateTime.Today.AddDays(-40).ToString("yyyy-MM-dd") + " Тест.txt");
var fresh = Path.Combine(dir, DateTime.Today.AddDays(-3).ToString("yyyy-MM-dd") + " Тест.txt");
var other = Path.Combine(dir, "заметки.txt");
File.WriteAllText(old, "старый"); File.WriteAllText(fresh, "свежий"); File.WriteAllText(other, "чужой");

ArduinoLog.Event("ПОДКЛЮЧЕНО COM5, 9600 бод");
ArduinoLog.Rx("SYSTEM: AKZHT KERNEL (ядро станции)");
for (int i = 0; i < 50; i++) ArduinoLog.Rx("Data: 101100");
ArduinoLog.Rx("Data: 101101");
ArduinoLog.Tx("W 3 1");
ArduinoLog.Rx("Data: 101101");
ArduinoLog.Event("ОТКЛЮЧЕНО");
ArduinoLog.Close();

var lines = File.ReadAllLines(today);
foreach (var l in lines) Console.WriteLine("        | " + l);
Check(File.Exists(today), "файл дня: " + Path.GetFileName(today));
Check(!File.Exists(old) && File.Exists(fresh) && File.Exists(other), "старше 30 дней удалён, свежий и чужой файлы остались");
Check(lines.Count(l => l.EndsWith("RX  Data: 101100")) == 1, "одинаковая строка записана один раз");
Check(lines.Any(l => l.Contains("(то же ещё 49 раз")), "повторы посчитаны: «то же ещё 49 раз»");
Check(lines.Any(l => l.Contains("ядро станции")), "кириллица от платы читается");
Check(lines.Any(l => l.EndsWith("TX  W 3 1")) && lines.Any(l => l.EndsWith("ПОДКЛЮЧЕНО COM5, 9600 бод")) && lines.Last().EndsWith("ОТКЛЮЧЕНО"),
      "команда стрелки, подключение и отключение записаны");
Check(lines.Count(l => l.EndsWith("RX  Data: 101101")) == 1 && lines.Any(l => l.Contains("(то же ещё 1 раз")), "повтор через другую запись (TX) тоже посчитан");
Check(lines.All(l => System.Text.RegularExpressions.Regex.IsMatch(l, @"^\d\d:\d\d:\d\d\.\d{3}  ")), "у каждой строки время чч:мм:сс.ммм");

// дописывание: второй сеанс в тот же день — в тот же файл, без второго BOM посреди файла
ArduinoLog.Event("ПОДКЛЮЧЕНО снова");
ArduinoLog.Close();
var bytes = File.ReadAllBytes(today);
int boms = 0;
for (int i = 0; i + 2 < bytes.Length; i++) if (bytes[i] == 0xEF && bytes[i + 1] == 0xBB && bytes[i + 2] == 0xBF) boms++;
Check(File.ReadAllLines(today).Length == lines.Length + 1 && boms == 1, "второй сеанс дописан в тот же файл (BOM один)");

// запись из потока порта и окна одновременно
var tasks = Enumerable.Range(0, 4).Select(t => Task.Run(() => { for (int i = 0; i < 200; i++) ArduinoLog.Rx($"Data: поток {t} строка {i}"); })).ToArray();
Task.WaitAll(tasks);
ArduinoLog.Close();
Check(File.ReadAllLines(today).Count(l => l.Contains("поток")) == 800, "800 строк из 4 потоков — все на месте");

// выключено — ничего не пишется
ArduinoLog.Enabled = false;
var before = new FileInfo(today).Length;
ArduinoLog.Rx("Data: 000000");
ArduinoLog.Close();
Check(new FileInfo(today).Length == before, "Enabled = false — файл не меняется");

try { Directory.Delete(dir, true); } catch { }
Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
