using StationApp.Akzht;
using StationApp.Core;
using StationApp.Post;

namespace StationApp.Model;

// =====================================================================
//  ВСЯ МОДЕЛЬ МАКЕТА: станция АКЖТ + перегон + путевой пост.
//  Одно ядро зависимостей (Interlocking), одни виртуальные поезда,
//  одна плата Arduino. Окна АКЖТ и поста — это два "вида" одной модели,
//  поэтому поезд сам переезжает с экрана на экран.
// =====================================================================

public class RailModel
{
    public readonly Interlocking IL = new();
    public readonly AkzhtLayout Akzht;
    public readonly PostLayout Post;
    public readonly TrainSim Trains;
    public readonly HardwareMap Hw;
    /// <summary>[ДОБАВЛЕНО 28.10.2026] Сообщения о чтении карты ламп (светофоры.txt) — в журнал при запуске окна.</summary>
    public readonly List<string> HwMessages;

    public RailModel(bool depot)
    {
        Akzht = new AkzhtLayout(IL, depot);
        Post = new PostLayout(IL, Akzht);
        Akzht.Link(Post);

        Trains = new TrainSim(IL, Next);
        Akzht.Trains = Trains;
        Post.Trains = Trains;

        Hw = AkzhtHardware.Build(depot);
        // [ИЗМЕНЕНО 28.10.2026] лампы АКЖТ и поста — из файла светофоры.txt (таблица распиновки, 13 байт);
        // PostHardware.Lamps больше не используются
        HwMessages = AkzhtHardware.LoadLamps(Hw, IL, out var frameBytes);
        ArduinoLink.FrameBytes = frameBytes;
        foreach (var (k, v) in PostHardware.Build().Servos) Hw.Servos[k] = v;
        foreach (var (k, v) in PostHardware.Build().Sensors) Hw.Sensors[k] = v;
        foreach (var (k, v) in PostHardware.Build().PinSensors) Hw.PinSensors[k] = v;

        IL.ComputeAspects();
    }

    /// <summary>Куда поезд поедет дальше — спрашиваем схему, которой принадлежит участок.</summary>
    public TrackStep Next(TrackSection s, Dir d) =>
        Akzht.Owns(s) ? Akzht.Next(s, d) : Post.Next(s, d);
}
