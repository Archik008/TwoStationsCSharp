namespace StationApp.Ui;

// =====================================================================
//  ЦВЕТА СХЕМЫ И ОКНА ПО РЕЖИМАМ ИНТЕРФЕЙСА  [ДОБАВЛЕНО 28.10.2026]
//  Файл ОДИНАКОВЫЙ в программах АКЖТ и Алгабаса.
//   • Standard — как было (инженерный и учебный режимы);
//   • Medeu    — рабочий режим, как АРМ ДСП станции Медеу (тёмно-бирюзовый фон, светлые пути, жёлтые подписи путей);
//   • Classic  — павлодарский режим, как тренажёр ДСП «Енисей» (серый фон, чёрные пути).
// =====================================================================

public class SchemePalette
{
    public Color Bg, Track, Route, Occ, Releasing, LampOff, LampOffSimple, Text, Dimmed, SectionLabel, SwitchLabel, Title, Button;
    public string Name = "";

    public static readonly SchemePalette Standard = new()
    {
        Name = "Standard",
        Bg = Color.FromArgb(122, 166, 155), Track = Color.White, Route = Color.FromArgb(255, 235, 0), Occ = Color.FromArgb(225, 25, 25),
        Releasing = Color.FromArgb(255, 150, 40), LampOff = Color.FromArgb(30, 30, 30), LampOffSimple = Color.FromArgb(128, 128, 128),
        Text = Color.FromArgb(35, 55, 50), Dimmed = Color.FromArgb(150, 175, 168), SectionLabel = Color.Empty, SwitchLabel = Color.Empty,
        Title = Color.FromArgb(30, 45, 45), Button = Color.FromArgb(238, 238, 238),
    };

    public static readonly SchemePalette Medeu = new()
    {
        Name = "Medeu",
        Bg = Color.FromArgb(94, 132, 127), Track = Color.FromArgb(222, 226, 224), Route = Color.FromArgb(120, 230, 90), Occ = Color.FromArgb(225, 35, 35),
        Releasing = Color.FromArgb(255, 160, 50), LampOff = Color.FromArgb(150, 160, 158), LampOffSimple = Color.FromArgb(150, 160, 158),
        Text = Color.FromArgb(240, 245, 240), Dimmed = Color.FromArgb(140, 170, 165), SectionLabel = Color.FromArgb(255, 222, 60),
        SwitchLabel = Color.FromArgb(240, 245, 240), Title = Color.FromArgb(245, 248, 245), Button = Color.FromArgb(236, 238, 236),
    };

    public static readonly SchemePalette Classic = new()
    {
        Name = "Classic",
        Bg = Color.FromArgb(206, 206, 206), Track = Color.FromArgb(25, 25, 25), Route = Color.FromArgb(250, 250, 250), Occ = Color.FromArgb(240, 70, 70),
        Releasing = Color.FromArgb(255, 150, 40), LampOff = Color.FromArgb(70, 70, 70), LampOffSimple = Color.FromArgb(110, 110, 110),
        Text = Color.FromArgb(15, 15, 15), Dimmed = Color.FromArgb(150, 150, 150), SectionLabel = Color.FromArgb(20, 20, 20),
        SwitchLabel = Color.FromArgb(235, 200, 0), Title = Color.FromArgb(20, 20, 20), Button = Color.FromArgb(225, 225, 225),
    };

    public static SchemePalette For(UiMode m) => m switch
    {
        UiMode.Work => Medeu,
        UiMode.Classic => Classic,
        _ => Standard
    };

    /// <summary>Цвета текущего режима (UiSettings.Mode).</summary>
    public static SchemePalette Current => For(UiSettings.Mode);

    /// <summary>Перекрасить уже созданные элементы окна: фон и цвет заголовков старой палитры → новой.</summary>
    public static void Recolor(Control root, SchemePalette from, SchemePalette to)
    {
        if (from == to) return;
        void Walk(Control c)
        {
            if (c.BackColor.ToArgb() == from.Bg.ToArgb()) c.BackColor = to.Bg;
            else if (c.BackColor.ToArgb() == from.Button.ToArgb()) c.BackColor = to.Button;
            if (c.ForeColor.ToArgb() == from.Title.ToArgb()) c.ForeColor = to.Title;
            foreach (Control ch in c.Controls) Walk(ch);
        }
        Walk(root);
    }
}
