import React from 'react';
import ReactDOM from 'react-dom';
import { BrowserRouter as Router } from 'react-router-dom';
import './index.css';
import App from './App';
//import registerServiceWorker from './registerServiceWorker';
import { unregister } from './registerServiceWorker';

ReactDOM.render((<Router><App/></Router>), document.getElementById('root'));

//disable serving assets from client cache as a progressive web app
//https://goo.gl/KwvDNy
//registerServiceWorker();
unregister();

