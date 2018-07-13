CALL build.bat
if %errorlevel% neq 0 exit /b %errorlevel%
eb deploy landformweb --profile %1