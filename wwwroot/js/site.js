(function () {
  window.fitPrintPreview = function fitPrintPreview(container) {
    if (!container) return;
    if (container.clientWidth < 100) return;
    const canvas = container.querySelector(".print-canvas");
    if (!canvas) return;
    const stage = canvas.parentElement;
    if (!stage) return;

    stage.classList.add("print-preview-stage");
    stage.style.width = "";
    stage.style.height = "";
    canvas.style.width = "210mm";
    canvas.style.height = "297mm";
    canvas.style.transform = "none";
    const paperWidth = canvas.offsetWidth || 794;
    const paperHeight = canvas.offsetHeight || 1123;
    const availableWidth = Math.max(220, container.clientWidth - 32);
    const availableHeight = Math.max(280, container.clientHeight - 32);
    const scale = Math.min(1, availableWidth / paperWidth, availableHeight / paperHeight);

    stage.style.width = `${Math.ceil(paperWidth * scale)}px`;
    stage.style.height = `${Math.ceil(paperHeight * scale)}px`;
    canvas.style.transform = `scale(${scale.toFixed(4)})`;
    canvas.style.transformOrigin = "top left";
  };

  window.printDocumentCanvas = function printDocumentCanvas(canvas) {
    if (!canvas) return;
    document.querySelector(".print-portal")?.remove();

    const portal = document.createElement("div");
    portal.className = "print-portal";
    const printable = canvas.cloneNode(true);
    printable.removeAttribute("id");
    printable.style.transform = "none";
    printable.style.width = "210mm";
    printable.style.height = "297mm";
    portal.append(printable);
    document.body.append(portal);
    document.body.classList.add("printing-document");

    const cleanup = () => {
      document.body.classList.remove("printing-document");
      portal.remove();
    };
    window.addEventListener("afterprint", cleanup, { once: true });
    window.print();
  };

  document.querySelectorAll("#prescriptionPreviewModal, #diagnosisPreviewModal").forEach((modal) => {
    modal.addEventListener("shown.bs.modal", () => {
      const container = modal.querySelector("#prescriptionPreviewContent, #diagnosisPreviewContent");
      requestAnimationFrame(() => window.fitPrintPreview(container));
    });
  });

  window.addEventListener("resize", () => {
    const modal = document.querySelector("#prescriptionPreviewModal.show, #diagnosisPreviewModal.show");
    if (!modal) return;
    const container = modal.querySelector("#prescriptionPreviewContent, #diagnosisPreviewContent");
    window.fitPrintPreview(container);
  });

  const connectionStatus = document.getElementById("connectionStatus");
  const localQueueKey = "medrec.localPostQueue";
  const legacyQueueKey = "medrec.offlineQueue";
  let serverReachable = navigator.onLine;

  function setConnectionState() {
    const online = navigator.onLine && serverReachable;
    const label = online ? "Online" : "Disconnected";

    if (connectionStatus) {
      connectionStatus.textContent = label;
      connectionStatus.classList.toggle("online", online);
      connectionStatus.classList.toggle("offline", !online);
    }
  }

  async function checkServerReachability() {
    if (!navigator.onLine) {
      serverReachable = false;
      setConnectionState();
      return false;
    }

    try {
      const controller = new AbortController();
      const timeout = window.setTimeout(() => controller.abort(), 3000);
      const response = await fetch(`/service-worker.js?health=${Date.now()}`, {
        cache: "no-store",
        credentials: "same-origin",
        signal: controller.signal
      });
      window.clearTimeout(timeout);
      serverReachable = response.ok;
    } catch {
      serverReachable = false;
    }

    setConnectionState();
    return serverReachable;
  }

  window.addEventListener("online", checkServerReachability);
  window.addEventListener("offline", () => {
    serverReachable = false;
    setConnectionState();
  });
  
  // Periodic polling to ensure status updates even if events don't fire
  setInterval(checkServerReachability, 10000);
  
  setConnectionState();
  checkServerReachability();
  migrateLegacyOfflineQueue();
  replayLocalPostQueue();

  document.querySelectorAll("[data-manual-sync]").forEach((button) => {
    button.addEventListener("click", () => {
      button.textContent = navigator.onLine ? "Sync Requested" : "Queued Locally";
      button.classList.add("disabled");
    });
  });

  const search = document.querySelector("[data-table-search]");
  const statusFilter = document.querySelector("[data-status-filter]");

  function filterPatients() {
    const needle = search ? search.value.trim().toLowerCase() : "";
    const status = statusFilter ? statusFilter.value : "";

    document.querySelectorAll(".patient-card").forEach((card) => {
      const matchesText = card.textContent.toLowerCase().includes(needle);
      const matchesStatus = !status || card.dataset.status === status;
      card.hidden = !matchesText || !matchesStatus;
    });
  }

  if (search) {
    search.addEventListener("input", filterPatients);
  }

  if (statusFilter) {
    statusFilter.addEventListener("change", filterPatients);
  }

  document.querySelectorAll("[data-camera-capable]").forEach((input) => {
    const fieldContainer = input.closest("div");
    const uploadGroup = input.closest(".photo-field") || input.closest(".form-grid") || input.closest("form");
    const browseButton = fieldContainer?.querySelector("[data-file-picker-button]");
    const cameraButton = fieldContainer?.querySelector("[data-camera-button]");

    if (browseButton) {
      browseButton.addEventListener("click", () => {
        input.removeAttribute("capture");
        input.click();
      });
    }

    if (cameraButton) {
      cameraButton.addEventListener("click", async () => {
        if (!navigator.mediaDevices?.getUserMedia) {
          input.setAttribute("capture", "environment");
          input.click();
          return;
        }

        try {
          await openCameraCapture(input);
        } catch (error) {
          window.alert(error.message || "Camera could not be opened.");
        }
      });
    }

    input.addEventListener("change", () => {
      input.removeAttribute("capture");
      const fileName = input.files.length > 0 ? input.files[0].name : "";
      const status = uploadGroup?.querySelector("[data-upload-file-name]");
      if (status) {
        status.textContent = fileName;
      }
    });
  });

  async function openCameraCapture(targetInput) {
    const overlay = ensureCameraOverlay();
    const video = overlay.querySelector("[data-camera-video]");
    const canvas = overlay.querySelector("[data-camera-canvas]");
    const captureButton = overlay.querySelector("[data-camera-capture]");
    const cancelButtons = overlay.querySelectorAll("[data-camera-close]");
    let stream;

    function closeCamera() {
      if (stream) {
        stream.getTracks().forEach((track) => track.stop());
        stream = null;
      }

      video.srcObject = null;
      overlay.classList.remove("show");
      document.body.classList.remove("camera-capture-open");
    }

    stream = await navigator.mediaDevices.getUserMedia({
      video: {
        facingMode: { ideal: "environment" },
        width: { ideal: 1280 },
        height: { ideal: 960 }
      },
      audio: false
    });

    video.srcObject = stream;
    overlay.classList.add("show");
    document.body.classList.add("camera-capture-open");
    await video.play();

    const capturePromise = new Promise((resolve) => {
      const capture = () => {
        const width = video.videoWidth || 1280;
        const height = video.videoHeight || 960;
        canvas.width = width;
        canvas.height = height;
        canvas.getContext("2d").drawImage(video, 0, 0, width, height);
        canvas.toBlob((blob) => {
          if (!blob) {
            resolve();
            return;
          }

          const file = new File([blob], `camera-${new Date().toISOString().replace(/[:.]/g, "-")}.jpg`, {
            type: "image/jpeg"
          });
          const transfer = new DataTransfer();
          transfer.items.add(file);
          targetInput.files = transfer.files;
          targetInput.dispatchEvent(new Event("change", { bubbles: true }));
          resolve();
        }, "image/jpeg", 0.92);
      };

      captureButton.addEventListener("click", capture, { once: true });
      cancelButtons.forEach((button) => {
        button.addEventListener("click", () => resolve(), { once: true });
      });
    });

    await capturePromise;
    closeCamera();
  }

  function ensureCameraOverlay() {
    let overlay = document.getElementById("cameraCaptureOverlay");
    if (overlay) {
      return overlay;
    }

    overlay = document.createElement("div");
    overlay.id = "cameraCaptureOverlay";
    overlay.className = "camera-capture-overlay";
    overlay.innerHTML = `
      <div class="camera-capture-dialog" role="dialog" aria-modal="true" aria-label="Camera capture">
        <div class="camera-capture-header">
          <strong>Camera</strong>
          <button class="btn btn-outline-secondary" type="button" data-camera-close>Close</button>
        </div>
        <video class="camera-capture-video" autoplay playsinline muted data-camera-video></video>
        <canvas class="d-none" data-camera-canvas></canvas>
        <div class="camera-capture-actions">
          <button class="btn btn-outline-secondary" type="button" data-camera-close>Cancel</button>
          <button class="btn btn-primary" type="button" data-camera-capture>Capture image</button>
        </div>
      </div>`;
    document.body.append(overlay);
    return overlay;
  }

  document.querySelectorAll("[data-sortable-table]").forEach((table) => {
    const body = table.tBodies[0];
    if (!body) return;
    const collator = new Intl.Collator(undefined, { sensitivity: "base", numeric: true });
    let activeColumn = -1;
    let direction = 1;

    table.querySelectorAll("[data-sort-column]").forEach((button) => {
      button.setAttribute("aria-sort", "none");
      button.addEventListener("click", () => {
        const column = Number(button.dataset.sortColumn);
        const type = button.dataset.sortType || "text";
        direction = activeColumn === column ? direction * -1 : 1;
        activeColumn = column;

        const rows = Array.from(body.rows).map((row, index) => ({ row, index }));
        rows.sort((left, right) => {
          const leftCell = left.row.cells[column];
          const rightCell = right.row.cells[column];
          const leftEmpty = leftCell.textContent.trim() === "-";
          const rightEmpty = rightCell.textContent.trim() === "-";
          if (leftEmpty !== rightEmpty) return leftEmpty ? 1 : -1;

          const leftValue = leftCell.dataset.sortValue ?? leftCell.textContent.trim();
          const rightValue = rightCell.dataset.sortValue ?? rightCell.textContent.trim();
          const comparison = type === "number"
            ? Number(leftValue) - Number(rightValue)
            : collator.compare(leftValue, rightValue);
          return comparison === 0 ? left.index - right.index : comparison * direction;
        });

        rows.forEach(({ row }) => body.append(row));
        table.querySelectorAll("[data-sort-column]").forEach((item) => item.setAttribute("aria-sort", "none"));
        button.setAttribute("aria-sort", direction === 1 ? "ascending" : "descending");
      });
    });
  });

  document.querySelectorAll("[data-confirm]").forEach((form) => {
    form.addEventListener("submit", (event) => {
      if (!window.confirm(form.dataset.confirm)) {
        event.preventDefault();
      }
    });
  });

  document.querySelectorAll('[data-bs-toggle="modal"][data-bs-target="#recordModal"], [data-bs-toggle="modal"][data-bs-target="#labModal"]').forEach((button) => {
    button.addEventListener("click", (event) => {
      const modalElement = document.querySelector(button.dataset.bsTarget || "");
      if (!modalElement || !window.bootstrap) {
        return;
      }

      event.preventDefault();
      bootstrap.Modal.getOrCreateInstance(modalElement).show();
    });
  });

  document.querySelectorAll(".edit-patient-button").forEach((button) => {
    button.addEventListener("click", () => {
      setField("EditPatient.Id", button.dataset.id);
      setField("EditPatient.FullName", button.dataset.fullName);
      setField("EditPatient.Age", button.dataset.age);
      setField("EditPatient.Address", button.dataset.address);
      setField("EditPatient.Sex", button.dataset.sex);
      setField("EditPatient.CivilStatus", button.dataset.civilStatus);
      setField("EditPatient.ContactNumber", button.dataset.contactNumber);
      setField("EditPatient.Occupation", button.dataset.occupation);
      setField("EditPatient.Company", button.dataset.company);
      setField("EditPatient.Email", button.dataset.email);
      setField("EditPatient.PartnerName", button.dataset.partnerName);
      setField("EditPatient.PartnerContactNumber", button.dataset.partnerContactNumber);
      setField("EditPatient.ReferredBy", button.dataset.referredBy);
      setField("EditPatient.AgeOfMenarche", button.dataset.ageOfMenarche);
      setField("EditPatient.MenopauseAge", button.dataset.menopauseAge);
      setField("EditPatient.PreviousMenstrualPeriod", button.dataset.previousMenstrualPeriod);
      setField("EditPatient.PeriodCycleDays", button.dataset.periodCycleDays);
      setField("EditPatient.PeriodDurationDays", button.dataset.periodDurationDays);
      setField("EditPatient.MenstrualAmount", button.dataset.menstrualAmount);
      setField("EditPatient.MenstrualPattern", button.dataset.menstrualPattern);
      setField("EditPatient.SexuallyActive", button.dataset.sexuallyActive);
      setField("EditPatient.ContraceptionMethod", button.dataset.contraceptionMethod);
      setField("EditPatient.HeightCm", button.dataset.heightCm);
      setField("EditPatient.WeightKg", button.dataset.weightKg);
      setField("EditPatient.BloodPressure", button.dataset.bloodPressure);
      setField("EditPatient.FetalHeartTone", button.dataset.fetalHeartTone);
      setField("EditPatient.LastMenstrualPeriod", button.dataset.lastMenstrualPeriod);
      setField("EditPatient.PhotoUrl", button.dataset.photoUrl);
    });
  });

  document.querySelectorAll(".archive-patient-button").forEach((button) => {
    button.addEventListener("click", () => {
      const modal = document.getElementById("archivePatientModal");
      if (!modal) {
        return;
      }

      const nameTarget = modal.querySelector("[data-archive-patient-name]");
      const idTarget = modal.querySelector("[data-archive-patient-id]");

      if (nameTarget) {
        nameTarget.textContent = button.dataset.patientName || "this patient";
      }

      if (idTarget) {
        idTarget.value = button.dataset.patientId || "";
      }
    });
  });

  document.querySelectorAll("[data-ob-patient-form]").forEach((form) => {
    const heightInput = form.querySelector("[data-height-input]");
    const weightInput = form.querySelector("[data-weight-input]");
    const lmpInput = form.querySelector("[data-lmp-input]");
    const bmiOutput = form.querySelector("[data-bmi-output]");
    const aogOutput = form.querySelector("[data-aog-output]");
    const eddOutput = form.querySelector("[data-edd-output]");
    const photoInput = form.querySelector("[data-photo-input]");
    const photoUrlInput = form.querySelector("[data-photo-url-input]");
    const photoPreview = form.querySelector("[data-photo-preview]");
    const photoPreviewEmpty = form.querySelector("[data-photo-preview-empty]");
    let previewObjectUrl = "";

    function updateObCalculations() {
      const height = Number.parseFloat(heightInput ? heightInput.value : "");
      const weight = Number.parseFloat(weightInput ? weightInput.value : "");

      if (bmiOutput) {
        bmiOutput.value = height > 0 && weight > 0
          ? (weight / ((height / 100) ** 2)).toFixed(1)
          : "";
      }

      if (!lmpInput || !lmpInput.value) {
        if (aogOutput) {
          aogOutput.value = "";
        }

        if (eddOutput) {
          eddOutput.value = "";
        }

        return;
      }

      const lmp = new Date(`${lmpInput.value}T00:00:00`);
      const today = new Date();
      today.setHours(0, 0, 0, 0);
      const days = Math.floor((today - lmp) / 86400000);

      if (aogOutput) {
        aogOutput.value = days >= 0 ? `${Math.floor(days / 7)}w ${days % 7}d` : "";
      }

      if (eddOutput) {
        const edd = new Date(lmp);
        edd.setDate(edd.getDate() + 280);
        eddOutput.value = edd.toLocaleDateString(undefined, { year: "numeric", month: "short", day: "numeric" });
      }
    }

    [heightInput, weightInput, lmpInput].forEach((input) => {
      if (input) {
        input.addEventListener("input", updateObCalculations);
        input.addEventListener("change", updateObCalculations);
      }
    });

    function updatePhotoPreview() {
      if (!photoPreview) {
        return;
      }

      const file = photoInput && photoInput.files.length > 0 ? photoInput.files[0] : null;
      const url = photoUrlInput ? photoUrlInput.value.trim() : "";

      if (previewObjectUrl) {
        URL.revokeObjectURL(previewObjectUrl);
        previewObjectUrl = "";
      }

      if (file) {
        previewObjectUrl = URL.createObjectURL(file);
        photoPreview.src = previewObjectUrl;
        photoPreview.classList.remove("d-none");
        if (photoPreviewEmpty) {
          photoPreviewEmpty.classList.add("d-none");
        }
        return;
      }

      if (url) {
        photoPreview.src = url;
        photoPreview.classList.remove("d-none");
        if (photoPreviewEmpty) {
          photoPreviewEmpty.classList.add("d-none");
        }
        return;
      }

      photoPreview.removeAttribute("src");
      photoPreview.classList.add("d-none");
      if (photoPreviewEmpty) {
        photoPreviewEmpty.classList.remove("d-none");
      }
    }

    if (photoInput) {
      photoInput.addEventListener("change", updatePhotoPreview);
    }

    if (photoUrlInput) {
      photoUrlInput.addEventListener("input", updatePhotoPreview);
      photoUrlInput.addEventListener("change", updatePhotoPreview);
    }

    updateObCalculations();
    updatePhotoPreview();
  });

  document.querySelectorAll("[data-print-layout-form]").forEach((form) => {
    const logoInput = form.querySelector("[data-logo-input]");
    const logoUrlInput = form.querySelector("[data-logo-url-input]");
    const logoPreview = form.querySelector("[data-logo-preview]");
    const logoPreviewEmpty = form.querySelector("[data-logo-preview-empty]");
    const layoutInput = form.querySelector("[data-layout-json-input]");
    const builder = form.querySelector("[data-layout-builder]");
    const blockSelect = form.querySelector("[data-block-select]");
    const visibleInput = form.querySelector("[data-block-visible]");
    const resetButton = form.querySelector("[data-reset-layout]");
    const deleteButton = form.querySelector("[data-delete-layout-element]");
    const textInput = form.querySelector("[data-block-text]");
    const imageUpload = form.querySelector("[data-layout-image-upload]");
    const fieldSelect = form.querySelector("[data-layout-field-select]");
    const addFieldButton = form.querySelector("[data-add-layout-field]");
    const addDoctorSignatureButton = form.querySelector("[data-add-doctor-signature]");
    const addSignatureDetailsButton = form.querySelector("[data-add-signature-details]");
    const propInputs = Array.from(form.querySelectorAll("[data-block-prop]"));
    let logoObjectUrl = "";
    let blocks = readLayoutBlocks().map(normalizeBlockShape);
    let activeBlockKey = blockSelect ? blockSelect.value : blocks[0] ? blocks[0].key : "";
    const originalBlocks = cloneBlocks(blocks);

    updateLogoPreview();
    renderLayoutBlocks();
    syncBlockControls();
    syncHiddenLayout();
    resizeLayoutEditor();

    form.addEventListener("submit", () => {
      syncSelectedTextValue();
      blocks.forEach(normalizeBlock);
      syncHiddenLayout();
    });

    if (blockSelect) {
      blockSelect.addEventListener("change", () => {
        syncSelectedTextValue(activeBlockKey);
        activeBlockKey = blockSelect.value;
        syncBlockControls();
        renderLayoutBlocks();
      });
    }

    propInputs.forEach((input) => {
      input.addEventListener("input", () => updateSelectedProperty(input));
      input.addEventListener("change", () => updateSelectedProperty(input));
    });

    if (textInput) {
      textInput.addEventListener("input", () => {
        syncSelectedTextValue();
      });
    }

    if (visibleInput) {
      visibleInput.addEventListener("change", () => {
        const block = selectedBlock();
        if (!block) {
          return;
        }

        block.isVisible = visibleInput.checked;
        renderLayoutBlocks();
        syncHiddenLayout();
      });
    }

    form.querySelectorAll("[data-add-layout-element]").forEach((button) => {
      button.addEventListener("click", () => {
        const type = button.dataset.addLayoutElement;

        if (type === "Image" && imageUpload) {
          imageUpload.click();
          return;
        }

        addElement(type);
      });
    });

    if (addFieldButton) {
      addFieldButton.addEventListener("click", () => {
        if (!fieldSelect || !fieldSelect.value) {
          return;
        }

        addLayoutField(fieldSelect.value);
      });
    }

    if (addDoctorSignatureButton) {
      addDoctorSignatureButton.addEventListener("click", () => addLayoutField("signatureImage"));
    }

    if (addSignatureDetailsButton) {
      addSignatureDetailsButton.addEventListener("click", () => addLayoutField("signature"));
    }

    if (imageUpload) {
      imageUpload.addEventListener("change", async () => {
        const file = imageUpload.files.length > 0 ? imageUpload.files[0] : null;
        if (!file) {
          return;
        }

        try {
          const imageUrl = await uploadLayoutImage(file);
          addElement("Image", imageUrl);
        } catch (error) {
          window.alert(error.message || "Image upload failed.");
        } finally {
          imageUpload.value = "";
        }
      });
    }

    if (deleteButton) {
      deleteButton.addEventListener("click", () => {
        const block = selectedBlock();
        if (!block) {
          return;
        }

        blocks = blocks.filter((item) => item.key !== block.key);
        selectBlock(blocks[0] ? blocks[0].key : "");
        renderLayoutBlocks();
        syncHiddenLayout();
      });
    }

    if (resetButton) {
      resetButton.addEventListener("click", () => {
        blocks = cloneBlocks(originalBlocks);
        selectBlock(blocks[0] ? blocks[0].key : "");
        renderLayoutBlocks();
        syncBlockControls();
        syncHiddenLayout();
      });
    }

    if (logoInput) {
      logoInput.addEventListener("change", updateLogoPreview);
    }

    if (logoUrlInput) {
      logoUrlInput.addEventListener("input", updateLogoPreview);
      logoUrlInput.addEventListener("change", updateLogoPreview);
    }

    form.querySelectorAll('[name$=".DocumentTitle"], [name$=".ClinicName"], [name$=".DoctorName"], [name$=".LicenseNumber"], [name$=".ClinicSchedule"], [name$=".ClinicAddress"], [name$=".SignatoryName"], [name$=".SignatoryTitle"]').forEach((input) => {
      input.addEventListener("input", renderLayoutBlocks);
      input.addEventListener("change", renderLayoutBlocks);
    });

    if (builder) {
      const modal = form.closest(".modal");
      if (modal) {
        modal.addEventListener("shown.bs.modal", resizeLayoutEditor);
      }

      window.addEventListener("resize", resizeLayoutEditor);

      builder.addEventListener("click", (event) => {
        const item = event.target.closest("[data-builder-block]");
        if (item) {
          selectBlock(item.dataset.builderBlock);
        }
      });

      builder.addEventListener("pointerdown", (event) => {
        const item = event.target.closest("[data-builder-block]");
        if (item) {
          const handle = event.target.closest("[data-resize-handle]");
          if (handle) {
            startBlockResize(event, item, handle.dataset.resizeHandle || "");
            return;
          }

          startBlockDrag(event, item);
        }
      });
    }

    function readLayoutBlocks() {
      if (!layoutInput) {
        return [];
      }

      try {
        const value = layoutInput.dataset.layoutJsonEncoded === "true"
          ? decodeBase64(layoutInput.value || "")
          : layoutInput.value;
        return JSON.parse(value || "[]");
      } catch {
        return [];
      }
    }

    function cloneBlocks(source) {
      return JSON.parse(JSON.stringify(source || []));
    }

    function selectedBlock() {
      return blocks.find((block) => block.key === activeBlockKey);
    }

    function selectBlock(key) {
      syncSelectedTextValue(activeBlockKey);

      if (blockSelect) {
        syncSelectOptions(key);
      } else {
        activeBlockKey = key || "";
      }

      syncBlockControls();
      renderLayoutBlocks();
    }

    function addElement(type, imageUrl) {
      const normalizedType = ["Text", "Line", "Image"].includes(type) ? type : "Text";
      const key = `${normalizedType.toLowerCase()}_${Date.now()}`;
      const count = blocks.filter((block) => block.type === normalizedType).length + 1;
      const block = normalizeBlockShape({
        key,
        type: normalizedType,
        label: `${normalizedType} ${count}`,
        text: normalizedType === "Text" ? "Text box" : "",
        imageUrl: imageUrl || "",
        x: 24,
        y: 76,
        width: normalizedType === "Line" ? 80 : 64,
        height: normalizedType === "Line" ? 4 : 22,
        fontSize: 12,
        lineWidth: 2,
        textAlign: "Left",
        fontWeight: "Normal",
        isVisible: true
      });

      blocks.push(block);
      selectBlock(block.key);
      renderLayoutBlocks();
      syncHiddenLayout();
      resizeLayoutEditor();
    }

    function addLayoutField(key) {
      if (blocks.some((block) => block.key === key)) {
        selectBlock(key);
        return;
      }

      const block = defaultFieldBlock(key);
      if (!block) {
        return;
      }

      blocks.push(normalizeBlockShape(block));
      selectBlock(key);
      renderLayoutBlocks();
      syncHiddenLayout();
      resizeLayoutEditor();
    }

    async function uploadLayoutImage(file) {
      const token = form.querySelector('input[name="__RequestVerificationToken"]');
      const body = new FormData();
      body.append("file", file);

      if (token) {
        body.append("__RequestVerificationToken", token.value);
      }

      const response = await fetch(form.dataset.layoutUploadUrl || "/Reports/UploadLayoutImage", {
        method: "POST",
        body,
        credentials: "same-origin"
      });

      if (!response.ok) {
        let message = "Image upload failed.";
        try {
          const error = await response.json();
          message = error.error || message;
        } catch {
          // Keep the generic message.
        }
        throw new Error(message);
      }

      const result = await response.json();
      return result.url;
    }

    function updateSelectedProperty(input) {
      const block = selectedBlock();
      if (!block) {
        return;
      }

      const prop = input.dataset.blockProp;
      if (["textAlign", "fontWeight"].includes(prop)) {
        block[prop] = input.value;
      } else {
        const value = Number.parseFloat(input.value);
        if (!Number.isFinite(value)) {
          return;
        }
        block[prop] = prop === "fontSize" || prop === "lineWidth" ? Math.round(value) : value;
      }

      normalizeBlock(block);
      renderLayoutBlocks();
      syncHiddenLayout();
    }

    function syncSelectedTextValue(key) {
      if (!textInput) {
        return;
      }

      const block = blocks.find((candidate) => candidate.key === (key || activeBlockKey));
      if (!block) {
        return;
      }

      if (block.type === "Image") {
        block.imageUrl = textInput.value.trim();
      } else if (block.type === "Text") {
        block.text = textInput.value;
      } else {
        return;
      }

      renderLayoutBlocks();
      syncHiddenLayout();
    }

    function syncBlockControls() {
      const block = selectedBlock();
      if (!block) {
        propInputs.forEach((input) => {
          input.value = "";
          input.disabled = true;
        });

        if (textInput) {
          textInput.value = "";
          textInput.disabled = true;
        }

        if (visibleInput) {
          visibleInput.checked = false;
          visibleInput.disabled = true;
        }

        if (deleteButton) {
          deleteButton.disabled = true;
        }

        return;
      }

      propInputs.forEach((input) => {
        input.value = block[input.dataset.blockProp] ?? "";
        input.disabled = false;
      });

      if (textInput) {
        textInput.value = block.type === "Image" ? block.imageUrl || "" : block.text || "";
        textInput.disabled = block.type === "Field" || block.type === "Line";
      }

      if (visibleInput) {
        visibleInput.checked = block.isVisible !== false;
        visibleInput.disabled = false;
      }

      if (deleteButton) {
        deleteButton.disabled = false;
      }
    }

    function renderLayoutBlocks() {
      if (!builder) {
        syncSelectOptions();
        syncFieldRestoreOptions();
        return;
      }

      syncSelectOptions();
      syncFieldRestoreOptions();
      const keys = new Set(blocks.map((block) => block.key));
      builder.querySelectorAll("[data-builder-block]").forEach((item) => {
        if (!keys.has(item.dataset.builderBlock)) {
          item.remove();
        }
      });

      blocks.forEach((block) => {
        normalizeBlock(block);
        let item = builder.querySelector(`[data-builder-block="${block.key}"]`);

        if (!item) {
          item = document.createElement("div");
          item.dataset.builderBlock = block.key;
          builder.append(item);
        }

        item.className = `layout-builder-block layout-builder-block-${block.type.toLowerCase()}`;
        item.style.left = `${block.x}mm`;
        item.style.top = `${block.y}mm`;
        item.style.width = `${block.width}mm`;
        item.style.height = `${block.height}mm`;
        item.style.fontSize = `${block.fontSize}px`;
        item.style.textAlign = block.textAlign.toLowerCase();
        item.style.fontWeight = block.fontWeight === "Bold" ? "800" : "400";
        item.classList.toggle("is-hidden", block.isVisible === false);
        item.classList.toggle("is-active", block.key === activeBlockKey);
        renderBuilderContent(item, block);
        addResizeHandles(item);
      });
    }

    function syncFieldRestoreOptions() {
      if (!fieldSelect || !addFieldButton) {
        return;
      }

      const existingKeys = new Set(blocks.map((block) => block.key));
      const missingFields = defaultFieldBlocks().filter((block) => !existingKeys.has(block.key));
      fieldSelect.replaceChildren();

      if (missingFields.length === 0) {
        const option = document.createElement("option");
        option.value = "";
        option.textContent = "All fields added";
        fieldSelect.append(option);
        fieldSelect.disabled = true;
        addFieldButton.disabled = true;
        return;
      }

      missingFields.forEach((block) => {
        const option = document.createElement("option");
        option.value = block.key;
        option.textContent = block.label;
        fieldSelect.append(option);
      });

      fieldSelect.disabled = false;
      addFieldButton.disabled = false;
    }

    function addResizeHandles(item) {
      ["nw", "n", "ne", "e", "se", "s", "sw", "w"].forEach((direction) => {
        const handle = document.createElement("span");
        handle.className = "layout-resize-handle";
        handle.dataset.resizeHandle = direction;
        item.append(handle);
      });
    }

    function syncSelectOptions(preferredKey) {
      if (!blockSelect) {
        return;
      }

      const selectedKey = preferredKey || blockSelect.value || (blocks[0] ? blocks[0].key : "");
      blockSelect.replaceChildren();

      blocks.forEach((block) => {
        const option = document.createElement("option");
        option.value = block.key;
        option.textContent = block.label || block.key;
        blockSelect.append(option);
      });

      if (blocks.some((block) => block.key === selectedKey)) {
        blockSelect.value = selectedKey;
        activeBlockKey = selectedKey;
      } else if (blocks[0]) {
        blockSelect.value = blocks[0].key;
        activeBlockKey = blocks[0].key;
      } else {
        activeBlockKey = "";
      }
    }

    function renderBuilderContent(item, block) {
      item.replaceChildren();
      const content = document.createElement("div");
      content.className = `layout-builder-content layout-field-${block.key}`;
      item.append(content);

      if (block.type === "Line") {
        const line = document.createElement("span");
        line.className = "builder-line";
        line.style.borderTopWidth = `${block.lineWidth}px`;
        content.append(line);
        return;
      }

      if (block.type === "Image") {
        if (block.imageUrl) {
          const image = document.createElement("img");
          image.src = block.imageUrl;
          image.alt = block.label || "Layout image";
          content.append(image);
        } else {
          content.textContent = "Image";
        }
        return;
      }

      if (block.type === "Text") {
        content.textContent = block.text || "Text box";
        return;
      }

      if (block.key === "logo") {
        updateBuilderLogoContent(content);
        return;
      }

      renderFieldSample(content, block.key);
    }

    function layoutFieldValue(name, fallback = "") {
      const input = form.querySelector(`[name$=".${name}"]`);
      return input && input.value.trim() ? input.value.trim() : fallback;
    }

    function appendElement(parent, tag, text, className = "") {
      const element = document.createElement(tag);
      element.textContent = text;
      if (className) element.className = className;
      parent.append(element);
      return element;
    }

    function renderFieldSample(content, key) {
      const documentType = builder ? builder.dataset.documentType : "";
      const isDiagnosis = documentType === "Diagnosis";

      if (key === "clinic") {
        appendElement(content, "strong", layoutFieldValue("ClinicName", "MedRec Clinic"));
        appendElement(content, "span", layoutFieldValue("DoctorName", "Dr. Cruz"));
        const license = layoutFieldValue("LicenseNumber");
        const address = layoutFieldValue("ClinicAddress");
        const schedule = layoutFieldValue("ClinicSchedule");
        if (license) appendElement(content, "span", `License No. ${license}`);
        if (address) appendElement(content, "span", address);
        if (schedule) appendElement(content, "span", schedule);
        return;
      }

      if (key === "title") {
        appendElement(content, "h2", layoutFieldValue("DocumentTitle", isDiagnosis ? "Medical Certificate" : "Prescription"));
        appendElement(content, "span", "Jun 19, 2026");
        return;
      }

      if (key === "patient") {
        const details = document.createElement("div");
        details.className = "print-patient-details";
        [
          ["Patient:", "Jomer Nobleza", "print-patient-name"],
          ["Date:", "Jun 19, 2026", "print-patient-date"],
          ["Address:", "Sample patient address", "print-patient-address"],
          ["Age:", "35", "print-patient-age"],
          ["Sex:", "Female", "print-patient-sex"]
        ].forEach(([label, value, className]) => {
          const detail = document.createElement("div");
          detail.className = `print-patient-detail ${className}`;
          appendElement(detail, "span", label);
          appendElement(detail, "strong", value);
          details.append(detail);
        });
        content.append(details);
        return;
      }

      if (key === "body") {
        if (isDiagnosis) {
          appendElement(content, "h3", "Diagnosis");
          appendElement(content, "p", "Sample diagnosis details");
          return;
        }

        const items = document.createElement("div");
        items.className = "prescription-print-items";
        [
          ["Biogesic", "500mg", "3x a Day", "7 days"],
          ["Neozep", "150mg", "1x a Day", "3 days"],
          ["Amlodipine", "1500mg", "5x a Day", "20 Days"]
        ].forEach((values) => {
          const row = document.createElement("div");
          row.className = "prescription-print-item";
          values.forEach((value, index) => appendElement(row, index === 0 ? "strong" : "span", value));
          items.append(row);
        });
        content.append(items);
        return;
      }

      if (key === "notes") {
        appendElement(content, "h3", isDiagnosis ? "Notes" : "Instructions");
        appendElement(content, "p", isDiagnosis ? "Sample clinical notes" : "Drink as needed");
        return;
      }

      if (key === "signatureImage") {
        const placeholder = document.createElement("div");
        placeholder.className = "signature-element-placeholder";
        placeholder.textContent = "Doctor signature image";
        content.append(placeholder);
        return;
      }

      if (key === "signature") {
        const line = document.createElement("div");
        line.className = "signature-line";
        content.append(line);
        appendElement(content, "strong", "Doctor Name");
        const license = layoutFieldValue("LicenseNumber");
        appendElement(content, "span", `License No. ${license || "PRC number"}`);
        return;
      }
    }

    function defaultFieldBlock(key) {
      return defaultFieldBlocks().find((block) => block.key === key);
    }

    function defaultFieldBlocks() {
      const documentType = builder ? builder.dataset.documentType : "";
      const isDiagnosis = documentType === "Diagnosis";

      return [
        { key: "logo", type: "Field", label: "Logo", x: 14, y: 12, width: 28, height: 24, fontSize: 11, lineWidth: 2, textAlign: "Left", fontWeight: "Normal", isVisible: true },
        { key: "clinic", type: "Field", label: "Clinic Details", x: 46, y: 12, width: 150, height: 28, fontSize: 12, lineWidth: 2, textAlign: "Left", fontWeight: "Normal", isVisible: true },
        { key: "title", type: "Field", label: "Title", x: 14, y: 46, width: 182, height: 16, fontSize: 18, lineWidth: 2, textAlign: "Left", fontWeight: "Bold", isVisible: true },
        { key: "patient", type: "Field", label: "Patient Details", x: 14, y: 68, width: 182, height: 30, fontSize: 11, lineWidth: 2, textAlign: "Left", fontWeight: "Normal", isVisible: true },
        { key: "body", type: "Field", label: isDiagnosis ? "Diagnosis" : "Prescription Details", x: 14, y: 106, width: 182, height: isDiagnosis ? 70 : 58, fontSize: 12, lineWidth: 2, textAlign: "Left", fontWeight: "Normal", isVisible: true },
        { key: "notes", type: "Field", label: isDiagnosis ? "Notes" : "Instructions", x: 14, y: isDiagnosis ? 184 : 172, width: 182, height: 46, fontSize: 11, lineWidth: 2, textAlign: "Left", fontWeight: "Normal", isVisible: true },
        { key: "signatureImage", type: "Field", label: "Doctor Signature Image", x: 133, y: 220, width: 48, height: 18, fontSize: 11, lineWidth: 2, textAlign: "Center", fontWeight: "Normal", isVisible: true },
        { key: "signature", type: "Field", label: "Signature Details", x: 118, y: 240, width: 78, height: 28, fontSize: 11, lineWidth: 2, textAlign: "Center", fontWeight: "Normal", isVisible: true }
      ];
    }

    function syncHiddenLayout() {
      if (layoutInput) {
        const json = JSON.stringify(blocks);
        layoutInput.value = layoutInput.dataset.layoutJsonEncoded === "true"
          ? encodeBase64(json)
          : json;
      }
    }

    function normalizeBlockShape(block) {
      const normalized = {};
      Object.keys(block || {}).forEach((key) => {
        normalized[key.charAt(0).toLowerCase() + key.slice(1)] = block[key];
      });

      normalized.key = normalized.key || `element_${Date.now()}`;
      normalized.type = normalizeType(normalized.type);
      normalized.label = normalized.label || normalized.type;
      normalized.text = normalized.text || "";
      normalized.imageUrl = normalized.imageUrl || "";
      normalized.x = normalized.x ?? 14;
      normalized.y = normalized.y ?? 14;
      normalized.width = normalized.width ?? 60;
      normalized.height = normalized.height ?? 24;
      normalized.fontSize = normalized.fontSize ?? 11;
      normalized.lineWidth = normalized.lineWidth ?? 2;
      normalized.textAlign = normalizeAlignment(normalized.textAlign);
      normalized.fontWeight = normalized.fontWeight === "Bold" ? "Bold" : "Normal";
      if (typeof normalized.isVisible !== "boolean") {
        normalized.isVisible = true;
      }

      return normalizeBlock(normalized);
    }

    function normalizeBlock(block) {
      block.type = normalizeType(block.type);
      block.x = clampNumber(block.x, 0, 205);
      block.y = clampNumber(block.y, 0, 292);
      block.width = clampNumber(block.width, block.type === "Line" ? 4 : 8, 210 - block.x);
      block.height = clampNumber(block.height, block.type === "Line" ? 2 : 8, 297 - block.y);
      block.fontSize = Math.round(clampNumber(block.fontSize, 8, 28));
      block.lineWidth = Math.round(clampNumber(block.lineWidth, 1, 12));
      block.textAlign = normalizeAlignment(block.textAlign);
      block.fontWeight = block.fontWeight === "Bold" ? "Bold" : "Normal";
      if (typeof block.isVisible !== "boolean") {
        block.isVisible = true;
      }
      return block;
    }

    function normalizeType(type) {
      return ["Field", "Text", "Line", "Image"].includes(type) ? type : "Field";
    }

    function normalizeAlignment(value) {
      return ["Left", "Center", "Right"].includes(value) ? value : "Left";
    }

    function clampNumber(value, min, max) {
      const number = Number.parseFloat(value);
      if (!Number.isFinite(number)) {
        return min;
      }

      return Math.min(Math.max(number, min), Math.max(min, max));
    }

    function startBlockDrag(event, item) {
      if (!builder || event.button !== 0) {
        return;
      }

      const block = blocks.find((candidate) => candidate.key === item.dataset.builderBlock);
      if (!block) {
        return;
      }

      event.preventDefault();
      selectBlock(block.key);

      const rect = builder.getBoundingClientRect();
      const pxPerMm = rect.width / 210;
      const startX = event.clientX;
      const startY = event.clientY;
      const initialX = block.x;
      const initialY = block.y;

      item.setPointerCapture(event.pointerId);

      function move(pointerEvent) {
        block.x = initialX + ((pointerEvent.clientX - startX) / pxPerMm);
        block.y = initialY + ((pointerEvent.clientY - startY) / pxPerMm);
        normalizeBlock(block);
        renderLayoutBlocks();
        syncBlockControls();
        syncHiddenLayout();
      }

      function stop(pointerEvent) {
        item.releasePointerCapture(pointerEvent.pointerId);
        item.removeEventListener("pointermove", move);
        item.removeEventListener("pointerup", stop);
        item.removeEventListener("pointercancel", stop);
      }

      item.addEventListener("pointermove", move);
      item.addEventListener("pointerup", stop);
      item.addEventListener("pointercancel", stop);
    }

    function startBlockResize(event, item, direction) {
      if (!builder || event.button !== 0 || !direction) {
        return;
      }

      const block = blocks.find((candidate) => candidate.key === item.dataset.builderBlock);
      if (!block) {
        return;
      }

      event.preventDefault();
      event.stopPropagation();
      selectBlock(block.key);

      const rect = builder.getBoundingClientRect();
      const pxPerMm = rect.width / 210;
      const startX = event.clientX;
      const startY = event.clientY;
      const initial = {
        x: block.x,
        y: block.y,
        width: block.width,
        height: block.height
      };

      item.setPointerCapture(event.pointerId);

      function move(pointerEvent) {
        const deltaX = (pointerEvent.clientX - startX) / pxPerMm;
        const deltaY = (pointerEvent.clientY - startY) / pxPerMm;
        const minWidth = block.type === "Line" ? 4 : 8;
        const minHeight = block.type === "Line" ? 2 : 8;
        const initialRight = initial.x + initial.width;
        const initialBottom = initial.y + initial.height;
        let nextX = initial.x;
        let nextY = initial.y;
        let nextWidth = initial.width;
        let nextHeight = initial.height;

        if (direction.includes("e")) {
          nextWidth = clampNumber(initial.width + deltaX, minWidth, 210 - nextX);
        }

        if (direction.includes("s")) {
          nextHeight = clampNumber(initial.height + deltaY, minHeight, 297 - nextY);
        }

        if (direction.includes("w")) {
          nextX = clampNumber(initial.x + deltaX, 0, initialRight - minWidth);
          nextWidth = initialRight - nextX;
        }

        if (direction.includes("n")) {
          nextY = clampNumber(initial.y + deltaY, 0, initialBottom - minHeight);
          nextHeight = initialBottom - nextY;
        }

        block.x = nextX;
        block.y = nextY;
        block.width = nextWidth;
        block.height = nextHeight;
        normalizeBlock(block);
        renderLayoutBlocks();
        syncBlockControls();
        syncHiddenLayout();
      }

      function stop(pointerEvent) {
        item.releasePointerCapture(pointerEvent.pointerId);
        item.removeEventListener("pointermove", move);
        item.removeEventListener("pointerup", stop);
        item.removeEventListener("pointercancel", stop);
      }

      item.addEventListener("pointermove", move);
      item.addEventListener("pointerup", stop);
      item.addEventListener("pointercancel", stop);
    }

    function resizeLayoutEditor() {
      if (!builder) {
        return;
      }

      const shell = builder.closest(".layout-builder-shell");
      if (!shell) {
        return;
      }

      shell.style.setProperty("--paper-scale", "1");
      const availableWidth = Math.max(shell.clientWidth - 24, 220);
      const availableHeight = Math.max(shell.clientHeight - 24, 280);
      const paperWidth = builder.offsetWidth || 794;
      const paperHeight = builder.offsetHeight || 1123;
      const scale = Math.min(1, availableWidth / paperWidth, availableHeight / paperHeight);
      shell.style.setProperty("--paper-scale", scale.toFixed(4));
      shell.style.setProperty("--paper-display-width", `${Math.ceil(paperWidth * scale)}px`);
      shell.style.setProperty("--paper-display-height", `${Math.ceil(paperHeight * scale)}px`);
    }

    function updateLogoPreview() {
      const file = logoInput && logoInput.files.length > 0 ? logoInput.files[0] : null;
      const url = logoUrlInput ? logoUrlInput.value.trim() : "";

      if (logoObjectUrl) {
        URL.revokeObjectURL(logoObjectUrl);
        logoObjectUrl = "";
      }

      if (file) {
        logoObjectUrl = URL.createObjectURL(file);
        setLogoPreview(logoObjectUrl);
        return;
      }

      setLogoPreview(url);
    }

    function setLogoPreview(src) {
      if (logoPreview) {
        if (src) {
          logoPreview.src = src;
          logoPreview.classList.remove("d-none");
        } else {
          logoPreview.removeAttribute("src");
          logoPreview.classList.add("d-none");
        }
      }

      if (logoPreviewEmpty) {
        logoPreviewEmpty.classList.toggle("d-none", Boolean(src));
      }

      const logoBlock = builder ? builder.querySelector('[data-builder-block="logo"]') : null;
      if (logoBlock) {
        updateBuilderLogoContent(logoBlock);
      }
    }

    function updateBuilderLogoContent(logoBlock) {
      const target = logoBlock.classList.contains("layout-builder-content")
        ? logoBlock
        : logoBlock.querySelector(".layout-builder-content") || logoBlock;

      target.replaceChildren();
      const file = logoInput && logoInput.files.length > 0 ? logoInput.files[0] : null;
      const src = file && logoObjectUrl ? logoObjectUrl : logoUrlInput ? logoUrlInput.value.trim() : "";

      if (src) {
        const image = document.createElement("img");
        image.src = src;
        image.alt = "Logo preview";
        target.append(image);
      } else {
        target.textContent = "Logo";
      }
    }
  });

  const attachCheckUpSelect = document.querySelector("[data-checkup-date-select]");
  const attachRequestedDate = document.querySelector("[data-requested-date-input]");
  const attachLabTitle = document.querySelector("[data-attach-lab-title]");

  function syncAttachRequestedDate() {
    if (!attachCheckUpSelect || !attachRequestedDate) {
      return;
    }

    const selectedOption = attachCheckUpSelect.options[attachCheckUpSelect.selectedIndex];
    const visitDate = selectedOption ? selectedOption.dataset.visitDate : "";

    if (visitDate) {
      attachRequestedDate.value = visitDate;
    }
  }

  if (attachCheckUpSelect) {
    attachCheckUpSelect.addEventListener("change", syncAttachRequestedDate);
  }

  document.querySelectorAll(".attach-lab-button").forEach((button) => {
    button.addEventListener("click", () => {
      setField("LabAttachment.LabId", button.dataset.labId);
      setField("LabAttachment.PatientId", button.dataset.patientId);

      if (attachLabTitle) {
        attachLabTitle.value = button.dataset.labTitle || "";
      }

      syncAttachRequestedDate();
    });
  });

  document.querySelectorAll("[data-prescription-drugs]").forEach((section) => {
    const list = section.querySelector("[data-prescription-drug-list]");
    const template = section.querySelector("[data-prescription-drug-template]");
    const addButton = section.querySelector("[data-add-prescription-drug]");

    if (!list || !template) {
      return;
    }

    function drugRows() {
      return Array.from(list.querySelectorAll("[data-prescription-drug-row]"));
    }

    function reindexDrugRows() {
      const rows = drugRows();

      rows.forEach((row, index) => {
        const rowTitle = row.querySelector("[data-drug-row-title]");
        if (rowTitle) {
          rowTitle.textContent = `Drug ${index + 1}`;
        }

        row.querySelectorAll("[data-drug-field]").forEach((field) => {
          const fieldName = field.dataset.drugField;
          field.name = `NewPrescription.Items[${index}].${fieldName}`;
          field.id = `NewPrescription_Items_${index}__${fieldName}`;
        });

        row.querySelectorAll("[data-drug-label]").forEach((label) => {
          const fieldName = label.dataset.drugLabelField;
          label.htmlFor = `NewPrescription_Items_${index}__${fieldName}`;
        });

        row.querySelectorAll("[data-drug-validation-field]").forEach((message) => {
          const fieldName = message.dataset.drugValidationField;
          message.setAttribute("data-valmsg-for", `NewPrescription.Items[${index}].${fieldName}`);
        });
      });

      rows.forEach((row) => {
        const removeButton = row.querySelector("[data-remove-prescription-drug]");
        if (removeButton) {
          removeButton.disabled = rows.length === 1;
        }
      });
    }

    if (addButton) {
      addButton.addEventListener("click", () => {
        list.append(template.content.cloneNode(true));
        reindexDrugRows();
      });
    }

    list.addEventListener("click", (event) => {
      const removeButton = event.target.closest("[data-remove-prescription-drug]");
      if (!removeButton) {
        return;
      }

      const rows = drugRows();
      if (rows.length <= 1) {
        return;
      }

      removeButton.closest("[data-prescription-drug-row]").remove();
      reindexDrugRows();
    });

    reindexDrugRows();
  });

  document.querySelectorAll("[data-prescription-patient-search]").forEach((patientSearch) => {
    const form = patientSearch.closest("form");
    if (!form) {
      return;
    }

    const patientIdField = form.querySelector("[data-prescription-patient-id]");
    const checkupSelect = form.querySelector("[data-prescription-checkup-select]");
    const wrapper = patientSearch.closest("[data-searchable-select]");
    const menu = wrapper ? wrapper.querySelector("[data-searchable-select-menu]") : null;
    const options = menu ? Array.from(menu.querySelectorAll("[data-prescription-patient-option]")) : [];

    if (!patientIdField || !menu || options.length === 0) {
      return;
    }

    function matchingPatientOption() {
      const searchValue = patientSearch.value.trim().toLocaleLowerCase();
      return options.find((option) => (option.dataset.patientName || "").trim().toLocaleLowerCase() === searchValue);
    }

    function setMenuOpen(isOpen) {
      menu.classList.toggle("show", isOpen);
      patientSearch.setAttribute("aria-expanded", isOpen ? "true" : "false");
    }

    function filterPatientOptions() {
      const searchValue = patientSearch.value.trim().toLocaleLowerCase();
      let visibleCount = 0;

      options.forEach((option) => {
        const name = (option.dataset.patientName || "").toLocaleLowerCase();
        const isVisible = !searchValue || name.includes(searchValue);
        option.hidden = !isVisible;
        if (isVisible) {
          visibleCount += 1;
        }
      });

      menu.classList.toggle("is-empty", visibleCount === 0);
    }

    function filterCheckups() {
      if (!checkupSelect) {
        return;
      }

      const selectedPatientId = patientIdField.value;
      let selectedCheckupVisible = false;

      Array.from(checkupSelect.options).forEach((option) => {
        if (!option.value) {
          option.hidden = false;
          option.disabled = false;
          return;
        }

        const isPatientCheckup = option.dataset.patientId === selectedPatientId;
        option.hidden = !isPatientCheckup;
        option.disabled = !isPatientCheckup;

        if (isPatientCheckup && option.selected) {
          selectedCheckupVisible = true;
        }
      });

      if (!selectedCheckupVisible) {
        checkupSelect.value = "";
      }
    }

    function syncSelectedPatient() {
      const option = matchingPatientOption();
      patientIdField.value = option ? option.dataset.patientId || "" : "";
      filterPatientOptions();
      filterCheckups();
    }

    function choosePatient(option) {
      patientSearch.value = option.dataset.patientName || "";
      patientIdField.value = option.dataset.patientId || "";
      filterPatientOptions();
      filterCheckups();
      setMenuOpen(false);
    }

    patientSearch.addEventListener("focus", () => {
      filterPatientOptions();
      setMenuOpen(true);
    });

    patientSearch.addEventListener("click", () => {
      filterPatientOptions();
      setMenuOpen(true);
    });

    patientSearch.addEventListener("input", () => {
      syncSelectedPatient();
      setMenuOpen(true);
    });

    patientSearch.addEventListener("change", syncSelectedPatient);

    patientSearch.addEventListener("keydown", (event) => {
      if (event.key === "Escape") {
        setMenuOpen(false);
      }
    });

    options.forEach((option) => {
      option.addEventListener("click", () => choosePatient(option));
    });

    document.addEventListener("click", (event) => {
      if (wrapper && !wrapper.contains(event.target)) {
        setMenuOpen(false);
      }
    });

    if (patientIdField.value) {
      const selectedOption = options.find((option) => option.dataset.patientId === patientIdField.value);
      if (selectedOption) {
        patientSearch.value = selectedOption.dataset.patientName || patientSearch.value;
      }
      filterPatientOptions();
      filterCheckups();
    } else {
      syncSelectedPatient();
    }
  });

  document.querySelectorAll(".print-form").forEach((form) => {
    form.addEventListener("submit", async (event) => {
      event.preventDefault();
      const sheet = form.closest("[data-prescription-sheet]");
      beginPrintMode("printing-prescription", sheet);

      try {
        await fetch(form.action, {
          method: "POST",
          body: new FormData(form),
          credentials: "same-origin"
        });
      } finally {
        window.print();
      }
    });
  });

  document.querySelectorAll("[data-print-target]").forEach((button) => {
    button.addEventListener("click", () => {
      syncCertificatePrint();
      const target = document.getElementById(button.dataset.printTarget || "");
      beginPrintMode("printing-target", target);
      window.print();
    });
  });

  const openModalId = document.body.dataset.openModal;
  if (openModalId && window.bootstrap) {
    const modalElement = document.getElementById(openModalId);
    if (modalElement) {
      bootstrap.Modal.getOrCreateInstance(modalElement).show();
    }
  }

  const pdfFrame = document.getElementById("labPdfFrame");
  const labImageFrame = document.getElementById("labImageFrame");
  const pdfEmpty = document.getElementById("labPdfEmpty");
  const pdfTitle = document.getElementById("pdfTitle");
  const pdfPatient = document.getElementById("pdfPatient");

  document.querySelectorAll("[data-lab-url]").forEach((button) => {
    button.addEventListener("click", () => {
      const url = button.dataset.labUrl;
      const kind = button.dataset.labKind;

      document.querySelectorAll("[data-lab-url]").forEach((item) => item.classList.remove("active"));
      button.classList.add("active");

      if (pdfTitle) {
        pdfTitle.textContent = button.dataset.labTitle || "Lab result";
      }

      if (pdfPatient) {
        pdfPatient.textContent = button.dataset.labPatient || "";
      }

      if (!pdfFrame || !url) {
        if (pdfFrame) {
          pdfFrame.classList.add("d-none");
          pdfFrame.src = "about:blank";
        }

        if (labImageFrame) {
          labImageFrame.classList.add("d-none");
          labImageFrame.removeAttribute("src");
        }

        if (pdfEmpty) {
          pdfEmpty.classList.remove("d-none");
          pdfEmpty.textContent = "This lab has no file.";
        }

        return;
      }

      if (kind === "image" && labImageFrame) {
        labImageFrame.src = url;
        labImageFrame.classList.remove("d-none");
        pdfFrame.classList.add("d-none");
        pdfFrame.src = "about:blank";
      } else {
        pdfFrame.src = pdfViewerUrl(url);
        pdfFrame.classList.remove("d-none");
        if (labImageFrame) {
          labImageFrame.classList.add("d-none");
          labImageFrame.removeAttribute("src");
        }
      }

      if (pdfEmpty) {
        pdfEmpty.classList.add("d-none");
      }
    });
  });

  document.querySelectorAll("form[data-offline-form]").forEach((form) => {
    form.addEventListener("submit", async (event) => {
      if (navigator.onLine && serverReachable) {
        return;
      }

      event.preventDefault();
      const formData = new FormData(form);
      const hasFile = Array.from(form.querySelectorAll("input[type='file']")).some((input) => input.files.length > 0);

      const fields = Array.from(formData.entries())
        .filter(([name, value]) => typeof value === "string" && name !== "__RequestVerificationToken")
        .map(([name, value]) => ({ name, value }));

      try {
        if (window.medrecOfflineStore && typeof window.medrecOfflineStore.enqueuePost === "function") {
          await window.medrecOfflineStore.enqueuePost(form);
          showLocalFlash(hasFile
            ? "Saved locally with its file. It will upload when the connection returns."
            : "Saved locally on this browser. It will send when the connection returns.");
          form.reset();
          return;
        }

        if (hasFile) {
          showLocalFlash("Files cannot be saved locally in this browser. Reconnect before submitting this upload.", "error");
          return;
        }

        enqueueLocalPostItem({ action: form.action, method: form.method || "POST", fields });
        showLocalFlash("Saved locally on this browser. It will send when the connection returns.");
        form.reset();
      } catch (error) {
        showLocalFlash(error.message || "Local save failed. Keep this page open and try again.", "error");
      }
    });
  });

  window.addEventListener("online", replayLocalPostQueue);
  window.addEventListener("online", () => window.medrecOfflineStore?.replayPostQueue?.());

  function antiForgeryToken() {
    return document.querySelector('input[name="__RequestVerificationToken"]')?.value || "";
  }

  function enqueueLocalPostItem(item) {
    const queue = readLocalPostQueue();
    if (queue.length >= 200) {
      throw new Error("The local save queue is full. Reconnect and sync pending changes first.");
    }

    queue.push({
      id: crypto.randomUUID ? crypto.randomUUID() : `${Date.now()}-${Math.random()}`,
      action: item.action,
      method: item.method || "POST",
      fields: item.fields,
      createdAtUtc: new Date().toISOString()
    });
    writeLocalPostQueue(queue);
  }

  function migrateLegacyOfflineQueue() {
    let legacyItems;
    try {
      legacyItems = JSON.parse(localStorage.getItem(legacyQueueKey) || "[]");
    } catch {
      legacyItems = [];
    }

    if (!Array.isArray(legacyItems) || legacyItems.length === 0) {
      localStorage.removeItem(legacyQueueKey);
      return;
    }

    const queue = readLocalPostQueue();
    legacyItems.forEach((item) => {
      const fields = (item.fields || [])
        .filter(([name, value]) => typeof value === "string" && name !== "__RequestVerificationToken")
        .map(([name, value]) => ({ name, value }));
      queue.push({
        id: item.id || `${Date.now()}-${Math.random()}`,
        action: item.action,
        method: item.method || "POST",
        fields,
        createdAtUtc: item.createdAtUtc || new Date().toISOString()
      });
    });
    writeLocalPostQueue(queue);
    localStorage.removeItem(legacyQueueKey);
  }

  async function replayLocalPostQueue() {
    if (!navigator.onLine) {
      return;
    }

    const queue = readLocalPostQueue();
    if (!Array.isArray(queue) || queue.length === 0) return;

    const remaining = [];
    for (const item of queue) {
      try {
        const body = new URLSearchParams();
        item.fields.forEach((field) => body.append(field.name, field.value));
        body.set("__RequestVerificationToken", antiForgeryToken());

        const response = await fetch(item.action, {
          method: item.method,
          body,
          credentials: "same-origin",
          headers: { "Content-Type": "application/x-www-form-urlencoded" }
        });

        const loginRedirect = response.url.includes("/Account/Login");
        const accepted = response.status === 204 || (response.ok && response.redirected && !loginRedirect);
        if (!accepted) remaining.push(item);
      } catch {
        remaining.push(item);
      }
    }

    writeLocalPostQueue(remaining);
    showLocalFlash(remaining.length === 0
      ? "Locally saved changes were sent."
      : "Some locally saved changes are still waiting for connection.");
  }

  function readLocalPostQueue() {
    try {
      const queue = JSON.parse(localStorage.getItem(localQueueKey) || "[]");
      return Array.isArray(queue) ? queue : [];
    } catch {
      return [];
    }
  }

  function writeLocalPostQueue(queue) {
    if (!queue.length) {
      localStorage.removeItem(localQueueKey);
      return;
    }

    localStorage.setItem(localQueueKey, JSON.stringify(queue));
  }

  function showLocalFlash(message, type = "success") {
    const main = document.querySelector("main");
    if (!main) {
      return;
    }

    const flash = document.createElement("div");
    flash.className = `flash ${type}`;
    flash.textContent = message;
    main.prepend(flash);
    window.setTimeout(() => flash.remove(), 5000);
  }

  function setField(name, value) {
    const field = document.querySelector(`[name="${name}"]`);
    if (field) {
      field.value = value || "";
      field.dispatchEvent(new Event("input", { bubbles: true }));
      field.dispatchEvent(new Event("change", { bubbles: true }));
    }
  }

  function pdfViewerUrl(url) {
    const baseUrl = url.split("#")[0];
    return `${baseUrl}#toolbar=0&navpanes=0&scrollbar=1&view=FitH`;
  }

  function encodeBase64(value) {
    return btoa(unescape(encodeURIComponent(value)));
  }

  function decodeBase64(value) {
    return value ? decodeURIComponent(escape(atob(value))) : "";
  }

  function beginPrintMode(mode, target) {
    cleanupPrintMode();

    if (target) {
      target.classList.add("is-printing");
    }

    document.body.classList.add(mode);
    window.addEventListener("afterprint", cleanupPrintMode, { once: true });
  }

  function cleanupPrintMode() {
    document.body.classList.remove("printing-prescription", "printing-target");
    document.querySelectorAll(".is-printing").forEach((item) => item.classList.remove("is-printing"));
  }

  function syncCertificatePrint() {
    const diagnosis = document.getElementById("Diagnosis");
    const notes = document.getElementById("Notes");
    const diagnosisTarget = document.querySelector("[data-certificate-diagnosis]");
    const notesTarget = document.querySelector("[data-certificate-notes]");

    if (diagnosis && diagnosisTarget) {
      diagnosisTarget.textContent = diagnosis.value;
    }

    if (notes && notesTarget) {
      notesTarget.textContent = notes.value;
    }
  }
})();
