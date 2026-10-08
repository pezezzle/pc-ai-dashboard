import Foundation
import IOKit

// Read-only AppleSMC ABI, documented by Stats (MIT) and AppleSMC implementations.
// No write selector, fan override, or privileged helper is present.
public final class ReadOnlySMC {
    private var connection: io_connect_t = 0
    private var info: [String: (UInt32, UInt32)] = [:]
    public init() {
        let service = IOServiceGetMatchingService(kIOMainPortDefault, IOServiceMatching("AppleSMC"))
        guard service != 0 else { return }
        defer { IOObjectRelease(service) }
        if IOServiceOpen(service, mach_task_self_, 0, &connection) != KERN_SUCCESS { connection = 0 }
    }
    deinit { if connection != 0 { IOServiceClose(connection) } }

    public func availableKeys() -> [String] {
        guard let count = read("#KEY"), count > 0, count <= 10_000 else { return [] }
        return (0..<Int(count)).compactMap { index in
            guard let response = call(0, size: 0, command: 8, index: UInt32(index)) else { return nil }
            let key: UInt32 = response.withUnsafeBytes { $0.loadUnaligned(as: UInt32.self) }
            return String(bytes: [UInt8((key >> 24) & 255), UInt8((key >> 16) & 255), UInt8((key >> 8) & 255), UInt8(key & 255)], encoding: .ascii)
        }
    }

    public func read(_ key: String) -> Double? {
        guard connection != 0, key.utf8.count == 4 else { return nil }
        let code = key.utf8.reduce(UInt32(0)) { ($0 << 8) | UInt32($1) }
        if info[key] == nil {
            guard let response = call(code, size: 0, command: 9) else { return nil }
            let size: UInt32 = response.withUnsafeBytes { $0.loadUnaligned(fromByteOffset: 28, as: UInt32.self) }
            let type: UInt32 = response.withUnsafeBytes { $0.loadUnaligned(fromByteOffset: 32, as: UInt32.self) }
            guard size > 0, size <= 32 else { return nil }
            info[key] = (size, type)
        }
        guard let (size, type) = info[key], let response = call(code, size: size, command: 5) else { return nil }
        return Self.decode(type: type, bytes: Array(response[48..<(48 + Int(size))]))
    }
    private func call(_ key: UInt32, size: UInt32, command: UInt8, index: UInt32 = 0) -> [UInt8]? {
        // SMCKeyData: key@0, keyInfo@28, result@40, command@42, payload@48.
        guard command == 9 || command == 5 || command == 8 else { return nil }
        var input = [UInt8](repeating: 0, count: 80)
        input.withUnsafeMutableBytes {
            $0.storeBytes(of: key, toByteOffset: 0, as: UInt32.self)
            $0.storeBytes(of: size, toByteOffset: 28, as: UInt32.self)
            $0.storeBytes(of: index, toByteOffset: 44, as: UInt32.self)
        }
        input[42] = command
        var output = [UInt8](repeating: 0, count: 80)
        var count = 80
        let result = input.withUnsafeBytes { source in
            output.withUnsafeMutableBytes { target in
                IOConnectCallStructMethod(connection, 2, source.baseAddress, 80, target.baseAddress, &count)
            }
        }
        return result == KERN_SUCCESS && count == 80 && output[40] == 0 ? output : nil
    }
    public static func decode(type: UInt32, bytes: [UInt8]) -> Double? {
        let code = String(bytes: [UInt8((type >> 24) & 255), UInt8((type >> 16) & 255), UInt8((type >> 8) & 255), UInt8(type & 255)], encoding: .ascii)
        let value: Double
        switch code {
        case "flt " where bytes.count >= 4:
            value = Double(bytes.withUnsafeBytes { $0.loadUnaligned(as: Float.self) })
        case "sp78" where bytes.count >= 2:
            value = Double(Int16(bitPattern: UInt16(bytes[0]) << 8 | UInt16(bytes[1]))) / 256
        case "fpe2" where bytes.count >= 2:
            value = Double(UInt16(bytes[0]) << 8 | UInt16(bytes[1])) / 4
        case "ui8 " where !bytes.isEmpty: value = Double(bytes[0])
        case "ui16" where bytes.count >= 2: value = Double(UInt16(bytes[0]) << 8 | UInt16(bytes[1]))
        case "ui32" where bytes.count >= 4: value = Double(UInt32(bytes[0]) << 24 | UInt32(bytes[1]) << 16 | UInt32(bytes[2]) << 8 | UInt32(bytes[3]))
        default: return nil
        }
        return value.isFinite ? value : nil
    }
}
