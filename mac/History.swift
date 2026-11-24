import Foundation
import CryptoKit
import AppKit

struct HistoryEntry: Codable, Identifiable {
    var id: String; var kind: String; var title: String; var text: String; var paths: [String]; var bytes: Int; var created: Int64; var pinned: Bool; var origin: String; var wireID: String
}
struct MacPreferences: Codable {
    var device = uniqueID(); var since = milliseconds(); var textDays = 30; var fileDays = 7; var budgetMB = 2048; var maximumMB = 256; var reserveMB = 5120; var maximumEntries = 2000
    var paused = false; var sharedPaste = true; var excluded = ["com.1password.1password","com.bitwarden.desktop","org.keepassxc.keepassxc"]
    var sharingEnabled=false;var searchKey:UInt32=49;var searchModifiers:UInt32=256;var historyKey:UInt32=9;var historyModifiers:UInt32=768;var searchLabel="⌘Leertaste";var historyLabel="⌘⇧V"
    var received = [String](); var sent = [String](); var lastWindowsAck = ""
    init(){}
    enum CodingKeys:String,CodingKey{case device,since,textDays,fileDays,budgetMB,maximumMB,reserveMB,maximumEntries,paused,sharedPaste,excluded,received,sent,lastWindowsAck,sharingEnabled,searchKey,searchModifiers,historyKey,historyModifiers,searchLabel,historyLabel}
    init(from decoder:Decoder) throws {
        self.init();let c=try decoder.container(keyedBy:CodingKeys.self)
        func read<T:Decodable>(_ key:CodingKeys,_ fallback:T)->T{(try? c.decodeIfPresent(T.self,forKey:key)) ?? fallback}
        device=read(.device,device);since=read(.since,since);textDays=read(.textDays,textDays);fileDays=read(.fileDays,fileDays);budgetMB=read(.budgetMB,budgetMB);maximumMB=read(.maximumMB,maximumMB);reserveMB=read(.reserveMB,reserveMB);maximumEntries=read(.maximumEntries,maximumEntries);paused=read(.paused,paused);sharedPaste=read(.sharedPaste,sharedPaste);excluded=read(.excluded,excluded);received=read(.received,received);sent=read(.sent,sent);lastWindowsAck=read(.lastWindowsAck,lastWindowsAck);sharingEnabled=read(.sharingEnabled,sharingEnabled);searchKey=read(.searchKey,searchKey);searchModifiers=read(.searchModifiers,searchModifiers);historyKey=read(.historyKey,historyKey);historyModifiers=read(.historyModifiers,historyModifiers);searchLabel=read(.searchLabel,searchLabel);historyLabel=read(.historyLabel,historyLabel)
    }
}
final class MacHistory {
    let root: URL; var entries = [HistoryEntry](); var preferences = MacPreferences()
    private let fm = FileManager.default
    init(root: URL? = nil) {
        self.root = root ?? fm.urls(for:.applicationSupportDirectory,in:.userDomainMask)[0].appendingPathComponent("Kopra",isDirectory:true)
        try? fm.createDirectory(at:self.root.appendingPathComponent("objects"),withIntermediateDirectories:true)
        try? fm.setAttributes([.posixPermissions:0o700],ofItemAtPath:self.root.path)
        if let data = try? Data(contentsOf:self.root.appendingPathComponent("preferences.json")), let value = try? JSONDecoder().decode(MacPreferences.self,from:data) { preferences = value }
        if let data = try? Data(contentsOf:self.root.appendingPathComponent("history.json")), data.count < 32 * 1_048_576, let value = try? JSONDecoder().decode([HistoryEntry].self,from:data) { entries = value }
        prune()
    }
    func save() throws { try JSONEncoder().encode(entries).write(to:root.appendingPathComponent("history.json"),options:.atomic); try JSONEncoder().encode(preferences).write(to:root.appendingPathComponent("preferences.json"),options:.atomic) }
    func allocated() -> Int64 {
        var size:Int64 = 0; if let files = fm.enumerator(at:root,includingPropertiesForKeys:[.totalFileAllocatedSizeKey,.isSymbolicLinkKey],options:[]) { for case let url as URL in files { if let value = try? url.resourceValues(forKeys:[.totalFileAllocatedSizeKey,.isSymbolicLinkKey]), value.isSymbolicLink != true { size += Int64(value.totalFileAllocatedSize ?? 4096) } } }; return size
    }
    func remove(_ entry: HistoryEntry) { guard entry.id.count == 32, entry.id.allSatisfy({$0.isHexDigit}) else { return }; try? fm.removeItem(at:root.appendingPathComponent("objects/"+entry.id)); entries.removeAll{$0.id==entry.id} }
    func prune() {
        let now = milliseconds(); for entry in entries where !entry.pinned { let days = entry.kind == "text" ? preferences.textDays : preferences.fileDays; if entry.created < now - Int64(days)*86_400_000 { remove(entry) } }
        while entries.filter({!$0.pinned}).count > preferences.maximumEntries || allocated() + 4 * 1_048_576 > Int64(preferences.budgetMB)*1_048_576 { guard let oldest = entries.filter({!$0.pinned}).min(by:{$0.created<$1.created}) else { break }; remove(oldest) }
        let known = Set(entries.map{$0.id}); if let dirs = try? fm.contentsOfDirectory(at:root.appendingPathComponent("objects"),includingPropertiesForKeys:nil) { for dir in dirs where dir.lastPathComponent.count == 32 && !known.contains(dir.lastPathComponent) { try? fm.removeItem(at:dir) } }; try? save()
    }
    func reserve(_ bytes: Int) throws {
        guard bytes <= min(preferences.maximumMB*1_048_576,maximumContent) else { throw SharedError.tooLarge }
        let needed = Int64(bytes)*2 + 4*1_048_576; let budget = Int64(preferences.budgetMB)*1_048_576
        let disk = try fm.attributesOfFileSystem(forPath:root.path); let free = (disk[.systemFreeSize] as? NSNumber)?.int64Value ?? 0
        guard free > needed + Int64(preferences.reserveMB)*1_048_576, needed < budget else { throw SharedError.storage }
        while allocated() + needed > budget || entries.filter({!$0.pinned}).count >= preferences.maximumEntries { guard let oldest = entries.filter({!$0.pinned}).min(by:{$0.created<$1.created}) else { throw SharedError.storage }; remove(oldest) }
    }
    @discardableResult func insert(_ clip: SharedClip, origin: String) throws -> HistoryEntry {
        if let existing=entries.first(where:{$0.wireID==clip.id}){return existing}
        if clip.kind=="text",let index=entries.firstIndex(where:{$0.kind=="text" && $0.text==clip.text}){try SharedProtocol.check(clip);entries[index].created=clip.created;entries[index].wireID=clip.id;entries[index].origin=origin;try save();return entries[index]}
        try SharedProtocol.check(clip); var bytes = clip.text.utf8.count; for file in clip.files { bytes += file.data.count*3/4 }
        try reserve(bytes); let id = uniqueID(); let folder = root.appendingPathComponent("objects/"+id,isDirectory:true); var paths=[String]()
        do {
            if clip.kind != "text" { try fm.createDirectory(at:folder,withIntermediateDirectories:true); for file in clip.files.sorted(by:{$0.path.split(separator:"/").count<$1.path.split(separator:"/").count}) { let target = folder.appendingPathComponent(file.path); guard target.standardizedFileURL.path.hasPrefix(folder.path+"/") else { throw SharedError.invalid }; if file.directory { try fm.createDirectory(at:target,withIntermediateDirectories:true) } else { try fm.createDirectory(at:target.deletingLastPathComponent(),withIntermediateDirectories:true); guard let data = Data(base64Encoded:file.data) else { throw SharedError.invalid }; try data.write(to:target,options:.atomic) } }; var names=Set<String>(); for file in clip.files { let top=String(file.path.split(separator:"/")[0]); if names.insert(top).inserted { paths.append(folder.appendingPathComponent(top).path) } } }
            let entry=HistoryEntry(id:id,kind:clip.kind,title:clip.title,text:clip.text,paths:paths,bytes:bytes,created:clip.created,pinned:false,origin:origin,wireID:clip.id); entries.insert(entry,at:0); try save(); return entry
        } catch { try? fm.removeItem(at:folder); throw error }
    }
    func export(_ entry: HistoryEntry) throws -> SharedClip {
        if entry.kind == "text" { return SharedClip(id:entry.wireID,kind:"text",title:entry.title,text:entry.text,created:entry.created,files:[]) }
        var clip=try SharedProtocol.pack(urls:entry.paths.map{URL(fileURLWithPath:$0)},kind:entry.kind,title:entry.title);clip.id=entry.wireID;clip.created=entry.created;return clip
    }
    func pin(_ entry: HistoryEntry) { if let index=entries.firstIndex(where:{$0.id==entry.id}) { entries[index].pinned.toggle();try? save() } }
}
