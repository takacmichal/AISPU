# Build context: repository root (needs Directory.*.props and global.json).
#   docker build -f src/SolarOps.Api/Dockerfile -t solarops-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /repo

# Restore first for better layer caching.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/SolarOps.Api/SolarOps.Api.csproj src/SolarOps.Api/
RUN dotnet restore src/SolarOps.Api/SolarOps.Api.csproj

COPY src/ src/
RUN dotnet publish src/SolarOps.Api/SolarOps.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "SolarOps.Api.dll"]
