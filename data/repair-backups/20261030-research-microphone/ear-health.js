'use strict';
function normalizeEarHealth(body, now = Date.now()) {
  const text = key => String(body[key] || '').slice(0, 120);
  const number = key => Math.max(0, Math.min(1e9, Number(body[key]) || 0));
  return {
    microphone: ['starting', 'ready', 'error'].includes(body.microphone) ? body.microphone : 'starting',
    recognition: ['starting', 'restarting', 'paused', 'listening', 'error'].includes(body.recognition) ? body.recognition : 'starting',
    speechBuffer: text('speechBuffer'), workerSession: text('workerSession'), sequence: number('sequence'),
    event: text('event'), error: text('error'), lastError: text('lastError'), endReason: text('endReason'),
    gapMs: number('gapMs'), generation: number('generation'), speechActive: Boolean(body.speechActive),
    commandIntent: text('commandIntent'), commandOk: Boolean(body.commandOk), commandIgnored: Boolean(body.commandIgnored), utteranceChars: number('utteranceChars'),
    turn: body.turn ? { finalSegments: Math.max(0, Math.min(1000, Number(body.turn.finalSegments) || 0)), interimChars: Math.max(0, Math.min(12000, Number(body.turn.interimChars) || 0)), finalizing: Boolean(body.turn.finalizing) } : null,
    ts: now
  };
}
function getEarStatus(health, startedAt, now = Date.now()) {
  const ageMs = health ? Math.max(0, now - health.ts) : null;
  return { ok: true,
    connected: Boolean(health && health.microphone === 'ready' && health.recognition === 'listening' && ageMs < 15000),
    recoverable: now - startedAt > 8000 && (!health || ageMs > 20000),
    microphone: health && health.microphone || 'starting', recognition: health && health.recognition || 'starting',
    event: health && health.event || '', reason: health && health.endReason || '', error: health && health.error || '', ageMs };
}
module.exports = { normalizeEarHealth, getEarStatus };
