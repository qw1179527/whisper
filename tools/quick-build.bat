@echo off
setlocal
set N=%1
if "%N%"=="" set N=40
"C:\Users\qing_\.dsh\tools\PortableGit\bin\bash.exe" -lc "cd '/d/DSH专用/whisper' && bash tools/quick-build.sh %N%"
endlocal
