const fs = require('fs-extra');
const path = require('path');
const { createLogger, format, transports } = require('winston');
require('winston-daily-rotate-file');

const config = require('./config');

if (!fs.existsSync(config.app.logDir)) fs.mkdirSync(config.app.logDir);

const logger = createLogger({
  level: config.app.logLevel || 'silly',
  transports: [
    new transports.Console({ format: format.combine(format.colorize(), format.simple()) }),
    new transports.DailyRotateFile({
      format: format.combine(format.timestamp(),
                             format.align(),
                             format.printf(info => `${info.timestamp} ${info.level}: ${info.message}`)),
      filename: path.join(config.app.logDir, `${config.app.name}-server-%DATE%.log`),
      localTime: true,
      maxSize: '10m',
      maxFiles: 10,
    }),
  ],
});

//stream binding point for other logging systems
logger.stream = {
  write(message, encoding) {
    message = typeof message === 'string' ? message : message.toString(encoding);
    logger.info(message.replace(/(\r?\n|\r)$/, '')); //strip trailing newline characters, info() will add its own
  },
  emit(ev, data) { if (ev === 'error') logger.error(data); },
  end() { /* NOOP */ },
  on() { /* NOOP */ },
  once() { /* NOOP */ },
  removeListener() { /* NOOP */ },
};

//log exceptions to console only and only if log level is debug or higher
//TODO maybe rework this so that the exception goes into the regular log transports
//for now leaving it like this because console.error() formats the exception better anyway
logger.exception = (err) => {
  if (logger.levels[logger.level] >= logger.levels.debug) console.error(err);
};

module.exports = logger;
