# ATM Cash Management Backend

ASP.NET Core 10 REST API for per-ATM cassette configuration, cash loading and deposits, lifecycle control, balance reporting, reconciliations, and reserved withdrawal quote/confirm flows. PostgreSQL is accessed through EF Core Code First. Pending migrations are applied automatically when the API starts.

## Requirements

- .NET SDK 10.0.302 or a compatible .NET 10 SDK
- Docker Desktop with Docker Compose v2

## Run with Docker Compose

From the repository root:

```powershell
$env:CARD_NUMBER_HMAC_KEY = "<stable secret with at least 32 bytes>"
docker compose up --build -d
```

PostgreSQL must pass its readiness check before the API starts. The API listens on `http://localhost:8080`.

- Health check: <http://localhost:8080/health>
- Swagger UI (Development): <http://localhost:8080/swagger>
- OpenAPI document: <http://localhost:8080/openapi/v1.json>

Stop the containers with `docker compose down`. The named `postgres-data` volume keeps the local database between runs. Use `docker compose down --volumes` only when you intentionally want to delete that database.

The default PostgreSQL credentials in `compose.yaml` and `appsettings.json` are for local development only. Replace them with managed secrets before any shared or production deployment. Compose values can be overridden with `POSTGRES_DB`, `POSTGRES_USER`, and `POSTGRES_PASSWORD` environment variables.

`CARD_NUMBER_HMAC_KEY` is required and must remain stable for the database's lifetime. It is used to create a deterministic HMAC-SHA256 card identifier; the key and original card number are never stored in the database. Supply the key through the environment or a secret manager and do not commit it.

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
| `POST` | `/api/atms/{id}/deposits` | Accept recognized customer notes while the ATM is in service. |
| `POST` | `/api/atms/{id}/reconciliations` | Record a complete physical cassette snapshot in maintenance. |
| `POST` | `/api/atms/{id}/withdrawal-quotes` | Reserve eligible recycling notes and create a 60-second withdrawal quote. |
| `POST` | `/api/withdrawal-quotes/{quoteId}/confirm` | Confirm one quote option; requires the `Idempotency-Key` header. |
| `POST` | `/api/withdrawal-quotes/{quoteId}/cancel` | Cancel an active quote and release its reservations. |

### Withdrawal quote and confirm (A9, A10, B4–B6)

Create a quote with `{ "cardNumber": "4111111111111111", "currency": "TRY", "requestedAmount": 280 }`. Only `TRY`, `USD`, and `EUR` are accepted. The ATM must be `IN_SERVICE`. The response includes a `quoteId`, `expiresAt` in UTC, quote status, and exact or payable alternative options. Each option identifies its amount, note count, denominations, and cassette allocations. `INoteSelectionService` provides the B3 minimum-note plan and cassette allocation; only recycling cassettes and notes not already reserved are eligible. If exact payment is impossible, lower and upper options are offered when payable, with an upper option omitted when it exceeds the remaining daily limit. A ProblemDetails response explains when no option can be paid and reserved.

The quote is reserved atomically when created. If two options are offered, the reservation for each cassette/currency/denomination is the maximum allocation required by either option, so overlapping notes are reserved once. The per-option allocations remain distinct. Confirming one option consumes its allocated notes and releases any excess reservation. A quote can be confirmed once; a cancelled or expired quote cannot be confirmed. Cancellation is safe to repeat, while a confirmed quote cannot be cancelled.

Confirm with `{ "selectedOptionId": "<option-guid>" }` and an `Idempotency-Key` header. Repeating the same key and request returns the stored successful result without another withdrawal; reusing the key for a different quote or option returns `409 Conflict`. A different key cannot withdraw from an already confirmed quote. Quote creation, reservation, confirmation, cancellation, and expiration update the quote, balances, daily limit usage, withdrawal, idempotency result, and cassette movements in database transactions.

Quotes expire after 60 seconds. A hosted worker releases overdue reservations, and quote API operations also check expiration while holding the same database locks, so worker delay does not allow expired notes to be confirmed or re-offered. Expiration is capped at midnight in the `Europe/Istanbul` calendar, and a new quote is required on the next local date. Each card has a shared daily limit across ATMs and per currency: TRY 20,000, USD 1,000, EUR 1,000. Confirmed withdrawals plus the maximum payable option reserved by each active quote count against the limit; the alternatives are not added together. Cancel and expiration release the reserved limit. The day boundary uses the Europe/Istanbul calendar, including daylight-saving rules from the host time-zone database.

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

Central, testable state rules expose checks for customer cash operations (allowed only in `IN_SERVICE`), loading (initially in `CONFIGURED`, later in `MAINTENANCE`), configuration (initially in `UNCONFIGURED`, later in `MAINTENANCE`), and reconciliation (in `MAINTENANCE`). Deposits are accepted only in `IN_SERVICE`; physical reconciliation is accepted only in `MAINTENANCE`.

### Deposits (A11, B7)

`POST /api/atms/{id}/deposits` accepts note counts grouped by currency and denomination, plus a count of unrecognized notes. The request must contain at least one recognized or unrecognized note. Recognized notes are routed to matching recycling cassettes in ascending cassette slot order. If no recycling cassette matches, or matching cassettes run out of room, the remaining notes go to the deposit-only cassette. Its shared capacity is calculated from the physical count of every currency and denomination it contains. Notes that still do not fit are returned with currency, denomination, and count in `capacityReturns`; unrecognized notes are returned separately and are never stored. The order across different note types is deterministic: currency enum order (`TRY`, `USD`, `EUR`), then denomination ascending. This ordering only determines which types get any remaining shared deposit-only capacity; it does not use the withdrawal fill-ratio preference.

The response identifies the deposit transaction, accepted currency/denomination totals, each cassette placement, capacity returns, unrecognized return count, and accepted value totals by currency. Only placed notes increase physical balances. Deposit-only balances remain excluded from withdrawal stock because B3 selects notes from recycling cassettes only. The ATM row is locked with `SELECT ... FOR UPDATE` in a database transaction before capacities are read, so concurrent operations on one ATM serialize. The deposit header, note-level accepted/returned counts, balance changes, and `DEPOSIT` movements commit together. A fully returned but otherwise valid deposit is still recorded with zero accepted placements.

```json
{
  "notes": [
    { "currency": "TRY", "denomination": "50", "count": 3 },
    { "currency": "EUR", "denomination": "100", "count": 2 }
  ],
  "unrecognizedNoteCount": 1
}
```

### Physical reconciliation (A8)

`POST /api/atms/{id}/reconciliations` requires a full snapshot containing cassette slots 1 through 5, and is available only in `MAINTENANCE`. Each cassette has a `notes` array of physical currency/denomination counts. A note type omitted from an included cassette's array is explicitly counted as zero. Deposit-only cassettes may contain mixed note types; recycling cassettes accept only their configured type. Negative counts, duplicate note types, missing cassette slots, and totals above cassette capacity are rejected before any write. Reconciliation is maintenance-only: initial setup already captures the physical stock through the mandatory five-cassette initial load, while the project has no separate reconciliation lifecycle before that load.

The system compares each counted type with its current physical count (an absent existing or counted type is treated as zero), stores expected, counted, and signed difference counts in a reconciliation snapshot, then sets physical balances to the counted values. Each non-zero difference creates one `RECONCILIATION_ADJUSTMENT` movement; a repeated unchanged snapshot records its evidence but creates no adjustment movement. Existing reserved counts are never changed. Reconciliation is rejected while any cassette has reserved notes, avoiding conflicts with active withdrawal quotes. The snapshot header and lines retain an audit record that can be extended with B9 approval state later; this phase does not implement approval or user identity.

The response includes expected and counted counts per cassette/note type, count differences, currency-specific value differences, and a summary of changed cassettes. The request shape is:

```json
{
  "cassettes": [
    { "cassetteNumber": 1, "notes": [] },
    { "cassetteNumber": 2, "notes": [{ "currency": "TRY", "denomination": "100", "count": 9 }] },
    { "cassetteNumber": 3, "notes": [] },
    { "cassetteNumber": 4, "notes": [{ "currency": "USD", "denomination": "20", "count": 30 }] },
    { "cassetteNumber": 5, "notes": [] }
  ]
}
```

### Optimal note selection (B3)

`INoteSelectionService` accepts a requested currency and amount, cassette stock snapshots, and an optional remaining daily limit. It uses only matching `RECYCLING` cassettes. Available notes are `PhysicalCount - ReservedCount`; deposit-only stock and notes reserved by another operation are excluded. The service returns an exact option when possible, otherwise the closest payable lower and upper amounts with their minimum-note allocations. Options above the supplied limit are omitted. The service calculates plans; the withdrawal application service reserves the resulting allocations transactionally.

The selector uses bounded dynamic programming grouped by denomination, not a greedy largest-note-first rule. For each denomination, a sliding-window transition considers only the available count and records the minimum note count for each reachable total. Processing denominations from largest to smallest gives deterministic denomination tie-breaking; the bounded transition takes fewer notes of the current denomination when note counts tie. It reconstructs choices using per-denomination decision arrays. With at most four recycling cassettes, 100 notes per cassette, and denominations up to 500, the amount range is at most 200,000; time is O(D × M) and reconstruction storage is O(D × M), where D ≤ 4 denomination groups and M is the maximum payable total.

When a denomination exists in more than one recycling cassette, the chosen total count for that denomination is allocated after optimization. Cassettes are ordered by physical fill ratio (`PhysicalCount / Capacity`) from highest to lowest, then by cassette slot number ascending. Reserved notes reduce the available count but do not change the physical fill ratio. This allocation rule preserves the minimum-note result while preferring the fuller cassette for equal denominations.

## Architecture and data model

The project is a modular monolith in one Web API project. `Domain` contains entities, enums, and lifecycle rules; `Application` contains request contracts, validation, and ATM operations; `Infrastructure` contains EF Core persistence, migration startup, API exception handling, and the database health check.

The schema contains:

- `Atm`: unique code, lifecycle state, creation time, and an initial-load completion timestamp.
- `Cassette`: one physical slot number per ATM, type, capacity, and optional recycling currency and denomination.
- `CassetteBalance`: physical and reserved counts by cassette, currency, and denomination. Available count is physical minus reserved.
- `CassetteMovement`: append-only movement records with ATM/cassette, type, currency, denomination, physical and reserved count deltas, timestamp, transaction reference, and an initial-load marker.
- `CashDeposit` and `CashDepositNote`: deposit transaction time, unrecognized return count, and requested/accepted/returned note counts by currency and denomination.
- `CashReconciliation` and `CashReconciliationLine`: timestamped full snapshot evidence by cassette/note type, including expected, counted, and difference counts.
- `WithdrawalQuote`, `WithdrawalQuoteOption`, `WithdrawalQuoteOptionAllocation`, and `QuoteReservation`: expiring quote state, individual selectable plans, and the combined cassette-level reservation.
- `CashWithdrawal`, `CardDailyLimitUsage`, and `ConfirmIdempotencyRecord`: confirmed withdrawal facts, card/currency/day limit usage, and durable successful confirm replay results. Card identifiers are HMAC-SHA256 values.

The movement model supports `LOAD`, `UNLOAD`, `WITHDRAWAL`, `DEPOSIT`, `RESERVATION`, `RESERVATION_RELEASE`, and `RECONCILIATION_ADJUSTMENT`. Withdrawal quote creation writes `RESERVATION`; confirmation writes `WITHDRAWAL` for physical and reserved counts consumed and `RESERVATION_RELEASE` for unused reserved notes. Cancellation and expiration write `RESERVATION_RELEASE`. References point to the quote or withdrawal transaction. This phase does not add an unload operation endpoint.

### Movement ledger and consistency (B8)

Movement deltas use a single sign convention:

| Type | Physical delta | Reserved delta |
| --- | --- | --- |
| `LOAD`, `DEPOSIT` | Positive | Zero |
| `RESERVATION` | Zero | Positive |
| `RESERVATION_RELEASE` | Zero | Negative |
| `WITHDRAWAL` | Negative | Negative for reserved notes consumed (zero is permitted for an unreserved withdrawal) |
| `RECONCILIATION_ADJUSTMENT` | Positive or negative | Zero |
| `UNLOAD` | Negative | Zero |

Quote confirmation reduces physical and reserved counts together for dispensed notes. A separate `RESERVATION_RELEASE` records only unused reserved notes, so the physical amount is never counted twice. Constructor validation and a PostgreSQL check constraint enforce the type/delta signs. A unique database index on transaction reference, cassette, movement type, currency, and denomination prevents a transaction from recording the same cassette/note event twice.

`GET /api/atms/{id}/ledger/consistency` compares every cassette balance row with grouped sums of its movement deltas. It examines the union of balance and ledger note types, reports both sides when a row is missing, checks `available = physical - reserved`, and flags negative or impossible totals. Physical, reserved, and available differences are calculated as database count minus ledger count. It runs as a read-only PostgreSQL `REPEATABLE READ` transaction. The endpoint returns HTTP 200 with `isConsistent: false` and a list of differences when it finds a mismatch; an unknown ATM returns 404 ProblemDetails. It never repairs balances.

EF Core rejects updates and deletes of tracked `CassetteMovement` rows. There is no database trigger preventing a privileged direct SQL client from modifying the ledger; database administrators remain able to bypass the application-level append-only guard.

## Data integrity and transaction decisions

- The database constrains slot numbers to 1 through 5 and has a unique `(AtmId, Number)` index. Together these ensure an ATM cannot have more than five slots or duplicate a physical slot. The application requires exactly five distinct slots, one deposit-only and four recycling cassettes, before saving configuration. This exact-count check is not implemented as a row `CHECK` constraint because such a constraint cannot count related cassette rows.
- Configuration, loading, state changes, deposits, reconciliations, and withdrawal operations lock the ATM row with `SELECT ... FOR UPDATE` inside a database transaction. Withdrawal operations acquire locks in a consistent order: daily-limit rows (sorted), ATM, then quote. This serializes cassette use and cancel/confirm/expiration for the same ATM/quote. Daily-limit rows are keyed by HMAC card identifier, currency, and Istanbul business date, and locked in PostgreSQL across ATM instances.
- Idempotency keys have a database unique constraint and a PostgreSQL transaction advisory lock during replay/creation. The idempotency request identity, successful response, quote state, withdrawal, balances, daily-limit transition, and ledger rows commit atomically.
- Quote expiration uses persisted UTC timestamps and an injectable clock in tests. The expiration worker is repeatable: it locks and rechecks each active quote before releasing reservations, so competing workers and API operations cannot double-release.
- PostgreSQL ledger consistency reads use one repeatable-read snapshot so a balance update and its movement are observed together, even while other ATM operations commit.
- For daily limits, an active quote reserves the maximum value of its offered options once. Confirmation moves the selected value from reserved usage to confirmed usage in the same transaction, avoiding a temporary double count.
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

The xUnit suite includes domain-rule, note-selection, and deposit-routing unit tests, plus HTTP integration tests against a real PostgreSQL database. Each integration fixture creates an isolated temporary schema and removes only that schema after the tests. Set `ATM_TEST_CONNECTION_STRING` to a dedicated PostgreSQL test database before running:

```powershell
$env:ATM_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=atm_cash_test;Username=atm_app;Password=dev-only-change-me;GSS Encryption Mode=Disable"
dotnet test AtmCashManagement.sln --no-restore --maxcpucount:1
```

The unit tests include the B3 acceptance scenarios and additional cases for empty and wrong-currency stock, deposit-only exclusion, reservations, repeated denominations, cassette fill priority, capacity validation, upper-limit filtering, determinism, and non-mutation; deposit routing tests cover matched and unmatched notes, duplicate recycling slots, overflow, capacity returns, mixed currencies, and invalid inputs. PostgreSQL integration tests cover exact and alternative quotes, union reservations, release/consumption, idempotency, expiration, all currency limits, HMAC storage, ATM state validation, movement rows, and the B6 concurrency scenarios. The independent `AtmPostgreSqlConcurrencyTests` class starts HTTP operations behind a shared task barrier; each request is handled by its own scoped `DbContext` and PostgreSQL transaction. It covers last-note contention, multi-request stock contention, confirm replay, confirm/cancel races, and a shared daily limit across two ATMs. `CassetteLedgerConsistencyTests` verifies ledger reconstruction through load, deposit, reservation, release, withdrawal, expiration, reconciliation, empty cassettes, mixed deposit-only note types, immutable event keys, and a deliberately corrupted balance in the fixture's isolated schema.

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

- Phase 6.1 was validated on Windows where `docker.exe` was not available on `PATH` or in the standard Docker Desktop CLI path. The installed standalone Compose plugin (`docker-compose.exe` v5.5.1) was used against Docker Engine 29.8.2: the API image was rebuilt, the existing PostgreSQL container and volume were retained, the Phase 6 migration was applied to the existing `atm_cash` database, and health plus HTTP quote/confirm/ledger-consistency checks passed. The `docker version` CLI command itself could not be run.
- B9 four-eyes/X-User approval and TRY 1,000 threshold, and B10 general TRY conversion and advanced operational reports/alerts/outbox are not implemented. Authentication and authorization remain out of scope.
- Rotating `CARD_NUMBER_HMAC_KEY` changes card identifiers and therefore starts new limit histories; production key rotation needs an explicit migration strategy. A time-zone database with `Europe/Istanbul` (or Windows `Turkey Standard Time`) must be available to the API runtime.
- Ledger immutability is enforced by the EF Core save path and a unique event index, but not by a PostgreSQL trigger; direct SQL with database privileges can bypass the EF guard. No unload operation endpoint is implemented in this phase.
- Authentication and authorization are outside the current scope. Local Compose credentials are development-only.
- The initial load must cover all five slots in one request. No endpoint pre-creates empty slots; configuration creates each physical slot once, protected by the ATM/slot uniqueness constraint.

## AI tools

Codex was used to scaffold and implement the API, EF Core model and migration, Docker workflow, tests, and README. The generated work was checked with builds, xUnit tests against PostgreSQL, and direct HTTP calls to the Docker-hosted API.
