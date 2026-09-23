# CI/CD: GitHub Actions → Azure Web App

`.github/workflows/deploy.yml` builds the API, runs `dotnet test` (including the
required concurrency test) as a deploy gate, folds the static `frontend/` folder into
the published app's `wwwroot`, and pushes the result to your Azure Web App on every
push to `main`.

## One-time setup

### 1. Point the workflow at your Web App

Edit `AZURE_WEBAPP_NAME` in `.github/workflows/deploy.yml` to match the name you used
in `az webapp create --name ...` (see `docs/DEPLOYMENT.md`).

### 2. Get a publish profile from Azure

```bash
az webapp deployment list-publishing-profiles \
  --name <your-webapp-name> \
  --resource-group booking-system-rg \
  --xml
```

This prints an XML document. Copy the **entire** output (or use the Portal:
App Service → Overview → **Download publish profile**, then open the file).

### 3. Add it as a GitHub secret

In your GitHub repo: **Settings → Secrets and variables → Actions → New repository
secret**

- Name: `AZURE_WEBAPP_PUBLISH_PROFILE`
- Value: the full XML from step 2

Never commit this file to the repo — it's a bearer credential for deploying to your
app. If it ever leaks, reset it from the Portal (Overview → Reset publish profile).

### 4. Push to `main`

That's it — the next push triggers `build-and-test`, then `deploy`. Watch it under
the **Actions** tab of your repo. If `dotnet test` fails, the `deploy` job never runs
(it depends on `build-and-test` via `needs:`).

## Configuring the app itself

The workflow only ships code — it does not set `Database:Provider`,
`ConnectionStrings:SqlServer`, `ConnectionStrings:AzureSignalR`, or `Jwt:Key`. Those
are environment configuration, not something that belongs in the repo or the
pipeline, and are set once via `az webapp config appsettings set` as described in
`docs/DEPLOYMENT.md`. They persist across deploys since app settings live on the Web
App resource, not in the deployed package.

## Why a publish profile instead of OIDC / a service principal

A publish profile is the simplest thing that works and is enough for a small project
like this. For a longer-lived or team project, the more modern (and more secure)
approach is federated OIDC login via `azure/login`, which avoids a long-lived secret
entirely — see
[Microsoft's guide to configuring GitHub Actions with an OIDC service principal](https://learn.microsoft.com/en-us/azure/developer/github/connect-from-azure)
if you want to switch to that later; swapping it in only changes the `deploy` job's
auth step, not the build/test/publish steps above it.

## Manually triggering a deploy

The workflow also has `workflow_dispatch`, so you can trigger a deploy without a new
commit from the repo's **Actions** tab → **Build, test, and deploy to Azure** →
**Run workflow** — useful right after setting up the secret, to confirm it works.
