# Builds Drover from source. Nothing here is published as an image (docs/adr/0009).
# Base images are pinned by digest; dependabot proposes new digests.

FROM mcr.microsoft.com/dotnet/sdk:11.0@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS build
WORKDIR /src

# The native client's browser build is served beside the web client under /native/ (docs/adr/0015). Building it
# needs the WebAssembly workload and Python for its native linker, in this stage only, and adds time and memory.
# Leave it out with: docker build --build-arg DROVER_NATIVE_CLIENT=false .
ARG DROVER_NATIVE_CLIENT=true

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
COPY src/Drover.Adapters.E2B/Drover.Adapters.E2B.csproj src/Drover.Adapters.E2B/
COPY src/Drover.Adapters.DataProtection/Drover.Adapters.DataProtection.csproj src/Drover.Adapters.DataProtection/
COPY src/Drover.Adapters.LocalFiles/Drover.Adapters.LocalFiles.csproj src/Drover.Adapters.LocalFiles/
COPY src/Drover.Adapters.Mcp/Drover.Adapters.Mcp.csproj src/Drover.Adapters.Mcp/
COPY src/Drover.Adapters.OpenAICompatible/Drover.Adapters.OpenAICompatible.csproj src/Drover.Adapters.OpenAICompatible/
COPY src/Drover.Adapters.Venice/Drover.Adapters.Venice.csproj src/Drover.Adapters.Venice/
COPY src/Drover.Adapters.WebPush/Drover.Adapters.WebPush.csproj src/Drover.Adapters.WebPush/
COPY src/Drover.Client/Drover.Client.csproj src/Drover.Client/
COPY src/Drover.Presentation/Drover.Presentation.csproj src/Drover.Presentation/
COPY src/Drover.Ui/Drover.Ui.csproj src/Drover.Ui/
COPY src/Drover.Web/Drover.Web.csproj src/Drover.Web/
COPY src/Drover.Server/Drover.Server.csproj src/Drover.Server/
RUN dotnet restore src/Drover.Server/Drover.Server.csproj

# Only the browser target of the native client is restored and built here: the image needs no phone or desktop app.
COPY src/Drover.Native/Drover.Native.csproj src/Drover.Native/
SHELL ["/bin/bash", "-o", "pipefail", "-c"]
# hadolint ignore=DL3008
RUN if [[ "$DROVER_NATIVE_CLIENT" == true ]]; then \
        apt-get update \
        && apt-get install -y --no-install-recommends python3 \
        && rm -rf /var/lib/apt/lists/* \
        && dotnet workload install wasm-tools \
        && dotnet restore src/Drover.Native/Drover.Native.csproj -p:DroverNativeTargets=net11.0-browserwasm; \
    fi

# Publishing the server brings the web client with it. Only the server's project graph is built:
# no tests, no app host, no Android shell.
COPY BannedSymbols.txt .editorconfig ./
COPY tools tools
COPY src src
RUN dotnet publish src/Drover.Server/Drover.Server.csproj \
    --no-restore -c Release -o /app/publish \
    -maxcpucount:1 -p:UseSharedCompilation=false -p:BuildInParallel=false

# The server serves the native client from the folder "native" beside it (NativeClient:Path).
RUN if [[ "$DROVER_NATIVE_CLIENT" == true ]]; then \
        dotnet publish src/Drover.Native/Drover.Native.csproj \
            --no-restore -c Release -f net11.0-browserwasm -o /src/native-publish -p:DroverNativeTargets=net11.0-browserwasm \
            -maxcpucount:1 -p:UseSharedCompilation=false -p:BuildInParallel=false \
        && cp -r /src/native-publish/wwwroot /app/publish/native; \
    fi

FROM mcr.microsoft.com/dotnet/aspnet:11.0@sha256:bc0d6653979efba491ab3d6f589d691f96c309c3c3ef81325efa6978e7b7f242 AS runtime
WORKDIR /app

# The volumes for the key ring and files are created here so they start out writable by the non-root user.
RUN mkdir -p /var/lib/drover/keys /var/lib/drover/files \
    && chown -R "$APP_UID" /var/lib/drover

COPY --from=build /app/publish .

USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "Drover.Server.dll"]
