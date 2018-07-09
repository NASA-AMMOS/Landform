del landformweb.zip
CALL npm install
if %errorlevel% neq 0 exit /b %errorlevel%
cd client
CALL npm install
cd ..
CALL npm run build
if %errorlevel% neq 0 exit /b %errorlevel%
zip -r landformweb.zip . -x node_modules/* -x client/node_modules*
if %errorlevel% neq 0 exit /b %errorlevel%
