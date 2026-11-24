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
