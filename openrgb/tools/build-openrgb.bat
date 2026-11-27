@echo off
:: Builds OpenRGB (with the Lenovo Legion GPU controller) using the local Qt, jom and VS 2022 Build Tools.
set "PATH=C:\CCPL\build\Qt\6.8.3\msvc2022_64\bin;C:\CCPL\build\tools\jom;%PATH%"
call "C:\Program Files (x86)\Microsoft Visual Studio\2022\BuildTools\VC\Auxiliary\Build\vcvarsall.bat" x64
if errorlevel 1 exit /b 1
cd /d C:\CCPL\build\OpenRGB
qmake OpenRGB.pro CONFIG-=debug_and_release CONFIG+=release
if errorlevel 1 exit /b 1
jom
if errorlevel 1 exit /b 1
windeployqt --no-patchqt --no-quick-import --no-translations --no-system-d3d-compiler --no-compiler-runtime --no-opengl-sw --no-network .\release\
if errorlevel 1 exit /b 1
copy /y "C:\CCPL\build\Qt\6.8.3\msvc2022_64\bin\Qt6Core5Compat.dll" .\release\
copy /y "C:\CCPL\build\Qt\6.8.3\msvc2022_64\bin\Qt6OpenGL.dll" .\release\
echo BUILD OK
