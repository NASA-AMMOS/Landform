# LandformWeb Test Procedures

## Authentication Test Procedures

### SSO Test Procedure
1. Note this will only work on https://landform.hi.jpl.nasa.gov because that is the domain SSO is configured to use.
2. Click `API Token` and confirm the response is `not authenticated`.
3. Click `Login (SSO)`.
4. Enter JPL credentials.
5. Click `API Token` and confirm the response is a web token - copy the token ID for later.
6. Click `Logout`.
7. Click `API Token` and confirm the response is `not authenticated`.

### LDAP Test Procedure
1. Note that this will not work on https://landform.hi.jpl.nasa.gov because it is running on a cloud instance and does not currently have access to JPLs internal network / LDAP server. However, it will work when the server is run locally or on a JPL host machine.
2. If your JPL user is not in the landform LDAP group contact Alex Menzies to be added
3. Click `Login (LDAP)` 
4. Enter JPL credentials
5. Click `API Token` and confirm the response is a web token - copy the token ID for later
6. Click `Logout`
7. Click `API Token` and confirm the response is `not authenticated`

## REST API Test Procedure

**TODO this needs to be updated for the [new API](API.md)**

This procedure is designed to be excuted from the Windows 10 command prompt (`cmd`).  It may not run correctly on other command prompts such as cygwin, Git bash, etc.  It runs tests against the server currently deployed on https://landform.hi.jpl.nasa.gov.  To instead test against a server run locally or on a JPL host machine, use LDAP login instead of SSO and substitute the URL to the machine in the commands below.

1. Login and get an API token using either the SSO test procedure above.  Copy the token to the system clipboard, and then paste it on the windows command line to set a temporary environment variable by running a command like this:

   `set API_TOKEN=PASTED_TOKEN`

   Perform the remaining steps of this procedure in the same command window.

2. Create a project:

   `curl -d "{\"name\":\"test-project-name\"}" -H "Content-Type: application/json" -H "x-landform-token: %API_TOKEN%" -X POST https://landform.hi.jpl.nasa.gov/api/project`

3. List projects

   `curl -H "x-landform-token: %API_TOKEN%" -X GET https://landform.hi.jpl.nasa.gov/api/project`

4. Get data about a project

   `curl -H "x-landform-token: %API_TOKEN%" -X GET https://landform.hi.jpl.nasa.gov/api/project/test-project-name`

5. Delete a project

   `curl -H "x-landform-token: %API_TOKEN%" -X DELETE https://landform.hi.jpl.nasa.gov/api/project/test-project-name`
