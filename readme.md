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

