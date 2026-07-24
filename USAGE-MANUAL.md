# MedRec EMR Usage Manual

This guide is for doctors and clinic staff using MedRec on a laptop, tablet, or phone.

## Connecting PostgreSQL on Render

MedRec reads the database connection from `DATABASE_URL` first when the checked-in default connection string still contains the placeholder password.

On Render:

1. Open the MedRec web service in the Render dashboard.
2. Go to Environment.
3. Add `DATABASE_URL` with your Render PostgreSQL Internal Database URL if the app and database are on Render, or the External Database URL for local testing.
4. Save and redeploy the web service.

For a local PowerShell test, set the variable before running the app:

```powershell
$env:DATABASE_URL="postgresql://USER:PASSWORD@HOST:PORT/DATABASE?sslmode=require"
dotnet run
```

The app runs `Database/schema.sql` at startup when PostgreSQL is configured, so the tables are created automatically if they do not exist.

## What MedRec Does

MedRec keeps patient records, checkups, laboratories, prescriptions, reports, and doctor settings in one web app.

The app works from the deployed website link. After the first online use, the browser keeps the needed MedRec pages, data, and files on the device so the same deployed link can continue opening when the device is disconnected. When internet returns, pending changes are sent back to the server.

## First-Time Offline Setup

Use the app online the first time on every device.

1. Open the deployed MedRec website link in the browser.
2. Log in with your doctor or admin account.
3. Wait until the footer says Offline site ready.
4. Wait until the Online status appears.
5. The device will quietly download every active patient, checkup, lab, prescription, page, and linked file that is available from the server for offline use.

After an app update, open MedRec online once from the deployed link before using it offline. This lets the browser download the newest offline files and refresh local data.

Do not use a copied local folder or a different address for offline setup. Offline files are downloaded only from the same deployed link where MedRec is hosted.

After the footer shows the offline cache is ready, MedRec can be closed and opened again later from the same browser, even after a device restart, as long as browser site data was not cleared.

## Using on a Laptop/Tablet

Recommended browsers: Microsoft Edge or Google Chrome.

1. Open the deployed MedRec website link.
2. Log in.
3. Wait for Offline site ready.
4. Bookmark the deployed link if needed.
5. Use the same browser and link when offline.

## Using on Android Tablet or Phone

Recommended browser: Google Chrome.

1. Open MedRec in Chrome.
2. Log in.
3. Wait for Offline site ready.
4. Bookmark the deployed link if needed.
5. Use the same browser and link when offline.

## Using on iPhone or iPad

Use Safari.

1. Open MedRec in Safari.
2. Log in.
3. Wait for Offline site ready.
4. Bookmark the deployed link if needed.
5. Use the same browser and link when offline.

## Online and Offline Status

At the top of the app, the status pill shows:

- Online: connected to the server
- Disconnected: no internet or server unavailable

When online, changes are saved to the server normally.

When disconnected, supported forms are saved locally on the device and sent when the connection returns.

If the MedRec server is unavailable but the device still has Wi-Fi, the app also treats that as disconnected. Checkups, lab uploads, and other supported forms are saved locally instead of being lost.

## Manual Sync

Use Manual Sync when you want to force the app to send and receive updates.

1. Make sure the device is online.
2. Click Manual Sync.
3. Wait for the success message.

If Manual Sync fails, check the internet connection and try again.

## Patient Workflow

### Add Patient

1. Go to Dashboard or Patients.
2. Click Add patient.
3. Fill in patient information.
4. Add or capture a patient photo if needed.
5. Click Save patient.

If offline, the patient is saved locally and will sync later.

### Edit Patient

1. Go to Patients.
2. Click Edit info on the patient card.
3. Update the details.
4. Click Save changes.

### Archive Patient

1. Go to Patients.
2. Click the red trash button on the patient card.
3. Confirm in the archive modal.

Archived patients are hidden from the normal patient list.

## Check Up Workflow

### Open Check Ups

1. Go to Patients or Dashboard.
2. Open the patient.
3. Go to Check Ups.

### Add New Checkup

1. Click New Checkup.
2. Select or confirm the patient.
3. Enter visit date.
4. Type the complaint.
   - Complaint suggestions appear from common previous complaints.
5. Enter height, weight, BP, fetal heart rate, and temperature.
6. Click Create check up.

If offline or the server is unavailable, the checkup is saved locally on the device. The footer shows how many changes are waiting to sync.

### Edit Checkup Details

1. Open the selected checkup.
2. Click Edit checkup.
3. Correct complaint or vital signs.
4. Click Save checkup.

### Diagnosis and Notes

1. Type diagnosis in the Diagnosis field.
2. Type additional notes in Notes.
3. Click Save diagnosis.
4. Use View diagnosis to preview the printable document.

## Laboratory Workflow

### Upload Lab

1. Open the patient checkup.
2. Click Upload lab.
3. Select the checkup.
4. Enter lab name.
5. Upload a PDF or image.
6. Camera capture is available on supported devices.
7. Save.

If offline or the server is unavailable, the lab form and selected file are saved locally on the device. Keep the same browser storage until the footer shows the change has synced.

### View Lab Offline

Lab files can be viewed offline after the device finishes the online offline-sync download.

To make sure a lab is available offline:

1. Open the deployed MedRec link while online.
2. Log in.
3. Wait until the footer says the offline site is ready and shows the patient/checkup cache count.

If a lab file was added on another device after the last sync, this device needs to go online again before that new file is available offline.

## Prescription Workflow

### Add Prescription

1. Go to Prescriptions.
2. Click New prescription.
3. Select patient.
4. Select checkup if needed.
5. Add medication rows.
   - Medication suggestions appear from drugs prescribed previously.
6. Enter dosage, frequency, and duration.
7. Add instructions.
8. Confirm prescriber.
9. Click Save prescription.

### Print Prescription

1. Go to Prescriptions.
2. Click View.
3. Review the preview.
4. Click Print.

## Reports

Use Reports to review patient, checkup, lab, prescription, and sync data.

Reports are best used while online because they depend on the latest server data.

## My Settings

Use My Settings to update doctor details, license information, contact number, signature, and print layout settings.

Open this page online first if you need it available offline later.

## Offline Use

While offline, you can continue using cached pages and supported forms.

Log in while the server is available before going offline. A new login cannot be verified while the server is shut down, but the browser can keep opening the normal MedRec pages already saved on that device. The normal login session is kept for up to 7 days so overnight use, browser close, and device restart are supported.

Supported offline actions include:

- Add/edit patients
- Save patient photos for later upload
- Add/edit checkups
- Save diagnosis and notes
- Upload lab files for later sync
- Add prescriptions

If the server is fully shut down and the deployed link is opened offline in the same browser, MedRec opens the normal cached site pages from the device. Every patient page and checkup page downloaded during the last online sync can be opened, even if that patient was not manually viewed before going offline. Patient data, checkups, laboratories, prescriptions, and cached files come from the last successful online sync.

Example: if the clinic goes offline at 8:00 PM, the device can be shut down or restarted, then MedRec can be opened again at 9:00 AM without internet from the same deployed link and browser profile.

When internet returns:

1. The app attempts to replay locally saved changes.
2. Files are uploaded.
3. Local device data is refreshed from the server.
4. The footer stops showing changes waiting to sync.

If the browser was closed while offline, open the same deployed MedRec link after reconnecting. The app retries the saved changes and uploads files from the local queue.

## Data Priority

The online SQL database is the central backup and main source of truth.

If the same record is changed on two devices, the server version has priority. Offline changes may be rejected if the server record was updated after the device downloaded its copy.

## File Storage

The app can cache files on the device:

- patient photos
- lab images
- lab PDFs
- signatures
- layout logos

Files are available offline after the device downloads them during online offline-sync. This includes patient photos, lab files, signatures, and layout logos from the synced patient records.

Large files take more device storage. If the browser clears storage, offline files may need to be downloaded again.

## Best Practices

- Log in online before using a new device offline.
- Open the deployed link while online and wait for the patient/checkup cache count before clinic travel.
- Use Manual Sync before closing the app at the end of the day.
- If the footer says changes are waiting to sync, keep MedRec open online until the waiting count disappears.
- Keep the browser storage enabled.
- Do not use private/incognito mode for offline work.
- Avoid clearing site data unless you are sure all pending changes have synced.
- If using multiple devices, sync each device before switching.

## Troubleshooting

### The deployed link says "This site can't be reached" when offline

This usually means the browser did not finish downloading the offline files from the deployed link.

1. Reconnect to the internet.
2. Open the deployed MedRec link in Chrome, Edge, or Safari.
3. Log in.
4. Wait until the footer says Offline site ready.
5. Keep the tab open for a short time so data and files can finish downloading.
6. Disconnect and open the same deployed link again in the same browser.

The app should now open the normal cached MedRec site instead of the browser error page.

### The app says Disconnected

Check Wi-Fi or mobile data. If the website server is down, wait until it is available again.

### Offline changes are not appearing on another device

The first device must reconnect and sync before other devices can receive those changes.

### A patient, checkup, or lab file is not available offline

Open the deployed link while online, log in, and wait for the footer to show the patient/checkup cache count. The device needs to finish the full offline-sync download before newly added records from another device are available offline.

### A saved offline change did not sync

Reconnect to the internet and click Manual Sync. If it still fails, check whether the record was changed from another device.

### The app is showing old data

Go online and click Manual Sync. Refresh the page after sync completes.

## Device Recommendations

Laptop:

- Best for reports, printing, and admin work.
- Use Chrome or Edge.

Tablet:

- Best for patient intake, checkups, photos, and labs.
- Install to home screen.

Phone:

- Best for quick lookup, camera capture, and emergency access.
- Use after first online login and sync.

