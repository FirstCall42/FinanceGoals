# syntax=docker/dockerfile:1
# The migrator image: runs once per deployment as a Container Apps job and exits. Build from the
# repository root:
#   docker build -f src/FinanceGoals.Migrator/Dockerfile -t financegoals-migrator .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/FinanceGoals.Migrator/ src/FinanceGoals.Migrator/
# The scripts are embedded in the assembly at build time.
COPY db/ db/
# Code style is enforced by CI ("Build and test"). Re-checking it here would fail on a Windows
# working copy, whose CRLF line endings the Linux formatter flags.
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish src/FinanceGoals.Migrator --configuration Release --output /app \
      -p:ContinuousIntegrationBuild=true -p:EnforceCodeStyleInBuild=false

# Chiseled: no shell or package manager, runs as a non-root user.
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled-extra
WORKDIR /app
COPY --from=build /app .
ENTRYPOINT ["dotnet", "FinanceGoals.Migrator.dll"]
