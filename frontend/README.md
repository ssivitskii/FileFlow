# FileFlow Explorer

The Angular 22 client uses only same-origin `/api` requests. During development, `npm start` proxies them to the loopback API at `http://127.0.0.1:5084`. Every API call includes the non-simple `X-FileFlow-Client: web` resource-gate header; it is not an authentication credential.

```bash
npm install
npm start
```

The interface browses the configured workspace, renders text by interpolation inside `<pre>`, scans bounded duplicate candidates, shows redacted history, and previews operations. It cannot execute or undo operations.
