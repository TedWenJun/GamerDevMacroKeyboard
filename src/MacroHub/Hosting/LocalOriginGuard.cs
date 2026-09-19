namespace MacroHub.Hosting;

/// <summary>
/// The web UI / API / WebSocket listen on 127.0.0.1 without authentication, which is fine for local processes but
/// not for web pages: browsers let any site open a WebSocket to localhost or submit simple cross-site POSTs, and a
/// DNS-rebinding domain can even read responses. This middleware only admits requests that
///   - address the Hub by a loopback host name (Host = 127.0.0.1 / localhost / [::1] with our port), and
///   - either carry no Origin (non-browser clients: UE plugin, scripts, tests) or an Origin of the Hub itself.
/// </summary>
public sealed class LocalOriginGuard(RequestDelegate next, int port, ILogger<LocalOriginGuard> log)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        if (!IsLoopbackHost(ctx.Request.Host.Host) || (ctx.Request.Host.Port ?? 80) != port)
        {
            await Reject(ctx, $"host '{ctx.Request.Host}' is not the local MacroHub");
            return;
        }
        var origin = ctx.Request.Headers.Origin.ToString();
        if (origin.Length > 0 && !IsOwnOrigin(origin))
        {
            await Reject(ctx, $"origin '{origin}' is not allowed");
            return;
        }
        await next(ctx);
    }

    private static bool IsLoopbackHost(string host) =>
        host.Equals("127.0.0.1", StringComparison.Ordinal) || host.Equals("localhost", StringComparison.OrdinalIgnoreCase) || host is "[::1]" or "::1";

    private bool IsOwnOrigin(string origin) =>
        Uri.TryCreate(origin, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttp && IsLoopbackHost(uri.Host) && uri.Port == port;

    private async Task Reject(HttpContext ctx, string reason)
    {
        log.LogWarning("rejected {Method} {Path}: {Reason}", ctx.Request.Method, ctx.Request.Path, reason);
        ctx.Response.StatusCode = StatusCodes.Status403Forbidden;
        await ctx.Response.WriteAsync("MacroHub only accepts requests from its own local web UI or local non-browser clients.");
    }
}
