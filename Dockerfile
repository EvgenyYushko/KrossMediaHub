# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

# Этот этап используется при запуске из VS в быстром режиме (по умолчанию для конфигурации Debug)
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
# Пользователь APP_UID, скорее всего, определен в другом месте, оставляем как есть.
USER $APP_UID 
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

# --- ИСПРАВЛЕНИЕ НАЧИНАЕТСЯ ЗДЕСЬ ---
# Временно переключаемся на root, чтобы установить пакеты
USER root
# НОВЫЙ ШАГ ДЛЯ ИСПРАВЛЕНИЯ ОШИБКИ SSL/TLS
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*
# Снова переключаемся на не-рутового пользователя для последующих шагов и запуска
USER $APP_UID
# --- ИСПРАВЛЕНИЕ ЗАКАНЧИВАЕТСЯ ЗДЕСЬ ---

# Этот этап используется для сборки проекта сервиса
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# ДОБАВЛЯЕМ ARG ДЛЯ NUGET АУТЕНТИФИКАЦИИ
ARG GITHUB_USER
ARG GITHUB_TOKEN

WORKDIR /src

RUN echo "DEBUG INFO: GITHUB_USER is ${GITHUB_USER}"
RUN echo "DEBUG INFO: GITHUB_TOKEN starts with ${GITHUB_TOKEN:0:5} and ends with ${GITHUB_TOKEN: -5}"

# Копируем файл .csproj
COPY ["AlinaKrossManager/AlinaKrossManager.csproj", "AlinaKrossManager/"]
RUN dotnet nuget add source "https://nuget.pkg.github.com/EvgenyYushko/index.json" --name "github" --username "${GITHUB_USER}" --password "${GITHUB_TOKEN}" --store-password-in-clear-text

# Восстанавливаем зависимости
RUN dotnet restore "./AlinaKrossManager/AlinaKrossManager.csproj"

# Копируем остальную часть проекта (из корня репозитория)
COPY . .

# Переходим в директорию проекта для сборки
WORKDIR "/src/AlinaKrossManager"

# Собираем проект
RUN dotnet build "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Этот этап используется для публикации проекта сервиса
FROM build AS publish
ARG BUILD_CONFIGURATION=Release
# Публикуем проект
RUN dotnet publish "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Этот этап используется в продакшене или при запуске из VS в обычном режиме
FROM base AS final
WORKDIR /app

# Копируем опубликованные файлы
COPY --from=publish /app/publish .

# Точка входа
ENTRYPOINT ["dotnet", "AlinaKrossManager.dll"]