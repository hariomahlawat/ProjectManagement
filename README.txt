PRISM ERP - Build Fixes - 08 Aug 2026

This package fixes the two reported ProjectManagement.Tests compilation errors.

1. ProjectManagement.Tests/ProjectBriefings/ProjectBriefingSlideComposerTests.cs
   CS1061: ShapeProperties.PresetGeometry does not exist with DocumentFormat.OpenXml 3.1.1.
   Fix: query the direct A.PresetGeometry child using OpenXmlElement.Elements<T>(), then inspect its A.ShapeGuide descendant.
   This preserves the original regression assertion for the rounded-rectangle geometry adjustment (val 6000).

2. ProjectManagement.Tests/ConferenceProjectScopeServiceTests.cs
   CS0103: ProcurementWorkflow does not exist in the current context.
   Fix: add using ProjectManagement.Models.Stages;, which is the namespace containing ProcurementWorkflow.

Copy the two ProjectManagement.Tests files over the matching paths in the solution.
No production/runtime files or database migrations are changed.
