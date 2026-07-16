(function () {
  if (!("serviceWorker" in navigator)) {
    return;
  }

  const reloadKey = "medrec.serviceWorkerReloaded";
  let offlineReady = false;
  let offlineDataStatus = "";
  let offlineQueueStatus = "";
  const offlineUrls = [
    "/",
    "/Patients",
    "/Records",
    "/Prescriptions",
    "/Reports",
    "/Sync",
    "/Settings"
  ];

  renderStatus();

  document.addEventListener("medrec:offline-data-ready", (event) => {
    const patientCount = event.detail?.patientCount || 0;
    const recordCount = event.detail?.recordCount || 0;
    offlineDataStatus = ` - ${patientCount} patients, ${recordCount} checkups cached`;
    renderStatus();
  });

  document.addEventListener("medrec:offline-queue-changed", (event) => {
    const queuedCount = event.detail?.queuedCount || 0;
    offlineQueueStatus = queuedCount > 0 ? ` - ${queuedCount} change${queuedCount === 1 ? "" : "s"} waiting to sync` : "";
    renderStatus();
  });

  document.addEventListener("medrec:offline-queue-replayed", (event) => {
    const remaining = event.detail?.remaining || 0;
    offlineQueueStatus = remaining > 0 ? ` - ${remaining} change${remaining === 1 ? "" : "s"} waiting to sync` : "";
    renderStatus();
  });

  navigator.serviceWorker.addEventListener("controllerchange", () => {
    if (sessionStorage.getItem(reloadKey) === "1") {
      return;
    }

    sessionStorage.setItem(reloadKey, "1");
    window.location.reload();
  });

  navigator.serviceWorker.register("/service-worker.js", { scope: "/" })
    .then(async (registration) => {
      if (registration.waiting) {
        registration.waiting.postMessage({ type: "SKIP_WAITING" });
      }

      await registration.update().catch(() => null);

      const ready = await navigator.serviceWorker.ready;
      ready.active?.postMessage({
        type: "CACHE_CURRENT_PAGE",
        url: window.location.pathname + window.location.search
      });
      await warmOfflinePages(ready.active);

      renderStatus();
      window.setTimeout(renderStatus, 1000);
    })
    .catch(() => null);

  function renderStatus() {
    const target = document.querySelector("[data-pwa-status]");
    if (!target) {
      return;
    }

    target.textContent = navigator.serviceWorker.controller
      ? offlineReady
        ? `Offline site ready${offlineDataStatus}${offlineQueueStatus}`
        : "Preparing offline site - keep this deployed link open once while online"
      : "Preparing offline site - keep this deployed link open once while online";
  }

  function warmOfflinePages(worker) {
    return new Promise((resolve) => {
      if (!worker || typeof MessageChannel === "undefined") {
        resolve();
        return;
      }

      const channel = new MessageChannel();
      const timeout = window.setTimeout(() => resolve(), 8000);
      channel.port1.onmessage = () => {
        window.clearTimeout(timeout);
        offlineReady = true;
        renderStatus();
        resolve();
      };

      worker.postMessage({
        type: "WARM_OFFLINE_PAGES",
        urls: offlineUrls
      }, [channel.port2]);
    });
  }
})();
