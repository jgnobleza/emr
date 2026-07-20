# MedRec Mobile

This is the iOS/mobile MAUI client for MedRec. It is offline-first: patients and checkups are saved immediately to local app storage, then the Sync tab can connect to the cloud app.

## Run locally on Windows preview

```powershell
dotnet workload restore .\MedRec.Mobile.csproj
dotnet build .\MedRec.Mobile.csproj -f net10.0-windows10.0.19041.0
dotnet run .\MedRec.Mobile.csproj -f net10.0-windows10.0.19041.0
```

## Run on iOS

Use a Mac with Xcode and the .NET MAUI iOS workload installed.

```bash
dotnet workload install maui-ios
dotnet restore MedRec.Mobile.csproj
dotnet build MedRec.Mobile.csproj -f net10.0-ios
```

For simulator:

```bash
dotnet build MedRec.Mobile.csproj -f net10.0-ios -t:Run
```

## Current sync status

The mobile app can save and display offline patients and checkups. The Sync tab verifies the configured cloud URL, but the ASP.NET backend still needs dedicated mobile sync endpoints and authentication before mobile records can upload into PostgreSQL.
