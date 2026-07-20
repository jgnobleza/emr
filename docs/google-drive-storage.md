# Google Drive File Storage

MedRec can store uploaded patient photos, lab PDFs/images, signatures, logos, and layout images in Google Drive.

## Setup

1. Create or choose a Google Drive folder for MedRec uploads.
2. Create a Google Cloud service account with Drive API access.
3. Share the Drive folder with the service account email as Editor.
4. Set these environment variables before launching the desktop app:

```powershell
$env:GOOGLE_DRIVE_FOLDER_ID="your-drive-folder-id"
$env:GOOGLE_DRIVE_SERVICE_ACCOUNT_JSON_PATH="C:\secure\medrec-drive-service-account.json"
```

Alternatively, use the base64 form if you do not want a JSON file path:

```powershell
$env:GOOGLE_DRIVE_FOLDER_ID="your-drive-folder-id"
$env:GOOGLE_DRIVE_SERVICE_ACCOUNT_JSON_BASE64="base64-of-service-account-json"
```

The desktop shell automatically switches `MedRec:FileStorageProvider` to `GoogleDrive` when `GOOGLE_DRIVE_FOLDER_ID` is present.

## Behavior

- New uploads go directly to Google Drive when credentials are configured and the internet is available.
- If Drive upload fails, MedRec saves the file locally so the user can continue working.
- Manual Sync uploads existing `/local-files/...` records to Drive, rewrites the database URL to `/files/drive/{fileId}/...`, then syncs the database to Render PostgreSQL.
- Files are downloaded from Drive on demand through the app route `/files/drive/{fileId}/{fileName}`.
