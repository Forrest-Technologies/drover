// Runs on the browser's audio thread, loaded from the app's own origin. It hands the microphone's samples to the
// page in pieces of about forty milliseconds, as they are: the page turns them into the sound a call carries.
class DroverCapture extends AudioWorkletProcessor {
    constructor() {
        super();
        this.held = new Float32Array(2048);
        this.filled = 0;
    }

    process(inputs) {
        const input = inputs[0] && inputs[0][0];
        if (!input) {
            return true;
        }

        let at = 0;
        while (at < input.length) {
            const taken = Math.min(input.length - at, this.held.length - this.filled);
            this.held.set(input.subarray(at, at + taken), this.filled);
            this.filled += taken;
            at += taken;
            if (this.filled === this.held.length) {
                this.port.postMessage(this.held.slice());
                this.filled = 0;
            }
        }

        return true;
    }
}

registerProcessor('drover-capture', DroverCapture);
