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
}

/// <summary>
/// Contexto MongoDB — substitui o EF Core DbContext.
/// </summary>
public sealed class MongoDbContext
{
    private readonly IMongoDatabase _database;
    private readonly MongoDbOptions _options;

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
        var client = new MongoClient(_options.ConnectionString);
        _database = client.GetDatabase(_options.DatabaseName);

        EnsureIndexesAsync().GetAwaiter().GetResult();
    }

    public IMongoCollection<SaldoConsolidado> SaldosConsolidados =>
        _database.GetCollection<SaldoConsolidado>(_options.ColecaoSaldos);

    private async Task EnsureIndexesAsync()
    {
        var indexModel = new CreateIndexModel<SaldoConsolidado>(
            Builders<SaldoConsolidado>.IndexKeys.Ascending(s => s.Data),
            new CreateIndexOptions { Unique = true, Name = "idx_data_unique" }
        );
        await SaldosConsolidados.Indexes.CreateOneAsync(indexModel);
    }
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
