using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace FluxoCaixa.Projetor.Infrastructure.Persistence;

public sealed class ProjectorMongoOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "consolidado_db";
    public string ColecaoSaldos { get; set; } = "saldos_consolidados";
    public string ColecaoEventosProcessados { get; set; } = "eventos_processados";
}

public sealed class ProjectorMongoContext
{
    private readonly IMongoDatabase _database;
    public IMongoClient Client { get; }

    static ProjectorMongoContext()
    {
        var conventions = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true)
        };
        ConventionRegistry.Register("FluxoCaixaProjectorConventions", conventions, _ => true);
        BsonSerializer.RegisterSerializer(new ProjectorDateOnlySerializer());
        BsonSerializer.RegisterSerializer(new GuidSerializer(GuidRepresentation.Standard));
    }

    public ProjectorMongoContext(IOptions<ProjectorMongoOptions> options)
    {
        var settings = options.Value;
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.ConnectionString);
        ArgumentException.ThrowIfNullOrWhiteSpace(settings.DatabaseName);

        Client = new MongoClient(settings.ConnectionString);
        _database = Client.GetDatabase(settings.DatabaseName);
        Saldos = _database.GetCollection<ProjectedDailyBalance>(settings.ColecaoSaldos);
        ProcessedEvents = _database.GetCollection<ProjectedEventRecord>(settings.ColecaoEventosProcessados);

        EnsureIndexesAsync().GetAwaiter().GetResult();
    }

    public IMongoCollection<ProjectedDailyBalance> Saldos { get; }
    public IMongoCollection<ProjectedEventRecord> ProcessedEvents { get; }

    private async Task EnsureIndexesAsync()
    {
        var eventIndex = new CreateIndexModel<ProjectedEventRecord>(
            Builders<ProjectedEventRecord>.IndexKeys
                .Ascending(evento => evento.Projetor)
                .Ascending(evento => evento.EventoId),
            new CreateIndexOptions { Unique = true, Name = "uq_projetor_evento" });

        await ProcessedEvents.Indexes.CreateOneAsync(eventIndex);
    }
}

public sealed class ProjectedDailyBalance
{
    [MongoDB.Bson.Serialization.Attributes.BsonId]
    public string Id { get; set; } = string.Empty;

    [MongoDB.Bson.Serialization.Attributes.BsonElement("data")]
    public DateOnly Data { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("totalCreditos")]
    public decimal TotalCreditos { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("totalDebitos")]
    public decimal TotalDebitos { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("atualizadoEm")]
    public DateTime AtualizadoEm { get; set; }

    [MongoDB.Bson.Serialization.Attributes.BsonElement("criadoEm")]
    public DateTime CriadoEm { get; set; }
}

public sealed class ProjectedEventRecord
{
    public string Projetor { get; set; } = string.Empty;
    public Guid EventoId { get; set; }
    public DateTime ProcessadoEm { get; set; }
}

internal sealed class ProjectorDateOnlySerializer : SerializerBase<DateOnly>
{
    public override DateOnly Deserialize(
        BsonDeserializationContext context,
        BsonDeserializationArgs args)
        => DateOnly.ParseExact(context.Reader.ReadString(), "yyyy-MM-dd");

    public override void Serialize(
        BsonSerializationContext context,
        BsonSerializationArgs args,
        DateOnly value)
        => context.Writer.WriteString(value.ToString("yyyy-MM-dd"));
}
