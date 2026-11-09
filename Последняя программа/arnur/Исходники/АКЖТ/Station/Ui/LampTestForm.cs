using StationApp.Core;
using StationApp.Model;

namespace StationApp.Ui;

// =====================================================================
//  ПРОВЕРКА ПРОВОДКИ «ЛАМПЫ ПО ОДНОЙ»  [ДОБАВЛЕНО 28.10.2026]
//  На макете горит только выбранный бит (ArduinoLink.OnlyLamp): видно, та ли лампа загорается.
//  В списке все биты всех байтов: какой светофор и лампа к нему привязаны по светофоры.txt.
//  «Авто» — перебор по одной лампе раз в секунду. Закрыли окно — обычная работа.
// =====================================================================

class LampTestForm : Form
{
    readonly ListBox list = new() { Dock = DockStyle.Fill, IntegralHeight = false, Font = new Font("Consolas", 10f) };
    readonly System.Windows.Forms.Timer auto = new() { Interval = 1000 };
    readonly Label info = new() { Dock = DockStyle.Top, Height = 54, Padding = new Padding(8, 6, 8, 0) };
    readonly List<(int b, int bit, string text)> bits = new();

    public LampTestForm()
    {
        Text = "Проверка проводки: лампы по одной";
        Font = new Font("Segoe UI", 10f);
        Size = new Size(560, 640);
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;

        var names = new Dictionary<(int, int), string>();
        foreach (var ((sig, lamp), pos) in ModelHost.Model.Hw.Lamps)
        {
            var s = ModelHost.Model.IL.Signals.TryGetValue(sig, out var rs) ? rs : null;
            string scheme = sig.StartsWith("А:") ? "АКЖТ" : "ПОСТ";
            names[pos] = $"{scheme} {s?.Label ?? sig} — {LampRu(lamp)}";
        }
        for (int b = 0; b < ArduinoLink.FrameBytes; b++)
            for (int bit = 0; bit < 8; bit++)
            {
                string t = names.TryGetValue((b, bit), out var n) ? n : "— не привязан";
                bits.Add((b, bit, t));
                list.Items.Add($"{b,2}.{bit}   {t}");
            }

        var bar = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, Padding = new Padding(6) };
        Button B(string text, Action a) { var x = new Button { Text = text, AutoSize = true, Height = 32, Margin = new Padding(3) }; x.Click += (_, _) => a(); bar.Controls.Add(x); return x; }
        B("◀ Предыдущая", () => Move(-1));
        B("Следующая ▶", () => Move(1));
        var bAuto = B("Авто (1 с)", () => { });
        bAuto.Click += (_, _) => { auto.Enabled = !auto.Enabled; bAuto.Text = auto.Enabled ? "Стоп" : "Авто (1 с)"; };
        B("Погасить все", () => { list.SelectedIndex = -1; ArduinoLink.OnlyLamp = (-1, -1); Pump(); Show(); });
        B("Закрыть", Close);

        Controls.Add(list);
        Controls.Add(info);
        Controls.Add(bar);

        list.SelectedIndexChanged += (_, _) =>
        {
            if (list.SelectedIndex < 0) return;
            var (b, bit, _) = bits[list.SelectedIndex];
            ArduinoLink.OnlyLamp = (b, bit);
            Pump();
            Show();
        };
        auto.Tick += (_, _) => Move(1);
        FormClosed += (_, _) =>
        {
            auto.Dispose();
            ArduinoLink.OnlyLamp = null;
            Pump();
        };
        ArduinoLink.OnlyLamp = (-1, -1);   // сразу всё погашено
        Pump();
        Show();
    }

    new void Show()
    {
        string conn = ArduinoLink.IsConnected ? $"Arduino: {ArduinoLink.PortName}" : "Arduino не подключена — выберите порт в главном окне";
        info.Text = (list.SelectedIndex < 0 ? "Выберите бит в списке — на макете загорится только эта лампа." :
                     $"Горит: байт {bits[list.SelectedIndex].b}, бит {bits[list.SelectedIndex].bit} — {bits[list.SelectedIndex].text}") +
                    $"\n{conn}. Байт 0 — первый регистр, бит 0 — выход Q0. Карта — файл {AkzhtHardware.LampsFileName}.";
    }

    void Move(int d)
    {
        if (bits.Count == 0) return;
        int i = list.SelectedIndex < 0 ? (d > 0 ? 0 : bits.Count - 1) : (list.SelectedIndex + d + bits.Count) % bits.Count;
        list.SelectedIndex = i;
    }

    static void Pump()
    {
        ArduinoLink.PumpLights();
        ModelHost.MarkDirty();
    }

    static string LampRu(string lamp) => lamp switch
    {
        "yellow" => "жёлтый (верхний)", "yellow1" => "жёлтый нижний", "green" => "зелёный", "red" => "красный",
        "white" => "белый", "blue" => "синий", _ => lamp
    };
}
