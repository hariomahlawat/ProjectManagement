const fs = require('fs');
const path = require('path');
const assert = require('assert');

const root = path.resolve(__dirname, '../../..');
const read = rel => fs.readFileSync(path.join(root, rel), 'utf8');

function test(name, fn) {
  try {
    fn();
    console.log(`PASS ${name}`);
  } catch (error) {
    console.error(`FAIL ${name}`);
    console.error(error.message);
    process.exitCode = 1;
  }
}

test('activity details uses one shared photo viewer and dedicated assets', () => {
  const view = read('Pages/Activities/Details.cshtml');
  assert.match(view, /activities\/details\.css/);
  assert.match(view, /activity-details\.js/);
  assert.match(view, /data-activity-photo-viewer/);
  assert.doesNotMatch(view, /photo-modal-/);
  assert.doesNotMatch(view, /No photos uploaded\./);
  assert.doesNotMatch(view, /No videos uploaded\./);
  assert.doesNotMatch(view, /No documents uploaded\./);
  assert.doesNotMatch(view, /No other files uploaded\./);
});

test('details media mutations flow through ActivityService', () => {
  const model = read('Pages/Activities/Details.cshtml.cs');
  assert.doesNotMatch(model, /IActivityAttachmentManager/);
  assert.match(model, /_activityService\.AddAttachmentAsync\(/);
  assert.match(model, /_activityService\.RemoveAttachmentAsync\(/);
});

test('attachment mutations persist activity audit metadata with the attachment change', () => {
  const service = read('Services/Activities/ActivityService.cs');
  assert.match(service, /LastModifiedAtUtc\s*=\s*_clock\.UtcNow;[\s\S]*?_attachmentManager\.AddAsync\(/);
  assert.match(service, /LastModifiedAtUtc\s*=\s*_clock\.UtcNow;[\s\S]*?_attachmentManager\.RemoveAsync\(/);
});

test('attachment policy supports more records and larger videos', () => {
  const manager = read('Services/Activities/ActivityAttachmentManager.cs');
  const validator = read('Services/Activities/ActivityAttachmentValidator.cs');
  assert.match(manager, /MaxAttachmentsPerActivity\s*=\s*50/);
  assert.match(validator, /MaxStandardAttachmentSizeBytes\s*=\s*25\s*\*\s*1024\s*\*\s*1024/);
  assert.match(validator, /MaxVideoAttachmentSizeBytes\s*=\s*200\s*\*\s*1024\s*\*\s*1024/);
  const client = read('wwwroot/js/pages/activity-details.js');
  assert.match(validator, /MaxUploadBatchSizeBytes\s*=\s*200\s*\*\s*1024\s*\*\s*1024/);
  assert.match(client, /dataset\.batchMax/);
  assert.match(client, /upload batch limit/);
});

test('new activity date is enforced conditionally and duplicate title is not blocked', () => {
  const validator = read('Services/Activities/ActivityInputValidator.cs');
  const editModel = read('Pages/Activities/Edit.cshtml.cs');
  assert.match(validator, /existing is null/);
  assert.match(validator, /Event date is required/);
  assert.match(editModel, /ValidateEventDate\(existing\)/);
  assert.match(editModel, /existingActivity is null \|\| existingActivity\.ScheduledStartUtc\.HasValue/);
  assert.doesNotMatch(validator, /ExistsByTypeAndTitleAsync/);
  assert.doesNotMatch(validator, /already exists for the selected type/);
});

test('hard duplicate title index is removed by model and migration', () => {
  const db = read('Data/ApplicationDbContext.cs');
  const migration = read('Migrations/20261216230000_AllowDuplicateActivityTitles.cs');
  assert.doesNotMatch(db, /UX_Activities_ActivityTypeId_Title/);
  assert.match(migration, /DropIndex/);
  assert.match(migration, /UX_Activities_ActivityTypeId_Title/);
});

test('activity authorization is centralised and stale edits carry row version', () => {
  const policy = read('Services/Activities/ActivityAuthorizationPolicy.cs');
  const service = read('Services/Activities/ActivityService.cs');
  const edit = read('Pages/Activities/Edit.cshtml');
  const editModel = read('Pages/Activities/Edit.cshtml.cs');
  assert.match(policy, /CanManage/);
  assert.match(policy, /CanRequestDelete/);
  assert.match(service, /ActivityAuthorizationPolicy/);
  assert.match(edit, /Input\.RowVersion/);
  assert.match(editModel, /ExpectedRowVersion/);
});

test('pending delete state is rendered instead of a second delete request', () => {
  const view = read('Pages/Activities/Details.cshtml');
  assert.match(view, /Deletion pending/);
  assert.match(view, /Model\.HasPendingDelete/);
});

test('file lifecycle protects database consistency on add and delete', () => {
  const manager = read('Services/Activities/ActivityAttachmentManager.cs');
  assert.match(manager, /Failed to roll back activity attachment file/);
  assert.match(manager, /orphaned activity attachment file/);
});


test('activities remain standalone institutional records without project or industry linkage', () => {
  const files = [
    read('Services/Activities/IActivityService.cs'),
    read('Services/Activities/ActivityService.cs'),
    read('Pages/Activities/Edit.cshtml.cs'),
    read('Pages/Activities/Details.cshtml.cs')
  ].join('\n');
  assert.doesNotMatch(files, /ActivityProject|ProjectId|IndustryPartnerId|IndustryPartner/);
});


test('activity photos use media-library derivatives with protected-source fallback', () => {
  const detailsModel = read('Pages/Activities/Details.cshtml.cs');
  const indexModel = read('Pages/Activities/Index.cshtml.cs');
  assert.match(detailsModel, /BuildMediaUrl\(asset\.Id, "thumb"/);
  assert.match(detailsModel, /BuildMediaUrl\(asset\.Id, "preview"/);
  assert.match(detailsModel, /\?\? photo\.InlineUrl/);
  assert.match(indexModel, /variant = "thumb"/);
  assert.match(indexModel, /CreateInlineUrl\(media\.StorageKey/);
});

test('concurrency conflict does not silently advance the stale edit token', () => {
  const editModel = read('Pages/Activities/Edit.cshtml.cs');
  assert.doesNotMatch(editModel, /catch \(ActivityConcurrencyException[\s\S]*?Input\.RowVersion\s*=\s*Convert\.ToBase64String\(latest\.RowVersion/);
  assert.match(editModel, /Reload the latest version before saving|Reload the page and try again/);
});
