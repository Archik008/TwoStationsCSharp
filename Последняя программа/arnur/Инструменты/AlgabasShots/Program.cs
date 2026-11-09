using StationApp.Model;
using StationApp.Ui;

// =====================================================================
//  [ДОБАВЛЕНО 28.10.2026] Снимки главного окна Алгабаса во всех режимах интерфейса.
//  Запуск: dotnet run --project Инструменты\AlgabasShots <папка>
//  Режим пользователя (интерфейс.txt) после снимков возвращается как был.
// =====================================================================

var dir = Path.GetFullPath(args.Length > 0 ? args[0] : "algabas_shots");
Directory.CreateDirectory(dir);
ModelHost.Start();
var main = (Form)Activator.CreateInstance(typeof(ModelHost).Assembly.GetType("StationApp.Algabas.AlgabasForm")!, true)!;
main.StartPosition = FormStartPosition.Manual;
main.Show();
main.WindowState = FormWindowState.Normal;
main.Bounds = new Rectangle(-4000, -4000, 1920, 1040);

var was = UiSettings.Mode;
foreach (var m in Enum.GetValues<UiMode>())
{
    UiSettings.SetMode(m);
    for (int i = 0; i < 25; i++) { Application.DoEvents(); Thread.Sleep(40); }
    using var bmp = new Bitmap(main.Width, main.Height);
    main.DrawToBitmap(bmp, new Rectangle(Point.Empty, main.Size));
    bmp.Save(Path.Combine(dir, $"algabas_{m}.png"));
}
UiSettings.SetMode(was);
main.Close();
ModelHost.Stop();
