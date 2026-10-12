# Final Requirements Checklist

Requirement source: [`Project_Assignment_Software_Developer.pdf`](../Project_Assignment_Software_Developer.pdf). Statuses below are based on code inspection and the verification runs recorded in this file. Limitations and assumptions are stated where they qualify a PASS.

## Part A — Assignment Requirements

| ID / short requirement | Status | Implementation: class, method, or endpoint | Automated test / observed evidence | Gap or assumption |
| --- | --- | --- | --- | --- |
| A1 — TRY, USD, EUR | PASS | `Domain/Enums/Currency`; withdrawal, deposit, reporting, daily-limit services | `NoteSelectionServiceTests.All_supported_currencies_can_be_selected`; mixed-currency deposit/report tests | None. |
| A2 — 20, 50, 100, 500 denominations | PASS | `BanknoteDenomination`; cassette validation and note selector | `AtmCoreRulesTests.Configuration_rejects_unsupported_currency_and_denomination_values`; B3 tests | None. |
| A3 — Exactly five cassettes, max 100 notes each | PASS | `CassetteConfigurationValidator.Validate`; `POST /api/atms/{id}/configure` | `AtmApiTests.Configure_rejects_missing_cassettes...`; `Initial_load_accepts_capacity_of_one_hundred_but_rejects_overfill_without_changes` | Configuration requires exactly one deposit-only plus four recycling slots. |
| A4 — Deposit-only accepts mixed cash and is excluded from payout | PASS | `DepositAllocationPlanner`; `NoteSelectionService.Select` filters `RECYCLING` | `NoteSelectionServiceTests.Deposit_only_cassette_cash_is_never_selected`; `AtmDepositReconciliationTests.Deposit_sends_unmatched_banknotes_to_the_deposit_only_cassette` | None. |
| A5 — Recycling cassette contains its configured note type | PASS | `CassetteConfigurationValidator`; `AtmManagementService.LoadCashAsync` | `AtmApiTests.Load_rejects_wrong_recycling_notes_and_failed_multi_cassette_load_is_atomic` | None. |
| A6 — One or two foreign-currency recycling cassettes | PASS | `CassetteConfigurationValidator.Validate` | `AtmCoreRulesTests.Configuration_requires_one_or_two_foreign_currency_cassettes`; configure API validation test | USD/EUR cassette count is one or two. |
| A7 — Configure and record initial load before service | PASS | `AtmManagementService.ConfigureAsync`, `LoadCashAsync`, `SetStateAsync`; `AtmLifecycleRules` | `AtmApiTests.End_to_end_flow_creates_configures_loads_services_and_reports_independent_atms`; lifecycle guard test | Initial load includes all five slots in one request and at least one note overall. |
| A8 — Physical reconciliation records expected/count/difference | PASS | `AtmManagementService.ReconcileAsync`; `POST /api/atms/{id}/reconciliations` | `AtmDepositReconciliationTests.Reconciliation_records_missing_notes_as_negative_adjustment_and_updates_balance`; `CassetteLedgerConsistencyTests.Ledger_matches_balance_after_load_deposit_reserve_release_confirm_expire_and_reconcile` | Reconciliation is a complete five-slot snapshot in maintenance. |
| A9 — Optimal note combination / closest payout | PASS | `NoteSelectionService.Select`; withdrawal quote endpoint | All eight exact B3 acceptance cases below; `AtmWithdrawalTests.Exact_quote_reserves_recycling_notes_without_reducing_physical_stock` | Daily-limit-ineligible upper choices are omitted. |
| A10 — Customer confirms selected withdrawal | PASS | `WithdrawalQuoteService.ConfirmAsync`; `POST /api/withdrawal-quotes/{quoteId}/confirm` | `AtmWithdrawalTests.Confirming_lower_option_consumes_selected_notes_and_releases_surplus_reservation`; ledger consistency tests | Confirmation requires an `Idempotency-Key`. |
| A11 — Deposit routes to recycling then deposit-only; overflow returned | PASS | `DepositAllocationPlanner`; `POST /api/atms/{id}/deposits` | `Deposit_routes_to_recycling_persists_ledger_and_updates_balances`; unmatched, capacity overflow, all-full and unrecognized-note tests | Deterministic routing uses cassette slot order. |
| A12 — Show note counts and currency totals | PASS | `GET /api/atms/{id}/balances`; `OperationsReportService`; `GET /api/reports/operations` | `Operations_report_summarizes_mixed_cassettes_and_network_in_try_without_changing_balances`; Newman report checks | Cross-currency totals require seeded TRY rates. |

### B3 acceptance combinations

All eight named `NoteSelectionServiceTests.B3_acceptance_01` through `_08` passed in the final unit-test run.

| Case | Request and available stock | Expected / observed selection |
| --- | --- | --- |
| 1 | TRY 160; 50×3, 20×10 | Exact: 2×50 + 3×20. |
| 2 | TRY 80; 50×1, 20×2 | Lower 70: 50+20; upper 90: 50+2×20. |
| 3 | TRY 130; 100×5, 20×10 | Lower 120: 100+20; upper 140: 100+2×20. |
| 4 | USD 250; 100×2 | Lower 200: 2×100; no payable upper. |
| 5 | TRY 600; 500×1, 100×0, 20×5 | Exact: 500+5×20. |
| 6 | TRY 200; 100×1, 50×4, 20×5 | Exact minimum: 100+2×50 (three notes). |
| 7 | TRY 1,000; 500×1, 100×3, 20×20 | Exact: 500+3×100+10×20. |
| 8 | TRY 110; 100×3, 50×1, 20×1 | Lower 100; upper 120. |

## Part B — Assignment Requirements

| ID / short requirement | Status | Implementation: class, method, or endpoint | Automated test / observed evidence | Gap or assumption |
| --- | --- | --- | --- | --- |
| B1 — Multiple ATMs | PASS | ATM-scoped entities and `AtmsController` routes | `AtmApiTests.End_to_end_flow_creates_configures_loads_services_and_reports_independent_atms`; Newman created and exercised two ATMs. | None. |
| B2 — Lifecycle and duplicate-note cassette priority | PASS | `AtmLifecycleRules`; `NoteSelectionService.BuildOption` | `AtmApiTests.Lifecycle_and_configuration_are_guarded_by_state_and_existing_cash`; `Higher_physical_fill_ratio_cassette_is_used_first_for_the_same_denomination`; tie-break test | Fill ratio uses physical notes/capacity; lower slot wins ties. |
| B3 — Bounded optimal note selection | PASS | `NoteSelectionService.BuildBoundedDynamicProgram` | Eight B3 cases above plus reserved-stock, tie-break, and daily-limit selector tests | None. |
| B4 — Quote/confirm/cancel, 60-second expiry, idempotency | PASS | `WithdrawalQuoteService`; quote controller; `QuoteExpirationBackgroundService` | Exact 60-second expiry across Istanbul midnight, union reservation, cancel, expire, idempotent replay, key conflict, and confirm/cancel race tests | Expiry is creation time plus 60 seconds; reservation accounting stays on the quote's creation business date. |
| B5 — Daily card limits by currency | PASS | `CardDailyLimitUsage`, HMAC identifier, `WithdrawalQuoteService` | `Daily_limits_are_separate_by_currency_and_confirmed_try_limit_is_enforced`; active reservation, cross-ATM, and cross-midnight date-isolation tests; plaintext check | TRY 20,000; USD/EUR 1,000. Active quotes count their maximum option once against the quote creation date; a quote created after midnight uses the new date. |
| B6 — Real PostgreSQL concurrency | PASS | Transaction row locks, advisory idempotency lock, unique database indexes | `AtmPostgreSqlConcurrencyTests.Parallel_last_note_quotes_allow_only_one_reservation`; parallel quote, confirm/cancel, and cross-ATM card tests | Integration tests ran against PostgreSQL, not a mock provider. |
| B7 — Deposit acceptance, routing, overflow and returns | PASS | `AtmManagementService.DepositAsync`; `DepositAllocationPlanner` | Matching, unmatched, recycling overflow, full deposit cassette, unrecognized notes, concurrent capacity tests | None. |
| B8 — Movement ledger and consistency endpoint | PASS | `CassetteMovement`; `CassetteLedgerConsistencyService`; `GET /api/atms/{id}/ledger/consistency` | Same-cassette `WITHDRAWAL` + `RESERVATION_RELEASE`; reconstruction, mismatch, and duplicate/update/delete tests; Newman returned `isConsistent: true` | EF guards append-only writes; privileged direct SQL can bypass them. `UNLOAD` type exists; no unload endpoint. |
| B9 — Four-eyes reconciliation approval | PASS | `AtmManagementService.ApproveReconciliationAsync`; approval route | `Absolute_net_zero_difference_requires_four_eyes_and_preserves_ledger_through_approval`; concurrent approval test; Newman approval with different user passed | `X-User` is caller-provided; authentication is out of scope. Threshold is absolute TRY difference > 1,000. |
| B10 — Operations report and low-cash outbox | PASS | `OperationsReportService`; `LowCashEventRecorder`; outbox hosted worker | Report, threshold, rollback, retry, `SENT`, and worker concurrency integration tests; clean-stack Postman run created low-cash rows and PostgreSQL showed `Sent` | Notification is logged locally only; at-least-once logging semantics. |

## Technical requirements and delivery

| Requirement | Status | Implementation / evidence | Gap or assumption |
| --- | --- | --- | --- |
| ASP.NET Core REST API, DI | PASS | `Program.cs`, controllers, `DependencyInjection.AddInfrastructure` | None. |
| EF Core Code First and relational PostgreSQL | PASS | EF Core/Npgsql model and seven tracked migrations | Clean database applied all seven at API startup. |
| Docker Compose starts API + DB | PASS | `compose.yaml`; PostgreSQL health-gated API dependency | Clean verification used the standalone Compose plugin because `docker.exe` was absent. Separate project, ports 25432/28080 and volume. |
| Swagger, OpenAPI, ProblemDetails, health | PASS | `Program.cs`, `AtmApiExceptionHandler`, database health check | Clean deployment returned HTTP 200 for `/health`, `/swagger/index.html`, and `/openapi/v1.json`. |
| Unit and PostgreSQL integration tests | PASS | Two xUnit projects; integration fixtures isolate each run in a temporary schema | Final hotfix run: 78 unit + 74 PostgreSQL integration = 152 passed, 0 failed/skipped. |
| Postman required ten-step flow and additional checks | PASS | `postman/AtmCashManagement.postman_collection.json` + local environment | Newman clean-stack run: 27 requests, 46 assertions, 0 failures. Verified replay, B9, report, low-cash and ledger. |
| README sections A–G | PASS | Root `README.md` | Includes assumptions and limitations. |
| Requirements checklist | PASS | This file | Every A/B item mapped to code and verification. |
| Git commit and clean source tree | PASS | Final documentation changes committed after verification; final tree checked clean. | Commit hash is included in the delivery report. |
| Submission ZIP, reopen/integrity check | PASS | `AtmCashManagement_Submission.zip` in parent directory, built from committed HEAD and reopened for inspection. | Exact file list, size, and SHA256 are in the delivery report. |

## Security and data-integrity review

- No production credential, private-key block, cloud access key, or GitHub token was found in tracked files. Password-like values are documented development/test placeholders. The Compose HMAC fallback is public and marked local-development-only; the running development database's key was not read, changed, or replaced.
- Long numeric values in tests are synthetic fixture card numbers; migration numbers are timestamps. The Postman collection creates numeric synthetic values dynamically. No real card number was identified in the repository or test output.
- Card identifiers are HMAC-SHA256 keyed values. `AtmWithdrawalTests.Card_numbers_are_only_stored_as_keyed_identifiers_and_are_not_returned` passed. The API service does not log raw card numbers.
- Tracked files contain no `.env`, local database/volume, log, cache, `bin`, `obj`, or `node_modules` content. The ZIP exclusion check is recorded below.
- Physical, reserved and available counts remain distinct in the entity model and ledger checks. Deposit-only stock is filtered out of withdrawals; PostgreSQL parallel reservation and cross-ATM daily-limit tests passed. Confirm replay is database-idempotent; expiry/cancel/confirm serialize on database locks. Reconciliation approval and outbox writes are transactional and covered by PostgreSQL tests.

## Final audit record

Verification date: 2026-10-12 (Europe/Istanbul).

| Check | Observed result |
| --- | --- |
| `dotnet restore AtmCashManagement.sln` | Passed. |
| `dotnet build AtmCashManagement.sln --no-restore --maxcpucount:1` | Passed; 0 warnings, 0 errors. |
| `dotnet test AtmCashManagement.sln --no-restore --maxcpucount:1` | Passed; 78 unit + 74 integration = 152, 0 failed, 0 skipped. Integration tests used the isolated Docker database on host port 35432. |
| Clean Compose build/start | Passed using standalone `docker-compose.exe` v5.5.1; unique project `atm-cash-b4-hotfix-20261012`, ports 35432/38080, separate named volume. Existing Compose project, `atm_cash` volume, and HMAC key were not targeted. |
| Migration and API startup | Passed; seven migrations applied through `20261012104509_AddOperationsReportAndOutbox`. `/health` returned HTTP 200. |
| Newman | Passed; 27 requests, 27 scripts, 46 assertions, 0 failures. Ledger endpoint returned consistent. |
| Docker executable | `C:\Program Files\Docker\Docker\resources\bin\docker.exe` absent. Standalone Compose plugin was present and able to access Docker Engine. |
| Git scan and delivery package | Passed; committed source tree is clean. The reopened archive contains the tracked project files and excludes build output, `.git`, environment secrets, logs, caches, and temporary test files. Exact ZIP size and SHA256 are in the delivery report. |
