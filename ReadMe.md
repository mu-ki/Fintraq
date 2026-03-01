# Fintraq (ExpenseManager)

Personal finance tracker built with ASP.NET Core MVC, EF Core, and SQLite.

![Dashboard](ExpenseManager/Images/screencapture-localhost-7153-Dashboard-2026-02-28-03_34_08.png)

## Live Demo

- Site: http://fintraq.runasp.net/
- Demo user: `demo@expensemanager.local`
- Demo password: `Demo@12345`

## Stack

- .NET 10, ASP.NET Core MVC, Razor Views
- ASP.NET Core Identity (cookie auth)
- Entity Framework Core 10 + SQLite

## Features

- Register, login, logout
- One-time and recurring income/expense entries
- Monthly dashboard: income, expense, net, savings
- AI Chat (authenticated): ask month-wise balance, income, and expense questions
- Due items list with mark done/revert (recurring + one-time)
- Category totals and account balance trend
- Bank accounts with calculated mode or manual override mode
- Soft delete for transactions and bank accounts
- Seeded demo account + sample data

## Important Notes

- Categories are global/shared across users.
- Expense entries require `Paid From Account`.
- Income entries can optionally set `Received To Account`.
- Recurring completion can store a month-specific amount for dashboard totals.

## Run Locally

### Prerequisites

- .NET 10 SDK

### Start

```bash
dotnet run --project ExpenseManager
```

## Database

Runtime SQLite file:

`<repo>/database/app.db`

On startup, the app applies migrations and seeds:

- default categories
- demo user
- demo financial data (if missing)

## AI Setup (Gemini)

Set Gemini API key using user secrets:

```bash
dotnet user-secrets --project ExpenseManager set "Gemini:ApiKey" "<your-gemini-api-key>"
```

Then open `AI Chat` in the app navigation after login.

## Project Layout

```text
ExpenseManager/
├── Areas/Identity/
├── Controllers/
├── Data/
├── Models/
├── Services/
├── Views/
├── wwwroot/
└── Program.cs
```

## Screens

- Login: ![Login](ExpenseManager/Images/screencapture-localhost-7153-Identity-Account-Login-2026-02-28-03_35_11.png)
- Transactions: ![Transactions](ExpenseManager/Images/screencapture-localhost-7153-Transactions-2026-02-28-03_34_25.png)
- Bank Accounts: ![Bank Accounts](ExpenseManager/Images/screencapture-localhost-7153-BankAccounts-2026-02-28-03_34_33.png)
- Categories: ![Categories](ExpenseManager/Images/screencapture-localhost-7153-Categories-2026-02-28-03_34_40.png)
