/* eslint-disable react/jsx-filename-extension */
import React from 'react';
import { Switch, Route, Link } from 'react-router-dom'
import './App.css';
import LdapLogin from './LdapLogin';
import Documentation from './Documentation';

const App = () => (
  <div className="App">
    <header className="App-header">
      <h1 className="App-title">Landform Web Console</h1>
    </header>
    <p className="App-intro">
      <a href="/auth/sso">Login (SSO)</a><br />
      <Link to='/ldapLogin'>Login (LDAP)</Link> <br />
      <a href="/auth/logout">Logout</a><br />
      <a href="/auth/apiToken">API token</a><br />
      <Link to='/documentation'>Documentation</Link>     
    </p>
    <div className="App-body">
      <Route path='/ldapLogin' component={LdapLogin} />
      <Route path='/documentation' component={Documentation} />
    </div>
  </div>
);

export default App;
