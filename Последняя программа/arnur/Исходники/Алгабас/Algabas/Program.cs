using StationApp.Algabas;
using StationApp.Model;

namespace StationApp;

static class Program
{
    /// <summary>Точка входа программы станции Алгабас.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // [ДОБАВЛЕНО 22.10.2026, этап 8] язык интерфейса (ru / kk / en) и перевод окон
        Core.L.Init(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationAlgabas", "lang.txt"));
        Ui.LangUi.Install();

        // [ДОБАВЛЕНО 20.10.2026] размеры подписей схемы — из scheme_style.ini рядом с программой (создаётся сам), как у АКЖТ
        Ui.SchemeStyle.Load();
        ModelHost.Start();
        Ai.AiStation.Install();              // [ДОБАВЛЕНО 06.11.2026] ИИ-помощник, как у АКЖТ: журнал программы и «показ мышью»
        Ui.UiSettings.AiAvailable = true;

        var form = new AlgabasForm { WindowState = FormWindowState.Maximized };
        Application.Run(form);
        ModelHost.Stop();
    }
}
