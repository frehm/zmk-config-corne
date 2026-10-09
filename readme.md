# Update firmware

* Change config or keymap, commit, then push to github
* This will trigger a build action that produces to firmware files
* Download new firmware from [GitHub Actions](https://github.com/frehm/zmk-config-corne/actions)
* Connect left half of keyboard using USB
* Double tap reset button on keyboard
* The controller will appear as a new USB storage device
* Copy the correct UF2 file onto the root of the device
* Once the flash is complete the controller will reboot
* Repeat for the other side

The halves should reconnect automatically. For additional info see [docs](https://zmk.dev/docs/user-setup#installing-the-firmware).

## Local builds with Docker

Start Docker Desktop with Linux containers. Run these commands in PowerShell
from the repository root. Authenticate to Docker Hub before downloading the
base image if you encounter its anonymous pull limit:

```powershell
docker login
docker build -t corne-builder .
New-Item -ItemType Directory -Force firmware | Out-Null
docker run --rm --mount "type=bind,source=$($PWD.Path),target=/config,readonly" --mount "type=bind,source=$($PWD.Path)/firmware,target=/out" corne-builder
```

The image caches ZMK and its dependencies using `config/west.yml` (currently
ZMK v0.3). The container reads your current configuration from the mounted
repository, so keymap edits do not require rebuilding the image. Rebuild the
image after changing `config/west.yml`, the Dockerfile, or the build script.

By default, this builds `corne_left`. Append `corne_right`, `settings_reset`,
multiple shield names, or `all` after `corne-builder` to select other targets.
Append `--dry-run` to inspect the commands without compiling.

The script reads `build.yaml`, including each target's `cmake-args`, so the
left-half P1.04 repair flag is included automatically. The right half retains
its normal mapping. It uses pristine builds to avoid stale configuration.

Firmware is exported to `firmware/<shield>-<board>-zmk.uf2`, alongside the
generated `.dts` hardware definition. For the repaired left half, check that
the first `row-gpios` entry under `kscan0` uses Pro Micro pin 8 (P1.04), not pin
4 (P0.22), before flashing. Copy the left UF2 to its `NICENANO` bootloader drive;
no settings reset or right-half reflash is needed for this repair.

