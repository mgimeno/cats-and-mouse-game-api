using CatsAndMouseGame.Hubs;
using Microsoft.AspNetCore.HttpOverrides;

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();

//app.UseHttpsRedirection();

app.UseRouting();

app.UseCors("CorsPolicy");

app.MapHub<GameHub>("/gameHub");

app.MapControllerRoute("default","{controller=Status}/{action=Status}");

app.Run("http://127.0.0.1:51000");
