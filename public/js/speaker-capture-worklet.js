// A bounded 16 kHz PCM stream from the same input track used by speech recognition.
class SpeakerCaptureProcessor extends AudioWorkletProcessor {
  constructor() { super(); this.sum = 0; this.count = 0; this.position = 0; this.chunk = []; }
  process(inputs) {
    const input = inputs[0] && inputs[0][0];
    if (input) for (const value of input) {
      this.sum += value; this.count++; this.position += 16000;
      if (this.position >= sampleRate) {
        this.position -= sampleRate;
        this.chunk.push(this.sum / this.count); this.sum = 0; this.count = 0;
        if (this.chunk.length === 1600) { const samples = new Float32Array(this.chunk); this.port.postMessage(samples, [samples.buffer]); this.chunk = []; }
      }
    }
    return true;
  }
}
registerProcessor('jarvis-speaker-capture', SpeakerCaptureProcessor);
