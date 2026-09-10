# Tasks

## 1. The rule
- [x] 1.1 A framework-agnostic predicate over `Host`, its port, the bound port, and `Origin`
- [x] 1.2 Accept every spelling of this machine: `localhost`, the whole of `127.0.0.0/8`, and IPv6 loopback both bracketed and bare
- [x] 1.3 Refuse an opaque origin, a foreign host, a foreign origin, a wrong port and a missing port

## 2. The wiring
- [x] 2.1 Middleware ahead of every endpoint, health included
- [x] 2.2 Refuse with 403 rather than 404, so a misconfigured client can tell the two apart
- [x] 2.3 Bind loopback as an explicit endpoint rather than a URL string

## 3. Proof
- [x] 3.1 Unit tests over the rule, asserting what must still pass as well as what must be refused
- [x] 3.2 Verified against the running IDE with forged `Host` and `Origin` headers on a real socket
- [x] 3.3 Confirmed the listener is on the loopback addresses only, and refused on the machine's LAN address
