# syntax=docker/dockerfile:1
# Build context: repository root (docker build -f src/WalletFlow.Api/Dockerfile .)
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
# Restore first so the layer is cached until a csproj changes.
COPY global.json Directory.Build.props ./
COPY src/WalletFlow.Domain/*.csproj src/WalletFlow.Domain/
COPY src/WalletFlow.Application/*.csproj src/WalletFlow.Application/
COPY src/WalletFlow.Infrastructure/*.csproj src/WalletFlow.Infrastructure/
COPY src/WalletFlow.Api/*.csproj src/WalletFlow.Api/
RUN dotnet restore src/WalletFlow.Api/WalletFlow.Api.csproj
COPY src/ src/
RUN dotnet publish src/WalletFlow.Api/WalletFlow.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true \
    DOTNET_EnableDiagnostics=0
COPY --from=build /app .
# Run as the unprivileged user that ships with the base image.
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=15s --timeout=3s --start-period=20s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8080/health/live >/dev/null || exit 1
ENTRYPOINT ["dotnet", "WalletFlow.Api.dll"]
