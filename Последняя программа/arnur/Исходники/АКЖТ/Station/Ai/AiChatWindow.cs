using StationApp.Core;
using StationApp.Ui;

namespace StationApp.Ai;

// =====================================================================
//  ОКНО ЖИ-ПОМОЩНИКА  [ДОБАВЛЕНО 28.10.2026, по панели «ЖД AI» из Station LAB (Ui/WikiAiHost.cs)]
//  Отдельное окно поверх окна станции: его можно двигать и растягивать, оно не ломает раскладку режимов.
//  Открывается только кнопкой «ИИ» вверху окна станции ([ИЗМЕНЕНО 09.11.2026] само при запуске не открывается).
//  Закрытие только прячет окно — разговор сохраняется.
//  Когда программа отказывает студенту (красная строка журнала), подсказка появляется здесь сама.
// =====================================================================

sealed class AiChatWindow : Form
{
    static readonly Color Back = Color.FromArgb(28, 36, 48), UserC = Color.FromArgb(215, 227, 244), AiC = Color.FromArgb(183, 227, 192),
                          DimC = Color.FromArgb(140, 150, 165), WarnC = Color.FromArgb(255, 196, 110);

    readonly AiAssistant ai = new();
    readonly Label header = new();
    readonly RichTextBox output = new();
    readonly TextBox input = new();
    readonly Button send = new();
    bool busy;
    string headerInfo = "";
    string lastRefusal = "";
    DateTime lastRefusalAt;

    /// <summary>Пришла подсказка при отказе, а окно скрыто (для значка на кнопке «ЖИ»).</summary>
    public event Action Unread;

    public AiChatWindow()
    {
        Text = "ИИ-помощник";   // [ИЗМЕНЕНО 06.11.2026] переводится: kk «ЖИ-көмекші», en «AI assistant»
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Size = new Size(520, 420);
        MinimumSize = new Size(320, 220);
        BackColor = Back;
        Font = new Font("Segoe UI", 10f);

        output.Dock = DockStyle.Fill;
        output.ReadOnly = true;
        output.BorderStyle = BorderStyle.None;
        output.BackColor = Back;
        output.ForeColor = AiC;
        output.Font = new Font("Segoe UI", 10f);
        output.ScrollBars = RichTextBoxScrollBars.Vertical;
        Controls.Add(output);

        header.Dock = DockStyle.Top;
        header.Height = 24;
        header.BackColor = Color.FromArgb(18, 24, 32);
        header.ForeColor = Color.FromArgb(120, 190, 255);
        header.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
        header.TextAlign = ContentAlignment.MiddleLeft;
        header.Padding = new Padding(6, 0, 0, 0);
        header.Tag = "nolang";
        header.Text = L.T("ИИ");
        Controls.Add(header);
        // [ДОБАВЛЕНО 06.11.2026] название помощника в заголовке — на языке интерфейса (ИИ / ЖИ / AI)
        L.Changed += OnLangChanged;
        Disposed += (_, _) => L.Changed -= OnLangChanged;

        var bar = new Panel { Dock = DockStyle.Bottom, Height = 34, BackColor = Color.FromArgb(18, 24, 32), Padding = new Padding(4) };
        input.Dock = DockStyle.Fill;
        input.Font = new Font("Segoe UI", 11f);
        input.PlaceholderText = "Сұрақ / вопрос: маршрутты қалай беремін? как задать " + AiProfile.RouteExampleRu + "?";
        input.KeyDown += (_, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; Send(); } };
        send.Dock = DockStyle.Right;
        send.Width = 50;
        send.Text = "▶";
        send.FlatStyle = FlatStyle.Flat;
        send.ForeColor = Color.White;
        send.BackColor = Color.FromArgb(0, 120, 215);
        send.Click += (_, _) => Send();
        bar.Controls.Add(input);
        bar.Controls.Add(send);
        Controls.Add(bar);

        AiStation.Refused += OnRefused;
        Shown += (_, _) => input.Focus();
        _ = InitAsync();
    }

    void OnLangChanged()
    {
        if (IsDisposed) return;
        header.Text = L.T("ИИ") + headerInfo;
    }

    /// <summary>Поставить окно в правый нижний угол экрана, где окно станции.</summary>
    public void PlaceNear(Form owner)
    {
        var wa = Screen.FromControl(owner).WorkingArea;
        if (owner.Bounds.Width > wa.Width * 1.5)        // окно растянуто на два монитора — ставим на правый
            wa = Screen.AllScreens.OrderByDescending(s => s.Bounds.Right).First().WorkingArea;
        Location = new Point(wa.Right - Width - 24, wa.Bottom - Height - 24);
    }

    async Task InitAsync()
    {
        Append("Іске қосылып жатыр… / Запуск…", DimC);
        var wikiTask = ai.PingWikiAsync();
        bool llm = await ai.StartAsync();
        bool wiki = await wikiTask;
        if (IsDisposed) return;
        headerInfo = $" · білім: {ai.KnowledgeCount} · Wikipedia: {(wiki ? "on" : "off")} · нейросеть: {(llm ? "on (" + ai.LlmWhere + ")" : "off")}";
        header.Text = L.T("ИИ") + headerInfo;
        output.Clear();
        Append("Сәлем! Мен " + AiProfile.Station + " станциясының ЖИ-көмекшісімін. Бағдарлама, маршрут, светофор, стрелка туралы немесе темір жол терминдері туралы сұраңыз — қазақша немесе орысша. " +
               "Мысалы: «маршрутты қалай беремін?», «" + AiProfile.RouteExampleKk + "», «Н1 светофоры не үшін?», «автоблокировка деген не?»", AiC);
        Append("Здравствуйте! Я ИИ-помощник станции " + AiProfile.Station + ". Спросите про программу, маршруты, светофоры, стрелки или термины железной дороги — по-русски или қазақша. " +
               "Наведите мышь на объект схемы и спросите «что это?». Если программа откажет, подскажу здесь, что делать.", AiC);
        if (!llm) Append("Нейросеть (Ollama) қосылмаған — дайын мәтіндермен жауап беремін. / Нейросеть не подключена — отвечаю готовыми текстами.", DimC);
    }

    void OnRefused(string text)
    {
        if (IsDisposed || !IsHandleCreated) return;
        if (InvokeRequired) { try { BeginInvoke(new Action(() => OnRefused(text))); } catch { } return; }
        if (text == lastRefusal && (DateTime.Now - lastRefusalAt).TotalSeconds < 5) return;
        lastRefusal = text;
        lastRefusalAt = DateTime.Now;
        Append(ai.RefusalHint(text), WarnC);
        if (!Visible) Unread?.Invoke();
    }

    /// <summary>Задать вопрос из кода (кнопки-подсказки, тесты).</summary>
    public void Ask(string q) { input.Text = q; Send(); }

    async void Send()
    {
        if (busy) return;
        var q = input.Text.Trim();
        if (q.Length == 0) return;
        input.Clear();
        busy = true;
        send.Enabled = false;
        Append("▶ " + q, UserC);
        int mark = output.TextLength;
        Append(AiText.DetectLang(q) == AiLang.Kk ? "… іздеп жатырмын" : "… ищу", DimC);
        string answer;
        try { answer = await ai.AskAsync(q); }
        catch (Exception ex) { answer = "Қате / ошибка: " + ex.Message; }
        if (IsDisposed) return;
        output.ReadOnly = false;                    // только для чтения RichTextBox не даёт стереть «… ищу»
        output.Select(mark, output.TextLength - mark);
        output.SelectedText = "";
        output.ReadOnly = true;
        Append(answer, AiC);
        busy = false;
        send.Enabled = true;
        input.Focus();
    }

    void Append(string text, Color color)
    {
        if (IsDisposed) return;
        output.SelectionStart = output.TextLength;
        output.SelectionLength = 0;
        output.SelectionColor = color;
        output.AppendText(text + "\n\n");
        output.SelectionStart = output.TextLength;
        output.ScrollToCaret();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); return; }
        base.OnFormClosing(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { AiStation.Refused -= OnRefused; ai.Dispose(); }
        base.Dispose(disposing);
    }
}
