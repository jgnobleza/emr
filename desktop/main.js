const { app, BrowserWindow, dialog } = require("electron");
const { spawn } = require("node:child_process");
const fs = require("node:fs");
const path = require("node:path");

let serverProcess = null;
let logStream = null;

const defaultCloudDatabaseUrl =
  "postgresql://emrdb_mtxm_user:1CW1uy8lv3PQRxCEbHR451O8qA6RXHkk@dpg-d9ciidb7uimc73dt3dl0-a.oregon-postgres.render.com/emrdb_mtxm?sslmode=require";

function publishRoot() {
  const packagedRoot = path.join(process.resourcesPath || "", "server");
  if (fs.existsSync(path.join(packagedRoot, "medrec.dll"))) {
    return packagedRoot;
  }

  return path.resolve(__dirname, "..", "bin", "Release", "net10.0", "publish");
}

function startServer() {
  const root = publishRoot();
  const dll = path.join(root, "medrec.dll");
  if (!fs.existsSync(dll)) {
    dialog.showErrorBox("MedRec is not published", "Run `dotnet publish -c Release` before starting the desktop shell.");
    app.quit();
    return;
  }

  const port = process.env.MEDREC_DESKTOP_PORT || "5177";
  const localDataPath = path.join(app.getPath("userData"), "MedRec");
  const fileStorageProvider =
    process.env.MedRec__FileStorageProvider
    || (process.env.GOOGLE_DRIVE_FOLDER_ID ? "GoogleDrive" : "Local");
  fs.mkdirSync(localDataPath, { recursive: true });
  logStream = fs.createWriteStream(path.join(localDataPath, "desktop-server.log"), { flags: "a" });
  logStream.write(`\n[${new Date().toISOString()}] Starting MedRec server from ${root}\n`);
  serverProcess = spawn("dotnet", [dll], {
    cwd: root,
    env: {
      ...process.env,
      ASPNETCORE_URLS: `http://127.0.0.1:${port}`,
      ASPNETCORE_ENVIRONMENT: "Production",
      DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE: "false",
      DATABASE_URL: process.env.DATABASE_URL || defaultCloudDatabaseUrl,
      MedRec__StorageMode: "Local",
      MedRec__FileStorageProvider: fileStorageProvider,
      MedRec__LocalDataPath: localDataPath,
      GoogleDrive__AuthMode: process.env.GoogleDrive__AuthMode || process.env.GOOGLE_DRIVE_AUTH_MODE || "",
      GoogleDrive__FolderId: process.env.GoogleDrive__FolderId || process.env.GOOGLE_DRIVE_FOLDER_ID || "",
      GoogleDrive__ServiceAccountJson: process.env.GoogleDrive__ServiceAccountJson || process.env.GOOGLE_DRIVE_SERVICE_ACCOUNT_JSON || "",
      GoogleDrive__ServiceAccountJsonBase64: process.env.GoogleDrive__ServiceAccountJsonBase64 || process.env.GOOGLE_DRIVE_SERVICE_ACCOUNT_JSON_BASE64 || "",
      GoogleDrive__ServiceAccountJsonPath: process.env.GoogleDrive__ServiceAccountJsonPath || process.env.GOOGLE_DRIVE_SERVICE_ACCOUNT_JSON_PATH || "",
      GoogleDrive__OAuthClientJson: process.env.GoogleDrive__OAuthClientJson || process.env.GOOGLE_DRIVE_OAUTH_CLIENT_JSON || "",
      GoogleDrive__OAuthClientJsonBase64: process.env.GoogleDrive__OAuthClientJsonBase64 || process.env.GOOGLE_DRIVE_OAUTH_CLIENT_JSON_BASE64 || "",
      GoogleDrive__OAuthClientJsonPath: process.env.GoogleDrive__OAuthClientJsonPath || process.env.GOOGLE_DRIVE_OAUTH_CLIENT_JSON_PATH || ""
    },
    stdio: ["ignore", "pipe", "pipe"],
    windowsHide: true
  });

  serverProcess.stdout.on("data", chunk => logStream && logStream.write(chunk));
  serverProcess.stderr.on("data", chunk => logStream && logStream.write(chunk));

  serverProcess.on("exit", () => {
    logStream && logStream.write(`[${new Date().toISOString()}] MedRec server exited\n`);
    logStream && logStream.end();
    logStream = null;
    serverProcess = null;
  });

  return `http://127.0.0.1:${port}`;
}

function createWindow(url) {
  const window = new BrowserWindow({
    width: 1280,
    height: 860,
    minWidth: 1024,
    minHeight: 720,
    title: "MedRec",
    webPreferences: {
      preload: path.join(__dirname, "preload.js")
    }
  });

  window.loadURL(url);
}

app.whenReady().then(async () => {
  const url = startServer();
  if (!url) {
    return;
  }

  setTimeout(() => createWindow(url), 1800);
});

app.on("window-all-closed", () => {
  if (serverProcess) {
    serverProcess.kill();
  }
  app.quit();
});
