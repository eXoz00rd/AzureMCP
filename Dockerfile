# The build stage runs natively on the build machine and cross-compiles for each target architecture.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
ARG VERSION
WORKDIR /source
COPY . .
RUN dotnet publish src/AzureDevOpsServer.Mcp \
        --configuration Release \
        --arch "$TARGETARCH" \
        --no-self-contained \
        -p:PackAsTool=false \
        ${VERSION:+-p:Version=$VERSION} \
        --output /app

# Chiseled: no shell or package manager, non-root by default, and nothing to patch beyond the .NET runtime.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
# The base image's ASPNETCORE_HTTP_PORTS would compete with ADOS_HTTP_URL and log a warning at every start.
ENV ADOS_TRANSPORT=http \
    ADOS_HTTP_URL=http://+:8080 \
    ASPNETCORE_HTTP_PORTS=
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["dotnet", "AzureDevOpsServer.Mcp.dll"]
