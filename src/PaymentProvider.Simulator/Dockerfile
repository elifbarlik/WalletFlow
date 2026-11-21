# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/PaymentProvider.Simulator/*.csproj src/PaymentProvider.Simulator/
RUN dotnet restore src/PaymentProvider.Simulator/PaymentProvider.Simulator.csproj
COPY src/PaymentProvider.Simulator/ src/PaymentProvider.Simulator/
RUN dotnet publish src/PaymentProvider.Simulator/PaymentProvider.Simulator.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8081 DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
COPY --from=build /app .
USER $APP_UID
HEALTHCHECK --interval=15s --timeout=3s --start-period=10s --retries=3 \
  CMD wget -qO- http://127.0.0.1:8081/health >/dev/null || exit 1
ENTRYPOINT ["dotnet", "PaymentProvider.Simulator.dll"]
