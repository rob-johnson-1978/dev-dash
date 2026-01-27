# dev-dash
Simple dev dashboard

-----
# Backlog
- [x] _Try hex1b (if not, figure out colouring of text in console somehow)_

- [x] Order of startup / dependencies

- [x] Kill and restart all button

- [ ] Rework "RunStatus" to use current actor behaviour to report on status, 
rather separate status state in both supervisor and runner. Still use a timer, 
but handle it in each behaviour state and report based on that instead!

- [x] Ensure logs don't get too big in UI (eg only last 1000 lines or something)

- [ ] Custom commands (eg watch vs not)

- [ ] Debugging (.NET)

- [ ] Non-.NET support (Node.js, Python, Java, etc)

- [ ] OTel (although could just advise zipkin or similar exporter + 
configure URL to display in iframe?) (or even force some existing product
or something into dll)

- [ ] Tests (Playwright)

- [ ] CLI Tool version - point at config file

- [ ] Packaging
-----

