using System.Media;
using StationApp.Core;

namespace StationApp.Ui;

// =====================================================================
//  ОКНО «СОСЕДНЯЯ СТАНЦИЯ ПРОСИТ СМЕНИТЬ НАПРАВЛЕНИЕ»: Дать согласие / Отказать.
//  [ДОБАВЛЕНО 12.10.2026, проблемы.txt п.4] Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//  Окно модальное, поверх всех; если дежурный не ответил за отведённое время — отказ.
//  Если сосед отменил запрос (или отключился) — окно закрывается само.
// =====================================================================

class DirectionConsentDialog : Form
{
    static readonly Color Bg = Color.FromArgb(245, 247, 246);
    readonly ConsentRequest req;
    readonly Label lblLeft;
    readonly System.Windows.Forms.Timer timer = new() { Interval = 250 };
    readonly DateTime deadline;
    bool answered;

    DirectionConsentDialog(ConsentRequest req)
    {
        this.req = req;
        deadline = DateTime.Now.AddSeconds(req.Seconds);
        Text = req.Title;
        Font = new Font("Bahnschrift", 11f);
        BackColor = Bg;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ControlBox = false;                  // закрыть можно только ответом
        ShowInTaskbar = false;
        TopMost = true;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(18);
        StartPosition = FormStartPosition.Manual;

        var flow = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false };
        flow.Controls.Add(new Label
        {
            Text = req.Title.ToUpperInvariant(), AutoSize = true, ForeColor = Color.FromArgb(219, 0, 105),
            Font = new Font("Bahnschrift", 14f, FontStyle.Bold), Margin = new Padding(3, 0, 3, 10)
        });
        flow.Controls.Add(new Label { Text = req.Text, AutoSize = true, MaximumSize = new Size(620, 0) });
        lblLeft = new Label { AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 14, 3, 3) };
        flow.Controls.Add(lblLeft);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, Margin = new Padding(0, 14, 0, 0) };
        var bYes = Big("Дать согласие", Color.FromArgb(0, 140, 60));
        var bNo = Big("Отказать", Color.FromArgb(190, 20, 20));
        bYes.Click += (_, _) => Finish(true, "");
        bNo.Click += (_, _) => Finish(false, "дежурный отказал");
        buttons.Controls.Add(bYes);
        buttons.Controls.Add(bNo);
        flow.Controls.Add(buttons);
        Controls.Add(flow);
        CancelButton = bNo;                  // Esc — отказ

        timer.Tick += (_, _) => UpdateLeft();
        req.Cancelled += OnCancelled;
        UpdateLeft();
    }

    static Button Big(string text, Color c)
    {
        var b = new Button
        {
            Text = text, AutoSize = true, MinimumSize = new Size(200, 52), FlatStyle = FlatStyle.Flat, BackColor = c,
            ForeColor = Color.White, Font = new Font("Bahnschrift", 12f, FontStyle.Bold), Margin = new Padding(3, 3, 14, 3),
            UseVisualStyleBackColor = false
        };
        b.FlatAppearance.BorderSize = 0;
        return b;
    }

    void UpdateLeft()
    {
        double left = (deadline - DateTime.Now).TotalSeconds;
        if (left <= 0)
        {
            Finish(false, "дежурный не ответил");
            return;
        }
        lblLeft.Text = $"Нет ответа через {Math.Ceiling(left):0} с — это будет отказ.";
    }

    void Finish(bool ok, string reason)
    {
        if (answered) return;
        answered = true;
        timer.Stop();
        req.Cancelled -= OnCancelled;
        Close();
        req.Answer?.Invoke(ok, reason);
    }

    void OnCancelled()
    {
        if (answered) return;
        answered = true;
        timer.Stop();
        Close();
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        // на том мониторе, где сейчас мышь (у АКЖТ окно растянуто на два монитора)
        var wa = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(wa.Left + (wa.Width - Width) / 2, wa.Top + (wa.Height - Height) / 3);
        SystemSounds.Exclamation.Play();
        timer.Start();
        Activate();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        timer.Dispose();
        base.OnFormClosed(e);
    }

    /// <summary>Показать окно (не блокируя того, кто спрашивает: окно откроется сразу после текущего события).</summary>
    public static void Ask(Form owner, ConsentRequest req)
    {
        owner.BeginInvoke(() =>
        {
            if (req.IsCancelled || owner.IsDisposed) return;
            using var d = new DirectionConsentDialog(req);
            d.ShowDialog(owner);
        });
    }
}
