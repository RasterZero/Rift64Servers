using System.Diagnostics;
using RiftServe64.Sdk.Protocol;

/// <summary>
/// A playable Snake game illustrating real-time input and incremental,
/// bandwidth-frugal screen updates over the serial link.
/// <para>
/// Each frame only the three cells that actually change are redrawn — the new
/// head, the previous head (demoted to a body segment) and the erased tail —
/// rather than repainting the whole board. A frame-timed <see cref="Stopwatch"/>
/// drives the tick while <c>ReadKeyAsync</c> polls for input within the
/// remaining time budget, and the pause feature snapshots/restores the screen
/// via the firmware's save-buffer slots.
/// </para>
/// </summary>
public sealed class SnakeGameExample : IRift64MenuExample
{
    public char Key => 'E';
    public string MenuLabel => "Snake Game";

    private enum Direction
    {
        Up,
        Down,
        Left,
        Right
    }

    private static int _highScore = 0;

    public async Task RunAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        try
        {
            await RunCoreAsync(client, initialIdentity, cancellationToken);
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

    private async Task RunCoreAsync(Rift64ProtocolClient client, Rift64ClientIdentity initialIdentity, CancellationToken cancellationToken)
    {
        bool running = true;
        while (running && !cancellationToken.IsCancellationRequested)
        {
            // Show welcome and instructions screen
            await ShowWelcomeScreenAsync(client, cancellationToken);

            var result = await PlayGameAsync(client, cancellationToken);
            if (result.Quit)
            {
                running = false;
            }
            else
            {
                // Show game over popup
                var playAgain = await ShowGameOverScreenAsync(client, result.Score, cancellationToken);
                if (!playAgain)
                {
                    running = false;
                }
            }
        }

        // Clean up screen when leaving
        await client.ClearScreenAsync(cancellationToken);
    }

    private async Task ShowWelcomeScreenAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        await client.ClearScreenAsync(ct);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, ct);
        await client.SetCursorVisibilityAsync(false, ct);

        // Title Box
        await client.SetCursorAsync(4, 3, ct);
        await client.DrawBorderAsync(32, 7, Rift64BorderGlyphs.Classic, ct);

        await client.WriteAtAsync(12, 5, "RIFT64 SNAKE GAME", Rift64Color.LightBlue, ct);
        await client.WriteAtAsync(10, 7, "A RETRO CTTY EXPERIENCE", Rift64Color.Cyan, ct);

        // Instructions
        await client.WriteAtAsync(6, 11, "CONTROLS:", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(6, 13, "W / I  -  MOVE UP", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 14, "S / K  -  MOVE DOWN", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 15, "A / J  -  MOVE LEFT", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 16, "D / L  -  MOVE RIGHT", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 17, "P      -  PAUSE GAME", Rift64Color.White, ct);
        await client.WriteAtAsync(6, 18, "Q      -  QUIT TO MENU", Rift64Color.White, ct);

        await client.WriteAtAsync(6, 21, $"CURRENT HIGH SCORE: {_highScore:D4}", Rift64Color.LightGreen, ct);

        await client.WriteAtAsync(8, 23, "PRESS ANY KEY TO START...", Rift64Color.Orange, ct);

        await client.PauseForKeyAsync(cancellationToken: ct);
    }

    private async Task<(int Score, bool Quit)> PlayGameAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        await client.ClearScreenAsync(ct);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, ct);

        // Draw playing board border: width 38, height 20 starting at (1, 3)
        // Play area resides inside the border: x from 2 to 37 (inclusive), y from 4 to 21 (inclusive)
        await client.SetCursorAsync(1, 3, ct);
        await client.DrawBorderAsync(38, 20, Rift64BorderGlyphs.Classic, ct);

        // Setup Scoreboard UI
        await client.WriteAtAsync(2, 1, "RIFT64 SNAKE", Rift64Color.Cyan, ct);
        await client.WriteAtAsync(19, 1, $"HI: {_highScore:D4}", Rift64Color.Yellow, ct);
        int score = 0;
        int level = 1;
        await UpdateScoreboardAsync(client, score, level, ct);

        // Setup Snake
        var snake = new List<(int X, int Y)>
        {
            (X: 20, Y: 12), // Head
            (X: 19, Y: 12), // Body segment 1
            (X: 18, Y: 12)  // Body segment 2
        };

        // Draw initial snake
        foreach (var segment in snake)
        {
            await DrawSnakePartAsync(client, segment, isHead: segment == snake[0], ct);
        }

        // Setup Food
        var food = SpawnFood(snake);
        await DrawFoodAsync(client, food, ct);

        // Game Loop State
        Direction currentDir = Direction.Right;
        Direction nextDir = Direction.Right;
        int currentTickMs = 200; // Starting speed (slower)
        var tickInterval = TimeSpan.FromMilliseconds(currentTickMs);
        var stopwatch = Stopwatch.StartNew();
        bool gameOver = false;
        bool quit = false;

        while (!gameOver && !quit && !ct.IsCancellationRequested)
        {
            stopwatch.Restart();
            char? key = null;

            // Wait for key press during the remaining tick interval
            while (stopwatch.Elapsed < tickInterval)
            {
                var remaining = tickInterval - stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero) break;

                var read = await client.ReadKeyAsync(remaining, ct);
                if (read != null)
                {
                    key = read; // Store the latest valid key pressed during this frame
                }
            }

            if (key != null)
            {
                char lowerKey = char.ToLowerInvariant(key.Value);
                if (lowerKey == 'q')
                {
                    quit = true;
                    break;
                }
                else if (lowerKey == 'p')
                {
                    // PAUSE GAME: Save current screen to buffer index 1
                    await client.SaveScreenBufferAsync(1, ct);
                    await ShowPauseScreenAsync(client, ct);
                    // Restore screen when resuming
                    await client.RestoreScreenBufferAsync(1, ct);
                    stopwatch.Restart();
                    continue;
                }

                // Map control keys to directions and prevent reverse collisions
                if ((lowerKey == 'w' || lowerKey == 'i') && currentDir != Direction.Down)
                {
                    nextDir = Direction.Up;
                }
                else if ((lowerKey == 's' || lowerKey == 'k') && currentDir != Direction.Up)
                {
                    nextDir = Direction.Down;
                }
                else if ((lowerKey == 'a' || lowerKey == 'j') && currentDir != Direction.Right)
                {
                    nextDir = Direction.Left;
                }
                else if ((lowerKey == 'd' || lowerKey == 'l') && currentDir != Direction.Left)
                {
                    nextDir = Direction.Right;
                }
            }

            currentDir = nextDir;

            // Calculate new head position
            var head = snake[0];
            var newHead = currentDir switch
            {
                Direction.Up => (X: head.X, Y: head.Y - 1),
                Direction.Down => (X: head.X, Y: head.Y + 1),
                Direction.Left => (X: head.X - 1, Y: head.Y),
                Direction.Right => (X: head.X + 1, Y: head.Y),
                _ => head
            };

            // Wall Collision Check
            if (newHead.X < 2 || newHead.X > 37 || newHead.Y < 4 || newHead.Y > 21)
            {
                gameOver = true;
                break;
            }

            // Self Collision Check (skip checking tail if it will move anyway, but checking full list is safer)
            if (snake.Contains(newHead))
            {
                gameOver = true;
                break;
            }

            // Move Snake
            snake.Insert(0, newHead);

            // Food eating check
            if (newHead == food)
            {
                score += 10;
                if (score > _highScore)
                {
                    _highScore = score;
                    await client.WriteAtAsync(19, 1, $"HI: {_highScore:D4}", Rift64Color.Yellow, ct);
                }

                // Spawn new food
                food = SpawnFood(snake);

                // Level progression and speed increase
                if (score % 50 == 0 && currentTickMs > 60)
                {
                    level++;
                    currentTickMs = Math.Max(60, currentTickMs - 20); // Speed up
                    tickInterval = TimeSpan.FromMilliseconds(currentTickMs);
                }

                // Flash outer C64 border for a fun physical visual response!
                await FlashOuterBorderAsync(client, ct);

                // Update UI elements
                await UpdateScoreboardAsync(client, score, level, ct);
                await DrawFoodAsync(client, food, ct);
            }
            else
            {
                // Normal move: remove old tail segment
                var tail = snake[^1];
                snake.RemoveAt(snake.Count - 1);
                await EraseCellAsync(client, tail.X, tail.Y, ct);
            }

            // Redraw snake segments to reflect movement
            await DrawSnakePartAsync(client, newHead, isHead: true, ct);
            if (snake.Count > 1)
            {
                // Restore old head to a normal body segment appearance
                await DrawSnakePartAsync(client, snake[1], isHead: false, ct);
            }
        }

        return (score, quit);
    }

    private static (int X, int Y) SpawnFood(List<(int X, int Y)> snake)
    {
        var random = new Random();
        while (true)
        {
            var x = random.Next(2, 38); // 2 to 37
            var y = random.Next(4, 22); // 4 to 21
            var pos = (X: x, Y: y);
            if (!snake.Contains(pos))
            {
                return pos;
            }
        }
    }

    private static async Task DrawCellAsync(Rift64ProtocolClient client, int x, int y, string character, Rift64Color color, CancellationToken ct)
    {
        await client.SetCursorAsync((byte)x, (byte)y, ct);
        await client.DrawColoredWindowAsync(color, 1, 1, character, ct);
    }

    private static Task DrawSnakePartAsync(Rift64ProtocolClient client, (int X, int Y) part, bool isHead, CancellationToken ct)
    {
        var color = isHead ? Rift64Color.Yellow : Rift64Color.LightGreen;
        var character = isHead ? "●" : "▒";
        return DrawCellAsync(client, part.X, part.Y, character, color, ct);
    }

    private static Task DrawFoodAsync(Rift64ProtocolClient client, (int X, int Y) food, CancellationToken ct)
    {
        return DrawCellAsync(client, food.X, food.Y, "♥", Rift64Color.Red, ct);
    }

    private static Task EraseCellAsync(Rift64ProtocolClient client, int x, int y, CancellationToken ct)
    {
        return DrawCellAsync(client, x, y, " ", Rift64Color.Black, ct);
    }

    private static async Task UpdateScoreboardAsync(Rift64ProtocolClient client, int score, int level, CancellationToken ct)
    {
        await client.WriteAtAsync(30, 0, $"SCORE: {score:D4}", Rift64Color.White, ct);
        await client.WriteAtAsync(30, 1, $"LEVEL: {level:D2}", Rift64Color.LightGreen, ct);
    }

    private static async Task FlashOuterBorderAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        // Set outer border color to green, wait briefly, then restore to black
        await client.SetColorsAsync(Rift64Color.Green, Rift64Color.White, ct);
        await Task.Delay(40, ct);
        await client.SetColorsAsync(Rift64Color.Black, Rift64Color.White, ct);
    }

    private static async Task ShowPauseScreenAsync(Rift64ProtocolClient client, CancellationToken ct)
    {
        await client.SetCursorAsync(10, 9, ct);
        await client.DrawBorderAsync(20, 6, Rift64BorderGlyphs.Classic, ct);
        await client.WriteAtAsync(17, 11, "PAUSED", Rift64Color.Yellow, ct);
        await client.WriteAtAsync(12, 13, "ANY KEY TO RESUME", Rift64Color.White, ct);
        await client.PauseForKeyAsync(cancellationToken: ct);
    }

    private async Task<bool> ShowGameOverScreenAsync(Rift64ProtocolClient client, int finalScore, CancellationToken ct)
    {
        // Draw popup panel for Game Over
        await client.SetCursorAsync(6, 7, ct);
        await client.DrawBorderAsync(28, 10, Rift64BorderGlyphs.Classic, ct);

        await client.WriteAtAsync(15, 9, "GAME OVER", Rift64Color.Red, ct);
        await client.WriteAtAsync(10, 11, $"FINAL SCORE: {finalScore:D4}", Rift64Color.White, ct);

        if (finalScore >= _highScore && finalScore > 0)
        {
            await client.WriteAtAsync(10, 12, "NEW HIGH SCORE!!", Rift64Color.Yellow, ct);
        }

        await client.WriteAtAsync(9, 14, "PRESS R TO REPLAY", Rift64Color.LightGreen, ct);
        await client.WriteAtAsync(9, 15, "PRESS Q TO QUIT", Rift64Color.Orange, ct);

        while (!ct.IsCancellationRequested)
        {
            var key = await client.ReadKeyAsync(TimeSpan.FromMinutes(5), ct);
            if (key != null)
            {
                var lowerKey = char.ToLowerInvariant(key.Value);
                if (lowerKey == 'r')
                {
                    return true; // Replay
                }
                else if (lowerKey == 'q')
                {
                    return false; // Quit to menu
                }
            }
        }

        return false;
    }
}
