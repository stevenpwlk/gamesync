FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/GameSaveHub.Server.Admin/GameSaveHub.Server.Admin.csproj --runtime linux-musl-x64 \
    && dotnet publish src/GameSaveHub.Server.Admin/GameSaveHub.Server.Admin.csproj --configuration Release --runtime linux-musl-x64 \
       --self-contained false --no-restore --output /app

FROM mcr.microsoft.com/dotnet/runtime:10.0-alpine AS final
RUN addgroup -S gamesavehub && adduser -S -G gamesavehub -h /app gamesavehub
WORKDIR /app
COPY --from=build --chown=gamesavehub:gamesavehub /app .
USER gamesavehub
ENTRYPOINT ["dotnet", "GameSaveHub.Server.Admin.dll"]
