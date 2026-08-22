using Gcg2OfflineServer;
using Gcg2OfflineServer.Protocol;
using Gcg2OfflineServer.Services;

var builder = WebApplication.CreateBuilder(args);

// ---- 配置 ----
var httpHost    = Environment.GetEnvironmentVariable("GCG_HTTP_HOST")    ?? builder.Configuration["http:host"]    ?? "0.0.0.0";
var httpPort    = int.Parse(Environment.GetEnvironmentVariable("GCG_HTTP_PORT")    ?? builder.Configuration["http:port"]    ?? "18080");
var gatewayHost = Environment.GetEnvironmentVariable("GCG_GATEWAY_HOST") ?? builder.Configuration["gateway:host"] ?? "0.0.0.0";
var gatewayPort = int.Parse(Environment.GetEnvironmentVariable("GCG_GATEWAY_PORT") ?? builder.Configuration["gateway:port"] ?? "30400");
var advertisedHost = Environment.GetEnvironmentVariable("GCG_GAME_HOST") ?? builder.Configuration["gateway:advertisedHost"] ?? "127.0.0.1";
var gmToken = Environment.GetEnvironmentVariable("GCG_GM_TOKEN") ?? builder.Configuration["gm:token"] ?? "";

var serverList = new ServerListConfig
{
    Id    = long.Parse(builder.Configuration["serverList:id"]    ?? "1"),
    Aid   = long.Parse(builder.Configuration["serverList:aid"]   ?? "1"),
    Sid   = long.Parse(builder.Configuration["serverList:sid"]   ?? "1"),
    Name  = builder.Configuration["serverList:name"]  ?? "离线研究服",
    State = long.Parse(builder.Configuration["serverList:state"] ?? "1"),
    Level = long.Parse(builder.Configuration["serverList:level"] ?? "1"),
};

// ---- 目录 ----
var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
var logDir  = Path.Combine(AppContext.BaseDirectory, "logs");
Directory.CreateDirectory(dataDir);
Directory.CreateDirectory(logDir);

// ---- 依赖注入 ----
var logger = new GameLogger(Path.Combine(logDir, "server.log"), GameLogLevel.Debug);
var repo = new PlayerRepository(dataDir, logger);
var gateway = new TcpGateway(gatewayHost, gatewayPort, repo, logger, serverList);
var gmService = new GmCommandService(repo, logger);

builder.Services.AddSingleton(logger);
builder.Services.AddSingleton(repo);
builder.Services.AddSingleton(gateway);
builder.Services.AddSingleton(gmService);
builder.Services.AddSingleton(serverList);

builder.WebHost.UseUrls($"http://{httpHost}:{httpPort}");
builder.Logging.ClearProviders();

var app = builder.Build();
var startedAt = DateTime.UtcNow;

// ---- 全局异常兜底：避免未捕获异常直接崩进程 ----
AppDomain.CurrentDomain.UnhandledException += (s, e) =>
{
    var ex = e.ExceptionObject as Exception ?? new Exception("Unknown unhandled exception");
    logger.Error($"[FATAL] UnhandledException: {ex}");
    if (e.IsTerminating)
    {
        logger.Error("[FATAL] Process is terminating due to unhandled exception.");
        try { repo.Flush(); } catch { }
    }
};
TaskScheduler.UnobservedTaskException += (s, e) =>
{
    logger.Error($"[FATAL] UnobservedTaskException: {e.Exception}");
    e.SetObserved();
};

// ---- HTTP 接口 ----

app.MapGet("/health", () => Results.Json(new
{
    ok = true,
    startedAt = startedAt.ToString("o"),
    uptimeSeconds = (int)(DateTime.UtcNow - startedAt).TotalSeconds
}));

app.MapGet("/serverlist", () => Results.Json(new
{
    serverlist = new[]
    {
        new
        {
            id = serverList.Id, aid = serverList.Aid, sid = serverList.Sid,
            name = serverList.Name, ip = advertisedHost, port = new[] { gatewayPort },
            state = serverList.State
        }
    },
    level = serverList.Level
}));

app.MapGet("/serverstate/{id:int}", (int id) => Results.Json(new { state = serverList.State }));

// ---- GM 命令接口 ----
// 用法：http://127.0.0.1:18080/gm?cmd=level%2080&token=xxx
// 支持命令：level <n>, exp <n>, vigor <n>, gold <n>, diamond <n>,
//           unlockall, addcard <id>, addgirl <id>, maxcards, help
// 安全：若配置了 GCG_GM_TOKEN 环境变量或 gm:token，则必须携带正确 token。
app.MapGet("/gm", (string? cmd, string? token, string? account) =>
{
    // Token 校验（配置了才校验）
    if (!string.IsNullOrEmpty(gmToken) && token != gmToken)
    {
        return Results.Json(new { ok = false, error = "未授权：缺少或错误的 GM token" }, statusCode: 401);
    }

    var targetAccount = string.IsNullOrEmpty(account) ? "1" : account;

    if (string.IsNullOrWhiteSpace(cmd))
    {
        return Results.Json(new
        {
            ok = false,
            error = "缺少命令参数",
            usage = new[]
            {
                "GET /gm?cmd=level%2080  设置等级",
                "GET /gm?cmd=vigor%20100  设置体力",
                "GET /gm?cmd=gold%20100000  设置金币",
                "GET /gm?cmd=diamond%2030000  设置青辉石",
                "GET /gm?cmd=unlockall  一键解锁所有关卡",
                "GET /gm?cmd=maxcards  获得所有角色卡",
                "GET /gm?cmd=help  显示帮助",
                "可选参数: &account=xxx 指定玩家账号 (默认1)",
            }
        });
    }

    var (ok, result, data) = gmService.Execute(targetAccount, cmd);
    if (data != null)
        return Results.Json(new { ok = true, account = targetAccount, command = cmd.Split(' ')[0].ToLower(), result = data });
    return Results.Json(new { ok, account = targetAccount, command = cmd.Split(' ')[0].ToLower(), result });
});

// ---- 优雅关闭 ----
app.Lifetime.ApplicationStopping.Register(() =>
{
    logger.Info("Application stopping, flushing state...");
    try { gateway.Stop(); } catch (Exception ex) { logger.Error($"Gateway stop error: {ex.Message}"); }
    try { repo.Flush(); } catch (Exception ex) { logger.Error($"Repo flush error: {ex.Message}"); }
    logger.Info("Shutdown complete.");
});

AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    try { repo.Flush(); } catch { }
};
Console.CancelKeyPress += (s, e) =>
{
    e.Cancel = true;
    logger.Info("Ctrl+C received, stopping...");
    try { gateway.Stop(); } catch (Exception ex) { logger.Error($"Gateway stop error: {ex.Message}"); }
    try { repo.Flush(); } catch (Exception ex) { logger.Error($"Repo flush error: {ex.Message}"); }
    logger.Info("Shutdown complete.");
    Environment.Exit(0);
};

// ---- 启动 TCP 网关（后台）----
_ = gateway.StartAsync();

logger.Info($"HTTP server starting on http://{httpHost}:{httpPort}");
logger.Info($"Game host advertised to clients: {advertisedHost}:{gatewayPort}");
if (!string.IsNullOrEmpty(gmToken))
    logger.Info("GM interface protected by token");
else
    logger.Warn("GM interface has NO token protection (set GCG_GM_TOKEN to secure)");
logger.Info("Ready.");
app.Run();
