-- =====================================================================
-- Barcode Inspection: admin password setup (for databases created before this script existed)
--   psql -U postgres -h localhost -p 5432 -d qa_db -f 002_admin_password.sql
-- Safe to run more than once.
-- =====================================================================

-- ---------------------------------------------------------------------
-- Settings shared by every PC. The admin password is stored only as a salted
-- PBKDF2-SHA256 hash; the initial password is 12345678. Change it from the
-- Setting tab ("Change Password") right after the first login.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS barcode_inspection.app_setting (
    key        varchar(100) PRIMARY KEY,
    value      text         NOT NULL,
    updated_at timestamptz  NOT NULL DEFAULT now()
);

-- Only sets the initial password when none exists, so running this again never resets a changed password.
INSERT INTO barcode_inspection.app_setting (key, value)
VALUES ('admin_password_hash', 'pbkdf2-sha256$100000$7CTyizKD00UTH2jTLye+Uw==$THqbiEuM9k6oEEwHiyTo8QNoYUOqlH9NwZ3+1/u72MM=')
ON CONFLICT (key) DO NOTHING;
