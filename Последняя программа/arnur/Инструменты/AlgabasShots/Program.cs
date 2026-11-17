using StationApp.Model;
using StationApp.Ui;

// =====================================================================
//  [ДОБАВЛЕНО 28.10.2026] Снимки главного окна Алгабаса во всех режимах интерфейса.
//  Запуск: dotnet run --project Инструменты\AlgabasShots <папка> [ширина высота]
//  [ДОБАВЛЕНО 17.11.2026] размер окна — для проверки на маленьком мониторе (например 1366 728, 1024 700)
//  Режим пользователя (интерфейс.txt) после снимков возвращается как был.
// =====================================================================

var dir = Path.GetFullPath(args.Length > 0 ? args[0] : "algabas_shots");
Directory.CreateDirectory(dir);
ModelHost.Start();
var main = (Form)Activator.CreateInstance(typeof(ModelHost).Assembly.GetType("StationApp.Algabas.AlgabasForm")!, true)!;
main.StartPosition = FormStartPosition.Manual;
main.Show();
main.WindowState = FormWindowState.Normal;
int w = args.Length > 2 ? int.Parse(args[1]) : 1920, h = args.Length > 2 ? int.Parse(args[2]) : 1040;
main.MinimumSize = Size.Empty;   // окно само ставит минимум по экрану — для снимков задаём размер явно
main.Bounds = new Rectangle(-4000, -4000, w, h);

var was = UiSettings.Mode;
foreach (var m in Enum.GetValues<UiMode>())
{
    UiSettings.SetMode(m);
    for (int i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(40); }
    using var bmp = new Bitmap(main.Width, main.Height);
    main.DrawToBitmap(bmp, new Rectangle(Point.Empty, main.Size));
    bmp.Save(Path.Combine(dir, $"algabas_{m}_{w}x{h}.png"));
}
UiSettings.SetMode(was);
main.Close();
ModelHost.Stop();
