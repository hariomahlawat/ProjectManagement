param(
    [string]$ProjectRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
)

$ErrorActionPreference = "Stop"

function Assert-FileContains {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    if (-not (Test-Path $Path)) {
        throw "Missing required file: $Path"
    }

    $content = Get-Content $Path -Raw
    if (-not $content.Contains($Text)) {
        throw "Expected contract '$Text' was not found in $Path"
    }
}

function Assert-FileDoesNotContain {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Text
    )

    if (-not (Test-Path $Path)) {
        throw "Missing required file: $Path"
    }

    $content = Get-Content $Path -Raw
    if ($content.Contains($Text)) {
        throw "Obsolete contract '$Text' is still present in $Path"
    }
}

$accountPage = Join-Path $ProjectRoot "Areas\Identity\Pages\Account\Manage\Index.cshtml"
$accountModel = Join-Path $ProjectRoot "Areas\Identity\Pages\Account\Manage\Index.cshtml.cs"
$loginPartial = Join-Path $ProjectRoot "Pages\Shared\_LoginPartial.cshtml"
$linkService = Join-Path $ProjectRoot "Features\MediaLibrary\Services\MediaPersonUserLinkService.cs"

Assert-FileContains $accountPage 'asp-page-handler="UsePhotosPortrait"'
Assert-FileContains $accountPage 'asp-page-handler="UseInitials"'
Assert-FileDoesNotContain $accountPage 'asp-page-handler="PhotoAvatar"'
Assert-FileDoesNotContain $accountPage 'name="usePhotosPortrait"'
Assert-FileContains $accountModel 'OnPostUsePhotosPortraitAsync()'
Assert-FileContains $accountModel 'OnPostUseInitialsAsync()'
Assert-FileDoesNotContain $accountModel 'OnPostPhotoAvatarAsync'
Assert-FileContains $loginPartial 'ShouldUsePortraitAsAvatar'
Assert-FileContains $linkService 'Photos avatar preference persistence verification failed'

Write-Host "PRISM Photos avatar source contracts are present." -ForegroundColor Green

Push-Location $ProjectRoot
try {
    dotnet clean .\ProjectManagement.csproj
    Remove-Item .\bin, .\obj -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item .\ProjectManagement.Tests\bin, .\ProjectManagement.Tests\obj -Recurse -Force -ErrorAction SilentlyContinue

    dotnet build .\ProjectManagement.csproj
    dotnet test .\ProjectManagement.Tests\ProjectManagement.Tests.csproj --filter "FullyQualifiedName~AccountManagePageTests|FullyQualifiedName~AccountPhotoAvatarContractTests|FullyQualifiedName~MediaPersonUserLinkServiceTests"
}
finally {
    Pop-Location
}

Write-Host "PRISM Photos avatar stabilisation validation passed." -ForegroundColor Green
