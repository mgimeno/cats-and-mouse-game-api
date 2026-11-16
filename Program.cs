using System.IO.Compression;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using CatsAndMouseApi.Hubs;
using CatsAndMouseApi.Serialization;
using CatsAndMouseApi.Services;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;
using Microsoft.AspNetCore.SignalR;

const string CorsPolicyName = "CorsPolicy";

// The slim builder leaves out HTTPS, HTTP/3, IIS integration, the EventLog and EventSource
// loggers and regex route constraints. Nothing here uses them behind nginx; it starts faster,
// and it is the builder Native AOT is designed around.
var builder = WebApplication.CreateSlimBuilder(args);

// Under systemd, stdout goes to the journal, and systemd says so through JOURNAL_STREAM. The
// systemd formatter writes each entry as one line with a syslog priority prefix, so journald
// records the real level (`journalctl -p warning` works) and a multi-line entry stays one
// record. The slim builder only registers the simple formatter, so this is the only way to get it.
if (Environment.GetEnvironmentVariable("JOURNAL_STREAM") is not null)
{
    builder.Logging.AddSystemdConsole();
}

var allowedOrigins = builder.Configuration
    .GetSection("AllowedOrigins")
    .Get<string[]>()?
    .Select(origin => origin.Trim().TrimEnd('/'))
    .Where(origin => !string.IsNullOrWhiteSpace(origin))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

if (allowedOrigins == null || allowedOrigins.Length == 0)
{
    throw new InvalidOperationException("AllowedOrigins is not configured");
}

if (allowedOrigins.Contains("*", StringComparer.Ordinal))
{
    throw new InvalidOperationException("AllowedOrigins cannot contain '*' because SignalR uses credentialed requests");
}

// The listening address is the "Urls" setting in appsettings. It outranks ASPNETCORE_URLS, which
// is host configuration; to run a second copy beside the live one, pass `--urls` instead, which
// outranks everything. nginx sets its own Server header, so Kestrel's is dropped.
builder.WebHost.ConfigureKestrel(options => options.AddServerHeader = false);

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto;

    // Trust NGINX
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicyName, policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<GameService>();

if (builder.Environment.IsDevelopment())
{
    builder.Services.AddResponseCompression(options =>
    {
        options.MimeTypes = ResponseCompressionDefaults.MimeTypes.Concat(["application/octet-stream"]);
    });
    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        options.Level = CompressionLevel.Fastest;
    });
}

builder.Services.AddSignalR(options =>
{
    // Browsers throttle timers in a backgrounded tab to roughly one tick per minute,
    // which stretches the client's 15s keep-alive ping out to about 60s. At a 60s
    // timeout that lands exactly on the boundary and drops players who merely switched
    // tabs, so allow room for two throttled pings before declaring a client gone.
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(120);
    options.HandshakeTimeout = TimeSpan.FromSeconds(15);
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.MaximumReceiveMessageSize = 16 * 1024;
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.AddFilter(new HubExceptionUnwrappingFilter());
})
    .AddJsonProtocol(options =>
    {
        // Source-generated metadata only: no reflection, which Native AOT does not have anyway.
        options.PayloadSerializerOptions.TypeInfoResolverChain.Insert(0, GameJsonSerializerContext.Default);
    });

var app = builder.Build();

// MUST be first
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}

app.UseRouting();

app.UseCors(CorsPolicyName);

// Deliberately no AllowStatefulReconnects. It replays buffered messages across a brief
// drop, but when its in-place transport resume fails -- which it does behind a proxy
// that will not carry the resumed connection id -- it stops the connection outright
// instead of falling through to the client's automatic reconnect. The client refetches
// authoritative state after every reconnect anyway, so the replay bought nothing that
// was not already covered, at the cost of turning a silent recovery into a dead socket.
app.MapHub<GameHub>("/gameHub");

// Built once: nothing in it changes while the process runs. Short-circuited, so a health check
// skips everything after routing (CORS included: no browser reads it).
var statusText = BuildStatusText(app.Environment, allowedOrigins);
app.MapGet("/status", [ShortCircuit] () => statusText);

app.Run();

static string BuildStatusText(IHostEnvironment environment, string[] allowedOrigins)
{
    var isDebugMode = false;
#if DEBUG
    isDebugMode = true;
#endif

    // The runtime that is actually executing, e.g. ".NET 11.0.0 (Native AOT, linux-arm64)". Under
    // Native AOT it is the runtime compiled into the executable, so it shows which patch a deploy
    // brought; there is no dynamic code there, which is how AOT is told apart from the JIT.
    var runtime = $"{RuntimeInformation.FrameworkDescription} " +
                  $"({(RuntimeFeature.IsDynamicCodeSupported ? "JIT" : "Native AOT")}, {RuntimeInformation.RuntimeIdentifier})";

    return $"""

        Cats & Mouse API is ready

        Build Mode: {(isDebugMode ? "DEBUG (Development)" : "RELEASE (Production)")}
        Environment: {environment.EnvironmentName.ToUpperInvariant()}
        Runtime: {runtime}
        Allowed origins: {string.Join(", ", allowedOrigins)}
        """;
}
