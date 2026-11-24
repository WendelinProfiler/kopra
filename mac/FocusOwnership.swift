import AppKit

extension AppModel {
    func beginFocusOwnership(){
        focusGuard?.invalidate()
        let timer=Timer(timeInterval:0.15,repeats:true){[weak self] _ in Task{@MainActor in self?.ensureFocusOwnership()}}
        focusGuard=timer;RunLoop.main.add(timer,forMode:.common)
        DispatchQueue.main.async{[weak self] in self?.ensureFocusOwnership()}
    }
    func ensureFocusOwnership(){
        guard let panel,panel.isVisible else{return}
        let owned=NSApp.modalWindow ?? panel.attachedSheet ?? NSApp.keyWindow.flatMap{$0.isVisible ? $0:nil} ?? panel
        if !NSApp.isActive {
            if let front=NSWorkspace.shared.frontmostApplication,front.processIdentifier != ProcessInfo.processInfo.processIdentifier,front.activationPolicy != .regular{return}
            NSApp.activate(ignoringOtherApps:true)
        }
        if !owned.isKeyWindow{owned.makeKeyAndOrderFront(nil)}
        guard owned===panel else{return}
        let responder=panel.firstResponder
        if !settings,!(responder is NSTextView),!(responder is NSControl),let field=firstSearchField(panel.contentView){panel.makeFirstResponder(field)}
        else if responder == nil || responder===panel{panel.selectNextKeyView(nil)}
    }
    private func firstSearchField(_ view:NSView?)->NSTextField?{
        guard let view,!view.isHiddenOrHasHiddenAncestor else{return nil}
        if let field=view as? NSTextField,field.isEditable,field.isEnabled{return field}
        for child in view.subviews{if let field=firstSearchField(child){return field}}
        return nil
    }
}
