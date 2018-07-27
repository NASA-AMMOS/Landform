const express = require('express');

const router = express.Router();

const projects = {};

// Create a new project
router.post('/', (req, res) => {
  if ('name' in req.body) {
    if (req.body.name in projects) res.status(400).send('project already exists');
    else {
      const name = req.body.name;
      const proj = { name };
      projects[name] = proj;
      res.status(201).send(proj);
    }
  } else res.status(400).send('name property not found in body of request');
});

// List projects
router.get('/', (req, res) => { res.send(Object.keys(projects)); });

// Get a project
router.get('/:name', (req, res) => {
  if (req.params.name in projects) res.send(projects[req.params.name]);
  else res.status(404).send('project does not exist');
});

// Update a project
//projectRouter.patch('/:name', (req, res) => { res.send('REST in peace'); });

// Delete a project
router.delete('/:name', (req, res) => {
  if (req.params.name in projects) {
    const proj = projects[req.params.name];
    delete projects[req.params.name];
    res.send(proj);
  } else res.status(404).send('project does not exist');
});

module.exports = router;
