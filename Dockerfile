# syntax=docker/dockerfile:1.7
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-alpine AS build
ARG TARGETARCH
WORKDIR /workspace
COPY global.json ./
COPY src/LendUp.Notifications.Api/LendUp.Notifications.Api.csproj src/LendUp.Notifications.Api/
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore src/LendUp.Notifications.Api/LendUp.Notifications.Api.csproj -a $TARGETARCH
COPY src ./src
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish src/LendUp.Notifications.Api/LendUp.Notifications.Api.csproj \
    -c Release -a $TARGETARCH --no-restore -o /app /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0-alpine
# tzdata para America/Lima e icu para cultura es-PE; wget viene en la imagen base (busybox).
RUN apk add --no-cache tzdata icu-libs
WORKDIR /app
COPY --from=build --chown=app:app /app ./
# Usuario sin privilegios que trae la imagen oficial (UID 1654).
USER $APP_UID
EXPOSE 8083
ENV SERVER_PORT=8083 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false \
    DOTNET_gcServer=0
HEALTHCHECK --interval=30s --timeout=3s --start-period=30s --retries=3 CMD wget -qO- "http://127.0.0.1:${SERVER_PORT}/actuator/health/readiness" || exit 1
ENTRYPOINT ["dotnet", "LendUp.Notifications.Api.dll"]
