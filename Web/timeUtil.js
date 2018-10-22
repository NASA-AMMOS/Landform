function parseSec(duration) {
  if (!duration || duration.length < 1) return 0;
  if (duration === 'help') return 'N|Ns|Nm|Nh|Nd|Nw|No|Ny';
  const suffix = duration[duration.length - 1].toLowerCase();
  let sec = parseInt(duration);
  switch (suffix) {
    case 's': default: break;
    case 'm': sec *= 60; break;
    case 'h': sec *= 60 * 60; break;
    case 'd': sec *= 60 * 60 * 24; break;
    case 'w': sec *= 60 * 60 * 24 * 7; break;
    case 'o': sec *= 60 * 60 * 24 * 7 * 31; break;
    case 'y': sec *= 60 * 60 * 24 * 7 * 365; break;
  }
  return sec;
}

//yyyyMMddHHmmssZ[+-]zz
function timeStamp(timeOrDate) {
  const d = (timeOrDate instanceof Date) ? timeOrDate : timeOrDate > 0 ? new Date(timeOrDate) : new Date();
  const tzo = -d.getTimezoneOffset();
  const pad2 = n => { n = Math.floor(Math.abs(n)); return (n < 10 ? '0' : '') + n; };
  return `${d.getFullYear()}${pad2(d.getMonth() + 1)}${pad2(d.getDate())}` +
         `${pad2(d.getHours())}${pad2(d.getMinutes())}${pad2(d.getSeconds())}` +
         `Z${tzo >= 0 ? '+' : '-'}${pad2(tzo / 60)}`;
}

function timeFmt(ms) {
  if (ms <= 0) return '0s';
  const msInS = 1000, msInM = 60 * msInS, msInH = 60 * msInM, msInD = 24 * msInH, msInY = 365 * msInD;
  const y = Math.floor(ms / msInY); ms -= y * msInY; const yy = y > 0 ? `${y}y ` : '';
  const d = Math.floor(ms / msInD); ms -= d * msInD; const dd = d > 0 ? `${d}d ` : '';
  const h = Math.floor(ms / msInH); ms -= h * msInH; const hh = h > 0 ? `${h}h ` : '';
  const m = Math.floor(ms / msInM); ms -= m * msInM; const mm = m > 0 ? `${m}m ` : '';
  const s = Math.floor(ms / msInS); ms -= s * msInS; const ss = s > 0 || ms > 0 ? `${s}.${ms}s` : '';
  return `${yy}${dd}${hh}${mm}${ss}`;
}

module.exports = { parseSec, timeStamp, timeFmt };
