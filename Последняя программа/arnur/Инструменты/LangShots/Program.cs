using System.Reflection;
using System.Text.RegularExpressions;

// =====================================================================
//  [ДОБАВЛЕНО 23.11.2026] Снимки окна программы на ru / kk / en и список надписей, что остались русскими.
//  Программа грузится как сборка (exe не запускается), окно строится за краем экрана.
//  dotnet run -c Release -- <StationEditor.dll | Dnc.dll> <папка для снимков>
//  Язык в файле настроек пользователя не меняется: перевод включается через L.Set, а файл языка — во временной папке.
// =====================================================================

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Console.WriteLine("ИСКЛЮЧЕНИЕ: " + e.Exception);
        // настройки ДНЦ — во временной папке (файлы пользователя не трогаем); окно не закрываем — без автосохранения
        Environment.SetEnvironmentVariable("DNC_DATA_DIR", Path.Combine(Path.GetTempPath(), "LangShots_dnc"));
        var asm = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var outDir = Path.GetFullPath(args[1]);
        Directory.CreateDirectory(outDir);
        var L = asm.GetType("StationApp.Core.L")!;
        var LangUi = asm.GetType("StationApp.Ui.LangUi")!;
        L.GetMethod("Init")!.Invoke(null, new object[] { Path.Combine(Path.GetTempPath(), "LangShots_lang.txt") });
        LangUi.GetMethod("Install")!.Invoke(null, null);
        var formType = asm.GetTypes().First(t => t.Name is "MainForm" or "DncForm" && typeof(Form).IsAssignableFrom(t));
        var ctor = formType.GetConstructors().First();
        var form = (Form)ctor.Invoke(ctor.GetParameters().Select(_ => (object)null).ToArray());
        form.StartPosition = FormStartPosition.Manual;
        form.WindowState = FormWindowState.Normal;
        form.FormBorderStyle = FormBorderStyle.Sizable;
        form.Bounds = new Rectangle(-3000, 0, 1600, 900);
        form.Show();
        var cyr = new Regex("[а-яё]{3,}");
        int left = 0;
        foreach (var lang in new[] { "ru", "kk", "en" })
        {
            L.GetMethod("Set")!.Invoke(null, new object[] { lang });
            for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(50); }
            LangUi.GetMethod("Apply")!.Invoke(null, new object[] { form, true });
            Application.DoEvents();
            form.Bounds = new Rectangle(-3000, 0, 1600, 900);
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(Point.Empty, form.Size));
            bmp.Save(Path.Combine(outDir, $"{formType.Name}_{lang}.png"));
            if (lang == "ru") continue;
            var texts = new SortedSet<string>();
            void Walk(Control c)
            {
                if (c is not (TextBoxBase or ComboBox or ListControl) && cyr.IsMatch(c.Text ?? "")) texts.Add($"{c.GetType().Name}: {c.Text}");
                if (c is ToolStrip ts) WalkItems(ts.Items);
                foreach (Control x in c.Controls) Walk(x);
            }
            void WalkItems(ToolStripItemCollection items)
            {
                foreach (ToolStripItem it in items)
                {
                    if (cyr.IsMatch(it.Text ?? "")) texts.Add($"{it.GetType().Name}: {it.Text}");
                    if (it is ToolStripDropDownItem dd) WalkItems(dd.DropDownItems);
                }
            }
            Walk(form);
            Console.WriteLine($"== {lang}: русских надписей {texts.Count}");
            foreach (var t in texts) Console.WriteLine("   " + t.Replace("\n", " / "));
            left += texts.Count;
        }
        foreach (var lang in new[] { "kk", "ru", "en", "ru" })   // ещё раз сменить язык туда-обратно: окно не должно падать
        {
            L.GetMethod("Set")!.Invoke(null, new object[] { lang });
            for (int i = 0; i < 30; i++) { Application.DoEvents(); Thread.Sleep(50); }
            Console.WriteLine("смена языка → " + lang + ": ок");
        }
        Console.Out.Flush();
        Environment.Exit(0);   // без FormClosing: редактор не пишет автосохранение, ДНЦ — настройки
        return 0;
    }
}
