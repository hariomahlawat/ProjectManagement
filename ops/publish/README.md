# Publish folder setup

Use this folder to generate a clean, repeatable publish output for deployments. The scripts avoid inline commands inside Razor views and respect the application's CSP-friendly build pipeline.

## Steps

1. Ensure the .NET 8 SDK is installed and restore dependencies.
2. Run the publish script for your platform to create `./publish` under the repo root. Adjust the `--configuration` or `--framework` switches if you need a specific build target.

```bash
./ops/publish/create-publish-folder.sh
```

On Windows PowerShell, run:

```powershell
./ops/publish/create-publish-folder.ps1
```

The scripts validate required configuration before publishing and use Release builds to avoid shipping debug assets.
