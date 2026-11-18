namespace DncApp;

// =====================================================================
//  Программа «ДНЦ» — панель диспетчера  [ДОБАВЛЕНО 18.11.2026]
//  Подключается к серверу PromServer под именем "dnc" (как «Преподаватель» — под "teacher").
//  В одном окне — схемы станций в линию слева направо; какие и в каком порядке — выбирает диспетчер.
//  Станцию можно раскрыть на всё окно и забрать управление: кнопки пульта станции — сверху, схема — под ними.
//  Протокол со станциями — Core/Training.cs программ станций (сообщения t_info, t_scheme, t_screen, t_take, t_click).
// =====================================================================

static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        // имя "dnc" на сервере одно — вторая копия программы выбила бы первую
        using var single = new Mutex(true, "DncApp_ArnurRail", out bool first);
        if (!first) { MessageBox.Show("Программа «ДНЦ» уже запущена.", "ДНЦ"); return; }
        Settings.Load();
        Application.Run(new DncForm());
    }
}
