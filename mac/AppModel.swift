import AppKit
import SwiftUI
import ApplicationServices

struct SearchResult: Identifiable { var id: String; var title: String; var subtitle: String; var url: URL?; var symbol: String; var entry: HistoryEntry? }
@MainActor final class AppModel: ObservableObject {
    let history: MacHistory
    @Published var query = "" { didSet { search() } }; @Published var historyMode = false; @Published var settings = false; @Published var rows=[SearchResult](); @Published var selected=0; @Published var status="Nur auf diesem Mac"; @Published var revision=0; @Published var pairingText=""; @Published var paired=false
    @Published var settingsTab=0;@Published var showQrCamera=false;@Published var pairingStatus="";@Published var pairingDigits="";@Published var pairingCanConfirm=false;@Published var pairingInProgress=false;var pendingPair:PairClient?;var pairingTask:Task<Void,Never>?;var pairingApproved=false
    @Published var pairingViaCode=false;@Published var pairingOtherConfirmed=false
    var focusGuard:Timer?
    var shortcutService:MacShortcuts?;@Published var recordingShortcut=false
    var panel: NSPanel?; var previousApp: NSRunningApplication?; private var pasteboardCount=NSPasteboard.general.changeCount; private var ignoredCount = -1; private var syncing=false; private var capturing=false; private var timer:Timer?; private var syncTimer:Timer?;private var pruneTimer:Timer?; private var lastPrune=Date();private var lastDiscovery=Date.distantPast;private var lastToggle=Date.distantPast; private var searchGeneration=0
    private var applications=[URL](); private var recent=[URL](); private var skipped=Set<String>()
    init(history:MacHistory=MacHistory(),loadPairing:Bool=true) { self.history=history; paired=history.preferences.sharingEnabled && loadPairing && PairingVault.load() != nil }
    func start() {
        DispatchQueue.global(qos:.utility).async { let fm=FileManager.default;var apps=[URL]();for path in ["/Applications","/System/Applications",NSHomeDirectory()+"/Applications"] { if let walker=fm.enumerator(at:URL(fileURLWithPath:path),includingPropertiesForKeys:nil,options:[.skipsHiddenFiles]) { for case let url as URL in walker { if url.pathExtension=="app" { apps.append(url);walker.skipDescendants() } } } };Task{@MainActor in self.applications=apps;self.search()} }
        pruneTimer=Timer.scheduledTimer(withTimeInterval:900,repeats:true){[weak self] _ in Task{@MainActor in self?.history.prune();self?.changed()} }
        timer=Timer.scheduledTimer(withTimeInterval:0.7,repeats:true){[weak self] _ in Task{@MainActor in self?.capture()} }
        syncTimer=Timer.scheduledTimer(withTimeInterval:2,repeats:true){[weak self] _ in Task{@MainActor in await self?.sync()} }
        NSWorkspace.shared.notificationCenter.addObserver(forName:NSWorkspace.didActivateApplicationNotification,object:nil,queue:.main){[weak self] note in guard let app=note.userInfo?[NSWorkspace.applicationUserInfoKey] as? NSRunningApplication, app.bundleIdentifier != Bundle.main.bundleIdentifier, let url=app.bundleURL else{return};Task{@MainActor in guard let self else{return};self.recent.removeAll{$0==url};self.recent.insert(url,at:0);self.recent=Array(self.recent.prefix(12))} }
    }
    func open(_ clipboard:Bool=false) { previousApp=NSWorkspace.shared.frontmostApplication;historyMode=clipboard;settings=false;query="";selected=0;search();if let panel,let screen=NSScreen.main { let area=screen.visibleFrame;panel.setFrame(NSRect(x:area.midX-370,y:area.maxY-(clipboard ? 590:460)-100,width:740,height:clipboard ? 590:460),display:true);NSApp.activate(ignoringOtherApps:true);panel.makeKeyAndOrderFront(nil);beginFocusOwnership() } }
    func toggle(history:Bool=false){guard Date().timeIntervalSince(lastToggle)>0.2 else{return};lastToggle=Date();if panel?.isVisible==true && historyMode==history && !settings{hide()}else{open(history)}}
    func hide(){focusGuard?.invalidate();focusGuard=nil;panel?.orderOut(nil)}
    func appTitle(_ url:URL)->String {let title=FileManager.default.displayName(atPath:url.path);return title.hasSuffix(".app") ? String(title.dropLast(4)):title}
    func appKeywords(_ url:URL)->String {
        let aliases=["Calculator":"Rechner Taschenrechner", "System Settings":"Systemeinstellungen Einstellungen", "Notes":"Notizen", "Calendar":"Kalender", "Contacts":"Kontakte", "Activity Monitor":"Aktivitätsanzeige Taskmanager Prozesse", "Preview":"Vorschau PDF", "Photos":"Fotos Bilder", "Music":"Musik", "Reminders":"Erinnerungen", "Voice Memos":"Sprachmemos", "Maps":"Karten", "Find My":"Wo ist", "Passwords":"Passwörter", "Safari":"Browser Internet", "Terminal":"Konsole"]
        return url.deletingPathExtension().lastPathComponent+" "+(aliases[url.deletingPathExtension().lastPathComponent] ?? "")
    }
    func search() {
        searchGeneration+=1;let generation=searchGeneration;selected=0
        if historyMode { rows=history.entries.filter{query.isEmpty||$0.title.localizedCaseInsensitiveContains(query)||$0.text.localizedCaseInsensitiveContains(query)}.sorted{a,b in a.pinned != b.pinned ? a.pinned : a.created>b.created}.prefix(200).map{SearchResult(id:$0.id,title:$0.title,subtitle:($0.origin.isEmpty ? "": "Von \($0.origin) · ")+ByteCountFormatter.string(fromByteCount:Int64($0.bytes),countStyle:.file),url:$0.paths.first.map{URL(fileURLWithPath:$0)},symbol:$0.kind=="text" ? "text.alignleft":"doc",entry:$0)};return }
        let term=query;var result=[SearchResult]();if term.isEmpty||"Verlauf Zwischenablage".localizedCaseInsensitiveContains(term){result.append(SearchResult(id:"history",title:"Verlauf",subtitle:"Texte, Bilder, Dateien und Ordner",symbol:"clipboard",entry:nil))}
        let apps=term.isEmpty ? recent : applications.filter{self.appTitle($0).localizedCaseInsensitiveContains(term)||self.appKeywords($0).localizedCaseInsensitiveContains(term)}.sorted{a,b in a.deletingPathExtension().lastPathComponent.localizedCaseInsensitiveCompare(term) == .orderedSame}
        result += apps.prefix(12).map{SearchResult(id:$0.path,title:self.appTitle($0),subtitle:term.isEmpty ? "Zuletzt verwendet":"App",url:$0,symbol:"app",entry:nil)}
        let settingsList:[(String,String)]=[("Systemeinstellungen",""),("WLAN","com.apple.wifi-settings-extension"),("Bluetooth","com.apple.BluetoothSettings"),("Ton","com.apple.Sound-Settings.extension"),("Displays","com.apple.Displays-Settings.extension"),("Tastatur","com.apple.Keyboard-Settings.extension"),("Datenschutz und Sicherheit","com.apple.settings.PrivacySecurity.extension"),("Mitteilungen","com.apple.Notifications-Settings.extension"),("Allgemein","com.apple.systempreferences.GeneralSettings"),("Bedienungshilfen","com.apple.Accessibility-Settings.extension"),("Batterie","com.apple.Battery-Settings.extension"),("Schreibtisch und Dock","com.apple.Desktop-Settings.extension"),("Maus","com.apple.Mouse-Settings.extension"),("Trackpad","com.apple.Trackpad-Settings.extension"),("Netzwerk","com.apple.Network-Settings.extension"),("Speicher","com.apple.settings.Storage"),("Datum und Uhrzeit","com.apple.Date-Time-Settings.extension"),("Sprache und Region","com.apple.Localization-Settings.extension"),("Benutzer und Gruppen","com.apple.Users-Groups-Settings.extension"),("Sperrbildschirm","com.apple.Lock-Screen-Settings.extension"),("Fokus","com.apple.Focus-Settings.extension")]
        if !term.isEmpty {result += settingsList.filter{$0.0.localizedCaseInsensitiveContains(term)}.map{SearchResult(id:"setting:"+$0.1,title:$0.0,subtitle:"Systemeinstellung",url:URL(string:"x-apple.systempreferences:"+$0.1),symbol:"gearshape",entry:nil)}}
        rows=Array(result.prefix(20));guard term.count>=2 else{return};let base=result
        DispatchQueue.global(qos:.userInitiated).async {let task=Process();task.executableURL=URL(fileURLWithPath:"/usr/bin/mdfind");task.arguments=["-onlyin",NSHomeDirectory(),"-name",term];let pipe=Pipe();task.standardOutput=pipe;task.standardError=FileHandle.nullDevice;do{try task.run();let data=pipe.fileHandleForReading.readDataToEndOfFile();task.waitUntilExit();let paths=String(data:data,encoding:.utf8)?.split(separator:"\n").prefix(20).map(String.init) ?? [];Task{@MainActor in guard self.searchGeneration==generation else{return};var seen=Set(base.map{$0.id});self.rows=base+paths.filter{seen.insert($0).inserted}.map{let url=URL(fileURLWithPath:$0);return SearchResult(id:$0,title:url.lastPathComponent,subtitle:url.deletingLastPathComponent().path,url:url,symbol:"doc",entry:nil)}}}catch{} }
    }
    func move(_ amount:Int){guard !rows.isEmpty else{return};selected=max(0,min(rows.count-1,selected+amount))}
    var historyOpener:(([URL])->Bool)?
    func openHistory(_ entry:HistoryEntry){
        let urls=entry.paths.map{URL(fileURLWithPath:$0)};guard !urls.isEmpty,urls.allSatisfy({FileManager.default.fileExists(atPath:$0.path)}) else{status="Gespeicherter Inhalt fehlt";return}
        let opened:Bool
        if let historyOpener{opened=historyOpener(urls)}else if urls.count>1{NSWorkspace.shared.activateFileViewerSelecting(urls);opened=true}else{opened=NSWorkspace.shared.open(urls[0])}
        if opened{hide()}else{status="Datei konnte nicht geöffnet werden"}
    }
    func execute(){
        guard rows.indices.contains(selected) else{return};let row=rows[selected]
        if row.id=="history"{historyMode=true;query="";selected=0;search();panel?.makeKeyAndOrderFront(nil);status="Verlauf geöffnet";health();return}
        if let entry=row.entry{if entry.kind=="files" || entry.kind=="image"{openHistory(entry)}else{restore(entry,paste:true)};return}
        guard let url=row.url else{status="Kein Ziel ausgewählt";return}
        if url.isFileURL && url.pathExtension=="app"{
            hide();let configuration=NSWorkspace.OpenConfiguration();configuration.activates=true
            NSWorkspace.shared.openApplication(at:url,configuration:configuration){_,error in Task{@MainActor in if error==nil{self.hide()}else{self.open();self.status="Die App konnte nicht geöffnet werden"};self.health()}}
        }else if NSWorkspace.shared.open(url){hide()}else{status="Dieses Ergebnis konnte nicht geöffnet werden"}
    }
    func restore(_ entry:HistoryEntry,paste:Bool){if entry.kind != "text" && entry.paths.contains(where:{!FileManager.default.fileExists(atPath:$0)}){status="Gespeicherter Inhalt fehlt";return};let board=NSPasteboard.general;board.clearContents();if entry.kind=="text"{board.setString(entry.text,forType:.string)}else if entry.kind=="image",let path=entry.paths.first,let image=NSImage(contentsOfFile:path){board.writeObjects([image])}else{board.writeObjects(entry.paths.map{NSURL(fileURLWithPath:$0)})};board.setString("1",forType:NSPasteboard.PasteboardType("org.nspasteboard.TransientType"));ignoredCount=board.changeCount;pasteboardCount=board.changeCount;if paste{hide();previousApp?.activate(options:[]);if AXIsProcessTrusted(){DispatchQueue.main.asyncAfter(deadline:.now()+0.15){let down=CGEvent(keyboardEventSource:nil,virtualKey:9,keyDown:true);down?.flags = .maskCommand;let up=CGEvent(keyboardEventSource:nil,virtualKey:9,keyDown:false);up?.flags = .maskCommand;down?.post(tap:.cghidEventTap);up?.post(tap:.cghidEventTap)}}else{status="Kopiert – mit ⌘V einfügen"}}}
    func capture(){let board=NSPasteboard.general;guard !history.preferences.paused,!capturing,board.changeCount != pasteboardCount else{return};let count=board.changeCount;pasteboardCount=count;guard count != ignoredCount else{return};let types=board.types?.map{$0.rawValue} ?? [];guard !types.contains(where:{$0=="org.nspasteboard.ConcealedType"||$0=="org.nspasteboard.TransientType"||$0=="org.nspasteboard.AutoGeneratedType"}) else{return};if let app=NSWorkspace.shared.frontmostApplication?.bundleIdentifier,history.preferences.excluded.contains(app){return}
        var clip:SharedClip?
        do{if let urls=board.readObjects(forClasses:[NSURL.self],options:[.urlReadingFileURLsOnly:true]) as? [URL],!urls.isEmpty{clip=try SharedProtocol.pack(urls:urls,kind:"files",title:urls.count==1 ? urls[0].lastPathComponent:"\(urls.count) kopierte Objekte")}
            else if let png=board.data(forType:.png){guard png.count<=maximumContent else{throw SharedError.tooLarge};clip=SharedClip(id:uniqueID(),kind:"image",title:"Kopiertes Bild",text:"",created:milliseconds(),files:[SharedFile(path:"Bild.png",directory:false,data:png.base64EncodedString())])}
            else if let tiff=board.data(forType:.tiff),let bitmap=NSBitmapImageRep(data:tiff),let png=bitmap.representation(using:.png,properties:[:]){guard png.count<=maximumContent else{throw SharedError.tooLarge};clip=SharedClip(id:uniqueID(),kind:"image",title:"Kopiertes Bild",text:"",created:milliseconds(),files:[SharedFile(path:"Bild.png",directory:false,data:png.base64EncodedString())])}
            else if let text=board.string(forType:.string),!text.isEmpty{clip=SharedClip(id:uniqueID(),kind:"text",title:String(text.replacingOccurrences(of:"\n",with:" ").prefix(120)),text:text,created:milliseconds(),files:[])}
            if let clip {try history.insert(clip,origin:"");changed()};if Date().timeIntervalSince(lastPrune)>900{history.prune();lastPrune=Date()}
        }catch{status="Nicht gesichert: Speichergrenze erreicht oder Inhalt nicht übertragbar."}
    }
    func health(){let values:[String:Any]=["paired":paired,"entries":history.entries.count,"remoteEntries":history.entries.filter{!$0.origin.isEmpty}.count,"acknowledgedSends":history.preferences.sent.count,"acknowledgedReceives":history.preferences.received.count,"status":status,"updated":milliseconds()];if let data=try? JSONSerialization.data(withJSONObject:values){try? data.write(to:history.root.appendingPathComponent("health.json"),options:.atomic)}}
    func changed(){revision+=1;health();if historyMode{let current=rows.indices.contains(selected) ? rows[selected].id:nil;search();if let current,let index=rows.firstIndex(where:{$0.id==current}){selected=index}}}
    func save(){try? history.save();revision+=1}
    func sync() async {
        guard paired,history.preferences.sharingEnabled,!syncing,!history.preferences.paused,let pairing=PairingVault.load() else{return};syncing=true;defer{syncing=false}
        var outgoing:SharedClip?
        for entry in history.entries.filter({$0.origin.isEmpty && $0.created>=history.preferences.since}).sorted(by:{$0.created<$1.created}) where !history.preferences.sent.contains(entry.wireID) && !skipped.contains(entry.wireID){do{outgoing=try history.export(entry);break}catch{skipped.insert(entry.wireID);status="Eine Kopie ist zu groß zum Teilen (max. 32 MB)."}}
        let packet=SyncPacket(version:1,device:history.preferences.device,id:uniqueID(),issued:milliseconds(),kind:"exchange",ack:history.preferences.lastWindowsAck,entry:outgoing);let before=NSPasteboard.general.changeCount
        do{let response=try await Task.detached{try SharedProtocol.exchange(packet,pairing:pairing)}.value;guard paired,history.preferences.sharingEnabled else{return};if !response.ack.isEmpty{history.preferences.sent.append(response.ack);history.preferences.sent=Array(history.preferences.sent.suffix(4096))}
            if let clip=response.entry{if !history.preferences.received.contains(clip.id){let newest=history.entries.filter{$0.origin.isEmpty}.map{$0.created}.max() ?? 0;let entry=try history.insert(clip,origin:"Windows");history.preferences.received.append(clip.id);history.preferences.received=Array(history.preferences.received.suffix(4096));if history.preferences.sharedPaste && abs(milliseconds()-clip.created)<60_000 && clip.created>=newest && before==NSPasteboard.general.changeCount{restore(entry,paste:false)};changed()};history.preferences.lastWindowsAck=clip.id};try history.save();status="Mit Windows verbunden"
        }catch{status="Windows nicht erreichbar – lokale Kopien bleiben erhalten";if Date().timeIntervalSince(lastDiscovery)>20{lastDiscovery=Date();let device=history.preferences.device;if let updated=try? await Task.detached(operation:{try SharedProtocol.discover(pairing,device:device)}).value,updated.host != pairing.host{try? PairingVault.save(updated)}}};health()
    }
}
