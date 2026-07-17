# IPR Patent–Copyright Guidance and Category Breakdown

## Installation

Copy the contents of this package over the **ProjectManagement solution root** and replace the existing files while preserving the directory structure.

The package is based on the latest IPR Workbench implementation, including natural browser scrolling, the persistent record inspector, project dossiers, follow-up and analytics.

## Functional changes

### 1. Layered patent/copyright guidance

The Add/Edit IPR workflow now provides guidance at three levels:

- A short, always-visible explanation below **IPR category**.
- A three-question inline decision aid.
- A detailed **Patent, copyright—or both?** modal with SDD-relevant examples.

The guidance explicitly explains that one project may require separate patent and copyright records.

### 2. Correct type-specific terminology

The UI retains a unified IPR register but uses legally clearer record terminology:

- **Patent pending** / **Patent granted**
- **Registration pending** / **Copyright registered**

Combined management summaries use the neutral terms **Filed**, **Protected** and **Pending**.

### 3. Patent/copyright breakdown

The main summary ribbon now shows:

- Filed: patent and copyright split
- Protected: patents granted and copyrights registered
- Pending: patent and copyright split

Each split is directly clickable and filters the Records register to the selected category and position.

The Analytics tab includes an exact category matrix for Filed, Protected and Pending figures. Project dossiers also show their patent/copyright composition.

### 4. Dynamic form behaviour

Changing **IPR category** dynamically updates:

- Position labels
- Guidance text
- Grant/registration date label
- Supporting explanatory text

### 5. Export and validation consistency

Excel export now includes:

- IPR category
- Type-specific position text
- A neutral **Protection date** heading

Server validation messages use **protection date** so they apply correctly to patents and copyrights.

## No database change

No database migration or configuration change is required. The implementation uses the existing `IprType`, `IprStatus` and date fields.

## Verification performed

- JavaScript syntax validation passed for `index.js` and `ipr-register.js`.
- 17 frontend regression tests passed.
- Source brace-balance checks passed for the modified C#, Razor, CSS and JavaScript files.

The .NET SDK is not available in the generation environment, so the solution and C# test suite could not be compiled here.

## Local verification

After replacement:

1. Stop the running application.
2. Delete the `bin` and `obj` folders for the web and test projects.
3. Rebuild the solution.
4. Run the IPR tests.
5. Verify the following manually:
   - Open **Add IPR record** and switch between Patent and Copyright.
   - Confirm the position and date labels change appropriately.
   - Open the guidance modal from both the page header and form.
   - Confirm Filed, Protected and Pending show patent/copyright splits.
   - Click each split and verify the correct register filter is applied.
   - Export the register and verify category, status wording and protection date.
