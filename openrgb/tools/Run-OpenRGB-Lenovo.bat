@echo off
:: Starts the test build of OpenRGB with Lenovo Legion GPU support.
:: Uses its own settings folder so the installed OpenRGB 1.1 settings are not touched.
:: Close the installed OpenRGB and LGPU before using this.
start "" "C:\CCPL\build\OpenRGB\release\OpenRGB.exe" --noautoconnect --config "C:\CCPL\build\openrgb-config"
