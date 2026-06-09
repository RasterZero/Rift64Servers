using RiftServe64.Sdk.Protocol;

/// <summary>
/// Demonstrates the client-side metatile renderer, which expands a compact map
/// of tile IDs into screen characters entirely on the C64.
/// <para>
/// Rather than sending one screen code per cell, the host uploads a small map
/// plus tile-definition pages to the safe $4000-$68FF zone, then issues a single
/// <c>D</c> command describing the mode (1x1 raw, 2x2, or 3x3 metatiles), the
/// source/target addresses and the visible window. The firmware walks the map
/// and writes the corresponding glyphs and Color RAM, dramatically cutting
/// bandwidth. Combined with hardware region scrolling and single-edge refills,
/// this drives a smoothly scrolling viewport over a larger virtual map.
/// </para>
/// </summary>
public sealed class MetatileDemoExample : IRift64MenuExample
{
    public char Key => 'C';
    public string MenuLabel => "Metatile";

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 12: METATILE RENDERER (D)", Rift64Color.LightGreen, cancellationToken);

        ushort MAP1_ADDR = 0x4000;
        ushort MAP2_ADDR = 0x4100;
        ushort MAP3_ADDR = 0x4200;
        ushort COL1_ADDR = 0x4300;
        ushort COLTAB_ADDR = 0x4400;
        byte META2_BASE_HI = 0x50;
        byte META3_BASE_HI = 0x60;

        // ---- Mode 1 map: 16x16 of distinct screen codes ----
        var map1 = new byte[256];
        for (int r = 0; r < 16; r++)
        {
            for (int c = 0; c < 16; c++)
            {
                map1[r * 16 + c] = (byte)(((r * 16 + c) & 0x7F) | 0x01);
            }
        }

        // ---- Mode 2 map: 8x8 of tile IDs 0..3 in a checkerboard layout ----
        var map2 = new byte[64];
        for (int r = 0; r < 8; r++)
        {
            for (int c = 0; c < 8; c++)
            {
                map2[r * 8 + c] = (byte)(((r / 2) * 2 + (c / 2)) & 0x03);
            }
        }

        // ---- Mode 3 map: 6x6 of tile IDs 0..2 in a striped layout ----
        var map3 = new byte[36];
        for (int r = 0; r < 6; r++)
        {
            for (int c = 0; c < 6; c++)
            {
                map3[r * 6 + c] = (byte)((c / 2) % 3);
            }
        }

        // ---- Mode 1 Color Map (rainbow rows) ----
        var rainbow = new byte[] { 2, 8, 9, 7, 5, 3, 14, 6, 4, 10, 12, 15, 1, 13, 11, 0 };
        var col1Map = new byte[256];
        for (int r = 0; r < 16; r++)
        {
            for (int c = 0; c < 16; c++)
            {
                col1Map[r * 16 + c] = rainbow[r];
            }
        }

        // ---- Color per tile ID table (256B) ----
        var coltab = new byte[256];
        Array.Fill(coltab, (byte)14); // light blue default
        coltab[0] = 1;  // Tile 0: white
        coltab[1] = 3;  // Tile 1: cyan
        coltab[2] = 7;  // Tile 2: yellow
        coltab[3] = 10; // Tile 3: light-red
        coltab[4] = 5;  // Tile 4: green
        coltab[5] = 14; // Tile 5: lt-blue
        coltab[6] = 4;  // Tile 6: purple
        coltab[7] = 15; // Tile 7: lt-grey
        coltab[8] = 9;  // Tile 8: brown

        // Slot page generator
        byte[] SlotPage(int slotIndex, int tileCount, byte baseCode)
        {
            var page = new byte[256];
            Array.Fill(page, (byte)0x20); // space padding
            for (int t = 0; t < tileCount; t++)
            {
                page[t] = (byte)((baseCode + t * 4 + slotIndex) & 0xFF);
            }
            return page;
        }

        var meta2_tl = SlotPage(0, 4, 1);
        var meta2_tr = SlotPage(1, 4, 1);
        var meta2_bl = SlotPage(2, 4, 1);
        var meta2_br = SlotPage(3, 4, 1);

        var meta3_pages = new byte[9][];
        for (int s = 0; s < 9; s++)
        {
            meta3_pages[s] = SlotPage(s, 3, 1);
        }

        // 1. Upload Data
        await client.WriteAtAsync(0, 22, "UPLOADING MAP + TILE DATA...   ", Rift64Color.Yellow, cancellationToken);

        await UploadPagesAsync(client, MAP1_ADDR, map1, cancellationToken);
        await UploadPagesAsync(client, MAP2_ADDR, map2, cancellationToken);
        await UploadPagesAsync(client, MAP3_ADDR, map3, cancellationToken);
        await UploadPagesAsync(client, COL1_ADDR, col1Map, cancellationToken);
        await UploadPagesAsync(client, COLTAB_ADDR, coltab, cancellationToken);
        await UploadPagesAsync(client, 0x5000, meta2_tl, cancellationToken);
        await UploadPagesAsync(client, 0x5100, meta2_tr, cancellationToken);
        await UploadPagesAsync(client, 0x5200, meta2_bl, cancellationToken);
        await UploadPagesAsync(client, 0x5300, meta2_br, cancellationToken);
        for (ushort i = 0; i < 9; i++)
        {
            await UploadPagesAsync(client, (ushort)(0x6000 + i * 0x100), meta3_pages[i], cancellationToken);
        }

        await client.WriteAtAsync(0, 22, "UPLOAD COMPLETE                ", Rift64Color.LightGreen, cancellationToken);
        await Task.Delay(500, cancellationToken);

        // 2. Mode 1 raw character mode
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "MODE 1: RAW 1x1", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "MAP BYTE = SCREEN CODE", Rift64Color.White, cancellationToken);

        await client.DrawMetatileAsync(mode: 1, mapAddr: MAP1_ADDR, mapW: 16, mapH: 16,
            metaHi: 0, targetAddr: ScreenAddress(3, 3), stride: 40,
            winW: 16, winH: 16, x: 0, y: 0, offX: 0, offY: 0, cancellationToken: cancellationToken);
        await client.WriteAtAsync(0, 21, "16x16 FULL MAP AT (3,3)", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 23, "PRESS KEY FOR 10x6 WINDOW.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 10x6 sub-window starting inside the map
        await client.ClearScreenAsync(cancellationToken);
        await client.DrawMetatileAsync(mode: 1, mapAddr: MAP1_ADDR, mapW: 16, mapH: 16,
            metaHi: 0, targetAddr: ScreenAddress(0, 0), stride: 40,
            winW: 10, winH: 6, x: 4, y: 4, offX: 0, offY: 0, cancellationToken: cancellationToken);
        await client.WriteAtAsync(0, 10, "10x6 WINDOW AT MAP (4,4)", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 12, "PRESS KEY FOR EDGE CLIPPING.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 12x8 window straddling right + bottom edges
        await client.ClearScreenAsync(cancellationToken);
        await client.DrawMetatileAsync(mode: 1, mapAddr: MAP1_ADDR, mapW: 16, mapH: 16,
            metaHi: 0, targetAddr: ScreenAddress(0, 0), stride: 40,
            winW: 12, winH: 8, x: 10, y: 12, offX: 0, offY: 0,
            fillChar: 46, cancellationToken: cancellationToken); // '.'
        await client.WriteAtAsync(0, 10, "EDGE CLIP: WINDOW PAST RIGHT+BOTTOM", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 11, "FILL CHAR = . (DOT)", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 13, "PRESS KEY FOR MODE 2.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 3. Mode 2: 2x2 metatiles
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "MODE 2: 2x2 METATILES", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "TILE 0=ABCD 1=EFGH 2=IJKL 3=MNOP", Rift64Color.White, cancellationToken);

        await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
            metaHi: META2_BASE_HI, targetAddr: ScreenAddress(3, 3),
            stride: 40, winW: 16, winH: 16, x: 0, y: 0, offX: 0, offY: 0, cancellationToken: cancellationToken);
        await client.WriteAtAsync(0, 21, "FULL EXPANSION 16x16 (OFF=0,0)", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 23, "PRESS KEY FOR OFFSET JUMPS.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // Cycle (OffX, OffY)
        var offsets = new[] { (1, 0), (0, 1), (1, 1) };
        foreach (var (ox, oy) in offsets)
        {
            await client.ClearScreenAsync(cancellationToken);
            await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
                metaHi: META2_BASE_HI, targetAddr: ScreenAddress(3, 3),
                stride: 40, winW: 16, winH: 16, x: 0, y: 0, offX: (byte)ox, offY: (byte)oy, cancellationToken: cancellationToken);
            await client.WriteAtAsync(0, 21, $"OFFX={ox} OFFY={oy} - SHIFTED START", Rift64Color.Cyan, cancellationToken);
            await Task.Delay(1000, cancellationToken);
        }

        // 4. Mode 3: 3x3 metatiles
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "MODE 3: 3x3 METATILES", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "TILE 0=A..I 1=J..R 2=S..[ (9 SLOTS)", Rift64Color.White, cancellationToken);

        await client.DrawMetatileAsync(mode: 3, mapAddr: MAP3_ADDR, mapW: 6, mapH: 6,
            metaHi: META3_BASE_HI, targetAddr: ScreenAddress(2, 2),
            stride: 40, winW: 18, winH: 18, x: 0, y: 0, offX: 0, offY: 0, cancellationToken: cancellationToken);
        await client.WriteAtAsync(0, 22, "FULL 18x18 (OFF=0,0)", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 24, "PRESS KEY FOR COLOR SHOWCASE.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 5. Colour Modes Showcase: NONE / FILL / MAP
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 12: COLOUR MODES", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "NONE / FILL / MAP - SAME GEOMETRY", Rift64Color.White, cancellationToken);

        byte panelW = 8;
        byte panelH = 6;

        await client.WriteAtAsync(0, 4, "MODE 1: NONE    FILL=YEL   MAP=ROWS", Rift64Color.Yellow, cancellationToken);

        // Pre-paint NONE panel in white to prove NONE leaves whatever is there
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, cancellationToken);
        for (byte r = 0; r < panelH; r++)
        {
            await client.SetCursorAsync(2, (byte)(6 + r), cancellationToken);
            await client.WriteTextAsync("        ", cancellationToken);
        }

        // Draw panels (Mode 1)
        await client.DrawMetatileAsync(mode: 1, mapAddr: MAP1_ADDR, mapW: 16, mapH: 16,
            metaHi: 0, targetAddr: ScreenAddress(2, 6), stride: 40,
            winW: panelW, winH: panelH, x: 0, y: 0, offX: 0, offY: 0,
            colorMode: 0, cancellationToken: cancellationToken);

        await client.DrawMetatileAsync(mode: 1, mapAddr: MAP1_ADDR, mapW: 16, mapH: 16,
            metaHi: 0, targetAddr: ScreenAddress(14, 6), stride: 40,
            winW: panelW, winH: panelH, x: 0, y: 0, offX: 0, offY: 0,
            colorMode: 1, colorTgtAddr: (ushort)(0xD800 + 6 * 40 + 14),
            colorFill: 7, cancellationToken: cancellationToken); // Yellow fill

        await client.DrawMetatileAsync(mode: 1, mapAddr: MAP1_ADDR, mapW: 16, mapH: 16,
            metaHi: 0, targetAddr: ScreenAddress(26, 6), stride: 40,
            winW: panelW, winH: panelH, x: 0, y: 0, offX: 0, offY: 0,
            colorMode: 2, colorTgtAddr: (ushort)(0xD800 + 6 * 40 + 26),
            colorSrcAddr: COL1_ADDR, cancellationToken: cancellationToken); // Map rainbow rows

        // Mode 2 panels
        await client.WriteAtAsync(0, 13, "MODE 2: NONE    FILL=CYN   MAP=TILEHUE", Rift64Color.Yellow, cancellationToken);

        await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
            metaHi: META2_BASE_HI, targetAddr: ScreenAddress(2, 15), stride: 40,
            winW: 8, winH: 6, x: 0, y: 0, offX: 0, offY: 0,
            colorMode: 0, cancellationToken: cancellationToken);

        await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
            metaHi: META2_BASE_HI, targetAddr: ScreenAddress(14, 15), stride: 40,
            winW: 8, winH: 6, x: 0, y: 0, offX: 0, offY: 0,
            colorMode: 1, colorTgtAddr: (ushort)(0xD800 + 15 * 40 + 14),
            colorFill: 3, cancellationToken: cancellationToken); // Cyan fill

        await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
            metaHi: META2_BASE_HI, targetAddr: ScreenAddress(26, 15), stride: 40,
            winW: 8, winH: 6, x: 0, y: 0, offX: 0, offY: 0,
            colorMode: 2, colorTgtAddr: (ushort)(0xD800 + 15 * 40 + 26),
            colorSrcAddr: COLTAB_ADDR, cancellationToken: cancellationToken); // Color per tile ID

        await client.WriteAtAsync(0, 23, "PRESS KEY FOR PER-CELL COLOUR.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 5b. PERCELL — mode 2 with 4 distinct colours per metatile id
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 12: PERCELL COLOUR (MODE 2)", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "EACH METATILE ID = 4 INDEPENDENT COLOURS", Rift64Color.White, cancellationToken);

        // Authoring per-tile palette: (TopLeft, TopRight, BottomLeft, BottomRight).
        // Tile ids 0..3 used in MAP2_ADDR; rest defaults to light blue (14).
        var perTilePalette = new (byte TL, byte TR, byte BL, byte BR)[]
        {
            (1,  2,  3,  4),  // tile 0: white  red       cyan         purple
            (5,  6,  7,  8),  // tile 1: green  blue      yellow       orange
            (9, 10, 11, 12),  // tile 2: brown  lt-red    dk-grey      mid-grey
            (13, 14, 15, 0),  // tile 3: lt-grn lt-blue   lt-grey      black
        };
        var percellBank = Rift64ProtocolClient.BuildMode2PerCellColorBank(perTilePalette, defaultColor: 14);

        // Bank must be page-aligned. Place at $4800 (4 pages = 1 KB through $4BFF).
        const ushort PERCELL_BANK_ADDR = 0x4800;
        await UploadPagesAsync(client, PERCELL_BANK_ADDR, percellBank, cancellationToken);

        // Draw a 16x16 panel of mode-2 metatiles (full 8x8 map expanded), with
        // PERCELL colour. Each 2x2 metatile shows its 4-colour palette.
        await client.DrawMetatileAsync(
            mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
            metaHi: META2_BASE_HI,
            targetAddr: ScreenAddress(3, 3), stride: 40,
            winW: 16, winH: 16, x: 0, y: 0, offX: 0, offY: 0,
            color: MetatileColorMode.MapPerCell,
            colorSrcAddr: PERCELL_BANK_ADDR,
            colorTgtAddr: (ushort)(0xD800 + 3 * 40 + 3),
            cancellationToken: cancellationToken);

        await client.WriteAtAsync(0, 21, "EACH 2x2 BLOCK SHOWS 4 DIFFERENT", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 22, "COLOURS - ONE PER CHILD CELL.", Rift64Color.Cyan, cancellationToken);
        await client.WriteAtAsync(0, 24, "PRESS KEY TO START VIEWPORT SCROLL.", Rift64Color.Yellow, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);

        // 6. Viewport Scroll automated real-time testing
        await client.ClearScreenAsync(cancellationToken);
        await client.WriteAtAsync(0, 0, "EXAMPLE 12: VIEWPORT SCROLL (G+D)", Rift64Color.LightGreen, cancellationToken);
        await client.WriteAtAsync(0, 1, "AUTOMATED REALTIME SCROLL & EDGE REFILL", Rift64Color.White, cancellationToken);

        byte view_x = 4;
        byte view_y = 5;
        byte view_w = 16;
        byte view_h = 12;
        ushort SCROLL_COL_TGT = (ushort)(0xD800 + view_y * 40 + view_x);

        int cam_x = 0;
        int cam_y = 0;

        (int mapVal, byte offVal) CamToMap(int c)
        {
            return (c / 2, (byte)(c % 2));
        }

        // Draw initial viewport window
        var (mx_init, ox_init) = CamToMap(cam_x);
        var (my_init, oy_init) = CamToMap(cam_y);

        await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
            metaHi: META2_BASE_HI, targetAddr: ScreenAddress(view_x, view_y), stride: 40,
            winW: view_w, winH: view_h, x: (byte)mx_init, y: (byte)my_init, offX: ox_init, offY: oy_init,
            fillChar: 46, colorMode: 2, colorTgtAddr: SCROLL_COL_TGT, colorSrcAddr: COLTAB_ADDR,
            cancellationToken: cancellationToken);

        await Task.Delay(500, cancellationToken);

        // Define moves: (scroll_direction, dx, dy, steps)
        var moves = new[]
        {
            (Rift64ScrollDirection.Left,  1,  0, 4), // Camera moves right, world scrolls left
            (Rift64ScrollDirection.Up,    0,  1, 3), // Camera moves down, world scrolls up
            (Rift64ScrollDirection.Right, -1, 0, 4), // Camera moves left, world scrolls right
            (Rift64ScrollDirection.Down,  0, -1, 3)  // Camera moves up, world scrolls down
        };

        var dirNames = new[] { "UP", "DOWN", "LEFT", "RIGHT" };

        foreach (var (direction, dx, dy, steps) in moves)
        {
            for (int step = 0; step < steps; step++)
            {
                cam_x += dx;
                cam_y += dy;

                // 1. Hardware Scroll of Viewport Rect on C64
                await client.ScrollRegionAsync(view_x, view_y, view_w, view_h, direction, cancellationToken);

                // 2. Refill the newly exposed edge with D winW=1 or winH=1
                var (mx_step, ox_step) = CamToMap(cam_x);
                var (my_step, oy_step) = CamToMap(cam_y);

                if (direction == Rift64ScrollDirection.Up)
                {
                    // Bottom row exposed
                    var (emy, eoy) = CamToMap(cam_y + view_h - 1);
                    await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
                        metaHi: META2_BASE_HI, targetAddr: ScreenAddress(view_x, (byte)(view_y + view_h - 1)), stride: 40,
                        winW: view_w, winH: 1, x: (byte)mx_step, y: (byte)emy, offX: ox_step, offY: eoy,
                        fillChar: 46, colorMode: 2, colorTgtAddr: (ushort)(0xD800 + (view_y + view_h - 1) * 40 + view_x),
                        colorSrcAddr: COLTAB_ADDR, cancellationToken: cancellationToken);
                }
                else if (direction == Rift64ScrollDirection.Down)
                {
                    // Top row exposed
                    await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
                        metaHi: META2_BASE_HI, targetAddr: ScreenAddress(view_x, view_y), stride: 40,
                        winW: view_w, winH: 1, x: (byte)mx_step, y: (byte)my_step, offX: ox_step, offY: oy_step,
                        fillChar: 46, colorMode: 2, colorTgtAddr: SCROLL_COL_TGT,
                        colorSrcAddr: COLTAB_ADDR, cancellationToken: cancellationToken);
                }
                else if (direction == Rift64ScrollDirection.Left)
                {
                    // Right column exposed
                    var (emx, eox) = CamToMap(cam_x + view_w - 1);
                    await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
                        metaHi: META2_BASE_HI, targetAddr: ScreenAddress((byte)(view_x + view_w - 1), view_y), stride: 40,
                        winW: 1, winH: view_h, x: (byte)emx, y: (byte)my_step, offX: eox, offY: oy_step,
                        fillChar: 46, colorMode: 2, colorTgtAddr: (ushort)(0xD800 + view_y * 40 + view_x + view_w - 1),
                        colorSrcAddr: COLTAB_ADDR, cancellationToken: cancellationToken);
                }
                else // Right
                {
                    // Left column exposed
                    await client.DrawMetatileAsync(mode: 2, mapAddr: MAP2_ADDR, mapW: 8, mapH: 8,
                        metaHi: META2_BASE_HI, targetAddr: ScreenAddress(view_x, view_y), stride: 40,
                        winW: 1, winH: view_h, x: (byte)mx_step, y: (byte)my_step, offX: ox_step, offY: oy_step,
                        fillChar: 46, colorMode: 2, colorTgtAddr: SCROLL_COL_TGT,
                        colorSrcAddr: COLTAB_ADDR, cancellationToken: cancellationToken);
                }

                await client.WriteAtAsync(0, 22, $"CAM {cam_x:D2},{cam_y:D2} {dirNames[(byte)direction].PadRight(5)} {step + 1}/{steps}  ", Rift64Color.White, cancellationToken);
                await Task.Delay(150, cancellationToken); // Real-time delay between frames!
            }
        }

        await client.WriteAtAsync(0, 24, "SCROLL TESTS COMPLETE. PRESS KEY.", Rift64Color.LightGreen, cancellationToken);
        await client.PauseForKeyAsync(cancellationToken: cancellationToken);
    }

    private static async Task UploadPagesAsync(Rift64ProtocolClient client, ushort baseAddr, byte[] data, CancellationToken cancellationToken)
    {
        int offset = 0;
        while (offset < data.Length)
        {
            int chunkSize = Math.Min(256, data.Length - offset);
            var chunk = data.AsMemory(offset, chunkSize);
            await client.StoreMemoryCheckedAsync((ushort)(baseAddr + offset), chunk, cancellationToken);
            offset += chunkSize;
        }
    }

    private static ushort ScreenAddress(byte col, byte row)
    {
        return (ushort)(1024 + row * 40 + col);
    }

    private static string FormatAck(bool? ack)
    {
        return ack switch
        {
            true => "ACK",
            false => "NAK",
            null => "TIMEOUT"
        };
    }
}
