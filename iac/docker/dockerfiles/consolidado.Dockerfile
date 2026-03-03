FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

RUN printf '<?xml version="1.0" encoding="utf-8"?>\n\
<configuration>\n\
  <fallbackPackageFolders>\n\
  </fallbackPackageFolders>\n\
  <packageSources>\n\
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />\n\
  </packageSources>\n\
</configuration>' > /src/nuget.config

COPY src/backend/FluxoCaixa.SharedKernel/FluxoCaixa.SharedKernel.csproj \
     FluxoCaixa.SharedKernel/

COPY src/backend/FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Domain/FluxoCaixa.Consolidado.Domain.csproj \
     FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Domain/

COPY src/backend/FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Application/FluxoCaixa.Consolidado.Application.csproj \
     FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Application/

COPY src/backend/FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Application.Services/FluxoCaixa.Consolidado.Application.Services.csproj \
     FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Application.Services/

COPY src/backend/FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Infrastructure/FluxoCaixa.Consolidado.Infrastructure.csproj \
     FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Infrastructure/

COPY src/backend/FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Api/FluxoCaixa.Consolidado.Api.csproj \
     FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Api/

RUN dotnet restore FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Api/FluxoCaixa.Consolidado.Api.csproj \
    --configfile /src/nuget.config

COPY src/backend/FluxoCaixa.SharedKernel/   FluxoCaixa.SharedKernel/
COPY src/backend/FluxoCaixa.Consolidado/    FluxoCaixa.Consolidado/

RUN dotnet publish FluxoCaixa.Consolidado/FluxoCaixa.Consolidado.Api/FluxoCaixa.Consolidado.Api.csproj \
    -c Release -o /app/publish \
    --configfile /src/nuget.config

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FluxoCaixa.Consolidado.Api.dll"]