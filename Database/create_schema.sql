-- =====================================================================
-- Barcode Inspection: database schema
-- Rebuilt from the SQL used by the application.
--
-- Run it against the qa_db database:
--   psql -U postgres -h localhost -p 5432 -d qa_db -f create_schema.sql
-- The script can be run again safely: it only creates what is missing.
-- =====================================================================

CREATE SCHEMA IF NOT EXISTS barcode_inspection;
SET search_path = barcode_inspection;

-- ---------------------------------------------------------------------
-- Product master: barcode format and which checks each product needs
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS product_spec (
    product_name      varchar(100) PRIMARY KEY,
    item_code         varchar(100),
    barcode_spec      varchar(200) NOT NULL,           -- '@' = any character
    hinge_spec        varchar(200),
    quantity          integer      NOT NULL CHECK (quantity > 0),  -- parts per carton
    modulus_34        boolean      NOT NULL DEFAULT false,
    modulus_36        boolean      NOT NULL DEFAULT false,
    dimension_check   boolean      NOT NULL DEFAULT false,
    electrical_check  boolean      NOT NULL DEFAULT false,
    appearance_check  boolean      NOT NULL DEFAULT false,
    hinge_check       boolean      NOT NULL DEFAULT false,
    cmos_check        boolean      NOT NULL DEFAULT false,
    compare_check     boolean      NOT NULL DEFAULT false,
    machine_check     boolean      NOT NULL DEFAULT false,
    running_check     boolean      NOT NULL DEFAULT false
);

-- ---------------------------------------------------------------------
-- Running part number per work order, one counter table per record table.
-- The app deletes a counter when its work order has no records left.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS pass_part_counter       (work_order varchar(50) PRIMARY KEY, last_part_no integer NOT NULL);
CREATE TABLE IF NOT EXISTS fail_part_counter       (work_order varchar(50) PRIMARY KEY, last_part_no integer NOT NULL);
CREATE TABLE IF NOT EXISTS dimension_part_counter  (work_order varchar(50) PRIMARY KEY, last_part_no integer NOT NULL);
CREATE TABLE IF NOT EXISTS appearance_part_counter (work_order varchar(50) PRIMARY KEY, last_part_no integer NOT NULL);

-- Fills part_no on insert. The counter table name is passed as the trigger argument.
CREATE OR REPLACE FUNCTION assign_part_no() RETURNS trigger
LANGUAGE plpgsql AS $$
DECLARE
    next_no integer;
BEGIN
    IF NEW.part_no IS NULL THEN
        EXECUTE format(
            'INSERT INTO barcode_inspection.%I AS c (work_order, last_part_no) VALUES ($1, 1)
             ON CONFLICT (work_order) DO UPDATE SET last_part_no = c.last_part_no + 1
             RETURNING last_part_no', TG_ARGV[0])
        INTO next_no
        USING NEW.work_order;

        NEW.part_no := next_no;
    END IF;
    RETURN NEW;
END;
$$;

-- ---------------------------------------------------------------------
-- Final inspection results
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS record_barcode_pass (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_no           integer,
    product_name      varchar(100) NOT NULL REFERENCES product_spec (product_name) ON UPDATE CASCADE,
    work_order        varchar(50)  NOT NULL,
    barcode           varchar(100) NOT NULL,
    barcode_hinge     varchar(100),
    carton_box_no     integer      NOT NULL,
    line_no           integer      NOT NULL,
    operator_id       varchar(50)  NOT NULL,
    inspection_date   date         NOT NULL DEFAULT CURRENT_DATE,
    inspection_time   time         NOT NULL DEFAULT LOCALTIME,
    electrical_result boolean,
    dimension_result  boolean,
    appearance_result boolean,
    hinge_max_15      numeric(12,4),
    hinge_avg_15      numeric(12,4),
    hinge_max_120     numeric(12,4),
    hinge_avg_120     numeric(12,4),
    hinge_angle       numeric(12,4),
    hinge_vendor      varchar(100),
    hinge_date        date,
    mc_1_result       boolean,
    mc_2_result       boolean,
    remark            text,
    CONSTRAINT uq_pass_barcode UNIQUE (barcode)      -- a part can only pass once
);

-- Same columns as the pass table; the same barcode may fail many times.
CREATE TABLE IF NOT EXISTS record_barcode_fail (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_no           integer,
    product_name      varchar(100) NOT NULL REFERENCES product_spec (product_name) ON UPDATE CASCADE,
    work_order        varchar(50)  NOT NULL,
    barcode           varchar(100) NOT NULL,
    barcode_hinge     varchar(100),
    carton_box_no     integer      NOT NULL,
    line_no           integer      NOT NULL,
    operator_id       varchar(50)  NOT NULL,
    inspection_date   date         NOT NULL DEFAULT CURRENT_DATE,
    inspection_time   time         NOT NULL DEFAULT LOCALTIME,
    electrical_result boolean,
    dimension_result  boolean,
    appearance_result boolean,
    hinge_max_15      numeric(12,4),
    hinge_avg_15      numeric(12,4),
    hinge_max_120     numeric(12,4),
    hinge_avg_120     numeric(12,4),
    hinge_angle       numeric(12,4),
    hinge_vendor      varchar(100),
    hinge_date        date,
    mc_1_result       boolean,
    mc_2_result       boolean,
    remark            text
);

-- ---------------------------------------------------------------------
-- Dimension and appearance stations (checked by the final inspection)
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS record_barcode_dimension (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_no           integer,
    product_name      varchar(100) NOT NULL REFERENCES product_spec (product_name) ON UPDATE CASCADE,
    work_order        varchar(50)  NOT NULL,
    barcode           varchar(100) NOT NULL,
    barcode_hinge     varchar(100),
    line_no           integer      NOT NULL,
    operator_id       varchar(50)  NOT NULL,
    inspection_date   date         NOT NULL DEFAULT CURRENT_DATE,
    inspection_time   time         NOT NULL DEFAULT LOCALTIME,
    dimension_result  boolean      NOT NULL,
    remark            text
);

CREATE TABLE IF NOT EXISTS record_barcode_appearance (
    id                bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    part_no           integer,
    product_name      varchar(100) NOT NULL REFERENCES product_spec (product_name) ON UPDATE CASCADE,
    work_order        varchar(50)  NOT NULL,
    barcode           varchar(100) NOT NULL,
    barcode_hinge     varchar(100),
    line_no           integer      NOT NULL,
    operator_id       varchar(50)  NOT NULL,
    inspection_date   date         NOT NULL DEFAULT CURRENT_DATE,
    inspection_time   time         NOT NULL DEFAULT LOCALTIME,
    appearance_result boolean      NOT NULL,
    remark            text
);

-- ---------------------------------------------------------------------
-- Parts packed into a combined (mixed work order) carton
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS record_combined_carton (
    id                 bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    combined_carton_no integer      NOT NULL,
    product_name       varchar(100) NOT NULL REFERENCES product_spec (product_name) ON UPDATE CASCADE,
    work_order         varchar(50)  NOT NULL,
    barcode            varchar(100) NOT NULL,
    operator_id        varchar(50)  NOT NULL,
    combined_date      date         NOT NULL DEFAULT CURRENT_DATE,
    combined_time      time         NOT NULL DEFAULT LOCALTIME,
    CONSTRAINT uq_combined_barcode UNIQUE (barcode)
);

-- ---------------------------------------------------------------------
-- Part number triggers
-- ---------------------------------------------------------------------
DROP TRIGGER IF EXISTS trg_pass_part_no ON record_barcode_pass;
CREATE TRIGGER trg_pass_part_no BEFORE INSERT ON record_barcode_pass
    FOR EACH ROW EXECUTE FUNCTION assign_part_no('pass_part_counter');

DROP TRIGGER IF EXISTS trg_fail_part_no ON record_barcode_fail;
CREATE TRIGGER trg_fail_part_no BEFORE INSERT ON record_barcode_fail
    FOR EACH ROW EXECUTE FUNCTION assign_part_no('fail_part_counter');

DROP TRIGGER IF EXISTS trg_dimension_part_no ON record_barcode_dimension;
CREATE TRIGGER trg_dimension_part_no BEFORE INSERT ON record_barcode_dimension
    FOR EACH ROW EXECUTE FUNCTION assign_part_no('dimension_part_counter');

DROP TRIGGER IF EXISTS trg_appearance_part_no ON record_barcode_appearance;
CREATE TRIGGER trg_appearance_part_no BEFORE INSERT ON record_barcode_appearance
    FOR EACH ROW EXECUTE FUNCTION assign_part_no('appearance_part_counter');

-- ---------------------------------------------------------------------
-- Indexes for the lookups the app runs on every scan
-- ---------------------------------------------------------------------
CREATE INDEX IF NOT EXISTS ix_pass_carton      ON record_barcode_pass (product_name, work_order, carton_box_no);
CREATE INDEX IF NOT EXISTS ix_pass_date        ON record_barcode_pass (inspection_date);
CREATE INDEX IF NOT EXISTS ix_pass_barcode_like ON record_barcode_pass (barcode varchar_pattern_ops);  -- base-barcode LIKE 'ABC%'
CREATE INDEX IF NOT EXISTS ix_fail_barcode     ON record_barcode_fail (barcode);
CREATE INDEX IF NOT EXISTS ix_fail_wo          ON record_barcode_fail (product_name, work_order);
CREATE INDEX IF NOT EXISTS ix_dimension_barcode ON record_barcode_dimension (barcode);
CREATE INDEX IF NOT EXISTS ix_dimension_wo     ON record_barcode_dimension (product_name, work_order);
CREATE INDEX IF NOT EXISTS ix_appearance_barcode ON record_barcode_appearance (barcode);
CREATE INDEX IF NOT EXISTS ix_appearance_wo    ON record_barcode_appearance (product_name, work_order);
CREATE INDEX IF NOT EXISTS ix_combined_carton  ON record_combined_carton (product_name, combined_carton_no);

-- ---------------------------------------------------------------------
-- Settings shared by every PC. The admin password is stored only as a salted
-- PBKDF2-SHA256 hash; the initial password is 12345678. Change it from the
-- Setting tab ("Change Password") right after the first login.
-- ---------------------------------------------------------------------
CREATE TABLE IF NOT EXISTS app_setting (
    key        varchar(100) PRIMARY KEY,
    value      text         NOT NULL,
    updated_at timestamptz  NOT NULL DEFAULT now()
);

-- Only sets the initial password when none exists, so running this again never resets a changed password.
INSERT INTO app_setting (key, value)
VALUES ('admin_password_hash', 'pbkdf2-sha256$100000$7CTyizKD00UTH2jTLye+Uw==$THqbiEuM9k6oEEwHiyTo8QNoYUOqlH9NwZ3+1/u72MM=')
ON CONFLICT (key) DO NOTHING;
