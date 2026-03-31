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

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
LABEL org.opencontainers.image.source="https://github.com/alex-serebriiskii/AgentSIEM"
LABEL org.opencontainers.image.description="AgentSIEM - LLM Agent Activity SIEM"
WORKDIR /app
COPY --from=build /app/publish .

EXPOSE 8080
ENV ASPNETCORE_ENVIRONMENT=Production

ENTRYPOINT ["dotnet", "Siem.Api.dll"]
