PRISM ERP — FFC Export Test TableProperties Compile Fix
=========================================================

ERROR
-----
CS1061:
'Table' does not contain a definition for 'TableProperties'

CAUSE
-----
DocumentFormat.OpenXml.Wordprocessing.Table does not expose a typed
TableProperties convenience property in the OpenXML version used by this
solution.

The test was using:

    table.TableProperties?
        .GetFirstChild<W.TableCellMarginDefault>();

The production Word builder is not at fault. The compile failure is confined
to the test code.

FIX
---
Retrieve the child TableProperties element through the OpenXmlElement API:

    var tableProperties = table.GetFirstChild<W.TableProperties>();
    var margins = tableProperties?
        .GetFirstChild<W.TableCellMarginDefault>();

This is version-safe and matches the actual OOXML tree:

    w:tbl
      -> w:tblPr
           -> w:tblCellMar

FILE TO REPLACE
---------------
ProjectManagement.Tests/Reports/FfcProjectsUpdateExportTests.cs

AFTER PASTING
-------------
dotnet build .\ProjectManagement.Tests\ProjectManagement.Tests.csproj

dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj `
    --filter "FullyQualifiedName~FfcProjectsUpdateExportTests"
