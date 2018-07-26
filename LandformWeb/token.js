const jwt = require('jsonwebtoken');

const config = require('./config');

function makeToken(uid) {
  return jwt.sign({ uid }, config.app.tokenSecret, { expiresIn: config.app.tokenLifespan });
}

function getRawToken(req) {
  const cookies = config.app.sessionCookieSecure ? req.signedCookies : req.cookies;
  return req.headers[config.app.tokenHeader] || cookies[config.app.tokenCookie];
}

function decodeToken(rawToken) { return jwt.verify(rawToken, config.app.tokenSecret); }

function apiTokenCheck(req, res, next) {
  const rawToken = getRawToken(req);
  if (!rawToken) {
    res.status(401).send(`missing "${config.app.tokenHeader}" header and no "${config.app.tokenCookie}" cookie is set`);
  } else {
    try {
      req.apiToken = decodeToken(rawToken);
      next();
    } catch (e) {
      res.status(401).send(`invalid api token: ${e.message}`);
    }
  }
}

function saveToken(res, uid) {
  res.cookie(config.app.tokenCookie, makeToken(uid), {
    maxAge: config.app.tokenLifespan * 1000, // seconds to milliseconds
    signed: config.app.sessionCookieSecure,
    secret: config.app.sessionCookieSecret,
  });
}

function deleteToken(res) { res.clearCookie(config.app.tokenCookie); }

module.exports = { saveToken, apiTokenCheck, getRawToken, deleteToken, decodeToken };
