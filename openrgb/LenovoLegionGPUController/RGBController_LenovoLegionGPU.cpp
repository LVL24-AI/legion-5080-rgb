/*---------------------------------------------------------*\
| RGBController_LenovoLegionGPU.cpp                         |
|                                                           |
|   RGBController for Lenovo Legion GeForce RTX graphics    |
|   cards (Legion tower desktops)                           |
|                                                           |
|   27 Nov 2026                                             |
|                                                           |
|   This file is part of the OpenRGB project                |
|   SPDX-License-Identifier: GPL-2.0-or-later               |
\*---------------------------------------------------------*/

#include "RGBController_LenovoLegionGPU.h"

/**------------------------------------------------------------------*\
    @name Lenovo Legion GPU
    @category GPU
    @type I2C
    @save :x:
    @direct :x:
    @effects :white_check_mark:
    @detectors DetectLenovoLegionGPUControllers
    @comment
        The light chip of Lenovo Legion RTX cards sits at I2C address
        0x49 on the GPU's I2C bus. It has a light strip (any color,
        static, blinking or rainbow, brightness 10-100%) and a logo
        LED that can only be switched on or off. Set the Logo LED to
        any color to switch it on and to black to switch it off.
\*-------------------------------------------------------------------*/

RGBController_LenovoLegionGPU::RGBController_LenovoLegionGPU(LenovoLegionGPUController* controller_ptr)
{
    controller              = controller_ptr;

    name                    = controller->GetName();
    vendor                  = "Lenovo";
    description             = "Lenovo Legion GPU";
    location                = controller->GetDeviceLocation();
    type                    = DEVICE_TYPE_GPU;

    mode Static;
    Static.name             = "Static";
    Static.value            = LENOVO_LEGION_GPU_MODE_STATIC;
    Static.flags            = MODE_FLAG_HAS_PER_LED_COLOR | MODE_FLAG_HAS_BRIGHTNESS;
    Static.color_mode       = MODE_COLORS_PER_LED;
    Static.brightness_min   = LENOVO_LEGION_GPU_BRIGHTNESS_MIN;
    Static.brightness_max   = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    Static.brightness       = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    modes.push_back(Static);

    mode Blinking;
    Blinking.name           = "Blinking";
    Blinking.value          = LENOVO_LEGION_GPU_MODE_BLINKING;
    Blinking.flags          = MODE_FLAG_HAS_PER_LED_COLOR | MODE_FLAG_HAS_BRIGHTNESS | MODE_FLAG_HAS_SPEED;
    Blinking.color_mode     = MODE_COLORS_PER_LED;
    Blinking.brightness_min = LENOVO_LEGION_GPU_BRIGHTNESS_MIN;
    Blinking.brightness_max = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    Blinking.brightness     = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    Blinking.speed_min      = LENOVO_LEGION_GPU_SPEED_SLOW;
    Blinking.speed_max      = LENOVO_LEGION_GPU_SPEED_FAST;
    Blinking.speed          = LENOVO_LEGION_GPU_SPEED_NORMAL;
    modes.push_back(Blinking);

    mode Rainbow;
    Rainbow.name            = "Spiral Rainbow";
    Rainbow.value           = LENOVO_LEGION_GPU_MODE_RAINBOW;
    Rainbow.flags           = MODE_FLAG_HAS_BRIGHTNESS | MODE_FLAG_HAS_SPEED;
    Rainbow.color_mode      = MODE_COLORS_NONE;
    Rainbow.brightness_min  = LENOVO_LEGION_GPU_BRIGHTNESS_MIN;
    Rainbow.brightness_max  = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    Rainbow.brightness      = LENOVO_LEGION_GPU_BRIGHTNESS_MAX;
    Rainbow.speed_min       = LENOVO_LEGION_GPU_SPEED_SLOW;
    Rainbow.speed_max       = LENOVO_LEGION_GPU_SPEED_FAST;
    Rainbow.speed           = LENOVO_LEGION_GPU_SPEED_NORMAL;
    modes.push_back(Rainbow);

    mode Off;
    Off.name                = "Off";
    Off.value               = LENOVO_LEGION_GPU_MODE_OFF;
    Off.flags               = 0;
    Off.color_mode          = MODE_COLORS_NONE;
    modes.push_back(Off);

    SetupZones();

    /*-----------------------------------------------------*\
    | Default colors: Legion cyan strip, logo LED off       |
    \*-----------------------------------------------------*/
    colors[0]               = ToRGBColor(0, 211, 254);
    colors[1]               = ToRGBColor(0, 0, 0);
}

RGBController_LenovoLegionGPU::~RGBController_LenovoLegionGPU()
{
    Shutdown();

    delete controller;
}

void RGBController_LenovoLegionGPU::SetupZones()
{
    /*-----------------------------------------------------*\
    | Zone 0: the light strip, zone 1: the logo LED         |
    \*-----------------------------------------------------*/
    zone strip_zone;
    strip_zone.name         = "Light Strip";
    strip_zone.type         = ZONE_TYPE_SINGLE;
    strip_zone.leds_min     = 1;
    strip_zone.leds_max     = 1;
    strip_zone.leds_count   = 1;
    zones.push_back(strip_zone);

    led strip_led;
    strip_led.name          = "Light Strip";
    leds.push_back(strip_led);

    zone logo_zone;
    logo_zone.name          = "Logo";
    logo_zone.type          = ZONE_TYPE_SINGLE;
    logo_zone.leds_min      = 1;
    logo_zone.leds_max      = 1;
    logo_zone.leds_count    = 1;
    zones.push_back(logo_zone);

    led logo_led;
    logo_led.name           = "Logo LED";
    leds.push_back(logo_led);

    SetupColors();
}

void RGBController_LenovoLegionGPU::DeviceUpdateLEDs()
{
    DeviceUpdateMode();
}

void RGBController_LenovoLegionGPU::DeviceUpdateZoneLEDs(int /*zone*/)
{
    DeviceUpdateMode();
}

void RGBController_LenovoLegionGPU::DeviceUpdateSingleLED(int /*led*/)
{
    DeviceUpdateMode();
}

void RGBController_LenovoLegionGPU::DeviceUpdateMode()
{
    u8          mode_value  = (u8)modes[active_mode].value;
    RGBColor    strip_color = colors[0];
    bool        logo_on     = (mode_value != LENOVO_LEGION_GPU_MODE_OFF) && (colors[1] != 0);

    controller->SetMode(mode_value,
                        (u8)RGBGetRValue(strip_color),
                        (u8)RGBGetGValue(strip_color),
                        (u8)RGBGetBValue(strip_color),
                        modes[active_mode].brightness,
                        modes[active_mode].speed);

    controller->SetLogo(logo_on);
}
