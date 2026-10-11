# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Packages.props .
COPY src/Einvoice.Domain/Einvoice.Domain.csproj src/Einvoice.Domain/
COPY src/Einvoice.Infrastructure/Einvoice.Infrastructure.csproj src/Einvoice.Infrastructure/
COPY src/Einvoice.Api/Einvoice.Api.csproj src/Einvoice.Api/
RUN dotnet restore src/Einvoice.Api/Einvoice.Api.csproj

COPY src/ src/
RUN dotnet publish src/Einvoice.Api/Einvoice.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .
EXPOSE 8080

USER $APP_UID

HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 \
    CMD curl -fsS http://localhost:8080/health/live || exit 1

ENTRYPOINT ["dotnet", "Einvoice.Api.dll"]
