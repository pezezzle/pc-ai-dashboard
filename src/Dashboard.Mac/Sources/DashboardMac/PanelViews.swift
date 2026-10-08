import AppKit
import SwiftUI
import ServiceManagement
import DashboardMacKit

struct DashboardPanel: View {
    @ObservedObject var model: DashboardModel
    var openDashboard: () -> Void
    var pin: () -> Void
    var settings: () -> Void
    var body: some View {
        VStack(alignment: .leading, spacing: 12) {
            HStack {
                HStack(spacing: 8) {
                    Image(nsImage: NSApplication.shared.applicationIconImage).resizable().frame(width: 28, height: 28)
                    Text("AI Dashboard").font(.headline)
                }
                Spacer()
                Text("VERBRAUCHT").font(.caption2).foregroundStyle(.secondary)
            }
            ScrollView {
                VStack(spacing: 12) {
                    ForEach(["Codex", "Claude"], id: \.self) { name in
                        ProviderCard(provider: model.providers.first { $0.name == name }, name: name, now: model.now,
                            selection: name == "Codex" ? $model.selectedCodex : $model.selectedClaude)
                    }
                    SystemCard(reading: model.hardware)
                }
            }
            HStack {
                Button("Dashboard", action: openDashboard)
                Button("Anheften", action: pin)
                Spacer()
                Button(action: settings) { Image(systemName: "gearshape") }.help("Einstellungen")
                Button("Beenden") { NSApplication.shared.terminate(nil) }
            }.controlSize(.small)
            Text(model.backendStatus).font(.caption2).foregroundStyle(.secondary)
        }
        .padding(16)
        .frame(minWidth: 370, idealWidth: 400, maxWidth: .infinity, minHeight: 500, maxHeight: .infinity)
        .onChange(of: model.selectedCodex) { model.savePreferences() }
        .onChange(of: model.selectedClaude) { model.savePreferences() }
    }
}

private struct ProviderCard: View {
    var provider: AiUsage?
    var name: String
    var now: Date
    @Binding var selection: String
    private var fresh: Bool { provider?.isFresh(at: now) == true }
    var body: some View {
        VStack(alignment: .leading, spacing: 9) {
            HStack {
                Text(name).font(.headline)
                Spacer()
                Text(fresh ? "Live" : provider?.status == "Live" ? "Veraltet" : provider?.status ?? "Verbinden").font(.caption).foregroundStyle(fresh ? Color.green : Color.secondary)
            }
            if let reset = provider?.manualResets, reset.availableCount > 0 {
                let valid = fresh && (reset.nextExpiresAt.map { Double($0) > now.timeIntervalSince1970 } ?? true)
                let quantity = "\(reset.availableCount) " + (reset.availableCount == 1 ? "manueller Reset" : "manuelle Resets")
                VStack(alignment: .leading, spacing: 3) {
                    Label(valid ? quantity + " verfügbar" : "Letzter Stand: " + quantity, systemImage: "arrow.counterclockwise")
                        .font(.subheadline.bold()).foregroundStyle(valid ? Color.green : Color.secondary)
                    if let expiry = reset.nextExpiresAt {
                        Text("Bekanntes Ablaufdatum: " + Date(timeIntervalSince1970: Double(expiry)).formatted(date: .abbreviated, time: .shortened)).font(.caption2)
                    }
                    Text(valid ? "Nur Anzeige · selbst in Codex prüfen" : "Verfügbarkeit unbestätigt · in Codex prüfen").font(.caption2).foregroundStyle(.secondary)
                }.padding(9).frame(maxWidth: .infinity, alignment: .leading)
                    .background((valid ? Color.green : Color.gray).opacity(0.1), in: RoundedRectangle(cornerRadius: 8))
            }
            if let provider, !provider.quotas.isEmpty {
                ForEach(Array(provider.quotas.enumerated()), id: \.offset) { _, quota in
                    let expired = quota.resetsAt.map { Double($0) <= now.timeIntervalSince1970 } ?? false
                    VStack(spacing: 3) {
                        HStack {
                            Text(quota.label).font(.subheadline)
                            Spacer()
                            Text(percent(quota.usedPercent) + " verbraucht").font(.subheadline.monospacedDigit())
                        }
                        ProgressView(value: min(100, max(0, quota.usedPercent)), total: 100)
                            .tint(quota.usedPercent >= 90 ? .orange : .accentColor)
                        HStack {
                            Text(percent(100 - quota.usedPercent) + " verbleibend")
                            Spacer()
                            Text(countdown(quota.resetsAt, at: now))
                        }.font(.caption2).foregroundStyle(.secondary)
                    }.opacity(fresh && !expired ? 1 : 0.6)
                }
            } else {
                Text(provider?.detail ?? "Account-Limits werden gelesen …").font(.caption).foregroundStyle(.secondary)
            }
            if name == "Codex", provider?.manualResets == nil {
                Text("Manuelle Resets: Status nicht geliefert").font(.caption2).foregroundStyle(.secondary)
            }
            if let credits = provider?.credits {
                HStack {
                    Text("Guthaben")
                    Spacer()
                    if credits.unlimited { Text("Unbegrenzt") }
                    else if let balance = credits.balance { Text(balance.formatted(.number.precision(.fractionLength(2))) + " " + credits.unit) }
                    else { Text("Nicht abrufbar") }
                }.font(.caption).foregroundStyle(.secondary)
            }
            if let sessions = provider?.sessions, !sessions.isEmpty {
                Divider()
                Picker("Kontext", selection: $selection) {
                    Text("Letzter aktiver Chat").tag("")
                    ForEach(sessions, id: \.id) { Text($0.label).tag($0.id) }
                }.font(.caption)
                let session = sessions.first { $0.id == selection } ?? sessions.first!
                HStack {
                    Text(session.usedPercent.map { percent($0) + " Kontext" } ?? "\(session.tokens ?? 0) Tokens")
                    Spacer()
                    if let tokens = session.tokens, let capacity = session.capacity { Text("\(tokens) / \(capacity)") }
                }.font(.caption).foregroundStyle(.secondary)
            }
            if let updatedAt = provider?.updatedAt, let date = parseDate(updatedAt) {
                Text((fresh ? "Abruf: " : "Letzter Stand: ") + date.formatted(date: .omitted, time: .standard)).font(.caption2).foregroundStyle(.secondary)
            }
        }
        .padding(12).frame(maxWidth: .infinity, alignment: .leading)
        .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 11))
    }
}

private struct SystemCard: View {
    var reading: HardwareReading
    var body: some View {
        VStack(alignment: .leading, spacing: 7) {
            Text("SYSTEM").font(.caption.bold()).foregroundStyle(.secondary)
            LazyVGrid(columns: [GridItem(.flexible(), alignment: .leading), GridItem(.flexible(), alignment: .leading)], alignment: .leading, spacing: 8) {
                ForEach(reading.metrics.filter { ["cpuLoad", "cpuTemp", "gpuLoad", "gpuTemp", "ramLoad", "batteryLoad"].contains($0.id) || $0.unit == "RPM" }, id: \.id) { metric in
                    VStack(alignment: .leading, spacing: 2) {
                        Text(metric.label).font(.caption2).foregroundStyle(.secondary)
                        Text(metric.value.map { $0.formatted(.number.precision(.fractionLength(metric.unit == "°C" ? 1 : 0))) + " " + metric.unit } ?? "—")
                            .font(.subheadline.monospacedDigit()).help(metric.source)
                    }
                }
            }
            if let pressure = reading.metrics.first(where: { $0.id == "memoryPressure" })?.value {
                Text("Speicherdruck: " + (pressure == 1 ? "normal" : pressure == 2 ? "erhöht" : pressure == 4 ? "kritisch" : "unbekannt")).font(.caption2).foregroundStyle(.secondary)
            }
            ForEach(reading.drives, id: \.name) { drive in
                Divider().padding(.vertical, 3)
                VStack(alignment: .leading, spacing: 4) {
                    HStack {
                        Label("Speicherplatz", systemImage: "internaldrive").font(.caption).foregroundStyle(.secondary)
                        Spacer()
                        Text(drive.usedPercent.formatted(.number.precision(.fractionLength(1))) + " % belegt")
                            .font(.caption.monospacedDigit())
                    }
                    Text(drive.usedGb.formatted(.number.precision(.fractionLength(1))) + " von " + drive.totalGb.formatted(.number.precision(.fractionLength(1))) + " GB belegt")
                        .font(.subheadline.monospacedDigit())
                    ProgressView(value: min(100, max(0, drive.usedPercent)), total: 100)
                        .tint(drive.usedPercent >= 90 ? .orange : .accentColor)
                    Text(max(0, drive.totalGb - drive.usedGb).formatted(.number.precision(.fractionLength(1))) + " GB frei")
                        .font(.caption2).foregroundStyle(.secondary)
                }
            }
            Text(reading.status).font(.caption2).foregroundStyle(.secondary)
        }.padding(12).frame(maxWidth: .infinity, alignment: .leading)
            .background(Color.primary.opacity(0.04), in: RoundedRectangle(cornerRadius: 11))
    }
}

struct SettingsView: View {
    @ObservedObject var model: DashboardModel
    @State private var loginEnabled = SMAppService.mainApp.status == .enabled
    @State private var notice = ""
    var body: some View {
        Form {
            Picker("Menüleiste", selection: $model.mode) {
                Text("Codex und Claude").tag(MenuMode.both)
                Text("Nur Symbol").tag(MenuMode.icon)
                Text("Codex").tag(MenuMode.codex)
                Text("Claude").tag(MenuMode.claude)
            }
            Toggle("Angeheftetes Panel im Vordergrund", isOn: $model.alwaysOnTop)
            Toggle("Dashboard beim Schließen im Hintergrund behalten", isOn: $model.keepDashboardInBackground)
            Text("Aus: Schließen oder Ausblenden gibt Video und Webansicht frei. Die Menüleiste bleibt aktiv. An: Das Video kann weiterlaufen.").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Toggle("Bei Anmeldung starten", isOn: $loginEnabled).onChange(of: loginEnabled) { _, enabled in
                do {
                    if enabled { try SMAppService.mainApp.register() } else { try SMAppService.mainApp.unregister() }
                    notice = SMAppService.mainApp.status == .requiresApproval ? "Startobjekt in den Systemeinstellungen freigeben." : ""
                } catch { notice = "Autostart konnte nicht geändert werden."; loginEnabled = SMAppService.mainApp.status == .enabled }
            }
            Text("Die Menüleiste zeigt verbrauchte Nutzung, bevorzugt für 5 Stunden. Liefert der Anbieter ein anderes Zeitfenster, wird es beschriftet: 7T bedeutet Wochenlimit. Alle Limits findest du im Panel.").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Text("↻1: ein manueller Reset verfügbar\n↻1?: letzter bekannter Stand\n!: Nutzungswert veraltet").font(.caption.monospaced()).fixedSize(horizontal: false, vertical: true)
            Text("Manuelle Resets werden ausschließlich angezeigt. Diese App kann keinen Reset auslösen.").font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Divider()
            Button("Claude-Code-Kontext verbinden") {
                model.send(["type": "installClaudeIntegration"])
                notice = "Anbindung angefordert. Bestehende Statuszeilen werden erhalten; neue Sitzungen liefern Kontextdaten."
            }
            Text(notice).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
            Text(model.integrationNotice).font(.caption).foregroundStyle(.secondary).fixedSize(horizontal: false, vertical: true)
        }
        .padding(22).frame(width: 460, height: 520)
        .onChange(of: model.mode) { model.savePreferences() }
        .onChange(of: model.alwaysOnTop) { model.savePreferences() }
    }
}
