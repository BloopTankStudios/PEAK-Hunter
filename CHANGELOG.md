# Changelog

All notable changes to this project will be documented in this file.

## Known Bugs

- Not Fully Tested in Multiplayer

## [1.1.0] - 2025-12-22

### Added

- Hunters can now spectate again when knocked out
- Made attack distance cubic instead of linear
- Made hunters have half the attack knockback compared to climbers
- Hunters dying into Zombies now reset the curse value
- Hunter has default 0.25 fall damage now
- Blowgun has 1 minute cooldown in lobby
- Blowgun causes drowsiness in lobby
- Removed nametags for opposite teams (or in zombie mode)
- Hunters share the set extra stamina in zombie mode
- New config value to set amount of curse upon climber respawn (set as triple)
- New config option to color teams: climbers v. hunters
- Removed fog/lava for Hunters so you can now play ascents properly
- Hunters/Climbers can no longer reach into the other team's backpack

### Fixed

- Blowgun Cooldown will now be visible again
- Fixed hunters sometimes spawning with curse
- Fixed climbers still initially spectating a hunter
- Fixed initial spawn timing to finally now be based on slowest networked player
- Fixed hunter being able to start campfire with everyone there
- Updated list of afflictions to new Updates (now will dynamically update)

## [1.0.1] - 2025-12-07

### Fixed

- The asset folder was not included in mod release, whoops!

## [1.0.0] - 2025-12-02

Changes since first Hunter video has released

### Added

- Death Timer no longer ticks down for Hunters that are passed out, you can still hold E
- Climbers can no longer switch to spectate a Hunter
- Hunters can now always attack, but will still be knocked out if they have no stamina left
- Hunters no longer lose items upon passing out/teleporting, but they do still lose items on death
- Added a "1 Hunter" profile for HunterTab values

### Fixed

- Mod fixed to work with Roots Update!
- Fixed compatibility issue with "PEAK Unlimited" Mod
- Fixed syncing of Hunter/Climber roles
- Fixed syncing of HunterTab values
- Scout Effigies should no longer acknowledge Hunters
- Hunters will no longer spawn inside the beach

### Changed

- Base Config Values Modified
  - AddedCooldownPerSection: 10s -> 3s
  - BlowgunCooldown: 2m -> 7m
  - AttackKnockback: 1 -> 2
- Improved HunterTab Menu
- ExtraStamina now regens similar to regular stamina regen
