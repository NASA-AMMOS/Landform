import React from 'react';

const ApiEntry = (props) => {

  const method = props.method.toUpperCase();

  const params = props.params ? (
    <div>
      {method === 'POST' ? 'Body' : 'Params'}:
      {props.params.map((p, i, a) => (<span> {p.name} ({p.type}): <i>{p.desc}</i>{i < a.length - 1 ? ',' : ''}</span>))}
    </div>
  ) : null;

  return (
    <div style={{ 'margin-top': '15px' }}>
      <div><b>{props.title}</b></div>
      <div>{method} /api/{props.route}</div>
      {params}
      <div>Returns: {props.returns}</div>
    </div>
  );
};

export default ApiEntry;
