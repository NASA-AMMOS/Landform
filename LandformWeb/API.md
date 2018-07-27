# LandformWeb REST API
All API methods require an API token to be set as an `x-landform-token` HTTP header or `landform-token` cookie.

## Generic Task API
Many API calls launch a task.  By default such calls are synchronous: they will not return a response until the task completes.  At that point an `application/json` response will normally be sent with the following *task metadata*:
```
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
```
In this case the HTTP status will be 200 (ok) if the task completed successfully or 500 (server error) if it failed.

If the server encountered an error processing the request (e.g. invalid API call) or failed to launch the task it will return an `application/json` failure response containing only the following *API error metadata*:
```
{
  success: false
  error: error message if any
}
```
In this case the HTTP status will be 400 (bad request) if the API call was invalid or 500 if the task failed to lauch for some other reason.

If the task was successfully launched (i.e. if a task `id` was returned):
* The text output of the task may be retrieved via the /api/task/*id*/log API.
* The task metadata may be retrieved again via the /api/task/*id* API.

The server will expire task metadata and logs after 24h has expired since the completion of the task, after which they will no longer be available.

### API Arguments as Query Parameters or Body Fields
Unless otherwise specified, all API arguments may be specified as either URL query parameters or as fields in a `application/json` or `application/x-www-form-urlencoded` HTTP request body.  If both the query parameter and body field are present the former takes precedence.

### Asynchronous Task API
If the caller prefers an asynchronous task interface the request may include the optional argument `async=true`.  In this case the server will respond with the task (or API error) metadata without waiting for the task to complete.

If the task was successfully launched the caller may monitor execution of the task by subsequently polling the /api/task/*id* API.  The `success`, `exitCode`, `error`, and `ended` fields are only valid once the task is completed, i.e. `running=false`.

### Text Task API
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

## Landform Project Management API

### Create Project: POST|PUT /api/project/*name*
Create the named project.  Implements the generic task API.

Creating the same project more than once has no effect (not an error).

Accepts the following arugments:
* *tilingscheme*: tiling scheme; one of `Bin`, `Quad`, `Oct`, or `UserDefined`; default `Bin`
* *skirtmode*: skirt mode; one of `None`, `X`, `Y`, `Z`; default `None`
* *facespertile*: target maximum faces per tile; default 2000
* *tileresolution*: maximum image resolution per tile; default 256

### Upload Data: POST /api/project/*name*/upload
Upload data for the named project.  Implements the generic task API.

Uploading the data with the same mesh filename (TODO?) more than once has no effect (not an error).

Accepts the following arguments in a `multipart/form-data` encoded HTTP request body:
* *mesh*: mesh data file (required); data format will be implied from filename extension
* *texture*: texture image file (optional); data format will be implied from filename extension
* *tileid*: tile ID string; must be given if and only if the tiling scheme is user-defined (may also be specified as a URL query parameter)
* all generic task API arguments (may also be specified as URL query parameters)

### Run Project : GET|POST /api/project/*name*/run
Initiate a run of a project.  Implements the generic task API.

TODO what happens if this is called more than once?

TODO this task will typically complete before the project is actually finished running.

## Task Execution API

## Task Metadata: GET /api/task/*id*
Returns `application/json` metadata for task with the given id.

## Task Log: GET /api/task/*id*/log
Returns the log for task with the given id.

By default the log is returned as an `application/json` array of strings containing the line-by-line text output of the task.  If the task is still running then whatever it has output up to the time of call will be returned.

If the argument `text=true` is specified then the output is `text/plain` lines instead of a JSON array.

If the argument `live=true` is specified then the output is `text/plain` lines starting with the log to-date.  If the task is still running then subsequent lines will be sent progressively until the task is complete.

## Master Task: GET /api/task/master/id
Returns id of the master task.
