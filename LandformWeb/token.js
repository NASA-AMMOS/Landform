const jwt = require('jsonwebtoken');
const config = require('./config');

const landformRequestHeaderKey = 'x-landform-token';
const landformCookieKey = 'landform-token';
const tokenLifespan = 864000; // seconds

function makeToken(uid) {
  const tokenPayload = {
    uid,
  };
  const options = {
    expiresIn: tokenLifespan,
  };
  return jwt.sign(tokenPayload, config.app.tokenSecret, options);
}

function getRawToken(req) {
  let cookieList = req.cookies;
  if (config.app.sessionCookieSecure) {
    cookieList = req.signedCookies;
  }
  return req.headers[landformRequestHeaderKey] || cookieList[landformCookieKey];
}

function apiTokenCheck(req, res, next) {
  const rawToken = getRawToken(req);
  if (!rawToken) {
    res.send(401, `missing "${landformRequestHeaderKey}" api token header and no "${landformCookieKey}" cookie is set`);
  } else {
    try {
      const tokenPayload = jwt.verify(rawToken, config.app.tokenSecret);
      req.user = { uid: tokenPayload.uid };
      next();
    } catch (e) {
      res.send(401, `invalid api token: ${e.message}`);
    }
  }
}

function saveToken(res, uid) {
  res.cookie(landformCookieKey, makeToken(uid), {
    maxAge: tokenLifespan * 1000, // Convert seconds to milliseconds
    signed: config.app.sessionCookieSecure,
    secret: config.app.sessionCookieSecret,
  });
}

function deleteToken(res) {
  res.clearCookie(landformCookieKey);
}

module.exports = {
  saveToken,
  apiTokenCheck,
  getRawToken,
  deleteToken,
};
