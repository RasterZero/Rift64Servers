namespace RiftWriter;

/// <summary>
/// Screen geometry, key codes, and border glyph constants.
/// </summary>
internal static class WriterConstants
{
    // Network
    public const string Host = "0.0.0.0";
    public const int Port = 8003;

    // Screen geometry
    public const int ScreenWidth = 40;
    public const int ScreenHeight = 25;
    public const int ViewportTop = 2;
    public const int ViewportBot = 22;
    public const int ViewportRows = 21;
    public const int ViewportCols = 40;
    public const int DocMaxCols = 80;
    public const int HScrollMargin = 0;
    public const int HScrollJump = 1;
    public const int VScrollMax = 8;
    public const int HScrollMax = 30;

    // Key codes (C64 PETSCII)
    public const byte KeyReturn = 13;
    public const byte KeyDel = 20;
    public const byte KeyInst = 148;
    public const byte KeyHome = 19;
    public const byte KeyClr = 147;
    public const byte KeyRunStop = 3;
    public const byte KeyCrsrUp = 145;
    public const byte KeyCrsrDown = 17;
    public const byte KeyCrsrLeft = 157;
    public const byte KeyCrsrRight = 29;
    public const byte KeyF1 = 133;
    public const byte KeyF2 = 137;
    public const byte KeyF3 = 134;
    public const byte KeyF4 = 138;
    public const byte KeyF5 = 135;
    public const byte KeyF6 = 139;
    public const byte KeyF7 = 136;
    public const byte KeyF8 = 140;
    public const byte KeyTab = 9;

    // PETSCII border characters (screen codes)
    public const byte BorderTL = 0x70;
    public const byte BorderTR = 0x6E;
    public const byte BorderBL = 0x6D;
    public const byte BorderBR = 0x7D;
    public const byte BorderH = 0x40;
    public const byte BorderV = 0x5D;

    // Color mappings (C64 control byte -> palette ID)
    public static readonly Dictionary<byte, byte> ColorMappings = new()
    {
        [5] = 1, [28] = 2, [30] = 5, [31] = 6,
        [144] = 0, [156] = 4, [158] = 7, [159] = 3,
        [129] = 8, [149] = 9, [150] = 10, [151] = 11,
        [152] = 12, [153] = 13, [154] = 14, [155] = 15,
    };
}
