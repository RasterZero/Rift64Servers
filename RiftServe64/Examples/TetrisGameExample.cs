using System.Diagnostics;
using RiftServe64.Sdk.Protocol;

/// <summary>
/// A playable Tetris game demonstrating shadow-buffer diff rendering directly to
/// C64 screen and Color RAM.
/// <para>
/// The host keeps a mirror of the board's screen codes and colours; each frame
/// it composes the new board (locked blocks + ghost + active piece) and writes
/// only the rows that changed via <c>StoreMemoryAsync</c> straight to $0400 and
/// $D800. Filled cells use the reverse-space glyph ($A0) and the ghost uses a
/// checkerboard ($66). A <see cref="Stopwatch"/>-based gravity tick is
/// interleaved with tight input polling so buffered keypresses drain instantly
/// for responsive controls.
/// </para>
/// </summary>
public sealed class TetrisGameExample : IRift64MenuExample
{
    public char Key => 'F';
    public string MenuLabel => "Tetris Game";

    private const int BoardW = 10;
    private const int BoardH = 20;
    private const int BoardScrX = 15; // screen column of board cell (0,0)
    private const int BoardScrY = 3;  // screen row of board cell (0,0)

    private const int BoardOffX = 14; // border top-left X
    private const int BoardOffY = 2;  // border top-left Y

    private const int PreviewOuterW = 8;
    private const int PreviewOuterH = 6;
    private const int PreviewInnerW = PreviewOuterW - 2;
    private const int PreviewInnerH = PreviewOuterH - 2;
    private const int HoldBoxX = 2;
    private const int HoldBoxY = 2;
    private const int NextBoxX = 28;
    private const int NextBoxY = 2;
    private const int InputPollMs = 16;

    // Raw PETSCII screen codes
    private const byte ScBlock  = 0xA0; // reverse space = filled block
    private const byte ScGhost  = 0x66; // checkerboard
    private const byte ScEmpty  = 0x20; // space
    private const byte ScWhite  = 0xA0; // same as block, used for flash

    private static readonly int[][,,] Tetrominoes = new int[][,,]
    {
        new int[4,4,4] { // I - Cyan
            { {0,0,0,0},{1,1,1,1},{0,0,0,0},{0,0,0,0} },
            { {0,0,1,0},{0,0,1,0},{0,0,1,0},{0,0,1,0} },
            { {0,0,0,0},{1,1,1,1},{0,0,0,0},{0,0,0,0} },
            { {0,1,0,0},{0,1,0,0},{0,1,0,0},{0,1,0,0} }
        },
        new int[4,3,3] { // J - Blue
            { {1,0,0},{1,1,1},{0,0,0} }, { {0,1,1},{0,1,0},{0,1,0} },
            { {0,0,0},{1,1,1},{0,0,1} }, { {0,1,0},{0,1,0},{1,1,0} }
        },
        new int[4,3,3] { // L - Orange
            { {0,0,1},{1,1,1},{0,0,0} }, { {0,1,0},{0,1,0},{0,1,1} },
            { {0,0,0},{1,1,1},{1,0,0} }, { {1,1,0},{0,1,0},{0,1,0} }
        },
        new int[4,2,2] { // O - Yellow
            { {1,1},{1,1} }, { {1,1},{1,1} }, { {1,1},{1,1} }, { {1,1},{1,1} }
        },
        new int[4,3,3] { // S - LightGreen
            { {0,1,1},{1,1,0},{0,0,0} }, { {0,1,0},{0,1,1},{0,0,1} },
            { {0,0,0},{0,1,1},{1,1,0} }, { {1,0,0},{1,1,0},{0,1,0} }
        },
        new int[4,3,3] { // T - Purple
            { {0,1,0},{1,1,1},{0,0,0} }, { {0,1,0},{0,1,1},{0,1,0} },
            { {0,0,0},{1,1,1},{0,1,0} }, { {0,1,0},{1,1,0},{0,1,0} }
        },
        new int[4,3,3] { // Z - Red
            { {1,1,0},{0,1,1},{0,0,0} }, { {0,0,1},{0,1,1},{0,1,0} },
            { {0,0,0},{1,1,0},{0,1,1} }, { {0,1,0},{1,1,0},{1,0,0} }
        }
    };

    private static readonly byte[] PieceColors = new byte[]
    {
        (byte)Rift64Color.Cyan,       // I
        (byte)Rift64Color.Blue,       // J
        (byte)Rift64Color.Orange,     // L
        (byte)Rift64Color.Yellow,     // O
        (byte)Rift64Color.LightGreen, // S
        (byte)Rift64Color.Purple,     // T
        (byte)Rift64Color.Red         // Z
    };

    private byte[,] _board = new byte[BoardH, BoardW];
    private int _score, _lines, _level;
    private int _currentPiece, _currentRot, _currentX, _currentY;
    private int _nextPiece;
    private int? _heldPiece;
    private bool _heldThisTurn;

    private byte[][] _lastScr = new byte[BoardH][]; // cached screen codes
    private byte[][] _lastCol = new byte[BoardH][]; // cached colors

    private static int _highScore;

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken ct)
    {
        try
        {
            await RunCoreAsync(client, initialIdentity, ct);
        }
        finally
        {
            await RestoreClientStateAsync(client);
        }
    }

    // Phase 2.3: best-effort restoration that re-enables the text cursor and
    // resets colours so the host menu looks correct after a fault or cancel.
    private static async Task RestoreClientStateAsync(Rift64ProtocolClient client)
    {
        try
        {
            await client.SetCursorVisibilityAsync(true, CancellationToken.None);
            await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, CancellationToken.None);
        }
        catch
        {
            // The connection may already be gone during shutdown; ignore.
        }
    }

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken ct)
    {
        var running = true;
        while (running && !ct.IsCancellationRequested)
        {
            await ShowWelcomeAsync(client, ct);
            var (score, quit) = await PlayAsync(client, ct);
            if (quit) { running = false; break; }
            if (!await ShowGameOverAsync(client, score, ct)) running = false;
        }
        await client.ClearScreenAsync(ct);
    }

    private async Task ShowWelcomeAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        await client.ClearScreenAsync(ct);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, ct);
        await client.SetCursorVisibilityAsync(false, ct);
        await client.SetCursorAsync(4, 3);
        await client.DrawBorderAsync(32, 7, Rift64BorderGlyphs.Classic, ct);
        await client.WriteAtAsync(12, 5, "RIFT64 TETRIS GAME", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(10, 7, "A RETRO CTTY EXPERIENCE", Rift64Color.Cyan, ct);
        await client.WriteAtAsync(6, 11, "CONTROLS:", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(6, 13, "A/J=LEFT  D/L=RIGHT", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 14, "S/K=DROP  RET=PLACE", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 15, "F/SPACE=ROTATE C/H=HOLD", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 16, "P=PAUSE   Q=QUIT", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 18, $"HIGH SCORE: {_highScore:D5}", Rift64Color.LightGreen, ct);
        await client.WriteAtAsync(8, 20, "PRESS ANY KEY TO START", Rift64Color.Orange, ct);
        await client.PauseForKeyAsync(cancellationToken: ct);
    }

    private async Task<(int Score, bool Quit)> PlayAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        await client.ClearScreenAsync(ct);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, ct);

        _board = new byte[BoardH, BoardW];
        _score = 0; _lines = 0; _level = 1;
        _heldPiece = null; _heldThisTurn = false;

        _lastScr = new byte[BoardH][];
        _lastCol = new byte[BoardH][];
        for (var i = 0; i < BoardH; i++)
        {
            _lastScr[i] = new byte[BoardW]; // all zeros -> differs from ScEmpty=0x20, triggers draw
            _lastCol[i] = new byte[BoardW];
        }

        var rng = new Random();
        _currentPiece = rng.Next(7);
        _nextPiece = rng.Next(7);

        await DrawStaticFramesAsync(client, ct);
        await UpdateStatusAsync(client, ct);
        await DrawHoldBoxAsync(client, ct);
        await DrawNextBoxAsync(client, ct);

        GenPiece();
        await DrawBoardAsync(client, true, ct);

        var baseTick = 700;
        var tickMs = baseTick;
        var sw = Stopwatch.StartNew();
        bool gameOver = false, quit = false;

        while (!gameOver && !quit && !ct.IsCancellationRequested)
        {
            sw.Restart();

            var changed = false;

            while (sw.ElapsedMilliseconds < tickMs && !gameOver && !quit && !ct.IsCancellationRequested)
            {
                var rem = tickMs - (int)sw.ElapsedMilliseconds;
                if (rem <= 0) break;

                var k = await client.ReadKeyAsync(TimeSpan.FromMilliseconds(Math.Min(rem, InputPollMs)), ct);
                if (k == null) continue;

                var currentKey = k.Value;
                bool anyInputChanged = false;
                bool anyResetTick = false;
                bool anySkipGravity = false;

                while (currentKey != '\0')
                {
                    var (inputChanged, resetTick, requestQuit, requestGameOver, skipGravity) = await HandleInputAsync(client, currentKey, ct);
                    if (requestQuit)
                    {
                        quit = true;
                        break;
                    }

                    if (requestGameOver)
                    {
                        gameOver = true;
                        break;
                    }

                    if (inputChanged) anyInputChanged = true;
                    if (resetTick) anyResetTick = true;
                    if (skipGravity) anySkipGravity = true;

                    // Try to drain next buffered keypress instantly
                    var nextK = await client.ReadKeyAsync(TimeSpan.FromMilliseconds(1), ct);
                    currentKey = nextK != null ? nextK.Value : '\0';
                }

                if (quit || gameOver) break;

                if (anyInputChanged)
                {
                    await DrawBoardAsync(client, false, ct);
                }

                if (anySkipGravity)
                {
                    break;
                }

                if (anyResetTick)
                {
                    sw.Restart();
                }
            }

            if (!gameOver && !quit && !ct.IsCancellationRequested && sw.ElapsedMilliseconds >= tickMs)
            {
                if (Valid(_currentX, _currentY+1, _currentRot)) { _currentY++; changed = true; }
                else
                {
                    Lock();
                    changed = true;
                    await ClearLinesAsync(client, ct);
                    if (!Spawn()) { gameOver = true; break; }
                    _heldThisTurn = false;
                    await DrawNextBoxAsync(client, ct);
                    await UpdateStatusAsync(client, ct);
                }
            }

            if (changed) await DrawBoardAsync(client, false, ct);

            tickMs = Math.Max(80, baseTick - (_level - 1) * 50);
        }

        return (_score, quit);
    }

    private async Task<(bool Changed, bool ResetTick, bool Quit, bool GameOver, bool SkipGravity)> HandleInputAsync(Rift64ProtocolClient client, char key, CancellationToken ct)
    {
        var lk = char.ToLowerInvariant(key);
        if (lk == 'q') return (false, false, true, false, false);

        if (lk == 'p')
        {
            await client.SaveScreenBufferAsync(1, ct);
            await ShowPauseAsync(client, ct);
            await client.RestoreScreenBufferAsync(1, ct);
            return (false, true, false, false, false);
        }

        if (lk == 'a' || lk == 'j')
        {
            if (Valid(_currentX - 1, _currentY, _currentRot))
            {
                _currentX--;
                return (true, false, false, false, false);
            }

            return (false, false, false, false, false);
        }

        if (lk == 'd' || lk == 'l')
        {
            if (Valid(_currentX + 1, _currentY, _currentRot))
            {
                _currentX++;
                return (true, false, false, false, false);
            }

            return (false, false, false, false, false);
        }

        if (lk == 's' || lk == 'k')
        {
            if (Valid(_currentX, _currentY + 1, _currentRot))
            {
                _currentY++;
                _score++;
                await UpdateStatusAsync(client, ct);
                return (true, false, false, false, false);
            }

            return (false, false, false, false, false);
        }

        if (lk == 'f' || key == ' ')
        {
            var oldRot = _currentRot;
            var oldX = _currentX;
            var oldY = _currentY;
            Rotate();
            return (_currentRot != oldRot || _currentX != oldX || _currentY != oldY, false, false, false, false);
        }

        if (lk == 'c' || lk == 'h')
        {
            if (!_heldThisTurn)
            {
                Hold();
                _heldThisTurn = true;
                await DrawHoldBoxAsync(client, ct);
                await DrawNextBoxAsync(client, ct);
                return (true, false, false, false, false);
            }

            return (false, false, false, false, false);
        }

        if (key == '\r' || key == '\n' || lk == 'w' || lk == 'i')
        {
            var dist = 0;
            while (Valid(_currentX, _currentY + 1, _currentRot))
            {
                _currentY++;
                dist++;
            }

            _score += dist * 2;
            Lock();
            await ClearLinesAsync(client, ct);
            if (!Spawn()) return (true, true, false, true, true);
            _heldThisTurn = false;
            await DrawNextBoxAsync(client, ct);
            await UpdateStatusAsync(client, ct);
            return (true, true, false, false, true);
        }

        return (false, false, false, false, false);
    }

    // --- Piece logic ---

    private void GenPiece() { _currentPiece = _nextPiece; _nextPiece = new Random().Next(7); _currentRot = 0; _currentX = 3; _currentY = 0; }
    private bool Spawn() { GenPiece(); return Valid(_currentX, _currentY, _currentRot); }

    private void Hold()
    {
        if (_heldPiece == null) { _heldPiece = _currentPiece; GenPiece(); }
        else { var t = _currentPiece; _currentPiece = _heldPiece.Value; _heldPiece = t; _currentRot = 0; _currentX = 3; _currentY = 0; }
    }

    private void Rotate()
    {
        var nr = (_currentRot + 1) % 4;
        if (Valid(_currentX, _currentY, nr)) { _currentRot = nr; }
        else if (Valid(_currentX-1, _currentY, nr)) { _currentX--; _currentRot = nr; }
        else if (Valid(_currentX+1, _currentY, nr)) { _currentX++; _currentRot = nr; }
        else if (Valid(_currentX, _currentY-1, nr)) { _currentY--; _currentRot = nr; }
    }

    private bool Valid(int gx, int gy, int rot)
    {
        var s = Tetrominoes[_currentPiece];
        var sz = s.GetLength(1);
        for (var r = 0; r < sz; r++)
            for (var c = 0; c < sz; c++)
                if (s[rot, r, c] != 0)
                {
                    var bx = gx + c; var by = gy + r;
                    if (bx < 0 || bx >= BoardW || by >= BoardH) return false;
                    if (by >= 0 && _board[by, bx] != 0) return false;
                }
        return true;
    }

    private void Lock()
    {
        var s = Tetrominoes[_currentPiece]; var sz = s.GetLength(1);
        for (var r = 0; r < sz; r++)
            for (var c = 0; c < sz; c++)
                if (s[_currentRot, r, c] != 0)
                {
                    var bx = _currentX + c; var by = _currentY + r;
                    if (by >= 0 && by < BoardH && bx >= 0 && bx < BoardW)
                        _board[by, bx] = (byte)(_currentPiece + 1);
                }
    }

    private int GhostY()
    {
        var gy = _currentY;
        while (Valid(_currentX, gy+1, _currentRot)) gy++;
        return gy;
    }

    // --- Line clearing ---

    private async Task ClearLinesAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        var clear = new List<int>();
        for (var r = 0; r < BoardH; r++)
        {
            var full = true;
            for (var c = 0; c < BoardW; c++) if (_board[r, c] == 0) { full = false; break; }
            if (full) clear.Add(r);
        }
        if (clear.Count == 0) return;

        // Flash via direct color-RAM writes
        var addrBase = (ushort)(0xD800 + BoardScrY * 40 + BoardScrX);
        var flashWhite = new byte[BoardW]; Array.Fill(flashWhite, (byte)1);
        var flashBlack = new byte[BoardW];

        for (var f = 0; f < 2; f++)
        {
            foreach (var r in clear)
            {
                var addr = (ushort)(addrBase + r * 40);
                await client.StoreMemoryAsync(addr, flashWhite, ct);
            }
            await Task.Delay(80, ct);
            foreach (var r in clear)
            {
                var addr = (ushort)(addrBase + r * 40);
                await client.StoreMemoryAsync(addr, flashBlack, ct);
            }
            await Task.Delay(80, ct);
        }

        // Rebuild board
        var nb = new byte[BoardH, BoardW];
        var dr = BoardH - 1;
        for (var sr = BoardH - 1; sr >= 0; sr--)
            if (!clear.Contains(sr)) { for (var c = 0; c < BoardW; c++) nb[dr, c] = _board[sr, c]; dr--; }
        _board = nb;

        _score += new[] { 0, 100, 300, 500, 800 }[clear.Count] * _level;
        _lines += clear.Count;
        _level = _lines / 10 + 1;
        if (_score > _highScore) _highScore = _score;

        await DrawBoardAsync(client, true, ct);
        await FlashBorderAsync(client, Rift64Color.Cyan, ct);
    }

    // --- Direct memory rendering ---

    private async Task DrawBoardAsync(Rift64ProtocolClient client, bool forceFull, CancellationToken ct)
    {
        var scr = new byte[BoardH][];
        var col = new byte[BoardH][];

        // Build from locked blocks
        for (var r = 0; r < BoardH; r++)
        {
            scr[r] = new byte[BoardW];
            col[r] = new byte[BoardW];
            for (var c = 0; c < BoardW; c++)
            {
                var id = _board[r, c];
                scr[r][c] = id == 0 ? ScEmpty : ScBlock;
                col[r][c] = id == 0 ? (byte)0 : PieceColors[id - 1];
            }
        }

        // Overlay ghost piece
        var gy = GhostY();
        if (gy != _currentY)
        {
            var s = Tetrominoes[_currentPiece]; var sz = s.GetLength(1);
            for (var r = 0; r < sz; r++)
                for (var c = 0; c < sz; c++)
                    if (s[_currentRot, r, c] != 0)
                    {
                        var bx = _currentX + c; var by = gy + r;
                        if (by >= 0 && by < BoardH && bx >= 0 && bx < BoardW)
                        { scr[by][bx] = ScGhost; col[by][bx] = (byte)Rift64Color.DarkGray; }
                    }
        }

        // Overlay active piece
        {
            var s = Tetrominoes[_currentPiece]; var sz = s.GetLength(1);
            for (var r = 0; r < sz; r++)
                for (var c = 0; c < sz; c++)
                    if (s[_currentRot, r, c] != 0)
                    {
                        var bx = _currentX + c; var by = _currentY + r;
                        if (by >= 0 && by < BoardH && bx >= 0 && bx < BoardW)
                        { scr[by][bx] = ScBlock; col[by][bx] = PieceColors[_currentPiece]; }
                    }
        }

        // Write only changed rows via direct StoreMemoryAsync to $0400/$D800
        var scrBase = (ushort)(0x0400 + BoardScrY * 40 + BoardScrX);
        var colBase = (ushort)(0xD800 + BoardScrY * 40 + BoardScrX);

        for (var r = 0; r < BoardH; r++)
        {
            if (!forceFull && ByteSeqEq(scr[r], _lastScr[r]) && ByteSeqEq(col[r], _lastCol[r]))
                continue;

            _lastScr[r] = scr[r];
            _lastCol[r] = col[r];

            var addr = (ushort)(scrBase + r * 40);
            await client.StoreMemoryAsync(addr, scr[r], ct);
            await client.StoreMemoryAsync((ushort)(colBase + r * 40), col[r], ct);
        }
    }

    private static bool ByteSeqEq(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return false;
        for (var i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
        return true;
    }

    // --- UI drawing (also direct memory for boxes) ---

    private async Task DrawStaticFramesAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        // Board border
        await client.SetCursorAsync(BoardOffX, BoardOffY, ct);
        await client.DrawBorderAsync(12, 22, Rift64BorderGlyphs.Classic, ct);

        // Hold box border (8x6 at col 2, row 2)
        await client.SetCursorAsync(HoldBoxX, HoldBoxY, ct);
        await client.DrawBorderAsync(PreviewOuterW, PreviewOuterH, Rift64BorderGlyphs.Classic, ct);
        await client.WriteAtAsync((byte)(HoldBoxX + 1), (byte)HoldBoxY, "HOLD", Rift64Color.Cyan, ct);

        // Next box border (8x6 at col 28, row 2)
        await client.SetCursorAsync(NextBoxX, NextBoxY, ct);
        await client.DrawBorderAsync(PreviewOuterW, PreviewOuterH, Rift64BorderGlyphs.Classic, ct);
        await client.WriteAtAsync((byte)(NextBoxX + 1), (byte)NextBoxY, "NEXT", Rift64Color.Cyan, ct);

        // Controls on right
        await client.WriteAtAsync(28, 9, "CONTROLS:", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(28, 11, "A/J=LEFT", Rift64Color.White, ct);
        await client.WriteAtAsync(28, 12, "D/L=RIGHT", Rift64Color.White, ct);
        await client.WriteAtAsync(28, 13, "S/K=DROP", Rift64Color.White, ct);
        await client.WriteAtAsync(28, 14, "RET=PLACE", Rift64Color.White, ct);
        await client.WriteAtAsync(28, 15, "F/SPACE=ROTATE", Rift64Color.White, ct);
        await client.WriteAtAsync(28, 16, "C/H=HOLD", Rift64Color.White, ct);
    }

    private async Task DrawBoxPieceAsync(Rift64ProtocolClient client, int scrX, int scrY, int pieceId, CancellationToken ct)
    {
        // Erase only the preview interior so the border remains intact.
        for (var r = 0; r < PreviewInnerH; r++)
        {
            var addr = (ushort)(0x0400 + (scrY + r) * 40 + scrX);
            var empty = new byte[PreviewInnerW]; Array.Fill(empty, ScEmpty);
            var black = new byte[PreviewInnerW];
            await client.StoreMemoryAsync(addr, empty, ct);
            await client.StoreMemoryAsync((ushort)(0xD800 + (scrY + r) * 40 + scrX), black, ct);
        }

        if (pieceId < 0) return;

        var s = Tetrominoes[pieceId]; var sz = s.GetLength(1);
        var color = PieceColors[pieceId];

        var dr = (PreviewInnerH - sz) / 2;
        var dc = (PreviewInnerW - sz) / 2;

        for (var r = 0; r < sz; r++)
            for (var c = 0; c < sz; c++)
                if (s[0, r, c] != 0)
                {
                    var addr = (ushort)(0x0400 + (scrY + dr + r) * 40 + scrX + dc + c);
                    await client.StoreMemoryAsync(addr, new byte[] { ScBlock }, ct);
                    await client.StoreMemoryAsync((ushort)(0xD800 + (scrY + dr + r) * 40 + scrX + dc + c), new byte[] { color }, ct);
                }
    }

    private Task DrawHoldBoxAsync(Rift64ProtocolClient client, CancellationToken ct)
        => DrawBoxPieceAsync(client, HoldBoxX + 1, HoldBoxY + 1, _heldPiece ?? -1, ct);

    private Task DrawNextBoxAsync(Rift64ProtocolClient client, CancellationToken ct)
        => DrawBoxPieceAsync(client, NextBoxX + 1, NextBoxY + 1, _nextPiece, ct);

    private async Task UpdateStatusAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        // Keep these writes serialized so cursor positioning cannot interleave.
        await client.WriteAtAsync(2, 9, "SCORE", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(2, 10, $"{_score:D5}", Rift64Color.White, ct);
        await client.WriteAtAsync(2, 12, "LINES", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(2, 13, $"{_lines:D3}", Rift64Color.White, ct);
        await client.WriteAtAsync(2, 15, "LEVEL", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(2, 16, $"{_level:D2}", Rift64Color.LightGreen, ct);
        await client.WriteAtAsync(2, 18, "BEST", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(2, 19, $"{_highScore:D5}", Rift64Color.White, ct);
    }

    private static async Task FlashBorderAsync(Rift64ProtocolClient client, Rift64Color c, CancellationToken ct)
    {
        await client.SetColorsAsync(c, Rift64Color.White, ct);
        await Task.Delay(50, ct);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, ct);
    }

    private static async Task ShowPauseAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        await client.SetCursorAsync(10, 9, ct);
        await client.DrawBorderAsync(20, 6, Rift64BorderGlyphs.Classic, ct);
        await client.WriteAtAsync(17, 11, "PAUSED", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(12, 13, "ANY KEY = RESUME", Rift64Color.White, ct);
        await client.PauseForKeyAsync(cancellationToken: ct);
    }

    private async Task<bool> ShowGameOverAsync(Rift64ProtocolClient client, int score, CancellationToken ct)
    {
        await client.SetCursorAsync(6, 7, ct);
        await client.DrawBorderAsync(28, 10, Rift64BorderGlyphs.Classic, ct);
        await client.WriteAtAsync(15, 9, "GAME OVER", Rift64Color.Red, ct);
        await client.WriteAtAsync(10, 11, $"SCORE: {score:D5}", Rift64Color.White, ct);
        if (score >= _highScore && score > 0) await client.WriteAtAsync(10, 12, "NEW BEST!", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(9, 14, "R = REPLAY", Rift64Color.LightGreen, ct);
        await client.WriteAtAsync(9, 15, "Q = QUIT", Rift64Color.Orange, ct);
        while (!ct.IsCancellationRequested)
        {
            var k = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), ct);
            if (k == null) continue;
            var lk = char.ToLowerInvariant(k.Value);
            if (lk == 'r') return true;
            if (lk == 'q') return false;
        }
        return false;
    }
}
