FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
USER $APP_UID 
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

RUN echo "=== START: Installing CA certificates ===" && \
    apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates && \
    rm -rf /var/lib/apt/lists/* && \
    echo "=== COMPLETE: CA certificates installed ==="

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG BUILD_CONFIGURATION=Release
ARG GITHUB_USER=EvgenyYushko
ARG GITHUB_TOKEN=ghp_rtGWU6196kTD5ifykvYj9KvMrZBfku3rZ8jl

WORKDIR /src

RUN echo "=== START: Build stage - Setting up environment ==="

# Копируем ТОЛЬКО .csproj сначала
RUN echo "=== STEP 1: Copying project file ==="
COPY ["AlinaKrossManager/AlinaKrossManager.csproj", "AlinaKrossManager/"]
RUN echo "Project file copied successfully"

# ДОБАВЛЯЕМ GITHUB NUGET SOURCE ПЕРЕД restore
RUN echo "=== STEP 2: Adding GitHub NuGet source ===" && \
    echo "Adding GitHub NuGet source for user: ${GITHUB_USER}" && \
    dotnet nuget add source "https://nuget.pkg.github.com/EvgenyYushko/index.json" \
    --name "github" \
    --username "${GITHUB_USER}" \
    --password "${GITHUB_TOKEN}" \
    --store-password-in-clear-text && \
    echo "GitHub NuGet source added successfully"

# Проверяем что source добавился
RUN echo "=== STEP 3: Listing available NuGet sources ==="
RUN dotnet nuget list source
RUN echo "NuGet sources listed"

# ТЕПЕРЬ восстанавливаем зависимости
RUN echo "=== STEP 4: Restoring dependencies ==="
RUN echo "Starting dotnet restore for AlinaKrossManager.csproj..."
RUN dotnet restore "./AlinaKrossManager/AlinaKrossManager.csproj"
RUN echo "=== SUCCESS: Dependencies restored ==="

# Копируем остальные файлы
RUN echo "=== STEP 5: Copying remaining source files ==="
COPY . .
RUN echo "Source files copied successfully"

WORKDIR "/src/AlinaKrossManager"

RUN echo "=== STEP 6: Building project ==="
RUN echo "Building with configuration: $BUILD_CONFIGURATION"
RUN dotnet build "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/build
RUN echo "=== SUCCESS: Build completed ==="

RUN echo "=== COMPLETE: Build stage finished ==="

FROM build AS publish
ARG BUILD_CONFIGURATION=Release

RUN echo "=== START: Publish stage ==="
RUN echo "Publishing with configuration: $BUILD_CONFIGURATION"
RUN dotnet publish "./AlinaKrossManager.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN echo "=== SUCCESS: Publish completed ==="

FROM base AS final
WORKDIR /app

RUN echo "=== START: Final stage - Preparing runtime ==="
RUN echo "Copying published files..."
COPY --from=publish /app/publish .
RUN echo "Published files copied successfully"
RUN echo "=== COMPLETE: Application ready ==="

ENTRYPOINT ["dotnet", "AlinaKrossManager.dll"]