const express = require('express');

const { routeError, abortRoute, sendJson, getTask, parseBool } = require('./util');

const router = express.Router();

router.get('/:id', (req, res) => {
  try {
    const task = getTask(req.params.id);
    if (!task) throw routeError(`no task with id '${req.params.id}'`);
    sendJson(res, task.info);
  } catch (e) { abortRoute(res, 'failed to get task info', e); }
});

router.get('/:id/log', (req, res) => {
  try {
    const task = getTask(req.params.id);
    if (!task) throw routeError(`no task with id '${req.params.id}'`);
    if (!parseBool(req.query.text)) sendJson(res, task.log);
    else {
      res.contentType('text/plain');
      const txt = task.log.join('\n');
      if (!task.info.running || !parseBool(res.query.live)) res.send(txt);
      else {
        res.write(txt);
        res.listeners.push(msg => { if (msg) { res.write('\n'); res.write(msg); } else res.end(); });
      }
    }
  } catch (e) { abortRoute(res, 'failed to get task log', e, parseBool(res.query.text)); }
});

module.exports = router;
