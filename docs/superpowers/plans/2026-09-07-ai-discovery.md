# Catalogue discovery implementation plan

**Goal:** Make the existing public catalogue easier for readers and automated clients to discover and interpret.
**Architecture:** Keep the existing read API and server-rendered site. Generate OpenAPI 3.1 response schemas from the API serializer and wire records; document query semantics explicitly. Keep all provenance and unknown-count behavior intact.
**Approved scope:** The recommendations approved in this task: schema correctness, linked API documentation, crawler policy, and related semantic/accessibility improvements.

- Add regression tests for schema types, sourced genre/language, script escaping, API contract discovery, and public crawler access.
- Correct `GameStructuredData`, retaining timestamped measured counts and demo suppression.
- Publish `/api/openapi.json`, covering public read routes, parameters, response schemas, redirects, errors, and bulk formats. Link it from `/api`.
- Add `/about/api`, using semantic headings, native links, readable examples, and explicit English language markup. Link from About; include in sitemap and discovery headers.
- Permit public API crawling while excluding account, MCP, metrics, and random routes.
- Build Release, run the Web suite, exercise the rendered documentation and machine contract, review the diff, then commit and open a PR against main.

Review also identified missing CORS preflight and header exposure for browser consumers. Add a GET-only policy to public API routes, expose validators and licence headers, and verify readable errors without exposing account routes.
