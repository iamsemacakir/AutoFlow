# syntax=docker/dockerfile:1
# Build context: the repository root (see docker-compose.yml).

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, so the package layer is reused while only code changes.
COPY global.json Directory.Build.props AutoFlow.slnx .editorconfig ./
COPY src/AutoFlow.Domain/AutoFlow.Domain.csproj src/AutoFlow.Domain/
COPY src/AutoFlow.Application/AutoFlow.Application.csproj src/AutoFlow.Application/
COPY src/AutoFlow.Infrastructure/AutoFlow.Infrastructure.csproj src/AutoFlow.Infrastructure/
COPY src/AutoFlow.Api/AutoFlow.Api.csproj src/AutoFlow.Api/
RUN dotnet restore src/AutoFlow.Api/AutoFlow.Api.csproj

COPY src/ src/
RUN dotnet publish src/AutoFlow.Api/AutoFlow.Api.csproj --configuration Release --no-restore --output /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080

# The base image's unprivileged user.
USER $APP_UID

# The runtime image has no curl; bash talks HTTP to the readiness endpoint itself.
HEALTHCHECK --interval=10s --timeout=3s --start-period=20s --retries=6 \
    CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health/ready HTTP/1.0\\r\\nHost: localhost\\r\\n\\r\\n' >&3 && grep -q Healthy <&3"]

ENTRYPOINT ["dotnet", "AutoFlow.Api.dll"]
