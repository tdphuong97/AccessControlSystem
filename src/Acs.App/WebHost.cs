using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Acs.Core;
using Acs.Core.Host;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace Acs.App;

/// <summary>
/// Hosts the page from the ui folder and carries messages both ways (contract C3):
/// the page sends {id, cmd, args} and gets {id, result|error}; the app pushes {event, data}.
/// </summary>
sealed class WebHost
{
    public const string HostName = "app.acs";
    static Task<CoreWebView2Environment>? _environment;

    readonly WebView2 _web;
    readonly Func<string, JsonElement, Task<object?>> _handler;

    public WebHost(WebView2 web, Func<string, JsonElement, Task<object?>> handler)
    {
        _web = web;
        _handler = handler;
    }

    static Task<CoreWebView2Environment> Environment()
    {
        // One browser profile, kept with the app's data.
        return _environment ??= CoreWebView2Environment.CreateAsync(null,
            Path.Combine(AppPaths.Data, "WebView2"),
            new CoreWebView2EnvironmentOptions("--disable-features=msSmartScreenProtection"));
    }

    public async Task LoadAsync(string page)
    {
        _web.DefaultBackgroundColor = System.Drawing.Color.FromArgb(0xF4, 0xF6, 0xF9);
        await _web.EnsureCoreWebView2Async(await Environment());
        var core = _web.CoreWebView2;
        var s = core.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
#if !DEBUG
        s.AreDevToolsEnabled = false;
#endif
        core.SetVirtualHostNameToFolderMapping(HostName, AppPaths.Ui, CoreWebView2HostResourceAccessKind.Allow);
        core.WebMessageReceived += OnMessage;
        core.PermissionRequested += OnPermissionRequested;
        core.NewWindowRequested += (_, e) =>
        {
            e.Handled = true;
            OpenUrl(e.Uri);
        };
        core.NavigationStarting += (_, e) =>
        {
            // The page itself never navigates away; links to the web open in the user's browser.
            if (!e.Uri.StartsWith($"https://{HostName}/", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                OpenUrl(e.Uri);
            }
        };
        core.Navigate($"https://{HostName}/{page}");
    }

    /// <summary>Only the camera, and only for our own page; everything else is refused.</summary>
    internal static bool IsAllowed(CoreWebView2PermissionKind kind, string uri)
    {
        return kind == CoreWebView2PermissionKind.Camera
               && Uri.TryCreate(uri, UriKind.Absolute, out var u)
               && u.Scheme == Uri.UriSchemeHttps
               && string.Equals(u.Host, HostName, StringComparison.OrdinalIgnoreCase)
               && u.IsDefaultPort;
    }

    static void OnPermissionRequested(object? sender, CoreWebView2PermissionRequestedEventArgs e)
    {
        e.State = IsAllowed(e.PermissionKind, e.Uri)
            ? CoreWebView2PermissionState.Allow
            : CoreWebView2PermissionState.Deny;
    }

    static void OpenUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https")) return;
        try { Process.Start(new ProcessStartInfo(u.AbsoluteUri) { UseShellExecute = true })?.Dispose(); }
        catch (Exception) { }
    }

    async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode reply;
        var id = 0;
        try
        {
            using var doc = JsonDocument.Parse(e.WebMessageAsJson);
            var root = doc.RootElement;
            id = root.GetProperty("id").GetInt32();
            var cmd = root.GetProperty("cmd").GetString() ?? "";
            var args = root.TryGetProperty("args", out var a) ? a.Clone() : default;
            // Off the UI thread: many core commands do database or device work before their first await.
            var result = await Task.Run(() => _handler(cmd, args));
            reply = new JsonObject { ["id"] = id, ["result"] = JsonSerializer.SerializeToNode(result, AppHost.Json) };
        }
        catch (Exception ex)
        {
            reply = new JsonObject { ["id"] = id, ["error"] = ex.Message };
        }
        Post(reply);
    }

    public void Emit(string evt, object? data)
    {
        Post(new JsonObject { ["event"] = evt, ["data"] = JsonSerializer.SerializeToNode(data, AppHost.Json) });
    }

    void Post(JsonNode node)
    {
        if (!_web.Dispatcher.CheckAccess())
        {
            _web.Dispatcher.BeginInvoke(() => Post(node));
            return;
        }
        if (_web.CoreWebView2 is null) return;
        try { _web.CoreWebView2.PostWebMessageAsJson(node.ToJsonString()); }
        catch (InvalidOperationException) { }
    }
}
