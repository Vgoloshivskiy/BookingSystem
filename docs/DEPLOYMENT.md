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
  --name booking-system-sql-vgptreenbit \
  --resource-group booking-system-rg \
  --location australiaeast \
  --admin-user sqladmin \
  --admin-password '<strong-password>'

az sql server firewall-rule create \
  --resource-group booking-system-rg \
  --server booking-system-sql-vgptreenbit \
  --name AllowAzureServices \
  --start-ip-address 0.0.0.0 --end-ip-address 0.0.0.0

az sql db create \
  --resource-group booking-system-rg \
  --server booking-system-sql-vgptreenbit \
  --name BookingSystemDb \
  --service-objective Basic
```

## 3. Azure SignalR Service

```bash
az signalr create \
  --name booking-system-signalr-vgptreenbit \
  --resource-group booking-system-rg \
  --sku Free_F1 \
  --service-mode Default
```

## 4. Azure Web App (backend + frontend served from the same app)

```bash
az appservice plan create \
  --name booking-system-plan \
  --resource-group booking-system-rg \
  --sku F1 --is-linux \
  --location australiaeast

az webapp create \
  --resource-group booking-system-rg \
  --plan booking-system-plan \
  --name booking-system-app-vgptreenbit \
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
SQL_CONN="Server=tcp:booking-system-sql-vgptreenbit.database.windows.net,1433;Database=BookingSystemDb;User ID=sqladmin;Password=<strong-password>;Encrypt=true;"
SIGNALR_CONN=$(az signalr key list --name booking-system-signalr-vgptreenbit --resource-group booking-system-rg --query primaryConnectionString -o tsv)

az webapp config appsettings set \
  --resource-group booking-system-rg \
  --name booking-system-app-vgptreenbit \
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

On first startup, `SeedData.InitializeAsync` runs `Database.MigrateAsync()`
automatically, applying EF Core migrations and creating the roles / seed admin
account / sample resources against the Azure SQL database — no manual migration step
is required for first deploy, though for ongoing changes prefer running
`dotnet ef database update` from CI as part of the release pipeline instead of relying
on auto-migrate in production.

## 7. Verify

- `https://booking-system-app-<unique-suffix>.azurewebsites.net/swagger` — API docs.
- Log in as `admin@bookingsystem.local` / `Admin123!` (change this password
  immediately after first deploy, or remove the seed account entirely).
- Open the schedule for a resource in two browser windows and confirm a booking made
  in one window appears in the other without a refresh (SignalR working end to end).