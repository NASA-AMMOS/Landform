import React from 'react';
import { Link } from 'react-router-dom';
import ApiEntry from './ApiEntry';

const Documentation = () => (
  <div className="documentation">

    <h2>Authentication</h2>
    <ol>
      <li>Login to the web portal using either <Link to="/ldapLogin">LDAP</Link> or <a href="/auth/sso">SSO</a> (preferred).  To use LDAP the server must be running inside the firewall and you must be a member of the <i>landform</i> LDAP group.</li>
      <li><a href="/auth/apiToken">Generate an API token</a>.  API tokens are currently set to expire after 10 days but you can create more at any time.  This will also set the <i>landform-token</i> cookie.</li>
      <li>When ready, <a href="/auth/logout">logout</a>.  Generated API keys will continue to work until they expire.  However, logging out will clear any <i>landform-token</i> cookie.</li>
    </ol>

    All API methods require an API token <i>x-landform-token</i> to be set in the header or in a <i>landform-token</i> cookie in the browser session.

    <br/><br/>

    For example, using <code>curl</code> on the command line:<br/><br/>
    <code>
      {'curl -d \'{"name": "test-project-name"}\' -H "Content-Type: application/json" -H "x-landform-token: YOUR_API_TOKEN" -X POST https://YOUR_HOSTNAME/api/project'}
    </code>

    <h2>REST API</h2>

    <ApiEntry
      title="Create Project" method="POST" route="project"
      params={[{ name: 'name', type: 'string', desc: 'project name' }]}
      returns="project object"
    />

    <ApiEntry
      title="List Projects" method="GET" route="project"
      returns="list of project names"
    />

    <ApiEntry
      title="Get Project" method="GET" route={<span>project/<i>projectName</i></span>}
      returns="project object"
    />

    <ApiEntry
      title="Delete Project" method="DELETE" route={<span>project/<i>projectName</i></span>}
      returns="project object"
    />
  </div>
);

export default Documentation;
