const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');

const root = path.resolve(__dirname, '..', '..', '..');
const read = (...segments) => fs.readFileSync(path.join(root, ...segments), 'utf8');

const details = read('Pages', 'ProjectIdeas', 'Details.cshtml');
const detailsModel = read('Pages', 'ProjectIdeas', 'Details.cshtml.cs');
const deleted = read('Pages', 'ProjectIdeas', 'Deleted.cshtml');
const edit = read('Pages', 'ProjectIdeas', 'Edit.cshtml');
const editModel = read('Pages', 'ProjectIdeas', 'Edit.cshtml.cs');
const permissions = read('Services', 'ProjectIdeas', 'ProjectIdeaPermissionService.cs');
const commands = read('Services', 'ProjectIdeas', 'ProjectIdeaCommandService.cs');
const client = read('wwwroot', 'js', 'pages', 'project-ideas-details.js');
const conferenceRead = read('Services', 'Workspace', 'OfficerConferenceReadService.cs');
const officerWorkload = read('Services', 'Workspace', 'OfficerWorkloadReadService.cs');
const poWorkspace = read('Services', 'Workspace', 'ProjectOfficerWorkspaceService.cs');

 test('commandant default is resolved on server and rendered into discussion composer', () => {
    assert.match(permissions, /user\.IsInRole\(RoleNames\.Comdt\)/);
    assert.match(permissions, /\? ProjectIdeaCommentTypes\.Conference\s*:\s*ProjectIdeaCommentTypes\.General/);
    assert.match(detailsModel, /DefaultCommentType = _permissions\.GetDefaultCommentType\(User, idea\)/);
    assert.match(details, /data-pi-comment-default="@Model\.DefaultCommentType"/);
});

test('discussion remarks expose governed edit and soft-delete flows', () => {
    assert.match(commands, /EditCommentAsync\(/);
    assert.match(commands, /SoftDeleteCommentAsync\(/);
    assert.match(details, /data-pi-comment-edit/);
    assert.match(details, /data-pi-delete-comment/);
    assert.match(client, /Delete Conference direction\?/);
    assert.match(client, /no longer be considered in Conference Review/);
});

test('idea deletion is controlled soft-delete with a recovery workspace', () => {
    assert.match(commands, /SoftDeleteIdeaAsync\(/);
    assert.match(commands, /RestoreDeletedIdeaAsync\(/);
    assert.match(commands, /idea\.IsDeleted = true/);
    assert.match(deleted, /Deleted Ideas/);
    assert.match(deleted, /asp-page-handler="Restore"/);
    assert.match(details, /Reason for deletion/);
});


test('deleted ideas and deleted directions are excluded from operational conference surfaces', () => {
    assert.match(officerWorkload, /!idea\.IsDeleted/);
    assert.match(poWorkspace, /!idea\.IsDeleted/);
    assert.match(conferenceRead, /!direction\.IsDeleted[\s\S]*ProjectIdeaCommentTypes\.Conference/);
});


test('governance mutations use friendly optimistic concurrency handling', () => {
    assert.match(commands, /ConcurrencyConflictMessage/);
    assert.match(commands, /DbUpdateConcurrencyException/);
    assert.match(commands, /ApplyRowVersion\(idea, rowVersion\)/);
    assert.match(commands, /ApplyRowVersion\(comment, rowVersion\)/);
    assert.match(details, /name="rowVersion"/);
    assert.match(detailsModel, /ArchiveAsync\(Idea, archiveReason, DecodeRowVersion\(rowVersion\)\)/);
    assert.match(detailsModel, /RestoreAsync\(Idea, DecodeRowVersion\(rowVersion\)\)/);
    assert.match(edit, /asp-for="Input.RowVersion"/);
    assert.match(editModel, /UpdateAsync\(idea, DecodeRowVersion\(Input.RowVersion\)\)/);
});
