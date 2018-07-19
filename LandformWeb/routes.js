const token = require('./token');

// This module controls routes that are used during an interactive browser session
// These routes can use session varaibles and the isAuthenticated method
module.exports = (app, config, passport) => {
  // This supports loggin in through LDAP, must be inside the firewall for this to work
  // An api token will be generated and saved as a cookie
  app.post('/auth/ldap', passport.authenticate('ldapauth', { session: true }), (req, res) => {
    token.saveToken(res, req.user.uid);
    res.redirect('/');
  });

  // This route will forward to the single signon service specified in the config file
  app.get('/auth/sso', passport.authenticate(
    config.sso.strategy,
    {
      successRedirect: '/',
      failureRedirect: '/auth/sso',
    },
  ));

  // Single sign on callback path
  // This is called by the single signon service after a successful login
  // and will parse out user information from SAML data
  // An api token will be generated and saved as a cookie
  app.post(
    config.sso.saml.path,
    passport.authenticate(
      config.sso.strategy,
      {
        failureRedirect: '/',
        failureFlash: true,
      },
    ),
    (req, res) => {
      token.saveToken(res, req.user.uid);
      res.redirect('/');
    },
  );

  // End a users session, cleares the API cookie and
  // any routes using isAuthenticated will no longer work.
  // However, calls to the API using the API token in the http header
  // will still work until the API token expires
  app.get('/auth/logout', (req, res) => {
    req.logout();
    token.deleteToken(res);
    req.session.destroy((err) => {
      if (err) {
        return res.status(401).json({
          error: err,
        });
      }
      return res.redirect('/');
    });
  });

  // Sample route that requires authentication
  app.get('/auth/apiToken', (req, res) => {
    if (req.isAuthenticated()) {
      return res.json({ token: token.getRawToken(req) });
    }
    return res.send(401, 'Not authenticated');
  });
};
