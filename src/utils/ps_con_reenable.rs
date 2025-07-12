use std::{thread, time::Duration};
use windows::Win32::Devices::DeviceAndDriverInstallation::{
    SetupDiGetClassDevsW, SetupDiEnumDeviceInterfaces, SetupDiGetDeviceInterfaceDetailW,
    SetupDiSetClassInstallParamsW, SetupDiCallClassInstaller, SetupDiDestroyDeviceInfoList,
    DIGCF_PRESENT, DIGCF_DEVICEINTERFACE, DIF_PROPERTYCHANGE,
    SP_DEVICE_INTERFACE_DATA, SP_DEVICE_INTERFACE_DETAIL_DATA_W,
    SP_CLASSINSTALL_HEADER, SP_PROPCHANGE_PARAMS, SP_DEVINFO_DATA,
    DICS_FLAG_GLOBAL, DICS_DISABLE, DICS_ENABLE,
};
use windows::Win32::Devices::HumanInterfaceDevice::GUID_DEVINTERFACE_HID;

/// 对单个设备节点执行 Disable → Enable
fn property_change(
    dev_info_set: windows::Win32::Devices::DeviceAndDriverInstallation::HDEVINFO,
    devinfo: SP_DEVINFO_DATA,  // 改为按值传入，方便线程移动
) {
    for &state in &[DICS_DISABLE, DICS_ENABLE] {
        let mut params = SP_PROPCHANGE_PARAMS::default();
        params.ClassInstallHeader.cbSize =
            std::mem::size_of::<SP_CLASSINSTALL_HEADER>() as u32;
        params.ClassInstallHeader.InstallFunction = DIF_PROPERTYCHANGE;
        params.StateChange = state;
        params.Scope = DICS_FLAG_GLOBAL;
        params.HwProfile = 0;

        unsafe {
            SetupDiSetClassInstallParamsW(
                dev_info_set,
                Some(&devinfo),
                Some(&params.ClassInstallHeader),
                std::mem::size_of::<SP_PROPCHANGE_PARAMS>() as u32,
            );
            SetupDiCallClassInstaller(
                DIF_PROPERTYCHANGE,
                dev_info_set,
                Some(&devinfo),
            );
        }

        thread::sleep(Duration::from_millis(50));
    }
}

/// 并行对所有 VID=054C 的设备执行重枚举
pub fn reenumerate() -> bool {
    unsafe {
        // 1. 获取 HID 接口设备列表
        let dev_info_set = match SetupDiGetClassDevsW(
            Some(&GUID_DEVINTERFACE_HID),
            None,
            None,
            DIGCF_PRESENT | DIGCF_DEVICEINTERFACE,
        ) {
            Ok(h) => h,
            Err(_) => return false,
        };

        let mut iface_data = SP_DEVICE_INTERFACE_DATA::default();
        iface_data.cbSize = std::mem::size_of::<SP_DEVICE_INTERFACE_DATA>() as u32;
        let mut index = 0;
        // 收集所有需操作的 SP_DEVINFO_DATA
        let mut devinfos = Vec::new();

        // 2. 枚举每个接口，收集所有匹配的设备
        while SetupDiEnumDeviceInterfaces(
            dev_info_set,
            None,
            &GUID_DEVINTERFACE_HID,
            index,
            &mut iface_data,
        )
        .as_bool()
        {
            // 3. 获取 detail buffer 大小
            let mut required_size: u32 = 0;
            let _ = SetupDiGetDeviceInterfaceDetailW(
                dev_info_set,
                &iface_data,
                None,
                0,
                Some(&mut required_size),
                None,
            );

            // 4. 准备 detail 结构与 devinfo
            let mut raw_buf = vec![0u8; required_size as usize];
            let detail_ptr = raw_buf.as_mut_ptr() as *mut SP_DEVICE_INTERFACE_DETAIL_DATA_W;
            (*detail_ptr).cbSize =
                std::mem::size_of::<SP_DEVICE_INTERFACE_DETAIL_DATA_W>() as u32;

            let mut devinfo = SP_DEVINFO_DATA::default();
            devinfo.cbSize = std::mem::size_of::<SP_DEVINFO_DATA>() as u32;

            let ok = SetupDiGetDeviceInterfaceDetailW(
                dev_info_set,
                &iface_data,
                Some(detail_ptr),
                required_size,
                None,
                Some(&mut devinfo),
            )
            .as_bool();

            if ok {
                // 5. 读取 DevicePath 并转 String
                let path_ptr = &(*detail_ptr).DevicePath as *const u16;
                let offset = (path_ptr as usize) - (raw_buf.as_ptr() as usize);
                let wide_len = (required_size as usize - offset) / 2;
                let wide: &[u16] = std::slice::from_raw_parts(path_ptr, wide_len);
                let mut path = String::from_utf16_lossy(wide);
                if let Some(pos) = path.find('\u{0}') {
                    path.truncate(pos);
                }

                // 6. 筛选 VID_054C，保存 devinfo
                if path.to_uppercase().contains("VID_054C") {
                    devinfos.push(devinfo);
                }
            }

            index += 1;
        }

        // 7. 并行执行 Disable→Enable
        let mut handles = Vec::with_capacity(devinfos.len());
        for devinfo in devinfos.iter().cloned() {
            let handle = thread::spawn({
                let dev_info_set = dev_info_set; // 复制句柄
                move || {
                    property_change(dev_info_set, devinfo);
                }
            });
            handles.push(handle);
        }
        // 等待所有线程完成
        for h in handles {
            let _ = h.join();
        }

        // 8. 清理，返回是否至少处理过一个设备
        SetupDiDestroyDeviceInfoList(dev_info_set);
        !devinfos.is_empty()
    }
}
