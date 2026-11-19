# Platform support policy

Avalonia's platform support tiers vary by OS release and are reviewed with framework releases. The product must publish a tested OS/architecture matrix rather than claiming generic support for every version.

The current Avalonia documentation lists Windows 11 24H2 (x64/ARM64), macOS 26 (x64/ARM64), Ubuntu 25.x, Fedora 43, and Debian 13 as Tier 1 desktop targets at the time this document was reviewed (2026-11-12). Other listed OS releases are Tier 2 or Tier 3. See https://docs.avaloniaui.net/docs/supported-platforms.

Linux currently uses X11 by default. Avalonia 12.1.0 introduced an opt-in native Wayland backend; otherwise Wayland sessions typically run through XWayland. Linux deployment prerequisites and native dependencies must be included in release documentation.

Firebird Studio publishes Windows x64 packages and runs its automated tests as part of that release workflow. Linux and macOS packages and application smoke-test results remain pending. The [Firebird compatibility matrix](FIREBIRD-COMPATIBILITY.md) separately tracks real-server validation.
