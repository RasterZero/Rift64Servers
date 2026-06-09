namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// VIC-II 16 KB bank selector (0..3). The VIC sees only this 16 KB window for
/// screen RAM, character data, sprite data and bitmap data; absolute pointers
/// must always lie inside the chosen bank. The SDK's wire-protocol calls
/// accept this enum directly and convert to the (inverted) CIA2 $DD00 PA0/PA1
/// bits internally — use <see cref="VicMemoryLayout.CiaPortABits"/> only when
/// composing wire bytes by hand.
/// </summary>
public enum VicBank : byte
{
    /// <summary>Bank 0 — $0000-$3FFF. Char ROM shadow at $1000-$1FFF. RIFT64 firmware default.</summary>
    Bank0 = 0,
    /// <summary>Bank 1 — $4000-$7FFF. All RAM.</summary>
    Bank1 = 1,
    /// <summary>Bank 2 — $8000-$BFFF. Char ROM shadow at $9000-$9FFF.</summary>
    Bank2 = 2,
    /// <summary>Bank 3 — $C000-$FFFF. All RAM.</summary>
    Bank3 = 3
}

/// <summary>
/// 1 KB screen-RAM slot within a VIC bank. 16 slots × 1024 B = 16 KB bank.
/// Maps to the high nibble of the VIC <c>$D018</c> register.
/// </summary>
public enum VicScreenSlot : byte
{
    Slot0 = 0, Slot1 = 1, Slot2 = 2, Slot3 = 3,
    Slot4 = 4, Slot5 = 5, Slot6 = 6, Slot7 = 7,
    Slot8 = 8, Slot9 = 9, Slot10 = 10, Slot11 = 11,
    Slot12 = 12, Slot13 = 13, Slot14 = 14, Slot15 = 15
}

/// <summary>
/// 2 KB character-set slot within a VIC bank. 8 slots × 2048 B = 16 KB.
/// Maps to bits 1..3 of the VIC <c>$D018</c> register.
/// </summary>
public enum VicCharsetSlot : byte
{
    Slot0 = 0, Slot1 = 1, Slot2 = 2, Slot3 = 3,
    Slot4 = 4, Slot5 = 5, Slot6 = 6, Slot7 = 7
}

/// <summary>
/// 8 KB bitmap slot within a VIC bank. 2 slots — low half ($0000) or high
/// half ($2000) of the bank. Maps to bit 3 of <c>$D018</c>.
/// </summary>
public enum VicBitmapSlot : byte
{
    /// <summary>Bitmap at bank base + $0000 (overlaps char ROM in banks 0/2).</summary>
    Low = 0,
    /// <summary>Bitmap at bank base + $2000.</summary>
    High = 1
}

public static class VicMemoryLayout
{
    /// <summary>Bank size in bytes (16 KB).</summary>
    public const int BankSize = 0x4000;

    /// <summary>Screen-RAM slot size in bytes (1000 used + 24 padding = 1024).</summary>
    public const int ScreenSlotSize = 0x0400;

    /// <summary>Visible cells in a screen slot (40 × 25).</summary>
    public const int ScreenCellCount = 1000;

    /// <summary>Character-set slot size in bytes (2048).</summary>
    public const int CharsetSlotSize = 0x0800;

    /// <summary>Bitmap slot size in bytes (8000 used + 192 padding = 8192).</summary>
    public const int BitmapSlotSize = 0x2000;

    /// <summary>Visible bitmap byte count (40 × 25 × 8).</summary>
    public const int BitmapDataSize = 8000;

    /// <summary>Bytes per sprite (24 × 21 bits used + 1 padding byte).</summary>
    public const int SpriteSize = 64;

    /// <summary>Visible sprite payload size (63 bytes).</summary>
    public const int SpriteDataSize = 63;

    /// <summary>Color RAM (fixed, not bank-relative).</summary>
    public const ushort ColorRamAddress = 0xD800;

    /// <summary>Bank base address ($0000 / $4000 / $8000 / $C000).</summary>
    public static ushort BaseAddress(this VicBank bank) =>
        (ushort)((byte)bank * BankSize);

    /// <summary>
    /// CIA2 port-A value (low 2 bits) needed to select this bank — VIC uses
    /// the inverted PA0/PA1 lines.
    /// </summary>
    public static byte CiaPortABits(this VicBank bank) =>
        (byte)((~(byte)bank) & 0x03);

    /// <summary>Absolute address of a screen slot in the given bank.</summary>
    public static ushort Address(this VicScreenSlot slot, VicBank bank) =>
        (ushort)(bank.BaseAddress() + (byte)slot * ScreenSlotSize);

    /// <summary>Absolute address of a character-set slot in the given bank.</summary>
    public static ushort Address(this VicCharsetSlot slot, VicBank bank) =>
        (ushort)(bank.BaseAddress() + (byte)slot * CharsetSlotSize);

    /// <summary>Absolute address of a bitmap slot in the given bank.</summary>
    public static ushort Address(this VicBitmapSlot slot, VicBank bank) =>
        (ushort)(bank.BaseAddress() + ((byte)slot * BitmapSlotSize));

    /// <summary>Absolute address of one sprite-data slot (0..255) within a bank.</summary>
    public static ushort SpriteAddress(VicBank bank, byte pointer) =>
        (ushort)(bank.BaseAddress() + pointer * SpriteSize);

    /// <summary>
    /// True if the chosen character-set slot is hidden by the C64's character
    /// ROM shadow. In banks 0 and 2 the VIC sees char ROM at offsets
    /// $1000-$1FFF, so charset slots 2 and 3 cannot be used for custom fonts.
    /// </summary>
    public static bool IsHiddenByCharRom(VicBank bank, VicCharsetSlot slot) =>
        (bank == VicBank.Bank0 || bank == VicBank.Bank2) &&
        ((byte)slot == 2 || (byte)slot == 3);

    /// <summary>
    /// True if the chosen bitmap slot overlaps the character ROM shadow.
    /// Affects banks 0 and 2 with the <see cref="VicBitmapSlot.Low"/> slot.
    /// </summary>
    public static bool IsHiddenByCharRom(VicBank bank, VicBitmapSlot slot) =>
        (bank == VicBank.Bank0 || bank == VicBank.Bank2) &&
        slot == VicBitmapSlot.Low;

    /// <summary>
    /// Compose a <c>$D018</c> register byte from screen + charset slots
    /// (text mode) or screen + bitmap slot (bitmap mode).
    /// </summary>
    public static byte D018(VicScreenSlot screen, VicCharsetSlot charset) =>
        (byte)(((byte)screen << 4) | (((byte)charset & 0x07) << 1));

    public static byte D018(VicScreenSlot screen, VicBitmapSlot bitmap) =>
        (byte)(((byte)screen << 4) | ((byte)bitmap == 0 ? 0x00 : 0x08));
}

/// <summary>
/// Combined VIC-II memory layout: bank + screen slot + char/bitmap slot.
/// Yields the absolute upload addresses your data must occupy plus the
/// CIA2 / $D018 register values needed to make the VIC see them.
/// </summary>
public readonly record struct VicLayout
{
    public VicBank Bank { get; }
    public VicScreenSlot Screen { get; }
    public VicCharsetSlot? Charset { get; }
    public VicBitmapSlot? Bitmap { get; }

    private VicLayout(VicBank bank, VicScreenSlot screen, VicCharsetSlot? charset, VicBitmapSlot? bitmap)
    {
        Bank = bank;
        Screen = screen;
        Charset = charset;
        Bitmap = bitmap;
    }

    /// <summary>Layout for a text-mode screen (charset slot required).</summary>
    public static VicLayout ForText(VicBank bank, VicScreenSlot screen, VicCharsetSlot charset) =>
        new(bank, screen, charset, null);

    /// <summary>Layout for a bitmap-mode screen (color cell ram + bitmap slot).</summary>
    public static VicLayout ForBitmap(VicBank bank, VicScreenSlot screen, VicBitmapSlot bitmap) =>
        new(bank, screen, null, bitmap);

    public ushort BankBaseAddress => Bank.BaseAddress();
    public ushort ScreenAddress => Screen.Address(Bank);
    public ushort? CharsetAddress => Charset?.Address(Bank);
    public ushort? BitmapAddress => Bitmap?.Address(Bank);

    /// <summary>Sprite-pointer table address (last 8 bytes of the screen slot).</summary>
    public ushort SpritePointerTableAddress => (ushort)(ScreenAddress + 0x3F8);

    /// <summary>VIC <c>$D018</c> register value for this layout.</summary>
    public byte D018 => Charset.HasValue
        ? VicMemoryLayout.D018(Screen, Charset.Value)
        : VicMemoryLayout.D018(Screen, Bitmap!.Value);

    /// <summary>CIA2 port-A bits (low 2) selecting this bank.</summary>
    public byte CiaPortABits => Bank.CiaPortABits();

    /// <summary>True when graphics data in this slot is hidden by char ROM.</summary>
    public bool IsHiddenByCharRom => Charset.HasValue
        ? VicMemoryLayout.IsHiddenByCharRom(Bank, Charset.Value)
        : VicMemoryLayout.IsHiddenByCharRom(Bank, Bitmap!.Value);
}
