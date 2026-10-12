# ATM Cash Management Backend

ASP.NET Core 10 REST API foundation for the ATM cash management assignment. The API uses EF Core Code First with PostgreSQL. The first migration is applied automatically at application startup.

## Requirements

- .NET SDK 10.0.302 or a compatible .NET 10 SDK
- Docker Desktop with Docker Compose v2

## Run with Docker Compose

From the repository root:

```powershell
docker compose up --build
```

The PostgreSQL container must pass its readiness check before the API container starts. The API then applies pending EF Core migrations. The API listens on `http://localhost:8080`.

- Health check: <http://localhost:8080/health>
- Swagger UI (Development): <http://localhost:8080/swagger>
- OpenAPI document: <http://localhost:8080/openapi/v1.json>

Stop the containers with `Ctrl+C`, or run `docker compose down`. The named `postgres-data` volume keeps the local database between runs. Use `docker compose down --volumes` only when you intentionally want to delete that database.

The default PostgreSQL credentials in `compose.yaml` and `appsettings.json` are for local development only. They must be replaced with managed secrets before any shared or production deployment. You can override the Compose values with `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` environment variables.

TLS is expected to terminate at the development server or deployment proxy. The container listens on HTTP port 8080 and does not redirect requests to an HTTPS port that is not exposed by the container.

## Build and tests

```powershell
dotnet restore AtmCashManagement.sln
dotnet build AtmCashManagement.sln --no-restore --maxcpucount:1
```

The integration project includes a real PostgreSQL connectivity check and does not silently skip it. Start PostgreSQL with Compose, then set the test connection string and run the solution tests:

```powershell
docker compose up -d postgres
$env:ATM_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=atm_cash;Username=atm_app;Password=dev-only-change-me;GSS Encryption Mode=Disable"
dotnet test AtmCashManagement.sln --no-build --no-restore --maxcpucount:1
```

The unit test project contains no placeholder tests; business-rule tests will be added with their implementation phases. The integration project includes `Microsoft.AspNetCore.Mvc.Testing` and Npgsql for later API/database scenarios.

## EF Core migrations

Restore the repository-local EF tool and list migrations:

```powershell
dotnet tool restore
dotnet ef migrations list --project src/AtmCashManagement.Api --startup-project src/AtmCashManagement.Api
```

Create a later migration with:

```powershell
dotnet ef migrations add MigrationName --project src/AtmCashManagement.Api --startup-project src/AtmCashManagement.Api --output-dir Infrastructure/Persistence/Migrations
```

To apply migrations manually to the configured database:

```powershell
dotnet ef database update --project src/AtmCashManagement.Api --startup-project src/AtmCashManagement.Api
```

## Architecture and initial data model

The project is a modular monolith in one Web API project. `Domain` contains entities and enums, `Application` is reserved for use cases, and `Infrastructure` contains EF Core persistence and database health checks. Dependency injection is configured in `Infrastructure/DependencyInjection.cs`.

The initial schema contains:

- `Atm`: unique code and lifecycle state.
- `Cassette`: ATM number, cassette type, optional recycling-cassette currency and denomination, and capacity.
- `CassetteBalance`: physical and reserved note counts for one currency/denomination pair. Deposit-only cassettes can therefore hold multiple currencies and denominations.

The database enforces supported currencies and denominations, cassette numbers from 1 through 5, capacities up to 100, nonnegative counts, and reserved counts no greater than physical counts. A unique filtered index permits at most one deposit-only cassette per ATM. Service-level checks for exactly five cassettes and aggregate capacity will be added with cassette configuration operations.

Enum JSON values match the assignment for ATM lifecycle states (`UNCONFIGURED`, `CONFIGURED`, `LOADED`, `IN_SERVICE`, `MAINTENANCE`) and banknote denominations (`20`, `50`, `100`, `500`).

## Assumptions and current limitations

- New ATMs start in `Unconfigured` state.
- Cassette setup and cash operations are not implemented in this foundation phase; no cassettes are seeded automatically.
- The test projects are scaffolding only. B3 unit tests and real PostgreSQL integration tests belong to later phases.
- The API applies migrations on startup. In Compose, `depends_on: service_healthy` keeps it from attempting this before PostgreSQL is ready.
- Docker Desktop was not available in the current development environment when this phase was prepared, so Compose startup and database connectivity have not yet been verified here.

## AI tools

Codex was used to scaffold the solution, persistence model, container configuration, and documentation. Generated weather-forecast sample code was removed, and placeholder tests were not retained as passing tests.
