using System.Security.Cryptography;
using System.Text;
using FabCopilot.VectorStore.Configuration;
using FabCopilot.VectorStore.Interfaces;
using FabCopilot.VectorStore.Models;
using Microsoft.Extensions.Options;
using Qdrant.Client;
using Qdrant.Client.Grpc;

namespace FabCopilot.VectorStore;

public sealed class QdrantVectorStore : IVectorStore
{
    private readonly QdrantClient _client;

    public QdrantVectorStore(IOptions<QdrantOptions> options)
    {
        var opts = options.Value;
        _client = new QdrantClient(opts.Host, opts.GrpcPort);
    }

    public async Task EnsureCollectionAsync(string collection, int vectorSize, CancellationToken ct = default)
    {
        var exists = await _client.CollectionExistsAsync(collection, ct);
        if (!exists)
        {
            await _client.CreateCollectionAsync(
                collection,
                new VectorParams
                {
                    Size = (ulong)vectorSize,
                    Distance = Distance.Cosine
                },
                cancellationToken: ct);
        }
    }

    public async Task UpsertAsync(
        string collection,
        string id,
        float[] vector,
        Dictionary<string, object> payload,
        CancellationToken ct = default)
    {
        var pointId = ToPointId(id);
        var point = new PointStruct
        {
            Id = pointId,
            Vectors = vector
        };

        foreach (var (key, value) in payload)
        {
            point.Payload[key] = ToQdrantValue(value);
        }

        await _client.UpsertAsync(collection, [point], cancellationToken: ct);
    }

    public async Task<IReadOnlyList<VectorSearchResult>> SearchAsync(
        string collection,
        float[] queryVector,
        int topK = 5,
        Dictionary<string, object>? filter = null,
        CancellationToken ct = default)
    {
        var qdrantFilter = BuildQdrantFilter(filter);

        var results = await _client.QueryAsync(
            collection,
            query: queryVector,
            limit: (ulong)topK,
            filter: qdrantFilter,
            payloadSelector: true,
            cancellationToken: ct);

        var searchResults = new List<VectorSearchResult>(results.Count);

        foreach (var point in results)
        {
            var pointId = ExtractId(point.Id);
            var score = point.Score;
            var payload = FromQdrantPayload(point.Payload);

            searchResults.Add(new VectorSearchResult(pointId, score, payload));
        }

        return searchResults;
    }

    public async Task DeleteAsync(string collection, string id, CancellationToken ct = default)
    {
        var guid = ToGuid(id);
        await _client.DeleteAsync(collection, [guid], cancellationToken: ct);
    }

    public async Task DeleteByDocumentIdAsync(string collection, string documentId, CancellationToken ct = default)
    {
        var filter = new Filter();
        filter.Must.Add(new Condition
        {
            Field = new FieldCondition
            {
                Key = "document_id",
                Match = new Match { Keyword = documentId }
            }
        });

        await _client.DeleteAsync(collection, filter, cancellationToken: ct);
    }

    internal static Filter? BuildQdrantFilter(Dictionary<string, object>? filter)
    {
        if (filter is null || filter.Count == 0)
            return null;

        var f = new Filter();
        foreach (var (key, value) in filter)
        {
            f.Must.Add(new Condition
            {
                Field = new FieldCondition
                {
                    Key = key,
                    Match = new Match { Keyword = value?.ToString() ?? "" }
                }
            });
        }

        return f;
    }

    /// <summary>
    /// Converts a string ID to a deterministic Guid.
    /// If the string is already a valid Guid, it is used directly.
    /// Otherwise, MD5 hashing produces a stable 128-bit value from the string.
    /// </summary>
    private static Guid ToGuid(string id)
    {
        if (Guid.TryParse(id, out var guid))
        {
            return guid;
        }

        var hash = MD5.HashData(Encoding.UTF8.GetBytes(id));
        return new Guid(hash);
    }

    /// <summary>
    /// Converts a string ID to a deterministic Guid-based PointId.
    /// </summary>
    private static PointId ToPointId(string id)
    {
        return ToGuid(id);
    }

    /// <summary>
    /// Extracts the string representation of a PointId.
    /// </summary>
    private static string ExtractId(PointId? pointId)
    {
        if (pointId is null) return string.Empty;

        return pointId.PointIdOptionsCase switch
        {
            PointId.PointIdOptionsOneofCase.Uuid => pointId.Uuid,
            PointId.PointIdOptionsOneofCase.Num => pointId.Num.ToString(),
            _ => string.Empty
        };
    }

    /// <summary>
    /// Converts a CLR object to a Qdrant protobuf Value.
    /// </summary>
    private static Value ToQdrantValue(object? value)
    {
        return value switch
        {
            null => new Value { NullValue = NullValue.NullValue },
            string s => new Value { StringValue = s },
            bool b => new Value { BoolValue = b },
            int i => new Value { IntegerValue = i },
            long l => new Value { IntegerValue = l },
            float f => new Value { DoubleValue = f },
            double d => new Value { DoubleValue = d },
            IEnumerable<object> list => ToListValue(list),
            IDictionary<string, object> dict => ToStructValue(dict),
            _ => new Value { StringValue = value.ToString() ?? string.Empty }
        };
    }

    private static Value ToListValue(IEnumerable<object> items)
    {
        var listValue = new ListValue();
        foreach (var item in items)
        {
            listValue.Values.Add(ToQdrantValue(item));
        }
        return new Value { ListValue = listValue };
    }

    private static Value ToStructValue(IDictionary<string, object> dict)
    {
        var structValue = new Struct();
        foreach (var (key, val) in dict)
        {
            structValue.Fields[key] = ToQdrantValue(val);
        }
        return new Value { StructValue = structValue };
    }

    /// <summary>
    /// Converts Qdrant protobuf payload back to a CLR dictionary.
    /// </summary>
    private static Dictionary<string, object> FromQdrantPayload(
        IDictionary<string, Value>? payload)
    {
        var result = new Dictionary<string, object>();
        if (payload is null) return result;

        foreach (var (key, value) in payload)
        {
            result[key] = FromQdrantValue(value);
        }

        return result;
    }

    /// <summary>
    /// Converts a Qdrant protobuf Value back to a CLR object.
    /// </summary>
    private static object FromQdrantValue(Value value)
    {
        return value.KindCase switch
        {
            Value.KindOneofCase.StringValue => value.StringValue,
            Value.KindOneofCase.BoolValue => value.BoolValue,
            Value.KindOneofCase.IntegerValue => value.IntegerValue,
            Value.KindOneofCase.DoubleValue => value.DoubleValue,
            Value.KindOneofCase.ListValue => value.ListValue.Values
                .Select(FromQdrantValue).ToList(),
            Value.KindOneofCase.StructValue => value.StructValue.Fields
                .ToDictionary(f => f.Key, f => FromQdrantValue(f.Value)),
            Value.KindOneofCase.NullValue => null!,
            _ => value.StringValue ?? string.Empty
        };
    }
}
