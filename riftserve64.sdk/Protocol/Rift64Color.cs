namespace RiftServe64.Sdk.Protocol;

public enum Rift64Color : byte
{
    Black = 0,
    White = 1,
    Red = 2,
    Cyan = 3,
    Purple = 4,
    Green = 5,
    Blue = 6,
    Yellow = 7,
    Orange = 8,
    Brown = 9,
    LightRed = 10,
    DarkGray = 11,
    MediumGray = 12,
    LightGreen = 13,
    LightBlue = 14,
    LightGray = 15
}

public enum Rift64DisplayMode : byte
{
    Text = 0,
    HiresBitmap = 1,
    MulticolorBitmap = 2,
    HiresChunky = 3
}

public enum Rift64ScrollDirection : byte
{
    Up = 0,
    Down = 1,
    Left = 2,
    Right = 3
}