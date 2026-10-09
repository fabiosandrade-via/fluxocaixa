FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
RUN printf '<?xml version="1.0" encoding="utf-8"?>\n\
<configuration>\n\
  <fallbackPackageFolders>\n\
  </fallbackPackageFolders>\n\
  <packageSources>\n\
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />\n\
  </packageSources>\n\
</configuration>' > /src/nuget.config

COPY src/backend/ /src/backend/
RUN dotnet publish backend/FluxoCaixa.EventPublisher/FluxoCaixa.EventPublisher.Worker/FluxoCaixa.EventPublisher.Worker.csproj \
    -c Release -o /app/publish --configfile /src/nuget.config

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FluxoCaixa.EventPublisher.Worker.dll"]
