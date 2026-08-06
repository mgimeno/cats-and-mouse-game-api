using System.IO.Compression;
using System.Net;
using CatsAndMouseApi.Hubs;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.ResponseCompression;

var builder = WebApplication.CreateBuilder(args);
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

builder.Services.AddOpenApi();

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
    options.AddPolicy("CorsPolicy", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddControllers();

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
})
    .AddJsonProtocol(o =>
    {
        o.PayloadSerializerOptions.WriteIndented = false;
    });

// Kestrel: HTTP only
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, 53000);
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// MUST be first
app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseResponseCompression();
}

app.UseRouting();

app.UseCors("CorsPolicy");

// Deliberately no AllowStatefulReconnects. It replays buffered messages across a brief
// drop, but when its in-place transport resume fails -- which it does behind a proxy
// that will not carry the resumed connection id -- it stops the connection outright
// instead of falling through to the client's automatic reconnect. The client refetches
// authoritative state after every reconnect anyway, so the replay bought nothing that
// was not already covered, at the cost of turning a silent recovery into a dead socket.
app.MapHub<GameHub>("/gameHub");
app.MapControllers();

app.Run();
