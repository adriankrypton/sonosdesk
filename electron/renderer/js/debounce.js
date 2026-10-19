// Like the WPF VolumeDebouncer: sends the latest value 180 ms after the last change and keeps
// the local value for 2.5 s so polled state does not make a dragged slider jump back.
export class Debouncer {
  constructor(apply, { delay = 180, hold = 2500 } = {}) {
    this.apply = apply;
    this.delay = delay;
    this.hold = hold;
    this.lastChange = 0;
    this.timer = null;
    this.chain = Promise.resolve();
  }

  get holding() {
    return this.timer !== null || Date.now() - this.lastChange < this.hold;
  }

  submit(value, onError) {
    this.lastChange = Date.now();
    clearTimeout(this.timer);
    this.timer = setTimeout(() => {
      this.timer = null;
      // Requests go out one after another so an older value never overtakes a newer one.
      this.chain = this.chain
        .then(() => this.apply(value))
        .then(() => {
          this.lastChange = Date.now();
        })
        .catch((error) => onError?.(error));
    }, this.delay);
  }
}

const registry = new Map();

export function debouncer(key, apply, options) {
  let entry = registry.get(key);
  if (!entry) {
    entry = new Debouncer(apply, options);
    registry.set(key, entry);
  }

  entry.apply = apply;
  return entry;
}

export function isHolding(key) {
  return registry.get(key)?.holding === true;
}
