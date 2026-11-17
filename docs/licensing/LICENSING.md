# Licensing policy

The original Firebird Studio source code is licensed under MIT.

That license does not replace or override licenses of third-party packages, fonts, native libraries, Firebird utilities, or other bundled assets. A release must include applicable notices and a complete third-party license inventory.

Before each release:
1. Restore packages using the committed central package version file.
2. Capture direct and transitive dependency versions.
3. Inspect NuGet license metadata and upstream license files.
4. Inspect native/runtime assets and bundled fonts.
5. Generate and review an SBOM.
6. Include all required notices and source-offer/availability material.
7. Reject dependencies with unclear or incompatible terms.

This document is engineering guidance, not legal advice.
