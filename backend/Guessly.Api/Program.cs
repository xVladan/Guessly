using Guessly.Api.Hubs;
using Guessly.Api.Services;

var builder = WebApplication.CreateBuilder(args);

var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

const string CorsPolicy = "GuesslyCors";
var allowedOrigins = (Environment.GetEnvironmentVariable("ALLOWED_ORIGINS") ?? "http://localhost:5500,http://127.0.0.1:5500,http://localhost:8080")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

builder.Services.AddCors(options =>
{
    options.AddPolicy(CorsPolicy, policy =>
    {
        policy.WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.AddSignalR();
builder.Services.AddSingleton<EmbeddingService>();
builder.Services.AddSingleton<GameEngine>();
builder.Services.AddEndpointsApiExplorer();

var app = builder.Build();

app.UseCors(CorsPolicy);

app.MapGet("/health", (EmbeddingService embeddings) => Results.Ok(new
{
    status = "ok",
    embeddingsReady = embeddings.IsReady,
    vocabularyCount = embeddings.VocabularyCount,
}));

app.MapGet("/api/avatars", () => Results.Ok(GameEngine.AvailableAvatars));

app.MapHub<GameHub>("/hub/game");

app.Run();
