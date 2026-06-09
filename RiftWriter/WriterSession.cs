using RiftServe64.Sdk.Networking;
using RiftServe64.Sdk.Protocol;
using RiftWriter.Features;
using RiftWriter.IO;
using RiftWriter.Models;
using RiftWriter.Rendering;

namespace RiftWriter;

/// <summary>
/// Per-connection editor session. Owns all state for one client.
/// </summary>
internal sealed class WriterSession
{
    private readonly Rift64ProtocolClient _client;
    private readonly ThrottledClientConnection _connection;

    // State
    private readonly WriterDocument _doc = new();
    private readonly ThemeContext _theme = ThemeContext.CreateDefault();
    private readonly ChromeRenderer _chrome;
    private readonly ViewportRenderer _viewport;
    private readonly PopupRenderer _popup;
    private int _cursorRow;
    private int _cursorCol;
    private int _viewportX;
    private int _viewportY;
    private bool _insertMode = true;
    private WriterState _state = WriterState.Editor;

    // Popup state
    private int _menuSelected;
    private string[] _fileList = [];
    private int _fileBrowserSelected;
    private int _fileBrowserScroll;
    private string _inputText = "";
    private string _inputTitle = "";
    private Func<string, Task>? _inputCallback;
    private Func<bool, Task>? _confirmCallback;

    // Clipboard
    private string _clipboardLine = "";
    private List<byte> _clipboardColors = [];

    // Phase 7: Features
    private readonly SpellChecker _spellChecker = new();
    private readonly FindReplaceEngine _findReplace = new();
    private readonly FocusTimer _focusTimer = new();
    private readonly AutoSaveManager _autoSave = new();
    private List<(int Row, int Col, string Word)> _spellErrors = [];
    private int _spellIdx;
    private string _spellSuggestion = "";
    private int _spellPrevRow = -1; // doc row of previously highlighted word

    public WriterSession(ThrottledClientConnection connection)
    {
        _connection = connection;
        _client = new Rift64ProtocolClient(connection);
        _chrome = new ChromeRenderer(_client, _theme);
        _viewport = new ViewportRenderer(_client, _theme);
        _popup = new PopupRenderer(_client, _theme);
    }

    public async Task RunAsync(CancellationToken ct = default)
    {
        Console.WriteLine("Client connected, waiting for READY...");

        var ready = await _client.WaitForReadyAsync(TimeSpan.FromSeconds(30), ct);
        if (!ready)
        {
            Console.WriteLine("Client did not send READY within timeout.");
            return;
        }

        Console.WriteLine("READY received. Initializing writer...");

        // Load dictionary for spell check
        _spellChecker.Load();

        // Switch to lowercase/uppercase charset
        await _client.SetCharsetBankAsync(VicBank.Bank0, 0x17, ct);

        // Set background color
        await _client.SetColorsAsync(_theme.Bg, _theme.EditorFg, ct);

        // Clear screen
        await _client.ClearScreenAsync(ct);

        // Full redraw
        await FullRedrawAsync(ct);

        Console.WriteLine("Writer session initialized — chrome drawn.");

        // Main event loop
        var buf = new byte[64];
        while (_connection.IsConnected && !ct.IsCancellationRequested)
        {
            try
            {
                // First drain any bytes buffered by the protocol client during ACK waits
                // (user keystrokes that arrived while waiting for C64 to ACK a command)
                if (_client.HasPendingBytes)
                {
                    var pending = _client.DrainPendingBytes();
                    for (var i = 0; i < pending.Length; i++)
                        await ProcessKeyAsync(pending[i], ct);
                    continue;
                }

                if (_connection.IsDataAvailable)
                {
                    var n = await _client.ReadRawAsync(buf, ct);
                    for (var i = 0; i < n; i++)
                        await ProcessKeyAsync(buf[i], ct);
                    // Check autosave after processing keys
                    await CheckAutoSaveAsync(ct);
                }
                else
                {
                    // Timer tick (update title bar every second when active)
                    await TickTimerAsync(ct);
                    await Task.Delay(20, ct);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                Console.WriteLine($"Session error: {ex.Message}");
                break;
            }
        }
    }

    private async Task FullRedrawAsync(CancellationToken ct)
    {
        await _client.SetCursorVisibilityAsync(false, ct);
        await _client.SetColorsAsync(_theme.Bg, _theme.EditorFg, ct);
        await _client.ClearScreenAsync(ct);
        var timerStr = _focusTimer.IsActive ? (_focusTimer.Tick() ?? "") : "";
        await _chrome.DrawEditorChromeAsync(_doc, _viewportX, _cursorRow, _cursorCol, _insertMode, timerStr, ct);
        await RenderViewportAsync(forceRedraw: true, ct: ct);
        await PlaceCursorAsync(ct);
    }

    /// <summary>Helper that calls viewport renderer and writes back the updated viewport offsets.</summary>
    private async Task RenderViewportAsync(int? newY = null, int? newX = null, bool forceRedraw = false, CancellationToken ct = default)
    {
        var (vy, vx) = await _viewport.RenderAsync(_doc, _cursorRow, _cursorCol, _viewportY, _viewportX, newY, newX, forceRedraw, ct);
        _viewportY = vy;
        _viewportX = vx;
    }

    private async Task ProcessKeyAsync(byte b, CancellationToken ct)
    {
        if (_state == WriterState.Editor)
        {
            // Count keystrokes for autosave (skip navigation keys)
            if (b is not (WriterConstants.KeyF1 or WriterConstants.KeyF3 or WriterConstants.KeyF5
                or WriterConstants.KeyF7 or WriterConstants.KeyCrsrUp or WriterConstants.KeyCrsrDown
                or WriterConstants.KeyCrsrLeft or WriterConstants.KeyCrsrRight))
            {
                _autoSave.RecordKeystroke();
            }

            // Color control codes
            if (WriterConstants.ColorMappings.TryGetValue(b, out var colorId))
            {
                _doc.DefaultColor = colorId;
                await _client.SetColorsAsync(_theme.Bg, colorId, ct);
                return;
            }

            switch (b)
            {
                case WriterConstants.KeyF1: await OpenFileMenuAsync(ct); break;
                case WriterConstants.KeyF3: await OpenEditMenuAsync(ct); break;
                case WriterConstants.KeyF5: await OpenViewMenuAsync(ct); break;
                case WriterConstants.KeyF7: await OpenHelpAsync(ct); break;
                case WriterConstants.KeyRunStop: await DoFileExitAsync(ct); break;
                case WriterConstants.KeyCrsrUp: await HandleCursorUpAsync(ct); break;
                case WriterConstants.KeyCrsrDown: await HandleCursorDownAsync(ct); break;
                case WriterConstants.KeyCrsrLeft: await HandleCursorLeftAsync(ct); break;
                case WriterConstants.KeyCrsrRight: await HandleCursorRightAsync(ct); break;
                case WriterConstants.KeyReturn: await HandleEnterAsync(ct); break;
                case WriterConstants.KeyDel: await HandleBackspaceAsync(ct); break;
                case WriterConstants.KeyInst:
                    _insertMode = !_insertMode;
                    await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
                    break;
                case WriterConstants.KeyHome: await HandleHomeAsync(ct); break;
                case WriterConstants.KeyClr: await HandleClearAsync(ct); break;
                case WriterConstants.KeyTab: await HandleTabAsync(ct); break;
                default:
                    if ((b >= 32 && b <= 126) || (b >= 160 && b <= 254))
                    {
                        var ch = (char)b;
                        if (ch >= 'A' && ch <= 'Z') ch = char.ToLowerInvariant(ch);
                        await HandleTypingAsync(ch, ct);
                    }
                    break;
            }

            // Always restore cursor in editor mode
            if (_state == WriterState.Editor)
                await PlaceCursorAsync(ct);
        }
        else if (_state == WriterState.FileMenu)
        {
            await ProcessFileMenuKeyAsync(b, ct);
        }
        else if (_state == WriterState.EditMenu)
        {
            await ProcessEditMenuKeyAsync(b, ct);
        }
        else if (_state == WriterState.ViewMenu)
        {
            await ProcessViewMenuKeyAsync(b, ct);
        }
        else if (_state == WriterState.Help)
        {
            await ClosePopupAsync(ct);
        }
        else if (_state == WriterState.FileBrowser)
        {
            await ProcessFileBrowserKeyAsync(b, ct);
        }
        else if (_state == WriterState.InputDialog)
        {
            await ProcessInputDialogKeyAsync(b, ct);
        }
        else if (_state == WriterState.ConfirmDialog)
        {
            await ProcessConfirmDialogKeyAsync(b, ct);
        }
        else if (_state == WriterState.ThemeSelect)
        {
            await ProcessThemeSelectKeyAsync(b, ct);
        }
        else if (_state == WriterState.Spellcheck)
        {
            await ProcessSpellcheckKeyAsync(b, ct);
        }
        else if (_state == WriterState.FindReplace)
        {
            await ProcessFindReplaceKeyAsync(b, ct);
        }
    }

    private async Task HandleTypingAsync(char ch, CancellationToken ct)
    {
        var line = _doc.Lines[_cursorRow];

        if (line.Length >= WriterConstants.DocMaxCols)
        {
            // Word wrap at col 80
            var spaceIdx = line.LastIndexOf(' ');
            if (spaceIdx != -1 && (line.Length - spaceIdx) < 25)
            {
                _doc.SplitLine(_cursorRow, spaceIdx);
                _cursorRow++;
                _cursorCol = _doc.Lines[_cursorRow].Length;
                _doc.InsertChar(_cursorRow, _cursorCol, ch);
                _cursorCol++;
            }
            else
            {
                _doc.SplitLine(_cursorRow, _cursorCol);
                _cursorRow++;
                _cursorCol = 0;
                _doc.InsertChar(_cursorRow, _cursorCol, ch);
                _cursorCol++;
            }
            var (ty, tx) = CalculateVisibleViewport();
            if (tx != _viewportX) await _chrome.DrawRulerAsync(tx, ct);
            await RenderViewportAsync(ty, tx, forceRedraw: true, ct: ct);
        }
        else
        {
            if (_insertMode)
            {
                _doc.InsertChar(_cursorRow, _cursorCol, ch);
                _cursorCol++;
            }
            else
            {
                // Overwrite mode
                if (_cursorCol < line.Length)
                {
                    var chars = line.ToCharArray();
                    chars[_cursorCol] = ch;
                    _doc.Lines[_cursorRow] = new string(chars);
                    _doc.Colors[_cursorRow][_cursorCol] = _doc.DefaultColor;
                    _doc.Modified = true;
                }
                else
                {
                    _doc.InsertChar(_cursorRow, _cursorCol, ch);
                }
                _cursorCol++;
            }
            var (ty, tx) = CalculateVisibleViewport();
            if (tx != _viewportX)
            {
                await RenderViewportAsync(ty, tx, ct: ct);
                await _chrome.DrawRulerAsync(_viewportX, ct);
            }
            else
            {
                await RenderViewportAsync(ty, ct: ct);
            }
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleEnterAsync(CancellationToken ct)
    {
        (_cursorRow, _cursorCol) = _doc.SplitLine(_cursorRow, _cursorCol);
        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else if (ty != _viewportY)
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        else
        {
            await _viewport.ScrollInsertRowAsync(_doc, _cursorRow, _viewportY, _viewportX, ct);
            await _viewport.RenderSingleRowAsync(_doc, _cursorRow - 1, WriterConstants.ViewportTop + (_cursorRow - 1 - _viewportY), _viewportX, ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleBackspaceAsync(CancellationToken ct)
    {
        var oldRow = _cursorRow;
        var (newRow, newCol, merged) = _doc.Backspace(_cursorRow, _cursorCol);
        _cursorRow = newRow;
        _cursorCol = newCol;

        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else if (ty != _viewportY)
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        else if (merged)
        {
            await _viewport.ScrollDeleteRowAsync(_doc, oldRow, _viewportY, _viewportX, ct);
            await _viewport.RenderSingleRowAsync(_doc, _cursorRow, WriterConstants.ViewportTop + (_cursorRow - _viewportY), _viewportX, ct);
        }
        else
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleCursorUpAsync(CancellationToken ct)
    {
        if (_cursorRow > 0)
        {
            _cursorRow--;
            _cursorCol = Math.Min(_cursorCol, _doc.LineLen(_cursorRow));
            var (ty, tx) = CalculateVisibleViewport();
            if (tx != _viewportX)
            {
                await RenderViewportAsync(ty, tx, ct: ct);
                await _chrome.DrawRulerAsync(_viewportX, ct);
            }
            else
            {
                await RenderViewportAsync(ty, ct: ct);
            }
            await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
        }
    }

    private async Task HandleCursorDownAsync(CancellationToken ct)
    {
        if (_cursorRow < _doc.TotalLines() - 1)
        {
            _cursorRow++;
            _cursorCol = Math.Min(_cursorCol, _doc.LineLen(_cursorRow));
            var (ty, tx) = CalculateVisibleViewport();
            if (tx != _viewportX)
            {
                await RenderViewportAsync(ty, tx, ct: ct);
                await _chrome.DrawRulerAsync(_viewportX, ct);
            }
            else
            {
                await RenderViewportAsync(ty, ct: ct);
            }
            await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
        }
    }

    private async Task HandleCursorLeftAsync(CancellationToken ct)
    {
        if (_cursorCol > 0)
            _cursorCol--;
        else if (_cursorRow > 0)
        {
            _cursorRow--;
            _cursorCol = _doc.LineLen(_cursorRow);
        }
        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleCursorRightAsync(CancellationToken ct)
    {
        if (_cursorCol < _doc.LineLen(_cursorRow))
            _cursorCol++;
        else if (_cursorRow < _doc.TotalLines() - 1)
        {
            _cursorRow++;
            _cursorCol = 0;
        }
        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleHomeAsync(CancellationToken ct)
    {
        _cursorCol = 0;
        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleClearAsync(CancellationToken ct)
    {
        _cursorRow = 0;
        _cursorCol = 0;
        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private async Task HandleTabAsync(CancellationToken ct)
    {
        var spaces = 4 - (_cursorCol % 4);
        for (var i = 0; i < spaces; i++)
        {
            if (_doc.Lines[_cursorRow].Length >= WriterConstants.DocMaxCols) break;
            _doc.InsertChar(_cursorRow, _cursorCol, ' ');
            _cursorCol++;
        }
        var (ty, tx) = CalculateVisibleViewport();
        if (tx != _viewportX)
        {
            await RenderViewportAsync(ty, tx, ct: ct);
            await _chrome.DrawRulerAsync(_viewportX, ct);
        }
        else
        {
            await RenderViewportAsync(ty, ct: ct);
        }
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
    }

    private (int TargetY, int TargetX) CalculateVisibleViewport()
    {
        var targetY = _viewportY;
        var targetX = _viewportX;

        if (_cursorRow < targetY)
            targetY = _cursorRow;
        else if (_cursorRow >= targetY + WriterConstants.ViewportRows)
            targetY = _cursorRow - WriterConstants.ViewportRows + 1;

        // Clamp viewport Y to valid range
        targetY = Math.Max(0, targetY);

        var screenCol = _cursorCol - targetX;
        if (screenCol < WriterConstants.HScrollMargin && targetX > 0)
            targetX = Math.Max(0, _cursorCol - WriterConstants.HScrollJump);
        else if (screenCol >= WriterConstants.ViewportCols - WriterConstants.HScrollMargin)
        {
            targetX = Math.Min(WriterConstants.DocMaxCols - WriterConstants.ViewportCols, _cursorCol - WriterConstants.ViewportCols + WriterConstants.HScrollJump);
            targetX = Math.Max(0, targetX);
        }

        return (targetY, targetX);
    }

    private async Task PlaceCursorAsync(CancellationToken ct)
    {
        // Clamp cursor to valid document bounds
        _cursorRow = Math.Clamp(_cursorRow, 0, Math.Max(0, _doc.TotalLines() - 1));
        _cursorCol = Math.Clamp(_cursorCol, 0, _doc.LineLen(_cursorRow));

        var screenCol = _cursorCol - _viewportX;
        var screenRow = _cursorRow - _viewportY + WriterConstants.ViewportTop;
        if (screenCol >= 0 && screenCol < WriterConstants.ViewportCols &&
            screenRow >= WriterConstants.ViewportTop && screenRow <= WriterConstants.ViewportBot)
        {
            await _client.SetCursorAsync((byte)screenCol, (byte)screenRow, ct);
            await _client.SetCursorVisibilityAsync(true, ct);
        }
        else
        {
            await _client.SetCursorVisibilityAsync(false, ct);
        }
    }

    // -----------------------------------------------------------------------
    // Menu open/close
    // -----------------------------------------------------------------------

    private static readonly string[] FileMenuItems = ["New", "Open", "Save", "Save As", "Exit"];
    private static readonly string[] EditMenuItems = ["Cut Line", "Copy Line", "Paste Line", "Delete Line", "Duplicate Line", "Auto-Wrap Para"];
    private static readonly string[] ViewMenuItems = ["Goto Line", "Find & Replace", "Color Themes", "Focus Timer", "Word Count", "Doc Info", "Spell Check"];

    private async Task OpenFileMenuAsync(CancellationToken ct)
    {
        _state = WriterState.FileMenu;
        _menuSelected = 0;
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawMenuPopupAsync("File", FileMenuItems, _menuSelected, ct);
    }

    private async Task OpenEditMenuAsync(CancellationToken ct)
    {
        _state = WriterState.EditMenu;
        _menuSelected = 0;
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawMenuPopupAsync("Edit", EditMenuItems, _menuSelected, ct);
    }

    private async Task OpenViewMenuAsync(CancellationToken ct)
    {
        _state = WriterState.ViewMenu;
        _menuSelected = 0;
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawMenuPopupAsync("View", ViewMenuItems, _menuSelected, ct);
    }

    private async Task OpenHelpAsync(CancellationToken ct)
    {
        _state = WriterState.Help;
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawHelpScreenAsync(ct);
    }

    private async Task ClosePopupAsync(CancellationToken ct)
    {
        _state = WriterState.Editor;
        await _client.RestoreScreenBufferAsync(0, ct);
        await PlaceCursorAsync(ct);
    }

    // -----------------------------------------------------------------------
    // File Menu
    // -----------------------------------------------------------------------

    private async Task ProcessFileMenuKeyAsync(byte b, CancellationToken ct)
    {
        if (b == WriterConstants.KeyCrsrDown)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected + 1) % FileMenuItems.Length;
            await _popup.UpdateMenuSelectionAsync("File", FileMenuItems, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyCrsrUp)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected - 1 + FileMenuItems.Length) % FileMenuItems.Length;
            await _popup.UpdateMenuSelectionAsync("File", FileMenuItems, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyReturn)
        {
            switch (_menuSelected)
            {
                case 0: await DoFileNewAsync(ct); break;
                case 1: await DoFileOpenAsync(ct); break;
                case 2: await DoFileSaveAsync(ct); break;
                case 3: await DoFileSaveAsAsync(ct); break;
                case 4: await DoFileExitAsync(ct); break;
            }
        }
        else if (b == WriterConstants.KeyRunStop || b == WriterConstants.KeyF1)
        {
            await ClosePopupAsync(ct);
        }
    }

    private async Task DoFileNewAsync(CancellationToken ct)
    {
        _doc.Lines = [""];
        _doc.Colors = [[]];
        _doc.Filename = "untitled.txt";
        _doc.Modified = false;
        _doc.DefaultColor = _theme.EditorFg;
        _cursorRow = 0;
        _cursorCol = 0;
        _viewportY = 0;
        _viewportX = 0;
        _autoSave.Reset();
        await ClosePopupAsync(ct);
        await FullRedrawAsync(ct);
    }

    private async Task DoFileOpenAsync(CancellationToken ct)
    {
        _fileList = DocumentFileManager.ListDocuments().ToArray();
        if (_fileList.Length == 0)
        {
            await ClosePopupAsync(ct);
            await ShowMessageAsync("no documents found", ct);
            await FullRedrawAsync(ct);
            return;
        }
        _fileBrowserSelected = 0;
        _fileBrowserScroll = 0;
        _state = WriterState.FileBrowser;
        await _client.RestoreScreenBufferAsync(0, ct);
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawFileBrowserAsync(_fileList, _fileBrowserSelected, _fileBrowserScroll, ct);
    }

    private async Task DoFileSaveAsync(CancellationToken ct)
    {
        DocumentFileManager.Save(_doc);
        _state = WriterState.Editor;
        await _client.RestoreScreenBufferAsync(0, ct);
        await ShowMessageAsync($"saved: {_doc.Filename}", ct);
        await FullRedrawAsync(ct);
    }

    private async Task DoFileSaveAsAsync(CancellationToken ct)
    {
        _state = WriterState.InputDialog;
        _inputText = _doc.Filename;
        _inputTitle = "Save As Filename:";
        _inputCallback = async filename =>
        {
            if (!string.IsNullOrEmpty(filename))
            {
                if (!filename.EndsWith(".txt") && !filename.EndsWith(".rift"))
                    filename += ".txt";
                _doc.Filename = filename;
                DocumentFileManager.Save(_doc);
            }
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            await ShowMessageAsync($"saved: {_doc.Filename}", ct);
            await FullRedrawAsync(ct);
        };
        await _client.RestoreScreenBufferAsync(0, ct);
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawInputDialogAsync(_inputTitle, _inputText, ct);
    }

    private async Task DoFileExitAsync(CancellationToken ct)
    {
        if (_doc.Modified)
        {
            _state = WriterState.ConfirmDialog;
            _confirmCallback = async confirmed =>
            {
                if (confirmed)
                    await DoGoodbyeAsync(ct);
                else
                    await FullRedrawAsync(ct);
            };
            await _client.RestoreScreenBufferAsync(0, ct);
            await _client.SaveScreenBufferAsync(0, ct);
            await _client.SetCursorVisibilityAsync(false, ct);
            await _popup.DrawConfirmDialogAsync("discard changes?", ct);
        }
        else
        {
            await DoGoodbyeAsync(ct);
        }
    }

    private async Task DoGoodbyeAsync(CancellationToken ct)
    {
        await _client.ClearScreenAsync(ct);
        await _client.SetColorsAsync(_theme.Bg, (byte)Rift64Color.White, ct);
        await _client.SetCursorAsync(0, 12, ct);
        await _client.WriteTextAsync("   rift writer closed.".PadRight(WriterConstants.ScreenWidth), ct);
        await Task.Delay(1000, ct);
        await _connection.DisposeAsync();
    }

    // -----------------------------------------------------------------------
    // Edit Menu
    // -----------------------------------------------------------------------

    private async Task ProcessEditMenuKeyAsync(byte b, CancellationToken ct)
    {
        if (b == WriterConstants.KeyCrsrDown)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected + 1) % EditMenuItems.Length;
            await _popup.UpdateMenuSelectionAsync("Edit", EditMenuItems, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyCrsrUp)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected - 1 + EditMenuItems.Length) % EditMenuItems.Length;
            await _popup.UpdateMenuSelectionAsync("Edit", EditMenuItems, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyReturn)
        {
            switch (_menuSelected)
            {
                case 0: // Cut Line
                    (_clipboardLine, _clipboardColors) = _doc.DeleteLine(_cursorRow);
                    if (_cursorRow >= _doc.TotalLines()) _cursorRow = Math.Max(0, _doc.TotalLines() - 1);
                    _cursorCol = Math.Min(_cursorCol, _doc.LineLen(_cursorRow));
                    await ClosePopupAsync(ct);
                    await FullRedrawAsync(ct);
                    break;
                case 1: // Copy Line
                    if (_cursorRow < _doc.TotalLines())
                    {
                        _clipboardLine = _doc.Lines[_cursorRow];
                        _clipboardColors = new List<byte>(_doc.Colors[_cursorRow]);
                    }
                    await ClosePopupAsync(ct);
                    await ShowMessageAsync("line copied", ct);
                    await FullRedrawAsync(ct);
                    break;
                case 2: // Paste Line
                    if (_clipboardLine.Length > 0 || _clipboardColors.Count > 0)
                    {
                        _doc.InsertLine(_cursorRow + 1, _clipboardLine, new List<byte>(_clipboardColors));
                        _cursorRow++;
                    }
                    await ClosePopupAsync(ct);
                    await FullRedrawAsync(ct);
                    break;
                case 3: // Delete Line
                    _doc.DeleteLine(_cursorRow);
                    if (_cursorRow >= _doc.TotalLines()) _cursorRow = Math.Max(0, _doc.TotalLines() - 1);
                    _cursorCol = Math.Min(_cursorCol, _doc.LineLen(_cursorRow));
                    await ClosePopupAsync(ct);
                    await FullRedrawAsync(ct);
                    break;
                case 4: // Duplicate Line
                    _doc.DuplicateLine(_cursorRow);
                    _cursorRow++;
                    await ClosePopupAsync(ct);
                    await FullRedrawAsync(ct);
                    break;
                case 5: // Auto-Wrap Para (simplified)
                    await ClosePopupAsync(ct);
                    await FullRedrawAsync(ct);
                    break;
            }
        }
        else if (b == WriterConstants.KeyRunStop || b == WriterConstants.KeyF3)
        {
            await ClosePopupAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // View Menu
    // -----------------------------------------------------------------------

    private async Task ProcessViewMenuKeyAsync(byte b, CancellationToken ct)
    {
        if (b == WriterConstants.KeyCrsrDown)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected + 1) % ViewMenuItems.Length;
            await _popup.UpdateMenuSelectionAsync("View", ViewMenuItems, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyCrsrUp)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected - 1 + ViewMenuItems.Length) % ViewMenuItems.Length;
            await _popup.UpdateMenuSelectionAsync("View", ViewMenuItems, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyReturn)
        {
            switch (_menuSelected)
            {
                case 0: await DoViewGotoAsync(ct); break;
                case 1: await DoViewFindReplaceAsync(ct); break;
                case 2: await DoViewThemesAsync(ct); break;
                case 3: await DoViewTimerAsync(ct); break;
                case 4: await DoViewWordCountAsync(ct); break;
                case 5: await DoViewDocInfoAsync(ct); break;
                case 6: await DoViewSpellcheckAsync(ct); break;
                default:
                    await ClosePopupAsync(ct);
                    break;
            }
        }
        else if (b == WriterConstants.KeyRunStop || b == WriterConstants.KeyF5)
        {
            await ClosePopupAsync(ct);
        }
    }

    private async Task DoViewGotoAsync(CancellationToken ct)
    {
        _state = WriterState.InputDialog;
        _inputText = "";
        _inputTitle = "Goto Line Number:";
        _inputCallback = async text =>
        {
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            if (int.TryParse(text, out var lineNum))
            {
                _cursorRow = Math.Max(0, Math.Min(lineNum - 1, _doc.TotalLines() - 1));
                _cursorCol = 0;
            }
            var (ty, tx) = CalculateVisibleViewport();
            await RenderViewportAsync(ty, tx, forceRedraw: true, ct: ct);
            await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
            await PlaceCursorAsync(ct);
        };
        await _client.RestoreScreenBufferAsync(0, ct);
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawInputDialogAsync(_inputTitle, _inputText, ct);
    }

    private async Task DoViewThemesAsync(CancellationToken ct)
    {
        _state = WriterState.ThemeSelect;
        _menuSelected = _theme.ThemeIndex;
        await _client.RestoreScreenBufferAsync(0, ct);
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        var themeNames = WriterTheme.BuiltInThemes.Select(t => t.Name).ToArray();
        await _popup.DrawMenuPopupAsync("Themes", themeNames, _menuSelected, ct);
    }

    private async Task DoViewWordCountAsync(CancellationToken ct)
    {
        var totalChars = _doc.Lines.Sum(l => l.Length);
        var totalWords = _doc.Lines.Sum(l => l.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
        var totalLines = _doc.TotalLines();
        await ClosePopupAsync(ct);
        await ShowMessageAsync($"l:{totalLines} w:{totalWords} c:{totalChars}", ct);
        await FullRedrawAsync(ct);
    }

    private async Task DoViewDocInfoAsync(CancellationToken ct)
    {
        await ClosePopupAsync(ct);
        await ShowMessageAsync($"{_doc.Filename} {_doc.TotalLines()}l", ct);
        await FullRedrawAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Find & Replace
    // -----------------------------------------------------------------------

    private async Task DoViewFindReplaceAsync(CancellationToken ct)
    {
        _state = WriterState.InputDialog;
        _inputText = "";
        _inputTitle = "Find Text:";
        _inputCallback = async findQuery =>
        {
            if (string.IsNullOrEmpty(findQuery))
            {
                _state = WriterState.Editor;
                await _client.RestoreScreenBufferAsync(0, ct);
                await FullRedrawAsync(ct);
                return;
            }
            _findReplace.FindQuery = findQuery;
            _state = WriterState.InputDialog;
            _inputText = "";
            _inputTitle = "Replace With:";
            _inputCallback = async repQuery =>
            {
                _findReplace.ReplaceQuery = repQuery;
                _findReplace.Search(_doc);
                if (_findReplace.Matches.Count == 0)
                {
                    _state = WriterState.Editor;
                    await _client.RestoreScreenBufferAsync(0, ct);
                    await ShowMessageAsync("no matches found", ct);
                    await FullRedrawAsync(ct);
                    return;
                }
                _state = WriterState.FindReplace;
                _findReplace.MatchIndex = 0;
                await _client.RestoreScreenBufferAsync(0, ct);
                await _client.SaveScreenBufferAsync(0, ct);
                await FocusMatchAsync(ct);
            };
            await _client.RestoreScreenBufferAsync(0, ct);
            await _client.SaveScreenBufferAsync(0, ct);
            await _client.SetCursorVisibilityAsync(false, ct);
            await _popup.DrawInputDialogAsync(_inputTitle, _inputText, ct);
        };
        await _client.RestoreScreenBufferAsync(0, ct);
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawInputDialogAsync(_inputTitle, _inputText, ct);
    }

    private async Task ProcessFindReplaceKeyAsync(byte b, CancellationToken ct)
    {
        var ch = (b & 0x7F) is >= 32 and <= 126 ? char.ToLowerInvariant((char)(b & 0x7F)) : '\0';
        if (b == WriterConstants.KeyCrsrRight || ch == 'n')
        {
            if (_findReplace.Matches.Count > 0)
            {
                _findReplace.MatchIndex = (_findReplace.MatchIndex + 1) % _findReplace.Matches.Count;
                await _client.RestoreScreenBufferAsync(0, ct);
                await FocusMatchAsync(ct);
            }
        }
        else if (ch == 'r')
        {
            if (_findReplace.Matches.Count > 0)
            {
                var more = _findReplace.ReplaceCurrent(_doc);
                if (!more)
                {
                    _state = WriterState.Editor;
                    await _client.RestoreScreenBufferAsync(0, ct);
                    await ShowMessageAsync("all occurrences replaced", ct);
                    await FullRedrawAsync(ct);
                }
                else
                {
                    await _client.RestoreScreenBufferAsync(0, ct);
                    await FocusMatchAsync(ct);
                }
            }
        }
        else if (ch == 'a')
        {
            var count = _findReplace.ReplaceAll(_doc);
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            await ShowMessageAsync($"replaced {count} occurrences", ct);
            await FullRedrawAsync(ct);
        }
        else if (ch == 'x' || b == WriterConstants.KeyRunStop || b == WriterConstants.KeyReturn)
        {
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            await FullRedrawAsync(ct);
        }
    }

    private async Task FocusMatchAsync(CancellationToken ct)
    {
        if (_findReplace.Matches.Count == 0) return;
        var (row, col) = _findReplace.Matches[_findReplace.MatchIndex];
        _cursorRow = row;
        _cursorCol = col;

        // Scroll viewport to show match
        if (_cursorRow < _viewportY)
            _viewportY = Math.Max(0, _cursorRow - 5);
        else if (_cursorRow >= _viewportY + WriterConstants.ViewportRows)
            _viewportY = Math.Max(0, _cursorRow - WriterConstants.ViewportRows + 5);

        if (_cursorCol < _viewportX)
            _viewportX = Math.Max(0, _cursorCol - 10);
        else if (_cursorCol >= _viewportX + WriterConstants.ViewportCols)
            _viewportX = Math.Max(0, _cursorCol - WriterConstants.ViewportCols + 15);

        await FullRedrawAsync(ct);

        // Flash match
        var screenCol = col - _viewportX;
        var screenRow = row - _viewportY + WriterConstants.ViewportTop;
        if (screenCol >= 0 && screenCol < WriterConstants.ViewportCols &&
            screenRow >= WriterConstants.ViewportTop && screenRow <= WriterConstants.ViewportBot)
        {
            var visLen = Math.Min(_findReplace.FindQuery.Length, WriterConstants.ViewportCols - screenCol);
            if (visLen > 0)
            {
                var flashColors = new byte[visLen];
                Array.Fill(flashColors, (byte)1); // white flash
                for (var flash = 0; flash < 2; flash++)
                {
                    await _client.FillColorBlockAsync((byte)screenCol, (byte)screenRow, (byte)visLen, 1, flashColors, ct);
                    await Task.Delay(80, ct);
                    var blackColors = new byte[visLen];
                    await _client.FillColorBlockAsync((byte)screenCol, (byte)screenRow, (byte)visLen, 1, blackColors, ct);
                    await Task.Delay(80, ct);
                }
                // Restore original colors
                var origColors = _doc.Colors[row].Skip(col).Take(visLen).ToArray();
                if (origColors.Length == visLen)
                    await _client.FillColorBlockAsync((byte)screenCol, (byte)screenRow, (byte)visLen, 1, origColors, ct);
            }
        }

        // Draw hint popup
        await _client.SaveScreenBufferAsync(0, ct);
        await _popup.DrawFindReplacePopupAsync(_findReplace.MatchIndex, _findReplace.Matches.Count, screenRow, ct);
    }

    // -----------------------------------------------------------------------
    // Spell Check
    // -----------------------------------------------------------------------

    private async Task DoViewSpellcheckAsync(CancellationToken ct)
    {
        await ClosePopupAsync(ct);
        if (!_spellChecker.IsLoaded)
        {
            await ShowMessageAsync("dictionary unavailable", ct);
            await FullRedrawAsync(ct);
            return;
        }
        _spellErrors = _spellChecker.FindErrors(_doc);
        if (_spellErrors.Count == 0)
        {
            await ShowMessageAsync("no spelling errors", ct);
            await FullRedrawAsync(ct);
            return;
        }
        _state = WriterState.Spellcheck;
        _spellIdx = 0;
        await ShowSpellErrorAsync(_spellIdx, true, ct);
    }

    private async Task ProcessSpellcheckKeyAsync(byte b, CancellationToken ct)
    {
        var ch = (b & 0x7F) is >= 32 and <= 126 ? char.ToLowerInvariant((char)(b & 0x7F)) : '\0';
        if (b == WriterConstants.KeyCrsrRight || ch == 'n')
        {
            if (_spellErrors.Count > 0)
                await ShowSpellErrorAsync((_spellIdx + 1) % _spellErrors.Count, false, ct);
        }
        else if (b == WriterConstants.KeyCrsrLeft || ch == 'p')
        {
            if (_spellErrors.Count > 0)
                await ShowSpellErrorAsync((_spellIdx - 1 + _spellErrors.Count) % _spellErrors.Count, false, ct);
        }
        else if (ch == 'r')
        {
            await SpellReplaceCurrentAsync(ct);
        }
        else if (ch == 'x' || b == WriterConstants.KeyRunStop || b == WriterConstants.KeyReturn)
        {
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            await FullRedrawAsync(ct);
        }
    }

    private async Task ShowSpellErrorAsync(int idx, bool fresh, CancellationToken ct)
    {
        _spellIdx = idx;
        var (row, col, word) = _spellErrors[_spellIdx];
        _cursorRow = row;
        _cursorCol = col;

        // Scroll to show the error
        var (ty, tx) = CalculateVisibleViewport();
        var needsScroll = (ty != _viewportY || tx != _viewportX);
        _viewportY = ty;
        _viewportX = tx;

        if (fresh || needsScroll)
        {
            // Full repaint on first entry, after replace, or when viewport scrolls
            await _client.SetCursorVisibilityAsync(false, ct);
            await _client.SetColorsAsync(_theme.Bg, _theme.EditorFg, ct);
            await _client.ClearScreenAsync(ct);
            await _chrome.DrawEditorChromeAsync(_doc, _viewportX, _cursorRow, _cursorCol, _insertMode, ct: ct);
            await RenderViewportAsync(forceRedraw: true, ct: ct);
        }
        else
        {
            // Restore bank 0 (removes popup), then re-render the previous word's row to clear red
            await _client.RestoreScreenBufferAsync(0, ct);
            if (_spellPrevRow >= 0)
            {
                var prevScreenRow = _spellPrevRow - _viewportY + WriterConstants.ViewportTop;
                if (prevScreenRow >= WriterConstants.ViewportTop && prevScreenRow <= WriterConstants.ViewportBot)
                    await _viewport.RenderSingleRowAsync(_doc, _spellPrevRow, prevScreenRow, _viewportX, ct);
            }
        }

        _spellPrevRow = row;

        // Highlight misspelled word with flash then red
        var screenRow = row - _viewportY + WriterConstants.ViewportTop;
        var screenCol = col - _viewportX;
        if (screenCol >= 0 && screenCol < WriterConstants.ViewportCols &&
            screenRow >= WriterConstants.ViewportTop && screenRow <= WriterConstants.ViewportBot)
        {
            var wlen = Math.Min(word.Length, WriterConstants.ViewportCols - screenCol);
            if (wlen > 0)
            {
                var redColors = new byte[wlen];
                Array.Fill(redColors, (byte)2); // red
                var whiteColors = new byte[wlen];
                Array.Fill(whiteColors, (byte)1); // white
                // Flash white/red to draw attention
                for (var flash = 0; flash < 3; flash++)
                {
                    await _client.FillColorBlockAsync((byte)screenCol, (byte)screenRow, (byte)wlen, 1, whiteColors, ct);
                    await Task.Delay(100, ct);
                    await _client.FillColorBlockAsync((byte)screenCol, (byte)screenRow, (byte)wlen, 1, redColors, ct);
                    await Task.Delay(100, ct);
                }
            }
        }

        _spellSuggestion = _spellChecker.Suggest(word);
        // Save state with red highlight (no popup) to bank 0
        await _client.SaveScreenBufferAsync(0, ct);
        await _client.SetCursorVisibilityAsync(false, ct);
        await _popup.DrawSpellPopupAsync(word, _spellSuggestion, _spellIdx, _spellErrors.Count, screenRow, ct);
    }

    private async Task SpellReplaceCurrentAsync(CancellationToken ct)
    {
        if (string.IsNullOrEmpty(_spellSuggestion)) return;
        var (row, col, word) = _spellErrors[_spellIdx];
        var line = _doc.Lines[row];
        var colors = _doc.Colors[row];
        var baseColor = col < colors.Count ? colors[col] : _doc.DefaultColor;

        _doc.Lines[row] = line[..col] + _spellSuggestion + line[(col + word.Length)..];
        var newColors = new List<byte>(colors.GetRange(0, col));
        for (var i = 0; i < _spellSuggestion.Length; i++)
            newColors.Add(baseColor);
        if (col + word.Length < colors.Count)
            newColors.AddRange(colors.GetRange(col + word.Length, colors.Count - (col + word.Length)));
        _doc.Colors[row] = newColors;
        _doc.Modified = true;

        // Re-scan
        _spellErrors = _spellChecker.FindErrors(_doc);
        if (_spellErrors.Count == 0)
        {
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            await ShowMessageAsync("spell check complete", ct);
            await FullRedrawAsync(ct);
            return;
        }
        var newIdx = Math.Min(_spellIdx, _spellErrors.Count - 1);
        await ShowSpellErrorAsync(newIdx, true, ct);
    }

    // -----------------------------------------------------------------------
    // Focus Timer
    // -----------------------------------------------------------------------

    private async Task DoViewTimerAsync(CancellationToken ct)
    {
        if (!_focusTimer.IsActive)
        {
            _focusTimer.Start();
            await ClosePopupAsync(ct);
            await ShowMessageAsync("focus timer started: 25m", ct);
        }
        else
        {
            _focusTimer.Stop();
            await ClosePopupAsync(ct);
            await ShowMessageAsync("focus timer stopped", ct);
        }
        await FullRedrawAsync(ct);
    }

    private async Task TickTimerAsync(CancellationToken ct)
    {
        if (!_focusTimer.IsActive) return;
        var result = _focusTimer.Tick();
        if (result is null) return;
        if (result == "")
        {
            // Timer finished
            await _chrome.DrawTitleBarAsync("", ct);
            await ShowMessageAsync("pomodoro finished! take a break", ct);
            await FullRedrawAsync(ct);
        }
        else if (_state == WriterState.Editor)
        {
            // Update timer directly in screen memory — no cursor movement
            await _chrome.UpdateTimerInPlaceAsync(result, ct);
        }
    }

    // -----------------------------------------------------------------------
    // AutoSave
    // -----------------------------------------------------------------------

    private async Task CheckAutoSaveAsync(CancellationToken ct)
    {
        if (_state != WriterState.Editor) return;
        _autoSave.Check(_doc);
        if (!_autoSave.ShouldTrigger) return;
        _autoSave.Execute(_doc);
        // Flash "autosaved" briefly in status bar area
        await _client.SetCursorVisibilityAsync(false, ct);
        var msg = " autosaved ".PadLeft((WriterConstants.ScreenWidth + 11) / 2).PadRight(WriterConstants.ScreenWidth);
        await _client.SetCursorAsync(0, 24, ct);
        await _client.DrawColoredWindowRawAsync((Rift64Color)_theme.Highlight, WriterConstants.ScreenWidth, 1,
            LowercaseScreenCodeConverter.Encode(msg), ct);
        await Task.Delay(400, ct);
        // Restore status bar
        await _chrome.DrawStatusBarAsync(_doc, _cursorRow, _cursorCol, _insertMode, ct);
        await PlaceCursorAsync(ct);
    }

    // -----------------------------------------------------------------------
    // Theme Select
    // -----------------------------------------------------------------------

    private async Task ProcessThemeSelectKeyAsync(byte b, CancellationToken ct)
    {
        var themeNames = WriterTheme.BuiltInThemes.Select(t => t.Name).ToArray();
        if (b == WriterConstants.KeyCrsrDown)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected + 1) % themeNames.Length;
            await _popup.UpdateMenuSelectionAsync("Themes", themeNames, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyCrsrUp)
        {
            var oldIdx = _menuSelected;
            _menuSelected = (_menuSelected - 1 + themeNames.Length) % themeNames.Length;
            await _popup.UpdateMenuSelectionAsync("Themes", themeNames, oldIdx, _menuSelected, ct);
        }
        else if (b == WriterConstants.KeyReturn)
        {
            _state = WriterState.Editor;
            _theme.ApplyTheme(_menuSelected);
            _doc.DefaultColor = _theme.EditorFg;
            await _client.SetColorsAsync(_theme.Bg, _theme.EditorFg, ct);
            await FullRedrawAsync(ct);
        }
        else if (b == WriterConstants.KeyRunStop || b == WriterConstants.KeyF5)
        {
            await ClosePopupAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // File Browser
    // -----------------------------------------------------------------------

    private async Task ProcessFileBrowserKeyAsync(byte b, CancellationToken ct)
    {
        if (_fileList.Length == 0)
        {
            await ClosePopupAsync(ct);
            return;
        }
        const int maxVisible = 11;
        if (b == WriterConstants.KeyCrsrDown)
        {
            if (_fileBrowserSelected < _fileList.Length - 1)
            {
                var oldIdx = _fileBrowserSelected;
                _fileBrowserSelected++;
                if (_fileBrowserSelected >= _fileBrowserScroll + maxVisible)
                {
                    _fileBrowserScroll++;
                    await _popup.DrawFileBrowserAsync(_fileList, _fileBrowserSelected, _fileBrowserScroll, ct);
                }
                else
                {
                    await _popup.UpdateFileBrowserSelectionAsync(_fileList, oldIdx, _fileBrowserSelected, _fileBrowserScroll, ct);
                }
            }
        }
        else if (b == WriterConstants.KeyCrsrUp)
        {
            if (_fileBrowserSelected > 0)
            {
                var oldIdx = _fileBrowserSelected;
                _fileBrowserSelected--;
                if (_fileBrowserSelected < _fileBrowserScroll)
                {
                    _fileBrowserScroll--;
                    await _popup.DrawFileBrowserAsync(_fileList, _fileBrowserSelected, _fileBrowserScroll, ct);
                }
                else
                {
                    await _popup.UpdateFileBrowserSelectionAsync(_fileList, oldIdx, _fileBrowserSelected, _fileBrowserScroll, ct);
                }
            }
        }
        else if (b == WriterConstants.KeyReturn)
        {
            if (_fileList.Length > 0)
            {
                var filename = _fileList[_fileBrowserSelected];
                DocumentFileManager.Load(_doc, filename);
                _cursorRow = 0;
                _cursorCol = 0;
                _viewportY = 0;
                _viewportX = 0;
                _autoSave.Reset();
                _state = WriterState.Editor;
                await _client.RestoreScreenBufferAsync(0, ct);
                await FullRedrawAsync(ct);
            }
            else
            {
                await ClosePopupAsync(ct);
            }
        }
        else if (b == WriterConstants.KeyRunStop)
        {
            await ClosePopupAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // Input Dialog
    // -----------------------------------------------------------------------

    private async Task ProcessInputDialogKeyAsync(byte b, CancellationToken ct)
    {
        if (b == WriterConstants.KeyReturn)
        {
            var callback = _inputCallback;
            _inputCallback = null;
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            if (callback is not null)
                await callback(_inputText);
        }
        else if (b == WriterConstants.KeyRunStop)
        {
            _inputCallback = null;
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            await FullRedrawAsync(ct);
        }
        else if (b == WriterConstants.KeyDel || b == 8)
        {
            if (_inputText.Length > 0)
            {
                _inputText = _inputText[..^1];
                await _popup.DrawInputDialogAsync(_inputTitle, _inputText, ct, fullRedraw: false);
            }
        }
        else if (b >= 32 && b <= 126)
        {
            if (_inputText.Length < 24)
            {
                var ch = (char)b;
                if (ch >= 'A' && ch <= 'Z') ch = char.ToLowerInvariant(ch);
                _inputText += ch;
                await _popup.DrawInputDialogAsync(_inputTitle, _inputText, ct, fullRedraw: false);
            }
        }
    }

    // -----------------------------------------------------------------------
    // Confirm Dialog
    // -----------------------------------------------------------------------

    private async Task ProcessConfirmDialogKeyAsync(byte b, CancellationToken ct)
    {
        var ch = (char)(b & 0x7F);
        if (ch is 'y' or 'Y')
        {
            var callback = _confirmCallback;
            _confirmCallback = null;
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            if (callback is not null)
                await callback(true);
        }
        else if (ch is 'n' or 'N' || b == WriterConstants.KeyRunStop)
        {
            var callback = _confirmCallback;
            _confirmCallback = null;
            _state = WriterState.Editor;
            await _client.RestoreScreenBufferAsync(0, ct);
            if (callback is not null)
                await callback(false);
            else
                await FullRedrawAsync(ct);
        }
    }

    // -----------------------------------------------------------------------
    // Utilities
    // -----------------------------------------------------------------------

    private async Task ShowMessageAsync(string message, CancellationToken ct)
    {
        var msg = message.Length > WriterConstants.ScreenWidth
            ? message[..WriterConstants.ScreenWidth]
            : message.PadLeft((WriterConstants.ScreenWidth + message.Length) / 2).PadRight(WriterConstants.ScreenWidth);
        await _client.SetCursorAsync(0, 24, ct);
        await _client.DrawColoredWindowRawAsync((Rift64Color)_theme.Highlight, WriterConstants.ScreenWidth, 1,
            LowercaseScreenCodeConverter.Encode(msg), ct);
        await Task.Delay(800, ct);
    }
}
