## Testing

- Ask the user for explicit approval before running full app tests.

## Build and test architecture

- Follow `AGENTS.md`: use the native OS architecture for local builds, runs, and tests, not the architecture of an emulated terminal. This PC is ARM64; use `--arch arm64`, or `-p:Platform=ARM64 -r win-arm64` for direct .NET builds. Use x64 only on an x64 host or when explicitly cross-targeting.

## Tray app conventions

miniLook is an older, Store-associated WinUI 3 tray app. It predates the `winui-tray-app` scaffold and retains Hosting/DI, a hidden MainWindow, a WinUIEx-hosted Flyout, and MSAL/WAM authentication. Load that skill before changing lifetime, the flyout, the tray icon, polling, settings, or secrets.

- Launch on this ARM64 PC with `winapp run .\miniLook\miniLook.csproj -c Release --arch arm64 -p AppxBundle=Never -p UapAppxPackageBuildMode=SideloadOnly --detach`; use the matching native architecture on other hosts, and do not register it by hand. The overrides avoid Store-upload symbols tooling during local development. `--clean` deletes existing app data, so use it only when that reset is intended.
- `miniLook\App.xaml.cs` owns the tray icon and single-instance handling. The target convention is explicit dispatcher shutdown with Exit as the only shutdown path; the current hidden MainWindow lifetime is a known deviation.
- The target flyout lifecycle is hide-on-dismiss, then close after one minute hidden and release idle memory. Anything started or subscribed while visible must be stopped or unsubscribed on hide/close. The current Flyout discards its content instead and retains app-lifetime services.
- Network work should run only while visible through a foreground poller, with one request at a time, cancellation, backoff, and connectivity recovery. Background work is allowed only for tray-displayed state with an opt-in and documented reason. Current 10-second background mail/calendar sync is a known deviation.
- Tray icons and shell-style flyout materials should follow the taskbar theme independently of the app theme. The current icons do not have light/dark variants.
- Public-client registration values go in gitignored `miniLook\OAuth.resw`, copied from `OAuth.resw.example`. Preserve MSAL/WAM and its protected `MsalCacheHelper` token cache; never put user tokens or confidential secrets in LocalSettings, logs, or git.
- Keep pure decision logic in WinRT-free classes or `miniLook.Core` so plain .NET tests can cover it. The existing Core project targets `net9.0`; there is no test project yet.
- Update README from actual behavior: features, screenshots, installation, setup, privacy/footprint, build commands, and actual licensing. Do not claim scaffold behavior or an MIT license that the repository does not currently have.
- Release by bumping `miniLook\Package.appxmanifest`'s version, committing and pushing, running `build-msix-for-gh.ps1`, publishing its draft without changing the tag, and running `build-msix-for-gh.ps1 -VerifyPublished`. Check unsigned packages with `-SkipRelease -NoSign -OutputRoot <scratchpad>`.
- The release script is copied byte-for-byte from the `winui-tray-app` skill. Fix shared bugs there and recopy, never independently here. It temporarily swaps the csproj's GitHub identity and restores the Store manifest. Preserve shipped identities and keep the committed Store manifest free of `uap13:AutoUpdate`.
