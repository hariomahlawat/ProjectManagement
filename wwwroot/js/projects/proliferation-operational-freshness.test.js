const test = require("node:test");
const assert = require("node:assert/strict");
const fs = require("node:fs");
const path = require("node:path");

const root = process.cwd();
const read = relative => fs.readFileSync(path.join(root, relative), "utf8");

test("proliferation overview separates recent business events from staff-maintenance freshness", () => {
    const view = read("Areas/ProjectOfficeReports/Pages/Proliferation/Summary.cshtml");
    const service = read("Areas/ProjectOfficeReports/Application/ProliferationSummaryReadService.cs");

    assert.match(view, /Recent proliferation/);
    assert.match(view, /Data freshness/);
    assert.match(view, /Latest register activity/);
    assert.match(view, /Latest data entry \/ update/);
    assert.match(view, /maintenance actions/);

    assert.match(service, /GetOperationalSnapshotAsync/);
    assert.match(service, /ProliferationDate <= todayIst/);
    assert.match(service, /MaintenanceAuditActions/);
    assert.match(service, /CollapseActivityDrafts/);
    assert.match(service, /ReceivingUnitCount/);
    assert.match(service, /LatestDataEntryUtc|latestDataEntryUtc/);
    assert.doesNotMatch(service, /Proliferation\.ExportGenerated"\s*,?\s*$/m);
});

test("records page defaults to latest proliferation and offers latest-activity sorting", () => {
    const view = read("Areas/ProjectOfficeReports/Pages/Proliferation/Index.cshtml");
    const script = read("wwwroot/js/pages/proliferation-dashboard.js");
    const controller = read("Areas/ProjectOfficeReports/Api/ProliferationController.cs");

    assert.match(view, /id="pf-record-sort"/);
    assert.match(view, /value="latest-proliferation" selected/);
    assert.match(view, /value="latest-activity"/);
    assert.match(view, /Project A–Z/);

    assert.match(script, /sort:\s*"latest-proliferation"/);
    assert.match(script, /params\.set\("sort", state\.sort\)/);
    assert.match(controller, /NormalizeGroupedSort/);
    assert.match(controller, /"latest-activity"/);
    assert.match(controller, /OrderByDescending\(BusinessSortKey\)/);
    assert.match(controller, /x\.ProliferationDate <= todayIst/);
});

test("freshness activity references allow two-line context and old activity stays relative", () => {
    const css = read("wwwroot/css/proliferation.css");
    const pageModel = read("Areas/ProjectOfficeReports/Pages/Proliferation/Summary.cshtml.cs");

    assert.match(css, /-webkit-line-clamp:\s*2/);
    assert.match(pageModel, /months ago/);
    assert.match(pageModel, /years ago/);
});
