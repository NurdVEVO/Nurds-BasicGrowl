# Gen-Ai content warning.
## The mod herein uses code that is entirely generative ai.

# Nurd's Basic Growl

IR seeker growl audio mod for Nuclear Option.

## Features
- Embedded audio that plays from standard configs.
- Custom audio compatibility (needs to be redone to be more user friendly)
- Configurable missile profiles for seeker tone behaviour. (needs to be redone to be more user friendly)
- Automatic IR missle compatibility, adding profiles for new weapons detected from weapon mods.

## Known issues

- Upon death, seeker can keep playing. (very annoying)
- The parameters in the config files are confusing for some users.

# Install

- Go to t
- Delete or overwrite the older install if you want in folder `Nuclear Option\BepInEx\plugins\com.basicgrowl` or `Nuclear Option\BepInEx\plugins\Nurds_Basic_growl` depending on how the name looks.
- Download from the 
alternatively
- Download from NOMM.

The `AAM1.txt`, `AAM3.txt`, and `SAM_IR1.txt` files next to the DLL are editable profiles. Default seeker sounds are compressed and embedded in the plugin. Compatible `.wav` files placed in `SeekerNoises` override embedded sounds with matching filenames/configured names in the respective (dot)txt file.

After the game's weapon registry has loaded, the mod scans installed weapons for IR seekers without profiles. It asks before creating any additional profile files. The scan can also be run manually with **Scan for IR Missiles** in BepInEx Configuration Manager.

## License

The original source code, text profile files, and documentation are released
under the MIT License. See `LICENSE` for details.

Audio assets, game assets, and third-party software are not covered by that
license grant. See `THIRD_PARTY_NOTICES.md`.
