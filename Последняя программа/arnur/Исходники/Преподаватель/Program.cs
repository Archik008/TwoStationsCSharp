namespace TeacherApp;

// Программа «Преподаватель» — занятия на тренажёре ДСП (станции АКЖТ и Алгабас).
static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        using var single = new Mutex(true, "TeacherApp_ArnurRail", out bool first);
        if (!first) { MessageBox.Show("Программа «Преподаватель» уже запущена.", "Преподаватель"); return; }
        // [ДОБАВЛЕНО 22.10.2026, этап 8] язык интерфейса (ru / kk / en), перевод окон и свойств урока
        StationApp.Core.L.Init(Path.Combine(Db.Dir, "lang.txt"));
        StationApp.Ui.LangUi.Install();
        StationApp.Ui.LangUi.TranslateProperties(typeof(Lesson));
        try { Db.Load(); }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось открыть папку данных:\n" + ex.Message, "Преподаватель", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }
        Application.Run(new TeacherForm());
    }
}
