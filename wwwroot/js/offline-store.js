(function () {
  const dbName = "medrec-offline";
  const dbVersion = 2;
  const snapshotKey = "current";
  const snapshotUrl = "/api/offline/snapshot";
  const fileCacheName = "medrec-offline-files-v1";
  let replayInProgress = false;

  if (!("indexedDB" in window)) {
    return;
  }

  function openDb() {
    return new Promise((resolve, reject) => {
      const request = indexedDB.open(dbName, dbVersion);
      request.onupgradeneeded = () => {
        const db = request.result;
        if (!db.objectStoreNames.contains("snapshots")) {
          db.createObjectStore("snapshots", { keyPath: "id" });
        }
        if (!db.objectStoreNames.contains("metadata")) {
          db.createObjectStore("metadata", { keyPath: "key" });
        }
        if (!db.objectStoreNames.contains("postQueue")) {
          db.createObjectStore("postQueue", { keyPath: "id" });
        }
        if (!db.objectStoreNames.contains("operations")) {
          db.createObjectStore("operations", { keyPath: "id" });
        }
      };
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
  }

  async function put(storeName, value) {
    const db = await openDb();
    await new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readwrite");
      tx.objectStore(storeName).put(value);
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
    });
    db.close();
  }

  async function get(storeName, key) {
    const db = await openDb();
    const value = await new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readonly");
      const request = tx.objectStore(storeName).get(key);
      request.onsuccess = () => resolve(request.result);
      request.onerror = () => reject(request.error);
    });
    db.close();
    return value;
  }

  async function getAll(storeName) {
    const db = await openDb();
    const values = await new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readonly");
      const request = tx.objectStore(storeName).getAll();
      request.onsuccess = () => resolve(request.result || []);
      request.onerror = () => reject(request.error);
    });
    db.close();
    return values;
  }

  async function deleteItem(storeName, key) {
    const db = await openDb();
    await new Promise((resolve, reject) => {
      const tx = db.transaction(storeName, "readwrite");
      tx.objectStore(storeName).delete(key);
      tx.oncomplete = resolve;
      tx.onerror = () => reject(tx.error);
    });
    db.close();
  }

  async function refreshSnapshot() {
    if (!navigator.onLine) {
      return;
    }

    let response;
    try {
      response = await fetch(snapshotUrl, {
        credentials: "same-origin",
        headers: { "Accept": "application/json" }
      });
    } catch {
      return;
    }

    if (!response.ok || response.url.includes("/Account/Login")) {
      return;
    }

    const snapshot = await response.json();
    await put("snapshots", {
      id: snapshotKey,
      generatedAtUtc: snapshot.generatedAtUtc || new Date().toISOString(),
      data: snapshot
    });
    await put("metadata", {
      key: "lastSnapshotAtUtc",
      value: new Date().toISOString()
    });
    await cacheSnapshotFiles(snapshot);
    await cacheSnapshotRecordPages(snapshot);
    await requestPersistentStorage();
    notifyOfflineDataReady(snapshot);
  }

  async function cacheSnapshotFiles(snapshot) {
    if (!("caches" in window)) {
      return;
    }

    const urls = collectFileUrls(snapshot);
    if (urls.length === 0) {
      return;
    }

    const cache = await caches.open(fileCacheName);
    for (const url of urls) {
      try {
        const request = new Request(url, { credentials: "same-origin" });
        const cached = await cache.match(request);
        if (cached) {
          continue;
        }

        const response = await fetch(request);
        if (response.ok) {
          await cache.put(request, response);
        }
      } catch {
        // Best-effort prefetch. A missing file should not block data sync.
      }
    }
  }

  function collectFileUrls(snapshot) {
    const urls = new Set();

    (snapshot.patients || []).forEach((patient) => addLocalUrl(urls, patient.photoUrl));
    (snapshot.labResults || []).forEach((lab) => addLocalUrl(urls, lab.fileUrl));
    addLocalUrl(urls, snapshot.user && snapshot.user.signatureUrl);
    addLocalUrl(urls, snapshot.prescriptionLayout && snapshot.prescriptionLayout.logoUrl);
    addLocalUrl(urls, snapshot.diagnosisLayout && snapshot.diagnosisLayout.logoUrl);

    return Array.from(urls);
  }

  async function cacheSnapshotRecordPages(snapshot) {
    if (!("serviceWorker" in navigator)) {
      return;
    }

    const urls = collectRecordPageUrls(snapshot);
    if (urls.length === 0) {
      return;
    }

    const registration = await navigator.serviceWorker.ready.catch(() => null);
    const worker = registration?.active;
    if (!worker || typeof MessageChannel === "undefined") {
      return;
    }

    await new Promise((resolve) => {
      const channel = new MessageChannel();
      const timeout = window.setTimeout(resolve, Math.max(10000, urls.length * 1000));
      channel.port1.onmessage = () => {
        window.clearTimeout(timeout);
        resolve();
      };

      worker.postMessage({
        type: "CACHE_URLS",
        urls
      }, [channel.port2]);
    });
  }

  function collectRecordPageUrls(snapshot) {
    const urls = new Set([
      "/",
      "/Patients",
      "/Records",
      "/Prescriptions",
      "/Reports",
      "/Sync",
      "/Settings"
    ]);

    (snapshot.patients || []).forEach((patient) => {
      if (!patient || !Number.isFinite(Number(patient.id))) {
        return;
      }

      urls.add(`/Records?patientId=${encodeURIComponent(patient.id)}`);
    });

    (snapshot.records || []).forEach((record) => {
      if (!record || !Number.isFinite(Number(record.patientId)) || !Number.isFinite(Number(record.id))) {
        return;
      }

      urls.add(`/Records?patientId=${encodeURIComponent(record.patientId)}&recordId=${encodeURIComponent(record.id)}`);
    });

    return Array.from(urls);
  }

  function notifyOfflineDataReady(snapshot) {
    document.dispatchEvent(new CustomEvent("medrec:offline-data-ready", {
      detail: {
        patientCount: Array.isArray(snapshot.patients) ? snapshot.patients.length : 0,
        recordCount: Array.isArray(snapshot.records) ? snapshot.records.length : 0,
        labCount: Array.isArray(snapshot.labResults) ? snapshot.labResults.length : 0
      }
    }));
  }

  function addLocalUrl(urls, value) {
    if (!value || typeof value !== "string") {
      return;
    }

    if (value.startsWith("/")) {
      urls.add(value);
    }
  }

  async function requestPersistentStorage() {
    if (!navigator.storage?.persist) {
      return;
    }

    try {
      await navigator.storage.persist();
    } catch {
      // Persistent storage is best-effort; offline cache still works without it.
    }
  }

  async function enqueuePost(form) {
    const formData = new FormData(form);
    const fields = [];
    const files = [];

    for (const [name, value] of formData.entries()) {
      if (name === "__RequestVerificationToken") {
        continue;
      }

      if (value instanceof File) {
        if (!value.name || value.size === 0) {
          continue;
        }

        files.push({
          name,
          fileName: value.name,
          type: value.type || "application/octet-stream",
          blob: value
        });
        continue;
      }

      fields.push({ name, value: String(value) });
    }

    const id = crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`;
    await put("postQueue", {
      id,
      action: form.action,
      method: form.method || "POST",
      enctype: form.enctype || "",
      fields,
      files,
      createdAtUtc: new Date().toISOString()
    });
    await notifyQueueChanged();
    return id;
  }

  async function getQueuedFile(queueId, fieldName) {
    if (!queueId) {
      return null;
    }

    const item = await get("postQueue", queueId);
    if (!item || !Array.isArray(item.files)) {
      return null;
    }

    return item.files.find((file) => !fieldName || file.name === fieldName) || null;
  }

  async function enqueueOperation(operation, files = []) {
    const firstFile = files.find((file) => file instanceof File && file.name && file.size > 0);
    const maxBytes = operation.type === "patient.upsert" ? 5 * 1024 * 1024 : 15 * 1024 * 1024;
    if (firstFile && firstFile.size > maxBytes) {
      throw new Error(operation.type === "patient.upsert"
        ? "Patient image must be 5 MB or smaller."
        : "Lab attachment must be 15 MB or smaller.");
    }
    let storedFiles = files
      .filter((file) => file instanceof File && file.name && file.size > 0)
      .map((file) => ({
        fileName: file.name,
        type: file.type || "application/octet-stream",
        blob: file
      }));
    if (storedFiles.length === 0) {
      const existing = await get("operations", operation.id);
      storedFiles = existing?.files || [];
    }
    await put("operations", {
      ...operation,
      files: storedFiles,
      createdAtUtc: operation.createdAtUtc || new Date().toISOString()
    });
    await notifyQueueChanged();
    return operation.id;
  }

  async function getOperationFile(operationId) {
    const operation = await get("operations", operationId);
    return operation?.files?.[0] || null;
  }

  async function replayOperations() {
    if (!navigator.onLine || replayInProgress) {
      return { sent: 0, remaining: await countQueuedOperations() };
    }

    const stored = (await getAll("operations"))
      .sort((left, right) => new Date(left.createdAtUtc) - new Date(right.createdAtUtc));
    if (stored.length === 0) {
      return { sent: 0, remaining: 0 };
    }

    replayInProgress = true;
    try {
      const operations = [];
      for (const item of stored) {
        const payload = { ...(item.payload || {}) };
        const file = item.files?.[0];
        if (file?.blob) {
          const encoded = await blobToBase64(file.blob);
          if (item.type === "patient.upsert") {
            payload.photoBase64 = encoded;
          } else if (item.type === "lab.upsert") {
            payload.fileBase64 = encoded;
            payload.fileName = file.fileName;
            payload.fileContentType = file.type;
          }
        }
        operations.push({
          id: item.id,
          type: item.type,
          entityUid: item.entityUid,
          baseUpdatedAt: item.baseUpdatedAt || null,
          payload
        });
      }

      const token = antiForgeryToken();
      const response = await fetch("/api/offline/push", {
        method: "POST",
        credentials: "same-origin",
        headers: {
          "Content-Type": "application/json",
          ...(token ? { "RequestVerificationToken": token } : {})
        },
        body: JSON.stringify({
          deviceId: getDeviceId(),
          operations
        })
      });
      if (!response.ok || response.url.includes("/Account/Login")) {
        return { sent: 0, remaining: stored.length };
      }

      const result = await response.json();
      const accepted = Array.isArray(result.acceptedOperationIds) ? result.acceptedOperationIds : [];
      for (const id of accepted) {
        await deleteItem("operations", id);
      }
      await notifyQueueChanged();
      const remaining = await countQueuedOperations();
      document.dispatchEvent(new CustomEvent("medrec:offline-operations-replayed", {
        detail: {
          sent: accepted.length,
          remaining,
          conflicts: result.conflicts || [],
          snapshot: result.snapshot || null
        }
      }));
      if (accepted.length > 0 && remaining === 0) {
        await refreshSnapshot();
      }
      return { sent: accepted.length, remaining };
    } catch {
      return { sent: 0, remaining: await countQueuedOperations() };
    } finally {
      replayInProgress = false;
    }
  }

  async function blobToBase64(blob) {
    const bytes = new Uint8Array(await blob.arrayBuffer());
    let binary = "";
    const chunkSize = 0x8000;
    for (let offset = 0; offset < bytes.length; offset += chunkSize) {
      binary += String.fromCharCode(...bytes.subarray(offset, offset + chunkSize));
    }
    return btoa(binary);
  }

  function getDeviceId() {
    const key = "medrec.deviceId";
    let id = localStorage.getItem(key);
    if (!id) {
      id = crypto.randomUUID ? crypto.randomUUID() : `web-${Date.now()}-${Math.random()}`;
      localStorage.setItem(key, id);
    }
    return id;
  }

  async function replayPostQueue() {
    if (!navigator.onLine || replayInProgress) {
      return { sent: 0, remaining: await countQueuedPosts() };
    }

    replayInProgress = true;
    const queue = await getAll("postQueue");
    let sent = 0;

    for (const item of queue) {
      try {
        const response = await fetch(item.action, {
          method: item.method || "POST",
          body: buildQueuedBody(item),
          credentials: "same-origin",
          headers: item.files && item.files.length > 0
            ? undefined
            : { "Content-Type": "application/x-www-form-urlencoded" }
        });

        const loginRedirect = response.url.includes("/Account/Login");
        if (response.ok && !loginRedirect) {
          await deleteItem("postQueue", item.id);
          sent += 1;
          await notifyQueueChanged();
        }
      } catch {
        replayInProgress = false;
        return { sent, remaining: await countQueuedPosts() };
      }
    }

    replayInProgress = false;
    const remaining = await countQueuedPosts();
    document.dispatchEvent(new CustomEvent("medrec:offline-queue-replayed", {
      detail: { sent, remaining }
    }));

    if (sent > 0 && remaining === 0) {
      await refreshSnapshot();
    }

    return { sent, remaining };
  }

  function buildQueuedBody(item) {
    const token = antiForgeryToken();

    if (item.files && item.files.length > 0) {
      const body = new FormData();
      item.fields.forEach((field) => body.append(field.name, field.value));
      if (token) {
        body.set("__RequestVerificationToken", token);
      }

      item.files.forEach((file) => {
        body.append(file.name, file.blob, file.fileName);
      });
      return body;
    }

    const body = new URLSearchParams();
    item.fields.forEach((field) => body.append(field.name, field.value));
    if (token) {
      body.set("__RequestVerificationToken", token);
    }
    return body;
  }

  function antiForgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
  }

  async function countQueuedPosts() {
    return (await getAll("postQueue")).length;
  }

  async function countQueuedOperations() {
    return (await getAll("operations")).length;
  }

  async function notifyQueueChanged() {
    document.dispatchEvent(new CustomEvent("medrec:offline-queue-changed", {
      detail: {
        queuedCount: await countQueuedPosts() + await countQueuedOperations()
      }
    }));
  }

  window.medrecOfflineStore = {
    refreshSnapshot,
    enqueuePost,
    getQueuedFile,
    enqueueOperation,
    getOperationFile,
    replayPostQueue,
    replayOperations,
    countQueuedPosts,
    getSnapshot: async () => {
      const stored = await get("snapshots", snapshotKey);
      return stored ? stored.data : null;
    }
  };

  window.addEventListener("online", async () => {
    await replayOperations();
    await replayPostQueue();
    await refreshSnapshot();
  });
  document.addEventListener("DOMContentLoaded", async () => {
    await notifyQueueChanged();
    await replayOperations();
    await replayPostQueue();
    await refreshSnapshot();
  });
  window.setInterval(() => {
    if (navigator.onLine) {
      replayOperations().then(() => replayPostQueue());
    }
  }, 30000);
})();
