const SamlStrategy = require('passport-saml').Strategy;

module.exports = (passport, config) => {
  passport.serializeUser((user, done) => {
    done(null, user);
  });

  passport.deserializeUser((user, done) => {
    done(null, user);
  });

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
};
