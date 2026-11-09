using StationApp.Model;

namespace StationApp;

static class Program
{
    /// <summary>Точка входа приложения.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // [ДОБАВЛЕНО 22.10.2026, этап 8] язык интерфейса (ru / kk / en) и перевод окон
        Core.L.Init(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StationAKZHT", "lang.txt"));
        Ui.LangUi.Install();

        // Размеры подписей схемы и т.п. — из scheme_style.ini рядом с программой (создаётся сам).
        Ui.SchemeStyle.Load();

        // Одна общая модель (АКЖТ + перегон + пост).
        // Вариант "с депо / без депо" запоминается в настройках (кнопка "Депо" в половине АКЖТ).
        ModelHost.Start(ModelHost.LoadDepotSetting());
        Ai.AiStation.Install();              // [ДОБАВЛЕНО 28.10.2026] ЖИ-помощник читает журнал программы и «показ мышью»
        Ui.UiSettings.AiAvailable = true;

        // [ИЗМЕНЕНО 06.10.2026] Одно окно на АКЖТ и пост: на двух мониторах оно растягивается
        // на оба (АКЖТ — на основном), на одном — развёрнуто, обе схемы рядом.
        Application.Run(new MainForm());
        ModelHost.Stop();
    }
}
