const express = require('express');
const session = require('express-session');
const MemoryStore = require('memorystore')(session);
const bodyParser = require('body-parser');
const cookieParser = require('cookie-parser');
const cors = require('cors');
const path = require('path');

const config = require('./config');
const logger = require('./logger');
const { tilingMaster } = require('./tilingUtil');
const { abortRoute } = require('./routeUtil');
const authRouter = require('./auth');
const token = require('./token');
const projectRouter = require('./api/project');
const taskRouter = require('./api/task');

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
app.use(authRouter);

//serve API routes
const apiRouter = express.Router();
apiRouter.use(token.apiTokenCheck);
apiRouter.use('/projects', projectRouter);
apiRouter.use('/tasks', taskRouter);
apiRouter.use('*', (req, res) => abortRoute(res, `unrecognized API '${req.originalUrl}'`, 400));
app.use('/api', apiRouter);

//TODO favicon; for now return 204 no content
app.get('/favicon.ico', (req, res) => res.status(204));

//serve webpacked client but in production only
//for dev the client is served by a separate server on localhost:3000 which does hot module reloading
//that dev server proxies certain routes back to this server as configured in client/package.json
if (app.get('env') === 'production') app.use('/', express.static(path.join(__dirname, 'client', 'build')));

//serve public dir of client but in development only
//this is so that e.g. http://localhost:8081/viewer/index.html works, which is what the project/view API returns
if (app.get('env') === 'development') app.use('/', express.static(path.join(__dirname, 'client', 'public')));

logger.info(`NODE_ENV: ${app.get('env')}`);
logger.info(`LDAP group: ${config.app.ldapGroup}`);
logger.info(`Landform venue: ${config.app.venueName}`);
logger.info(`S3 URL: ${config.app.s3Url}`);
logger.info(`AWS Region: ${config.app.awsRegion}`);
logger.info(`AWS Profile: ${config.app.awsProfile}`);
logger.info(`AWS MSLICE Profile: ${config.app.awsMSLICEProfile}`);
logger.info(`AWS MSLICE S3 URL: ${config.app.awsMSLICES3Url}`);

tilingMaster().then(() => {
  logger.info('launched TilingServer master');
  app.listen(config.app.port, () => logger.info(`${config.app.name} listening on port ${config.app.port}`));
});
