using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace dreams.Services;

public class HttpLogRecord
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string? QueryString { get; set; }
    public int Status { get; set; }
    public long DurationMs { get; set; }
    public string? UserName { get; set; }
    public string? UserId { get; set; }
    public string? RemoteIp { get; set; }
    public string? UserAgent { get; set; }
}

public class UserActionRecord
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string? UserName { get; set; }
    public string? UserId { get; set; }
    public string? TargetType { get; set; }
    public string? TargetId { get; set; }
    public string? Details { get; set; }
}

/// <summary>
/// Пишет HTTP-логи и действия пользователя в MongoDB.
/// Если Mongo недоступна — пишет warning и становится no-op (IsEnabled = false).
/// Singleton: переиспользует клиент.
/// </summary>
public class MongoLogService
{
    private readonly IMongoCollection<HttpLogRecord>? _http;
    private readonly IMongoCollection<UserActionRecord>? _actions;
    private readonly ILogger<MongoLogService> _logger;

    public bool IsEnabled { get; }

    public MongoLogService(IConfiguration config, ILogger<MongoLogService> logger)
    {
        _logger = logger;
        var conn = config["MongoDB:ConnectionString"];
        var dbName = config["MongoDB:Database"];
        if (string.IsNullOrWhiteSpace(conn) || string.IsNullOrWhiteSpace(dbName))
        {
            _logger.LogWarning("MongoDB настройки не заданы, логирование отключено.");
            IsEnabled = false;
            return;
        }

        try
        {
            var settings = MongoClientSettings.FromConnectionString(conn);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            var client = new MongoClient(settings);
            var db = client.GetDatabase(dbName);

            // Ping синхронно с коротким таймаутом
            db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));

            _http    = db.GetCollection<HttpLogRecord>("http_logs");
            _actions = db.GetCollection<UserActionRecord>("user_actions");
            IsEnabled = true;
            _logger.LogInformation("MongoDB подключена: {Conn}/{Db}", conn, dbName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "MongoDB недоступна — логирование отключено. {Conn}", conn);
            IsEnabled = false;
        }
    }

    public async Task LogHttpAsync(HttpLogRecord r, CancellationToken ct = default)
    {
        if (!IsEnabled || _http is null) return;
        try { await _http.InsertOneAsync(r, cancellationToken: ct); }
        catch (Exception ex) { _logger.LogDebug(ex, "mongo http log write failed"); }
    }

    public async Task LogActionAsync(UserActionRecord r, CancellationToken ct = default)
    {
        if (!IsEnabled || _actions is null) return;
        try { await _actions.InsertOneAsync(r, cancellationToken: ct); }
        catch (Exception ex) { _logger.LogDebug(ex, "mongo action log write failed"); }
    }

    public async Task<List<HttpLogRecord>> GetRecentHttpAsync(int limit = 100)
    {
        if (!IsEnabled || _http is null) return new();
        try
        {
            return await _http.Find(FilterDefinition<HttpLogRecord>.Empty)
                .SortByDescending(x => x.At).Limit(limit).ToListAsync();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "mongo http log read failed"); return new(); }
    }

    public async Task<List<UserActionRecord>> GetRecentActionsAsync(int limit = 100)
    {
        if (!IsEnabled || _actions is null) return new();
        try
        {
            return await _actions.Find(FilterDefinition<UserActionRecord>.Empty)
                .SortByDescending(x => x.At).Limit(limit).ToListAsync();
        }
        catch (Exception ex) { _logger.LogDebug(ex, "mongo action log read failed"); return new(); }
    }
}
