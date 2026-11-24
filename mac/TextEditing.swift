import AppKit

@MainActor enum TextEditing {
    static func handle(_ event: NSEvent, in window: NSWindow) -> Bool {
        let flags = event.modifierFlags.intersection([.command, .control, .option, .shift])
        guard flags == [.command] || flags == [.command, .shift],
              let editor = window.firstResponder as? NSTextView else { return false }
        let key = event.charactersIgnoringModifiers?.lowercased() ?? ""
        if flags.contains(.shift) && key != "z" { return false }
        switch key {
        case "a": editor.selectAll(nil)
        case "c": editor.copy(nil)
        case "x": editor.cut(nil)
        case "v": editor.paste(nil)
        case "z":
            if flags.contains(.shift) { if editor.undoManager?.canRedo == true { editor.undoManager?.redo() } }
            else if editor.undoManager?.canUndo == true { editor.undoManager?.undo() }
        default: return false
        }
        return true
    }

    static func installMenu() {
        let main = NSMenu()
        let edit = NSMenu(title: L.text("Bearbeiten"))
        for (title, action, key, shift) in [
            ("Rückgängig", "undo:", "z", false), ("Wiederholen", "redo:", "z", true),
            ("Ausschneiden", "cut:", "x", false), ("Kopieren", "copy:", "c", false),
            ("Einfügen", "paste:", "v", false), ("Alles markieren", "selectAll:", "a", false)
        ] {
            let item = NSMenuItem(title: L.text(title), action: NSSelectorFromString(action), keyEquivalent: key)
            item.keyEquivalentModifierMask = shift ? [.command, .shift] : [.command]
            edit.addItem(item)
        }
        let item = NSMenuItem(title: L.text("Bearbeiten"), action: nil, keyEquivalent: "")
        item.submenu = edit
        main.addItem(item)
        NSApp.mainMenu = main
    }
}
