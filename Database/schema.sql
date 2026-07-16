CREATE DATABASE IF NOT EXISTS medrec
  CHARACTER SET utf8mb4
  COLLATE utf8mb4_unicode_ci;

USE medrec;

CREATE TABLE IF NOT EXISTS users (
  id INT AUTO_INCREMENT PRIMARY KEY,
  full_name VARCHAR(160) NOT NULL,
  email VARCHAR(190) NOT NULL UNIQUE,
  password_hash VARCHAR(255) NOT NULL,
  role ENUM('Admin', 'Doctor', 'Nurse', 'Staff') NOT NULL DEFAULT 'Doctor',
  specialty VARCHAR(160) NOT NULL DEFAULT '',
  license_number VARCHAR(80) NOT NULL DEFAULT '',
  contact_number VARCHAR(80) NOT NULL DEFAULT '',
  signature_url VARCHAR(500) NULL,
  is_active BOOLEAN NOT NULL DEFAULT TRUE,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP
);

CREATE TABLE IF NOT EXISTS patients (
  id INT AUTO_INCREMENT PRIMARY KEY,
  client_uid CHAR(36) NOT NULL,
  patient_number VARCHAR(40) NOT NULL UNIQUE,
  full_name VARCHAR(180) NOT NULL,
  age INT NOT NULL,
  address VARCHAR(255) NOT NULL DEFAULT '',
  sex VARCHAR(20) NOT NULL DEFAULT '',
  civil_status VARCHAR(80) NOT NULL DEFAULT '',
  contact_number VARCHAR(40) NOT NULL DEFAULT '',
  occupation VARCHAR(120) NOT NULL DEFAULT '',
  company VARCHAR(160) NOT NULL DEFAULT '',
  email VARCHAR(190) NOT NULL DEFAULT '',
  partner_name VARCHAR(180) NOT NULL DEFAULT '',
  partner_contact_number VARCHAR(40) NOT NULL DEFAULT '',
  referred_by VARCHAR(180) NOT NULL DEFAULT '',
  age_of_menarche INT NULL,
  menopause_age INT NULL,
  previous_menstrual_period DATE NULL,
  period_cycle_days INT NULL,
  period_duration_days INT NULL,
  menstrual_amount VARCHAR(80) NOT NULL DEFAULT '',
  menstrual_pattern VARCHAR(20) NOT NULL DEFAULT '',
  sexually_active BOOLEAN NULL,
  contraception_method VARCHAR(180) NOT NULL DEFAULT '',
  height_cm DECIMAL(6,2) NULL,
  weight_kg DECIMAL(6,2) NULL,
  blood_pressure VARCHAR(40) NOT NULL DEFAULT '',
  fetal_heart_tone VARCHAR(40) NOT NULL DEFAULT '',
  last_menstrual_period DATE NULL,
  photo_url VARCHAR(500) NULL,
  sync_status ENUM('Synced', 'Pending', 'Failed') NOT NULL DEFAULT 'Pending',
  last_synced_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  archived_at DATETIME NULL,
  UNIQUE KEY ux_patients_client_uid (client_uid),
  INDEX ix_patients_name (full_name),
  INDEX ix_patients_updated_sync (updated_at, sync_status)
);

CREATE TABLE IF NOT EXISTS clinical_records (
  id INT AUTO_INCREMENT PRIMARY KEY,
  client_uid CHAR(36) NOT NULL,
  patient_id INT NOT NULL,
  doctor_id INT NULL,
  visit_date DATETIME NOT NULL,
  height_cm DECIMAL(6,2) NULL,
  weight_kg DECIMAL(6,2) NULL,
  blood_pressure VARCHAR(40) NOT NULL DEFAULT '',
  fetal_heart_rate VARCHAR(40) NOT NULL DEFAULT '',
  temperature_c DECIMAL(5,2) NULL,
  chief_complaint VARCHAR(255) NOT NULL,
  diagnosis VARCHAR(255) NOT NULL,
  notes TEXT NOT NULL,
  doctor_name VARCHAR(160) NOT NULL,
  sync_status ENUM('Synced', 'Pending', 'Failed') NOT NULL DEFAULT 'Pending',
  last_synced_at DATETIME NULL,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY ux_clinical_records_client_uid (client_uid),
  CONSTRAINT fk_clinical_records_patient FOREIGN KEY (patient_id) REFERENCES patients(id),
  CONSTRAINT fk_clinical_records_doctor FOREIGN KEY (doctor_id) REFERENCES users(id),
  INDEX ix_clinical_records_patient_visit (patient_id, visit_date),
  INDEX ix_clinical_records_updated_sync (updated_at, sync_status)
);

CREATE TABLE IF NOT EXISTS lab_results (
  id INT AUTO_INCREMENT PRIMARY KEY,
  client_uid CHAR(36) NOT NULL,
  patient_id INT NOT NULL,
  clinical_record_id INT NULL,
  test_name VARCHAR(180) NOT NULL,
  requested_date DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  result_date DATETIME NOT NULL,
  status ENUM('Uploaded', 'Reviewed', 'Archived') NOT NULL DEFAULT 'Uploaded',
  file_url VARCHAR(500) NOT NULL,
  notes TEXT NULL,
  sync_status ENUM('Synced', 'Pending', 'Failed') NOT NULL DEFAULT 'Pending',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY ux_lab_results_client_uid (client_uid),
  CONSTRAINT fk_lab_results_patient FOREIGN KEY (patient_id) REFERENCES patients(id),
  CONSTRAINT fk_lab_results_record FOREIGN KEY (clinical_record_id) REFERENCES clinical_records(id),
  INDEX ix_lab_results_patient_requested (patient_id, requested_date)
);

CREATE TABLE IF NOT EXISTS prescriptions (
  id INT AUTO_INCREMENT PRIMARY KEY,
  client_uid CHAR(36) NOT NULL,
  patient_id INT NOT NULL,
  clinical_record_id INT NULL,
  issued_at DATETIME NOT NULL,
  medication VARCHAR(180) NOT NULL,
  dosage VARCHAR(120) NOT NULL,
  frequency VARCHAR(120) NOT NULL,
  duration VARCHAR(120) NOT NULL,
  instructions TEXT NULL,
  prescriber VARCHAR(160) NOT NULL,
  print_count INT NOT NULL DEFAULT 0,
  sync_status ENUM('Synced', 'Pending', 'Failed') NOT NULL DEFAULT 'Pending',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY ux_prescriptions_client_uid (client_uid),
  CONSTRAINT fk_prescriptions_patient FOREIGN KEY (patient_id) REFERENCES patients(id),
  CONSTRAINT fk_prescriptions_record FOREIGN KEY (clinical_record_id) REFERENCES clinical_records(id),
  INDEX ix_prescriptions_patient_issued (patient_id, issued_at)
);

CREATE TABLE IF NOT EXISTS prescription_items (
  id INT AUTO_INCREMENT PRIMARY KEY,
  prescription_id INT NOT NULL,
  medication VARCHAR(180) NOT NULL,
  dosage VARCHAR(120) NOT NULL,
  frequency VARCHAR(120) NOT NULL,
  duration VARCHAR(120) NOT NULL,
  sort_order INT NOT NULL DEFAULT 0,
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  CONSTRAINT fk_prescription_items_prescription FOREIGN KEY (prescription_id) REFERENCES prescriptions(id) ON DELETE CASCADE,
  INDEX ix_prescription_items_prescription_order (prescription_id, sort_order)
);

INSERT INTO prescription_items
  (prescription_id, medication, dosage, frequency, duration, sort_order)
SELECT p.id, p.medication, p.dosage, p.frequency, p.duration, 0
FROM prescriptions p
WHERE NOT EXISTS (
  SELECT 1
  FROM prescription_items pi
  WHERE pi.prescription_id = p.id
);

CREATE TABLE IF NOT EXISTS print_layouts (
  id INT PRIMARY KEY,
  document_type VARCHAR(40) NOT NULL DEFAULT 'Prescription',
  document_title VARCHAR(120) NOT NULL DEFAULT 'Prescription',
  clinic_name VARCHAR(180) NOT NULL DEFAULT 'MedRec Clinic',
  doctor_name VARCHAR(160) NOT NULL DEFAULT 'Dr. Cruz',
  license_number VARCHAR(80) NOT NULL DEFAULT '',
  clinic_schedule VARCHAR(255) NOT NULL DEFAULT '',
  clinic_address VARCHAR(255) NOT NULL DEFAULT '',
  logo_url VARCHAR(500) NULL,
  logo_position ENUM('Left', 'Center', 'Right') NOT NULL DEFAULT 'Left',
  details_alignment ENUM('Left', 'Center', 'Right') NOT NULL DEFAULT 'Left',
  signatory_name VARCHAR(160) NOT NULL DEFAULT 'Dr. Cruz',
  signatory_title VARCHAR(120) NOT NULL DEFAULT 'OB-Gyne',
  layout_json JSON NULL,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  UNIQUE KEY ux_print_layouts_document_type (document_type)
);

INSERT INTO print_layouts
  (id, document_type, document_title, clinic_name, doctor_name, signatory_name, signatory_title, logo_position, details_alignment)
VALUES
  (1, 'Prescription', 'Prescription', 'MedRec Clinic', 'Dr. Cruz', 'Dr. Cruz', 'OB-Gyne', 'Left', 'Left')
ON DUPLICATE KEY UPDATE id = id;

INSERT INTO print_layouts
  (id, document_type, document_title, clinic_name, doctor_name, signatory_name, signatory_title, logo_position, details_alignment)
VALUES
  (2, 'Diagnosis', 'Medical Certificate', 'MedRec Clinic', 'Dr. Cruz', 'Dr. Cruz', 'OB-Gyne', 'Left', 'Left')
ON DUPLICATE KEY UPDATE id = id;

CREATE TABLE IF NOT EXISTS appointments (
  id INT AUTO_INCREMENT PRIMARY KEY,
  patient_id INT NOT NULL,
  scheduled_at DATETIME NOT NULL,
  reason VARCHAR(255) NOT NULL,
  status ENUM('Scheduled', 'CheckedIn', 'Completed', 'Cancelled') NOT NULL DEFAULT 'Scheduled',
  created_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,
  CONSTRAINT fk_appointments_patient FOREIGN KEY (patient_id) REFERENCES patients(id),
  INDEX ix_appointments_scheduled_status (scheduled_at, status)
);

CREATE TABLE IF NOT EXISTS sync_queue (
  id INT AUTO_INCREMENT PRIMARY KEY,
  device_id VARCHAR(120) NOT NULL DEFAULT 'local-device',
  entity_type VARCHAR(80) NOT NULL,
  entity_id INT NOT NULL,
  operation ENUM('Create', 'Update', 'Delete') NOT NULL,
  payload_json JSON NULL,
  updated_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  synced_at DATETIME NULL,
  status ENUM('Pending', 'Synced', 'Failed') NOT NULL DEFAULT 'Pending',
  retry_count INT NOT NULL DEFAULT 0,
  INDEX ix_sync_queue_status_updated (status, updated_at),
  INDEX ix_sync_queue_entity (entity_type, entity_id)
);

CREATE TABLE IF NOT EXISTS sync_runs (
  id INT AUTO_INCREMENT PRIMARY KEY,
  sync_type ENUM('Daily', 'Manual') NOT NULL,
  started_at DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  finished_at DATETIME NULL,
  records_uploaded INT NOT NULL DEFAULT 0,
  status ENUM('Running', 'Completed', 'Failed') NOT NULL DEFAULT 'Running',
  message VARCHAR(255) NULL
);

-- The application creates the configured AdminLogin and DoctorLogin accounts on first login.
-- Passwords are stored as salted PBKDF2 hashes; additional doctors are created in Administration.
