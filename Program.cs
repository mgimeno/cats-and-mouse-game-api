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
    // Enough to cover a game's worth of moves and chat across a brief drop. Paired with
    // AllowStatefulReconnects on the hub mapping below.
    options.StatefulReconnectBufferSize = 1000;
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

// Stateful reconnect buffers messages while the transport is briefly down and replays
// them once it is back, so a move or chat line sent while the opponent's tab was
// backgrounded is not silently lost. Requires the WebSockets transport.
app.MapHub<GameHub>("/gameHub", options =>
{
    options.AllowStatefulReconnects = true;
});
app.MapControllers();

app.Run();
