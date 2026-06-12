# Personal Finance Tracker

A cross-platform personal finance app that connects to real bank accounts and provides spending insights, transaction history, and budget tracking.

## What it does

- Links to bank accounts via Plaid (supports Bank of America and most US banks)
- Fetches and displays real transaction history and account balances
- Categorises spending automatically
- Tracks budgets and financial goals
- Runs natively on iOS, Android, macOS, and Windows from a single codebase

## Tech stack

| Layer | Technology |
|---|---|
| Mobile / Desktop UI | .NET MAUI (C# + XAML) |
| Backend API | ASP.NET Core |
| Authentication | ASP.NET Identity + JWT |
| ORM | Entity Framework Core |
| Database | PostgreSQL |
| Bank data | Plaid API |
| Hosting | Azure App Service |
| Managed DB | Azure Database for PostgreSQL |
| Containers | Docker |

## Architecture

The MAUI frontend communicates exclusively with the ASP.NET Core backend via HTTPS REST. The backend handles all business logic, authentication, and third-party API calls. Plaid credentials and bank data never touch the client directly.
