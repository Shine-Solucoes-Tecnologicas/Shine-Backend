# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
WORKDIR /src
COPY platform ./platform
COPY modules ./modules
COPY host ./host
RUN dotnet restore host/Shine.Api/Shine.Api.csproj
RUN dotnet publish host/Shine.Api/Shine.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_EnableDiagnostics=0 \
    FileStorage__RootPath=/var/lib/shine/storage
RUN mkdir -p /var/lib/shine/storage && chown -R "$APP_UID:$APP_UID" /var/lib/shine
COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
VOLUME ["/var/lib/shine/storage"]
HEALTHCHECK --interval=30s --timeout=5s --start-period=15s --retries=3 \
    CMD wget -q -O /dev/null http://127.0.0.1:8080/health || exit 1
ENTRYPOINT ["dotnet", "Shine.Api.dll"]
