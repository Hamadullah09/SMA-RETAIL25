# Deploying to pos.sma-techno.net (myASP.NET)

The server (`src/RetailPlus.Server`: the tills' sync API and the API of the web back office) and the web back office
(`web/`, served from the server's `web` folder) run as one site on myASP.NET, at https://pos.sma-techno.net. The
pipeline is `.github/workflows/ci-cd.yml`.

## What the pipeline does

On every push and pull request:

1. **.NET build and tests.** The tests that need SQL Server skip on GitHub's runner.
2. **Web back office.** typecheck, lint, unit tests and the static build.
3. **Packages**, kept with the run for 30 days (*Actions › the run › Artifacts*):
   - `sma-retail-server`: the site, about 48 MB. It is framework-dependent on the host's .NET 10, Windows x64, out of
     process under IIS, with the web back office in `web`.
   - `sma-retail-database`: `migrate.sql`, an idempotent script of the database, to read or to apply by hand. The
     server brings its database up to date by itself when it starts, as the tills do.
   - `sma-retail-desktop`: the desktop till as one self-contained `RetailPlus.exe`.
4. **Deploy**, only while the repository variable `MYASP_DEPLOY_ENABLED` is `true`. It runs on a push to
   `integration-all-phases`, or on a manual run. The deploy:
   - takes the site offline with `app_offline.htm`; the tills keep selling meanwhile and send their sales later;
   - syncs the package with Web Deploy;
   - brings the site back, also when the sync failed;
   - checks that `/api/health` answers `status ok, database ok` and that the sign-in page loads.

The deploy **leaves these alone on the host**:
- `App_Data`: the server's signing keys, the tills' station keys and received packages. Never delete it: the tills
  would have to be enrolled again.
- `logs`.
- `.well-known`: the host's own files for domain checks and SSL renewal. `web.config` lets IIS serve them as plain files.
- `backend`: Retail25's API application, while it is still there.

Everything else in the site folder is replaced by the package, including Retail25's front end.

## One-time setup (before the first deploy)

### 1. The database

In the control panel, *Databases › MSSQL*, add a **new** database, for example `db_acd077_smaretail`. Leave Retail25's
`db_acd077_pos` as it is. Then either:

- **Carry Retail25 over.** Follow the runbook in `docs/CUTOVER.md` §2. Run `RetailPlus.Cutover` from a PC that can reach
  the panel's SQL Server, with `--source` set to `db_acd077_pos` (only read) and `--target` set to the new database.
  Do a `--dry-run` first and read the report. Staff then sign in with their Retail25 user names and passwords.
- **Start empty.** The server creates the tables when it first starts, and the first super administrator comes from
  the two `Bootstrap__…` variables below.

### 2. The site (control panel, *Websites › pos.sma-techno.net*)

- **Retail25's front end.** Switch off its Node.js app when you go live; the deploy replaces its files.
- **.NET.** Check that the site runs ASP.NET Core / .NET 10. The server runs out of process (`web.config`), so it does
  not compete with Retail25's API for the one in-process app the plan allows.
- **Settings.** *Application Pool › Environment Variables* (never in a file: the repository is public):
  - `ConnectionStrings__RetailPlus` = the panel's connection string for the new database, plus
    `;Encrypt=True;TrustServerCertificate=True`.
  - Only for an empty database, until the first sign-in: `Bootstrap__AdminUserName` and `Bootstrap__AdminPassword`.
    Remove them after the first sign-in; they are never used on a database that has users.
- **HTTPS.** Install the SSL certificate and switch on *Force HTTPS*. Sign-in cookies are sent only over HTTPS.

### 3. GitHub (repository *Settings › Secrets and variables › Actions*)

From the site's **Web Deploy publish profile** (control panel › the site › *Web Deploy* › download the profile):

| Kind | Name | Value |
|---|---|---|
| Secret | `MYASP_SITE_NAME` | `msdeploySite` in the profile |
| Secret | `MYASP_DEPLOY_USER` | `userName` in the profile |
| Secret | `MYASP_DEPLOY_PASSWORD` | `userPWD` in the profile |
| Variable | `MYASP_WEBDEPLOY_HOST` | only if `publishUrl` is not `win8238.site4now.net` |
| Variable | `MYASP_DEPLOY_ENABLED` | `true` **only when you go live** (step 5) |

Optional: *Settings › Environments › production* can require a person to approve each deploy.

### 4. Preview (nothing is written)

Run the pipeline by hand with **Preview only** ticked. The deploy job then lists what would be added, changed and
removed on the host:

```
gh workflow run ci-cd.yml --ref integration-all-phases -f dry_run=true
```

GitHub shows the *Run workflow* button only for workflows on the repository's default branch, which is `main`. Either
use the command above, or make `integration-all-phases` the default branch (*Settings › General*).

### 5. Go live

1. Take a backup of the site in the control panel, then of `db_acd077_pos`.
2. Set the variable `MYASP_DEPLOY_ENABLED` to `true`.
3. Push to `integration-all-phases`, or run the pipeline by hand without *Preview only*. From then on, every push to
   `integration-all-phases` that passes the build, the tests and the packaging goes live.
4. **Stop Retail25 coming back.** `main` has Retail25's own `deploy-myasp.yml`, which puts Retail25 on the site at every
   push to `main`. Disable it (*Actions › deploy-myasp › … › Disable workflow*), or never push to `main`.
5. **Tills.** On the web, *Stores › the store › Tills* gives each till an enrolment code. At each till,
   *Setup › Server* takes `https://pos.sma-techno.net` and the code.

## Day to day

- **Deploy:** push to `integration-all-phases`. A failing test or build stops the deploy.
- **Roll back:** open an earlier successful run and *Re-run jobs*, which deploys that run's package. Database changes go
  forward only: to go back past one, restore the database backup.
- **The site does not start:** `/api/health` says whether the database is reached. Set `stdoutLogEnabled="true"` in the
  site's `web.config` for a moment (logs in `logs`), or look at the panel's error log.
- **Shared hosting.** IIS stops an idle site after a while. The tills' syncs every few minutes keep it awake in opening
  hours, and the server's background work (such as posting the business days) catches up when it starts again.
