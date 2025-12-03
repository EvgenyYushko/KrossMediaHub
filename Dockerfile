# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Этот этап используется при запуске из VS в быстром режиме
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID 
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# Временно переключаемся на root для установки пакетов
USER root
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
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

# --- ИСПРАВЛЕНИЕ ПРАВ ДОСТУПА (ЖЕЛЕЗОБЕТОННОЕ) ---
USER root

# 1. Явно создаем структуру папок
RUN mkdir -p /app/wwwroot/temp_audio

# 2. Используем имя пользователя 'app' вместо переменной $APP_UID, 
#    так как в образах mcr.microsoft.com/dotnet/aspnet оно существует всегда.
RUN chown -R app:app /app/wwwroot

# 3. Даем права 777 рекурсивно (-R), чтобы писать мог кто угодно.
#    Это решает проблему, даже если юзернейм не совпадет.
RUN chmod -R 777 /app/wwwroot

# 4. Возвращаемся к пользователю приложения
USER $APP_UID
# --------------------------------------------------

ENTRYPOINT ["dotnet", "AlinaKrossManager.dll"]