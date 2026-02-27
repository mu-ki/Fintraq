# Personal Expense & Income Management System

**Project name (solution):** ExpenseManager  
**Technology stack:** ASP.NET Core (Web App) + SQLite + Entity Framework Core  
**Architecture:** Clean Architecture (API + Application + Domain + Infrastructure + UI)  
**Authentication:** ASP.NET Core Identity — Cookie-based (MVC) or JWT (SPA)

---

## 1. Project Overview

### 1.1 Purpose

Build a secure web-based personal finance management system where users can:

- Register and log in securely
- Add and manage **Income** (one-time and recurring)
- Add and manage **Expenses** (one-time and recurring)
- Track recurring payments (chit funds, EMI, subscriptions)
- Manage bank accounts and balances
- View a **Monthly Financial Dashboard**
- Switch between months to track historical data

---

## 2. Functional Requirements

### 2.1 User Authentication

**Features:**

- User registration
- Login / logout
- Forgot password
- Password reset
- Secure session handling
- Role: User (future-ready for Admin)

**Technical:**

- ASP.NET Core Identity
- Password hashing
- JWT (if SPA) or cookie-based auth (if MVC)
- User-based data isolation

---

## 3. Core Modules

### 3.1 Income Management

**Income types:**


| Type      | Examples                                        |
| --------- | ----------------------------------------------- |
| One-time  | Freelance payment, bonus, gift                  |
| Recurring | Salary, rental income, monthly business revenue |


**Income fields:**


| Field       | Type                           |
| ----------- | ------------------------------ |
| Id          | GUID                           |
| UserId      | FK                             |
| Title       | string                         |
| Amount      | decimal                        |
| CategoryId  | FK                             |
| Type        | Enum (OneTime / Recurring)     |
| Frequency   | Enum (Monthly, Weekly, Quarterly, Every4Months, HalfYearly, Yearly) |
| StartDate   | Date                           |
| EndDate     | Date (nullable)                |
| IsActive    | bool                           |
| CreatedDate | DateTime                       |


**Business logic:**

- **One-time:** Recorded only once for a specific date.
- **Recurring:**
  - System auto-generates projection (on-the-fly) per period; support **Monthly**, **Every 4 months**, **Every 6 months** (half-yearly), etc., so only the due months show the amount (e.g. chit every 4 months → Jan, May, Sep).
  - If start date is in the past → include historical due months in dashboard.
  - If end date exists → stop after end date.
  - Editable and pausable.
  - **Mark as completed:** For each projected period, user can mark the recurring item as *completed* (e.g. salary received, payment made) so they can see which projections are fulfilled vs pending.

### 3.2 Expense Management

**Expense types:**


| Type      | Examples                            |
| --------- | ----------------------------------- |
| One-time  | Shopping, medical bill              |
| Recurring | EMI, chit fund, subscriptions, rent |


**Expense fields:**


| Field             | Type                           |
| ----------------- | ------------------------------ |
| Id                | GUID                           |
| UserId            | FK                             |
| Title             | string                         |
| Amount            | decimal                        |
| CategoryId        | FK                             |
| Type              | Enum (OneTime / Recurring)     |
| Frequency         | Enum (Monthly, Weekly, Quarterly, Every4Months, HalfYearly, Yearly) |
| StartDate         | Date                           |
| EndDate           | Date (nullable)                |
| PaidFromAccountId | FK (nullable)                  |
| IsActive          | bool                           |
| CreatedDate       | DateTime                       |


**Recurring expense logic (important):**

- **Monthly:** Example — Chit fund ₹5,000, Start: Jan 2024, End: Dec 2025. If the user adds this in Aug 2025, the system must show all months from Jan 2024 (even when entered late).
- **Non-monthly (e.g. every 4 or 6 months):** Chit funds may be due only in specific months (e.g. every 4 months → Jan, May, Sep; every 6 months → Jan, Jul). The user pays only in that month; until then the amount stays in their account (e.g. salary account). The dashboard must show **bank balances** so users can see how much is available before the payment month.

**Implementation:** Use **on-the-fly calculation** (recommended). Optionally, a background service can generate stored monthly entries for reporting.

**Mark as completed:** Since recurring expenses are auto-projected, users need a way to record that a payment was actually made for a given month. Each projected recurring expense (and optionally income) can be **marked as completed** for a specific month (e.g. "EMI Jan 2024 — paid"). This helps distinguish:

- **Pending:** Projected for the month but not yet paid/received.
- **Completed:** User has confirmed payment/receipt for that month.

### 3.3 Category Management

- **Predefined + custom categories**
- **Income categories:** Salary, Business, Investments, Other
- **Expense categories:** Food, Travel, EMI, Chit Fund, Utilities, Insurance, Shopping
- User can add, edit, and delete custom categories. Every income/expense should have a category (use "Uncategorized" if needed).

### 3.4 Bank Account Management

Users can:

- Add multiple bank accounts
- Set initial balance
- Update balance manually (or derive from linked transactions)
- View monthly balance changes

**Bank account fields:**


| Field          | Type                                  |
| -------------- | ------------------------------------- |
| Id             | GUID                                  |
| UserId         | FK                                    |
| AccountName    | string                                |
| AccountType    | Enum (Savings, Current, Cash, Wallet) |
| InitialBalance | decimal                               |
| CurrentBalance | decimal (calculated or manual)        |
| CreatedDate    | DateTime                              |


**CurrentBalance:** Calculated as `InitialBalance + sum(income to account) - sum(expenses from account)` for the given account, or updated manually. Define one approach per implementation.

---

## 4. Monthly Dashboard

### 4.1 Features

- Month selector (dropdown or calendar)
- **Summary cards:** Total Income, Total Expense, Net Balance, Savings
- **Bank balances:** Show current balance for each bank account (e.g. Salary account, Savings). Essential for planning — e.g. when a chit fund is due every 4 or 6 months, the amount stays in the account until the payment month; users need to see balances at a glance.
- Category-wise pie chart
- Recurring upcoming payments (including non-monthly items, e.g. next chit due in May)
- Bank-wise balances (list or cards per account)

### 4.2 Dashboard calculation logic

For the selected month:

- **Income** = All one-time income in that month + All recurring income due in that month (for non-monthly frequency, only count months where the item is due, e.g. every 4 months → Jan, May, Sep)
- **Expense** = All one-time expense in that month + All recurring expense due in that month (same rule for every-4-months, every-6-months, etc.)
- **Net balance** = Income − Expense
- **Savings** = Net balance (same as above; shown as a separate card for clarity)

### 4.3 Bank balances on dashboard

Users must see **current balance per bank account** on the dashboard (e.g. Salary account, Savings, Cash). This is especially important when expenses are not monthly: for chit funds due every 4 or 6 months, the money stays in the account until the payment month, so viewing balances helps plan and avoid overspending.

---

## 5. Database Design

### 5.1 Tables


| Table        | Notes                                                         |
| ------------ | ------------------------------------------------------------- |
| Users        | Handled by ASP.NET Core Identity                              |
| Categories   | Id, UserId (nullable for system), Name, Type (Income/Expense) |
| Incomes      | As defined in §3.1                                            |
| Expenses     | As defined in §3.2                                            |
| BankAccounts | As defined in §3.4                                            |
| Transactions | Optional: store generated monthly entries for reporting       |


---

## 6. UI/UX Design Guidelines


| Page          | Guidelines                                                      |
| ------------- | --------------------------------------------------------------- |
| Login         | Clean, minimal                                                  |
| Dashboard     | Month switcher at top; summary cards; graphs; quick-add buttons |
| Income        | Add button; table view; edit/delete; filter by type             |
| Expense       | Same as Income                                                  |
| Bank accounts | Add account; view balances; transaction history                 |


**UI type:** MVC (Razor) with cookie auth, or SPA (React/Angular/Blazor) with JWT. Choose one and align auth and project structure.

---

## 7. Non-Functional Requirements

- Secure (auth, authorization, HTTPS)
- Fast loading and mobile responsive
- Clean UI and consistent UX
- Data isolation per user
- Soft delete instead of hard delete
- Proper logging and audit fields (e.g. CreatedBy, UpdatedBy, CreatedAt, UpdatedAt)

---

## 8. API Structure (Web API + SPA)

```
/api/auth
/api/income
/api/expense
/api/category
/api/bankaccount
/api/dashboard
```

---

## 9. Advanced Features (Phase 2+)

- Budget planning and savings goals
- PDF and Excel export
- Notifications for due EMI; recurring reminder email
- Graph analytics and AI expense categorization
- Multi-currency support

---

## 10. Edge Cases to Handle

- Recurring item added with start date in the past
- Editing recurring amount mid-cycle
- Deleting or pausing a recurring entry
- Leap year and timezone consistency
- Negative balance alerts

---

## 11. Security Considerations

- Validate ownership of every record (user can only access own data)
- Use authorization filters and prevent IDOR
- Input validation and parameterized queries (EF Core to avoid SQL injection)
- HTTPS only in production

---

## 12. Project Structure

```
/ExpenseManager
    /ExpenseManager.API
    /ExpenseManager.Application
    /ExpenseManager.Domain
    /ExpenseManager.Infrastructure
    /ExpenseManager.UI
```

---

## 13. Development Plan


| Phase   | Scope                                                                |
| ------- | -------------------------------------------------------------------- |
| Phase 1 | Authentication; CRUD Income; CRUD Expense; Categories; Bank accounts |
| Phase 2 | Recurring logic; Dashboard; Reporting                                |
| Phase 3 | Advanced analytics; Notifications; AI features                       |


---

## 14. Acceptance Criteria

- User can register and log in
- User can add recurring salary and EMI (e.g. from Jan 2024)
- Dashboard shows correct totals for a chosen month (e.g. Feb 2024)
- Month switching works
- Data is isolated per user
- Bank balance updates correctly (manual or from transactions)

---

## 15. Sample User Flow

1. Register
2. Add bank account (e.g. ₹50,000)
3. Add salary (e.g. ₹60,000 recurring from Jan 2024)
4. Add EMI (e.g. ₹10,000 from Jan 2024 to Dec 2026)
5. View dashboard for Feb 2024
  - Income: 60,000 | Expense: 10,000 | **Net: 50,000**

---

## 16. Getting Started

### Prerequisites

- .NET 8 SDK (or LTS version in use)
- SQLite (file-based; no separate database server required)
- IDE: Visual Studio 2022 or VS Code with C# extension

### Run the application

1. Clone the repository and open the solution (e.g. `ExpenseManager.sln`).
2. Set SQLite connection string in `appsettings.json` (e.g. `Data Source=app.db`). No separate database server is needed.
3. Run migrations: `dotnet ef database update` (from the project containing the DbContext).
4. Run the API or web project: `dotnet run --project ExpenseManager.API` or `ExpenseManager.UI`.

---

## Final Goal

A clean, scalable, and secure personal finance manager with:

- ASP.NET Core, Entity Framework Core, SQLite
- Identity-based authentication
- Monthly financial dashboard and reporting

