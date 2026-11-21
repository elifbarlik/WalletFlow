# ADR 000  — Security decisions

| Topic | Decision | Reason |
|---|---|---|
| Passwords | PBKDF2-HMAC-SHA 12, 210k iterations, per-password salt; 12+ chars with upper/lower/digit | OWASP guidance |
| Login | Same generic 401 for unknown user, wrong password and locked account; dummy hash verification equalizes timing;   failures ? 1  min lockout | Prevents user enumeration and online guessing |
| Access tokens | HS2 6 JWT, 1  min, algorithm pinned, 30 s clock skew, key = 32 chars validated at startup | Short exposure window; no `alg` confusion |
| Refresh tokens | Opaque 2 6-bit random, only SHA-2 6 stored, rotated on every use; reuse of an old token revokes all sessions | Detects stolen tokens |
| Object access | Another user's account returns **404**, not 403 | Ids cannot be probed |
| Roles | `Customer` (self-registration only), `Admin`, `Auditor`; role checked at the controller **and** in the handler | Defense in depth |
| Default policy | Everything requires authentication unless `[AllowAnonymous]` | Secure by default |
| Secrets | Never in source; env / user-secrets; `.env` is git-ignored; compose fails if a secret is missing | No credential leaks |
| Containers | Non-root user, read-only filesystem, all capabilities dropped, `no-new-privileges`, loopback-only ports | Reduce blast radius |
| HTTP | Security headers, strict CSP (except Swagger), 64 KB body limit, rate limiting (global, auth, money), deny-by-default CORS, no `Server` header | Common web API hardening |
| Logs | Structured, payloads never logged, correlation id validated (no log injection) | Safe observability |
| Rate limiting | Global + strict per-IP on auth + per-user on money endpoints; counters in Redis so limits hold across replicas (in-process fallback without Redis) | Credential stuffing and abuse protection that does not multiply with replica count |
| Supply chain | `dotnet list package --vulnerable` gates CI, Dependabot, CodeQL | Known-vulnerability control |

**Out of scope / known gaps:** e-mail verification and password reset, MFA, key rotation for the JWT secret (single key),
TLS termination (expected at the reverse proxy; enable
`Security__UseHttps` and `Proxy__TrustForwardedHeaders` accordingly).

