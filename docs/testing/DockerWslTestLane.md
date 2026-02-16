# Docker WSL Test Lane

Use Docker Desktop with WSL integration as a fast Linux `net8.0` test lane.

This lane is best for deterministic unit/integration test runs and dependency caching.
Windows-specific runtime checks (for example WPF app launch and Windows-native tools) should still run on Windows.

## Prerequisites

- Docker Desktop installed with WSL integration enabled.
- Run commands from repository root (`/mnt/c/Projects/STIGForge` in WSL).

## Build the runner image

```bash
docker compose -f docker-compose.tests.yml build
```

## Full test commands

- Full unit suite:

```bash
docker compose -f docker-compose.tests.yml run --rm unit-tests
```

  This command excludes one ACL-permission test that is host-permission dependent on Linux:
  `ScanAsync_SkipsInaccessibleSubdirectoryAndContinues`.

- Full integration suite (`net8.0` lane):

```bash
docker compose -f docker-compose.tests.yml run --rm integration-tests
```

## Filtered test commands

- Filtered unit tests:

```bash
docker compose -f docker-compose.tests.yml run --rm unit-tests \
  dotnet test tests/STIGForge.UnitTests/STIGForge.UnitTests.csproj \
  --framework net8.0 \
  --configuration Release \
  --nologo \
  --filter "FullyQualifiedName~ContentPackImporterTests" \
  --results-directory /workspace/.artifacts/test-results \
  --logger "trx;LogFileName=unit-tests-filtered.trx"
```

- Filtered integration tests:

```bash
docker compose -f docker-compose.tests.yml run --rm integration-tests \
  dotnet test tests/STIGForge.IntegrationTests/STIGForge.IntegrationTests.csproj \
  --framework net8.0 \
  --configuration Release \
  --nologo \
  --filter "FullyQualifiedName~E2E" \
  --results-directory /workspace/.artifacts/test-results \
  --logger "trx;LogFileName=integration-tests-filtered.trx"
```

## Artifacts and cache

- TRX results are written to `.artifacts/test-results`.
- NuGet cache is stored in Docker volume `stigforge-nuget`.
- .NET CLI cache is stored in Docker volume `stigforge-dotnet`.

## Notes

- This lane intentionally forces `--framework net8.0`.
- WPF project `src/STIGForge.App/STIGForge.App.csproj` is Windows-only and is not validated by this Linux container lane.
