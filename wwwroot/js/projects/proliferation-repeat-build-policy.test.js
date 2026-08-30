const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = process.cwd();
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

test("new proliferation entry and counting-rule pickers exclude repeat builds while review filters retain legacy access", () => {
    const controller = read("Areas/ProjectOfficeReports/Api/ProliferationController.cs");
    const manage = read("wwwroot/js/pages/proliferation-manage.js");
    const summary = read("wwwroot/js/pages/project-office-reports/proliferation-summary-charts.js");
    const reports = read("wwwroot/js/pages/proliferation-reports.js");

    assert.match(controller, /includeLegacy/);
    assert.match(controller, /IsBuild/);
    assert.match(manage, /includeLegacy:\s*true/);
    assert.match(summary, /includeLegacy(?:=|:\s*)true/);
    assert.match(reports, /includeLegacy=true/);
});

test("data-quality workspace surfaces repeat-build links and provides direct record review", () => {
    const service = read("Areas/ProjectOfficeReports/Application/ProliferationDataQualityService.cs");
    const view = read("Areas/ProjectOfficeReports/Pages/Proliferation/_DataQualityPanel.cshtml");
    const script = read("wwwroot/js/pages/proliferation-data-quality.js");
    const workspaces = read("wwwroot/js/pages/proliferation-manage-workspaces.js");

    assert.match(service, /repeat_build_link/);
    assert.match(service, /RepeatBuildLinkCount/);
    assert.match(view, /Repeat-build links/);
    assert.match(script, /Review record/);
    assert.match(script, /proliferation:reviewrecord/);
    assert.match(workspaces, /proliferation:reviewrecord/);
});

test("legacy repeat-build records receive an in-editor warning and are not carried into new entries", () => {
    const view = read("Areas/ProjectOfficeReports/Pages/Proliferation/_ProliferationEditor.cshtml");
    const script = read("wwwroot/js/pages/proliferation-manage.js");
    const dto = read("Areas/ProjectOfficeReports/Api/ProliferationDtos.cs");

    assert.match(view, /pf-repeat-build-legacy-warning/);
    assert.match(script, /isEligibleForNewEntry/);
    assert.match(script, /includeLegacy(?:=|:\s*)true/);
    assert.match(dto, /IsEligibleForNewEntry/);
});
