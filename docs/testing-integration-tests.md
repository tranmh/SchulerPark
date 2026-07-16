# Integration tests (real PostgreSQL via Testcontainers)

Most backend tests run against the EF Core **InMemory** provider — fast, no Docker.
But InMemory is not a real database: it **ignores** unique/filtered indexes, foreign-key
delete rules (`Restrict`), transactions, and `xmin` concurrency. Bugs that live in those
DB features (e.g. #9, #18, #19) cannot be proven on InMemory — a test would pass whether
or not the bug is present.

For those, we use **Testcontainers**: a throwaway real PostgreSQL started in a Docker
container for the duration of the test run, with the real EF migrations applied.

## Requirements

- **Docker Desktop running.** The container image is `postgres:16-alpine` (pulled once, then cached).
- No other setup — the `PostgresFixture` starts and disposes the container automatically.

## How the tests are gated

Every DB-integrity test class is tagged:

```csharp
[Collection("Postgres")]           // shares one container across the class
[Trait("Category", "Integration")] // lets you include/exclude these tests
public class DbIntegrityTests { ... }
```

and each test method is a `[SkippableFact]` that first calls:

```csharp
Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);
```

`PostgresFixture.InitializeAsync` tries to start the container inside a `try/catch`.
If Docker is unavailable the catch sets `DockerAvailable = false`, and every test is
reported **Skipped** — never **Failed**. So a machine without Docker still gets a green suite.

## Running the tests

All commands run from `backend/`. (`dotnet` is a user-profile install; a fresh terminal
has it on PATH. If not: `$env:PATH = "$env:USERPROFILE\.dotnet;$env:PATH"`.)

| Goal | Command | Docker needed? |
|------|---------|:---:|
| Fast loop — unit/InMemory only | `dotnet test --filter "Category!=Integration"` | No |
| Only the DB-integrity tests | `dotnet test --filter "Category=Integration"` | Yes (else Skipped) |
| Everything | `dotnet test` | Runs DB tests if Docker up; else they Skip |

Expected today: **87 InMemory tests** + **3 integration tests** (#9, #18, #19).
- Docker up → `Passed: 90`.
- Docker down → `Passed: 87, Skipped: 3`.

## Writing a new DB-integrity test

1. Add the class attributes `[Collection("Postgres")]` and `[Trait("Category","Integration")]`.
2. Inject `PostgresFixture` via the constructor.
3. Make the method a `[SkippableFact]` (or `[SkippableTheory]`) and start with
   `Skip.IfNot(_fx.DockerAvailable, _fx.SkipReason);`.
4. Get a context with `_fx.NewContext()`. **Use a separate context to seed vs. to act**
   when testing a database-enforced rule (FK `Restrict`, cascade) — EF client-side
   relationship fixup on tracked entities can otherwise mask the real DB behavior
   (this is exactly why the #18 test seeds in one context and deletes in another).

## CI

GitHub Actions runners include Docker, so the integration tests run for real in CI.
The fast `Category!=Integration` filter is suitable for pre-commit / quick local runs.

## Design-time EF (migrations)

Generating a migration needs the `dotnet-ef` tool and, because dotnet is a user-profile
install, `DOTNET_ROOT` must point at it:

```powershell
$env:DOTNET_ROOT = "$env:USERPROFILE\.dotnet"
$env:PATH = "$env:DOTNET_ROOT;$env:DOTNET_ROOT\tools;$env:PATH"
$env:ASPNETCORE_ENVIRONMENT = "Development"   # so the Api's prod JWT startup-guard doesn't block the design-time host
dotnet-ef migrations add <Name> `
  --project SchulerPark.Infrastructure `
  --startup-project SchulerPark.Api `
  --output-dir Data/Migrations
```
