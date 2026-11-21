# syntax=docker/dockerfile:1
FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY global.json Directory.Build.props ./
COPY src/Notification.Worker/*.csproj src/Notification.Worker/
RUN dotnet restore src/Notification.Worker/Notification.Worker.csproj
COPY src/Notification.Worker/ src/Notification.Worker/
RUN dotnet publish src/Notification.Worker/Notification.Worker.csproj -c Release -o /app --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:8.0-alpine AS final
WORKDIR /app
ENV DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=true
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["dotnet", "Notification.Worker.dll"]
