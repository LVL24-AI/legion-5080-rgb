/*---------------------------------------------------------*\
| LenovoLegionGPUControllerDetect.cpp                       |
|                                                           |
|   Detector for Lenovo Legion GeForce RTX graphics cards   |
|                                                           |
|   27 Nov 2026                                             |
|                                                           |
|   This file is part of the OpenRGB project                |
|   SPDX-License-Identifier: GPL-2.0-or-later               |
\*---------------------------------------------------------*/

#include "DetectionManager.h"
#include "i2c_smbus.h"
#include "LogManager.h"
#include "LenovoLegionGPUController.h"
#include "pci_ids.h"
#include "RGBController_LenovoLegionGPU.h"

DetectedControllers DetectLenovoLegionGPUControllers(i2c_smbus_interface* bus, u8 i2c_addr, const std::string& name)
{
    DetectedControllers detected_controllers;

    /*-----------------------------------------------------*\
    | The chip has no ID register, so check that it answers |
    | a register read at its address on this bus            |
    \*-----------------------------------------------------*/
    if(bus->i2c_smbus_read_byte_data(i2c_addr, LENOVO_LEGION_GPU_REG_MODE) >= 0)
    {
        LenovoLegionGPUController*     controller     = new LenovoLegionGPUController(bus, i2c_addr, name);
        RGBController_LenovoLegionGPU* rgb_controller = new RGBController_LenovoLegionGPU(controller);

        detected_controllers.push_back(rgb_controller);
    }
    else
    {
        LOG_DEBUG("[%s] No answer at address 0x%02X on %s", name.c_str(), i2c_addr, bus->info.device_name);
    }

    return(detected_controllers);
}

REGISTER_I2C_PCI_DETECTOR("Lenovo Legion GeForce RTX 5080", DetectLenovoLegionGPUControllers, NVIDIA_VEN, NVIDIA_RTX5080_DEV, LENOVO_SUB_VEN, LENOVO_LEGION_RTX5080_SUB_DEV, 0x49);
