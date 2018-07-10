CALL npm install
if %errorlevel% neq 0 exit /b %errorlevel%
CALL npm run build
if %errorlevel% neq 0 exit /b %errorlevel%
CALL npm run bundle
if %errorlevel% neq 0 exit /b %errorlevel%
