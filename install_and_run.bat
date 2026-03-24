@echo off
REM Use the Windows Python that already has PyQt6 + psutil installed
set PYTHON=C:\Users\dziad\AppData\Local\Python\pythoncore-3.14-64\python.exe

echo Using: %PYTHON%
echo.
echo Installing / verifying dependencies...
"%PYTHON%" -m pip install PyQt6 psutil winrt-Windows.Media.Control winrt-Windows.Foundation winrt-runtime
echo.
echo Starting Windows 11 MenuBar  (log: %~dp0menubar.log)
echo Close this window to kill the bar.
echo.
"%PYTHON%" "%~dp0menubar.py"
echo.
echo --- Process exited ---
if exist "%~dp0menubar_crash.log" (
    echo CRASH DETECTED — contents:
    type "%~dp0menubar_crash.log"
)
pause
