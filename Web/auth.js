const express = require('express');
const passport = require('passport');
const SamlStrategy = require('passport-saml').Strategy;
const LdapStrategy = require('passport-ldapauth');
const { createClient: createLDAPClient } = require('ldapjs');

const config = require('./config');
const logger = require('./logger');
const token = require('./token');

const router = express.Router();

router.use(passport.initialize());
router.use(passport.session());

passport.serializeUser((user, done) => { done(null, user); });
passport.deserializeUser((user, done) => { done(null, user); });

// Get API token
router.get('/auth/token', (req, res) => {
  //Content-Language header avoids Chrome offering to translate the result
  if (req.isAuthenticated()) return res.set('Content-Language', 'en').send(token.getRawToken(req));
  return res.status(401).send('not authenticated');
});

// End a users session, cleares the API cookie
// Any routes using isAuthenticated will no longer work
// However, calls to the API using the API token in the http header will still work until the API token expires
router.get('/auth/logout', (req, res) => {
  req.logout();
  token.deleteToken(res);
  req.session.destroy((err) => {
    if (err) return res.status(401).json({ error: err });
    return res.redirect('/');
  });
});

// LDAP ////////////////////////////////////////////////////////////////////////////////////////////////////////////////

function checkLDAPGroup(uid) {
  return new Promise((resolve, reject) => {
    const ldapClient = createLDAPClient({ url: config.ldap.url });
    let found = false;
    function onConnect() {
      ldapClient.search(
        config.ldap.searchBase,
        { scope: 'sub', filter: `(cn=${config.app.ldapGroup})` },
        (searchError, response) => {
          if (searchError) { ldapClient.destroy(); reject(searchError); }
          else {
            response.on('searchEntry', (e) => {
              //e.object.uniqueMember should be an array of strings like
              //uid=foobar,ou=personnel,dc=dir,dc=jpl,dc=nasa,dc=gov
              if (!found && e && e.object && e.object.uniqueMember &&
                  e.object.uniqueMember.find(m => m.split(',').find(p => p.includes(`uid=${uid}`)))) {
                found = true;
              }
            });
            response.on('end', () => { ldapClient.destroy(); resolve(found); });
            response.on('error', (err) => { ldapClient.destroy(); reject(err); });
          }
        });
    }
    ldapClient.on('connect', onConnect);
  });
}

passport.use(new LdapStrategy({ server: config.ldap }, async(user, done) => {
  done(null, await checkLDAPGroup(user.uid) ? user : false);
}));

// LDAP login, must be inside the firewall for this to work
// an api token will be generated and saved as a cookie
router.post('/auth/ldap', passport.authenticate('ldapauth', { session: true }), (req, res) => {
  token.saveToken(res, req.user.uid);
  res.redirect('/');
});

// SSO /////////////////////////////////////////////////////////////////////////////////////////////////////////////////

// SSO login only works when deployed to the production server
// because SSO setup required registering that server with the SSO service
// https://dir.jpl.nasa.gov/websso/index.php

passport.use(new SamlStrategy(
  {
    path: config.sso.saml.path,
    entryPoint: config.sso.saml.entryPoint,
    issuer: config.sso.saml.issuer,
    cert: config.sso.saml.cert,
  },
  ((profile, done) => done(
    null,
    {
      id: profile.uid,
      email: profile.email,
      displayName: profile.cn,
      firstName: profile.givenName,
      lastName: profile.sn,
    },
  )),
));

// Forward to the single signon service specified in the config file
router.get('/auth/sso', passport.authenticate(config.sso.strategy, { successRedirect: '/', failureRedirect: '/auth/sso' }));

// Single sign on callback path
// This is called by the single signon service after a successful login
// and will parse out user information from SAML data
// An api token will be generated and saved as a cookie
router.post(config.sso.saml.path,
            passport.authenticate(config.sso.strategy, { failureRedirect: '/', failureFlash: true }),
            (req, res) => {
              logger.info(`SSO login successful, uid=${req.user.uid}`);
              token.saveToken(res, req.user.uid);
              res.redirect('/');
            });

module.exports = router;
