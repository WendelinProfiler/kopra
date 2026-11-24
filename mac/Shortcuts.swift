import AppKit
import Carbon
import SwiftUI

@MainActor final class MacShortcuts {
    weak var model:AppModel?;var handler:EventHandlerRef?;var search:EventHotKeyRef?;var history:EventHotKeyRef?;var searchID:UInt32=0;var historyID:UInt32=0;var nextID:UInt32=10
    init(_ model:AppModel){self.model=model;var spec=EventTypeSpec(eventClass:OSType(kEventClassKeyboard),eventKind:UInt32(kEventHotKeyPressed));InstallEventHandler(GetApplicationEventTarget(),{_,event,context in guard let event,let context else{return noErr};var key=EventHotKeyID();let result=GetEventParameter(event,EventParamName(kEventParamDirectObject),EventParamType(typeEventHotKeyID),nil,MemoryLayout<EventHotKeyID>.size,nil,&key);if result==noErr{let owner=Unmanaged<MacShortcuts>.fromOpaque(context).takeUnretainedValue();let id=key.id;Task{@MainActor in owner.pressed(id)}};return noErr},1,&spec,Unmanaged.passUnretained(self).toOpaque(),&handler);resume()}
    func pressed(_ id:UInt32){guard let model,!model.recordingShortcut else{return};if id==searchID{model.toggle()}else if id==historyID{model.toggle(history:true)}}
    static func modifiers(_ flags:NSEvent.ModifierFlags)->UInt32{var value:UInt32=0;if flags.contains(.command){value|=UInt32(cmdKey)};if flags.contains(.shift){value|=UInt32(shiftKey)};if flags.contains(.option){value|=UInt32(optionKey)};if flags.contains(.control){value|=UInt32(controlKey)};return value}
    static func valid(_ key:UInt32,_ modifiers:UInt32)->Bool{let functions:Set<UInt32>=[122,120,99,118,96,97,98,100,101,109,103,111,105,107,113,106,64,79,80,90];guard key<128,(modifiers&UInt32(cmdKey|optionKey|controlKey)) != 0 || functions.contains(key) else{return false};if modifiers==UInt32(cmdKey),[12,13,4,46,48].contains(key){return false};return !(key==53 && modifiers==UInt32(cmdKey|optionKey))}
    static func label(_ event:NSEvent)->String{var text="";let flags=event.modifierFlags;if flags.contains(.control){text+="⌃"};if flags.contains(.option){text+="⌥"};if flags.contains(.shift){text+="⇧"};if flags.contains(.command){text+="⌘"};let names:[UInt16:String]=[49:"Leertaste",36:"Enter",48:"Tab",51:"⌫",53:"Escape",123:"←",124:"→",125:"↓",126:"↑",122:"F1",120:"F2",99:"F3",118:"F4",96:"F5",97:"F6",98:"F7",100:"F8",101:"F9",109:"F10",103:"F11",111:"F12",105:"F13",107:"F14",113:"F15",106:"F16",64:"F17",79:"F18",80:"F19",90:"F20"];return text+(names[event.keyCode] ?? event.charactersIgnoringModifiers?.uppercased() ?? "Taste")}
    func matches(_ event:NSEvent,history:Bool)->Bool{guard let model else{return false};let p=model.history.preferences;return UInt32(event.keyCode)==(history ? p.historyKey:p.searchKey) && Self.modifiers(event.modifierFlags)==(history ? p.historyModifiers:p.searchModifiers)}
    func register(_ key:UInt32,_ modifiers:UInt32,historyMode:Bool)->Bool{nextID+=1;var ref:EventHotKeyRef?;let id=EventHotKeyID(signature:0x4B4F5052,id:nextID);guard RegisterEventHotKey(key,modifiers,id,GetApplicationEventTarget(),0,&ref)==noErr else{return false};if historyMode{if let history{UnregisterEventHotKey(history)};history=ref;historyID=nextID}else{if let search{UnregisterEventHotKey(search)};search=ref;searchID=nextID};return true}
    func change(_ event:NSEvent,historyMode:Bool)->String?{guard let model else{return "Tastenkürzel nicht verfügbar."};let key=UInt32(event.keyCode),mods=Self.modifiers(event.modifierFlags);guard Self.valid(key,mods) else{return "Bitte eine Kombination mit ⌘, ⌥ oder ⌃ wählen."};let p=model.history.preferences;if key==(historyMode ? p.searchKey:p.historyKey),mods==(historyMode ? p.searchModifiers:p.historyModifiers){return "Diese Kombination verwendet bereits die andere Aktion."};guard register(key,mods,historyMode:historyMode) else{return "Diese Kombination ist belegt. Das bisherige Kürzel bleibt erhalten."};if historyMode{model.history.preferences.historyKey=key;model.history.preferences.historyModifiers=mods;model.history.preferences.historyLabel=Self.label(event)}else{model.history.preferences.searchKey=key;model.history.preferences.searchModifiers=mods;model.history.preferences.searchLabel=Self.label(event)};model.save();return nil}
    func suspend(){if let search{UnregisterEventHotKey(search)};if let history{UnregisterEventHotKey(history)};search=nil;history=nil}
    func resume(){guard let model else{return};var p=model.history.preferences;if search==nil && !register(p.searchKey,p.searchModifiers,historyMode:false){if register(49,UInt32(controlKey|optionKey),historyMode:false){model.history.preferences.searchKey=49;model.history.preferences.searchModifiers=UInt32(controlKey|optionKey);model.history.preferences.searchLabel="⌃⌥Leertaste";model.save();model.status="Bisheriges Kürzel belegt. Suche jetzt mit ⌃⌥Leertaste."}else{model.status="Suchkürzel belegt. Kopra bleibt über das Menü erreichbar."}};p=model.history.preferences;if history==nil && !register(p.historyKey,p.historyModifiers,historyMode:true){model.status="Verlaufskürzel belegt. Wähle in den Einstellungen ein anderes."}}
    func dispose(){suspend();if let handler{RemoveEventHandler(handler)};handler=nil}
}
final class ShortcutCaptureView:NSView {
    var captured:((NSEvent)->Void)?
    override var acceptsFirstResponder:Bool{true}
    override func viewDidMoveToWindow(){super.viewDidMoveToWindow();DispatchQueue.main.async{[weak self] in guard let self else{return};self.window?.makeFirstResponder(self)}}
    override func keyDown(with event:NSEvent){captured?(event)}
}
struct ShortcutCapture:NSViewRepresentable {
    let captured:(NSEvent)->Void
    func makeNSView(context:Context)->ShortcutCaptureView{let view=ShortcutCaptureView();view.captured=captured;return view}
    func updateNSView(_ nsView:ShortcutCaptureView,context:Context){}
}
struct ShortcutRecorder:View {
    @ObservedObject var model:AppModel;let history:Bool
    @Environment(\.dismiss) var dismiss
    @State var message="Drücke die gewünschte Tastenkombination."
    var body:some View{VStack(alignment:.leading,spacing:16){Text(history ? "Verlauf öffnen":"Suche öffnen").font(.title2);Text(message).font(.body);ShortcutCapture{event in if event.keyCode==53{dismiss();return};if let error=model.shortcutService?.change(event,historyMode:history){message=error}else{dismiss()}}.frame(height:2);Text("Escape bricht ab. Andere Apps können bereits belegte Kombinationen reservieren.").font(.caption).foregroundStyle(.secondary);Button("Abbrechen"){dismiss()}}.padding(24).frame(width:390).onAppear{model.recordingShortcut=true;model.shortcutService?.suspend()}.onDisappear{model.recordingShortcut=false;model.shortcutService?.resume()}}
}
