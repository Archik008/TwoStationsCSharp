using System.Text.RegularExpressions;
using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

// =====================================================================
//  [ДОБАВЛЕНО 22.10.2026, этап 8] Тест перевода интерфейса (ядро и окна АКЖТ).
//  1) примеры строк журнала и окон на казахском и английском;
//  2) каждый ключ словаря с подставленными значениями переводится, без «{0}» в результате;
//  3) отчёт HTML; 4) снимки главного окна АКЖТ и окна ДУ-46 на kk / en в папку из аргумента.
//  Запуск: dotnet run --project tools\LangTest [папка для снимков]
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }
Console.OutputEncoding = System.Text.Encoding.UTF8;

string[] samples =
{
    "Поезд №2002 ушёл со схемы",
    "08:04:05  [Занятие] Поезд принят (Ч → путь 4). Принято: 1",
    "КРИТИЧЕСКОЕ СОБЫТИЕ: Задержка поезда у закрытого входного светофора Ч (участок ЧУ) более 3 мин",
    "Поезд №2002 стоит у светофора Н1 (красный)",
    "Поезд №6001 (пригородный) подходит к станции (из тоннеля I к посту) — участок Тоннель I",
    "Опоздание отправления поезда №6001 со станции АКЖТ на 8 мин: по расписанию 08:44, отправлен в 08:52",
    "Студент: Иванов Иван (ДС-41)",
    "Задан маршрут Ч → путь 3",
    "Стрелка 5/7 получила контроль в плюсе",
    "Учебное №1 — основы (08:00–09:00)   ·   время 08:15:10",
    "Связь: algabas на связи",
};
var hasCyrRu = new Regex("[ыэъЫЭЪёЁ]");   // буквы, которых нет в казахском — признак непереведённого русского
foreach (var lang in new[] { "kk", "en" })
{
    L.Set(lang);
    Console.WriteLine($"--- {lang} ---");
    foreach (var s in samples) Console.WriteLine($"  {L.T(s)}");
    Check(L.T("Поезд №2002 ушёл со схемы") != "Поезд №2002 ушёл со схемы", $"{lang}: шаблон «Поезд №{{0}} ушёл со схемы»");
    Check(!L.T(samples[1]).Contains("Принято"), $"{lang}: строка журнала с временем и [Занятие]");
}

// каждый ключ словаря (дырки → значения) переводится без следов «{n}»
var rows = File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\i18n\strings.tsv"))
               .Where(l => l.Length > 0 && !l.StartsWith("#")).Select(l => l.Split('\t').Select(x => x.Replace("\\n", "\n").Replace("\\\\", "\\")).ToArray()).ToList();
foreach (var (lang, col) in new[] { ("kk", 1), ("en", 2) })
{
    L.Set(lang);
    int bad = 0, same = 0;
    foreach (var r in rows)
    {
        var k = r[0];
        var filled = Regex.Replace(k, @"\{(\d+)\}", m => "Ж" + m.Groups[1].Value);
        var t = L.T(filled);
        if (Regex.IsMatch(t, @"\{\d+\}")) { if (bad++ < 5) Console.WriteLine($"   осталось {{n}}: {k} → {t}"); }
        // шаблоны, где слов меньше двух букв («  — {0} {1} с»), и строки без кириллицы («№») переводчик не берёт — так задумано
        bool expectSame = r.Length <= col || r[col].Trim() == k.Trim() || r[col].Trim().Length == 0 ||
                          Regex.Replace(k, @"\{\d+\}", "").Count(char.IsLetter) < 2 || !Regex.IsMatch(k, "[А-Яа-яЁё]");
        if (t == filled && !expectSame) { if (same++ < 10) Console.WriteLine($"   не переведено: {k}"); }
    }
    Check(bad == 0, $"{lang}: все {rows.Count} ключей — без «{{n}}» в переводе ({bad})");
    Check(same == 0, $"{lang}: все ключи переводятся ({same} без перевода)");
}

L.Set("en");
var html = L.Html("<html lang=ru><body><h1>Отчёт по деловой игре «Приём поезда на станцию»</h1><table><tr><th>ФИО</th><td>Иванов</td></tr></table>" +
                  "<div class=log>08:00:01  [Занятие] Игровое время: ПУСК\n08:01:00  Поезд №2002 ушёл со схемы</div></body></html>");
Console.WriteLine(html);
Check(html.Contains("lang=en") && html.Contains("Full name") && html.Contains("Game time: RUN") && !html.Contains("Отчёт"), "HTML отчёта переведён (заголовок, таблица, журнал)");

// ---- снимки окон ----
if (args.Length > 0)
{
    Directory.CreateDirectory(args[0]);
    LangUi.Install();
    ModelHost.Start(false);
    var f = (Form)Activator.CreateInstance(typeof(ModelHost).Assembly.GetType("StationApp.MainForm")!, true)!;   // MainForm — internal
    f.StartPosition = FormStartPosition.Manual;
    f.Show();
    f.WindowState = FormWindowState.Normal;
    f.Bounds = new Rectangle(-4000, -4000, 1900, 1000);
    Du46Ui.Open();
    var w = Application.OpenForms.Cast<Form>().First(x => x.GetType().Name == "Du46Window");
    w.Location = new Point(-4000, -4000);
    void Snap(Form x, string name)
    {
        for (int i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(50); }
        using var bmp = new Bitmap(x.Width, x.Height);
        x.DrawToBitmap(bmp, new Rectangle(Point.Empty, x.Size));
        bmp.Save(Path.Combine(args[0], name + ".png"));
        Console.WriteLine("снимок: " + name);
    }
    foreach (var lang in new[] { "kk", "en", "ru" })
    {
        L.Set(lang);
        Snap(f, "akzht_" + lang);
        Snap(w, "du46_" + lang);
    }
    L.Set("ru");
    ModelHost.Stop();
}

Console.WriteLine(fails == 0 ? "ВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"ОШИБОК: {fails}");
return fails == 0 ? 0 : 1;
