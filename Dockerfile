# ---------------------------------------------------------------------------
# Stage 1: Build the SolidJS UI
# ---------------------------------------------------------------------------
FROM node:22-alpine AS ui-build
WORKDIR /ui
COPY src/Siem.UI/package.json src/Siem.UI/package-lock.json ./
RUN npm ci
COPY src/Siem.UI/ .
RUN npm run build

# ---------------------------------------------------------------------------
# Stage 2: Build the .NET API
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and build config for layer-cached restore
COPY AgentSIEM.slnx Directory.Build.props Directory.Packages.props ./
COPY src/Siem.Api/Siem.Api.csproj src/Siem.Api/
COPY src/Siem.Rules.Core/Siem.Rules.Core.fsproj src/Siem.Rules.Core/
RUN dotnet restore src/Siem.Api/Siem.Api.csproj

# Copy all source and publish
COPY src/ src/
RUN dotnet publish src/Siem.Api/Siem.Api.csproj -c Release -o /app/publish

# ---------------------------------------------------------------------------
# Stage 3: Runtime image
# ---------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
LABEL org.opencontainers.image.source="https://github.com/alex-serebriiskii/AgentSIEM"
LABEL org.opencontainers.image.description="AgentSIEM - LLM Agent Activity SIEM"

RUN apt-get update && apt-get install -y --no-install-recommends libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .
COPY --from=ui-build /ui/dist ./wwwroot/

EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Siem.Api.dll"]
