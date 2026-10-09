using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FluxoCaixa.Lancamentos.Api.Swagger;

internal sealed class ConfigureSwaggerOptions(
    IApiVersionDescriptionProvider provider) : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new()
            {
                Title = "FluxoCaixa - Lançamentos API",
                Version = description.ApiVersion.ToString()
            });
        }
    }
}
