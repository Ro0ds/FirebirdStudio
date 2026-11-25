# Third-party dependency inventory

This inventory covers direct package references and centrally pinned versions, including the pinned transitive CliWrap dependency. The NuGet restore graph provides the full list of resolved transitive packages.

| Package | Pinned version | Upstream / package evidence | Declared package license | Notes |
|---|---:|---|---|---|
| Avalonia | 12.1.6 | https://github.com/AvaloniaUI/Avalonia · https://www.nuget.org/packages/Avalonia/12.1.6 | MIT | .NET 10 compatible; NuGet page lists MIT |
| Avalonia.Controls.ColorPicker | 12.1.6 | https://www.nuget.org/packages/Avalonia.Controls.ColorPicker/12.1.6 | MIT | Visual color picker and palette from the Avalonia upstream project |
| Avalonia.Desktop | 12.1.6 | https://www.nuget.org/packages/Avalonia.Desktop/12.1.6 | MIT | Pulls platform-specific Avalonia packages including Native, Win32, X11, Skia and HarfBuzz |
| Avalonia.Themes.Fluent | 12.1.6 | https://www.nuget.org/packages/Avalonia.Themes.Fluent/12.1.6 | MIT | Same Avalonia upstream project |
| Avalonia.Fonts.Inter | 12.1.6 | https://www.nuget.org/packages/Avalonia.Fonts.Inter/12.1.6 | MIT | Package license is MIT; bundled Inter font asset is separately licensed under SIL Open Font License 1.1 and its notice must be preserved |
| Avalonia.AvaloniaEdit | 12.0.0 | https://github.com/AvaloniaUI/AvaloniaEdit · https://www.nuget.org/packages/Avalonia.AvaloniaEdit/12.0.0 | MIT | NuGet package targets .NET 8 and is compatible with .NET 10; depends on Avalonia >= 12.0.0 |
| FirebirdSql.Data.FirebirdClient | 10.3.4 | https://github.com/FirebirdSQL/NETProvider · https://www.nuget.org/packages/FirebirdSql.Data.FirebirdClient/10.3.4 | IDPL 1.0 (upstream `license.txt`) | Not MIT. Read and preserve upstream license and notices. The project references the package and does not modify or vendor provider source. |
| Latchkey | 0.1.0 | https://github.com/kerem-acer/Latchkey · https://www.nuget.org/packages/Latchkey/0.1.0 | MIT | Uses native Windows Credential Manager, macOS Keychain, and Linux Secret Service. Early 0.1.0 release with limited adoption; provisionally accepted behind `ISecureCredentialStore`, pending real platform tests. NuGet declares `CliWrap >= 3.10.2` for net10.0. |
| CliWrap | 3.10.5 | https://github.com/Tyrrrz/CliWrap · https://www.nuget.org/packages/CliWrap/3.10.5 | MIT | Transitive dependency of Latchkey, centrally pinned to 3.10.5. The net10.0 asset declares no further package dependencies. Firebird Studio does not select Latchkey's command-based `Pass` or `SystemdCreds` backends. |
| Microsoft.NET.Test.Sdk | 18.11.0 | https://www.nuget.org/packages/Microsoft.NET.Test.Sdk/18.11.0 | MIT | Test-only VSTest integration. Its net8.0 asset depends on Microsoft.CodeCoverage 18.11.0 and Microsoft.TestPlatform.TestHost 18.11.0; TestHost depends on Microsoft.TestPlatform.ObjectModel 18.11.0. Not referenced by shipping application projects. |
| xunit.v3.mtp-off | 4.0.2 | https://www.nuget.org/packages/xunit.v3.mtp-off/4.0.2 | Apache-2.0 | Test-only xUnit v3 framework with Microsoft Testing Platform integration disabled, avoiding MTP-specific runtime/telemetry dependencies. Transitive packages are centrally pinned: xunit.v3.core.mtp-off 4.0.2, xunit.v3.assert 4.0.2, xunit.analyzers 2.2.0, xunit.v3.extensibility.core 4.0.2, xunit.v3.runner.inproc.console 4.0.2, xunit.v3.runner.common 4.0.2, and xunit.v3.common 4.0.2. |
| xunit.runner.visualstudio | 4.0.1 | https://www.nuget.org/packages/xunit.runner.visualstudio/4.0.1 | Apache-2.0 | Test-only VSTest adapter. Its net8.0 asset declares no package dependencies. |
| Microsoft.CodeCoverage | 18.11.0 | https://www.nuget.org/packages/Microsoft.CodeCoverage/18.11.0 | MIT | Test-only code-coverage tooling brought transitively by Microsoft.NET.Test.Sdk; no further package dependencies. Its package contents are test tooling and are not referenced by shipping app projects. |
| Microsoft.TestPlatform.TestHost | 18.11.0 | https://www.nuget.org/packages/Microsoft.TestPlatform.TestHost/18.11.0 | MIT | Test-only host brought transitively by Microsoft.NET.Test.Sdk; net8.0 asset depends on Microsoft.TestPlatform.ObjectModel 18.11.0. |
| Microsoft.TestPlatform.ObjectModel | 18.11.0 | https://www.nuget.org/packages/Microsoft.TestPlatform.ObjectModel/18.11.0 | MIT | Test-only object model dependency; its net8.0 asset declares no further package dependencies. |
| xunit.analyzers | 2.2.0 | https://www.nuget.org/packages/xunit.analyzers/2.2.0 | Apache-2.0 | Test-only analyzer dependency of xunit.v3.mtp-off. |
| xunit.v3.core.mtp-off | 4.0.2 | https://www.nuget.org/packages/xunit.v3.core.mtp-off/4.0.2 | Apache-2.0 | Test-only xUnit core without Microsoft Testing Platform integration; depends on xunit.v3.extensibility.core and xunit.v3.runner.inproc.console 4.0.2. |
| xunit.v3.assert | 4.0.2 | https://www.nuget.org/packages/xunit.v3.assert/4.0.2 | Apache-2.0 | Test-only assertion library. |
| xunit.v3.extensibility.core | 4.0.2 | https://www.nuget.org/packages/xunit.v3.extensibility.core/4.0.2 | Apache-2.0 | Test-only core extensibility dependency; depends on xunit.v3.common 4.0.2. |
| xunit.v3.runner.inproc.console | 4.0.2 | https://www.nuget.org/packages/xunit.v3.runner.inproc.console/4.0.2 | Apache-2.0 | Test-only in-process runner; depends on xunit.v3.extensibility.core and xunit.v3.runner.common 4.0.2. |
| xunit.v3.runner.common | 4.0.2 | https://www.nuget.org/packages/xunit.v3.runner.common/4.0.2 | Apache-2.0 | Test-only runner support dependency; depends on xunit.v3.common 4.0.2. |
| xunit.v3.common | 4.0.2 | https://www.nuget.org/packages/xunit.v3.common/4.0.2 | Apache-2.0 | Test-only shared xUnit library. |

## Important Firebird provider license note

The Firebird .NET provider upstream repository contains an `Initial Developer's Public License (IDPL) Version 1.0` license file. It is free/open-source but has obligations different from MIT/Apache/BSD. Do not describe this provider as MIT. Do not modify or vendor provider code without reviewing the IDPL obligations.

## Native and transitive assets

`Avalonia.Desktop` declares dependencies on Avalonia.Native, Avalonia.Win32, Avalonia.X11, Avalonia.Skia, and Avalonia.HarfBuzz. The actual resolved graph can include Skia/SkiaSharp and platform-native assets. Each resolved package and every bundled native binary must be audited from the restored lock graph and its notices before publishing.

No claim is made that the complete transitive dependency graph has been audited. A release is blocked until a restore-generated SBOM and all applicable third-party notices are reviewed.

## Dependency policy

- No paid package, subscription, trial, or commercial runtime license.
- Prefer OSI-approved permissive licenses (MIT, Apache-2.0, BSD-2/3-Clause, ISC, OFL for fonts).
- Other free/open-source licenses require an explicit compatibility review.
- Never relicense third-party code under this repository's MIT license.
- Preserve third-party copyright and license notices.
- Audit transitive dependencies and native assets before publishing releases.
- Recheck licenses on every dependency update.

## Credential-store dependency decision

`Latchkey` 0.1.0 is now used provisionally behind the application-owned `ISecureCredentialStore` abstraction. Its NuGet metadata declares MIT and .NET 10 compatibility; the documented native backends are Windows Credential Manager, macOS Keychain, and Linux Secret Service. It is an early release (one published version and low repository adoption at review time), so this is not a final release approval. Native round-trip, unavailable-store, deletion, and failure-mode tests are required on all three operating systems before release.

NuGet declares `CliWrap >= 3.10.2` for Latchkey's net10.0 asset. This project centrally pins the transitive dependency to 3.10.5. CliWrap 3.10.5 is MIT-licensed and its net10.0 asset declares no further package dependencies. The app uses Latchkey's native `Auto` backend only; it does not select the optional plaintext `File`, command-based `Pass`, or `SystemdCreds` backends. Latchkey's default native backend has no bundled per-RID native assets, but Linux requires the system `libsecret` library and an active Secret Service provider (for example GNOME Keyring or KWallet). Windows uses Credential Manager; macOS uses Keychain Services.

This is a bounded adapter decision, not a claim that Latchkey has been fully source-audited or independently security-certified. Its API and platform behavior must be re-evaluated before a production release. See https://www.nuget.org/packages/Latchkey/0.1.0, https://github.com/kerem-acer/Latchkey, and https://www.nuget.org/packages/CliWrap/3.10.5.

`GnomeStack.Secrets.OperatingSystem` 0.1.6 remains evaluated but not adopted because its exact transitive graph and platform-specific behavior were not audited. See https://www.nuget.org/packages/GnomeStack.Secrets.OperatingSystem/0.1.6.


## Test-only dependency scope

The test project uses xUnit v3 4.0.2 with its MTP integration disabled, the VSTest adapter 4.0.1, and Microsoft.NET.Test.Sdk 18.11.0. These packages are referenced only by `tests/FirebirdStudio.Tests`; they are not referenced by Core, Application, Infrastructure, or Desktop and therefore do not become application runtime dependencies. The VSTest test SDK brings the test host and code-coverage tooling transitively. The runner adapter's net8.0 asset has no further package dependencies. No application native runtime assets were added by these test references. The package dependency edges and versions above were checked against the NuGet package metadata and pinned centrally. A resolved `project.assets.json`/lock-file audit, including inspection of any platform-specific test-tool assets actually selected for the target RID, still requires `dotnet restore` on a machine with the .NET SDK.
