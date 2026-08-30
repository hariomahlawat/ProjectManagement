const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '../../..');
const read = relative => fs.readFileSync(path.join(root, relative), 'utf8');
const assertContains = (text, needle, label) => {
    if (!text.includes(needle)) throw new Error(`${label}: expected to find ${needle}`);
};
const assertNotContains = (text, needle, label) => {
    if (text.includes(needle)) throw new Error(`${label}: did not expect ${needle}`);
};

const policy = read('Application/Ipr/IprProjectEligibilityPolicy.cs');
assertContains(policy, '!project.IsDeleted', 'IPR project policy excludes deleted projects');
assertContains(policy, '!project.IsBuild', 'IPR project policy excludes Repeat Builds');
assertNotContains(policy, '!project.IsArchived', 'Archived original projects remain eligible for IPR linking');

const picker = read('Areas/ProjectOfficeReports/Pages/Ipr/Index.SelectLists.cs');
assertContains(picker, '.Where(IprProjectEligibilityPolicy.EligibleProjectPredicate)', 'IPR project pickers use the canonical policy');

const write = read('Application/Ipr/IprWriteService.cs');
assertContains(write, '.Where(IprProjectEligibilityPolicy.EligibleProjectPredicate)', 'IPR writes enforce the canonical policy');

const directMeta = read('Pages/Projects/Meta/Edit.cshtml.cs');
assertContains(directMeta, 'IprProjectLinkMaintenance.DetachLinkedRecordsAsync', 'Direct Repeat Build conversion detaches IPR links');

const approvalMeta = read('Services/Projects/ProjectMetaChangeDecisionService.cs');
assertContains(approvalMeta, 'IprProjectLinkMaintenance.DetachLinkedRecordsAsync', 'Approved Repeat Build conversion detaches IPR links');

const compendium = read('Services/Compendiums/CompendiumReadService.cs');
assertContains(compendium, '!item.Project.IsBuild', 'Compendium defensively excludes Repeat Build IPR attribution');

const search = read('Services/SearchV2/Indexing/SearchProjectionBuilder.cs');
assertContains(search, 'ProjectIsBuild', 'Search V2 reads the linked project Repeat Build flag');
assertContains(search, '!row.ProjectIsBuild', 'Search V2 does not project Repeat Build IPR as project-owned');

const migration = read('Migrations/20261216220000_UnlinkIprFromRepeatBuildProjects.cs');
assertContains(migration, 'UPDATE "IprRecords"', 'Migration unlinks existing IPR records');
assertContains(migration, 'SET "ProjectId" = NULL', 'Migration preserves IPR records and clears only the project link');
assertContains(migration, 'p."IsBuild" = TRUE', 'Migration targets Repeat Build projects');
assertNotContains(migration, 'DELETE FROM "IprRecords"', 'Migration must not delete IPR records');

const migrationIds = read('Migrations/immutable-migration-ids.txt');
assertContains(migrationIds, '20261216220000_UnlinkIprFromRepeatBuildProjects', 'Migration is registered in the immutable manifest');

console.log('IPR Repeat Build policy source contracts passed.');
