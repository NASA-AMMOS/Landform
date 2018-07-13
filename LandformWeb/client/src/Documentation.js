/* eslint-disable react/jsx-filename-extension */
import React from 'react';

const Documentation = () => (
  <div className="documentation">
    <h1>Documentation</h1>
    <h2>Overview</h2>
    The landform web console consists of two parts, each with its own authentication scheme.
    <ol>
      <li>Interactive web console (can use LDAP inside firewall or SSO outside firewall)</li>
      <li>REST interface (uses cookie or header based token)</li>
    </ol>

    <h2>Authentication</h2>
    To use the API, first <a href="sso/login">login</a> to the interactive console.
    Then go <a href="/auth/apiToken">here</a> to generate an api token.
    Include the value of this token in the header of your API REST requests using
    the key {"'x-landform-token'"}.
    API tokens are currently set to expire after 10 days but you can create more at any time.
    <br />
    <br />
    <b>Login LDAP</b><br />
    POST /auth/ldap<br />
    Body {'{"username": "yourUsername", "password": "yourPassword"'}<br />
    Logs into the interactive console using LDAP credentials if the server
    running inside the firewall. This is especially useful when debugging since
    SSO is difficult to simulate.  Instead, use Postman to post your credentials to your development
    server running on local host.  This will create a sesson
    and set cookies that subsequent api calls will use to authenticate with.
    You must be in the landform LDAP group for this to work.
    <br />
    <br />
    <b>Login SSO</b><br />
    GET /auth/sso<br />
    You will be redirected to a SSO login page and redirected on success.
    <br />
    <br />
    <b>API Token</b><br />
    GET /auth/apiToken<br />
    Returns a json object with an API token
    <br />
    <br />
    <b>Logout</b><br />
    GET /auth/logout<br />
    Clears any API cookies and ends interactive browser session, regardless of login
    method (SSO or LDAP).  Note that generated API keys will continue to work until they expire.
    <br />
    <br />
    <h1>API Documentation</h1>
    All API methods require an API key {"'x-landform-token'"} to be set in the header or a {"'landform-token'"} cookie set in the browser session
    <br />
    <br />
    Example Curl Command Syntax:
    <br />
    curl -d {'\'{"name": "test-project-name"}\''} -H  "Content-Type: application/json" -H  "x-landform-token: YOUR_API_TOKEN" -X POST http://YOUR_HOSTNAME/api/project
    

    <h2>Projects</h2>
    <b>Create a project</b><br />
    POST /api/project<br />
    Body {'{"name": "projectName"'}<br />
    Returns project object
    <br />
    <br />
    <b>List projects</b><br />

    GET /api/project<br />
    Returns a list of project names
    <br />
    <br />
    <b>View project</b><br />
    GET /api/project/projectName<br />
    Returns project object
    <br />
    <br />
    <b>Delete project</b><br />
    DELETE /api/project/projectName<br />
    Returns project object
    <br />
  </div>
);

export default Documentation;
