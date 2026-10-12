# ATM Cash Management Backend

## A. Project Overview

ASP.NET Core 10 REST API for configuring and operating ATM cash cassettes. It covers setup and lifecycle, cash loading, deposits, withdrawal quotes and confirmation, reservations, daily card limits, physical reconciliation, the movement ledger, operations reporting, and low-cash outbox events. PostgreSQL is the source of truth; EF Core Code First migrations run automatically when the API starts.

The implementation follows Parts A and B of [`Project_Assignment_Software_Developer.pdf`](Project_Assignment_Software_Developer.pdf). A line-by-line implementation and verification map is in [`docs/REQUIREMENTS_CHECKLIST.md`](docs/REQUIREMENTS_CHECKLIST.md).

Main API areas:

| Area | Routes |
| --- | --- |
| ATM setup, lifecycle, balances | `/api/atms`, `/api/atms/{id}/configure`, `/load`, `/state`, `/balances` |
| Deposits and physical counts | `/api/atms/{id}/deposits`, `/reconciliations` |
| Reconciliation approval | `/api/atms/{id}/reconciliations/{reconciliationId}/approve` |
| Withdrawal | `/api/atms/{id}/withdrawal-quotes`, `/api/withdrawal-quotes/{quoteId}/confirm`, `/cancel` |
| Ledger and operations reports | `/api/atms/{id}/ledger/consistency`, `/api/reports/operations` |

Enums in JSON are strings. Supported currencies are TRY, USD, EUR; denominations are 20, 50, 100, and 500. Each ATM has five slots: one multi-currency `DEPOSIT_ONLY` cassette and four `RECYCLING` cassettes, each configured for one currency and denomination. Capacity is at most 100 notes per cassette.

## B. How to Run

### Requirements

- .NET SDK 10.0.302 or compatible .NET 10 SDK
- Docker Desktop with a running Docker Engine and Docker Compose
- Node.js/npm only if running Newman from the command line

### Start with Docker Compose

From the repository root:

```powershell
docker compose up --build -d
docker compose ps
```

The API listens on `http://localhost:8080`.

- Health: <http://localhost:8080/health>
- Swagger UI (Development): <http://localhost:8080/swagger>
- OpenAPI: <http://localhost:8080/openapi/v1.json>

The Compose file uses a stable, public development-only HMAC fallback so a new local database can start without secret setup. For an existing database, supply the exact `CARD_NUMBER_HMAC_KEY` previously used. Changing it changes card identifiers and separates their daily-limit history. Production deployments must provide a strong secret through a secret manager and must not use the fallback. The sample PostgreSQL credentials are also for local development only.

`docker compose down` stops the services and keeps the named PostgreSQL volume. Do not remove that volume when it contains data you need.

If `docker.exe` is unavailable but the standalone Compose plugin is installed on Windows, invoke its full path, commonly `%USERPROFILE%\.docker\cli-plugins\docker-compose.exe`:

```powershell
$compose = Join-Path $env:USERPROFILE ".docker\cli-plugins\docker-compose.exe"
& $compose up --build -d
```

### Postman end-to-end flow

Import `postman/AtmCashManagement.postman_collection.json` and `postman/AtmCashManagement.local.postman_environment.json`, select **ATM Cash Management - Local**, and run the full collection in order. Its base URL defaults to `http://localhost:8080`.

The required folder follows the ten business actions: create ATM, configure cassettes, initial load, enter service, request a withdrawal quote, confirm, deposit, enter maintenance, reconcile, and read the operations report. Supporting balance requests capture stock changes and the physical reconciliation snapshot. The additional folder replays idempotent confirmation, checks ledger consistency, tests B9 approval and network totals, then exercises a low-cash reservation and cancellation. Run the whole collection from the beginning because the additional checks use the ATM created by the first folder.

Every run generates synthetic ATM codes, a synthetic numeric test card, and an idempotency key. The collection contains no real card number or HMAC key. It creates and leaves two synthetic ATMs in the database; it does not clear existing data. There is no public outbox inspection route, so event persistence/status is verified with PostgreSQL during backend verification.

With Node.js/npm and registry access, run:

```powershell
npx --yes newman run postman/AtmCashManagement.postman_collection.json --environment postman/AtmCashManagement.local.postman_environment.json --reporters cli
```

The collection was run with Newman against an isolated clean Docker deployment during the final audit: 27 requests and 46 assertions passed. The full environment used generated ATM, quote, and synthetic card values. Detailed results are in `docs/REQUIREMENTS_CHECKLIST.md`.

## C. How to Run Tests

Restore and build:

```powershell
dotnet restore AtmCashManagement.sln
dotnet build AtmCashManagement.sln --no-restore --maxcpucount:1
```

Integration tests require a dedicated real PostgreSQL test database. Set `ATM_TEST_CONNECTION_STRING` to that database, which must allow creation and removal of schemas. Each test fixture creates a uniquely named schema, applies migrations there, and drops only that schema after its tests. Do not point integration tests at a database containing data that must not be modified.

```powershell
$env:ATM_TEST_CONNECTION_STRING = "Host=localhost;Port=5432;Database=atm_cash_test;Username=atm_app;Password=<test-password>;GSS Encryption Mode=Disable"
dotnet test AtmCashManagement.sln --no-restore --maxcpucount:1
```

The suite includes all eight B3 acceptance cases, setup/lifecycle and deposit routing rules, real-PostgreSQL quote/reservation/idempotency/expiration/daily-limit flows, independent connection concurrency races, ledger reconstruction, four-eyes reconciliation, and B10 report/outbox behavior. It does not use mocks as evidence for PostgreSQL locking.

Final hotfix verification on 2026-10-12: 78 unit tests and 74 PostgreSQL integration tests passed (152 total). The integration run used the separate Compose database on host port 35432 and removed only its own temporary test schemas.

The final audit's exact build, test, clean Compose, and Newman results are recorded in `docs/REQUIREMENTS_CHECKLIST.md`.

## D. Architecture and Key Design Decisions

### Structure and persistence

- `Domain`: ATM, cassette, balance, quote, reconciliation, withdrawal, outbox, and movement entities plus lifecycle and movement rules.
- `Application`: scoped use-case services, request/response contracts, bounded note selection, deposit allocation, and testable clock abstraction.
- `Infrastructure`: PostgreSQL/EF Core configuration, migrations, health check, and ProblemDetails exception handling.
- `Controllers`: REST endpoints. Dependency injection registers use cases and two hosted workers.

The main relationships are `Atm → Cassette → CassetteBalance/CassetteMovement`; `WithdrawalQuote → WithdrawalQuoteOption → option allocations`, with a quote-level union reservation; and transaction headers (`CashDeposit`, `CashWithdrawal`, `CashReconciliation`) linked to their detail rows, balances, and movements. Daily limit usage is keyed by HMAC card identifier, currency, and business date. Outbox rows reference their source ATM/cassette and are committed with the triggering balance change.

The API applies pending migrations at startup. The tracked EF Core migrations are under `src/AtmCashManagement.Api/Infrastructure/Persistence/Migrations`.

### Cassette setup and stock selection

Configuration must define slots 1 through 5, exactly one unrestricted `DEPOSIT_ONLY` cassette, and four `RECYCLING` cassettes. One or two recycling cassettes use USD or EUR. A recycling cassette contains only its configured currency and denomination. Cash must be recorded through the initial load before the ATM can enter service. Maintenance reconfiguration is rejected when a changed cassette has physical or reserved cash.

`NoteSelectionService` is a pure bounded dynamic-programming selector. It minimizes the number of notes, returns an exact option when payable, otherwise the nearest lower and upper payable options, and omits options beyond the remaining daily limit. It uses only recycling notes with `available = physical - reserved`. For repeated denominations across cassettes, allocation consumes the cassette with the higher physical fill ratio first, then lower slot number on a tie. The quote service, not the selector, persists reservations.

### Deposits and reconciliation

Recognized deposit notes go first to matching recycling cassettes by slot number, then overflow or unmatched types go to the shared-capacity deposit-only cassette. Unrecognized notes and notes that still do not fit are returned and recorded. Physical reconciliation requires a complete five-cassette snapshot in maintenance; differences create signed `RECONCILIATION_ADJUSTMENT` movements. Reconciliation evidence stores counted values and expected differences.

Differences whose sum of absolute TRY values is greater than TRY 1,000 wait for a second, different operator's approval. Currency conversions use the rate snapshot stored with the reconciliation. `X-User` identifies the operator; authentication and authorization are outside this project.

### Withdrawal quotes, reservations, and limits

Quote creation offers exact or payable lower/upper options. A reservation is created atomically with the quote and its `RESERVATION` movements. If two alternatives are shown, each cassette/currency/denomination reservation is the maximum allocation required by either option, so overlapping notes are reserved once. The option allocations remain separate. Confirm consumes the selected allocation and emits `WITHDRAWAL`; unused reserved notes are released with `RESERVATION_RELEASE`. Cancel and expiration release the complete reservation. Quotes expire after 60 seconds using stored UTC timestamps and a hosted cleanup worker; API operations also check expiry while holding PostgreSQL locks.

Limits are shared across ATMs per keyed card identifier, currency, and Europe/Istanbul business date: TRY 20,000, USD 1,000, EUR 1,000. Confirmed withdrawals plus the maximum offered active-quote option count toward the limit; alternatives are not added together. A quote's limit reservation remains attached to the Istanbul business date on which the quote was created, including when its 60-second validity crosses midnight. Confirmation transfers reserved usage to confirmed usage in that same business-date row in one transaction; a new quote after midnight uses the new day's limit row.

The raw card number is not stored or logged. `CardIdentifierService` computes an HMAC-SHA256 identifier using `CardSecurity:CardNumberHmacKey`, configured through environment/secret configuration and requiring at least 32 UTF-8 bytes. The repository's Compose fallback is only for local development.

### PostgreSQL transactions and concurrency

Balance-changing operations run inside PostgreSQL transactions and lock the ATM row with `SELECT ... FOR UPDATE`. Withdrawal operations use a consistent lock order for daily-limit rows, ATM, and quote; daily-limit rows are shared across ATMs. Confirm idempotency uses a PostgreSQL transaction advisory lock and a database uniqueness constraint. Expiration workers claim and lock quote rows before releasing them; outbox workers use `FOR UPDATE SKIP LOCKED`. These database controls protect requests served by multiple API instances; application-memory locks are not the concurrency boundary.

### Movement ledger and consistency (B8)

Movements record `LOAD`, `UNLOAD`, `WITHDRAWAL`, `DEPOSIT`, `RESERVATION`, `RESERVATION_RELEASE`, and `RECONCILIATION_ADJUSTMENT` deltas. PostgreSQL check constraints enforce movement type and delta semantics. A unique event index prevents duplicate transaction/cassette/type/currency/denomination entries while allowing distinct movement types for one note type in one transaction. `GET /api/atms/{id}/ledger/consistency` compares balances to grouped ledger deltas inside a repeatable-read snapshot and reports mismatches without repairing data.

EF Core rejects movement updates/deletes in the application save path. There is no PostgreSQL trigger preventing a privileged direct SQL client from changing historical rows. The `UNLOAD` movement type is supported; a cash-unload API operation is not implemented.

### Operations report and low-cash outbox (B10)

`GET /api/reports/operations` reports each ATM and cassette, physical/reserved/available counts, totals by currency, and network cash value in TRY. The sample rates (USD 35 TRY, EUR 38 TRY) are fixed assignment values, not live exchange rates. Missing rates produce a ProblemDetails error rather than a partial total.

When a recycling cassette crosses down from at least 10% available stock to below 10%, its operation inserts a low-cash event into the transactional outbox in the same database transaction as stock and ledger changes. Deposit-only cassettes do not alert. A hosted worker claims pending rows, logs events, and marks them sent. Delivery is log-only and at-least-once; no external notification provider is configured. Restoring stock to the threshold allows a later downward crossing to create a new event.

## E. Assumptions

- The first successful load explicitly covers all five slots in one request, contains at least one banknote in total, and records empty slots with empty note arrays. This defines when initial loading is complete.
- Physical reconciliation is a full snapshot of all five cassettes and runs only in maintenance. Existing reservations block reconciliation.
- The business day for a quote's daily-limit reservation is its creation date in Europe/Istanbul. A quote remains valid for exactly 60 seconds from creation, including across local midnight. Confirm, cancel, and expiration settle or release the original business-date reservation; a new quote after midnight uses the new business day's limit.
- Alternatives share one reservation: for each cassette and note type the quote reserves the maximum amount required by either offered option, not the sum. Each option retains its own allocation.
- A reconciliation above the approval threshold uses the sum of absolute TRY-converted note differences, so shortages and surpluses do not net against each other. Approval is required only when the amount is greater than TRY 1,000.
- If an exact withdrawal is impossible, both lower and upper options are offered only when stock and daily limit make them payable.
- Low-cash alerts are emitted on downward threshold crossings. This avoids repeated events during a single low-stock period.
- The seeded TRY conversion rates are fixed demo values. The caller-supplied `X-User` header is an operator identifier, not authenticated identity.

## F. Unfinished Items and Known Limitations

- Authentication and authorization are not implemented. Caller-supplied operator headers are not proof of identity.
- Outbox delivery writes structured application logs only. There is no external notification adapter or public outbox-status endpoint.
- Outbox delivery is at-least-once: a process crash after logging but before the status transaction commits can cause an event to be logged again.
- Movement append-only protection is in the EF Core application path; a privileged database user can bypass it with direct SQL because no immutable-ledger trigger is installed.
- The `UNLOAD` ledger movement type is supported, but no unload endpoint/workflow exists.
- Exchange rates are fixed demo values. HMAC key rotation requires an explicit daily-limit history migration; silently changing the key creates new card identifiers.
- The runtime needs an installed `Europe/Istanbul` (or Windows `Turkey Standard Time`) timezone definition.
- On the final-audit Windows host, `docker.exe` was not found at Docker Desktop's standard CLI location. The installed standalone Compose plugin can communicate with Docker Engine and was used for the isolated clean-start verification.

## G. AI Tools

Codex was used to implement the backend, migrations, tests, Postman collection, and documentation. The implementation and final delivery were checked with the .NET build and test suite, a clean isolated Docker Compose deployment, live HTTP/Newman requests, PostgreSQL migration history, and a source-to-requirement checklist. During Postman verification, an assumed state-response cassette snapshot was replaced with the API's balance read so the later reconciliation request uses an actual physical-count snapshot.
