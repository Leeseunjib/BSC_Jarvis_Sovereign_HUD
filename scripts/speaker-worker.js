'use strict';
// Native inference is isolated from the HUD/server process. No microphone files are written.
const path = require('path');
const fs = require('fs');
const crypto = require('crypto');
const { SpeakerEmbeddingExtractor } = require('sherpa-onnx-node');
const model = path.join(__dirname, '../models/speaker/wespeaker_en_voxceleb_resnet34_LM.onnx');
if (crypto.createHash('sha256').update(fs.readFileSync(model)).digest('hex') !== 'e9848563da86f263117134dfd7ad63c92355b37de492b55e325400c9d9c39012') throw new Error('speaker-model-integrity-failed');
const extractor = new SpeakerEmbeddingExtractor({ model, numThreads: 2, provider: 'cpu', debug: false });
process.on('disconnect', () => process.exit(0));
process.on('message', message => {
  try {
    const pcm = Buffer.from(message.pcm, 'base64');
    if (pcm.length < 32000 || pcm.length > 1920000 || pcm.length % 2) throw new Error('invalid-audio-length');
    const samples = new Float32Array(pcm.length / 2);
    for (let i = 0; i < samples.length; i++) samples[i] = pcm.readInt16LE(i * 2) / 32768;
    const vectors = [];
    // Check the entire turn, rather than approving a later speaker with an earlier speaker's score.
    const blocks = message.enroll ? 3 : Math.max(1, Math.ceil(samples.length / 64000));
    const size = Math.floor(samples.length / blocks);
    for (let i = 0; i < blocks; i++) {
      const raw = samples.subarray(i * size, i === blocks - 1 ? samples.length : (i + 1) * size);
      let first = -1, last = 0, active = 0, clipped = 0;
      for (let j = 0; j + 320 <= raw.length; j += 320) {
        let sum = 0;
        for (let k = j; k < j + 320; k++) { sum += raw[k] * raw[k]; if (Math.abs(raw[k]) > .995) clipped++; }
        if (Math.sqrt(sum / 320) >= .0003) { if (first < 0) first = j; last = j + 320; active += 320; }
      }
      if (clipped / raw.length > .03) throw new Error('audio-clipped');
      if (first < 0 || active < 12000) { if (message.enroll) throw new Error('insufficient-speech'); else continue; }
      const segment = raw.slice(Math.max(0, first - 1600), Math.min(raw.length, last + 1600));
      if (segment.length < 16000) throw new Error('speech-too-short');
      const stream = extractor.createStream();
      stream.acceptWaveform({ samples: segment, sampleRate: 16000 });
      stream.inputFinished();
      if (!extractor.isReady(stream)) throw new Error('speaker-model-not-ready');
      const vector = Array.from(extractor.compute(stream));
      if (vector.length !== 256 || !vector.every(Number.isFinite)) throw new Error('invalid-speaker-embedding');
      vectors.push(vector);
    }
    if (!vectors.length) throw new Error('insufficient-speech');
    process.send({ id: message.id, ok: true, vectors });
  } catch (error) { process.send({ id: message.id, ok: false, error: error.message }); }
});
