using StationApp.Model;

namespace StationApp;

static class Program
{
    /// <summary>Точка входа приложения.</summary>
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();

        // Размеры подписей схемы и т.п. — из scheme_style.ini рядом с программой (создаётся сам).
        Ui.SchemeStyle.Load();

        // Одна общая модель (АКЖТ + перегон + пост).
        // Вариант "с депо / без депо" запоминается в настройках (кнопка "Депо" в половине АКЖТ).
        ModelHost.Start(ModelHost.LoadDepotSetting());

        // [LAB] подключаем помощника «ЖД AI» к журналу и состоянию станции (логику программы не меняем)
        Ui.LabIntegration.Install();

        // [ИЗМЕНЕНО 06.10.2026] Одно окно на АКЖТ и пост: на двух мониторах оно растягивается
        // на оба (АКЖТ — на основном), на одном — развёрнуто, обе схемы рядом.
        Application.Run(new MainForm());   // [LAB] заголовок и метка «ЭКСПЕРИМЕНТ» — в MainForm
        ModelHost.Stop();
    }
}
