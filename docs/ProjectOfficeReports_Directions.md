# Development Directions for ProjectOfficeReports Module

## 1. Purpose

We are creating a new module named **ProjectOfficeReports**. This module will hold different trackers and reports that are maintained by the Project Office.

The first tracker to be created under this module will be called **"Visits & Dignitary Tracker"**.

Later, other trackers and reports will also come under this same module.

---

## 2. User Roles and Permissions

| Role           | What they can do                                      |
| -------------- | ----------------------------------------------------- |
| Admin          | Can view, create, edit, delete, and manage Visit Types. |
| HoD            | Can view, create, edit, and delete Visits.            |
| ProjectOffice  | Can view, create, edit, and delete Visits.            |
| All other users (normal) | Can view only.                              |

**Important:**
- "ProjectOffice" is the correct role name (not ProjectOfficer).
- Only these three roles (Admin, HoD, ProjectOffice) will have editing rights.

---

## 3. What to Build (Overview)

Under **ProjectOfficeReports**, create a sub-module named **Visits**.

It will have:
1. Visit Types – defined by Admin.
2. Visits – created for each actual visit.
3. Photos – photos linked to each visit.

---

## 4. Data Structure (Database Models)

### 4.1 VisitType table

This table will store all visit types. Admin can add or edit them anytime.

| Field               | Type   | Notes                                                   |
| ------------------- | ------ | ------------------------------------------------------- |
| Id                  | GUID   | Primary key                                             |
| Name                | string | Required, unique (e.g. "Course Visit", "Military Dignitary") |
| Description         | string | Optional                                                |
| IsActive            | bool   | If false, it should not show in dropdowns               |
| CreatedAtUtc, CreatedByUserId | audit fields |                                         |
| LastModifiedAtUtc, LastModifiedByUserId | audit fields |                              |
| RowVersion          |        | for concurrency                                         |

### 4.2 Visit table

This stores each individual visit.

| Field               | Type   | Notes                                                   |
| ------------------- | ------ | ------------------------------------------------------- |
| Id                  | GUID   | Primary key                                             |
| VisitTypeId         | FK → VisitType | Required                                         |
| DateOfVisit         | Date   | Required                                                |
| Strength            | Integer| Required, must be > 0                                   |
| Remarks             | String | Optional (max 2000 chars)                               |
| CoverPhotoId        | FK → VisitPhoto | Optional                                       |
| CreatedByUserId, CreatedAtUtc | audit fields |                                         |
| LastModifiedByUserId, LastModifiedAtUtc | audit fields |                              |
| RowVersion          |        | for concurrency                                         |

### 4.3 VisitPhoto table

Each visit can have multiple photos.

| Field        | Type   | Notes                                                         |
| ------------ | ------ | ------------------------------------------------------------- |
| Id           | GUID   | Primary key                                                   |
| VisitId      | FK → Visit | Required                                                   |
| StorageKey   | string | e.g. `project-office-reports/visits/{visitId}/photos/...`     |
| ContentType  | string | MIME type                                                     |
| Width, Height| int    | image size metadata                                           |
| Caption      | string | Optional caption                                              |
| VersionStamp | string | For cache-busting                                             |
| CreatedAtUtc | timestamp | Audit                                                      |

---

## 5. Folder and Namespace Structure

Follow this exact structure:

```
/Areas/ProjectOfficeReports/
    /Pages/
        /Visits/
            Index.cshtml
            New.cshtml
            Edit.cshtml
            Details.cshtml
            _PhotosPartial.cshtml
        /VisitTypes/
            Index.cshtml
            New.cshtml
            Edit.cshtml
    /Domain/
        Visit.cs
        VisitPhoto.cs
        VisitType.cs
    /Application/
        VisitService.cs
        VisitPhotoService.cs
        VisitTypeService.cs
```

Keep naming consistent — "Visit", "VisitType", and "VisitPhoto".

---

## 6. Upload Storage

All visit photos must be saved under this folder pattern:

```
project-office-reports/visits/{visitId}/photos/
```

Follow the same upload service logic already used in ProjectPhotoService. Use version stamps and caching logic the same way. If possible, make a common photo service that handles both “Project” and “Visit” scopes.

---

## 7. Pages and Functionality

### 7.1 Visits List Page

**URL:** `/ProjectOfficeReports/Visits`

- Show a table with columns:
  - Date of Visit
  - Visit Type
  - Strength
  - Photo Count
  - Actions (View/Edit/Delete)
- Filters on top:
  - Visit Type (dropdown)
  - Date range (from–to)
  - Text search in Remarks
- “New Visit” button visible only to Admin, HoD, and ProjectOffice.
- “Export visits” button is available to the same roles when at least one visit matches the current filters. Selecting it opens an offline-friendly modal where the user can choose visit type, optional search text, and a date range before downloading an Excel workbook.
- The workbook contains a tidy header row (serial number, visit type, date, visitor, strength, photo count, cover photo flag, remarks, created/modified audit fields) and a summary footer noting the applied date range and generation timestamp.

### 7.2 Create/Edit Visit Page

**URL:** `/ProjectOfficeReports/Visits/New` and `/ProjectOfficeReports/Visits/{id}/Edit`

Fields:
- Visit Type (dropdown from active VisitTypes)
- Date of Visit
- Strength
- Remarks

Below the form:
- Section to upload photos, show thumbnails, delete, and set cover.

Buttons:
- Save / Cancel

### 7.3 Visit Details Page

**URL:** `/ProjectOfficeReports/Visits/{id}`

Show:
- Visit Type (badge)
- Date
- Strength
- Remarks
- Photo gallery (cover photo first)
- Buttons: Edit / Delete (if permitted)
- Show photo lightbox on click

### 7.4 Visit Types Management Page

**URL:** `/ProjectOfficeReports/VisitTypes`

- Admin only.
- Columns: Name, Description, Status (Active/Inactive), Actions.
- Actions: Add, Edit, Disable/Enable, Delete (only if not used in any visit).

### 7.5 Dashboard Widget (optional, after main pages)

- Small card on the main dashboard showing 5 latest visits with:
  - Date
  - Visit Type
  - Strength
  - “View All” link → Visits list page.

---

## 8. Endpoints Summary

| Endpoint | Purpose |
| -------- | ------- |
| GET /ProjectOfficeReports/Visits | List all visits |
| POST /ProjectOfficeReports/Visits/New | Create visit |
| POST /ProjectOfficeReports/Visits/{id}/Edit | Edit visit |
| POST /ProjectOfficeReports/Visits/{id}/Delete | Delete visit |
| POST /ProjectOfficeReports/Visits/{id}/Photos | Upload photo |
| DELETE /ProjectOfficeReports/Visits/{id}/Photos/{photoId} | Delete photo |
| POST /ProjectOfficeReports/Visits/{id}/Photos/{photoId}/SetCover | Set cover |
| GET /ProjectOfficeReports/VisitTypes | List visit types |
| POST /ProjectOfficeReports/VisitTypes/New | Create new type |
| POST /ProjectOfficeReports/VisitTypes/{id}/Edit | Edit visit type |
| POST /ProjectOfficeReports/VisitTypes/{id}/Disable | Enable/Disable visit type |

---

## 9. Validation Rules

- Visit Type, Date of Visit, and Strength are mandatory.
- Strength must be greater than zero.
- Remarks maximum 2000 characters.
- If a Visit Type is disabled, it should not appear in dropdowns.

---

## 10. Security and UI Standards

- No inline scripts anywhere.
- Must follow CSP compliance same as other modules.
- Use the same toast messages and form layouts already used in Projects module.
- Use RowVersion for concurrency checks.

---

## 11. Audit and Logs

Add audit logs for:
- Visit Created / Updated / Deleted
- Visit Type Added / Updated / Disabled
- Visit Photo Added / Deleted / Cover Changed

Show these in the details page if needed.

---

## 12. Migration

Create a new EF Core migration named **Add_ProjectOfficeReports_Visits**.

This migration must include:
- VisitType table
- Visit table
- VisitPhoto table

Ensure foreign key constraints are correctly applied.

---

## 13. Testing Checklist

Before delivery, test the following:

- ✅ Role-based access for all roles
- ✅ Create/Edit/Delete of Visit
- ✅ Create/Edit/Delete of Visit Type
- ✅ Photo upload, delete, and set cover
- ✅ Validation of Strength > 0
- ✅ Disabled VisitType not visible in dropdown
- ✅ RowVersion concurrency works properly
- ✅ Filters and sorting on list page
- ✅ No inline scripts, all CSP headers correct

---

If the team follows this exactly, the ProjectOfficeReports module will be clean, modular, and expandable for future trackers like:
- Monthly Reports
- Audit Inspections
- Equipment Status Reports, etc.

---
