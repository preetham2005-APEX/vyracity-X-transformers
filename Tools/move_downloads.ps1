# Auto-mover: moves newly downloaded 3D model zips/glbs from C: Downloads to E:\VeyraCity\ArtSource\Downloads
param(
    [string]$Source = "$env:USERPROFILE\Downloads",
    [string]$Dest = "E:\VeyraCity\ArtSource\Downloads"
)

$patterns = @("*.zip", "*.glb", "*.gltf", "*.blend", "*.fbx")
Write-Host "Scanning $Source for 3D model files..." -ForegroundColor Cyan

$count = 0
foreach ($ext in $patterns) {
    Get-ChildItem -Path $Source -Filter $ext | Where-Object { 
        $_.LastWriteTime -gt (Get-Date).AddHours(-4) -and $_.Name -notlike "*.crdownload"
    } | ForEach-Object {
        $target = Join-Path $Dest $_.Name
        Write-Host "Moving $($_.Name) -> $target" -ForegroundColor Green
        Move-Item -Path $_.FullName -Destination $target -Force
        $count++
    }
}

if ($count -eq 0) {
    Write-Host "No recent 3D downloads found in $Source." -ForegroundColor Yellow
} else {
    Write-Host "Successfully moved $count files to $Dest" -ForegroundColor Green
}

