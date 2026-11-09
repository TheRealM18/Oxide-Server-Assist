# Release inputs

This folder contains the prebuilt binaries that the GitHub release workflow packages. The workflow does **not** compile the client mods or programs here.

Upload or replace the files in these folders before running the manual release workflow:

```text
release-inputs/
├── mods/
│   ├── ServerAssistMod/       ServerAssistMod.dll, ModInfo.xml, Config, docs
│   └── QLNPackBootstrap/      QLNPackBootstrap.dll, QLNPackUpdater.exe, config files
└── programs/
    ├── QLNPackServer/         QLNPackServer.exe, QLNPackServer.json
    ├── TwitchRelay/           one folder per runtime, e.g. win-x64/ and linux-x64/
    └── QLNPackUpdater/        optional standalone updater files
```

The release script reads the version from each plugin source file or `ModInfo.xml`. Programs use the version supplied to the manual workflow unless a `version.txt` file is placed in the program folder.

The generated ZIPs are written to `release-out/`, which is ignored by Git.
