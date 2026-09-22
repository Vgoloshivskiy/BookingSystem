# Deploying to Azure

These steps provision the three Azure resources the assignment calls for (Web App,
SignalR Service, SQL Database) and point the app at them. Run from the Azure CLI
(`az login` first).

## 1. Resource group

```bash
az group create --name booking-system-rg --location eastus
```

## 2. Azure SQL Database

```bash
az sql server create \
  --name booking-system-sql-<unique-suffix> \
  --resource-group booking-system-rg \
  --location eastus \
  --admin-user sqladmin \
  --admin-password '<strong-password>'

az sql server firewall-rule create \
  --resource-group booking-system-rg \
  --server booking-system-sql-<unique-suffix> \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

az sql db create \
  --resource-group booking-system-rg \
  --server booking-system-sql-<unique-suffix> \
  --name BookingSystemDb \
  --service-objective Basic
```

## 3. Azure SignalR Service

```bash
az signalr create \
  --name booking-system-signalr-<unique-suffix> \
  --resource-group booking-system-rg \
  --sku Free_F1 \
  --service-mode Default
```

## 4. Azure Web App (backend + frontend served from the same app)

```bash
az appservice plan create \
  --name booking-system-plan \
  --resource-group booking-system-rg \
  --sku B1 --is-linux

az webapp create \
  --resource-group booking-system-rg \
  --plan booking-system-plan \
  --name booking-system-app-<unique-suffix> \
  --runtime "DOTNETCORE:8.0"
```

The `frontend/` folder is static (no build step); the simplest option is to publish it
into `wwwroot` of the API project so one Web App serves both, e.g. copy
`frontend/*` into `src/BookingSystem.Api/wwwroot/` during CI/CD and add
`app.UseStaticFiles()` (and `app.UseDefaultFiles()`) in `Program.cs`. Alternatively,
deploy the frontend as a separate static Web App and set its origin in
`Cors:AllowedOrigins`.

## 5. Configure app settings

```bash
SQL_CONN="Server=tcp:booking-system-sql-<unique-suffix>.database.windows.net,1433;Database=BookingSystemDb;User ID=sqladmin;Password=<strong-password>;Encrypt=true;"
SIGNALR_CONN=$(az signalr key list --name booking-system-signalr-<unique-suffix> --resource-group booking-system-rg --query primaryConnectionString -o tsv)

az webapp config appsettings set \
  --resource-group booking-system-rg \
  --name booking-system-app-<unique-suffix> \
  --settings \
    Database__Provider="SqlServer" \
    ConnectionStrings__SqlServer="$SQL_CONN" \
    ConnectionStrings__AzureSignalR="$SIGNALR_CONN" \
    Jwt__Key="<generate-a-long-random-secret>" \
    Jwt__Issuer="BookingSystem" \
    Jwt__Audience="BookingSystemClient"
```

Never commit real connection strings or the JWT key — set them only via
`az webapp config appsettings set` (or the Azure Portal's Configuration blade).

## 6. Publish and deploy

```bash
dotnet publish src/BookingSystem.Api -c Release -o ./publish

cd publish && zip -r ../publish.zip . && cd ..

az webapp deploy \
  --resource-group booking-system-rg \
  --name booking-system-app-<unique-suffix> \
  --src-path publish.zip \
  --type zip
```

On first startup, `SeedData.InitializeAsync` calls `Database.EnsureCreatedAsync()`,
which builds the schema directly from the current EF Core model against the Azure SQL
database (no migration files are required for this), then creates the roles / seed
admin account / sample resources. This is fine for a first deploy to a fresh database.

Before you evolve the data model further, switch to a proper migrations workflow:
run `dotnet ef migrations add InitialCreate --project src/BookingSystem.Api` once,
change `SeedData.cs` to call `Database.MigrateAsync()` instead of
`EnsureCreatedAsync()`, and apply subsequent changes with
`dotnet ef database update` (ideally from CI) rather than relying on
auto-schema-creation in production.

## 7. Verify

- `https://booking-system-app-<unique-suffix>.azurewebsites.net/swagger` — API docs.
- Log in as `admin@bookingsystem.local` / `Admin123!` (change this password
  immediately after first deploy, or remove the seed account entirely).
- Open the schedule for a resource in two browser windows and confirm a booking made
  in one window appears in the other without a refresh (SignalR working end to end).
