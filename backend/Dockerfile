# Build stage: restore + publish
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY backend/*.csproj backend/
RUN dotnet restore backend/ClubHub.Api.csproj
COPY backend/ backend/
RUN dotnet publish backend/ClubHub.Api.csproj -c Release -o /app --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 5000
ENTRYPOINT ["dotnet", "ClubHub.Api.dll"]
