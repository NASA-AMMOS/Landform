const { createClient } = require('ldapjs');
const LdapStrategy = require('passport-ldapauth');

module.exports = (config, passport) => {
  function readFromLDAP() {
    return new Promise((resolve, reject) => {
      const ldapClient = createClient({
        url: config.ldap.url,
      });
      function onConnect() {
        ldapClient.search(config.ldap.searchBase, {
          scope: 'sub',
          filter: '(cn=landform)',
        }, (searchError, response) => {
          if (searchError) {
            reject(searchError);
          } else {
            let results = [];
            response.on('error', (responseError) => {
              reject(responseError);
            });
            response.on('searchEntry', (entry) => {
              // Note, this will BLOWUP if the ldap group as 1 or fewer members
              // If this is crashing, add another person to your ldap group
              results = results.concat(entry.object.uniqueMember.map((member) => {
                const matches = /uid=((\w|\d)+)/.exec(member);
                // If we didn't get a match, null it out so we know something's wrong
                return matches.length > 1 ? matches[1] : null;
              }));
            });
            response.on('end', () => {
              ldapClient.destroy();
              resolve(results);
            });
          }
        });
      }
      ldapClient.on('connect', onConnect);
    });
  }

  function setupLDAPAuth() {
    // TODO: Check an LDAP group to determine access to the current project being viewed.
    passport.use(new LdapStrategy({ server: config.ldap }, async(user, done) => {
      const results = await readFromLDAP();
      if (results.indexOf(user.uid) >= 0) {
        done(null, user);
      } else {
        done(null, false);
      }
    }));
    passport.serializeUser((user, done) => done(null, user));
  }
  setupLDAPAuth();
};
