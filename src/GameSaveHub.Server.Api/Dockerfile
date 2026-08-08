FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY . .
RUN dotnet restore src/GameSaveHub.Server.Api/GameSaveHub.Server.Api.csproj --runtime linux-musl-x64 \
    && dotnet publish src/GameSaveHub.Server.Api/GameSaveHub.Server.Api.csproj --configuration Release --runtime linux-musl-x64 \
       --self-contained false --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS final
RUN addgroup -S gamesavehub && adduser -S -G gamesavehub -h /app gamesavehub
WORKDIR /app
COPY --from=build --chown=gamesavehub:gamesavehub /app .
USER gamesavehub
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "GameSaveHub.Server.Api.dll"]
