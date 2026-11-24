import Foundation
import CryptoKit
import AppKit
import SwiftUI

@MainActor func uiSelfTest() throws {
    let app=NSApplication.shared;app.setActivationPolicy(.accessory);var failure:Error?
    func exercise() throws {
    let root=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-ui-tests-"+uniqueID());let history=MacHistory(root:root);history.preferences.reserveMB=0;defer{try? FileManager.default.removeItem(at:root)}
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"text",title:"Test",text:"Nur künstliche Testdaten",created:milliseconds(),files:[]),origin:"")
    let model=AppModel(history:history,loadPairing:false);let delegate=AppDelegate(model:model)
    let panel=LauncherPanel(contentRect:NSRect(x:-2000,y:-2000,width:740,height:500),styleMask:[.borderless],backing:.buffered,defer:false);model.panel=panel;panel.contentView=NSHostingView(rootView:LauncherView(model:model));model.search();panel.makeKeyAndOrderFront(nil);NSApp.activate(ignoringOtherApps:true);RunLoop.current.run(until:Date().addingTimeInterval(0.1))
    print("UI_PRE_ENTER",model.rows.first?.id=="history",panel.isKeyWindow,panel.isVisible);guard model.rows.first?.id=="history",let enter=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:[],timestamp:0,windowNumber:panel.windowNumber,context:nil,characters:"\r",charactersIgnoringModifiers:"\r",isARepeat:false,keyCode:36) else{throw SharedError.invalid}
    let handled=delegate.handleKey(enter)==nil;print("UI_POST_ENTER",handled,model.historyMode,panel.isVisible);guard handled,model.historyMode,panel.isVisible else{throw SharedError.invalid}
    let file=try history.insert(SharedClip(id:uniqueID(),kind:"files",title:"Notiz.txt",text:"",created:milliseconds(),files:[SharedFile(path:"Notiz.txt",directory:false,data:Data("Synthetischer Dateitest".utf8).base64EncodedString())]),origin:"")
    model.search();guard let fileIndex=model.rows.firstIndex(where:{$0.entry?.id==file.id}) else{throw SharedError.invalid};model.selected=fileIndex
    var openedPath="";model.historyOpener={urls in openedPath=urls.first?.path ?? "";return false};_ = delegate.handleKey(enter);guard openedPath==file.paths[0],panel.isVisible else{throw SharedError.invalid}
    model.historyOpener={urls in openedPath=urls.first?.path ?? "";return true};_ = delegate.handleKey(enter);guard openedPath==file.paths[0],!panel.isVisible else{throw SharedError.invalid};print("PASS NATIVE FILE ENTER: REAL_SAVED_PATH, FAILED_OPEN_STAYS_VISIBLE, SUCCESS_HIDES")
    panel.makeKeyAndOrderFront(nil)
    guard let escape=NSEvent.keyEvent(with:.keyDown,location:.zero,modifierFlags:[],timestamp:0,windowNumber:panel.windowNumber,context:nil,characters:"",charactersIgnoringModifiers:"",isARepeat:false,keyCode:53) else{throw SharedError.invalid}
    _ = delegate.handleKey(escape);guard !panel.isVisible else{throw SharedError.invalid};print("PASS NATIVE UI: ENTER_OPENS_HISTORY_AND_STAYS_VISIBLE, ESCAPE_HIDES")
    }
    DispatchQueue.main.async {
        do{try exercise()}catch{failure=error}
        app.stop(nil)
        if let event=NSEvent.otherEvent(with:.applicationDefined,location:.zero,modifierFlags:[],timestamp:0,windowNumber:0,context:nil,subtype:0,data1:0,data2:0){app.postEvent(event,atStart:true)}
    }
    app.run();if let failure{throw failure}
}

@MainActor func renderPreview(_ folder:String) throws {
    _ = NSApplication.shared;NSApp.setActivationPolicy(.accessory)
    let root=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-preview-"+uniqueID());let history=MacHistory(root:root);history.preferences.reserveMB=0;defer{try? FileManager.default.removeItem(at:root)}
    _ = try history.insert(SharedClip(id:uniqueID(),kind:"text",title:"Eine Idee festhalten",text:"Eine Idee festhalten. Später genau hier wiederfinden.",created:milliseconds(),files:[]),origin:"Windows")
    let model=AppModel(history:history,loadPairing:false);model.status="Mit Windows verbunden";model.historyMode=true;model.search()
    let panel=NSWindow(contentRect:NSRect(x:-2000,y:-2000,width:740,height:590),styleMask:[.borderless],backing:.buffered,defer:false);let host=NSHostingView(rootView:LauncherView(model:model).preferredColorScheme(.dark));panel.contentView=host;panel.orderFrontRegardless();RunLoop.current.run(until:Date().addingTimeInterval(0.3));host.layoutSubtreeIfNeeded()
    try FileManager.default.createDirectory(atPath:folder,withIntermediateDirectories:true);guard let bitmap=host.bitmapImageRepForCachingDisplay(in:host.bounds) else{throw SharedError.invalid};host.cacheDisplay(in:host.bounds,to:bitmap);guard let png=bitmap.representation(using:.png,properties:[:]) else{throw SharedError.invalid};try png.write(to:URL(fileURLWithPath:folder+"/mac-history.png"));panel.orderOut(nil);print("MAC_PREVIEW_READY")
}

func lanSelfTest(_ pairFile:String) throws {
    let code=try String(contentsOfFile:pairFile,encoding:.utf8).trimmingCharacters(in:.whitespacesAndNewlines);guard code.hasPrefix("IS1."),let bytes=Data(base64Encoded:String(code.dropFirst(4))) else{throw SharedError.invalid};let pair=try JSONDecoder().decode(Pairing.self,from:bytes)
    let root=FileManager.default.temporaryDirectory.appendingPathComponent("Kopra-lan-test-"+uniqueID());let history=MacHistory(root:root);history.preferences.reserveMB=0;defer{try? FileManager.default.removeItem(at:root)}
    let text=SharedClip(id:uniqueID(),kind:"text",title:"Mac-Test",text:"IslandSync Test Mac → Windows",created:milliseconds(),files:[])
    let image=SharedClip(id:uniqueID(),kind:"image",title:"Testbild",text:"",created:milliseconds(),files:[SharedFile(path:"Bild.png",directory:false,data:"iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mP8/x8AAwMCAO+a4GQAAAAASUVORK5CYII=")])
    let folder=SharedClip(id:uniqueID(),kind:"files",title:"Testordner",text:"",created:milliseconds(),files:[SharedFile(path:"Testordner",directory:true,data:""),SharedFile(path:"Testordner/Leer",directory:true,data:""),SharedFile(path:"Testordner/Notiz.txt",directory:false,data:Data("Mac test file".utf8).base64EncodedString())])
    let outgoing=[text,image,folder];var sent=0;var ack="";var received=Set<String>();let deadline=Date().addingTimeInterval(80)
    while Date()<deadline {
        let packet=SyncPacket(version:1,device:history.preferences.device,id:uniqueID(),issued:milliseconds(),kind:"exchange",ack:ack,entry:sent<outgoing.count ? outgoing[sent]:nil);let response=try SharedProtocol.exchange(packet,pairing:pair)
        if sent<outgoing.count,response.ack==outgoing[sent].id{sent+=1}
        if let clip=response.entry{if received.insert(clip.id).inserted{try history.insert(clip,origin:"Windows")};ack=clip.id}
        if received.count==3 && sent==3{let final=SyncPacket(version:1,device:history.preferences.device,id:uniqueID(),issued:milliseconds(),kind:"exchange",ack:ack,entry:nil);_ = try SharedProtocol.exchange(final,pairing:pair);let actual=history.entries.contains{$0.text=="IslandSync Test Windows → Mac"} && history.entries.contains{entry in entry.kind=="files" && (try? String(contentsOfFile:entry.paths[0]+"/Notiz.txt",encoding:.utf8))=="Windows test file" && FileManager.default.fileExists(atPath:entry.paths[0]+"/Leer")};guard actual else{throw SharedError.invalid};print("PASS LAN: 3 MAC_TO_WINDOWS, 3 WINDOWS_TO_MAC, ACTUAL_FILES_AND_EMPTY_FOLDERS");return}
        Thread.sleep(forTimeInterval:0.3)
    };throw SharedError.unavailable
}

func selfTest(_ fixture: String) throws {
    let fm=FileManager.default;let root=fm.temporaryDirectory.appendingPathComponent("Kopra-tests-"+uniqueID());try fm.createDirectory(at:root,withIntermediateDirectories:true);defer{try? fm.removeItem(at:root)}
    var count=0;func check(_ value:Bool) throws{count+=1;if !value{throw SharedError.invalid}}
    let json=try JSONSerialization.jsonObject(with:Data(contentsOf:URL(fileURLWithPath:fixture))) as! [String:String];let key=Data(base64Encoded:json["key"]!)!;let plain=Data(base64Encoded:json["plain"]!)!;let sealed=Data(base64Encoded:json["sealedBox"]!)!;try check(try SharedProtocol.open(sealed,key:key)==plain);try check(try SharedProtocol.open(SharedProtocol.seal(plain,key:key),key:key)==plain)
    let pairFile=URL(fileURLWithPath:fixture).deletingLastPathComponent().appendingPathComponent("pair-v2-fixture.json")
    let pair=try JSONSerialization.jsonObject(with:Data(contentsOf:pairFile)) as! [String:String]
    let privateKey=try P256.KeyAgreement.PrivateKey(rawRepresentation:Data(base64Encoded:pair["privateKey"]!)!);let peer=try P256.KeyAgreement.PublicKey(x963Representation:Data(base64Encoded:pair["peerPublic"]!)!);let secret=try privateKey.sharedSecretFromKeyAgreement(with:peer);let token=Data(base64Encoded:pair["token"]!)!;let derived=PairV2.derive(secret,token:token,session:pair["session"]!)
    try check(derived.base64EncodedString()==pair["expected"]);try check(PairV2.derive(secret,token:token,session:pair["session"]!,sync:true).base64EncodedString()==pair["syncExpected"]);try check(PairV2.code(derived)==pair["code"])
    var broken=sealed;broken[14]^=1;do{_ = try SharedProtocol.open(broken,key:key);throw SharedError.invalid}catch is CryptoKitError{count+=1}
    for path in ["../escape","/absolute","C:/drive","a\\b","a/../b","a/","a."]{let clip=SharedClip(id:"test",kind:"files",title:"Test",text:"",created:0,files:[SharedFile(path:path,directory:false,data:"")]);var rejected=false;do{try SharedProtocol.check(clip)}catch{rejected=true};try check(rejected)}
    let history=MacHistory(root:root);history.preferences.reserveMB=0;let clip=SharedClip(id:"synthetic",kind:"files",title:"Testordner",text:"",created:milliseconds(),files:[SharedFile(path:"Dokumente",directory:true,data:""),SharedFile(path:"Dokumente/Notiz.txt",directory:false,data:plain.base64EncodedString()),SharedFile(path:"Dokumente/Leer",directory:true,data:"")]);let entry=try history.insert(clip,origin:"Windows");try check(try Data(contentsOf:URL(fileURLWithPath:entry.paths[0]+"/Notiz.txt"))==plain);try check(fm.fileExists(atPath:entry.paths[0]+"/Leer"));let exported=try history.export(entry);try check(exported.files.count==3);try check(entry.origin=="Windows");let reopened=MacHistory(root:root);try check(reopened.entries.count==1);let response=try SharedProtocol.seal(plain,key:key);print("SWIFT_AEAD_FIXTURE:"+response.base64EncodedString());print("PASS \(count) MAC TESTS")
}
