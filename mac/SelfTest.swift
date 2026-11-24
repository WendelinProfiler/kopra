import Foundation
import CryptoKit
import AppKit
import SwiftUI
import Vision

@MainActor func uiSelfTest() throws {
    let app=NSApplication.shared;app.setActivationPolicy(.accessory);var failure:Error?
    func exercise() async throws {
    let root=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-ui-tests-"+uniqueID());let history=MacHistory(root:root);history.preferences.reserveMB=0;defer{try? FileManager.default.removeItem(at:root)}
    TextEditing.installMenu()
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"text",title:"Test",text:"Nur künstliche Testdaten",created:milliseconds(),files:[]),origin:"")
    let model=AppModel(history:history,loadPairing:false);let delegate=AppDelegate(model:model)
    let panel=LauncherPanel(contentRect:NSRect(x:-2000,y:-2000,width:740,height:500),styleMask:[.borderless],backing:.buffered,defer:false);model.panel=panel;panel.contentView=NSHostingView(rootView:LauncherView(model:model));model.search();panel.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true);try await Task.sleep(nanoseconds:100_000_000)
    print("UI_PRE_ENTER",model.rows.first?.id=="history",panel.isKeyWindow,panel.isVisible);guard model.rows.first?.id=="history",let enter=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:[],timestamp:0,windowNumber:panel.windowNumber,context:nil,characters:"\r",charactersIgnoringModifiers:"\r",isARepeat:false,keyCode:36) else{throw SharedError.invalid}
    let editField=NSTextField(frame:NSRect(x:20,y:20,width:320,height:30));editField.stringValue="Synthetic editable query";editField.isEditable=true;editField.isSelectable=true;panel.contentView?.addSubview(editField);let editingFocused=panel.makeFirstResponder(editField);print("UI_EDIT_FOCUS",editingFocused,editField.currentEditor() != nil,String(describing:type(of:panel.firstResponder)))
    guard let editor=editField.currentEditor(),let selectAll=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:[.command],timestamp:0,windowNumber:panel.windowNumber,context:nil,characters:"a",charactersIgnoringModifiers:"a",isARepeat:false,keyCode:0),panel.performKeyEquivalent(with:selectAll),editor.selectedRange.length==editField.stringValue.utf16.count else{print("UI_EDIT_SELECT_ALL_FAILED");throw SharedError.invalid}
    let editActions=Set((NSApp.mainMenu?.items.flatMap{$0.submenu?.items ?? []} ?? []).compactMap{$0.action.map{NSStringFromSelector($0)}});guard ["undo:","redo:","cut:","copy:","paste:","selectAll:"].allSatisfy({editActions.contains($0)}) else{print("UI_EDIT_MENU_FAILED");throw SharedError.invalid}
    editField.removeFromSuperview();panel.makeFirstResponder(panel.contentView);print("PASS NATIVE EDITING: COMMAND_A_SELECTS_ALL, STANDARD_EDIT_COMMANDS_REGISTERED")
    let handled=delegate.handleKey(enter)==nil;print("UI_POST_ENTER",handled,model.historyMode,panel.isVisible);guard handled,model.historyMode,panel.isVisible else{throw SharedError.invalid}
    let file=try history.insert(SharedClip(id:uniqueID(),kind:"files",title:"Notiz.txt",text:"",created:milliseconds(),files:[SharedFile(path:"Notiz.txt",directory:false,data:Data("Synthetischer Dateitest".utf8).base64EncodedString())]),origin:"")
    model.search();guard let fileIndex=model.rows.firstIndex(where:{$0.entry?.id==file.id}) else{throw SharedError.invalid};model.selected=fileIndex
    var openedPath="";model.historyOpener={urls in openedPath=urls.first?.path ?? "";return false};_ = delegate.handleKey(enter);guard openedPath==file.paths[0],panel.isVisible else{throw SharedError.invalid}
    model.historyOpener={urls in openedPath=urls.first?.path ?? "";return true};_ = delegate.handleKey(enter);guard openedPath==file.paths[0],!panel.isVisible else{throw SharedError.invalid};print("PASS NATIVE FILE ENTER: REAL_SAVED_PATH, FAILED_OPEN_STAYS_VISIBLE, SUCCESS_HIDES")
    panel.makeKeyAndOrderFront(nil)
    guard let escape=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:[],timestamp:0,windowNumber:panel.windowNumber,context:nil,characters:"",charactersIgnoringModifiers:"",isARepeat:false,keyCode:53) else{throw SharedError.invalid}
    _ = delegate.handleKey(escape);guard !panel.isVisible else{throw SharedError.invalid};print("PASS NATIVE UI: ENTER_OPENS_HISTORY_AND_STAYS_VISIBLE, ESCAPE_HIDES")
    if let finder=NSWorkspace.shared.runningApplications.first(where:{$0.bundleIdentifier=="com.apple.finder"}),let screen=NSScreen.main {
        let area=screen.visibleFrame;panel.setFrame(NSRect(x:area.midX-370,y:area.midY-230,width:740,height:460),display:true);panel.makeKeyAndOrderFront(nil);model.beginFocusOwnership()
        defer{model.hide()}
        for _ in 0..<3{finder.activate(options:[]);try await Task.sleep(nanoseconds:400_000_000);guard NSApp.isActive,panel.isKeyWindow else{print("FOCUS_FAILED_EXTERNAL_RECOVERY",NSApp.isActive,panel.isKeyWindow);throw SharedError.invalid}}
        model.previousApp=finder;model.open(true);guard model.previousApp?.processIdentifier==finder.processIdentifier else{print("FOCUS_FAILED_PASTE_TARGET");throw SharedError.invalid}
        let dialog=LauncherPanel(contentRect:NSRect(x:area.midX-120,y:area.midY-40,width:240,height:80),styleMask:[.titled],backing:.buffered,defer:false);let field=NSTextField(string:"Eigener Testdialog");dialog.contentView=field;panel.addChildWindow(dialog,ordered:.above);dialog.makeKeyAndOrderFront(nil);dialog.makeFirstResponder(field);try await Task.sleep(nanoseconds:300_000_000);guard dialog.isKeyWindow else{print("FOCUS_FAILED_OWN_DIALOG");throw SharedError.invalid};panel.removeChildWindow(dialog);dialog.orderOut(nil);model.ensureFocusOwnership();guard panel.isKeyWindow else{print("FOCUS_FAILED_DIALOG_RETURN");throw SharedError.invalid}
        let picker=NSOpenPanel();picker.beginSheetModal(for:panel){_ in};try await Task.sleep(nanoseconds:300_000_000);model.ensureFocusOwnership();guard panel.attachedSheet != nil else{print("FOCUS_FAILED_FILE_PICKER");throw SharedError.invalid};picker.cancel(nil);try await Task.sleep(nanoseconds:200_000_000);model.ensureFocusOwnership();guard panel.isKeyWindow else{print("FOCUS_FAILED_DIALOG_RETURN");throw SharedError.invalid}
        model.hide();finder.activate(options:[]);try await Task.sleep(nanoseconds:300_000_000);guard !panel.isVisible,NSWorkspace.shared.frontmostApplication?.bundleIdentifier=="com.apple.finder" else{print("FOCUS_FAILED_HIDE_RELEASE");throw SharedError.invalid}
        print("PASS FOCUS: THREE_EXTERNAL_LOSSES, OWNED_DIALOG, FILE_PICKER, RETURN_AFTER_DIALOG, RELEASE_WHEN_HIDDEN, PASTE_TARGET_PRESERVED")
    }else{throw SharedError.unavailable}
    }
    Task { @MainActor in
        do{try await exercise()}catch{failure=error}
        app.stop(nil)
        if let event=NSEvent.otherEvent(with:.applicationDefined,location:.zero,modifierFlags:[],timestamp:0,windowNumber:0,context:nil,subtype:0,data1:0,data2:0){app.postEvent(event,atStart:true)}
    }
    app.run();if let failure{throw failure}
}

@MainActor func renderPreview(_ folder:String) throws {
    _ = NSApplication.shared;NSApp.setActivationPolicy(.accessory);NSApp.appearance=NSAppearance(named:.darkAqua)
    let root=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-preview-"+uniqueID());let history=MacHistory(root:root);history.preferences.reserveMB=0;defer{try? FileManager.default.removeItem(at:root)}
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"text",title:"A note to keep",text:"A note to keep for later.",created:milliseconds()-3000,files:[]),origin:"Windows")
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"text",title:"https://example.com/design",text:"https://example.com/design",created:milliseconds()-2000,files:[]),origin:"")
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"files",title:"Project notes",text:"",created:milliseconds()-1000,files:[SharedFile(path:"Project notes",directory:true,data:""),SharedFile(path:"Project notes/Notes.txt",directory:false,data:Data("Ideas for the next project".utf8).base64EncodedString())]),origin:"")
    let image=NSImage(size:NSSize(width:640,height:400));image.lockFocus();NSGradient(starting:NSColor(calibratedRed:0.27,green:0.68,blue:0.88,alpha:1),ending:NSColor(calibratedRed:0.35,green:0.31,blue:0.73,alpha:1))!.draw(in:NSRect(x:0,y:0,width:640,height:400),angle:45);NSColor(calibratedRed:0.93,green:0.79,blue:0.61,alpha:1).setFill();NSBezierPath(ovalIn:NSRect(x:420,y:250,width:90,height:90)).fill();let mountains=NSBezierPath();mountains.move(to:NSPoint(x:0,y:0));mountains.line(to:NSPoint(x:170,y:280));mountains.line(to:NSPoint(x:340,y:90));mountains.line(to:NSPoint(x:440,y:190));mountains.line(to:NSPoint(x:640,y:0));mountains.close();NSColor(calibratedRed:0.15,green:0.25,blue:0.45,alpha:1).setFill();mountains.fill();image.unlockFocus()
    guard let tiff=image.tiffRepresentation,let bitmap=NSBitmapImageRep(data:tiff),let png=bitmap.representation(using:.png,properties:[:]) else{throw SharedError.invalid}
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"image",title:"Landscape.png",text:"",created:milliseconds(),files:[SharedFile(path:"Landscape.png",directory:false,data:png.base64EncodedString())]),origin:"")
    let model=AppModel(history:history,loadPairing:false);model.status="Mit Windows verbunden";model.historyMode=true;model.search()
    let area=NSScreen.main?.visibleFrame ?? NSRect(x:0,y:0,width:1280,height:900)
    let panel=NSWindow(contentRect:NSRect(x:area.midX-370,y:area.midY-295,width:740,height:590),styleMask:[.borderless],backing:.buffered,defer:false);panel.isOpaque=false;panel.backgroundColor = .clear
    let material=NSVisualEffectView();material.material = .hudWindow;material.blendingMode = .behindWindow;material.state = .active;material.wantsLayer=true;material.layer?.cornerRadius=18;material.layer?.masksToBounds=true
    let host=NSHostingView(rootView:LauncherView(model:model).preferredColorScheme(.dark).tint(.blue).accentColor(.blue));host.translatesAutoresizingMaskIntoConstraints=false;material.addSubview(host);NSLayoutConstraint.activate([host.leadingAnchor.constraint(equalTo:material.leadingAnchor),host.trailingAnchor.constraint(equalTo:material.trailingAnchor),host.topAnchor.constraint(equalTo:material.topAnchor),host.bottomAnchor.constraint(equalTo:material.bottomAnchor)]);panel.contentView=material;panel.orderFrontRegardless();RunLoop.current.run(until:Date().addingTimeInterval(0.8));host.layoutSubtreeIfNeeded()
    try FileManager.default.createDirectory(atPath:folder,withIntermediateDirectories:true)
    let output=folder+"/mac-history.png";let capture=Process();capture.executableURL=URL(fileURLWithPath:"/usr/sbin/screencapture");capture.arguments=["-x","-o","-l",String(panel.windowNumber),output];try capture.run();capture.waitUntilExit();panel.orderOut(nil)
    guard capture.terminationStatus==0,FileManager.default.fileExists(atPath:output) else{throw SharedError.unavailable};print("MAC_PREVIEW_READY")
}
func lanSelfTest(_ pairFile:String) throws {
    let code=try String(contentsOfFile:pairFile,encoding:.utf8).trimmingCharacters(in:.whitespacesAndNewlines);guard code.hasPrefix("IS1."),let bytes=Data(base64Encoded:String(code.dropFirst(4))) else{throw SharedError.invalid};let pair=try JSONDecoder().decode(Pairing.self,from:bytes)
    let root=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-lan-test-"+uniqueID());let history=MacHistory(root:root);history.preferences.reserveMB=0;defer{try? FileManager.default.removeItem(at:root)}
    let text=SharedClip(id:uniqueID(),kind:"text",title:"Mac-Test",text:"Kopra Test Mac → Windows",created:milliseconds(),files:[])
    let image=SharedClip(id:uniqueID(),kind:"image",title:"Testbild",text:"",created:milliseconds(),files:[SharedFile(path:"Bild.png",directory:false,data:"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4GQAAAAASUVORK5CYII=")])
    let folder=SharedClip(id:uniqueID(),kind:"files",title:"Testordner",text:"",created:milliseconds(),files:[SharedFile(path:"Testordner",directory:true,data:""),SharedFile(path:"Testordner/Leer",directory:true,data:""),SharedFile(path:"Testordner/Notiz.txt",directory:false,data:Data("Mac test file".utf8).base64EncodedString())])
    let outgoing=[text,image,folder];var sent=0;var ack="";var received=Set<String>();let deadline=Date().addingTimeInterval(80)
    while Date()<deadline {
        let packet=SyncPacket(version:1,device:history.preferences.device,id:uniqueID(),issued:milliseconds(),kind:"exchange",ack:ack,entry:sent<outgoing.count ? outgoing[sent]:nil);let response=try SharedProtocol.exchange(packet,pairing:pair)
        if sent<outgoing.count,response.ack==outgoing[sent].id{sent+=1}
        if let clip=response.entry{if received.insert(clip.id).inserted{try history.insert(clip,origin:"Windows")};ack=clip.id}
        if received.count==3 && sent==3{let final=SyncPacket(version:1,device:history.preferences.device,id:uniqueID(),issued:milliseconds(),kind:"exchange",ack:ack,entry:nil);_ = try SharedProtocol.exchange(final,pairing:pair);let actual=history.entries.contains{$0.text=="Kopra Test Windows → Mac"} && history.entries.contains{entry in entry.kind=="files" && (try? String(contentsOfFile:entry.paths[0]+"/Notiz.txt",encoding:.utf8))=="Windows test file" && FileManager.default.fileExists(atPath:entry.paths[0]+"/Leer")};guard actual else{throw SharedError.invalid};print("PASS LAN: 3 MAC_TO_WINDOWS, 3 WINDOWS_TO_MAC, ACTUAL_FILES_AND_EMPTY_FOLDERS");return}
        Thread.sleep(forTimeInterval:0.3)
    };throw SharedError.unavailable
}

func selfTest(_ fixture: String) throws {
    let fm=FileManager.default;let root=fm.temporaryDirectory.appendingPathComponent("Kopra-tests-"+uniqueID());try fm.createDirectory(at:root,withIntermediateDirectories:true);defer{try? fm.removeItem(at:root)}
    var count=0;func check(_ value:Bool) throws{count+=1;if !value{throw SharedError.invalid}}
    try check(MacPreferences().language=="en")
    let searchRoot=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-search-tests-"+uniqueID());try FileManager.default.createDirectory(at:searchRoot,withIntermediateDirectories:true);defer{try? FileManager.default.removeItem(at:searchRoot)}
    let searchFile=searchRoot.appendingPathComponent("Übergabe # 100%\nNotiz.txt");try Data("test".utf8).write(to:searchFile)
    let missing=searchRoot.appendingPathComponent("deleted.txt").path
    let framed=Data(([missing,searchFile.path,searchFile.path].joined(separator:"\0")+"\0").utf8)
    try check(FileSearch.urls(framed,limit:1).first?.path==searchFile.path)
    try check(FileSearch.urls(framed).count==1)
    try check(FileSearch.urls(Data("https://example.com\0relative.txt\0".utf8)).isEmpty)
    L.use("en");try check(L.text("Verlauf")=="History");try check(L.text("Keine Ergebnisse")=="No results")
    try check(L.format("Gültig für {0}:{1:D2} · Beide Geräte im selben Netzwerk",2,7).contains("2:07"))
    var localizedPreferences=MacPreferences();localizedPreferences.language="de"
    let savedLanguage=try JSONDecoder().decode(MacPreferences.self,from:JSONEncoder().encode(localizedPreferences));try check(savedLanguage.language=="de")
    L.use("de");try check(L.text("Verlauf")=="Verlauf");try check(L.text("Connected to Windows")=="Mit Windows verbunden");L.use("en")
    let json=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:fixture))) as! [String:String];let key=Data(base64Encoded:json["key"]!)!;let plain=Data(base64Encoded:json["plain"]!)!;let sealed=Data(base64Encoded:json["sealedBox"]!)!;try check(try SharedProtocol.open(sealed,key:key)==plain);try check(try SharedProtocol.open(SharedProtocol.seal(plain,key:key),key:key)==plain)
    let fixtureFolder=URL(fileURLWithPath:fixture).deletingLastPathComponent()
    let qrRequest=VNDetectBarcodesRequest();qrRequest.symbologies=[.qr]
    try VNImageRequestHandler(url:fixtureFolder.appendingPathComponent("qr-test.png"),options:[:]).perform([qrRequest])
    let qrExpected=try String(contentsOf:fixtureFolder.appendingPathComponent("qr-test.txt"),encoding:.utf8)
    try check(qrRequest.results?.first?.payloadStringValue==qrExpected)
    let numeric=try JSONSerialization.jsonObject(with:Data(contentsOf:fixtureFolder.appendingPathComponent("code-fixture.json"))) as! [String:String]
    let numericSalt=Data(base64Encoded:numeric["salt"]!)!,numericA=BigUInt(Data(base64Encoded:numeric["a"]!)!),numericB=BigUInt(Data(base64Encoded:numeric["b"]!)!)
    let (numericKey,numericContext)=try CodeCrypto.secret(code:numeric["code"]!,salt:numericSalt,a:numericA,b:numericB,session:numeric["session"]!,device:numeric["device"]!)
    try check(numericKey.base64EncodedString()==numeric["key"]);try check(numericContext.base64EncodedString()==numeric["context"]);try check(CodeCrypto.proof(numericKey,numericContext,"client").base64EncodedString()==numeric["proof"])
    let wrongCode=numeric["code"]=="000000" ? "999999":"000000"
    let (wrongKey,_)=try CodeCrypto.secret(code:wrongCode,salt:numericSalt,a:numericA,b:numericB,session:numeric["session"]!,device:numeric["device"]!)
    try check(wrongKey != numericKey)
    let pairFile=URL(fileURLWithPath:fixture).deletingLastPathComponent().appendingPathComponent("pair-v2-fixture.json")
    let pair=try JSONSerialization.jsonObject(with:Data(contentsOf:pairFile)) as! [String:String]
    let privateKey=try P256.KeyAgreement.PrivateKey(rawRepresentation:Data(base64Encoded:pair["privateKey"]!)!);let peer=try P256.KeyAgreement.PublicKey(x963Representation:Data(base64Encoded:pair["peerPublic"]!)!);let secret=try privateKey.sharedSecretFromKeyAgreement(with:peer);let token=Data(base64Encoded:pair["token"]!)!;let derived=PairV2.derive(secret,token:token,session:pair["session"]!)
    try check(derived.base64EncodedString()==pair["expected"]);try check(PairV2.derive(secret,token:token,session:pair["session"]!,sync:true).base64EncodedString()==pair["syncExpected"]);try check(PairV2.code(derived)==pair["code"])
    var broken=sealed;broken[14]^=1;do{_ = try SharedProtocol.open(broken,key:key);throw SharedError.invalid}catch is CryptoKitError{count+=1}
    for path in ["../escape","/absolute","C:/drive","a\\b","a/../b","a/","a."]{let clip=SharedClip(id:"test",kind:"files",title:"Test",text:"",created:0,files:[SharedFile(path:path,directory:false,data:"")]);var rejected=false;do{try SharedProtocol.check(clip)}catch{rejected=true};try check(rejected)}
    let history=MacHistory(root:root);history.preferences.reserveMB=0;let clip=SharedClip(id:"synthetic",kind:"files",title:"Testordner",text:"",created:milliseconds(),files:[SharedFile(path:"Dokumente",directory:true,data:""),SharedFile(path:"Dokumente/Notiz.txt",directory:false,data:plain.base64EncodedString()),SharedFile(path:"Dokumente/Leer",directory:true,data:"")]);let entry=try history.insert(clip,origin:"Windows");try check(try Data(contentsOf:URL(fileURLWithPath:entry.paths[0]+"/Notiz.txt"))==plain);try check(fm.fileExists(atPath:entry.paths[0]+"/Leer"));let exported=try history.export(entry);try check(exported.files.count==3);try check(entry.origin=="Windows");let reopened=MacHistory(root:root);try check(reopened.entries.count==1);let response=try SharedProtocol.seal(plain,key:key);print("SWIFT_AEAD_FIXTURE:"+response.base64EncodedString());print("PASS \(count) MAC TESTS")
}
