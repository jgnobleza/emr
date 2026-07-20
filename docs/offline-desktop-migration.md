# Offline Desktop Migration

MedRec is moving to an offline-first desktop model.

## Target Shape

- The installed desktop app writes to a local SQLite database first.
- Photos, lab PDFs, signatures, logos, and layout images are saved in a local app data folder first.
- A background sync engine uploads pending rows and files when internet is available.
- Render PostgreSQL remains the cloud source for backup and multi-device sync.
- The current Render web app remains available during the migration.

## Storage Layout

Local desktop data:

```text
MedRec/
  medrec.local.db
  files/
    patients/
    labs/
    signatures/
    logos/
    layout-images/
```

Cloud data:

```text
Render ASP.NET API
Render PostgreSQL
Cloud file storage for uploads
```

## Migration Order

1. Add SQLite schema and local storage paths.
2. Move repository operations behind a provider boundary.
3. Implement SQLite repository for patients, checkups, labs, prescriptions, reports, users, and layouts.
4. Make local mode the default for desktop.
5. Add sync endpoints and client sync worker using stable `client_uid` values.
6. Add desktop shell/installer.
7. Keep Render in server mode with PostgreSQL.

## Configuration

Render/cloud mode:

```text
DATABASE_URL=postgresql://...
MedRec:StorageMode=Cloud
```

Desktop/local mode:

```text
MedRec:StorageMode=Local
MedRec:LocalDataPath=<app-data-folder>
```

## Sync Rule

Every syncable entity has a `client_uid`. Local rows keep their local integer `id`, while sync uses `client_uid` to upsert into PostgreSQL and match records across devices.
