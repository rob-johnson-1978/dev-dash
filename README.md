# dev-dash
Simple dev dashboard

-----
# Backlog
- [ ] Get rid of code setup, use appsettings.json or YAML - start with single-file .NET 10 thing

- [ ] Installable and/or CLI Tool version

- [ ] Colours in node/py/go app output?

- [ ] Standard start / url detection regexes

- [ ] Get rid of dead letters on restart (I think it's ProcessExited)

- [ ] Rework "RunStatus" to use current actor behaviour to report on status, 
rather separate status state in both supervisor and runner. Still use a timer, 
but handle it in each behaviour state and report based on that instead!

- [ ] OTel (although could just advise zipkin or similar exporter + 
configure URL to display in iframe?) (or even force some existing product
or something into dll)

- [ ] tilt-like view of compose, on new page

- [ ] Tests (Playwright)

- [ ] Packaging
-----

