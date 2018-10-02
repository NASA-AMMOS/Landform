import React from 'react';
import { Link } from 'react-router-dom';

const Documentation = () => (
  <div className="documentation">
    <h2>Authentication</h2>
    <ol>
      <li>Login to the web portal using either <Link to="/ldapLogin">LDAP</Link> or <a href="/auth/sso">SSO</a> (preferred).  To use LDAP the server must be running inside the firewall and you must be a member of the <i>landform</i> LDAP group.</li>
      <li>An <a href="/auth/token">API token</a> will be generated upon successful login and stored in the <i>landform-token</i> cookie. API tokens are currently set to expire after 10 days but you can create more at any time by logging in again.</li>
      <li>When ready, <a href="/auth/logout">logout</a>.  Generated API keys will continue to work until they expire.  However, logging out will clear any <i>landform-token</i> cookie.</li>
    </ol>
  </div>
);

export default Documentation;
