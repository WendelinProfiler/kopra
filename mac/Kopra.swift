import SwiftUI
import AppKit
import Carbon
import ServiceManagement

struct LauncherView: View {
    @ObservedObject var model: AppModel
    @FocusState private var searchFocused: Bool
    var selected:SearchResult? {model.rows.indices.contains(model.selected) ? model.rows[model.selected]:nil}
    var body: some View {
        VStack(spacing:0){
            if model.historyMode || model.settings {HStack{Button{model.historyMode=false;model.settings=false;model.query="";model.search();searchFocused=true}label:{Image(systemName:"chevron.left")}.buttonStyle(.plain)
                Text(model.settings ? "Einstellungen":"Verlauf").foregroundStyle(.secondary);Spacer()}.font(.system(size:12)).padding(.bottom,12)}
            if model.settings {SettingsView(model:model)} else {
                HStack(spacing:14){Image(systemName:"magnifyingglass").font(.system(size:24,weight:.light)).foregroundStyle(.secondary);TextField(model.historyMode ? "Verlauf durchsuchen":"Suchen …",text:$model.query).textFieldStyle(.plain).font(.system(size:26)).focused($searchFocused);Button{model.settings=true}label:{Image(systemName:"gearshape").font(.system(size:19,weight:.regular))}.buttonStyle(.plain).foregroundStyle(.secondary).accessibilityLabel("Einstellungen")}.padding(.vertical,10)
                HStack{Text(model.historyMode ? "\(model.rows.count) \(model.rows.count == 1 ? "Eintrag":"Einträge")":model.query.isEmpty ? "Vorschläge":"Suchergebnisse").font(.system(size:11)).foregroundStyle(.secondary);Spacer()}.padding(.top,13).padding(.bottom,9)
                HStack(alignment:.top,spacing:16){
                    ScrollViewReader{proxy in ScrollView{LazyVStack(spacing:3){ForEach(Array(model.rows.enumerated()),id:\.element.id){index,row in Button{model.selected=index}label:{HStack(spacing:12){ResultIcon(row:row).frame(width:34,height:34);VStack(alignment:.leading,spacing:4){Text(row.title).font(.system(size:14,weight:.medium)).lineLimit(1);Text(row.subtitle).font(.system(size:11)).foregroundStyle(.secondary).lineLimit(1)};Spacer(minLength:0)}.padding(.horizontal,11).padding(.vertical,9).frame(maxWidth:.infinity,alignment:.leading).background(model.selected==index ? Color.accentColor:Color.clear,in:RoundedRectangle(cornerRadius:9)).contentShape(Rectangle())}.buttonStyle(.plain).simultaneousGesture(TapGesture(count:2).onEnded{model.selected=index;model.execute()}).id(row.id)}}}.onChange(of:model.selected){index in if model.rows.indices.contains(index){proxy.scrollTo(model.rows[index].id)}}}
                    if model.historyMode,let row=selected,let entry=row.entry{Divider();VStack(alignment:.leading,spacing:12){Text(entry.kind=="text" ? "Textvorschau":"Vorschau").font(.system(size:13,weight:.medium));Text(Date(timeIntervalSince1970:Double(entry.created)/1000),style:.date).font(.system(size:10)).foregroundStyle(.secondary);ScrollView{VStack(alignment:.leading,spacing:10){if let path=entry.paths.first,let image=NSImage(contentsOfFile:path){Image(nsImage:image).resizable().scaledToFit().frame(maxHeight:180)};Text(entry.kind=="text" ? String(entry.text.prefix(6000)):entry.paths.map{URL(fileURLWithPath:$0).lastPathComponent}.joined(separator:"\n")).font(.system(size:13)).textSelection(.enabled).frame(maxWidth:.infinity,alignment:.leading)}};Spacer(minLength:4);VStack(spacing:10){HStack{if entry.kind != "text"{Button("Öffnen"){model.openHistory(entry)}.buttonStyle(.borderedProminent)};Button("Kopieren"){model.restore(entry,paste:false)}.buttonStyle(.bordered);Spacer()};HStack{Text(entry.kind=="text" ? "↩ Einfügen":"⌘↩ Einfügen").font(.system(size:10)).foregroundStyle(.secondary);Spacer();Button{model.history.pin(entry);model.changed()}label:{Image(systemName:entry.pinned ? "pin.fill":"pin")}.buttonStyle(.plain).accessibilityLabel("Anheften");Button{model.history.remove(entry);model.save();model.changed()}label:{Image(systemName:"trash")}.buttonStyle(.plain).accessibilityLabel("Eintrag löschen")}}}.frame(width:220)}
                }.overlay{if model.rows.isEmpty{VStack(spacing:12){Image(systemName:"magnifyingglass").font(.largeTitle).foregroundStyle(.secondary);Text(model.query.isEmpty ? "Deine Zwischenablage":"Keine Ergebnisse").font(.headline);Text(model.query.isEmpty ? "Kopiere Text, Bilder, Dateien oder Ordner.":"Versuche einen anderen Suchbegriff.").font(.caption).foregroundStyle(.secondary)}.frame(maxWidth:.infinity,maxHeight:.infinity)}}
            }
            HStack{Text(model.settings ? "esc  Ausblenden":"↑↓  Auswählen     ↩  Öffnen / Einfügen     esc  Ausblenden");Spacer();Text(model.status).lineLimit(1)}.font(.system(size:10)).foregroundStyle(.secondary).padding(.top,13)
        }.padding(18).frame(maxWidth:.infinity,maxHeight:.infinity).onAppear{searchFocused=true}.onChange(of:model.settings){_ in searchFocused = !model.settings}.onChange(of:model.historyMode){_ in searchFocused=true}
    }
}
struct ResultIcon: View {
    let row: SearchResult
    var body: some View {Group{if let url=row.url,url.isFileURL {if let image=NSImage(contentsOf:url),row.entry != nil{Image(nsImage:image).resizable().scaledToFit()}else{Image(nsImage:NSWorkspace.shared.icon(forFile:url.path)).resizable().scaledToFit()}}else{Image(systemName:row.symbol).resizable().scaledToFit().padding(4).foregroundStyle(.secondary)}}}
}
struct SettingsView: View {
    @ObservedObject var model: AppModel
    @State var clearAll=false;@State var clearUnpinned=false;@State var advanced=false;@State var recordSearch=false;@State var recordHistory=false
    func binding<T>(_ path:WritableKeyPath<MacPreferences,T>)->Binding<T>{Binding(get:{model.history.preferences[keyPath:path]},set:{model.history.preferences[keyPath:path]=$0;model.save();model.history.prune();model.changed()})}
    var body: some View {
        VStack(alignment:.leading,spacing:14){Text("Kopra").font(.system(size:25,weight:.semibold));Picker("Bereich",selection:$model.settingsTab){Text("Allgemein").tag(0);Text("Verlauf").tag(1);Text("Geräte").tag(2);Text("Datenschutz").tag(3)}.pickerStyle(.segmented)
            Form{
                if model.settingsTab==0{Section("Bedienung"){Toggle("Zwischenablageverlauf pausieren",isOn:binding(\.paused));Toggle("Nach Anmeldung starten",isOn:Binding(get:{SMAppService.mainApp.status == .enabled},set:{enabled in do{if enabled{try SMAppService.mainApp.register()}else{try SMAppService.mainApp.unregister()};model.save()}catch{model.status="Autostart in den Systemeinstellungen prüfen"}}));HStack{Text("Suche öffnen");Spacer();Button(model.history.preferences.searchLabel){recordSearch=true}};HStack{Text("Verlauf öffnen");Spacer();Button(model.history.preferences.historyLabel){recordHistory=true}}};Section("Suche"){Text("Apps, Dateien und Ordner sowie die wichtigsten Systemeinstellungen.");Text("Die Dateisuche verwendet den lokalen macOS-Index.").font(.caption).foregroundStyle(.secondary)}}
                if model.settingsTab==1{Section("Wie lange soll dein Verlauf bleiben?"){Picker("Texte behalten",selection:binding(\.textDays)){Text("1 Tag").tag(1);Text("1 Woche").tag(7);Text("2 Wochen").tag(14);Text("1 Monat").tag(30);Text("3 Monate").tag(90);Text("1 Jahr").tag(365)};Picker("Bilder und Dateien behalten",selection:binding(\.fileDays)){Text("1 Tag").tag(1);Text("1 Woche").tag(7);Text("2 Wochen").tag(14);Text("1 Monat").tag(30);Text("3 Monate").tag(90);Text("1 Jahr").tag(365)};Text("Angeheftete Einträge bleiben erhalten.").font(.caption).foregroundStyle(.secondary)};Section("Speicher"){Picker("Platz für den Verlauf",selection:binding(\.budgetMB)){Text("512 MB").tag(512);Text("1 GB").tag(1024);Text("2 GB · empfohlen").tag(2048);Text("5 GB").tag(5120);Text("10 GB").tag(10240)};Text(ByteCountFormatter.string(fromByteCount:model.history.allocated(),countStyle:.file)+" verwendet").font(.caption).foregroundStyle(.secondary);DisclosureGroup("Weitere Schutzgrenzen",isExpanded:$advanced){Picker("Freihalten auf dem Laufwerk",selection:binding(\.reserveMB)){Text("1 GB").tag(1024);Text("5 GB · empfohlen").tag(5120);Text("10 GB").tag(10240)};Text("Maximal 32 MB pro geräteübergreifender Kopie. Größere Kopien werden in dieser Fassung nicht gesichert.").font(.caption).foregroundStyle(.secondary)}}}
                if model.settingsTab==2{DevicePairingView(model:model)}
                if model.settingsTab==3{Section("Von diesen Apps nichts speichern"){ForEach(model.history.preferences.excluded,id:\.self){id in HStack{Text(NSWorkspace.shared.urlForApplication(withBundleIdentifier:id)?.deletingPathExtension().lastPathComponent ?? id);Spacer();Button("Entfernen"){model.history.preferences.excluded.removeAll{$0==id};model.save()}}};Button("App ausschließen …"){let picker=NSOpenPanel();picker.allowedContentTypes=[.applicationBundle];picker.directoryURL=URL(fileURLWithPath:"/Applications");if picker.runModal() == .OK,let url=picker.url,let bundle=Bundle(url:url)?.bundleIdentifier{if !model.history.preferences.excluded.contains(bundle){model.history.preferences.excluded.append(bundle);model.save()}}}};Section("Verlauf löschen"){Button("Nicht angehefteten Verlauf löschen …",role:.destructive){clearUnpinned=true};Button("Gesamten Verlauf löschen …",role:.destructive){clearAll=true};Text("Originaldateien bleiben erhalten. Lokale Verlaufsdaten sind nicht zusätzlich verschlüsselt.").font(.caption).foregroundStyle(.secondary)}}
            }.formStyle(.grouped)
        }.sheet(isPresented:$recordSearch){ShortcutRecorder(model:model,history:false)}.sheet(isPresented:$recordHistory){ShortcutRecorder(model:model,history:true)}
        .confirmationDialog("Verlauf löschen?",isPresented:$clearUnpinned){Button("Löschen",role:.destructive){for entry in model.history.entries where !entry.pinned{model.history.remove(entry)};model.save();model.changed()}}message:{Text("Angeheftete Einträge und Originaldateien bleiben erhalten.")}.confirmationDialog("Gesamten Verlauf löschen?",isPresented:$clearAll){Button("Alles löschen",role:.destructive){for entry in model.history.entries{model.history.remove(entry)};NSPasteboard.general.clearContents();model.save();model.changed()}}message:{Text("Auch angeheftete Sicherungen werden gelöscht. Originaldateien bleiben erhalten.")}
    }
}
final class LauncherPanel:NSPanel {override var canBecomeKey:Bool{true};override var canBecomeMain:Bool{true}}
@MainActor final class AppDelegate:NSObject,NSApplicationDelegate {
    let model:AppModel;override init(){model=AppModel();super.init()};init(model:AppModel){self.model=model;super.init()};var item:NSStatusItem!;var hotKey:EventHotKeyRef?;var handler:EventHandlerRef?;var keyMonitor:Any?;var observer:NSObjectProtocol?
    func applicationDidFinishLaunching(_ notification:Notification){
        NSApp.setActivationPolicy(.accessory)
        let panel=LauncherPanel(contentRect:NSRect(x:0,y:0,width:740,height:590),styleMask:[.borderless],backing:.buffered,defer:false);panel.isFloatingPanel=true;panel.level = .floating;panel.isOpaque=false;panel.backgroundColor = .clear;panel.hasShadow=true;panel.collectionBehavior=[.canJoinAllSpaces,.fullScreenAuxiliary];panel.hidesOnDeactivate=false
        let material=NSVisualEffectView();material.material = .hudWindow;material.blendingMode = .behindWindow;material.state = .active;material.wantsLayer=true;material.layer?.cornerRadius=18;material.layer?.masksToBounds=true;let host=NSHostingView(rootView:LauncherView(model:model).preferredColorScheme(.dark).tint(.blue).accentColor(.blue));host.translatesAutoresizingMaskIntoConstraints=false;material.addSubview(host);NSLayoutConstraint.activate([host.leadingAnchor.constraint(equalTo:material.leadingAnchor),host.trailingAnchor.constraint(equalTo:material.trailingAnchor),host.topAnchor.constraint(equalTo:material.topAnchor),host.bottomAnchor.constraint(equalTo:material.bottomAnchor)]);panel.contentView=material;model.panel=panel
        item=NSStatusBar.system.statusItem(withLength:NSStatusItem.squareLength);item.button?.image=NSImage(systemSymbolName:"magnifyingglass",accessibilityDescription:"Kopra");let menu=NSMenu();menu.addItem(withTitle:"Suche öffnen",action:#selector(openSearch),keyEquivalent:"");menu.addItem(withTitle:"Zwischenablageverlauf",action:#selector(openHistory),keyEquivalent:"");menu.addItem(withTitle:"Einstellungen",action:#selector(openSettings),keyEquivalent:"");menu.addItem(.separator());menu.addItem(withTitle:"Beenden",action:#selector(quit),keyEquivalent:"");for entry in menu.items{entry.target=self};item.menu=menu
        model.shortcutService=MacShortcuts(model)
        keyMonitor=NSEvent.addLocalMonitorForEvents(matching:.keyDown){[weak self] event in guard let self else{return event};return self.handleKey(event)}
        observer=NotificationCenter.default.addObserver(forName:NSWindow.didResignKeyNotification,object:panel,queue:.main){[weak self] _ in Task{@MainActor in if self?.model.settings != true{self?.model.hide()}}}

        model.start()
        if CommandLine.arguments.contains("--show"){model.open()}
    }
    func handleKey(_ event:NSEvent)->NSEvent? {
        guard let panel=model.panel,panel.isVisible,(panel.isKeyWindow || event.windowNumber==panel.windowNumber) else{return event}
        if model.recordingShortcut{return event};if model.shortcutService?.matches(event,history:false)==true{model.toggle();return nil};if model.shortcutService?.matches(event,history:true)==true{model.toggle(history:true);return nil}
        if event.keyCode==53{model.hide();return nil}
        if !model.settings{if event.keyCode==36 || event.keyCode==76{if !event.isARepeat{if event.modifierFlags.contains(.command),model.historyMode,model.rows.indices.contains(model.selected),let entry=model.rows[model.selected].entry{model.restore(entry,paste:true)}else{model.execute()}};return nil};if event.keyCode==125{model.move(1);return nil};if event.keyCode==126{model.move(-1);return nil}}
        return event
    }
    func application(_ application:NSApplication,open urls:[URL]){guard let url=urls.first,url.scheme=="kopra" else{return};model.open();model.settings=true;model.settingsTab=2;model.joinQr(url.absoluteString)}
    @objc func openSearch(){model.open()};@objc func openHistory(){model.open(true)};@objc func openSettings(){model.open();model.settings=true};@objc func quit(){NSApp.terminate(nil)}
    func applicationWillTerminate(_ notification:Notification){model.shortcutService?.dispose();try? model.history.save()}
}

@main struct Main {
    @MainActor static func main() {
if CommandLine.arguments.contains("--ui-self-test"){do{try uiSelfTest()}catch{fputs("UI_SELF_TEST_FAILED\n",stderr);exit(1)}}else if CommandLine.arguments.contains("--self-test"),let i=CommandLine.arguments.firstIndex(of:"--self-test"),CommandLine.arguments.count>i+1 { do{try selfTest(CommandLine.arguments[i+1])}catch{fputs("SELF_TEST_FAILED\n",stderr);exit(1)}
}else if CommandLine.arguments.contains("--render-preview"),let i=CommandLine.arguments.firstIndex(of:"--render-preview"),CommandLine.arguments.count>i+1 {do{try renderPreview(CommandLine.arguments[i+1])}catch{fputs("PREVIEW_FAILED\n",stderr);exit(1)}
}else if CommandLine.arguments.contains("--lan-self-test"),let i=CommandLine.arguments.firstIndex(of:"--lan-self-test"),CommandLine.arguments.count>i+1 {do{try lanSelfTest(CommandLine.arguments[i+1])}catch{fputs("LAN_TEST_FAILED\n",stderr);exit(1)}
}else{
    let app=NSApplication.shared;let delegate=AppDelegate();app.delegate=delegate;withExtendedLifetime(delegate){app.run()}
}

    }
}
