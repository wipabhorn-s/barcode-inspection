-- =====================================================================
-- Barcode Inspection: sample data for trying the program
--   psql -U postgres -h localhost -p 5432 -d qa_db -f 003_sample_data.sql
--
-- Everything here is made up. All sample products start with DEMO- and all work orders with WO-DEMO-.
-- Running the script again resets the sample data; it never touches other products.
-- =====================================================================

SET search_path = barcode_inspection;

-- Start from a clean sample set
DELETE FROM record_combined_carton    WHERE product_name LIKE 'DEMO-%';
DELETE FROM record_barcode_pass       WHERE product_name LIKE 'DEMO-%';
DELETE FROM record_barcode_fail       WHERE product_name LIKE 'DEMO-%';
DELETE FROM record_barcode_dimension  WHERE product_name LIKE 'DEMO-%';
DELETE FROM record_barcode_appearance WHERE product_name LIKE 'DEMO-%';
DELETE FROM pass_part_counter       WHERE work_order LIKE 'WO-DEMO-%';
DELETE FROM fail_part_counter       WHERE work_order LIKE 'WO-DEMO-%';
DELETE FROM dimension_part_counter  WHERE work_order LIKE 'WO-DEMO-%';
DELETE FROM appearance_part_counter WHERE work_order LIKE 'WO-DEMO-%';
DELETE FROM product_spec WHERE product_name LIKE 'DEMO-%';

-- ---------------------------------------------------------------------
-- Products
--   DEMO-BASIC    only the barcode format is checked, 5 parts per carton
--   DEMO-HINGE    a hinge/CMOS barcode must be scanned with every part, 3 per carton
--   DEMO-RUNNING  running numbers 00001-00050 only; others ask Rework / Reject, 10 per carton
-- '@' in a format means "any character".
-- ---------------------------------------------------------------------
INSERT INTO product_spec (product_name, item_code, barcode_spec, hinge_spec, quantity, cmos_check, running_check) VALUES
    ('DEMO-BASIC',   'ITEM-100', 'DB@@@@@', NULL,    5,  FALSE, FALSE),
    ('DEMO-HINGE',   'ITEM-200', 'DH@@@@@', 'H@@@@', 3,  TRUE,  FALSE),
    ('DEMO-RUNNING', 'ITEM-300', 'DR@@@@@', NULL,    10, FALSE, TRUE);

-- ---------------------------------------------------------------------
-- Final inspection
--   DEMO-BASIC  WO-DEMO-001 carton 1 has 4 of 5 parts: scan DB00005 to fill it.
--   DEMO-HINGE  WO-DEMO-002 carton 1 has 1 part.
-- ---------------------------------------------------------------------
INSERT INTO record_barcode_pass (product_name, work_order, barcode, barcode_hinge, carton_box_no, line_no, operator_id, remark) VALUES
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00001', NULL,    1, 1, 'OP00001', NULL),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00002', NULL,    1, 1, 'OP00001', NULL),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00003', NULL,    1, 1, 'OP00001', NULL),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00004', NULL,    1, 1, 'OP00002', NULL),
    ('DEMO-BASIC', 'WO-DEMO-003', 'DB00101', NULL,    1, 2, 'OP00002', NULL),
    ('DEMO-BASIC', 'WO-DEMO-003', 'DB00102', NULL,    1, 2, 'OP00002', NULL),
    ('DEMO-HINGE', 'WO-DEMO-002', 'DH00001', 'H0001', 1, 1, 'OP00003', NULL);

INSERT INTO record_barcode_fail (product_name, work_order, barcode, carton_box_no, line_no, operator_id, remark) VALUES
    ('DEMO-BASIC', 'WO-DEMO-001', 'XX00001', 1, 1, 'OP00001', 'Barcode format mismatch'),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00002', 1, 1, 'OP00001', 'Duplicate barcode');

-- ---------------------------------------------------------------------
-- Dimension and appearance stations (Dimension Data / Appearance Data tabs)
-- ---------------------------------------------------------------------
INSERT INTO record_barcode_dimension (product_name, work_order, barcode, line_no, operator_id, dimension_result, remark) VALUES
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00001', 1, 'OP00004', TRUE,  NULL),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00002', 1, 'OP00004', TRUE,  NULL),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00007', 1, 'OP00004', FALSE, 'Out of tolerance');

INSERT INTO record_barcode_appearance (product_name, work_order, barcode, line_no, operator_id, appearance_result, remark) VALUES
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00001', 1, 'OP00005', TRUE,  NULL),
    ('DEMO-BASIC', 'WO-DEMO-001', 'DB00008', 1, 'OP00005', FALSE, 'Scratch');

-- ---------------------------------------------------------------------
-- Combined carton 1 already holds one part from each of two work orders.
-- On the Combined tab, DB00002 and DB00102 can still be added.
-- ---------------------------------------------------------------------
INSERT INTO record_combined_carton (combined_carton_no, product_name, work_order, barcode, operator_id) VALUES
    (1, 'DEMO-BASIC', 'WO-DEMO-001', 'DB00003', 'OP00006'),
    (1, 'DEMO-BASIC', 'WO-DEMO-003', 'DB00101', 'OP00006');
