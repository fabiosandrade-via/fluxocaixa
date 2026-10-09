using FluxoCaixa.Consolidado.Domain.Entities;
using FluxoCaixa.SharedKernel.ValueObjects;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;
using MongoDB.Driver;

namespace FluxoCaixa.Consolidado.Infrastructure.Persistence;

public sealed class MongoDbOptions
{
    public string ConnectionString { get; set; } = string.Empty;
    public string DatabaseName { get; set; } = "consolidado_db";
    public string ColecaoSaldos { get; set; } = "saldos_consolidados";
    public string ColecaoEventosProcessados { get; set; } = "eventos_processados";
}

/// <summary>
/// Contexto MongoDB — substitui o EF Core DbContext.
/// </summary>
public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly MongoDbOptions _options;
    public IMongoClient Client { get; }

    static MongoDbContext()
    {
        // Convenções globais: camelCase nas propriedades do documento
        var pack = new ConventionPack
        {
            new CamelCaseElementNameConvention(),
            new IgnoreExtraElementsConvention(true)
        };
        ConventionRegistry.Register("FluxoCaixaConventions", pack, _ => true);

        BsonSerializer.RegisterSerializer(new DateOnlyBsonSerializer());
        BsonSerializer.RegisterSerializer(new DinheiroBsonSerializer());
    }

    public MongoDbContext(IOptions<MongoDbOptions> options)
    {
        _options = options.Value;
        Client = new MongoClient(_options.ConnectionString);
        _database = Client.GetDatabase(_options.DatabaseName);

        EnsureIndexesAsync().GetAwaiter().GetResult();
    }

    public IMongoCollection<SaldoConsolidado> SaldosConsolidados =>
        _database.GetCollection<SaldoConsolidado>(_options.ColecaoSaldos);

    public IMongoCollection<EventoProcessadoRecord> EventosProcessados =>
        _database.GetCollection<EventoProcessadoRecord>(_options.ColecaoEventosProcessados);

    public IMongoCollection<ReprocessamentoDlqRecord> ReprocessamentosDlq =>
        _database.GetCollection<ReprocessamentoDlqRecord>("reprocessamentos_dlq");

    private async Task EnsureIndexesAsync()
    {
        var indexModel = new CreateIndexModel<SaldoConsolidado>(
            Builders<SaldoConsolidado>.IndexKeys.Ascending(s => s.Data),
            new CreateIndexOptions { Unique = true, Name = "idx_data_unique" }
        );
        await SaldosConsolidados.Indexes.CreateOneAsync(indexModel);

        var eventosIndex = new CreateIndexModel<EventoProcessadoRecord>(
            Builders<EventoProcessadoRecord>.IndexKeys
                .Ascending(e => e.Projetor)
                .Ascending(e => e.EventoId),
            new CreateIndexOptions { Unique = true, Name = "uq_projetor_evento" });
        await EventosProcessados.Indexes.CreateOneAsync(eventosIndex);

        var reprocessamentoIndex = new CreateIndexModel<ReprocessamentoDlqRecord>(
            Builders<ReprocessamentoDlqRecord>.IndexKeys.Ascending(e => e.ReprocessamentoId),
            new CreateIndexOptions { Unique = true, Name = "uq_reprocessamento_id" });
        await ReprocessamentosDlq.Indexes.CreateOneAsync(reprocessamentoIndex);
    }
}

public sealed class EventoProcessadoRecord
{
    public string Projetor { get; set; } = string.Empty;
    public Guid EventoId { get; set; }
    public DateTime ProcessadoEm { get; set; } = DateTime.UtcNow;
}

public sealed class ReprocessamentoDlqRecord
{
    public Guid ReprocessamentoId { get; set; }
    public Guid FalhaId { get; set; }
    public Guid EventoId { get; set; }
    public string Solicitante { get; set; } = string.Empty;
    public string Motivo { get; set; } = string.Empty;
    public string Estado { get; set; } = "solicitado";
    public DateTime SolicitadoEm { get; set; } = DateTime.UtcNow;
    public DateTime? PublicadoEm { get; set; }
}

internal sealed class DateOnlyBsonSerializer : SerializerBase<DateOnly>
{
    public override DateOnly Deserialize(BsonDeserializationContext ctx, BsonDeserializationArgs args)
    {
        var str = ctx.Reader.ReadString();
        return DateOnly.ParseExact(str, "yyyy-MM-dd");
    }

    public override void Serialize(BsonSerializationContext ctx, BsonSerializationArgs args, DateOnly value)
        => ctx.Writer.WriteString(value.ToString("yyyy-MM-dd"));
}

internal sealed class DinheiroBsonSerializer : SerializerBase<Dinheiro>
{
    public override Dinheiro Deserialize(BsonDeserializationContext ctx, BsonDeserializationArgs args)
    {
        var value = ctx.Reader.ReadDecimal128();
        return Dinheiro.De((decimal)value);
    }

    public override void Serialize(BsonSerializationContext ctx, BsonSerializationArgs args, Dinheiro value)
        => ctx.Writer.WriteDecimal128(new Decimal128(value.Valor));
}
