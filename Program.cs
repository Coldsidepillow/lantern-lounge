using LanternLounge.Server;

// Lantern Lounge online server: rooms with join codes for all four games.
// Render (and most hosts) tell the app which port to use through the PORT variable.
var builder = WebApplication.CreateBuilder(args);
string port = Environment.GetEnvironmentVariable("PORT") ?? "10000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

builder.Services.AddSingleton<RoomManager>();
builder.Services.AddSingleton<Hub>();
builder.Services.AddHostedService<Janitor>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(25) });

// The website (wwwroot/index.html and friends): play every game online in a browser.
app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        // Always fetch the newest page and scripts after a redeploy.
        ctx.Context.Response.Headers["Cache-Control"] = "no-cache";
    }
});

app.MapGet("/health", (RoomManager rooms) => Results.Json(new { ok = true, rooms = rooms.Count }));

app.MapGet("/games", () => Results.Json(
    GameCatalog.All.Select(g => new { id = g.GameId, name = g.Module.Info.DisplayName, seats = g.Seats }),
    Hub.Json));

app.Map("/ws", async (HttpContext context, Hub hub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsync("Connect with a WebSocket.");
        return;
    }
    using var socket = await context.WebSockets.AcceptWebSocketAsync();
    await hub.RunAsync(socket, context.RequestAborted);
});

app.Run();
