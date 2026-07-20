# Proliferation Analysis Excel Builder — CS1061 fix

Replace:

`Areas/ProjectOfficeReports/Application/ProliferationAnalysisExcelBuilder.cs`

The three unsupported calls:

```csharp
sheet.SheetView.ShowGridLines = false;
```

have been replaced with the ClosedXML 0.104-compatible print setting:

```csharp
sheet.PageSetup.ShowGridlines = false;
```

No other file, package, migration, or service registration is changed.
