using StationEditor.Model;

namespace StationEditor.Ui;

public sealed class Camera
{
    public float X = 80, Y = 60, Z = 1;
}

public sealed class Band
{
    public int X1, Y1, X2, Y2;
}

public sealed class Moving
{
    public HashSet<string> Keys;
    public int Dx, Dy;
}

public sealed class RoutePreview
{
    public string Id;
    public HashSet<string> Tiles;
}

/// <summary>Режим испытания: занятые участки, заданные маршруты, открытые светофоры.</summary>
public sealed class TestState
{
    public HashSet<string> Occ = new();
    public List<Route> Set = new();
    public HashSet<string> RouteTiles = new();
    public HashSet<string> OpenSigs = new();
    public SigRef? Pending;
    public Dictionary<string, bool?> SavedPlus;
}

/// <summary>Состояние окна редактора (не сохраняется в файл станции).</summary>
public sealed class UiState
{
    public string Mode = "edit";              // edit | test
    public string Tool = "place";             // select | place | erase | brush | signal | text
    public string PlaceType = "straight";
    public readonly Dictionary<string, int> Variant = new();   // тип кубика → выбранный вариант
    public Camera Cam = new();
    public HashSet<string> Sel = new();
    public SigRef? SelSig;
    public bool ShowGrid = true, ShowSecColors = true, ShowSecNames = true;
    public Point? Hover;
    public Tile Ghost;
    public Color? HoverColor;
    public Band Band;
    public Moving Moving;
    public string FlashSec;
    public RoutePreview Preview;
    public string BrushSec = "";
    public string SigKind = "entry", SigDir = "R";
    public string Tab = "props";              // props | secs | routes | checks
    public string RouteFilter = "all";        // all | train | shunt
    public readonly TestState Test = new();

    public bool IsTest => Mode == "test";
}
