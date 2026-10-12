# ATM Cash Management Backend

ASP.NET Core 10 REST API for per-ATM cassette configuration, initial and maintenance cash loading, lifecycle control, balance reporting, and the first stage of the cassette movement ledger. PostgreSQL is accessed through EF Core Code First. Pending migrations are applied automatically when the API starts.

## Requirements

- .NET SDK 10.0.302 or a compatible .NET 10 SDK
- Docker Desktop with Docker Compose v2

## Run with Docker Compose

From the repository root:

```powershell
docker compose up --build -d
```

PostgreSQL must pass its readiness check before the API starts. The API listens on `http://localhost:8080`.

- Health check: <http://localhost:8080/health>
- Swagger UI (Development): <http://localhost:8080/swagger>
- OpenAPI document: <http://localhost:8080/openapi/v1.json>

Stop the containers with `docker compose down`. The named `postgres-data` volume keeps the local database between runs. Use `docker compose down --volumes` only when you intentionally want to delete that database.

The default PostgreSQL credentials in `compose.yaml` and `appsettings.json` are for local development only. Replace them with managed secrets before any shared or production deployment. Compose values can be overridden with `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` environment variables.

## API endpoints

All enum values in JSON are strings. Lifecycle values are `UNCONFIGURED`, `CONFIGURED`, `LOADED`, `IN_SERVICE`, and `MAINTENANCE`; cassette types are `DEPOSIT_ONLY` and `RECYCLING`; denominations are `20`, `50`, `100`, and `500`.

| Method | Endpoint | Purpose |
| --- | --- | --- |
| `POST` | `/api/atms` | Create an ATM in `UNCONFIGURED` state. Body: `{ "code": "ATM-001" }`. |
| `GET` | `/api/atms` | List ATMs with their own cassette and balance data. |
| `GET` | `/api/atms/{id}` | Get one ATM and its cassettes and balances. |
| `POST` | `/api/atms/{id}/configure` | Configure all five cassette slots. |
| `POST` | `/api/atms/{id}/load` | Record the initial or a maintenance cash load. |
| `POST` | `/api/atms/{id}/state` | Request a valid lifecycle transition. Body: `{ "state": "IN_SERVICE" }`. |
| `GET` | `/api/atms/{id}/balances` | Get per-cassette note counts and separate totals for TRY, USD, and EUR. |

### Configure cassettes

Send all five slot definitions in one request. Use one `DEPOSIT_ONLY` cassette with no currency or denomination, and four `RECYCLING` cassettes with exactly one currency and denomination each. One or two recycling cassettes must use USD or EUR. Repeating the same currency and denomination in two recycling slots is supported.

```json
{
  "cassettes": [
    { "number": 1, "type": "DEPOSIT_ONLY", "capacity": 100 },
    { "number": 2, "type": "RECYCLING", "currency": "TRY", "denomination": "100", "capacity": 100 },
    { "number": 3, "type": "RECYCLING", "currency": "TRY", "denomination": "50", "capacity": 100 },
    { "number": 4, "type": "RECYCLING", "currency": "USD", "denomination": "20", "capacity": 100 },
    { "number": 5, "type": "RECYCLING", "currency": "TRY", "denomination": "100", "capacity": 100 }
  ]
}
```

Each cassette can hold at most 100 notes. Configuration is accepted during initial setup or in `MAINTENANCE`. If a requested change would alter a cassette that has physical or reserved cash, the API rejects the whole request. A configuration request updates the existing physical slots; it does not create a second set of cassette rows.

### Record cash loading

The first successful load must include all five cassette numbers. An empty `notes` array explicitly records that a cassette was checked and left empty. At least one banknote must be loaded across the request. Subsequent loads in `MAINTENANCE` may include only the cassettes being topped up. Zero-count note entries are accepted and do not create a balance or movement; negative counts are rejected.

```json
{
  "cassettes": [
    { "cassetteNumber": 1, "notes": [
      { "currency": "USD", "denomination": "20", "count": 2 },
      { "currency": "EUR", "denomination": "100", "count": 1 }
    ] },
    { "cassetteNumber": 2, "notes": [{ "currency": "TRY", "denomination": "100", "count": 10 }] },
    { "cassetteNumber": 3, "notes": [{ "currency": "TRY", "denomination": "50", "count": 20 }] },
    { "cassetteNumber": 4, "notes": [{ "currency": "USD", "denomination": "20", "count": 30 }] },
    { "cassetteNumber": 5, "notes": [] }
  ]
}
```

Recycling cassettes accept only their configured currency and denomination. Deposit-only cassettes may contain multiple currencies and denominations. Capacity is checked against the sum of all physical notes in a cassette. A successful first load records its completion time and moves the ATM from `CONFIGURED` to `LOADED`.

### Lifecycle

The operation endpoints advance initial setup automatically: successful configuration changes `UNCONFIGURED` to `CONFIGURED`, and a successful first load changes `CONFIGURED` to `LOADED`. The state endpoint then supports `LOADED` to `IN_SERVICE`, `IN_SERVICE` to `MAINTENANCE`, and `MAINTENANCE` back to `IN_SERVICE`. It rejects other transitions and verifies the complete configuration and initial-load record before returning an ATM to service.

Central, testable state rules also expose checks for customer cash operations (allowed only in `IN_SERVICE`), loading (initially in `CONFIGURED`, later in `MAINTENANCE`), configuration (initially in `UNCONFIGURED`, later in `MAINTENANCE`), and reconciliation (in `MAINTENANCE`). Withdrawal, deposit, and reconciliation endpoints are not part of this phase.

## Architecture and data model

The project is a modular monolith in one Web API project. `Domain` contains entities, enums, and lifecycle rules; `Application` contains request contracts, validation, and ATM operations; `Infrastructure` contains EF Core persistence, migration startup, API exception handling, and the database health check.

The schema contains:

- `Atm`: unique code, lifecycle state, creation time, and an initial-load completion timestamp.
- `Cassette`: one physical slot number per ATM, type, capacity, and optional recycling currency and denomination.
- `CassetteBalance`: physical and reserved counts by cassette, currency, and denomination. Available count is physical minus reserved.
- `CassetteMovement`: append-only movement records with ATM/cassette, type, currency, denomination, physical and reserved count deltas, timestamp, transaction reference, and an initial-load marker.

The movement model supports `LOAD`, `UNLOAD`, `WITHDRAWAL`, `DEPOSIT`, `RESERVATION`, `RESERVATION_RELEASE`, and `RECONCILIATION_ADJUSTMENT`. This phase writes `LOAD` movements only. There are no movement edit/delete endpoints; other movement workflows and the B8 consistency endpoint are deferred.

## Data integrity and transaction decisions

- The database constrains slot numbers to 1 through 5 and has a unique `(AtmId, Number)` index. Together these ensure an ATM cannot have more than five slots or duplicate a physical slot. The application requires exactly five distinct slots, one deposit-only and four recycling cassettes, before saving configuration. This exact-count check is not implemented as a row `CHECK` constraint because such a constraint cannot count related cassette rows.
- Configuration, loading, and state changes lock the ATM row with `SELECT ... FOR UPDATE` inside a database transaction. This serializes conflicting operations for the same ATM while allowing work on separate ATMs to proceed independently.
- A load request validates every cassette and note entry before changing balances. Physical-count updates, `LOAD` movements, and the initial-load completion marker commit in the same transaction. Invalid requests do not leave partial balance or ledger changes.
- The initial load is considered complete when a single successful request explicitly includes slots 1 through 5, contains at least one banknote overall, and passes all type and capacity checks. Empty `notes` arrays mean those cassettes were checked and left empty. This resolves the assignment's ambiguity about the completion boundary.
- A maintenance reconfiguration is rejected if any changed cassette has physical or reserved notes. This avoids moving cash or reservations to a different cassette configuration without an unload workflow.
- Currency totals are reported separately. The API does not combine TRY, USD, and EUR without an exchange rate.
- Database constraints enforce per-balance counts; the application enforces total cassette occupancy across all note types, which is needed for the multi-currency deposit-only cassette.

## Build and tests

```powershell
dotnet restore AtmCashManagement.sln
dotnet build AtmCashManagement.sln --no-restore --maxcpucount:1
```

The xUnit suite includes domain-rule unit tests and HTTP integration tests against a real PostgreSQL database. The integration fixture creates an isolated temporary schema and removes it after the tests. Set `ATM_TEST_CONNECTION_STRING` to a dedicated test database before running:

```powershell
$env:ATM_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=atm_cash_test;Username=atm_app;Password=dev-only-change-me;GSS Encryption Mode=Disable"
dotnet test AtmCashManagement.sln --no-restore --maxcpucount:1
```

The integration tests cover create/configure/load/state/balance HTTP flows, state and configuration rejection, mixed deposit-only balances, repeated recycling pairs, exact capacity, over-capacity rejection, atomic failed loads, ATM isolation, and the real PostgreSQL movement rows.

## EF Core migrations

```powershell
dotnet tool restore
dotnet ef migrations list --project src/AtmCashManagement.Api --startup-project src/AtmCashManagement.Api
```

Create a later migration with:

```powershell
dotnet ef migrations add MigrationName --project src/AtmCashManagement.Api --startup-project src/AtmCashManagement.Api --output-dir Infrastructure/Persistence/Migrations
```

## Unfinished items and limitations

- Withdrawal quote/confirm, note-selection algorithm, reservations, daily card limits, deposits, reconciliation, approvals, operational reports/alerts, outbox processing, and the B8 ledger consistency endpoint are not implemented.
- The ledger is append-only through the current API surface, but database-level immutability triggers and ledger-based balance reconstruction are deferred to the B8 phase.
- Authentication and authorization are outside the current scope. Local Compose credentials are development-only.
- The initial load must cover all five slots in one request. No endpoint pre-creates empty slots; configuration creates each physical slot once, protected by the ATM/slot uniqueness constraint.

## AI tools

Codex was used to scaffold and implement the API, EF Core model and migration, Docker workflow, tests, and README. The generated work was checked with builds, xUnit tests against PostgreSQL, and direct HTTP calls to the Docker-hosted API.
