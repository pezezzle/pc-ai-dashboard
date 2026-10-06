# PC / AI Dashboard

Windows-App für einen eingebauten HDMI-Monitor. Zeigt Hardware und Claude-/Codex-Verbrauch gemeinsam an, mit optionalem Video-Hintergrund. C# / .NET 10, WPF, WebView2 und TypeScript. Für 1024 × 600 entworfen, proportional skalierbar.

## Start

Nach dem Build `Start-App.cmd` oder `artifacts/app/PcAiDashboard.exe` öffnen. Der kleinste Monitor wird beim ersten Start gewählt. Über ⚙ kann der Monitor geändert werden. F11 wechselt Vollbild; Esc öffnet den Fenstermodus. Das Symbol im Infobereich öffnet Einstellungen und beendet die App.

Voraussetzungen: Windows 10/11, WebView2 Runtime, Aquasuite mit aktivem CPU-Package-Export, ein OCTO für dessen direkte Sensoren, NVIDIA-Treiber mit `nvidia-smi`, angemeldetes Codex CLI und Claude Desktop oder Claude Code. Der veröffentlichte App-Ordner enthält die .NET-Laufzeit.

## Build & Prüfung

Entwicklungswerkzeuge: .NET 10 SDK, Node.js, npm und Microsoft Edge für die UI-Tests.

```powershell
./Build.ps1 -Publish -Verify
```

Der App-Ordner muss vollständig erhalten bleiben; die EXE allein reicht nicht. Quellcode und generiertes `Web/main.js` werden gemeinsam versioniert. Builds und private Laufzeitdaten sind ausgeschlossen.

## Hardware

- OCTO: vier physische Temperaturkanäle und acht Lüfter-/Pumpen-RPM-Kanäle werden ausschließlich aus HID-Statusreports gelesen. Es werden keine Steuerbefehle, PWM- oder Pumpeneinstellungen geschrieben.
- Zuordnung über Einstellungen, Kanalnummern dort beginnen bei 1. Standard: Pump Coolant 1, Radiator Coolant 3, Case 4; Side 1, Bottom 2, Back 3, Top 4, Pumpe 5. Das ist die Zuordnung dieses PCs.
- CPU Package: automatischer Aquasuite-Export als XML im Shared Memory mit Namen `PC-AI-Dashboard`, Intervall 1 Sekunde, aktiv. Datenquelle `CPU Package` mit Einheit °C hinzufügen. Dieser Export wurde auf dem Ziel-PC eingerichtet. Aquasuite muss dafür geöffnet bleiben; bei Bedarf startet die App es minimiert.
- Alternativ ist eine XML-Exportdatei wählbar. Exporte mit mehr als 10 Sekunden Alter werden verworfen. Der Export wird nur gelesen.
- CPU-Gesamtauslastung: Windows-Systemzeiten. GPU-Core-Temperatur und GPU-Auslastung: NVIDIA, alle 2 Sekunden. RAM: physischer Windows-Arbeitsspeicher. Laufwerke: lokal eingebundene feste Volumes, belegter und freier Speicher.
- Bei einem Splitter ist die RPM-Anzeige das Tachosignal des angeschlossenen Kanals, kein Mittelwert aller Lüfter.

[Aquasuite-Datenexport, Handbuch Abschnitt 10.4](https://www.aquacomputer.de/tl_files/aquacomputer/downloads/manuals/aquaero_5_aquaero_6_english.pdf). Das OCTO-Statusreport-Layout wurde anhand der [öffentlichen Protokollbeschreibung im liquidctl-Projekt](https://github.com/liquidctl/liquidctl/blob/main/liquidctl/driver/aquacomputer.py) und Live-Reports überprüft. Der Adapter enthält nur eigene Lese- und Dekodierlogik.

## KI-Verbrauch

Account-Limits werden alle 60 Sekunden aktualisiert. Fehlende Zeitfenster erscheinen nicht als 0 %. Letzte bekannte Werte werden bei Verbindungsfehlern sichtbar als solche dargestellt. Zurücksetzung bedeutet die vom Anbieter angegebene Zeit; die App setzt kein Limit selbst zurück.

- Codex: `codex app-server`, dokumentiertes `account/rateLimits/read`. Es werden keine Modellanfragen gestartet. Die Anmeldung bleibt im Codex CLI.
- Claude: gültige Claude-Code-OAuth-Anmeldung, andernfalls der dedizierte OAuth-Cache der Claude-Desktop-App für den aktuellen Windows-Benutzer. Keine Browsercookies. Entschlüsselung erfolgt lokal über Windows DPAPI und AES-GCM; Tokens werden nur im Arbeitsspeicher verwendet und nicht in Dashboard-Dateien geschrieben. Der OAuth-Usage-Endpunkt ist keine zugesicherte öffentliche API und kann sich ändern. Ist kein gültiger Zugang verfügbar, bleibt die Anzeige ausdrücklich unavailable und kann Werte aus der Code-Statuszeile übernehmen.
- Codex-Kontext: letzte lokale `token_count`-Messung pro Chat aus den letzten Sitzungsdateien. Die App liest nur die Dateiendstücke und behält keine Gesprächstexte. Der angegebene Zeitstempel bleibt sichtbar; dies ist keine kontinuierliche Messung eines unbenutzten Chats.
- Claude-Code-Kontext: die Statuszeile schreibt ausschließlich Session-ID, Anzeigename, Kontextmessung und Limits. Die App installiert sie beim ersten Start, falls noch keine Statuszeile existiert. Bestehende Statuszeilen bleiben erhalten. Vorhandene Einstellungen werden vor einer Ergänzung lokal gesichert. Werte stehen ab der nächsten Claude-Code-Sitzung bereit; Kontext aus normalen Claude-Desktop-Chats ist nicht angebunden.

[Codex App Server](https://learn.chatgpt.com/docs/app-server) · [Claude-Code-Statuszeile](https://code.claude.com/docs/en/statusline). Das Windows-Claude-Cacheformat ist auch im [Claude Code Usage Monitor](https://github.com/CodeZeno/Claude-Code-Usage-Monitor/blob/main/src/poller/claude_desktop.rs) dokumentiert. Die Implementierung hier verwendet eigene .NET-Kryptografie und kopiert keinen Fremdcode.

## Video & Ton

Über ⚙ einen YouTube-Link oder eine Video-ID auswählen, oder eine lokale MP4/WebM-Datei. Ton an/aus und Lautstärke sind über die obere Leiste erreichbar. ▶ pausiert oder startet. „Player bedienen“ blendet das Dashboard aus, damit YouTube-Steuerelemente erreichbar bleiben. Die Einstellungen werden lokal gespeichert.

YouTube ist der reguläre eingebettete Player; blockierte Einbettung, Werbung, Netzwerkausfall und Autoplay-Vorgaben können die Wiedergabe beeinflussen. Ein erster Klick kann erforderlich sein. Das für privaten Gebrauch gewünschte Dashboard-Overlay weicht von YouTubes [Vorgaben zu Overlays](https://developers.google.com/youtube/terms/required-minimum-functionality#overlays-and-frames) ab. Lokale Videos benötigen keine Internetverbindung.

## Daten & Rücknahme

Einstellungen, WebView2-Profil, begrenztes Fehlerlog und Claude-Code-Messwerte liegen unter `%LOCALAPPDATA%/PcAiDashboard`. Diese Daten, Zugangsdaten und Diagnosesnapshots werden nicht eingecheckt. Hardwaremesswerte werden nicht hochgeladen. YouTube und die KI-Anbieter werden nur für ihre jeweilige Funktion kontaktiert.

Autostart ist standardmäßig aus. Wird er aktiviert, verwendet die App den benutzerspezifischen Windows-Run-Eintrag `PcAiDashboard`. Zur Rücknahme in der App deaktivieren. Zum Entfernen der Claude-Statuszeile den von dieser App gesetzten `statusLine`-Eintrag aus `~/.claude/settings.json` entfernen oder die Sicherung `claude-settings.before-dashboard.json` vergleichen; keine zwischenzeitlichen Änderungen überschreiben.

Diagnose ohne Fenster:

```powershell
./artifacts/app/PcAiDashboard.exe --diagnostics "$env:TEMP/dashboard-diagnostics.json"
```

Die JSON-Datei enthält Messwerte und Verbrauchsmetadaten, niemals Tokens. Debugging über CDP ist ausschließlich für einen gezielt gestarteten Testprozess vorgesehen und ist im normalen Start deaktiviert.
