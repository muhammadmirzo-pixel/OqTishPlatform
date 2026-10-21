# syntax=docker/dockerfile:1
# Multi-target build for the .NET services. Build with --target api | worker | migrator.

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_ASPNET_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/runtime:10.0

FROM ${DOTNET_SDK_IMAGE} AS build
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /src

# Optional extra root CAs for networks that intercept TLS (deploy/docker/extra-ca/*.crt, never committed).
# Build stage only; with no .crt files this is a no-op.
COPY deploy/docker/extra-ca/ /usr/local/share/ca-certificates/
RUN update-ca-certificates

# Restore layer: only project files and lock files, so source edits do not invalidate the cache.
COPY global.json Directory.Build.props Directory.Packages.props dotnet-tools.json .editorconfig ./
COPY src/OqTish.Domain/OqTish.Domain.csproj src/OqTish.Domain/packages.lock.json src/OqTish.Domain/
COPY src/OqTish.Application/OqTish.Application.csproj src/OqTish.Application/packages.lock.json src/OqTish.Application/
COPY src/OqTish.Infrastructure/OqTish.Infrastructure.csproj src/OqTish.Infrastructure/packages.lock.json src/OqTish.Infrastructure/
COPY src/OqTish.Api/OqTish.Api.csproj src/OqTish.Api/packages.lock.json src/OqTish.Api/
COPY src/OqTish.Worker/OqTish.Worker.csproj src/OqTish.Worker/packages.lock.json src/OqTish.Worker/
RUN dotnet restore src/OqTish.Api/OqTish.Api.csproj --locked-mode \
 && dotnet restore src/OqTish.Worker/OqTish.Worker.csproj --locked-mode \
 && dotnet tool restore

COPY src/ src/
RUN dotnet publish src/OqTish.Api/OqTish.Api.csproj -c Release -o /out/api --no-restore \
 && dotnet publish src/OqTish.Worker/OqTish.Worker.csproj -c Release -o /out/worker --no-restore \
 && dotnet tool run dotnet-ef migrations bundle \
      --project src/OqTish.Infrastructure/OqTish.Infrastructure.csproj \
      --startup-project src/OqTish.Infrastructure/OqTish.Infrastructure.csproj \
      --configuration Release --output /out/migrator/efbundle --force

FROM ${DOTNET_ASPNET_IMAGE} AS api
WORKDIR /app
COPY --from=build /out/api ./
USER $APP_UID
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "OqTish.Api.dll"]

FROM ${DOTNET_RUNTIME_IMAGE} AS worker
WORKDIR /app
COPY --from=build /out/worker ./
USER $APP_UID
ENTRYPOINT ["dotnet", "OqTish.Worker.dll"]

# One-shot: applies pending EF Core migrations. Reads OQTISH_MIGRATIONS_CONNECTION
# (see DesignTimeDbContextFactory). Runs as the schema-owner DB role, never the app role.
FROM ${DOTNET_RUNTIME_IMAGE} AS migrator
WORKDIR /app
COPY --from=build /out/migrator ./
USER $APP_UID
ENTRYPOINT ["./efbundle"]
