# Test Procedures

## Authentication Test

1. Access the web front end in Google Chrome:
    * test and demo deployment: <https://landform.hi.jpl.nasa.gov>
    * for SSO integration testing: <https://landform-dev.hi.jpl.nasa.gov>
    * for local test and dev: <http://localhost:3000>
1. Click `API Token` and confirm the response is `not authenticated`.
1. Click `Login (SSO)`, or `Login (LDAP)` if using a local deployment
1. Enter credentials for a JPL user identity that is a member of the LDAP group configured when the Landform master server was deployed.  For test and production deployments managed by the Landform team, this is `landform`.
1. Click `API Token` and confirm the response is a web token - copy the token ID for later.
1. Click `Logout`.
1. Click `API Token` and confirm the response is `not authenticated`.

Note: SSO login will only work in a deployment where the Landform master server DNS name matches a configuration registered with the JPL SSO service.  If you are running the server locally (within the JPL firewall) then you can use the LDAP login instead of SSO.

## REST API Tests

Three methods are supported for running [REST API](../docs/API.md) tests:

1. Using curl (<https://curl.haxx.se/windows>) from the Windows 10 command prompt (`cmd`)
1. Using `curl` from any other compatible command prompt.
1. Using Postman (<https://www.getpostman.com>).  This is the recommended method as it is mostly automated.

Details are given below for `curl` on the Windows 10 command prompt.  To use another compatible command prompt you may need to modify the syntax slightly.  For example, in `bash` style command prompts variable substitution syntax is `$VARIABLE` instead of `%VARIABLE%`.

To use Postman

1. import the Postman collection `Landform-run.postman_collection.json` included with the Landform source code.
1. define variables
    1. ellipsis menu for collection -> Edit -> Variables
    1. set the "current value" for each variable following the Setup procedure below
    1. Update
1. Follow the test sequence below, but using the pre-defined requests in the Postman collection instead of `curl`.  For example, to create the project, click the "create project" request in the collection and then click "Send".

### Setup

1.  Login and get an API token using the authentication test procedure.  Copy the token to the system clipboard, and then paste it on the windows command line to set a temporary environment variable by running a command like this:

        set API_TOKEN=(pasted token)

2.  Set the server URL.  For example, to test against the production or test server managed by the Landform team:

        set SERVER_URL=https://landform[-dev].hi.jpl.nasa.gov

    Or if you are running an instance of the server on your local machine:

        set SERVER_URL=http://localhost:8081

    Otherwise substitute the end-user DNS name of your Landform master server.

3.  Determine a unique name for a test project.  For example, each time you run this procedure use a project name like `testN` where `N` is an integer that you increment.

        set PROJECT_NAME=test0

4. Select a mesh and texture file for the test project.  Several test datasets are available at <https://landlords-dev.s3.amazonaws.com/landformweb-test-data/landform-test-data-shared.zip>.  Download and unzip it and then choose one of the included datasets, e.g.:

        set MESH_FILE=landform-test-data-shared/stick/stick.ply
        set TEXTURE_FILE=landform-test-data-shared/stick/stick.jpg

 Perform the remaining steps of this procedure in the same command window where these variables were set.

### Test Sequence

1. Create project:

        curl -sS --request POST
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response contains `"success":true`.  Example response:

        {"id":1,"running":false,"success":true,"exitCode":0,"error":null,"started":1593190323668,"ended":1593190326124}
   
1. List projects:

        curl -sS --request GET
                 --url "%SERVER_URL%/api/projects"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response contains PROJECT_NAME.  Example response:

        [
          "test0"
        ]

1. Upload input files:

        curl -sS --request POST
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%/upload"
                 --header "x-landform-token: %API_TOKEN%"
                 --form "mesh=@%MESH_FILE%"
                 --form "texture=@%TEXTURE_FILE%"

   (If using Postman instead of curl select the "upload data" request, then click Body, then Choose Files.)

   Validation: check that the response contains `"success":true`.  Example response:
   
       {"id":3,"running":false,"success":true,"exitCode":0,"error":null,"started":1593190654817,"ended":1593190658155}

1. Run project:

        curl -sS --request POST
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%/run"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response contains `"success":true`.  Example response:

        {"id":4,"running":false,"success":true,"exitCode":0,"error":null,"started":1593190740442,"ended":1593190742395}

1. Get project metadata:

        curl -sS --request GET
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response contains `"StartedRunning": true`.  Example response:

        {
          "Project": {
            "Name": "test0",
            "TilingScheme": "Bin",
            "SkirtMode": "None",
            "ReconMethod": "Poisson",
            "FacesPerTile": 2000,
            "TileResolution": 256,
            "TilesDefined": true,
            "ProjectType": "GenericTiling",
            "StartedRunning": true,
            "FinishedRunning": false,
            "MaxLeafGroupSize": 32,
            "ExportDir": "www",
            "ExportMeshFormat": null,
            "ExportImageFormat": null,
            "InternalTileDir": "tiles",
            "InternalMeshFormat": "ply",
            "InternalImageFormat": "png",
            "TilesetDir": "www",
            "TilesetMeshFormat": "b3dm",
            "TilesetImageFormat": "jpg"
          },
          "Inputs": [
            {
              "Name": "stick",
              "MeshUrl": "https://mipl-dev-landform.s3.amazonaws.com/landform-web/landform-dev-vona-quarth/input/test0/stick.ply",
              "ImageUrl": "https://mipl-dev-landform.s3.amazonaws.com/landform-web/landform-dev-vona-quarth/input/test0/stick.jpg",
              "Processed": true,
              "ImageBands": 3,
              "ImageWidth": 4096,
              "ImageHeight": 4096
            }
          ],
          "NumNodes": 49,
          "NumProcessedNodes": 0,
          "OutputUrl": "https://mipl-dev-landform.s3.amazonaws.com/landform-web/landform-dev-vona-quarth/www/test0/tileset.json"
        }

1. Poll project metadata about every 30 seconds until the response contains `"FinishedRunning": true`.  This should take 5 minutes for the "stick" dataset.

1. Get project result URL:

        curl -sS --request GET
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%/result?redirect=false"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response is a URL ending in "tileset.json".  Example response:

        "https://mipl-dev-landform.s3-us-gov-west-1.amazonaws.com/landform-web/landform-dev-vona-quarth/www/test0/tileset.json"

1. Get project viewer URL:

        curl -sS --request GET
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%/view?redirect=false"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response is a URL ending in "tileset.json".  Example response:

        "http://localhost:8081/viewer/index.html?Tileset=https%3A%2F%2Fmipl-dev-landform.s3-us-gov-west-1.amazonaws.com%2Flandform-web%2Flandform-dev-vona-quarth%2Fwww%2Ftest0%2Ftileset.json"

1. Copy and paste the project viewer URL from the last step in a Chrome browser window to visually inspect the result.  Note: don't copy the double quotes which surround the URL.  The "stick" test dataset may initially appear very small in teh center of the viewer.  Hit the "f" key on the keyboard to fit the view to the dataset, which will zoom it in.

1. Delete project:

        curl -sS --request DELETE
                 --url "%SERVER_URL%/api/projects/%PROJECT_NAME%"
                 --header "x-landform-token: %API_TOKEN%"

   Validation: check that the response contains `"success":true`.  Example response:
   
       {"id":7,"running":false,"success":true,"exitCode":0,"error":null,"started":1593191585989,"ended":1593191599176}
