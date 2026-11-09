using System.Reflection;
using System.Text.Json;
using TeacherApp;

// Окна преподавателя с данными, как их присылает станция (s_objects, s_faults).
var dir = args.Length > 0 ? args[0] : ".";
Directory.CreateDirectory(dir);
var st = new StationRt { Id = "algabas", Name = "Станция Алгабас", Online = true };
st.Faults.Add(new FaultRow { Id = "f1", Name = "Потеря контроля положения", Label = "стрелка 2", Cond = "без условия, немедленно", State = "fixing", StateText = "устраняет ШН", Activated = 28802 });
st.Faults.Add(new FaultRow { Id = "f2", Name = "Неисправность двигателя", Label = "участок 1П", Train = 3, Cond = "при переходе в состояние «занят», поездной, немедленно", State = "wait", StateText = "ждёт условия" });
var objects = JsonSerializer.SerializeToElement(new
{
    kind = "s_objects",
    signals = new[] { new { id = "S:Ч", label = "Ч" }, new { id = "S:Н", label = "Н" } },
    switches = new[] { new { id = "W:2", label = "2" } },
    sections = new[] { new { id = "P:1П", label = "1П" } },
    lines = new[] { new { id = "L:ОП", label = "Перегон Путевой пост — Алгабас" } },
    trains = new[] { new { id = "T:3", label = "№3 (поездной) на 1П" } },
    kinds = new[] { new { code = "sw_nocontrol", name = "Потеря контроля положения", target = "switch" },
                    new { code = "sig_noopen", name = "Светофор не открывается", target = "signal" },
                    new { code = "sig_drop", name = "Самопроизвольное перекрытие светофора", target = "signal" },
                    new { code = "tr_engine", name = "Неисправность двигателя", target = "train" },
                    new { code = "tr_brakes", name = "Неисправность тормозов", target = "train" } },
    states = new Dictionary<string, string[]> { ["signal"] = new[] { "открыт", "закрыт" }, ["switch"] = new[] { "+", "−" } },
});

void Shot(Form f, string name)
{
    f.StartPosition = FormStartPosition.Manual;
    f.Location = new Point(-4000, -4000);
    f.Show();
    Application.DoEvents();
    using var bmp = new Bitmap(f.Width, f.Height);
    f.DrawToBitmap(bmp, new Rectangle(Point.Empty, f.Size));
    bmp.Save(Path.Combine(dir, name + ".png"));
    Console.WriteLine("снимок: " + name);
}

Shot(new FaultsWindow(st), "teacher_faults");
var d = new ExpressDialog(st);
st.Objects = objects;
var ev = typeof(Classroom).GetField("Changed", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) as Action;
ev?.Invoke();
Shot(d, "teacher_express");

// ---- [21.10.2026] большое окно экрана станции с кадром АКЖТ (кадр — из e2e_screen_test.ps1, если есть) ----
var jpgPath = Path.Combine(dir, "screen_whole.jpg");
if (File.Exists(jpgPath))
{
    st.Name = "АКЖТ + путевой пост";
    var big = new StationScreenWindow(st) { WindowState = FormWindowState.Normal, Size = new Size(1400, 900) };
    big.StartPosition = FormStartPosition.Manual;
    big.Location = new Point(-4000, -4000);
    big.Show();
    var tagValue = (string)typeof(StationScreenWindow).GetField("tag", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(big)!;
    var frame = new ScreenFrame { Jpg = Convert.ToBase64String(File.ReadAllBytes(jpgPath)), Tag = tagValue };
    frame.Parts.Add(("АКЖТ", new RectangleF(0, 0, 0.5f, 1)));
    frame.Parts.Add(("Путевой пост", new RectangleF(0.5f, 0, 0.5f, 1)));
    var screenEv = typeof(Classroom).GetField("Screen", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null) as Action<string, ScreenFrame>;
    screenEv?.Invoke(st.Id, frame);
    Shot(big, "teacher_bigscreen");
}
