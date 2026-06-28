[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $PSScriptRoot "..\..\..\App_Data\media-models")
)

$ErrorActionPreference = "Stop"
$models = @(
    @{
        Name = "YuNet 2026may"
        File = "face_detection_yunet_2026may.onnx"
        Url = "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_detection_yunet/face_detection_yunet_2026may.onnx"
        Sha256 = "ebafce4e3c118d6554634be5c27ab333b4c047a9a8c3faf1d7cf93101c22f0f0"
    },
    @{
        Name = "SFace 2021dec"
        File = "face_recognition_sface_2021dec.onnx"
        Url = "https://media.githubusercontent.com/media/opencv/opencv_zoo/main/models/face_recognition_sface/face_recognition_sface_2021dec.onnx"
        Sha256 = "0ba9fbfa01b5270c96627c4ef784da859931e02f04419c829e83484087c34e79"
    }
)

New-Item -ItemType Directory -Force -Path $Destination | Out-Null
foreach ($model in $models) {
    $target = Join-Path $Destination $model.File
    $temporary = "$target.download"
    Write-Host "Downloading $($model.Name)..."
    Invoke-WebRequest -Uri $model.Url -OutFile $temporary -UseBasicParsing
    $actual = (Get-FileHash -Path $temporary -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($actual -ne $model.Sha256) {
        Remove-Item $temporary -Force -ErrorAction SilentlyContinue
        throw "Checksum mismatch for $($model.Name). Expected $($model.Sha256); received $actual."
    }
    Move-Item $temporary $target -Force
    Write-Host "Verified $($model.File)"
}

Write-Host "Approved models installed at $Destination"
Write-Host "Review model licences and organisational approval before enabling MediaLibrary:People."
