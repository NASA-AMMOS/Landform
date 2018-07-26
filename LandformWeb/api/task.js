const express = require('express');

const { routeError, abortRoute, sendJson, parseBool, parseArgs } = require('../routeUtil');
const { getTask } = require('../taskUtil');

const router = express.Router();

router.get('/:id', (req, res) => {
  try {
    const task = getTask(req.params.id);
    if (!task) throw routeError(`no task with id '${req.params.id}'`);
    sendJson(res, task.info);
  } catch (e) { abortRoute(res, 'error getting task info', e); }
});

router.get('/:id/log', (req, res) => {
  try {
    const task = getTask(req.params.id);
    if (!task) throw routeError(`no task with id '${req.params.id}'`);
    const { text, live } = parseArgs(req, {
      text: { type: 'bool', default: false },
      live: { type: 'bool', default: false },
    });
    if (!(text || live)) sendJson(res, task.log);
    else {
      res.contentType('text/plain');
      if (!live || !task.info.running) res.send(task.log.join('\n'));
      else {
        task.listeners.push(msg => { if (msg !== null) { res.write('\n'); res.write(msg); } else res.end(); });
        res.write(task.log.join('\n'));
      }
    }
  } catch (e) { abortRoute(res, 'error getting task log', e); }
});

module.exports = router;
