# Этот этап используется при запуске из VS в быстром режиме
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# --- УСТАНОВКА FFMPEG ---
USER root
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates ffmpeg && \
    rm -rf /var/lib/apt/lists/*
# ------------------------

USER $APP_UID 

# Этот этап используется для сборки
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

ARG GITHUB_USER
ARG GITHUB_TOKEN

WORKDIR /src
COPY ["AlinaKrossManager/AlinaKrossManager.csproj", "AlinaKrossManager/"]
RUN dotnet nuget add source "https://nuget.pkg.github.com/EvgenyYushko/index.json" --name "github" --username "${GITHUB_USER}" --password "${GITHUB_TOKEN}" --store-password-in-clear-text

RUN dotnet restore "./AlinaKrossManager/AlinaKrossManager.csproj"

COPY . .
WORKDIR "/src/AlinaKrossManager"
RUN dotnet build "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Финальный этап
FROM base AS final
WORKDIR /app

COPY --from=publish /app/publish .

# --- ИСПРАВЛЕНИЕ ПРАВ ДОСТУПА ---
USER root
RUN mkdir -p /app/wwwroot/temp_audio
RUN chown -R app:app /app/wwwroot
RUN chmod -R 777 /app/wwwroot
USER $APP_UID
# --------------------------------------------------

ENTRYPOINT ["dotnet", "AlinaKrossManager.dll"]