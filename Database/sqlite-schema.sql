CREATE TABLE IF NOT EXISTS users (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  client_uid TEXT NOT NULL UNIQUE DEFAULT (lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6)))),
  full_name TEXT NOT NULL,
  email TEXT NOT NULL UNIQUE,
  password_hash TEXT NOT NULL,
  role TEXT NOT NULL DEFAULT 'Doctor',
  specialty TEXT NOT NULL DEFAULT '',
  license_number TEXT NOT NULL DEFAULT '',
  contact_number TEXT NOT NULL DEFAULT '',
  signature_url TEXT NULL,
  is_active INTEGER NOT NULL DEFAULT 1,
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  last_synced_at TEXT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS patients (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  client_uid TEXT NOT NULL UNIQUE,
  patient_number TEXT NOT NULL UNIQUE,
  full_name TEXT NOT NULL,
  age INTEGER NOT NULL,
  address TEXT NOT NULL DEFAULT '',
  sex TEXT NOT NULL DEFAULT '',
  civil_status TEXT NOT NULL DEFAULT '',
  contact_number TEXT NOT NULL DEFAULT '',
  occupation TEXT NOT NULL DEFAULT '',
  company TEXT NOT NULL DEFAULT '',
  email TEXT NOT NULL DEFAULT '',
  partner_name TEXT NOT NULL DEFAULT '',
  partner_contact_number TEXT NOT NULL DEFAULT '',
  referred_by TEXT NOT NULL DEFAULT '',
  age_of_menarche INTEGER NULL,
  menopause_age INTEGER NULL,
  previous_menstrual_period TEXT NULL,
  period_cycle_days INTEGER NULL,
  period_duration_days INTEGER NULL,
  menstrual_amount TEXT NOT NULL DEFAULT '',
  menstrual_pattern TEXT NOT NULL DEFAULT '',
  sexually_active INTEGER NULL,
  contraception_method TEXT NOT NULL DEFAULT '',
  height_cm REAL NULL,
  weight_kg REAL NULL,
  blood_pressure TEXT NOT NULL DEFAULT '',
  fetal_heart_tone TEXT NOT NULL DEFAULT '',
  last_menstrual_period TEXT NULL,
  photo_url TEXT NULL,
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  last_synced_at TEXT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  archived_at TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_patients_name ON patients (full_name);
CREATE INDEX IF NOT EXISTS ix_patients_updated_sync ON patients (updated_at, sync_status);

CREATE TABLE IF NOT EXISTS clinical_records (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  client_uid TEXT NOT NULL UNIQUE,
  patient_id INTEGER NOT NULL REFERENCES patients(id),
  doctor_id INTEGER NULL REFERENCES users(id),
  visit_date TEXT NOT NULL,
  height_cm REAL NULL,
  weight_kg REAL NULL,
  blood_pressure TEXT NOT NULL DEFAULT '',
  fetal_heart_rate TEXT NOT NULL DEFAULT '',
  temperature_c REAL NULL,
  chief_complaint TEXT NOT NULL,
  diagnosis TEXT NOT NULL,
  notes TEXT NOT NULL,
  doctor_name TEXT NOT NULL,
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  last_synced_at TEXT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_clinical_records_patient_visit ON clinical_records (patient_id, visit_date);
CREATE INDEX IF NOT EXISTS ix_clinical_records_updated_sync ON clinical_records (updated_at, sync_status);

CREATE TABLE IF NOT EXISTS lab_results (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  client_uid TEXT NOT NULL UNIQUE,
  patient_id INTEGER NOT NULL REFERENCES patients(id),
  clinical_record_id INTEGER NULL REFERENCES clinical_records(id),
  test_name TEXT NOT NULL,
  requested_date TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  result_date TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'Uploaded',
  file_url TEXT NOT NULL,
  notes TEXT NULL,
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_lab_results_patient_requested ON lab_results (patient_id, requested_date);

CREATE TABLE IF NOT EXISTS prescriptions (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  client_uid TEXT NOT NULL UNIQUE,
  patient_id INTEGER NOT NULL REFERENCES patients(id),
  clinical_record_id INTEGER NULL REFERENCES clinical_records(id),
  issued_at TEXT NOT NULL,
  medication TEXT NOT NULL,
  dosage TEXT NOT NULL,
  frequency TEXT NOT NULL,
  duration TEXT NOT NULL,
  instructions TEXT NULL,
  prescriber TEXT NOT NULL,
  print_count INTEGER NOT NULL DEFAULT 0,
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_prescriptions_patient_issued ON prescriptions (patient_id, issued_at);

CREATE TABLE IF NOT EXISTS prescription_items (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  prescription_id INTEGER NOT NULL REFERENCES prescriptions(id) ON DELETE CASCADE,
  medication TEXT NOT NULL,
  dosage TEXT NOT NULL,
  frequency TEXT NOT NULL,
  duration TEXT NOT NULL,
  sort_order INTEGER NOT NULL DEFAULT 0,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_prescription_items_prescription_order ON prescription_items (prescription_id, sort_order);

CREATE TABLE IF NOT EXISTS print_layouts (
  id INTEGER PRIMARY KEY,
  document_type TEXT NOT NULL DEFAULT 'Prescription',
  document_title TEXT NOT NULL DEFAULT 'Prescription',
  clinic_name TEXT NOT NULL DEFAULT 'MedRec Clinic',
  doctor_name TEXT NOT NULL DEFAULT 'Dr. Cruz',
  license_number TEXT NOT NULL DEFAULT '',
  clinic_schedule TEXT NOT NULL DEFAULT '',
  clinic_address TEXT NOT NULL DEFAULT '',
  logo_url TEXT NULL,
  logo_position TEXT NOT NULL DEFAULT 'Left',
  details_alignment TEXT NOT NULL DEFAULT 'Left',
  signatory_name TEXT NOT NULL DEFAULT 'Dr. Cruz',
  signatory_title TEXT NOT NULL DEFAULT 'OB-Gyne',
  layout_json TEXT NULL,
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  last_synced_at TEXT NULL,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  UNIQUE (document_type)
);

INSERT OR IGNORE INTO print_layouts
  (id, document_type, document_title, clinic_name, doctor_name, signatory_name, signatory_title, logo_position, details_alignment)
VALUES
  (1, 'Prescription', 'Prescription', 'MedRec Clinic', 'Dr. Cruz', 'Dr. Cruz', 'OB-Gyne', 'Left', 'Left'),
  (2, 'Diagnosis', 'Medical Certificate', 'MedRec Clinic', 'Dr. Cruz', 'Dr. Cruz', 'OB-Gyne', 'Left', 'Left');

CREATE TABLE IF NOT EXISTS appointments (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  client_uid TEXT NOT NULL UNIQUE DEFAULT (lower(hex(randomblob(4))) || '-' || lower(hex(randomblob(2))) || '-4' || substr(lower(hex(randomblob(2))), 2) || '-' || substr('89ab', abs(random()) % 4 + 1, 1) || substr(lower(hex(randomblob(2))), 2) || '-' || lower(hex(randomblob(6)))),
  patient_id INTEGER NOT NULL REFERENCES patients(id),
  scheduled_at TEXT NOT NULL,
  reason TEXT NOT NULL,
  status TEXT NOT NULL DEFAULT 'Scheduled',
  sync_status TEXT NOT NULL DEFAULT 'Pending',
  last_synced_at TEXT NULL,
  created_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP
);

CREATE INDEX IF NOT EXISTS ix_appointments_scheduled_status ON appointments (scheduled_at, status);

CREATE TABLE IF NOT EXISTS sync_queue (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  device_id TEXT NOT NULL DEFAULT 'local-device',
  entity_type TEXT NOT NULL,
  entity_uid TEXT NOT NULL,
  operation TEXT NOT NULL,
  payload_json TEXT NULL,
  file_path TEXT NULL,
  updated_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  synced_at TEXT NULL,
  status TEXT NOT NULL DEFAULT 'Pending',
  retry_count INTEGER NOT NULL DEFAULT 0,
  last_error TEXT NULL
);

CREATE INDEX IF NOT EXISTS ix_sync_queue_status_updated ON sync_queue (status, updated_at);
CREATE INDEX IF NOT EXISTS ix_sync_queue_entity ON sync_queue (entity_type, entity_uid);

CREATE TABLE IF NOT EXISTS sync_runs (
  id INTEGER PRIMARY KEY AUTOINCREMENT,
  sync_type TEXT NOT NULL,
  started_at TEXT NOT NULL DEFAULT CURRENT_TIMESTAMP,
  finished_at TEXT NULL,
  records_uploaded INTEGER NOT NULL DEFAULT 0,
  files_uploaded INTEGER NOT NULL DEFAULT 0,
  status TEXT NOT NULL DEFAULT 'Running',
  message TEXT NULL
);
