# LandformWeb REST API
All API methods require an API token to be set as an `x-landform-token` HTTP header or `landform-token` cookie.


### Create Project: POST /api/project/*name*
Create the named project.  Implements the [task API](#task-api).

Creating the same project more than once has no effect (not an error).

Accepts the following arugments:
* *tilingscheme*: tiling scheme; one of `Bin`, `Quad`, `Oct`, or `UserDefined`; default `Bin`
* *skirtmode*: skirt mode; one of `None`, `X`, `Y`, `Z`; default `None`
* *facespertile*: target maximum faces per tile; default 2000
* *tileresolution*: maximum image resolution per tile; default 256

**Example:**  create project "testproj"

Request

    POST /api/project/testproj HTTP/1.1
    Host: https://landform.hi.jpl.nas.gov
    x-landform-token: API_TOKEN
    Content-Type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW
    
    ------WebKitFormBoundary7MA4YWxkTrZu0gW--

Response

    HTTP/1.1 200
    status: 200
    content-type: application/json; charset=utf-8

    {
      "id": 1,
      "running": false,
      "success": true,
      "exitCode": 0,
      "error": null,
      "started": 1537227661847,
      "ended": 1537227672413
    }

### Upload Data: POST /api/project/*name*/upload
Upload data for the named project.  Implements the [task API](#task-api).

Uploading the data with the same mesh filename more than once has no effect (not an error).

The data format is implied from the filename extension.

Accepts the following arguments in a `multipart/form-data` encoded HTTP request body:
* *mesh*: mesh data file (required); data format will be implied from filename extension
* *texture*: texture image file (optional); data format will be implied from filename extension
* *tileid*: tile ID string; must be given if and only if the tiling scheme is user-defined (may also be specified as a URL query parameter)
* all task API arguments (may also be specified as URL query parameters)

**Example:** upload "inputMeshSmall.ply" and "inputImage.png" to "testproj"

Request

    POST https://landform.hi.jpl.nasa.gov/api/project/testproj/upload HTTP/1.1
    Host: https://landform.hi.jpl.nas.gov
    x-landform-token: API_TOKEN
    Content-Type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW
    
    ------WebKitFormBoundary7MA4YWxkTrZu0gW
    Content-Disposition: form-data; name="mesh"; filename="inputMeshSmall.ply"
    Content-Type: 
    
    DATA
    ------WebKitFormBoundary7MA4YWxkTrZu0gW
    Content-Disposition: form-data; name="texture"; filename="inputImage.png"
    Content-Type: image/png
   
    DATA
    ------WebKitFormBoundary7MA4YWxkTrZu0gW--

Response

    HTTP/1.1 200
    status: 200
    content-type: application/json; charset=utf-8

    {
      "id": 2,
      "running": false,
      "success": true,
      "exitCode": 0,
      "error": null,
      "started": 1537228976377,
      "ended": 1537228980192
    }

### Run Project: POST /api/project/*name*/run
Initiate a run of a project.  Implements the [task API](#task-api).

Note: in the current implementation this task only initiates the execution of a project.  It will typically complete before the project is actually finished running.  To determine whether the project execution has completed:
1. Determine the project result URL via the `/api/project/*name*/result?redirect=false` API.
2. Poll the project result URL.  When it returns HTTP 200 (OK) with valid JSON the project has completed execution.

**Example:** run project "testproj"

Request

    POST /api/project/testproj/run HTTP/1.1
    Host: https://landform.hi.jpl.nasa.gov
    x-landform-token: API_TOKEN
    Content-Type: multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxkTrZu0gW
    
    ------WebKitFormBoundary7MA4YWxkTrZu0gW--

Response

    HTTP/1.1 200
    status: 200
    content-type: application/json; charset=utf-8

    {
      "id": 3,
      "running": false,
      "success": true,
      "exitCode": 0,
      "error": null,
      "started": 1537229779144,
      "ended": 1537229780231
    }

### Get Project Result URL: GET /api/project/*name*/result
Fetch `tileset.json` result for a project.

By default this API will return a HTTP 302 (found) redirect to the `tileset.json` file stored on AWS.  If execution of the project is not yet completed, this URL will return HTTP 403 (forbidden).

If the argument `redirect=false` is specified then the output is the `text/plain` `tileset.json` URL with no redirect.

**Example:** get result URL for project "testproj" without redirect

Request

    GET 8081/api/project/testproj/result?redirect=false
    Host: https://landform.hi.jpl.nasa.gov
    x-landform-token: API_TOKEN

Response

    HTTP/1.1 200
    status: 200
    content-type: text/plain; charset=utf-8

    https://landlords-dev.s3.amazonaws.com/landformweb/www/testproj/tileset.json

### View Project: GET /api/project/*name*/view
Launch a web-based 3D viewer for a completed project.

**Example:** launch 3D viewer for project "testproj"

Request

    GET /api/project/testproj/view HTTP/1.1
    Host: https://landform.hi.jpl.nasa.gov
    x-landform-token: API_TOKEN
    
Response

    HTTP/1.1 302 Found
    location: /viewer/index.html?TilesetURL=https%3A%2F%2Flandlords-dev.s3.amazonaws.com%2Flandformweb%2Fwww%2Ftestproj%2Ftileset.json
    content-type: text/html; charset=utf-8


---

## Task API
Many API calls launch a task.  By default such calls are synchronous in that they will not return a response until the task completes.  At that point an `application/json` response will normally be sent with the following task metadata:

    {
      id: server assigned unique task identifier
      name: server assigned task name (may not be unique)
      running: false
      success: whether task succeded (true or false)
      exitCode: 0 on success, nonzero integer or string error code on failure
      error: error message if any
      started: task start time (ms since epoch on server)
      ended: task end time (ms since epoch on server)
    }

The HTTP status will be 200 (ok) if the task completed successfully or 500 (server error) if it failed.

If the server encountered an error processing the request (e.g. invalid API call) or failed to launch the task it will return an `application/json` failure response containing only the following error metadata:

    {
      success: false
      error: error message if any
    }

In this case the HTTP status will be 400 (bad request) if the API call was invalid or 500 if the task failed to lauch for some other reason.

If the task was successfully launched (i.e. if a task `id` was returned):
* The text output of the task may be retrieved via the /api/task/*id*/log API.
* The task metadata may be retrieved again via the /api/task/*id* API.

The server will expire task metadata and logs after 24h has expired since the completion of the task, after which they will no longer be available.

#### API Arguments as Query Parameters or Body Fields
Unless otherwise specified, all API arguments may be specified as either URL query parameters or as fields in a `application/json` or `application/x-www-form-urlencoded` HTTP request body.  If both the query parameter and body field are present the former takes precedence.

#### Asynchronous Task API
If the caller prefers an asynchronous task interface the request may include the optional argument `async=true`.  In this case the server will respond with the task (or API error) metadata without waiting for the task to complete.  The HTTP status will be 200 if the task launched successfully, 400 if the API call was invalid, or 500 if the task failed to launch.

If the task was successfully launched the caller may monitor execution of the task by subsequently polling the /api/task/*id* API.  The `success`, `exitCode`, `error`, and `ended` fields are only valid once the task is completed, i.e. `running=false`.

#### Text Task API
If the caller prefers to receive the text output of the task instead of the task metadata the request may include the optional argument `text=true` or `live=true` (the latter takes precedence over the former, and `async=true` takes precedence over both).

When `text=true` the server returns the text output of the task as `text/plain` synchronously, i.e. all at once when the task completes.

When `live=true` the server returns the text output of the task as `text/plain` progressively, i.e. line-by-line as it is produced.

In either case
* If the server failed to launch the task it will return API error metadata as `application/json` and an HTTP status code of 400 or 500.
* If the task was successfully launched the first line of output will give the task ID.
* If the task fails an error message will be appended to the text output.

If the task was successfully launched
* For `text=true` the HTTP status will be 200 or 500 depending on whether the task completed successfully.
* For `live=true` the HTTP status will be 200 whether or not the task completed successfully (because the status code may need to be sent before the task completes).

### Get Master Task ID: GET /api/task/master/id
Returns id of the master task.

**Example:**

Request

    GET /api/task/master/id HTTP/1.1
    Host: SERVER_URL
    x-landform-token: API_TOKEN

Response

    HTTP/1.1 200
    status: 200
    content-type: application/json; charset=utf-8

    0

### Get Task Metadata: GET /api/task/*id*
Returns JSON metadata for task with the given id.

**Example:** get metadata for task 0 (typically task 0 is the master task)

Request

    GET /api/task/0 HTTP/1.1
    Host: https://landform.hi.jpl.nasa.gov
    x-landform-token: API_TOKEN

Response

    HTTP/1.1 200
    status: 200
    content-type: application/json; charset=utf-8

    {
      "id": 0,
      "running": true,
      "success": false,
      "exitCode": null,
      "error": null,
      "started": 1537227621242,
      "ended": null
    }

### Get Task Log: GET /api/task/*id*/log
Returns the log for task with the given id.

By default the log is returned as an JSON array of strings containing the line-by-line text output of the task.  If the task is still running then whatever it has output up to the time of call will be returned.

If the argument `text=true` is specified then the output is `text/plain` lines instead of a JSON array.

If the argument `live=true` is specified then the output is `text/plain` lines starting with the log to-date.  If the task is still running then subsequent lines will be sent progressively until the task is complete.

**Example:** get text log of task 0 (typically task 0 is the master task)

Request

    GET /api/task/0/log?text=true HTTP/1.1
    Host: https://landform.hi.jpl.nasa.gov
    x-landform-token: API_TOKEN

Response

    HTTP/1.1 200
    status: 200
    content-type: text/plain; charset=utf-8

    2018-09-17 17:17:55,414 OPS.Pipeline.TileServer.PipelineStateMachine: DefineTiles project:testproj
    2018-09-17 17:18:02,225 OPS.Pipeline.TileServer.PipelineStateMachine: ChunkInput project:testproj input:inputMeshSmall
    2018-09-17 17:18:02,263 OPS.Pipeline.TileServer.PipelineStateMachine: Build Leaves
    2018-09-17 17:18:13,798 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:110000000001
    2018-09-17 17:18:16,570 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0110101010010010000
    2018-09-17 17:18:25,463 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:011010101001001101
    2018-09-17 17:18:26,130 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:111
    2018-09-17 17:18:27,726 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:1000
    2018-09-17 17:18:29,523 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0110101010010001011
    2018-09-17 17:18:30,705 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:10010100
    2018-09-17 17:18:31,080 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:011010101001000100
    2018-09-17 17:18:32,427 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:1001010100
    2018-09-17 17:18:34,871 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0110101010010000011
    2018-09-17 17:18:38,479 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0110101010011
    2018-09-17 17:18:40,314 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0110101011
    2018-09-17 17:18:40,531 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:101
    2018-09-17 17:18:42,662 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0010
    2018-09-17 17:18:44,208 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:00110
    2018-09-17 17:18:44,427 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:011011
    2018-09-17 17:18:47,184 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:010
    2018-09-17 17:18:47,545 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:0011110
    2018-09-17 17:18:52,385 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:001111111101
    2018-09-17 17:18:55,313 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:001111110
    2018-09-17 17:18:56,468 OPS.Pipeline.TileServer.PipelineStateMachine: TileCompleted project:testproj tile:000
