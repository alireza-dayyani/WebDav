# Security policy

## Supported versions

Security fixes are prepared for the current `main` branch of this repository. Tagged releases (when published) will note which commit they include.

| Version | Supported |
| --- | --- |
| `main` (development) | Yes |

## Reporting a vulnerability

**Do not** open public GitHub issues for security-sensitive reports.

1. Use [GitHub private vulnerability reporting](https://github.com/alireza-dayyani/WebDav/security/advisories/new) on this repository if enabled, **or**
2. Email the repository owner with a description, impact, reproduction steps, and optional patch suggestions.

We aim to acknowledge reports within a few business days. We will coordinate disclosure timing with you when a fix is ready.

## Out of scope

- Denial-of-service against a self-hosted instance without demonstrating a defect in Papyrus (rate limits and upload caps are intentional).
- Issues in upstream [WordCraft](https://github.com/storytold/wordcraft) engine code unless reproduced through Papyrus’s supported API or hosted editor path (report upstream separately when appropriate).

## Security documentation

- [`docs/SECURITY_MODEL.md`](docs/SECURITY_MODEL.md) — editor iframe and bridge boundaries.
- [`docs/SECURITY_REVIEW.md`](docs/SECURITY_REVIEW.md) — assessment snapshot (not a certification).