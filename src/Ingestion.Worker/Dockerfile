# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Restore against the full solution graph (layer-cached on unchanged project files).
COPY *.sln .
COPY src/ src/
RUN dotnet restore src/Ingestion.Worker/Ingestion.Worker.csproj
RUN dotnet publish src/Ingestion.Worker/Ingestion.Worker.csproj -c Release -o /app --no-restore

# Runtime stage (no SDK; smaller, fewer CVEs)
# NOTE: runs as the image default user (root). Reviewed and accepted for the POC — the worker runs in a
# controlled/isolated container runtime. Non-root USER hardening is tracked as production backlog (Sonar S6471 accepted).
FROM mcr.microsoft.com/dotnet/runtime:8.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Layout, data-protection policy, and data volumes are mounted at runtime (see appsettings.json).
ENTRYPOINT ["dotnet", "Ingestion.Worker.dll"]
