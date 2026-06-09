using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace RiftGate;

public sealed class RiftGateAdminConfig
{
    public string AdminUsername { get; set; } = "admin";
    public string AdminPassword { get; set; } = "changeme";
    public int MaxConnections { get; set; } = 8;
    public bool EnableWaveAnimation { get; set; } = true;
    public bool EnableSpriteAnimation { get; set; } = true;
    public bool EnableTitleFader { get; set; } = true;
    public int WebPort { get; set; } = 8088;
}

public readonly record struct RiftGateRuntimeSettings(
    int MaxConnections,
    bool EnableWaveAnimation,
    bool EnableSpriteAnimation,
    bool EnableTitleFader,
    int WebPort);

public sealed class AppCsvDocument
{
    public List<string> Comments { get; init; } = new();
    public List<RegisteredApp> Apps { get; init; } = new();
}

public sealed class RiftGateSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static readonly string[] DefaultComments =
    [
        "# Rift Gate Registered Apps database",
        "# Format: dns/ip, port, name, description, useTls, validateCert"
    ];

    private readonly object _gate = new();
    private readonly string _configPath;
    private readonly string _appsCsvPath;

    private RiftGateAdminConfig _config;
    private long _version;

    private RiftGateSettingsStore(string configPath, string appsCsvPath, RiftGateAdminConfig config)
    {
        _configPath = configPath;
        _appsCsvPath = appsCsvPath;
        _config = config;
    }

    public string ConfigPath => _configPath;

    public string AppsCsvPath => _appsCsvPath;

    public long Version
    {
        get
        {
            lock (_gate)
            {
                return _version;
            }
        }
    }

    public static RiftGateSettingsStore LoadOrCreate(string configPath, string appsCsvPath, int defaultMaxConnections)
    {
        var config = LoadConfig(configPath, defaultMaxConnections);
        var store = new RiftGateSettingsStore(configPath, appsCsvPath, config);
        store.PersistConfig();
        store.EnsureAppsFile();
        return store;
    }

    public RiftGateRuntimeSettings GetRuntimeSettings()
    {
        lock (_gate)
        {
            return new RiftGateRuntimeSettings(
                _config.MaxConnections,
                _config.EnableWaveAnimation,
                _config.EnableSpriteAnimation,
                _config.EnableTitleFader,
                _config.WebPort);
        }
    }

    public bool ValidateCredentials(string username, string password)
    {
        lock (_gate)
        {
            return string.Equals(username, _config.AdminUsername, StringComparison.Ordinal) &&
                   string.Equals(password, _config.AdminPassword, StringComparison.Ordinal);
        }
    }

    public RiftGateAdminConfig GetAdminConfigSnapshot()
    {
        lock (_gate)
        {
            return new RiftGateAdminConfig
            {
                AdminUsername = _config.AdminUsername,
                AdminPassword = _config.AdminPassword,
                MaxConnections = _config.MaxConnections,
                EnableWaveAnimation = _config.EnableWaveAnimation,
                EnableSpriteAnimation = _config.EnableSpriteAnimation,
                EnableTitleFader = _config.EnableTitleFader,
                WebPort = _config.WebPort
            };
        }
    }

    public AppCsvDocument GetAppsDocument()
    {
        lock (_gate)
        {
            EnsureAppsFile();
            return LoadAppsDocument(_appsCsvPath);
        }
    }

    public void UpdateDashboard(int maxConnections, bool enableWaveAnimation, bool enableSpriteAnimation, bool enableTitleFader)
    {
        if (maxConnections <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnections), "Max connections must be greater than zero.");
        }

        lock (_gate)
        {
            _config.MaxConnections = maxConnections;
            _config.EnableWaveAnimation = enableWaveAnimation;
            _config.EnableSpriteAnimation = enableSpriteAnimation;
            _config.EnableTitleFader = enableTitleFader;
            PersistConfig();
            _version++;
        }
    }

    public int SaveApp(int? index, RegisteredApp app)
    {
        ValidateApp(app);

        lock (_gate)
        {
            EnsureAppsFile();
            var document = LoadAppsDocument(_appsCsvPath);

            if (index is >= 0 && index.Value < document.Apps.Count)
            {
                document.Apps[index.Value] = CloneApp(app);
                SaveAppsDocument(document);
                _version++;
                return index.Value;
            }

            document.Apps.Add(CloneApp(app));
            SaveAppsDocument(document);
            _version++;
            return document.Apps.Count - 1;
        }
    }

    public int DeleteApp(int index)
    {
        lock (_gate)
        {
            EnsureAppsFile();
            var document = LoadAppsDocument(_appsCsvPath);
            if (index < 0 || index >= document.Apps.Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index), "The selected record no longer exists.");
            }

            document.Apps.RemoveAt(index);
            SaveAppsDocument(document);
            _version++;
            return document.Apps.Count == 0 ? -1 : Math.Min(index, document.Apps.Count - 1);
        }
    }

    private void EnsureAppsFile()
    {
        if (File.Exists(_appsCsvPath))
        {
            return;
        }

        var directory = Path.GetDirectoryName(_appsCsvPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_appsCsvPath, string.Join(Environment.NewLine, DefaultComments) + Environment.NewLine);
    }

    private static RiftGateAdminConfig LoadConfig(string configPath, int defaultMaxConnections)
    {
        try
        {
            if (File.Exists(configPath))
            {
                var loaded = JsonSerializer.Deserialize<RiftGateAdminConfig>(File.ReadAllText(configPath));
                return SanitizeConfig(loaded, defaultMaxConnections);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Admin] Failed to read config '{configPath}': {ex.Message}");
        }

        return SanitizeConfig(null, defaultMaxConnections);
    }

    private void PersistConfig()
    {
        var directory = Path.GetDirectoryName(_configPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(_configPath, JsonSerializer.Serialize(_config, JsonOptions));
    }

    private void SaveAppsDocument(AppCsvDocument document)
    {
        File.WriteAllText(_appsCsvPath, SerializeAppsDocument(document));
    }

    private static AppCsvDocument LoadAppsDocument(string csvPath)
    {
        var document = new AppCsvDocument();

        if (!File.Exists(csvPath))
        {
            document.Comments.AddRange(DefaultComments);
            return document;
        }

        foreach (var rawLine in File.ReadAllLines(csvPath))
        {
            var trimmed = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            if (trimmed.StartsWith('#'))
            {
                document.Comments.Add(rawLine);
                continue;
            }

            var parts = ParseCsvLine(rawLine);
            if (parts.Length < 4 || !int.TryParse(parts[1], out var port))
            {
                continue;
            }

            bool useTls = parts.Length >= 5 && (parts[4] == "1" || string.Equals(parts[4], "true", StringComparison.OrdinalIgnoreCase));
            bool validateCert = parts.Length >= 6 && (parts[5] == "1" || string.Equals(parts[5], "true", StringComparison.OrdinalIgnoreCase));

            document.Apps.Add(new RegisteredApp
            {
                Host = parts[0],
                Port = port,
                Name = parts[2],
                Description = parts[3],
                UseTls = useTls,
                ValidateCertificate = validateCert
            });
        }

        if (document.Comments.Count == 0)
        {
            document.Comments.AddRange(DefaultComments);
        }

        return document;
    }

    private static string SerializeAppsDocument(AppCsvDocument document)
    {
        var lines = new List<string>();
        lines.AddRange(document.Comments.Count == 0 ? DefaultComments : document.Comments);

        foreach (var app in document.Apps)
        {
            lines.Add(string.Join(", ",
                EscapeCsvField(app.Host),
                app.Port.ToString(),
                EscapeCsvField(app.Name),
                EscapeCsvField(app.Description),
                app.UseTls ? "1" : "0",
                app.ValidateCertificate ? "1" : "0"));
        }

        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    private static string EscapeCsvField(string value)
    {
        var normalized = value.Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace("\r", "\n", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Trim();

        return $"\"{normalized.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static string[] ParseCsvLine(string line)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == ',' && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString().Trim());
        return result.ToArray();
    }

    private static RegisteredApp CloneApp(RegisteredApp app) => new()
    {
        Host = app.Host.Trim(),
        Port = app.Port,
        Name = app.Name.Trim(),
        Description = app.Description.Trim(),
        UseTls = app.UseTls,
        ValidateCertificate = app.ValidateCertificate
    };

    private static void ValidateApp(RegisteredApp app)
    {
        if (string.IsNullOrWhiteSpace(app.Host))
        {
            throw new ArgumentException("Host cannot be empty.", nameof(app));
        }

        if (app.Port <= 0 || app.Port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(app), "Port must be between 1 and 65535.");
        }

        if (string.IsNullOrWhiteSpace(app.Name))
        {
            throw new ArgumentException("App name cannot be empty.", nameof(app));
        }

        if (string.IsNullOrWhiteSpace(app.Description))
        {
            throw new ArgumentException("Description cannot be empty.", nameof(app));
        }
    }

    private static RiftGateAdminConfig SanitizeConfig(RiftGateAdminConfig? config, int defaultMaxConnections)
    {
        config ??= new RiftGateAdminConfig();

        if (string.IsNullOrWhiteSpace(config.AdminUsername))
        {
            config.AdminUsername = "admin";
        }

        if (string.IsNullOrWhiteSpace(config.AdminPassword))
        {
            config.AdminPassword = "changeme";
        }

        if (config.MaxConnections <= 0)
        {
            config.MaxConnections = defaultMaxConnections;
        }

        if (config.WebPort <= 0)
        {
            config.WebPort = 8088;
        }

        return config;
    }
}

public sealed class RiftGateAdminWebServer : IAsyncDisposable
{
    private const string SessionCookieName = "riftgate_session";
    private static readonly TimeSpan SessionLifetime = TimeSpan.FromHours(12);

    private readonly HttpListener _listener = new();
    private readonly RiftGateSettingsStore _settingsStore;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _sessions = new();

    private CancellationTokenSource? _cancellationSource;
    private Task? _acceptLoopTask;

    public RiftGateAdminWebServer(RiftGateSettingsStore settingsStore)
    {
        _settingsStore = settingsStore;

        var webPort = settingsStore.GetRuntimeSettings().WebPort;
        BaseUrl = $"http://localhost:{webPort}/";
        _listener.Prefixes.Add(BaseUrl);
        _listener.Prefixes.Add($"http://127.0.0.1:{webPort}/");
    }

    public string BaseUrl { get; }

    public Task StartAsync()
    {
        _listener.Start();
        _cancellationSource = new CancellationTokenSource();
        _acceptLoopTask = Task.Run(() => AcceptLoopAsync(_cancellationSource.Token));
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_cancellationSource is null)
        {
            return;
        }

        _cancellationSource.Cancel();
        _listener.Close();

        if (_acceptLoopTask is not null)
        {
            try
            {
                await _acceptLoopTask.ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        _cancellationSource.Dispose();
        _listener.Close();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var context = await _listener.GetContextAsync().WaitAsync(cancellationToken).ConfigureAwait(false);
                _ = Task.Run(() => HandleRequestAsync(context, cancellationToken), cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (HttpListenerException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        try
        {
            var path = context.Request.Url?.AbsolutePath ?? "/";

            if (path.Equals("/favicon.ico", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = (int)HttpStatusCode.NoContent;
                return;
            }

            if (path.Equals("/login", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await WriteHtmlAsync(context.Response, RenderLoginPage(null), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/login", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleLoginAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/logout", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                HandleLogout(context.Response, context.Request);
                Redirect(context.Response, "/login");
                return;
            }

            if (!IsAuthenticated(context.Request))
            {
                Redirect(context.Response, "/login");
                return;
            }

            if (path.Equals("/", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("GET", StringComparison.OrdinalIgnoreCase))
            {
                await RenderDashboardAsync(context.Response, context.Request, GetFlashMessage(context.Request), cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/settings", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleSettingsSaveAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/apps/save", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAppSaveAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            if (path.Equals("/apps/delete", StringComparison.OrdinalIgnoreCase) && context.Request.HttpMethod.Equals("POST", StringComparison.OrdinalIgnoreCase))
            {
                await HandleAppDeleteAsync(context, cancellationToken).ConfigureAwait(false);
                return;
            }

            context.Response.StatusCode = (int)HttpStatusCode.NotFound;
            await WriteHtmlAsync(context.Response, RenderSimplePage("Not Found", "The requested page does not exist."), cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await RenderDashboardAsync(context.Response, context.Request, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (ArgumentException ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            await RenderDashboardAsync(context.Response, context.Request, ex.Message, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            await WriteHtmlAsync(context.Response, RenderSimplePage("Server Error", WebUtility.HtmlEncode(ex.Message)), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Response.OutputStream.Close();
        }
    }

    private async Task RenderDashboardAsync(
        HttpListenerResponse response,
        HttpListenerRequest request,
        string? message,
        CancellationToken cancellationToken)
    {
        var config = _settingsStore.GetAdminConfigSnapshot();
        var document = _settingsStore.GetAppsDocument();
        bool isNewRecord = string.Equals(request.QueryString["mode"], "new", StringComparison.OrdinalIgnoreCase);
        int selectedIndex = GetSelectedIndex(request, document.Apps.Count, isNewRecord);

        RegisteredApp editorRecord;
        if (isNewRecord)
        {
            editorRecord = new RegisteredApp();
        }
        else if (selectedIndex >= 0 && selectedIndex < document.Apps.Count)
        {
            editorRecord = document.Apps[selectedIndex];
        }
        else
        {
            editorRecord = new RegisteredApp();
        }

        await WriteHtmlAsync(
            response,
            RenderDashboardPage(config, document, selectedIndex, editorRecord, isNewRecord, message),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task HandleLoginAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(context.Request, cancellationToken).ConfigureAwait(false);
        form.TryGetValue("username", out var username);
        form.TryGetValue("password", out var password);

        if (!_settingsStore.ValidateCredentials(username ?? string.Empty, password ?? string.Empty))
        {
            context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
            await WriteHtmlAsync(context.Response, RenderLoginPage("Invalid username or password."), cancellationToken).ConfigureAwait(false);
            return;
        }

        var token = Guid.NewGuid().ToString("N");
        _sessions[token] = DateTimeOffset.UtcNow.Add(SessionLifetime);
        context.Response.Cookies.Add(new Cookie(SessionCookieName, token)
        {
            HttpOnly = true,
            Path = "/"
        });

        Redirect(context.Response, "/");
    }

    private async Task HandleSettingsSaveAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (!form.TryGetValue("maxConnections", out var maxConnectionsValue) || !int.TryParse(maxConnectionsValue, out var maxConnections))
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnectionsValue), "Max connections must be a whole number greater than zero.");
        }

        _settingsStore.UpdateDashboard(
            maxConnections,
            form.ContainsKey("enableWaveAnimation"),
            form.ContainsKey("enableSpriteAnimation"),
            form.ContainsKey("enableTitleFader"));

        var selectedValue = form.GetValueOrDefault("selectedIndex") ?? string.Empty;
        var modeValue = form.GetValueOrDefault("mode") ?? string.Empty;
        Redirect(context.Response, BuildReturnUrl(selectedValue, modeValue, "settings"));
    }

    private async Task HandleAppSaveAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(context.Request, cancellationToken).ConfigureAwait(false);

        if (!int.TryParse(form.GetValueOrDefault("port"), out var port))
        {
            throw new ArgumentOutOfRangeException("port", "Port must be a whole number between 1 and 65535.");
        }

        int? selectedIndex = null;
        if (int.TryParse(form.GetValueOrDefault("selectedIndex"), out var parsedIndex) && parsedIndex >= 0)
        {
            selectedIndex = parsedIndex;
        }

        var app = new RegisteredApp
        {
            Host = form.GetValueOrDefault("host") ?? string.Empty,
            Port = port,
            Name = form.GetValueOrDefault("name") ?? string.Empty,
            Description = form.GetValueOrDefault("description") ?? string.Empty,
            UseTls = form.ContainsKey("useTls"),
            ValidateCertificate = form.ContainsKey("validateCertificate")
        };

        var savedIndex = _settingsStore.SaveApp(selectedIndex, app);
        Redirect(context.Response, BuildReturnUrl(savedIndex.ToString(), string.Empty, "app"));
    }

    private async Task HandleAppDeleteAsync(HttpListenerContext context, CancellationToken cancellationToken)
    {
        var form = await ReadFormAsync(context.Request, cancellationToken).ConfigureAwait(false);
        if (!int.TryParse(form.GetValueOrDefault("selectedIndex"), out var selectedIndex) || selectedIndex < 0)
        {
            throw new ArgumentOutOfRangeException("selectedIndex", "Select a record before deleting it.");
        }

        var nextIndex = _settingsStore.DeleteApp(selectedIndex);
        Redirect(context.Response, BuildReturnUrl(nextIndex >= 0 ? nextIndex.ToString() : string.Empty, nextIndex >= 0 ? string.Empty : "new", "deleted"));
    }

    private bool IsAuthenticated(HttpListenerRequest request)
    {
        CleanupExpiredSessions();

        var sessionCookie = request.Cookies[SessionCookieName]?.Value;
        if (string.IsNullOrWhiteSpace(sessionCookie))
        {
            return false;
        }

        if (_sessions.TryGetValue(sessionCookie, out var expiresAt) && expiresAt > DateTimeOffset.UtcNow)
        {
            return true;
        }

        _sessions.TryRemove(sessionCookie, out _);
        return false;
    }

    private void HandleLogout(HttpListenerResponse response, HttpListenerRequest request)
    {
        var sessionCookie = request.Cookies[SessionCookieName]?.Value;
        if (!string.IsNullOrWhiteSpace(sessionCookie))
        {
            _sessions.TryRemove(sessionCookie, out _);
        }

        response.Cookies.Add(new Cookie(SessionCookieName, string.Empty)
        {
            HttpOnly = true,
            Expires = DateTime.UtcNow.AddDays(-1),
            Path = "/"
        });
    }

    private void CleanupExpiredSessions()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var session in _sessions)
        {
            if (session.Value <= now)
            {
                _sessions.TryRemove(session.Key, out _);
            }
        }
    }

    private static async Task<Dictionary<string, string>> ReadFormAsync(HttpListenerRequest request, CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8, leaveOpen: true);
        var body = await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var part in body.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int splitIndex = part.IndexOf('=');
            if (splitIndex < 0)
            {
                continue;
            }

            var key = WebUtility.UrlDecode(part[..splitIndex]);
            var value = WebUtility.UrlDecode(part[(splitIndex + 1)..]);
            values[key] = value;
        }

        return values;
    }

    private static async Task WriteHtmlAsync(HttpListenerResponse response, string html, CancellationToken cancellationToken)
    {
        var buffer = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
    }

    private static void Redirect(HttpListenerResponse response, string location)
    {
        response.StatusCode = (int)HttpStatusCode.Redirect;
        response.RedirectLocation = location;
    }

    private static string BuildReturnUrl(string selectedIndexValue, string modeValue, string savedType)
    {
        var parts = new List<string> { $"saved={Uri.EscapeDataString(savedType)}" };
        if (!string.IsNullOrWhiteSpace(selectedIndexValue))
        {
            parts.Add($"selected={Uri.EscapeDataString(selectedIndexValue)}");
        }
        if (!string.IsNullOrWhiteSpace(modeValue))
        {
            parts.Add($"mode={Uri.EscapeDataString(modeValue)}");
        }

        return "/?" + string.Join("&", parts);
    }

    private static string? GetFlashMessage(HttpListenerRequest request)
    {
        return request.QueryString["saved"] switch
        {
            "settings" => "Gateway settings saved. Max connections applies the next time RiftGate starts.",
            "app" => "App record saved.",
            "deleted" => "App record deleted.",
            _ => null
        };
    }

    private static int GetSelectedIndex(HttpListenerRequest request, int count, bool isNewRecord)
    {
        if (isNewRecord)
        {
            return -1;
        }

        if (!int.TryParse(request.QueryString["selected"], out var selectedIndex))
        {
            return count > 0 ? 0 : -1;
        }

        if (count == 0)
        {
            return -1;
        }

        return Math.Clamp(selectedIndex, 0, count - 1);
    }

    private string RenderDashboardPage(
        RiftGateAdminConfig config,
        AppCsvDocument document,
        int selectedIndex,
        RegisteredApp editorRecord,
        bool isNewRecord,
        string? message)
    {
        var checkedWave = config.EnableWaveAnimation ? "checked" : string.Empty;
        var checkedSprites = config.EnableSpriteAnimation ? "checked" : string.Empty;
        var checkedTitleFader = config.EnableTitleFader ? "checked" : string.Empty;
        var encodedConfigPath = WebUtility.HtmlEncode(_settingsStore.ConfigPath);
        var encodedAppsPath = WebUtility.HtmlEncode(_settingsStore.AppsCsvPath);
        var messageHtml = string.IsNullOrWhiteSpace(message)
            ? string.Empty
            : $"<div class=\"notice\">{WebUtility.HtmlEncode(message)}</div>";
        var appListHtml = BuildAppListHtml(document.Apps, selectedIndex, isNewRecord);
        var selectedIndexValue = isNewRecord ? string.Empty : selectedIndex.ToString();
        var modeValue = isNewRecord ? "new" : string.Empty;
        var resetHref = !isNewRecord && selectedIndex >= 0 ? $"/?selected={selectedIndex}" : "/?mode=new";
        var disableDelete = isNewRecord || selectedIndex < 0 ? "disabled" : string.Empty;
        var modeTitle = isNewRecord ? "Add App Record" : "Edit App Record";
        var modeDetail = isNewRecord
            ? "Create a new entry with a clean field editor."
            : "Modify the selected record and save it back to apps.csv.";

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <title>RiftGate Admin</title>
    <style>
        :root {{
            --bg: #121822;
            --bg-gradient: radial-gradient(circle at top, #1a2336 0%, #121822 65%, #090c12 100%);
            --panel: rgba(26, 36, 52, 0.95);
            --panel-strong: #212c3f;
            --ink: #f1f5f9;
            --muted: #94a3b8;
            --accent: #14b8a6; /* Retro Mint */
            --accent-strong: #fb923c; /* Amber Orange */
            --line: #2e3c54;
            --line-strong: #3f5272;
            --selected: #1b2e3c;
            --input-bg: #171f2c;
            --notice-bg: #292823;
            --notice-border: var(--accent-strong);
            --crt-glow: rgba(20, 184, 166, 0.2);
            --shadow: 0 16px 40px rgba(0, 0, 0, 0.4);
        }}
        * {{ box-sizing: border-box; }}
        body {{
            margin: 0;
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
            background: var(--bg-gradient);
            color: var(--ink);
            line-height: 1.6;
        }}
        main {{ max-width: 1200px; margin: 28px auto; padding: 0 20px 40px; }}
        .panel {{
            background: var(--panel);
            border: 1px solid var(--line);
            box-shadow: var(--shadow);
            padding: 24px;
            margin-bottom: 20px;
            position: relative;
            overflow: hidden;
            border-radius: 8px;
        }}
        .panel::before {{
            content: '';
            position: absolute;
            top: 0; left: 0; width: 4px; height: 100%;
            background: var(--accent);
        }}
        .hero {{ display: flex; justify-content: space-between; gap: 20px; align-items: end; }}
        h1, h2, h3 {{ 
            font-family: Consolas, ""Courier New"", monospace;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: var(--accent);
            text-shadow: 0 0 8px var(--crt-glow);
            margin: 0; 
        }}
        h1 {{ font-size: 2.2rem; letter-spacing: 0.08em; }}
        h2 {{ font-size: 1.35rem; margin-bottom: 6px; border-bottom: 1px dashed var(--line); padding-bottom: 4px; }}
        p {{ margin: 8px 0 0; line-height: 1.5; }}
        .meta {{ color: var(--muted); font-size: 0.95rem; }}
        .notice {{ margin-top: 16px; padding: 12px 14px; border-left: 5px solid var(--notice-border); background: var(--notice-bg); }}
        .layout {{ display: grid; grid-template-columns: minmax(280px, 360px) minmax(0, 1fr); gap: 20px; align-items: start; }}
        .sidebar-header, .editor-header {{ display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 14px; }}
        .record-list {{ display: grid; gap: 10px; }}
        .record-card {{ display: block; padding: 14px 16px; text-decoration: none; color: inherit; border: 1px solid var(--line); background: var(--panel-strong); border-radius: 4px; }}
        .record-card:hover {{ border-color: var(--line-strong); }}
        .record-card.active {{ background: var(--selected); border-color: var(--accent); }}
        .record-card h3 {{ font-size: 1.05rem; margin-bottom: 4px; }}
        .record-card .record-meta {{ color: var(--muted); font-size: 0.92rem; margin-bottom: 8px; }}
        .record-card p {{ margin: 0; color: var(--muted); }}
        .empty-state {{ padding: 18px; border: 1px dashed var(--line-strong); color: var(--muted); background: var(--panel-strong); border-radius: 4px; }}
        form {{ display: grid; gap: 18px; }}
        .field-grid {{ display: grid; gap: 16px; grid-template-columns: repeat(2, minmax(0, 1fr)); }}
        label {{ display: grid; gap: 6px; font-weight: 700; }}
        label.full {{ grid-column: 1 / -1; }}
        input[type=text], input[type=number], textarea {{ width: 100%; border: 1px solid var(--line); background: var(--input-bg); color: var(--ink); padding: 11px 12px; font-family: Consolas, ""Courier New"", monospace; border-radius: 4px; }}
        input[type=text]:focus, input[type=number]:focus, textarea:focus {{ border-color: var(--accent); outline: none; box-shadow: 0 0 8px rgba(20, 184, 166, 0.3); }}
        textarea {{ min-height: 170px; resize: vertical; }}
        .checkboxes {{ display: flex; gap: 18px; flex-wrap: wrap; }}
        .checkboxes label {{ display: flex; align-items: center; gap: 8px; font-weight: 600; }}
        .actions {{ display: flex; gap: 12px; flex-wrap: wrap; align-items: center; border-top: 1px dashed var(--line); padding-top: 18px; margin-top: 10px; }}
        button, .button-link {{ 
            border: 1px solid var(--accent); 
            padding: 10px 20px; 
            font-family: Consolas, ""Courier New"", monospace; 
            font-weight: 700; 
            cursor: pointer; 
            background: transparent; 
            color: var(--accent); 
            text-transform: uppercase; 
            letter-spacing: 0.05em; 
            text-decoration: none; 
            display: inline-flex; 
            align-items: center; 
            justify-content: center; 
            border-radius: 4px; 
            transition: all 150ms ease; 
        }}
        button:hover, .button-link:hover {{ 
            background: var(--accent); 
            color: #121822; 
            box-shadow: 0 0 12px var(--crt-glow); 
        }}
        .button-link.secondary, button.secondary {{ 
            border-color: var(--line-strong); 
            color: var(--muted); 
            background: transparent; 
        }}
        .button-link.secondary:hover, button.secondary:hover {{ 
            background: var(--line-strong); 
            color: var(--ink); 
            border-color: var(--line-strong); 
            box-shadow: none; 
        }}
        button.danger {{ 
            border-color: var(--accent-strong); 
            color: var(--accent-strong); 
            background: transparent; 
            margin-left: auto; /* push delete button to the right */
        }}
        button.danger:hover {{ 
            background: var(--accent-strong); 
            color: #121822; 
            box-shadow: 0 0 12px rgba(251, 146, 60, 0.4); 
        }}
        code {{ font-family: Consolas, ""Courier New"", monospace; }}
        @media (max-width: 860px) {{
            .layout {{ grid-template-columns: 1fr; }}
            .field-grid {{ grid-template-columns: 1fr; }}
            .hero {{ flex-direction: column; align-items: start; }}
            button.danger {{ margin-left: 0; width: 100%; }} /* stack on small screens */
            button, .button-link {{ width: 100%; }}
        }}
    </style>
</head>
<body>
    <main>
        <section class=""panel"">
            <div class=""hero"">
                <div>
                    <h1>RiftGate Admin</h1>
                    <p>Manage gateway behavior and maintain <code>{encodedAppsPath}</code> through structured record editing instead of raw CSV text.</p>
                </div>
                <div class=""meta"">Credentials come from <code>{encodedConfigPath}</code></div>
            </div>
            {messageHtml}
        </section>

        <section class=""panel"">
            <div class=""editor-header"">
                <div>
                    <h2>Gateway Settings</h2>
                    <p class=""meta"">Animation flags apply live. Max connections is saved immediately and takes effect the next time RiftGate starts.</p>
                </div>
            </div>
            <form method=""post"" action=""/settings"">
                <input type=""hidden"" name=""selectedIndex"" value=""{WebUtility.HtmlEncode(selectedIndexValue)}"">
                <input type=""hidden"" name=""mode"" value=""{WebUtility.HtmlEncode(modeValue)}"">
                <div class=""field-grid"">
                    <label>
                        Max connections
                        <input type=""number"" min=""1"" name=""maxConnections"" value=""{config.MaxConnections}"">
                    </label>
                </div>
                <div class=""checkboxes"">
                    <label><input type=""checkbox"" name=""enableWaveAnimation"" value=""true"" {checkedWave}> Enable title wave</label>
                    <label><input type=""checkbox"" name=""enableSpriteAnimation"" value=""true"" {checkedSprites}> Enable sprite animation</label>
                    <label><input type=""checkbox"" name=""enableTitleFader"" value=""true"" {checkedTitleFader}> Enable title fader</label>
                </div>
                <div class=""actions"">
                    <button type=""submit"">Save gateway settings</button>
                </div>
            </form>
        </section>

        <section class=""layout"">
            <section class=""panel"">
                <div class=""sidebar-header"">
                    <div>
                        <h2>App Records</h2>
                        <p class=""meta"">Select a record to edit, or create a new one.</p>
                    </div>
                    <a class=""button-link secondary"" href=""/?mode=new"">Add new</a>
                </div>
                {appListHtml}
            </section>

            <section class=""panel"">
                <div class=""editor-header"">
                    <div>
                        <h2>{WebUtility.HtmlEncode(modeTitle)}</h2>
                        <p class=""meta"">{WebUtility.HtmlEncode(modeDetail)}</p>
                    </div>
                </div>
                <form method=""post"" action=""/apps/save"" id=""save-form"">
                    <input type=""hidden"" name=""selectedIndex"" value=""{WebUtility.HtmlEncode(selectedIndexValue)}"">
                    <div class=""field-grid"">
                        <label>
                            Host or IP
                            <input type=""text"" name=""host"" value=""{WebUtility.HtmlEncode(editorRecord.Host)}"" placeholder=""127.0.0.1"">
                        </label>
                        <label>
                            Port
                            <input type=""number"" min=""1"" max=""65535"" name=""port"" value=""{(editorRecord.Port == 0 ? string.Empty : editorRecord.Port.ToString())}"" placeholder=""8001"">
                        </label>
                        <label class=""full"">
                            Display name
                            <input type=""text"" name=""name"" value=""{WebUtility.HtmlEncode(editorRecord.Name)}"" placeholder=""Rift Writer"">
                        </label>
                        <label class=""full"">
                            Description
                            <textarea name=""description"" placeholder=""Describe what users will see when they connect."">{WebUtility.HtmlEncode(editorRecord.Description)}</textarea>
                        </label>
                        <div class=""checkboxes"" style=""grid-column: 1 / -1;"">
                            <label><input type=""checkbox"" name=""useTls"" value=""true"" {(editorRecord.UseTls ? "checked" : string.Empty)}> Use TLS Encryption</label>
                            <label><input type=""checkbox"" name=""validateCertificate"" value=""true"" {(editorRecord.ValidateCertificate ? "checked" : string.Empty)}> Enforce Strict CA Validation</label>
                        </div>
                    </div>
                </form>
                <form method=""post"" action=""/apps/delete"" id=""delete-form"">
                    <input type=""hidden"" name=""selectedIndex"" value=""{WebUtility.HtmlEncode(selectedIndexValue)}"">
                </form>
                <div class=""actions"">
                    <button type=""submit"" form=""save-form"">{(isNewRecord ? "Create record" : "Save record")}</button>
                    <a class=""button-link secondary"" href=""{resetHref}"">{(isNewRecord ? "Cancel" : "Reset")}</a>
                    {(!isNewRecord ? @"<button type=""submit"" form=""delete-form"" class=""danger"">Delete record</button>" : "")}
                </div>
            </section>
        </section>

        <section class=""panel"">
            <form method=""post"" action=""/logout"">
                <button type=""submit"" class=""secondary"">Log out</button>
            </form>
        </section>
    </main>
</body>
</html>";
    }

    private static string BuildAppListHtml(IReadOnlyList<RegisteredApp> apps, int selectedIndex, bool isNewRecord)
    {
        if (apps.Count == 0)
        {
            return "<div class=\"empty-state\">No app records exist yet. Use Add new to create the first entry.</div>";
        }

        var builder = new StringBuilder();
        builder.Append("<div class=\"record-list\">");

        for (int index = 0; index < apps.Count; index++)
        {
            var app = apps[index];
            var activeClass = !isNewRecord && index == selectedIndex ? " active" : string.Empty;
            string securityLabel = app.UseTls 
                ? (app.ValidateCertificate ? " [TLS: Secure CA]" : " [TLS: Self-Signed]") 
                : " [Plain TCP]";
            builder.Append($"<a class=\"record-card{activeClass}\" href=\"/?selected={index}\">");
            builder.Append($"<h3>{WebUtility.HtmlEncode(app.Name)}</h3>");
            builder.Append($"<div class=\"record-meta\">{WebUtility.HtmlEncode(app.Host)}:{app.Port}{securityLabel}</div>");
            builder.Append($"<p>{WebUtility.HtmlEncode(Truncate(app.Description, 110))}</p>");
            builder.Append("</a>");
        }

        builder.Append("</div>");
        return builder.ToString();
    }

    private static string Truncate(string text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length <= maxLength)
        {
            return text;
        }

        return text[..(maxLength - 3)] + "...";
    }

    private static string RenderLoginPage(string? errorMessage)
    {
        var errorHtml = string.IsNullOrWhiteSpace(errorMessage)
            ? string.Empty
            : $"<div class=\"notice\">{WebUtility.HtmlEncode(errorMessage)}</div>";

        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <title>RiftGate Login</title>
    <style>
        :root {{
            --login-bg: radial-gradient(circle at top, #1a2336 0%, #121822 65%, #090c12 100%);
            --card-bg: rgba(26, 36, 52, 0.95);
            --ink: #f1f5f9;
            --border: #2e3c54;
            --input-bg: #171f2c;
            --btn-bg: #14b8a6;
            --btn-bg-hover: #f1f5f9;
            --notice-bg: #292823;
            --notice-border: #fb923c;
            --crt-glow: rgba(20, 184, 166, 0.2);
            --shadow: 0 16px 40px rgba(0, 0, 0, 0.4);
        }}
        body {{
            margin: 0;
            min-height: 100vh;
            display: grid;
            place-items: center;
            background: var(--login-bg);
            color: var(--ink);
            font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, ""Helvetica Neue"", Arial, sans-serif;
            line-height: 1.6;
        }}
        .card {{
            width: min(420px, calc(100vw - 32px));
            padding: 28px;
            background: var(--card-bg);
            border: 1px solid var(--border);
            box-shadow: var(--shadow);
            position: relative;
            overflow: hidden;
            border-radius: 8px;
        }}
        .card::before {{
            content: '';
            position: absolute;
            top: 0; left: 0; width: 4px; height: 100%;
            background: var(--btn-bg);
        }}
        h1 {{ 
            font-family: Consolas, ""Courier New"", monospace;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            color: var(--btn-bg);
            text-shadow: 0 0 8px var(--crt-glow);
            margin: 0 0 10px; 
        }}
        form {{ display: grid; gap: 14px; }}
        label {{ display: grid; gap: 6px; font-weight: 700; }}
        input {{ 
            border: 1px solid var(--border); 
            padding: 10px 12px; 
            font: inherit; 
            background: var(--input-bg); 
            color: var(--ink); 
            border-radius: 4px;
            font-family: Consolas, ""Courier New"", monospace;
        }}
        input:focus {{ 
            border-color: var(--btn-bg); 
            outline: none; 
            box-shadow: 0 0 8px rgba(20, 184, 166, 0.3);
        }}
        button {{ 
            border: 1px solid var(--btn-bg); 
            padding: 10px 20px; 
            font-family: Consolas, ""Courier New"", monospace;
            font-weight: 700; 
            cursor: pointer; 
            color: var(--btn-bg); 
            background: transparent; 
            border-radius: 4px;
            text-transform: uppercase;
            letter-spacing: 0.05em;
            transition: all 150ms ease;
        }}
        button:hover {{
            background: var(--btn-bg);
            color: #121822;
            box-shadow: 0 0 12px var(--crt-glow);
        }}
        .notice {{ padding: 10px 12px; margin-bottom: 12px; background: var(--notice-bg); border-left: 5px solid var(--notice-border); }}
    </style>
</head>
<body>
    <section class=""card"">
        <h1>RiftGate Admin</h1>
        <p>Sign in with the credentials stored in the RiftGate JSON config file.</p>
        {errorHtml}
        <form method=""post"" action=""/login"">
            <label>
                Username
                <input type=""text"" name=""username"" autocomplete=""username"">
            </label>
            <label>
                Password
                <input type=""password"" name=""password"" autocomplete=""current-password"">
            </label>
            <button type=""submit"">Log in</button>
        </form>
    </section>
</body>
</html>";
    }

    private static string RenderSimplePage(string title, string body)
    {
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""utf-8"">
    <title>{WebUtility.HtmlEncode(title)}</title>
</head>
<body>
    <h1>{WebUtility.HtmlEncode(title)}</h1>
    <p>{body}</p>
</body>
</html>";
    }
}