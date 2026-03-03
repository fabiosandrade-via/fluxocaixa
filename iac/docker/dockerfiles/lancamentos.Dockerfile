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

COPY src/backend/FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Domain/FluxoCaixa.Lancamentos.Domain.csproj \
     FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Domain/

COPY src/backend/FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Application/FluxoCaixa.Lancamentos.Application.csproj \
     FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Application/

COPY src/backend/FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Application.Services/FluxoCaixa.Lancamentos.Application.Services.csproj \
     FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Application.Services/

COPY src/backend/FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Infrastructure/FluxoCaixa.Lancamentos.Infrastructure.csproj \
     FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Infrastructure/

COPY src/backend/FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Api/FluxoCaixa.Lancamentos.Api.csproj \
     FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Api/

RUN dotnet restore FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Api/FluxoCaixa.Lancamentos.Api.csproj \
    --configfile /src/nuget.config

COPY src/backend/FluxoCaixa.SharedKernel/   FluxoCaixa.SharedKernel/
COPY src/backend/FluxoCaixa.Lancamentos/    FluxoCaixa.Lancamentos/

RUN dotnet publish FluxoCaixa.Lancamentos/FluxoCaixa.Lancamentos.Api/FluxoCaixa.Lancamentos.Api.csproj \
    -c Release -o /app/publish \
    --configfile /src/nuget.config

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN groupadd --system appgroup && useradd --system --gid appgroup appuser
USER appuser

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "FluxoCaixa.Lancamentos.Api.dll"]