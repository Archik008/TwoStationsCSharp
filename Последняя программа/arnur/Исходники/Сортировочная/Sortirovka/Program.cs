using StationApp.Core;
using StationApp.Ui;

namespace StationApp;

// =====================================================================
//  «СОРТИРОВОЧНАЯ» — учебная сортировочная станция с горкой  [ДОБАВЛЕНО 12.11.2026]
//  Студент — дежурный по горке (ДСПГ) и оператор замедлителей. Подробно — ИНСТРУКЦИЯ.txt рядом с программой.
// =====================================================================

static class Program
{
    public static string SettingsDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sortirovka");

    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        try { Directory.CreateDirectory(SettingsDir); } catch { }
        L.Init(Path.Combine(SettingsDir, "lang.txt"));
        LangUi.Install();
        // [ДОБАВЛЕНО 24.11.2026] ИИ-помощник горки — через крючки окна (окно общее со «Станцией по схеме»)
        YardForm.AiSim = s => Ai.AiStation.Sim = s;
        YardForm.AiPoint = id => Ai.AiStation.Point(id);
        YardForm.AiNote = (t, bad) => Ai.AiStation.Note(t, bad);
        // служебное: Sortirovka.exe --shot файл.png секунд [lang] — снимок окна без показа (для инструкций и проверки)
        var args = Environment.GetCommandLineArgs();
        // [ДОБАВЛЕНО 26.11.2026] служебные запуски (снимки, проверка ИИ) язык из ключа НЕ запоминают — выбор пользователя не сбивается
        if (args.Any(a => a is "--shot" or "--tasks-shot" or "--devs-shot" or "--ask")) L.Init(null);
        // [26.11.2026] служебное: --mode training|exam|demo|shunt — вариант при запуске (для снимков)
        // служебное: --tasks-shot файл.png [номер] [lang] — снимок окна «Задания»
        // [ДОБАВЛЕНО 26.11.2026] служебное: --devs-shot файл.png [lang] — снимок окна «Разработчики»
        int dsi = Array.IndexOf(args, "--devs-shot");
        if (dsi >= 0 && args.Length > dsi + 1)
        {
            string dl = args.Length > dsi + 2 ? args[dsi + 2] : "ru";
            L.Set(dl);
            // [26.11.2026] дальше необязательно: номер человека 0..3 (открытая панель «что делал») и номер карточки под мышью
            int person = args.Length > dsi + 3 && int.TryParse(args[dsi + 3], out int pp) ? pp : -1;
            int hov = args.Length > dsi + 4 && int.TryParse(args[dsi + 4], out int hh) ? hh : -1;
            StationApp.Common.DevelopersForm.Shot(args[dsi + 1], L.T("Сортировочная — пульт дежурного по горке (ДСПГ)"), dl, person, hov);
            return;
        }
        int tsi = Array.IndexOf(args, "--tasks-shot");
        if (tsi > 0 && args.Length > tsi + 1)
        {
            YardForm.TasksShot = args[tsi + 1];
            if (args.Length > tsi + 2 && int.TryParse(args[tsi + 2], out int ix)) YardForm.TasksShotIndex = ix;
            if (args.Length > tsi + 3) L.Set(args[tsi + 3]);
            using var tf = new YardForm();
            tf.ShotTasks();
            return;
        }
        int ti = Array.IndexOf(args, "--task");
        if (ti > 0 && args.Length > ti + 1) YardForm.StartupTask = Yard.YardTask.All.FirstOrDefault(t => t.Id == args[ti + 1]);
        int mi = Array.IndexOf(args, "--mode");
        if (mi > 0 && args.Length > mi + 1)
            YardForm.StartupMode = args[mi + 1] switch { "training" => Yard.YardMode.Training, "exam" => Yard.YardMode.Exam, "demo" => Yard.YardMode.Demo, "shunt" => Yard.YardMode.HumpShunt, _ => Yard.YardMode.Hump };
        int i = Array.IndexOf(args, "--shot");
        if (i > 0 && args.Length > i + 2)
        {
            if (args.Length > i + 3 && !args[i + 3].StartsWith("--")) L.Set(args[i + 3]);
            using var f = new YardForm();
            f.Shot(args[i + 1], double.Parse(args[i + 2], System.Globalization.CultureInfo.InvariantCulture), args.Contains("--manual"));
            return;
        }
        // служебное: Sortirovka.exe --ask ответы.txt «вопрос 1» «вопрос 2» … — проверка ИИ-помощника без окна
        int q = Array.IndexOf(args, "--ask");
        if (q > 0 && args.Length > q + 2)
        {
            Ai.AiStation.Sim = new Yard.YardSim(1);
            using var ai = new Ai.AiAssistant();
            var sb = new System.Text.StringBuilder();
            foreach (var question in args.Skip(q + 2))
                sb.AppendLine("ВОПРОС: " + question).AppendLine(ai.AskAsync(question).GetAwaiter().GetResult()).AppendLine();
            File.WriteAllText(args[q + 1], sb.ToString());
            return;
        }
        Application.Run(new YardForm());
    }
}
