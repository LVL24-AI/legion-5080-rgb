/*---------------------------------------------------------*\
| LenovoLegionGPUController.cpp                             |
|                                                           |
|   Driver for the light chip on Lenovo Legion GeForce      |
|   RTX graphics cards (Legion tower desktops)              |
|                                                           |
|   27 Nov 2026                                             |
|                                                           |
|   This file is part of the OpenRGB project                |
|   SPDX-License-Identifier: GPL-2.0-or-later               |
\*---------------------------------------------------------*/

#include <chrono>
#include <cmath>
#include <thread>
#include "LenovoLegionGPUController.h"

LenovoLegionGPUController::LenovoLegionGPUController(i2c_smbus_interface* bus, u8 dev, std::string dev_name)
{
    this->bus   = bus;
    this->dev   = dev;
    this->name  = dev_name;
}

LenovoLegionGPUController::~LenovoLegionGPUController()
{
}

std::string LenovoLegionGPUController::GetDeviceLocation()
{
    std::string return_string(bus->info.device_name);
    char addr[5];
    snprintf(addr, 5, "0x%02X", dev);
    return_string.append(", address ");
    return_string.append(addr);
    return("I2C: " + return_string);
}

std::string LenovoLegionGPUController::GetName()
{
    return(name);
}

bool LenovoLegionGPUController::WriteRegister(u8 reg, u8 value)
{
    bool ok = (bus->i2c_smbus_write_byte_data(dev, reg, value) >= 0);

    /*-----------------------------------------------------*\
    | Lenovo's service and the tested tool both give the    |
    | chip a short pause after every register write         |
    \*-----------------------------------------------------*/
    std::this_thread::sleep_for(std::chrono::milliseconds(10));

    return(ok);
}

bool LenovoLegionGPUController::SetMode(u8 mode, u8 red, u8 green, u8 blue, unsigned int brightness, unsigned int speed)
{
    /*-----------------------------------------------------*\
    | Brightness is a percentage. In rainbow mode Lenovo    |
    | writes a value about 1.44 times higher (0x48 at 50%)  |
    \*-----------------------------------------------------*/
    if(brightness < LENOVO_LEGION_GPU_BRIGHTNESS_MIN)
    {
        brightness = LENOVO_LEGION_GPU_BRIGHTNESS_MIN;
    }

    if(brightness > LENOVO_LEGION_GPU_BRIGHTNESS_MAX)
    {
        brightness = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    }

    unsigned int brightness_reg = brightness;

    if(mode == LENOVO_LEGION_GPU_MODE_RAINBOW)
    {
        brightness_reg = (unsigned int)std::lround(brightness * 1.44);

        if(brightness_reg > 100)
        {
            brightness_reg = 100;
        }
    }

    /*-----------------------------------------------------*\
    | Effect timing in milliseconds. Lenovo uses 2000/2000  |
    | for blinking and 200 for rainbow (normal speed)       |
    \*-----------------------------------------------------*/
    unsigned int time_1 = 0;
    unsigned int time_2 = 0;

    if(mode == LENOVO_LEGION_GPU_MODE_BLINKING)
    {
        switch(speed)
        {
            case LENOVO_LEGION_GPU_SPEED_SLOW:  time_1 = 3000; break;
            case LENOVO_LEGION_GPU_SPEED_FAST:  time_1 = 1000; break;
            default:                            time_1 = 2000; break;
        }

        time_2 = time_1;
    }
    else if(mode == LENOVO_LEGION_GPU_MODE_RAINBOW)
    {
        switch(speed)
        {
            case LENOVO_LEGION_GPU_SPEED_SLOW:  time_1 = 300;  break;
            case LENOVO_LEGION_GPU_SPEED_FAST:  time_1 = 100;  break;
            default:                            time_1 = 200;  break;
        }
    }

    /*-----------------------------------------------------*\
    | Effect parameters                                     |
    \*-----------------------------------------------------*/
    bool ok = true;

    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_BEGIN,       LENOVO_LEGION_GPU_HANDSHAKE_BEGIN);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_40,          0x01);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_MODE,        mode);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_16,          0x00);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_BRIGHTNESS,  (u8)brightness_reg);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_RED,         red);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_GREEN,       green);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_BLUE,        blue);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_1B,          0x00);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_1C,          0xC8);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_1D,          0xFF);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_TIME_1_HI,   (u8)(time_1 >> 8));
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_TIME_1_LO,   (u8)(time_1 & 0xFF));
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_TIME_2_HI,   (u8)(time_2 >> 8));
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_TIME_2_LO,   (u8)(time_2 & 0xFF));
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_END,         LENOVO_LEGION_GPU_HANDSHAKE_END);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_APPLY,       LENOVO_LEGION_GPU_HANDSHAKE_APPLY);

    /*-----------------------------------------------------*\
    | Commit the mode                                       |
    \*-----------------------------------------------------*/
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_BEGIN,       LENOVO_LEGION_GPU_HANDSHAKE_BEGIN);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_40,          0x01);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_COMMIT_MODE, mode);
    ok &= WriteRegister(LENOVO_LEGION_GPU_REG_END,         LENOVO_LEGION_GPU_HANDSHAKE_END);

    return(ok);
}

bool LenovoLegionGPUController::SetLogo(bool on)
{
    /*-----------------------------------------------------*\
    | Lenovo's service sends the logo LED state four times  |
    \*-----------------------------------------------------*/
    bool ok = true;

    for(int i = 0; i < 4; i++)
    {
        ok &= WriteRegister(LENOVO_LEGION_GPU_REG_BEGIN,   LENOVO_LEGION_GPU_HANDSHAKE_BEGIN);
        ok &= WriteRegister(LENOVO_LEGION_GPU_REG_LOGO,    on ? 0x01 : 0x00);
        ok &= WriteRegister(LENOVO_LEGION_GPU_REG_END,     LENOVO_LEGION_GPU_HANDSHAKE_END);
        ok &= WriteRegister(LENOVO_LEGION_GPU_REG_APPLY,   LENOVO_LEGION_GPU_HANDSHAKE_APPLY);
    }

    return(ok);
}
