using System.Net;
using System.Security.Cryptography.X509Certificates;
using CatsAndMouseGame.Hubs;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Server.Kestrel.Core;
// using static Microsoft.AspNetCore.Http.StatusCodes;

var builder = WebApplication.CreateBuilder(args);
var allowedOrigins = builder.Configuration.GetValue<string>("AllowedOrigins");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
});

builder.Services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        builder =>
        {
            builder.AllowAnyMethod()
                    .AllowAnyHeader()
                    .WithOrigins(allowedOrigins)
                    .AllowCredentials();
        });

});

builder.Services.AddControllers();

builder.Services.AddSignalR(hubOptions =>
{
})
    .AddJsonProtocol(options =>
    {
        options.PayloadSerializerOptions.WriteIndented = false;
    });

// if (!builder.Environment.IsDevelopment())
// {
//     builder.Services.AddHttpsRedirection(options =>
//     {
//         options.HttpsPort = 53000;
//         options.RedirectStatusCode = Status307TemporaryRedirect;
//     });
// }

if (!builder.Environment.IsDevelopment()){
    //Set SSL Certificate
    builder.Services.Configure<KestrelServerOptions>(options =>
    {
        var cert = X509Certificate2.CreateFromPemFile ("/home/mgimeno/certificates/marcosgimeno_com_chain.crt", "/home/mgimeno/certificates/marcosgimeno.com.key");
        //var cert = new X509Certificate2("/", "");

        //options.Listen(IPAddress.Any, 80); // http
        options.Listen(IPAddress.Any, 443, listenOptions => // https
        {
            listenOptions.UseHttps(cert);
        });
        options.Listen(IPAddress.Any, 53000, listenOptions => // https
        {
            listenOptions.UseHttps(cert);
        });
    });
}

builder.WebHost.UseUrls(builder.Environment.IsDevelopment() ? ["http://127.0.0.1:53000"] : ["https://127.0.0.1:53000"]);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}
else{
    //app.UseHsts();
}

app.UseForwardedHeaders();

if (!app.Environment.IsDevelopment())
{
app.UseHttpsRedirection();
}

app.UseRouting();

app.UseCors("CorsPolicy");

app.MapHub<GameHub>("/gameHub");

app.MapControllerRoute("default","{controller=Status}/{action=Status}");

if (app.Environment.IsDevelopment())
{
app.Run("http://127.0.0.1:53000");
}
else{
    app.Run("https://127.0.0.1:53000");
}


