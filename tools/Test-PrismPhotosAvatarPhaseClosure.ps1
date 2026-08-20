$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ge 7) { $PSNativeCommandUseErrorActionPreference = $true }

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Write-Host "PRISM Photos avatar phase-closure validation" -ForegroundColor Cyan
Write-Host "Project root: $root"

function Read-ProjectFile([string]$relativePath) {
    $path = Join-Path $root $relativePath
    if (-not (Test-Path $path)) { throw "Missing required file: $relativePath" }
    return Get-Content $path -Raw
}

function Require-Text([string]$text, [string]$needle, [string]$message) {
    if (-not $text.Contains($needle)) { throw $message }
}

function Forbid-Text([string]$text, [string]$needle, [string]$message) {
    if ($text.Contains($needle)) { throw $message }
}

function Invoke-Checked([scriptblock]$command, [string]$label) {
    & $command
    if ($LASTEXITCODE -ne 0) { throw "$label failed with exit code $LASTEXITCODE" }
}

$account = Read-ProjectFile 'Areas/Identity/Pages/Account/Manage/Index.cshtml'
$accountModel = Read-ProjectFile 'Areas/Identity/Pages/Account/Manage/Index.cshtml.cs'
$service = Read-ProjectFile 'Features/MediaLibrary/Services/MediaPersonUserLinkService.cs'
$details = Read-ProjectFile 'Pages/Photos/People/Details.cshtml'
$siteCss = Read-ProjectFile 'wwwroot/css/site.css'
$peopleCss = Read-ProjectFile 'wwwroot/css/pages/photos-reference-readiness.css'
$contractTests = Read-ProjectFile 'ProjectManagement.Tests/AccountPhotoAvatarContractTests.cs'
$serviceTests = Read-ProjectFile 'ProjectManagement.Tests/MediaLibrary/MediaPersonUserLinkServiceTests.cs'

Require-Text $account 'asp-page-handler="UsePhotosPortrait"' 'Explicit Use Photos portrait command is missing.'
Require-Text $account 'asp-page-handler="UseInitials"' 'Explicit Use initials command is missing.'
Require-Text $account 'account-photo-avatar-setting__use-initials' 'Active Use initials visual treatment is missing.'
Forbid-Text $account 'name="usePhotosPortrait"' 'Client-supplied avatar Boolean toggle has returned.'

Require-Text $accountModel 'OnPostUsePhotosPortraitAsync()' 'UsePhotosPortrait handler is missing.'
Require-Text $accountModel 'OnPostUseInitialsAsync()' 'UseInitials handler is missing.'
Require-Text $accountModel 'PRISM profile image' 'Account status copy is not using the final profile-image terminology.'
Forbid-Text $accountModel 'PRISM avatar' 'Legacy PRISM avatar wording remains in Account Settings user-facing copy.'

Require-Text $details 'ShouldUsePortraitAsAvatar' 'Person Details is not using the resolved presentation state.'
Require-Text $details 'Photos portrait in use' 'Person Details photo-state label is missing.'
Require-Text $details 'Initials in use' 'Person Details initials-state label is missing.'
Require-Text $details 'person-account-link__profile-state' 'Person Details profile-state badge is missing.'
Require-Text $details 'Choose or prepare a trusted matching reference below.' 'Trusted-reference warning copy is not final.'
Require-Text $details 'PRISM profile image' 'Person Details is not using final profile-image terminology.'
Forbid-Text $details 'PRISM avatar' 'Legacy PRISM avatar wording remains on Person Details.'

Require-Text $siteCss '.account-photo-avatar-setting__use-initials' 'Use initials button contrast treatment is missing.'
Require-Text $siteCss 'height: 36px;' 'Current-profile preview compact sizing is missing.'
Require-Text $siteCss 'width: 36px;' 'Current-profile preview compact sizing is missing.'
Require-Text $peopleCss '.person-account-link__current .person-account-link__profile-state.is-photo' 'Linked-account state badge styling is incomplete.'

Forbid-Text $service 'PRISM avatar' 'A user-visible legacy PRISM avatar phrase remains in MediaPersonUserLinkService.'
$reportStart = $service.IndexOf('public async Task ReportIncorrectLinkAsync')
$resolveStart = $service.IndexOf('public async Task ResolveLinkConcernAsync')
$unlinkStart = $service.IndexOf('public async Task UnlinkAsync')
if ($reportStart -lt 0 -or $resolveStart -le $reportStart -or $unlinkStart -le $resolveStart) {
    throw 'Could not isolate account-link concern handlers.'
}
$reportSection = $service.Substring($reportStart, $resolveStart - $reportStart)
$resolveSection = $service.Substring($resolveStart, $unlinkStart - $resolveStart)
Require-Text $reportSection 'link.UsePortraitAsAvatar = false;' 'Incorrect-identity reporting must force the portrait preference off.'
Forbid-Text $resolveSection 'UsePortraitAsAvatar = true' 'Resolving a concern must not silently re-enable the portrait.'

Require-Text $contractTests 'FinalPolish_UsesProfileImageTerminology_AndMakesCurrentStateScannable' 'Phase-closure UI contract test is missing.'
Require-Text $serviceTests 'Assert.Null(await service.GetPhotoIdentityForUserAsync("user-1"' 'Unlink-to-initials fallback coverage is missing.'
Require-Text $serviceTests 'managerView!.ShouldUsePortraitAsAvatar' 'Manager-view avatar-state coverage is missing.'

Write-Host 'Source-contract checks passed.' -ForegroundColor Green

Push-Location $root
try {
    Invoke-Checked { node --check .\wwwroot\js\pages\photos-person-linkage.js } 'photos-person-linkage.js syntax check'
    Invoke-Checked { node --check .\wwwroot\js\pages\photos-person-profile.js } 'photos-person-profile.js syntax check'
    Invoke-Checked { node --test .\wwwroot\js\pages\photos-person-linkage-contract.test.js .\wwwroot\js\pages\photos-person-profile-contract.test.js } 'Photos JavaScript contract tests'

    Invoke-Checked { dotnet build .\ProjectManagement.csproj } 'ProjectManagement build'
    Invoke-Checked {
        dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --no-build --filter 'FullyQualifiedName~AccountManagePageTests|FullyQualifiedName~AccountPhotoAvatarContractTests|FullyQualifiedName~MediaPersonUserLinkServiceTests'
    } 'Focused Photos avatar tests'
}
finally {
    Pop-Location
}

Write-Host 'PRISM Photos avatar phase closure validated successfully.' -ForegroundColor Green
