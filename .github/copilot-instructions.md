## Testing

- Ask the user for explicit approval before running full app tests.

## Build and test architecture

- Follow `AGENTS.md`: use the native OS architecture for local builds, runs, and tests, not the architecture of an emulated terminal. This PC is ARM64; use `--arch arm64`, or `-p:Platform=ARM64 -r win-arm64` for direct .NET builds. Use x64 only on an x64 host or when explicitly cross-targeting.
