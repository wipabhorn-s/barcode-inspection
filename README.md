# Barcode Inspection

A Windows Forms application used on a production line to check every scanned part before it goes into a carton.
For each barcode it checks the format, duplicates, check digits and the results of earlier stations (dimension,
electrical, appearance, hinge torque, automated visual inspection), then records PASS or FAIL in PostgreSQL.

**Stack:** VB.NET, .NET Framework 4.8, WinForms, PostgreSQL (Npgsql), MSTest

## Try it

You need Windows, Visual Studio 2022 or later with the .NET Framework 4.8 targeting pack, and PostgreSQL.

1. Create an empty database (for example `qa_db`) and run the scripts in `Database/` in order:
   `create_schema.sql`, then `003_sample_data.sql`.
   (`002_admin_password.sql` is only for databases created before the password table existed.)
2. Copy `Barcode Inspection/connectionStrings.example.config` to `Barcode Inspection/connectionStrings.config`
   and fill in host, user, password and database.
3. Open `Barcode Inspection.sln`, choose **Release | x86**, and press **F5**.

**Admin password:** `12345678` (Setting tab, Query Console, deleting records).
Change it with **Change Password** on the Setting tab; it is stored only as a salted hash.

### Things to try with the sample data

| Tab | Do this | Expect |
|---|---|---|
| Recorder | Product `DEMO-BASIC`, work order `WO-DEMO-001`, line `1`, operator `OP00001`, carton `1` → Load, scan `DB00005` | PASS, and the carton is full (5 of 5) |
| Recorder | Same carton, scan `DB00001` | FAIL: duplicate barcode |
| Recorder | Scan `XX12345` | FAIL: barcode format |
| Recorder | Product `DEMO-HINGE`, work order `WO-DEMO-002`, carton `1`, scan `DH00002` then hinge `H0002` | PASS (the cursor moves to the hinge box by itself) |
| Recorder | Product `DEMO-RUNNING`, add work order `WO-DEMO-004`, running no. `00001-00050`, scan `DR00099` | Asks Rework / Reject |
| Combined | Product `DEMO-BASIC`, carton `1`, scan `DB00002` | PASS; `DB00003` is refused (already combined) |
| Barcode Reconciliation | Product `DEMO-BASIC`, scan `DB00001` and `DB00003`, then work order `WO-DEMO-001`, carton `1` → Compare | `DB00002` and `DB00004` are listed as missing |
| Data Management | Search `DEMO-BASIC` by work order | Passed parts, combined cartons, and the FAIL list with **Defect** ticked |

Run `003_sample_data.sql` again at any time to reset the sample data.

## What was refactored

The application started as one form with all logic in `Form1.vb`. It was refactored tab by tab into
controllers, services and repositories without changing the screens.

| | Before | After |
|---|---|---|
| `Form1.vb` | 7,011 lines | 699 lines |
| Automated tests | none | 144 (unit + integration against PostgreSQL) |
| Admin password | constant in the code | salted PBKDF2 hash in the database, changeable from the UI |
| Database credentials | in the source code | `connectionStrings.config`, not committed |
| Dimension / Appearance tabs | two copies of the same code | one controller class used twice |

Bugs found and fixed along the way include: a PASS shown when the database connection dropped during saving,
duplicate checks that let barcodes through when the database was unreachable, cartons that could be overfilled
from two PCs, deleting passed records without the admin password, "clear selected" removing the wrong rows in a
sorted grid, and CSV export that broke on values containing quotes.

## Architecture

```
Barcode Inspection/
├── Form1.vb                 Export and Query Console tabs, plus wiring for the controllers below
├── UI/                      Screen logic, one class per kind of tab (MVP / passive view)
│   ├── FinalInspectionController.vb    Recorder tab: final inspection scanning
│   ├── StationInspectionController.vb  Scanning tab, shared by the Dimension and Appearance stations
│   ├── StationReportController.vb      Search / export / delete tab, shared by both stations
│   ├── FinalReportController.vb        Final Data tab (final inspection results, combined cartons)
│   ├── SettingsController.vb           Setting tab: admin lock, input folders, product add / edit / delete
│   ├── CombinedCartonController.vb     Combined tab: pack passed parts from several work orders into one carton
│   ├── ReconciliationController.vb     Barcode Reconciliation tab: find passed parts missing from a carton
│   ├── ResultGrid.vb                   One result grid: show, merge without duplicates, remove rows
│   └── UiHelpers.vb                    UiKit: message boxes (replaceable in tests), admin-delete confirmation, filters, CSV export
├── Domain/                  Pure business rules, no I/O
│   ├── BarcodeRules.vb      Format template, modulo-34 / modulo-36 check digits, running-number range
│   ├── Station.vb           Station, judgement mode, table names (whitelisted), search filter
│   ├── ProductSpec.vb       Product barcode format and required checks
│   ├── ProductSpecValidation.vb  Product form rules
│   ├── PasswordHasher.vb    Salted PBKDF2-SHA256 hashes for the admin password, and the new-password rules
│   ├── Reconciliation.vb    Which passed parts were not scanned, and the filter rules
│   ├── InspectionData.vb    One inspected part
│   ├── InspectionOptions.vb Which checks are enabled for the product
│   └── InspectionFailure.vb Error shown to the operator, with priority
├── Services/
│   ├── BarcodeInspectionService.vb  Final inspection: runs the checks in order, stops at the first failure
│   ├── StationInspectionService.vb  Dimension / appearance scan: checks, then operator or automatic judgement
│   ├── ElectricalResultReader.vb    Electrical tester output (.txt / .csv)
│   ├── HingeFileReader.vb           Hinge torque CSV
│   ├── AviResultReader.vb           Two AVI stations, checked in parallel with a timeout
│   ├── CombinedCartonService.vb     Rules for adding a part to a combined carton (passed, same product, not full)
│   ├── AdminPasswordService.vb      Checks and changes the admin password (hash kept in the database)
│   └── Csv.vb                       RFC 4180 escaping for exports
└── Data/
    ├── DbConnectionFactory.vb       Connection string from config, retry with back-off
    ├── InspectionRepository.vb      Final inspection SQL
    ├── StationRepository.vb         One class for both station tables
    ├── FinalReportRepository.vb     Final Data searches and deletes
    ├── CombinedCartonRepository.vb
    ├── ReconciliationRepository.vb
    ├── AppSettingRepository.vb      Settings shared by every PC (app_setting table)
    └── ProductSpecRepository.vb

Barcode Inspection.Tests/    144 MSTest tests
    Unit tests               Rules, file parsing, both inspection flows (fake repository and fake operator)
    Integration tests        Real SQL against PostgreSQL, and the Recorder, Final Data, Setting, Combined and Reconciliation tab controllers driven
                             through real WinForms controls; skipped unless BARCODE_INSPECTION_TEST_DB is set

Database/create_schema.sql   Tables, part-number triggers, indexes and the initial admin password hash
Database/002_admin_password.sql   Adds the admin password table to a database created before it existed
Database/003_sample_data.sql      Made-up DEMO- products and records for trying the program
```

The Dimension and Appearance tabs used to be two copies of the same ~1,200 lines. They now run on the same two
controllers; `Form1` only maps each station's controls into a `StationInspectionView` / `StationReportView`.
Services and repositories do not reference WinForms, so their logic is tested without a UI.

## Running the tests

Open Test Explorer in Visual Studio. Unit tests run anywhere. Integration tests run real SQL and drive the
controllers through real WinForms controls; they are skipped unless `BARCODE_INSPECTION_TEST_DB` is set to a
connection string for a throw-away database created from `create_schema.sql`.

## Credits

Application icon by [Icons8](https://icons8.com).
