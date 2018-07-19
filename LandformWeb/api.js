const express = require('express');
const token = require('./token');
const pipleineRouter = require('./pipeline')();

// Configure rest API endpoints.  These are sessionless and
// are authenticated by using an api key (token) specified in the header or
// by a cookie stored in the browser
module.exports = (app) => {
  const database = {
    projects: {},
  };

  const apiRouter = express.Router();
  // Add middleware to check for authenticated token on any endpoints defined from here on out
  apiRouter.use(token.apiTokenCheck);
  app.use('/api', apiRouter);

  const projectRouter = express.Router();
  // Create a new project
  projectRouter.post('/', (req, res) => {
    if ('name' in req.body) {
      if (req.body.name in database.projects) {
        res.send(400, 'project already exists');
      } else {
        database.projects[req.body.name] = req.body;
        res.send(201, req.body);
      }
    } else {
      res.send(400, 'name property not found in body of request');
    }
  });
  // List projects
  projectRouter.get('/', (req, res) => {
    res.send(Object.keys(database.projects));
  });
  // Get a project
  projectRouter.get('/:id', (req, res) => {
    if (req.params.id in database.projects) {
      res.send(database.projects[req.params.id]);
    } else {
      res.send(404, 'project does not exist');
    }
  });
  // Update a project
  // projectRouter.patch('/:id', (req, res) => {
  //   res.send('REST in peace');
  // });
  // Delete a project
  projectRouter.delete('/:id', (req, res) => {
    if (req.params.id in database.projects) {
      const data = database.projects[req.params.id];
      delete database.projects[req.params.id];
      res.send(data);
    } else {
      res.send(404, 'project does not exist');
    }
  });
  apiRouter.use('/project', projectRouter);
  apiRouter.use('/pipeline', pipleineRouter);
};
