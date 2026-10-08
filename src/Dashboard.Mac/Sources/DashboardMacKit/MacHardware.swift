import Foundation
import IOKit
import IOKit.ps
import Darwin

public final class MacHardware {
    private let host = mach_host_self()
    private let smc = ReadOnlySMC()
    private var previous: [UInt32]?
    private let chip: String
    public init() { chip = Self.sysctlString("machdep.cpu.brand_string") }
    deinit { mach_port_deallocate(mach_task_self_, host) }

    public func read() -> HardwareReading {
        var metrics: [Metric] = [Metric("cpuLoad", "CPU gesamt", cpuLoad(), "%")]
        var vm = vm_statistics64()
        var count = mach_msg_type_number_t(MemoryLayout<vm_statistics64>.size / MemoryLayout<integer_t>.size)
        let result = withUnsafeMutablePointer(to: &vm) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) {
                host_statistics64(host, HOST_VM_INFO64, $0, &count)
            }
        }
        let total = Double(ProcessInfo.processInfo.physicalMemory) / 1_073_741_824
        let used = result == KERN_SUCCESS ? min(total, Double(UInt64(vm.active_count) + UInt64(vm.wire_count) + UInt64(vm.compressor_page_count)) * Double(vm_kernel_page_size) / 1_073_741_824) : nil
        metrics += [Metric("ramUsed", "RAM aktiv / wired / komprimiert", used, "GB"), Metric("ramTotal", "RAM gesamt", total, "GB"), Metric("ramLoad", "RAM genutzt", used.map { $0 / total * 100 }, "%")]
        var pressure: Int32 = 0; var length = MemoryLayout<Int32>.size
        let pressureResult = sysctlbyname("kern.memorystatus_vm_pressure_level", &pressure, &length, nil, 0)
        metrics.append(Metric("memoryPressure", "Speicherdruck (1 normal, 2 erhöht, 4 kritisch)", pressureResult == 0 ? Double(pressure) : nil, "Status"))
        let keys = temperatureKeys()
        for (id, label, candidates) in [("cpuTemp", "CPU Sensoren (Mittel)", keys.cpu), ("gpuTemp", "GPU Sensoren (Mittel)", keys.gpu)] {
            let readings = candidates.compactMap { key -> (String, Double)? in
                guard let value = smc.read(key), value > 0, value < 150 else { return nil }
                return (key, value)
            }
            let value = readings.isEmpty ? nil : readings.map { $0.1 }.reduce(0, +) / Double(readings.count)
            metrics.append(Metric(id, label, value, "°C", "AppleSMC" + (readings.isEmpty ? "" : ": " + readings.map { $0.0 }.joined(separator: ", "))))
        }
        metrics.append(Metric("gpuLoad", "GPU gesamt", gpuLoad(), "%", "IOAccelerator"))
        if let fanCount = smc.read("FNum"), fanCount >= 0, fanCount <= 8 {
            for index in 0..<Int(fanCount) {
                let rpm = smc.read("F\(index)Ac")
                metrics.append(Metric("fan\(index)Rpm", "Lüfter \(index + 1)", rpm, "RPM", "AppleSMC"))
            }
        }
        if let info = IOPSCopyPowerSourcesInfo()?.takeRetainedValue(), let list = IOPSCopyPowerSourcesList(info)?.takeRetainedValue() as? [CFTypeRef] {
            for source in list {
                guard let battery = IOPSGetPowerSourceDescription(info, source)?.takeUnretainedValue() as? [String: Any],
                      let current = battery["Current Capacity"] as? NSNumber,
                      let maximum = battery["Max Capacity"] as? NSNumber, maximum.doubleValue > 0 else { continue }
                metrics.append(Metric("batteryLoad", "Akku", current.doubleValue / maximum.doubleValue * 100, "%"))
                metrics.append(Metric("batteryCharging", "Akku lädt", (battery["Is Charging"] as? NSNumber)?.boolValue == true ? 1 : 0, "Status"))
                break
            }
        }
        var drives: [DriveUsage] = []
        if let volume = try? URL(fileURLWithPath: NSHomeDirectory()).resourceValues(forKeys: [.volumeTotalCapacityKey, .volumeAvailableCapacityKey]),
           let size = volume.volumeTotalCapacity, let free = volume.volumeAvailableCapacity, size > 0 {
            drives.append(DriveUsage(name: "/", label: "Macintosh", usedGb: Double(size - free) / 1_073_741_824, totalGb: Double(size) / 1_073_741_824, usedPercent: Double(size - free) / Double(size) * 100))
        }
        let temperatures = metrics.filter { $0.unit == "°C" && $0.value != nil }.count
        return HardwareReading(metrics: metrics, drives: drives, status: temperatures > 0 ? "macOS live · AppleSMC \(temperatures)/2 Temperaturen" : "macOS live · Temperaturen nicht verfügbar")
    }

    private func cpuLoad() -> Double? {
        var load = host_cpu_load_info()
        var count = mach_msg_type_number_t(MemoryLayout<host_cpu_load_info>.size / MemoryLayout<integer_t>.size)
        let result = withUnsafeMutablePointer(to: &load) {
            $0.withMemoryRebound(to: integer_t.self, capacity: Int(count)) { host_statistics(host, HOST_CPU_LOAD_INFO, $0, &count) }
        }
        guard result == KERN_SUCCESS else { return nil }
        let ticks = [load.cpu_ticks.0, load.cpu_ticks.1, load.cpu_ticks.2, load.cpu_ticks.3]
        defer { previous = ticks }
        guard let previous else { return nil }
        let delta = zip(ticks, previous).map { UInt64($0 &- $1) }
        let total = delta.reduce(0, +)
        return total > 0 ? Double(total - delta[Int(CPU_STATE_IDLE)]) / Double(total) * 100 : nil
    }

    private func gpuLoad() -> Double? {
        var iterator: io_iterator_t = 0
        guard IOServiceGetMatchingServices(kIOMainPortDefault, IOServiceMatching("IOAccelerator"), &iterator) == KERN_SUCCESS else { return nil }
        defer { IOObjectRelease(iterator) }
        var readings: [Double] = []
        while true {
            let service = IOIteratorNext(iterator); if service == 0 { break }
            defer { IOObjectRelease(service) }
            guard let stats = IORegistryEntryCreateCFProperty(service, "PerformanceStatistics" as CFString, kCFAllocatorDefault, 0)?.takeRetainedValue() as? [String: Any] else { continue }
            if let value = (stats["Device Utilization %"] ?? stats["GPU Activity(%)"]) as? NSNumber, value.doubleValue.isFinite, (0...100).contains(value.doubleValue) { readings.append(value.doubleValue) }
        }
        return readings.max()
    }

    private func temperatureKeys() -> (cpu: [String], gpu: [String]) {
        if chip.contains("M3") {
            // Firmware can expose older Tp/Tg thermal-zone aliases on an M3.
            // These aliases were observed on Mac15,13; keep the actual keys in
            // Source and describe an average of readable zones, never CPU cores.
            return (["Te05", "Te0L", "Te0P", "Te0S", "Tf04", "Tf09", "Tf0A", "Tf0B", "Tf0D", "Tf0E", "Tf44", "Tf49", "Tf4A", "Tf4B", "Tf4D", "Tf4E", "Tp01", "Tp05", "Tp09", "Tp0D", "Tp0H", "Tp0L", "Tp0P", "Tp0X", "Tp0b"], ["Tf14", "Tf18", "Tf19", "Tf1A", "Tf24", "Tf28", "Tf29", "Tf2A", "Tg0D", "Tg0j", "Tg0X", "Tg0b"])
        }
        if chip.contains("M2") { return (["Tp1h", "Tp1t", "Tp1p", "Tp1l", "Tp01", "Tp05", "Tp09", "Tp0D", "Tp0f", "Tp0j"], ["Tg0f", "Tg0j"]) }
        if chip.contains("M1") { return (["Tp09", "Tp0T", "Tp01", "Tp05", "Tp0D", "Tp0H", "Tp0L", "Tp0P", "Tp0X", "Tp0b"], ["Tg05", "Tg0D", "Tg0L", "Tg0T"]) }
        if chip.contains("M4") { return (["Tp01", "Tp05", "Tp09", "Tp0D", "Tp0V", "Tp0Y", "Tp0b", "Tp0e"], ["Tg0G", "Tg0H", "Tg1U", "Tg1k", "Tg0K", "Tg0L", "Tg0d", "Tg0e", "Tg0j", "Tg0k"]) }
        if chip.contains("Intel") { return (["TC0P", "TC0E", "TC0F"], ["TG0P", "TG0D"]) }
        return ([], []) // Unknown chips stay explicitly unsupported rather than guessed.
    }
    private static func sysctlString(_ name: String) -> String {
        var size = 0
        guard sysctlbyname(name, nil, &size, nil, 0) == 0, size > 0 else { return "" }
        var bytes = [CChar](repeating: 0, count: size)
        guard sysctlbyname(name, &bytes, &size, nil, 0) == 0 else { return "" }
        return String(cString: bytes)
    }
}
