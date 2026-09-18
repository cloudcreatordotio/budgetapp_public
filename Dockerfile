# Multi-stage build. The build stage runs on the builder's native architecture and
# cross-compiles for the target (dotnet maps TARGETARCH amd64 -> RID linux-x64), so an
# arm64 Mac produces an amd64 image without emulating the SDK. Build with:
#   docker buildx build --platform linux/amd64 ...   (see deploy/build-push.sh)
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first so the NuGet layer caches while source changes. This layer only
# warms the package cache: publish below must re-restore, because a restore run
# while only the csproj exists records "no static web assets" in obj/, and a
# later publish --no-restore trusts that and silently drops
# wwwroot/_framework/blazor.web.js from the output (no script -> no Blazor
# interactivity; seen live as 404s behind the Session 6 proxy).
COPY BudgetApp/BudgetApp.csproj BudgetApp/
RUN dotnet restore BudgetApp/BudgetApp.csproj -a $TARGETARCH

COPY BudgetApp/ BudgetApp/
RUN dotnet publish BudgetApp/BudgetApp.csproj -a $TARGETARCH -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .
# Non-root user shipped with the aspnet image; it can bind 8080 (the image's default
# ASPNETCORE_HTTP_PORTS) but not 80. App Service is told the port via WEBSITES_PORT=8080.
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "BudgetApp.dll"]
