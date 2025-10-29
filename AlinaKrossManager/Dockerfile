FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID 
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates && rm -rf /var/lib/apt/lists/*

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG GITHUB_USER=EvgenyYushko
ARG GITHUB_TOKEN=ghp_rtGWU6196kTD5ifykvYj9KvMrZBfku3rZ8jl

WORKDIR /src

# Копируем ТОЛЬКО .csproj сначала
COPY ["AlinaKrossManager/AlinaKrossManager.csproj", "AlinaKrossManager/"]

# ДОБАВЛЯЕМ GITHUB NUGET SOURCE ПЕРЕД restore
RUN echo "Adding GitHub NuGet source..." && \
    dotnet nuget add source "https://nuget.pkg.github.com/EvgenyYushko/index.json" \
    --name "github" \
    --username "${GITHUB_USER}" \
    --password "${GITHUB_TOKEN}" \
    --store-password-in-clear-text

# Проверяем что source добавился
RUN dotnet nuget list source

# ТЕПЕРЬ восстанавливаем зависимости
RUN dotnet restore "./AlinaKrossManager/AlinaKrossManager.csproj"

# Копируем остальные файлы
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