# syntax=docker/dockerfile:1
# Сборка одного веб-проекта: задайте PROJECT_FILE и ENTRY_DLL (имя основной DLL после publish).
ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${DOTNET_SDK_IMAGE} AS build
WORKDIR /src

COPY building-blocks/ building-blocks/
COPY gateways/ gateways/
COPY services/ services/
COPY tools/ tools/

ARG PROJECT_FILE
RUN test -n "$PROJECT_FILE" || (echo "ARG PROJECT_FILE is required" >&2 && exit 1)

# NuGet cache-mount убран: параллельные build-и compose портят общий /root/.nuget/packages.
RUN dotnet restore "$PROJECT_FILE"

RUN dotnet publish "$PROJECT_FILE" \
    -c Release \
    --no-restore \
    -o /app/publish \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS final
WORKDIR /app

ARG ENTRY_DLL
RUN test -n "$ENTRY_DLL" || (echo "ARG ENTRY_DLL is required" >&2 && exit 1)
ENV APP_DLL=${ENTRY_DLL}
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .
RUN chown -R app:app /app

EXPOSE 8080
USER app
ENTRYPOINT ["sh", "-c", "exec dotnet /app/$APP_DLL"]
