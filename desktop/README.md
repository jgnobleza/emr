# MedRec Desktop Shell

This is the installable desktop wrapper for MedRec.

Current state:

- Starts the published ASP.NET app locally.
- Forces `MedRec:StorageMode=Local`.
- Stores local SQLite/files under Electron's user data directory.
- Opens MedRec in a desktop window.

Development run:

```powershell
dotnet publish ..\medrec.csproj -c Release
npm install
npm start
```

Installer build:

```powershell
dotnet publish ..\medrec.csproj -c Release
npm install
npm run dist
```

The remaining migration work is to move MVC repository operations from PostgreSQL-only to the SQLite local repository and sync them to Render in the background.
