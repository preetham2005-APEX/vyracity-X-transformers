@echo off
setlocal
cd /d "%~dp0.."
echo ========================================================================
echo VEYRA CITY - Downloading Reference Screenshots (GTA V / Cyberpunk / Asphalt / NFS / Forza)
echo ========================================================================
python Tools\download_reference_gallery.py --extract-videos %*
echo.
echo Done! Inspect ReferenceImages\GALLERY.html and ReferenceImages\MANIFEST_FOR_CLAUDE.md
pause

