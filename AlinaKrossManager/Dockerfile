# See https://aka.ms/customizecontainer to learn how to customize your debug container and how Visual Studio uses this Dockerfile to build your images for faster debugging.

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID 
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release

# ДОБАВЛЯЕМ ARG ДЛЯ NUGET АУТЕНТИФИКАЦИИ
ARG GITHUB_USER
ARG GITHUB_TOKEN

WORKDIR /src

# ДОБАВЛЯЕМ ОТЛАДКУ - проверяем переменные
RUN echo "GITHUB_USER: ${GITHUB_USER}" && \
    echo "GITHUB_TOKEN length: ${#GITHUB_TOKEN}" && \
    echo "GITHUB_TOKEN first 10 chars: ${GITHUB_TOKEN:0:10}"

COPY ["AlinaKrossManager/AlinaKrossManager.csproj", "AlinaKrossManager/"]

# ДОБАВЛЯЕМ NUGET SOURCE С ПРОВЕРКОЙ
RUN dotnet nuget list source && \
    dotnet nuget add source "https://nuget.pkg.github.com/EvgenyYushko/index.json" --name "github" --username "${GITHUB_USER}" --password "${GITHUB_TOKEN}" --store-password-in-clear-text && \
    dotnet nuget list source

# Восстанавливаем зависимости
RUN dotnet restore "./AlinaKrossManager/AlinaKrossManager.csproj"

COPY . .
WORKDIR "/src/AlinaKrossManager"
RUN dotnet build "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "AlinaKrossManager.dll"]