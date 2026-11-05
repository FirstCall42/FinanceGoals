# syntax=docker/dockerfile:1
# The API image. Build from the repository root:
#   docker build -f src/FinanceGoals.Api/Dockerfile -t financegoals-api .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/ src/
# Code style is enforced by CI ("Build and test"). Re-checking it here would fail on a Windows
# working copy, whose CRLF line endings the Linux formatter flags.
RUN --mount=type=cache,target=/root/.nuget/packages \
    dotnet publish src/FinanceGoals.Api --configuration Release --output /app \
      -p:ContinuousIntegrationBuild=true -p:EnforceCodeStyleInBuild=false

# Chiseled: no shell or package manager, runs as a non-root user. "extra" keeps ICU and time zone
# data for culture-aware formatting (NOK, Norwegian dates).
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
# ASP.NET Core images listen on 8080 (infra/modules/container-apps.bicep targets it).
EXPOSE 8080
ENTRYPOINT ["dotnet", "FinanceGoals.Api.dll"]
