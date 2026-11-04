# Agent instructions

## Build and test architecture

- Use the native OS architecture for local builds, runs, and tests unless cross-targeting is explicitly requested. Detect it with `[System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture`; do not infer it from an emulated terminal or default to x64.
- This development PC is ARM64: use `--arch arm64` with `winapp run`. For direct .NET builds, use the matching `-p:Platform=ARM64 -r win-arm64`. On x64 machines, use the corresponding x64 targets.

## Testing

- Ask the user for explicit approval before running full app tests.

## Repository conventions

- Follow the tray app conventions and launch requirements in [CLAUDE.md](CLAUDE.md).
