namespace Drover.Engine.Audit;

/// <summary>
/// Writes what a person changed to the audit log: who, what, and each changed value before and after. The entry is
/// saved with the change itself, in the same tenant scope, so one cannot exist without the other.
/// </summary>
public sealed class AuditLog(AuditRepository entries, TimeProvider time)
{
    #region Private Fields

    private static readonly JsonSerializerOptions snakeCase = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower) },
    };

    #endregion

    #region Public Methods

    /// <summary>The value as the log stores it: snake_case names, enumerations as words.</summary>
    public static JsonObject Snapshot(object value) => JsonSerializer.SerializeToNode(value, snakeCase)!.AsObject();

    /// <summary>
    /// Records one change by a person. Only the values that differ are kept. Returns false, and writes nothing, when
    /// nothing differs. <paramref name="about"/> holds facts that identify what changed beyond the target, such as
    /// the tool a grant is for.
    /// </summary>
    public bool Record(Guid actorUserId, string action, string targetKind, Guid targetId, JsonObject? before, JsonObject? after, JsonObject? about = null)
    {
        var changes = Changes(before ?? [], after ?? []);

        if (changes.Count == 0 && (before is not null || after is not null))
        {
            return false;
        }

        var data = about?.DeepClone().AsObject() ?? [];
        data["changes"] = changes;
        entries.Add(new AuditLogEntry
        {
            ActorKind = ActorKind.User,
            ActorId = actorUserId,
            Action = action,
            TargetKind = targetKind,
            TargetId = targetId,
            Data = JsonSerializer.SerializeToElement(data),
            CreatedAt = time.GetUtcNow(),
        });
        return true;
    }

    #endregion

    #region Private Methods

    private static JsonObject Changes(JsonObject before, JsonObject after)
    {
        JsonObject changes = [];

        foreach (var name in before.Select(pair => pair.Key).Union(after.Select(pair => pair.Key), StringComparer.Ordinal))
        {
            var was = before[name];
            var now = after[name];

            if (!JsonNode.DeepEquals(was, now))
            {
                changes[name] = new JsonObject { ["before"] = was?.DeepClone(), ["after"] = now?.DeepClone() };
            }
        }

        return changes;
    }

    #endregion
}
