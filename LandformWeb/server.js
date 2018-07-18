const express = require('express');
const session = require('express-session');
const MemoryStore = require('memorystore')(session);
const bodyParser = require('body-parser');
const cookieParser = require('cookie-parser');
const cors = require('cors');
const passport = require('passport');
const path = require('path');

const config = require('./config/config');

const app = express();
app.use(cors());
app.use(cookieParser(config.app.sessionCookieSecret));
app.use(bodyParser.json());
app.use(bodyParser.urlencoded({
  extended: true,
}));
// Since we run this server on elastic beanstalk we need
// to trust the first proxy in order to use cookie.secure
// See https://github.com/expressjs/session#cookie-options
// and https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/nodejs-platform-proxy.html
app.set('trust proxy', 1); // trust first proxy
app.use(session({
  store: new MemoryStore({ checkPeriod: config.app.sessionTimeout }), //override default store to avoid memory leaks
  resave: false,
  saveUninitialized: true,
  secret: config.app.sessionCookieSecret,
  cookie: {
    secure: config.app.sessionCookieSecure,
    maxAge: config.app.sessionTimeout,
  },
}));
app.use('/', express.static(path.join(__dirname, 'client/build')));
app.use(passport.initialize());
app.use(passport.session());

// Enable SSO auth
require('./config/sso')(passport, config);
// Enable ldap auth
require('./config/ldap')(config, passport);
// Configure routes for interactive pages
require('./config/routes')(app, config, passport);
// Configure api
require('./config/api')(app);

// development error handler
// will print stacktrace
if (app.get('env') === 'development') {
  app.use((err, req, res) => {
    res.status(err.status || 500);
    res.render('error', {
      message: err.message,
      error: err,
    });
  });
}
// production error handler
// no stacktraces leaked to user
app.use((err, req, res) => {
  res.status(err.status || 500);
  res.render('error', {
    message: err.message,
    error: {},
  });
});

app.listen(config.app.port, () => {
  console.log(`Express server listening on port ${config.app.port}`);
});
