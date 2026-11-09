using System.Reflection;
using StationApp.Core;
using StationApp.Model;
using StationApp.Ui;

// =====================================================================
//  [ДОБАВЛЕНО 28.10.2026] Тест режимов интерфейса (Ui/UiSettings.cs, Ui/StationPane.cs, Ui/ClassicPanel.cs):
//  панели по умолчанию, «Параметры» (выбор панелей запоминается в интерфейс.txt), перестройка окна станции
//  в каждом режиме, маршрут кнопками кнопочного пульта «Енисей». Файл пользователя не трогается.
//  Запуск: dotnet run --project Инструменты\UiModesTest
// =====================================================================

int fails = 0;
void Check(bool ok, string what) { Console.WriteLine((ok ? "OK      " : "ОШИБКА  ") + what); if (!ok) fails++; }

ModelHost.Start(false);
var tmp = Path.Combine(Path.GetTempPath(), "UiModesTest_" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(tmp);
DispatchLink.SettingsFile = Path.Combine(tmp, "server.txt");   // интерфейс.txt — во временной папке
UiSettings.Load();
Check(UiSettings.Mode == UiMode.Engineer, "без файла — инженерный режим");

// ---- панели по умолчанию ----
Check(UiSettings.Show(UiMode.Engineer, UiSettings.Sections) && UiSettings.Show(UiMode.Engineer, UiSettings.Test), "инженерный: список участков и проверка ламп");
Check(!UiSettings.Show(UiMode.Training, UiSettings.Sections) && UiSettings.Show(UiMode.Training, UiSettings.Sim), "учебный: без списка участков, с имитацией");
Check(!UiSettings.Show(UiMode.Work, UiSettings.Sim) && !UiSettings.Show(UiMode.Work, UiSettings.ManualOcc) && UiSettings.Show(UiMode.Work, UiSettings.Hw),
      "рабочий: без имитации и ручной занятости, с портом Arduino");
Check(UiSettings.Show(UiMode.Classic, UiSettings.Classic) && !UiSettings.Show(UiMode.Engineer, UiSettings.Classic), "кнопочный пульт — только в павлодарском");

// ---- «Параметры»: выбор запоминается ----
int changed = 0;
UiSettings.Changed += () => changed++;
var sel = UiSettings.Panels.ToDictionary(p => p.id, p => UiSettings.DefaultShow(UiMode.Work, p.id));
sel[UiSettings.Log] = true;
UiSettings.Apply(UiMode.Work, sel);
Check(changed == 1 && UiSettings.Mode == UiMode.Work && UiSettings.Show(UiSettings.Log), "Apply: режим и журнал в рабочем, окно перестроено один раз");
var file = Path.Combine(tmp, "интерфейс.txt");
Check(File.Exists(file) && File.ReadAllText(file).Contains("Work.log = 1"), "интерфейс.txt записан");
UiSettings.Load();
Check(UiSettings.Mode == UiMode.Work && UiSettings.Show(UiSettings.Log), "интерфейс.txt прочитан обратно");
UiSettings.ResetPanels(UiMode.Work);
Check(!UiSettings.Show(UiSettings.Log), "«По умолчанию» — журнал рабочего снова скрыт");

// ---- окно станции в каждом режиме ----
var asm = typeof(ModelHost).Assembly;
var paneType = asm.GetType("StationApp.Ui.StationPane")!;
var A = ModelHost.Model.Akzht;
var pane = (Control)Activator.CreateInstance(paneType, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null,
                                             new object[] { A.View, true, true }, null)!;
var form = new Form { Width = 1920, Height = 1040, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
pane.Dock = DockStyle.Fill;
form.Controls.Add(pane);
form.Show();
IEnumerable<Control> All(Control c) => c.Controls.Cast<Control>().SelectMany(x => All(x).Prepend(x));
Control Find(string typeName) => All(pane).FirstOrDefault(c => c.GetType().Name == typeName);
foreach (var m in Enum.GetValues<UiMode>())
{
    UiSettings.SetMode(m);
    Application.DoEvents();
    bool hasScheme = Find("RailSchemePanel") is { Height: > 200 };
    bool hasClassic = Find("ClassicPanel") != null;
    bool hasLog = All(pane).Any(c => c is ListBox);
    bool hasSim = All(pane).Any(c => c is Label l && l.Text == "Имитация (обучение):");
    bool err = All(pane).Any(c => c is ListBox lb && lb.Items.Cast<object>().Any(i => i.ToString()!.Contains("Ошибка перестройки")));
    Check(hasScheme && !err && hasClassic == (m == UiMode.Classic) && hasLog == UiSettings.Show(UiSettings.Log) && hasSim == UiSettings.Show(UiSettings.Sim),
          $"{UiSettings.ModeName(m)}: схема видна, журнал {(hasLog ? "есть" : "нет")}, имитация {(hasSim ? "есть" : "нет")}, кнопочный пульт {(hasClassic ? "есть" : "нет")}");
}

// ---- павлодарский: маршрут кнопками светофоров ----
UiSettings.SetMode(UiMode.Classic);
Application.DoEvents();
var il = ModelHost.Model.IL;
var route = A.View.Routes.First(r => !r.Shunting && r.End != null && !r.Active);
Button Btn(string row, string label)
{
    var classic = Find("ClassicPanel")!;
    var title = classic.Controls.OfType<Label>().First(l => l.Text == row);
    var flow = ((TableLayoutPanel)classic).GetControlFromPosition(1, ((TableLayoutPanel)classic).GetRow(title))!;
    return flow.Controls.OfType<Button>().First(b => b.Text == label);
}
Btn("ПОЕЗДНЫЕ", route.Start.Label).PerformClick();
Btn("ПОЕЗДНЫЕ", route.End.Label).PerformClick();
for (int i = 0; i < 100 && !route.Active; i++) { il.Tick(0.1); }
Check(route.Active || A.View.Routes.Any(r => r.Active && r.Start == route.Start), $"кнопки {route.Start.Label} → {route.End.Label}: маршрут задан");
var sw = A.View.Switches.First();
bool plus = sw.Plus;
var swRow = Find("ClassicPanel")!.Controls.OfType<Label>().First(l => l.Text == "СТРЕЛКИ");
il.ResetAll();
for (int i = 0; i < 50; i++) il.Tick(0.1);
plus = sw.Plus;
var swFlow = ((TableLayoutPanel)Find("ClassicPanel")!).GetControlFromPosition(1, ((TableLayoutPanel)Find("ClassicPanel")!).GetRow(swRow))!;
var grp = swFlow.Controls[0];
grp.Controls.OfType<Button>().First(b => b.Text == (plus ? "−" : "+")).PerformClick();
for (int i = 0; i < 100; i++) il.Tick(0.1);
Check(sw.Plus != plus, $"стрелка {sw.Label}: кнопка «{(plus ? "−" : "+")}» перевела");

// ---- рабочий режим: ручной занятости в меню нет ----
UiSettings.SetMode(UiMode.Work);
Check(!UiSettings.Show(UiSettings.ManualOcc), "рабочий: «Занять вручную» скрыто");

form.Close();
ModelHost.Stop();
try { Directory.Delete(tmp, true); } catch { }
Console.WriteLine(fails == 0 ? "\nВСЕ ПРОВЕРКИ ПРОЙДЕНЫ" : $"\nОШИБОК: {fails}");
return fails;
