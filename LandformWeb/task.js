const express = require('express');

const { abortRoute, sendJson, taskInfo, taskLog } = require('./util');

const router = express.Router();

router.get('/:id', (req, res) => {
  try { sendJson(res, taskInfo(req.params.id)); }
  catch (e) { abortRoute(res, 'failed to get task info', e); }
});

router.get('/:id/log', (req, res) => {
  try { res.contentType('text/plain').send(taskLog(req.params.id)); }
  catch (e) { abortRoute(res, 'failed to get task log', e); }
});

module.exports = router;
