const express = require('express');

const { routeError, abortRoute, sendJson, parseArgs } = require('../routeUtil');
const { getTask } = require('../taskUtil');
const { tilingMaster } = require('../tilingUtil');

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
        task.listeners.push(msg => { if (msg !== null) res.write(`\n${msg}`); else res.end(); }); //'' is falsey
        res.write(task.log.join('\n'));
      }
    }
  } catch (e) { abortRoute(res, 'error getting task log', e); }
});

router.get('/master/id', async(req, res) => sendJson(res, (await tilingMaster()).info.id));

module.exports = router;
