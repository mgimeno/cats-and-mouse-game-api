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
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(60);
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

app.MapHub<GameHub>("/gameHub");
app.MapControllers();

app.Run();
