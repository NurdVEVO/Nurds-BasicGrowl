# Nurds Basic Growl

IR seeker growl audio mod for Nuclear Option.

## Install

1. Delete any old install folder for this mod from `Nuclear Option\BepInEx\plugins`.
2. Extract `com.basicGrowl-1.0.4.zip` directly into `Nuclear Option\BepInEx\plugins`.
3. Check that this file exists:

   `Nuclear Option\BepInEx\plugins\Nurds_Basic_growl\com.basicGrowl.dll`

Do not extract the zip inside an existing mod folder.

Older builds may have used one of these folder names: `BasicGrowl`, `Basic Growl`, `basicGrowl`, `com.basicGrowl`, `SoundOfTheGrowlers`, or `SoundsOfTheGrowlers`. Delete those old folders before installing this release.

The `AAM1.txt`, `AAM3.txt`, and `SAM_IR1.txt` files next to the DLL are editable profiles. Default seeker sounds are compressed and embedded in the plugin. Compatible `.wav` files placed in `SeekerNoises` override embedded sounds with matching filenames.

After the game's weapon registry has loaded, the mod scans installed weapons for IR seekers without profiles. It asks before creating any additional profile files. The scan can also be run manually with **Scan for IR Missiles** in BepInEx Configuration Manager.

## Build from source

Set the `NUCLEAR_OPTION_ROOT` environment variable to the Nuclear Option installation directory, then run:

```text
dotnet build com.basicGrowl.csproj -c Release
```

## License

The original source code, text profile files, and documentation are released
under the MIT License. See `LICENSE` for details.

Audio assets, game assets, and third-party software are not covered by that
license grant. See `THIRD_PARTY_NOTICES.md`.
