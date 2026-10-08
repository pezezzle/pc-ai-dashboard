// swift-tools-version: 6.0
import PackageDescription

let package = Package(
    name: "DashboardMac",
    platforms: [.macOS(.v15)],
    products: [.executable(name: "DashboardMac", targets: ["DashboardMac"])],
    targets: [
        .target(name: "DashboardMacKit", linkerSettings: [.linkedFramework("IOKit")]),
        .executableTarget(name: "DashboardMac", dependencies: ["DashboardMacKit"]),
        .testTarget(name: "DashboardMacKitTests", dependencies: ["DashboardMacKit"])
    ],
    swiftLanguageModes: [.v5]
)
