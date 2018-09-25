# LandformWeb Test Procedures
These instructions detail how to test a deployment of LandformWeb.

## Authentication Test
1. Note this will only work on https://landform.hi.jpl.nasa.gov because that is the domain SSO is configured to use.
1. Click `API Token` and confirm the response is `not authenticated`.
1. Click `Login (SSO)`.
1. Enter credentials for a JPL user identity that is a member of the `TILE_SERVER_LDAP_GROUP` configured when the Landform master server was deployed.  For test and production deployments managed by the Landform team, this is `landform`.
1. Click `API Token` and confirm the response is a web token - copy the token ID for later.
1. Click `Logout`.
1. Click `API Token` and confirm the response is `not authenticated`.

Note: SSO will only work in a deployment where the Landform master server DNS name matches a configuration registered with the JPL SSO service.  If you are running the server locally within the JPL IP address space, e.g. for testing, then you can use the LDAP login instead of SSO.

## REST API Tests
Three methods are supported for running [REST API](../docs/API.md) tests:
1. Using [curl](https://curl.haxx.se/windows) from the Windows 10 command prompt (`cmd`)
2. Using `curl` from any other compatible command prompt.
3. Using [Postman](https://www.getpostman.com).  This is the recommended method as it is mostly automated.

Details are given below for `curl` on the Windows 10 command prompt.  To use another compatible command prompt you may need to modify the syntax slightly.  For example, in `bash` style command prompts variable substitution syntax is `$VARIABLE` instead of `%VARIABLE%`.

To use Postman
1. download and import this [Postman collection](../test/Landform-test.postman_collection.json)
2. define variables
   1. ellipsis menu for collection -> Edit -> Variables
   4. set the "current value" for each variable following the Setup procedure below
   5. Update
3. Follow the test sequence below, but using the pre-defined requests in the Postman collection instead of `curl`.  For example, to create the project, click the "create project" request in the collection and then click "Send".

### Setup
1. Login and get an API token using the authentication test procedure.  Copy the token to the system clipboard, and then paste it on the windows command line to set a temporary environment variable by running a command like this:

  `set API_TOKEN=(pasted token)`

2. Set the server URL.  For example, to test against the production or test server managed by the Landform team:

  `set SERVER_URL=https://landform[-dev].hi.jpl.nasa.gov`

 Otherwise substitute the end-user DNS name of your Landform master server.

3. Determine a unique name for a test project.  For example, each time you run this procedure use a project name like `testN` where `N` is an integer that you increment.

   `set PROJECT_NAME=testN`

 Perform the remaining steps of this procedure in the same command window where these variables were set.

4. Select a mesh and texture file for the test project from the following:
  * mesh: `inputMeshSmall.ply`, texture: `inputImage.png`
  * TODO

  Note: test data is available at TODO.

### Test Sequence
1. Create project:

       curl -sS --request POST \
            --url http://%SERVER_URL%/api/projects/%PROJECT_NAME% \
            --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid JSON object with `success=true`.

1. List projects:

       curl -sS --request GET \
            --url http://%SERVER_URL%/api/projects \
            --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid JSON array of strings containing `%PROJECT_NAME%`.

1. Upload input files:

       curl -sS --request POST \
            --url http://%SERVER_URL%/api/projects/%PROJECT_NAME%/upload \
            --header "x-landform-token: %API_TOKEN%" \
            --header "content-type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW" \
            --form mesh=(mesh filename) \
            --form texture=(texture filename)

   use the mesh and texture filenames you selected during setup.  If using Postman select the "upload data" request, then click Body, then Choose Files.

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid JSON object with `success=true`.

1. Run project:

       curl -sS --request POST \
            --url http://%SERVER_URL%/api/projects/%PROJECT_NAME%/run \
            --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid JSON object with `success=true`.

1. Get project metadata:

       curl -sS --request GET \
            --url http://%SERVER_URL%/api/projects/%PROJECT_NAME% \
            --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid JSON object.

1. Get project result URL:

       curl -sS --request GET \
            --url http://%SERVER_URL%/api/projects/%PROJECT_NAME%/result?redirect=false \
            --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid URL.

1. Get project viewer URL:

       curl -sS --request GET \
            --url http://%SERVER_URL%/api/projects/%PROJECT_NAME%/view?redirect=false \
            --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response code is HTTP 200 (ok), the content type is `application/json`, and the response body is a valid URL.

   To visually inspect the dataset, copy the returned URL to the system clipboard and then load it in a Chrome browser.

1. Delete project: TODO
