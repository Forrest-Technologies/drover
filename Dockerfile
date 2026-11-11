# Builds Drover from source. Nothing here is published as an image (docs/adr/0009).
# Base images are pinned by digest; dependabot proposes new digests.

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:fe0fd4e93bd2fbb47c883b7a6369e5c8d3db8bf01cdc5981f6ee35fbdf3ff4fb AS build
WORKDIR /src

# Keep peak memory low on a small machine, at the cost of some build time (docs/architecture/13-deployment-and-push.md).
ENV DOTNET_NOLOGO=1 \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_gcServer=0 \
    DOTNET_gcConcurrent=0 \
    MSBUILDDISABLENODEREUSE=1

# Restore is its own layer. It changes only when package versions or project files change.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY tools/Drover.Analyzers/Drover.Analyzers.csproj tools/Drover.Analyzers/
COPY tools/Drover.Analyzers.CodeFixes/Drover.Analyzers.CodeFixes.csproj tools/Drover.Analyzers.CodeFixes/
COPY src/Drover.Contracts/Drover.Contracts.csproj src/Drover.Contracts/
COPY src/Drover.Domain/Drover.Domain.csproj src/Drover.Domain/
COPY src/Drover.Data/Drover.Data.csproj src/Drover.Data/
COPY src/Drover.Engine/Drover.Engine.csproj src/Drover.Engine/
COPY src/Drover.Agents/Drover.Agents.csproj src/Drover.Agents/
COPY src/Drover.Adapters.DataProtection/Drover.Adapters.DataProtection.csproj src/Drover.Adapters.DataProtection/
COPY src/Drover.Adapters.LocalFiles/Drover.Adapters.LocalFiles.csproj src/Drover.Adapters.LocalFiles/
COPY src/Drover.Adapters.Mcp/Drover.Adapters.Mcp.csproj src/Drover.Adapters.Mcp/
COPY src/Drover.Adapters.OpenAICompatible/Drover.Adapters.OpenAICompatible.csproj src/Drover.Adapters.OpenAICompatible/
COPY src/Drover.Adapters.Venice/Drover.Adapters.Venice.csproj src/Drover.Adapters.Venice/
COPY src/Drover.Client/Drover.Client.csproj src/Drover.Client/
COPY src/Drover.Ui/Drover.Ui.csproj src/Drover.Ui/
COPY src/Drover.Web/Drover.Web.csproj src/Drover.Web/
COPY src/Drover.Server/Drover.Server.csproj src/Drover.Server/
RUN dotnet restore src/Drover.Server/Drover.Server.csproj

# Publishing the server brings the web client with it. Only the server's project graph is built:
# no tests, no app host, no native shell.
COPY BannedSymbols.txt .editorconfig ./
COPY tools tools
COPY src src
RUN dotnet publish src/Drover.Server/Drover.Server.csproj \
    --no-restore -c Release -o /app/publish \
    -maxcpucount:1 -p:UseSharedCompilation=false -p:BuildInParallel=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e AS runtime
WORKDIR /app

# The volumes for the key ring and files are created here so they start out writable by the non-root user.
RUN mkdir -p /var/lib/drover/keys /var/lib/drover/files \
    && chown -R "$APP_UID" /var/lib/drover

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Drover.Server.dll"]
