using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace FluxoCaixa.Consolidado.Api.Swagger;

internal sealed class ConfigureSwaggerOptions(
    IApiVersionDescriptionProvider provider) : IConfigureOptions<SwaggerGenOptions>
{
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc(description.GroupName, new()
            {
                Title = "FluxoCaixa - Consolidado Diário API",
                Version = description.ApiVersion.ToString()
            });
        }
    }
}
