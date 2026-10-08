import AppKit

// Transparent native vector glyphs use the same foreground as the menu text.
// The full provider names remain available in the accessibility label.
public final class MenuTitleRenderer {
    public init() { }
    public func render(_ summary: MenuSummary, appearance: NSAppearance? = nil) -> NSAttributedString {
        var foreground = NSColor.labelColor
        (appearance ?? NSAppearance.currentDrawing()).performAsCurrentDrawingAppearance {
            foreground = NSColor.labelColor.usingColorSpace(.deviceRGB) ?? .labelColor
        }
        let attributes: [NSAttributedString.Key: Any] = [.font: NSFont.monospacedDigitSystemFont(ofSize: 12, weight: .medium), .foregroundColor: foreground]
        let result = NSMutableAttributedString(string: "")
        func append(_ text: String) { result.append(NSAttributedString(string: text, attributes: attributes)) }
        for (index, item) in summary.items.enumerated() {
            if index > 0 { append("  ·  ") }
            let attachment = NSTextAttachment()
            attachment.image = ProviderGlyph.image(item.provider, color: foreground)
            attachment.bounds = NSRect(x: 0, y: -3, width: 18, height: 18)
            result.append(NSAttributedString(attachment: attachment))
            append(" " + item.value)
        }
        if !summary.resetIndicator.isEmpty {
            if !summary.items.isEmpty { append("  ") }
            append(summary.resetIndicator)
        }
        return result
    }
}
