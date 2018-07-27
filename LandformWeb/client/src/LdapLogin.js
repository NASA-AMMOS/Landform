import React from 'react';

const LdapLogin = () => (
  <div className="LdapLogin">
    <h1>LDAP Login</h1>
    <form id="contact_form" action="/auth/ldap" method="post">
      Username: <input type="text" name="username"/><br/><br/>
      Password: <input type="password" name="password"/><br/><br/>
      <input type="submit" value="Submit"/>
    </form>
  </div>
);

export default LdapLogin;
