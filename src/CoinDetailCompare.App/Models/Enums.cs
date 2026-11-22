namespace CoinDetailCompare.App.Models;

public enum CoinSide
{
    Obverse,
    Reverse,
}

public enum SourceKind
{
    Match,
    Duplicate,
    Variant,
    Anomaly,
    Manual,
    Api,
}

public enum DeviceType
{
    Gpu,
    Cpu,
}

public enum VisionModel
{
    VitHPlus16,
    VitL16,
    VitB16,
}
