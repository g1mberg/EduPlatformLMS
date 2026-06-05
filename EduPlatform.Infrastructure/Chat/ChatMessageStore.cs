using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;
using MongoDB.Driver;

namespace EduPlatform.Infrastructure.Chat;

public class ChatMessage
{
    [BsonId, BsonRepresentation(BsonType.ObjectId)]
    public string? Id { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid CourseId { get; set; }

    [BsonRepresentation(BsonType.String)]
    public Guid UserId { get; set; }

    public string UserName { get; set; } = "";
    public string? AvatarUrl { get; set; }
    public string Text { get; set; } = "";
    public DateTime At { get; set; } = DateTime.UtcNow;
    public bool IsInstructor { get; set; }
}

/// <summary>
/// История сообщений чата курса. Использует MongoDB (коллекция course_chat),
/// при недоступности Mongo — fallback в память (in-process ring buffer на курс).
/// Singleton.
/// </summary>
public class ChatMessageStore
{
    private readonly IMongoCollection<ChatMessage>? _col;
    private readonly ILogger<ChatMessageStore> _logger;
    private readonly ConcurrentDictionary<Guid, List<ChatMessage>> _memory = new();
    private const int MemoryRingSize = 200;

    public bool IsMongoEnabled { get; }

    public ChatMessageStore(IConfiguration config, ILogger<ChatMessageStore> logger)
    {
        _logger = logger;
        var conn = config["MongoDB:ConnectionString"];
        var dbName = config["MongoDB:Database"];
        if (string.IsNullOrWhiteSpace(conn) || string.IsNullOrWhiteSpace(dbName))
        {
            _logger.LogWarning("ChatMessageStore: Mongo не настроен, используется in-memory.");
            IsMongoEnabled = false;
            return;
        }

        try
        {
            var settings = MongoClientSettings.FromConnectionString(conn);
            settings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
            var client = new MongoClient(settings);
            var db = client.GetDatabase(dbName);
            db.RunCommand<BsonDocument>(new BsonDocument("ping", 1));

            _col = db.GetCollection<ChatMessage>("course_chat");
            // Индекс по CourseId+At для выборки истории по курсу
            _col.Indexes.CreateOne(new CreateIndexModel<ChatMessage>(
                Builders<ChatMessage>.IndexKeys.Ascending(x => x.CourseId).Descending(x => x.At)));
            IsMongoEnabled = true;
            _logger.LogInformation("ChatMessageStore: Mongo подключена.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "ChatMessageStore: Mongo недоступна, fallback in-memory.");
            IsMongoEnabled = false;
        }
    }

    public async Task SaveAsync(ChatMessage msg, CancellationToken ct = default)
    {
        if (IsMongoEnabled && _col is not null)
        {
            try { await _col.InsertOneAsync(msg, cancellationToken: ct); return; }
            catch (Exception ex) { _logger.LogDebug(ex, "chat save mongo failed, fallback memory"); }
        }
        var list = _memory.GetOrAdd(msg.CourseId, _ => new List<ChatMessage>());
        lock (list)
        {
            list.Add(msg);
            if (list.Count > MemoryRingSize) list.RemoveRange(0, list.Count - MemoryRingSize);
        }
    }

    public async Task<List<ChatMessage>> GetHistoryAsync(Guid courseId, int limit = 50, CancellationToken ct = default)
    {
        if (IsMongoEnabled && _col is not null)
        {
            try
            {
                var items = await _col.Find(x => x.CourseId == courseId)
                    .SortByDescending(x => x.At).Limit(limit).ToListAsync(ct);
                items.Reverse();
                return items;
            }
            catch (Exception ex) { _logger.LogDebug(ex, "chat history mongo read failed"); }
        }
        if (_memory.TryGetValue(courseId, out var list))
        {
            lock (list) return list.TakeLast(limit).ToList();
        }
        return new List<ChatMessage>();
    }
}
