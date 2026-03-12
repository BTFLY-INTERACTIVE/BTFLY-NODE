# ── Build stage ───────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/Btfly.API/Btfly.API.csproj ./Btfly.API/
RUN dotnet restore ./Btfly.API/Btfly.API.csproj

COPY src/Btfly.API/ ./Btfly.API/
RUN dotnet publish ./Btfly.API/Btfly.API.csproj -c Release -o /app/publish

# ── Runtime stage ─────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Btfly.API.dll"]
