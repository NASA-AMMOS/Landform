const express = require('express');
const session = require('express-session');
const MemoryStore = require('memorystore')(session);
const bodyParser = require('body-parser');
const cookieParser = require('cookie-parser');
const cors = require('cors');
const path = require('path');

const config = require('./config');
const authRouter = require('./auth');
const token = require('./token');
const projectRouter = require('./project');

const app = express();

app.use(cors());

app.use(bodyParser.json());
app.use(bodyParser.urlencoded({ extended: true }));

// Since we run this server on elastic beanstalk we need to trust the first proxy in order to use cookie.secure
// See https://github.com/expressjs/session#cookie-options
// and https://docs.aws.amazon.com/elasticbeanstalk/latest/dg/nodejs-platform-proxy.html
if (app.get('env') === 'production') app.set('trust proxy', 1);

app.use(cookieParser(config.app.sessionCookieSecret));

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

//serve auth routes - login, logout, get token
app.use('/auth', authRouter);

//serve REST API routes
const apiRouter = express.Router();
apiRouter.use(token.apiTokenCheck);
apiRouter.use('/project', projectRouter);
app.use('/api', apiRouter);

//serve webpacked client but in production only
//for dev the client is served by a separate server on localhost:3000 which does hot module reloading
//that dev server proxies certain routes back to this server as configured in client/package.json
if (app.get('env') === 'production') app.use('/', express.static(path.join(__dirname, 'client', 'build')));

app.listen(config.app.port, () => { console.log(`${config.app.name} server listening on port ${config.app.port}`); });
