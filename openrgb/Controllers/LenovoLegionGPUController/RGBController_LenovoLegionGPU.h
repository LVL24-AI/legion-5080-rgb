/*---------------------------------------------------------*\
| RGBController_LenovoLegionGPU.h                           |
|                                                           |
|   RGBController for Lenovo Legion GeForce RTX graphics    |
|   cards (Legion tower desktops)                           |
|                                                           |
|   27 Nov 2026                                             |
|                                                           |
|   This file is part of the OpenRGB project                |
|   SPDX-License-Identifier: GPL-2.0-or-later               |
\*---------------------------------------------------------*/

#pragma once

#include "RGBController.h"
#include "LenovoLegionGPUController.h"

class RGBController_LenovoLegionGPU : public RGBController
{
public:
    RGBController_LenovoLegionGPU(LenovoLegionGPUController* controller_ptr);
    ~RGBController_LenovoLegionGPU();

    void SetupZones();

    void DeviceUpdateLEDs();
    void DeviceUpdateZoneLEDs(int zone);
    void DeviceUpdateSingleLED(int led);

    void DeviceUpdateMode();

private:
    LenovoLegionGPUController* controller;
};
