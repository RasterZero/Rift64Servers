namespace RiftServe64.Sdk.Protocol;

/// <summary>
/// VIC-II memory-aware upload helpers. These wrap <see cref="StoreMemoryCheckedAsync"/>
/// in chunked transfers and validate that the destination is a legal slot
/// for sprite / charset / bitmap / screen / colour-RAM data.
/// </summary>
public sealed partial class Rift64ProtocolClient
{
    /// <summary>Default per-chunk size for large checked uploads.</summary>
    public const int DefaultUploadChunkSize = 256;

    /// <summary>
    /// Chunked checked upload of an arbitrary block to any address. Splits
    /// the buffer into 256-byte (configurable) frames and invokes
    /// <see cref="StoreMemoryCheckedAsync"/> for each, awaiting ACK between
    /// chunks. Returns true only if every chunk was acknowledged.
    /// </summary>
    public async Task<bool> StoreMemoryLargeCheckedAsync(
        ushort baseAddress,
        ReadOnlyMemory<byte> data,
        TimeSpan? perChunkTimeout = null,
        int chunkSize = DefaultUploadChunkSize,
        CancellationToken cancellationToken = default)
    {
        if (chunkSize is < 1 or > 256)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be 1..256.");
        }

        var timeout = perChunkTimeout ?? TimeSpan.FromSeconds(5);
        var offset = 0;
        while (offset < data.Length)
        {
            var size = Math.Min(chunkSize, data.Length - offset);
            var chunk = data.Slice(offset, size);
            var ack = await StoreMemoryCheckedAsync(
                (ushort)(baseAddress + offset),
                chunk,
                timeout,
                cancellationToken).ConfigureAwait(false);
            if (ack != true) return false;
            offset += size;
        }
        return true;
    }

    /// <summary>
    /// Upload a single sprite frame (24×21 = 63 bytes; 64-byte slot) to the
    /// pointer slot <paramref name="pointer"/> within the given VIC bank.
    /// </summary>
    /// <remarks>
    /// The C64 sprite pointer (0..255) is bank-relative: the VIC reads sprite
    /// pixels from <c>BankBase + pointer * 64</c>. Pointer values in the screen
    /// slot's last 8 bytes ($3F8..$3FF) reference these slots.
    /// </remarks>
    public Task<bool> UploadSpriteAsync(
        VicBank bank,
        byte pointer,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length is not (VicMemoryLayout.SpriteSize or VicMemoryLayout.SpriteDataSize))
        {
            throw new ArgumentException(
                $"Sprite data must be {VicMemoryLayout.SpriteDataSize} or {VicMemoryLayout.SpriteSize} bytes (got {data.Length}).",
                nameof(data));
        }
        var address = VicMemoryLayout.SpriteAddress(bank, pointer);
        return StoreMemoryLargeCheckedAsync(address, data, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Upload a 2 KB character set into the chosen charset slot of a VIC bank.
    /// </summary>
    public Task<bool> UploadCharsetAsync(
        VicBank bank,
        VicCharsetSlot slot,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length != VicMemoryLayout.CharsetSlotSize)
        {
            throw new ArgumentException(
                $"Charset data must be exactly {VicMemoryLayout.CharsetSlotSize} bytes (got {data.Length}).",
                nameof(data));
        }
        if (VicMemoryLayout.IsHiddenByCharRom(bank, slot))
        {
            throw new InvalidOperationException(
                $"Charset {slot} in {bank} is hidden by the C64 character-ROM shadow ($1000-$1FFF). " +
                "Pick another slot or use bank 1 / 3.");
        }
        var address = slot.Address(bank);
        return StoreMemoryLargeCheckedAsync(address, data, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Upload an 8000-byte (or 8192-byte, padded) bitmap into the chosen
    /// bitmap slot of a VIC bank.
    /// </summary>
    public Task<bool> UploadBitmapAsync(
        VicBank bank,
        VicBitmapSlot slot,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length is not (VicMemoryLayout.BitmapDataSize or VicMemoryLayout.BitmapSlotSize))
        {
            throw new ArgumentException(
                $"Bitmap must be {VicMemoryLayout.BitmapDataSize} or {VicMemoryLayout.BitmapSlotSize} bytes (got {data.Length}).",
                nameof(data));
        }
        if (VicMemoryLayout.IsHiddenByCharRom(bank, slot))
        {
            throw new InvalidOperationException(
                $"Bitmap {slot} in {bank} overlaps the C64 character-ROM shadow. " +
                "Pick the other slot or use bank 1 / 3.");
        }
        var address = slot.Address(bank);
        return StoreMemoryLargeCheckedAsync(address, data, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Upload screen RAM (1000 visible cells; 1024-byte slot) into the chosen
    /// screen slot of a VIC bank.
    /// </summary>
    public Task<bool> UploadScreenRamAsync(
        VicBank bank,
        VicScreenSlot slot,
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length is not (VicMemoryLayout.ScreenCellCount or VicMemoryLayout.ScreenSlotSize))
        {
            throw new ArgumentException(
                $"Screen data must be {VicMemoryLayout.ScreenCellCount} or {VicMemoryLayout.ScreenSlotSize} bytes (got {data.Length}).",
                nameof(data));
        }
        var address = slot.Address(bank);
        return StoreMemoryLargeCheckedAsync(address, data, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Upload colour RAM (1000 visible cells, low nibble per cell) to the
    /// fixed VIC colour-RAM region at $D800.
    /// </summary>
    public Task<bool> UploadColorRamAsync(
        ReadOnlyMemory<byte> data,
        CancellationToken cancellationToken = default)
    {
        if (data.Length is not (VicMemoryLayout.ScreenCellCount or VicMemoryLayout.ScreenSlotSize))
        {
            throw new ArgumentException(
                $"Colour data must be {VicMemoryLayout.ScreenCellCount} or {VicMemoryLayout.ScreenSlotSize} bytes (got {data.Length}).",
                nameof(data));
        }
        return StoreMemoryLargeCheckedAsync(VicMemoryLayout.ColorRamAddress, data, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Write the eight sprite-pointer bytes at the tail of the chosen screen
    /// slot ($3F8..$3FF). Each entry references a 64-byte sprite slot within
    /// the same VIC bank.
    /// </summary>
    public async Task<bool> WriteSpritePointersAsync(
        VicBank bank,
        VicScreenSlot screenSlot,
        ReadOnlyMemory<byte> pointers,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (pointers.Length is < 1 or > 8)
        {
            throw new ArgumentException("Provide 1..8 sprite-pointer bytes.", nameof(pointers));
        }
        var address = (ushort)(screenSlot.Address(bank) + 0x3F8);
        var ack = await StoreMemoryCheckedAsync(
            address,
            pointers,
            timeout ?? TimeSpan.FromSeconds(2),
            cancellationToken).ConfigureAwait(false);
        return ack == true;
    }

    /// <summary>
    /// Apply a <see cref="VicLayout"/> to the C64 by issuing the appropriate
    /// DISPLAY MODE command. Bank-bits, $D018 and the chosen mode flags
    /// (text vs. multicolor bitmap) are derived from the layout.
    /// </summary>
    /// <remarks>
    /// For a text layout the firmware relocates its CPU screen-draw base to the
    /// layout's screen slot, so all subsequent text output lands in
    /// <see cref="VicLayout.ScreenAddress"/>. Upload the charset to
    /// <see cref="VicLayout.CharsetAddress"/> first if it is not already present
    /// in the target bank.
    /// </remarks>
    public Task<bool?> ApplyVicLayoutAsync(
        VicLayout layout,
        Rift64Color border,
        Rift64Color background,
        TimeSpan timeout,
        bool multicolor = false,
        byte d011 = 0x1B,
        byte d016 = 0x08,
        CancellationToken cancellationToken = default)
    {
        Rift64DisplayMode mode;
        byte effectiveD011 = d011;
        byte effectiveD016 = d016;

        if (layout.Bitmap.HasValue)
        {
            mode = multicolor ? Rift64DisplayMode.MulticolorBitmap : Rift64DisplayMode.HiresBitmap;
            effectiveD011 = (byte)(d011 | 0x20);            // BMM = 1
            effectiveD016 = multicolor ? (byte)(d016 | 0x10) : (byte)(d016 & ~0x10);
        }
        else
        {
            mode = Rift64DisplayMode.Text;
            effectiveD011 = (byte)(d011 & ~0x20);           // BMM = 0
            effectiveD016 = (byte)(d016 & ~0x10);           // MCM = 0
        }

        return SetDisplayModeAsync(
            mode,
            layout.Bank,
            layout.D018,
            effectiveD011,
            effectiveD016,
            border,
            background,
            timeout,
            cancellationToken);
    }

    /// <summary>
    /// Relocate the live text display to a new VIC bank / screen slot in one
    /// step. Requires firmware with the relocatable screen-draw base: the
    /// DISPLAY MODE command repoints both the VIC and the firmware's CPU draw
    /// base to <paramref name="layout"/>'s screen slot, so all subsequent text,
    /// cursor, scroll, window, clear, save/restore and sprite-pointer writes
    /// follow the new screen.
    /// </summary>
    /// <remarks>
    /// The <paramref name="layout"/> must be a text layout (created with
    /// <see cref="VicLayout.ForText"/>); bitmap layouts are rejected. When
    /// <paramref name="charset"/> is supplied it is uploaded to the layout's
    /// charset slot before the display mode switch, so the relocated screen has
    /// a valid font. The newly selected screen slot is then cleared via the
    /// CLEAR SCREEN command. Colour RAM remains fixed at $D800.
    /// </remarks>
    public async Task<bool?> RelocateTextDisplayAsync(
        VicLayout layout,
        Rift64Color border,
        Rift64Color background,
        ReadOnlyMemory<byte>? charset = null,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (!layout.Charset.HasValue)
        {
            throw new ArgumentException(
                "RelocateTextDisplayAsync requires a text layout (VicLayout.ForText).",
                nameof(layout));
        }
        if (layout.IsHiddenByCharRom)
        {
            throw new InvalidOperationException(
                $"Charset slot {layout.Charset} in {layout.Bank} is hidden by the C64 " +
                "character-ROM shadow ($1000-$1FFF). Pick another slot or use bank 1 / 3.");
        }

        var effectiveTimeout = timeout ?? DefaultAckTimeout;

        if (charset is { } charsetData)
        {
            var charsetAck = await UploadCharsetAsync(
                layout.Bank,
                layout.Charset.Value,
                charsetData,
                cancellationToken).ConfigureAwait(false);
            if (!charsetAck)
            {
                return false;
            }
        }

        var modeAck = await ApplyVicLayoutAsync(
            layout,
            border,
            background,
            effectiveTimeout,
            cancellationToken: cancellationToken).ConfigureAwait(false);
        if (modeAck != true)
        {
            return modeAck;
        }

        await ClearScreenAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}

