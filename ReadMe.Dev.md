# Developer README - Fintraq (ExpenseManager)

Implementation-focused notes for the current codebase.

## Architecture

- Single ASP.NET Core MVC app (`ExpenseManager`)
- Razor Views + ASP.NET Core Identity UI
- EF Core (code-first) with SQLite
- Dashboard calculations in `IDashboardService` / `DashboardService`

No multi-project Clean Architecture split is implemented yet.

## Runtime

- Target framework: `net10.0`
- Identity: `AddDefaultIdentity<IdentityUser>` with `RequireConfirmedAccount = false`
- Cookie authentication

## Data Model

### Main tables

- `Categories`
- `BankAccounts`
- `Transactions`
- ASP.NET Identity tables

### Entity highlights

- `Category`: global/shared taxonomy, no `UserId`
- `BankAccount`: `UserId` scoped, manual override supported, soft delete
- `TransactionEntry`: one-time + recurring template + recurring completion records
- `AuditableEntity`: `CreatedAt`, `UpdatedAt`, `IsDeleted`

## Access Rules

- `[Authorize]` on core controllers
- `Transactions` and `BankAccounts` enforce user ownership via `UserId`
- Categories are shared and visible to all users
- Role-based authorization is not implemented

## Startup Seed Flow

At startup (`Program.cs`):

1. migrate database
2. seed categories
3. seed demo user
4. seed demo finance data

Demo credentials:

- `demo@expensemanager.local`
- `Demo@12345`

## Modules

### Transactions

- Create/edit/delete one-time and recurring entries
- Recurring update-future versioning via `RecurrenceGroupId`
- Mark/revert monthly completion for recurring entries
- Mark/revert completion for one-time entries
- Validation includes required category, required account for expenses, date rules

### Dashboard

- Builds month model from one-time + due recurring items
- Supports weekly/monthly/quarterly/4-month/half-yearly/yearly due logic
- Uses recurring completion override amount for monthly totals
- Computes KPIs, category totals, due list, completion stats

### Bank Accounts

- Calculated balance mode: initial +/- account-linked transactions to month end
- Manual override mode: returns stored override amount
- Soft delete for account removal

### Categories

- Global CRUD
- Prevents duplicate name per type
- Blocks delete when category is in use

## Database Path and Config

Runtime DB path is set in `Program.cs`:

`Path.Combine(ContentRootPath, "..", "database", "app.db")`

`appsettings.json` contains `ConnectionStrings:DefaultConnection`, but runtime currently uses the hardcoded path above.

## Local Run

```bash
dotnet run --project ExpenseManager
```

Optional helper:

```bat
ExpenseManager\build-run.bat
```

Default local URLs:

- `https://localhost:7153`
- `http://localhost:5201`

## Current Gaps (Intentional/Not Yet Built)

- `/api/chat/query` is now available for authenticated AI finance Q&A
- No JWT-based auth flow
- No separate API/Application/Domain/Infrastructure projects
- No separate `Incomes` / `Expenses` tables (single `Transactions` table is used)

## AI Chat (Gemini)

- Endpoint: `POST /api/chat/query` (authenticated user session required)
- Supported intents (phase 1): `balance`, `income`, `expense`
- If month/year is unclear, API returns a clarification question
- Response format is structured JSON (`reply`, `requiresClarification`, `data` with account breakdown + total)

Configuration:

- `appsettings.json` -> `Gemini:Model`
- API key via user secrets preferred:

```bash
dotnet user-secrets --project ExpenseManager set "Gemini:ApiKey" "<your-gemini-api-key>"
```
