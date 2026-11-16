import AppKit
let url = URL(fileURLWithPath: CommandLine.arguments[1])
let image = NSImage(size: NSSize(width: 1024, height: 1024))
image.lockFocus()
let rect = NSRect(x: 32, y: 32, width: 960, height: 960)
let rounded = NSBezierPath(roundedRect: rect, xRadius: 215, yRadius: 215)
NSGradient(starting: NSColor(calibratedRed: 0.23, green: 0.51, blue: 0.95, alpha: 1), ending: NSColor(calibratedRed: 0.11, green: 0.30, blue: 0.77, alpha: 1))!.draw(in: rounded, angle: -90)
NSColor.white.setFill()
let left = NSBezierPath(); left.move(to: NSPoint(x: 512, y: 264)); left.curve(to: NSPoint(x: 220, y: 300), controlPoint1: NSPoint(x: 400, y: 326), controlPoint2: NSPoint(x: 300, y: 312)); left.line(to: NSPoint(x: 220, y: 752)); left.curve(to: NSPoint(x: 512, y: 704), controlPoint1: NSPoint(x: 330, y: 764), controlPoint2: NSPoint(x: 420, y: 752)); left.close(); left.fill()
let right = NSBezierPath(); right.move(to: NSPoint(x: 536, y: 264)); right.curve(to: NSPoint(x: 828, y: 300), controlPoint1: NSPoint(x: 648, y: 326), controlPoint2: NSPoint(x: 748, y: 312)); right.line(to: NSPoint(x: 828, y: 752)); right.curve(to: NSPoint(x: 536, y: 704), controlPoint1: NSPoint(x: 718, y: 764), controlPoint2: NSPoint(x: 628, y: 752)); right.close(); right.fill()
image.unlockFocus()
let rep = NSBitmapImageRep(data: image.tiffRepresentation!)!
try rep.representation(using: .png, properties: [:])!.write(to: url)
