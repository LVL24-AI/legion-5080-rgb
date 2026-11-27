/*---------------------------------------------------------*\
| LenovoLegionGPUController.h                               |
|                                                           |
|   Driver for the light chip on Lenovo Legion GeForce      |
|   RTX graphics cards (Legion tower desktops)              |
|                                                           |
|   27 Nov 2026                                             |
|                                                           |
|   This file is part of the OpenRGB project                |
|   SPDX-License-Identifier: GPL-2.0-or-later               |
\*---------------------------------------------------------*/

#pragma once

#include <string>
#include "i2c_smbus.h"

/*---------------------------------------------------------*\
| Register map, taken from what Lenovo's lighting service   |
| writes to the chip. Every write is a one byte register    |
| write, wrapped in the BEGIN/END/APPLY handshake.          |
\*---------------------------------------------------------*/
enum
{
    LENOVO_LEGION_GPU_REG_COMMIT_MODE   = 0x14,     /* Mode, written again after the handshake  */
    LENOVO_LEGION_GPU_REG_MODE          = 0x15,     /* Mode                                     */
    LENOVO_LEGION_GPU_REG_16            = 0x16,     /* Unknown, Lenovo writes 0x00              */
    LENOVO_LEGION_GPU_REG_BRIGHTNESS    = 0x17,     /* Brightness in percent                    */
    LENOVO_LEGION_GPU_REG_RED           = 0x18,     /* Red                                      */
    LENOVO_LEGION_GPU_REG_GREEN         = 0x19,     /* Green                                    */
    LENOVO_LEGION_GPU_REG_BLUE          = 0x1A,     /* Blue                                     */
    LENOVO_LEGION_GPU_REG_1B            = 0x1B,     /* Unknown, Lenovo writes 0x00              */
    LENOVO_LEGION_GPU_REG_1C            = 0x1C,     /* Unknown, Lenovo writes 0xC8              */
    LENOVO_LEGION_GPU_REG_1D            = 0x1D,     /* Unknown, Lenovo writes 0xFF              */
    LENOVO_LEGION_GPU_REG_TIME_1_HI     = 0x20,     /* Effect time 1 in ms, high byte           */
    LENOVO_LEGION_GPU_REG_TIME_1_LO     = 0x21,     /* Effect time 1 in ms, low byte            */
    LENOVO_LEGION_GPU_REG_TIME_2_HI     = 0x22,     /* Effect time 2 in ms, high byte           */
    LENOVO_LEGION_GPU_REG_TIME_2_LO     = 0x23,     /* Effect time 2 in ms, low byte            */
    LENOVO_LEGION_GPU_REG_BEGIN         = 0x30,     /* Handshake: start of a write              */
    LENOVO_LEGION_GPU_REG_END           = 0x31,     /* Handshake: end of a write                */
    LENOVO_LEGION_GPU_REG_APPLY         = 0x32,     /* Handshake: apply                         */
    LENOVO_LEGION_GPU_REG_40            = 0x40,     /* Unknown, Lenovo writes 0x01              */
    LENOVO_LEGION_GPU_REG_LOGO          = 0x50,     /* Logo LED, 0 off, 1 on                    */
};

enum
{
    LENOVO_LEGION_GPU_HANDSHAKE_BEGIN   = 0xB0,
    LENOVO_LEGION_GPU_HANDSHAKE_END     = 0xB1,
    LENOVO_LEGION_GPU_HANDSHAKE_APPLY   = 0xB2,
};

enum
{
    LENOVO_LEGION_GPU_MODE_OFF          = 0x00,
    LENOVO_LEGION_GPU_MODE_STATIC       = 0x01,
    LENOVO_LEGION_GPU_MODE_BLINKING     = 0x03,
    LENOVO_LEGION_GPU_MODE_RAINBOW      = 0x04,
};

enum
{
    LENOVO_LEGION_GPU_SPEED_SLOW        = 0x01,
    LENOVO_LEGION_GPU_SPEED_NORMAL      = 0x02,
    LENOVO_LEGION_GPU_SPEED_FAST        = 0x03,
};

enum
{
    LENOVO_LEGION_GPU_BRIGHTNESS_MIN    = 10,
    LENOVO_LEGION_GPU_BRIGHTNESS_MAX    = 100,
};

class LenovoLegionGPUController
{
public:
    LenovoLegionGPUController(i2c_smbus_interface* bus, u8 dev, std::string dev_name);
    ~LenovoLegionGPUController();

    std::string             GetDeviceLocation();
    std::string             GetName();

    bool                    SetMode(u8 mode, u8 red, u8 green, u8 blue, unsigned int brightness, unsigned int speed);
    bool                    SetLogo(bool on);

private:
    i2c_smbus_interface*    bus;
    u8                      dev;
    std::string             name;

    bool                    WriteRegister(u8 reg, u8 value);
};
