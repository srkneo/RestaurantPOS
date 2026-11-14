// Stand-in for @microsoft/signalr in unit tests: no network, handlers can be fired by hand.
export const handlers = {};

export class HubConnectionBuilder {
  withUrl() { return this; }
  withAutomaticReconnect() { return this; }
  build() {
    return {
      on: (name, fn) => { handlers[name] = fn; },
      onreconnecting: () => {},
      onreconnected: () => {},
      onclose: () => {},
      start: () => Promise.resolve(),
      stop: () => Promise.resolve(),
    };
  }
}
