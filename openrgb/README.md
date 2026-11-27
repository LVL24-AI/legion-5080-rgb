# OpenRGB support for the Lenovo Legion RTX 5080

This adds a new GPU controller to [OpenRGB](https://gitlab.com/CalcProgrammer1/OpenRGB) for the light chip on Lenovo Legion GeForce RTX 5080 cards (PCI `10DE:2C02`, subsystem `17AA:C770`).

Tested on 27 Nov 2026 against OpenRGB master `664118d1` on Windows 11, with the card in an ASUS PRIME B850-PLUS WIFI PC. Everything below worked on the real card:

- Static color on the light strip, brightness 10–100 %
- Blinking (3 speeds), Spiral Rainbow (3 speeds, brightness)
- Off
- Logo LED on/off. This LED is white only.

The effects run on the card's own chip. OpenRGB only sends the setting.

> This code was written with an AI assistant. OpenRGB's CONTRIBUTING.md does not accept AI-generated merge requests, so don't submit it upstream as it is.

## How it talks to the card

The chip sits at I2C address `0x49` (8-bit `0x92`) on the GPU's I2C port 1, which is the bus OpenRGB already opens through NvAPI ("Nvidia NvAPI I2C on GPU 0"). No change to OpenRGB's NVIDIA bus code is needed.

Every update is a series of one-byte register writes, the same ones Lenovo's lighting service sends, each followed by a 10 ms pause:

| Register | Meaning |
|---|---|
| `0x30` = `B0`, `0x31` = `B1`, `0x32` = `B2` | write handshake: begin, end, apply |
| `0x40` = `01` | written after every `B0` |
| `0x15` / `0x14` | mode: 0 off, 1 static, 3 blinking, 4 rainbow (`0x14` commits it) |
| `0x17` | brightness in % (rainbow uses about 1.44× the value) |
| `0x18`–`0x1A` | red, green, blue |
| `0x16`, `0x1B`–`0x1D` | unknown, Lenovo writes `00`, `00`, `C8`, `FF` |
| `0x20`–`0x23` | effect timing in ms, two big-endian 16-bit values |
| `0x50` | logo LED 0/1, sent four times |

The chip has no ID register, so detection matches on the PCI subsystem ID and checks that address `0x49` answers a read.

## Files

- `Controllers/LenovoLegionGPUController/`: the controller. Copy it into OpenRGB's `Controllers/` folder. OpenRGB.pro picks it up automatically.
- `pci_ids.patch`: adds `LENOVO_SUB_VEN` (`0x17AA`) and `LENOVO_LEGION_RTX5080_SUB_DEV` (`0xC770`) to `pci_ids/pci_ids.h`.
- `tools/build-openrgb.bat`: the build script used here (Qt 6.8.3 msvc2022_64, jom, VS 2022 Build Tools; paths are for this PC).
- `tools/Run-OpenRGB-Lenovo.bat`: starts the build with its own settings folder.
- `tools/I2CReadProbe.cs`: read-only check that the chip answers on OpenRGB's NvAPI bus settings.

## Build

```bat
git clone https://gitlab.com/CalcProgrammer1/OpenRGB.git
xcopy /E /I Controllers\LenovoLegionGPUController OpenRGB\Controllers\LenovoLegionGPUController
cd OpenRGB
git apply ..\pci_ids.patch
```

Then build as usual (see OpenRGB's `Documentation/Compiling.md`) or with `tools/build-openrgb.bat`.

Don't run LGPU or Legion RGB at the same time. They would overwrite what OpenRGB sets.
