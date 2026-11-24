# Contributing

Open an issue for substantive changes and describe the observed behavior, Windows build, device model, and connection type. For hardware fixes, state whether you used physical hardware or sample frames.

- Use Windows x64 and PowerShell. Run `./Build.ps1 -Test` and `./Build.ps1` before submitting.
- Run `./tests/verify-ui.ps1` in an interactive Windows session with existing app instances closed. The script launches and stops only the process it starts. Hardware-specific checks require the relevant hardware.
- Keep protocol, storage, and UI changes scoped to the issue. Update both READMEs when behavior visible to users changes.
- Remove device addresses, serial numbers, local paths, personal identifiers, and credentials from logs and screenshots.

Versions follow [Semantic Versioning](https://semver.org/). Use patch versions for compatible fixes, minor versions for compatible features, and major versions for breaking changes to documented behavior. Set `VERSION`, update `CHANGELOG.md`, and place an annotated matching `vX.Y.Z` tag on the reviewed commit. GitHub Actions builds a draft release for the tag. The maintainer reviews the artifacts and publishes the draft explicitly.
