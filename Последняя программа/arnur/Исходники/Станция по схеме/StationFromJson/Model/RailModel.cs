using StationApp.Core;
using StationApp.Scheme;
using Ed = StationEditor.Model;

namespace StationApp.Model;

// =====================================================================
//  ВСЯ МОДЕЛЬ: станция из файла .station.json  [ДОБАВЛЕНО 10.11.2026]
//  Ядро зависимостей (Interlocking), виртуальные поезда — те же, что у АКЖТ и Алгабаса.
//  Макета Arduino у таких станций пока нет — карта проводов пустая (лампы, стрелки, датчики только на экране).
// =====================================================================

public class RailModel
{
    public readonly Interlocking IL = new();
    public readonly JsonLayout Layout;
    public readonly TrainSim Trains;
    public readonly HardwareMap Hw = new();
    /// <summary>Файл схемы, из которого построена станция.</summary>
    public readonly string FilePath;

    public RailModel(string path)
    {
        FilePath = path;
        var st = Ed.StationFile.Load(path);
        Layout = new JsonLayout(IL, st, path);
        Trains = new TrainSim(IL, Layout.Next);
        Layout.Trains = Trains;
        IL.ComputeAspects();
    }
}
