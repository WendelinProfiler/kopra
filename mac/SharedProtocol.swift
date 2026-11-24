import Foundation
import CryptoKit
import Security
import Darwin

let maximumContent = 32 * 1_048_576
let maximumFrame = 48 * 1_048_576
struct SharedFile: Codable { var path: String; var directory: Bool; var data: String }
struct SharedClip: Codable { var id: String; var kind: String; var title: String; var text: String; var created: Int64; var files: [SharedFile] }
struct SyncPacket: Codable { var version: Int; var device: String; var id: String; var issued: Int64; var kind: String; var ack: String; var entry: SharedClip? }
struct Pairing: Codable { var host: String; var port: Int; var device: String; var key: String }
enum SharedError: Error { case invalid, unavailable, storage, tooLarge }
func milliseconds() -> Int64 { Int64(Date().timeIntervalSince1970 * 1000) }
func uniqueID() -> String { UUID().uuidString.replacingOccurrences(of: "-", with: "").lowercased() }

enum SharedProtocol {
    static let aad = Data("Kopra/1".utf8)
    static func seal(_ data: Data, key: Data) throws -> Data { guard key.count == 32, data.count <= maximumFrame - 28 else { throw SharedError.invalid }; return try AES.GCM.seal(data, using: SymmetricKey(data: key), authenticating: aad).combined! }
    static func open(_ data: Data, key: Data) throws -> Data { guard data.count >= 28, data.count <= maximumFrame else { throw SharedError.invalid }; return try AES.GCM.open(AES.GCM.SealedBox(combined: data), using: SymmetricKey(data: key), authenticating: aad) }
    static func check(_ clip: SharedClip) throws {
        guard !clip.id.isEmpty, clip.id.count <= 100, clip.title.count <= 500, clip.text.utf8.count <= 1_048_576, clip.files.count <= 2000, ["text","image","files"].contains(clip.kind) else { throw SharedError.invalid }
        var names = Set<String>(); var total = clip.text.utf8.count
        for file in clip.files {
            let parts = file.path.split(separator: "/", omittingEmptySubsequences: false)
            guard file.path.count <= 500, parts.count <= 32, !parts.isEmpty, !parts.contains(where: { $0.isEmpty || $0 == "." || $0 == ".." || $0.contains("\\") || $0.contains(":") || $0.contains("\0") || $0.contains("<") || $0.contains(">") || $0.contains("|") || $0.contains("\"") || $0.contains("?") || $0.contains("*") || $0.hasSuffix(".") || $0.hasSuffix(" ") }), names.insert(file.path.lowercased()).inserted else { throw SharedError.invalid }
            guard !file.directory || file.data.isEmpty else { throw SharedError.invalid }
            total += file.data.count * 3 / 4; guard total <= maximumContent else { throw SharedError.tooLarge }
            guard file.directory || Data(base64Encoded: file.data) != nil else { throw SharedError.invalid }
        }
        guard !(clip.kind == "text" && !clip.files.isEmpty), !(clip.kind == "image" && (clip.files.count != 1 || clip.files[0].directory)), !(clip.kind == "files" && clip.files.isEmpty) else { throw SharedError.invalid }
        for file in clip.files where !file.directory { guard !clip.files.contains(where: { $0.path.lowercased().hasPrefix(file.path.lowercased() + "/") }) else { throw SharedError.invalid } }
    }
    static func pack(urls: [URL], kind: String, title: String) throws -> SharedClip {
        var files = [SharedFile](); var total = 0
        func add(_ url: URL, relative: String, depth: Int) throws {
            guard depth <= 32, files.count < 2000 else { throw SharedError.tooLarge }
            let v = try url.resourceValues(forKeys: [.isSymbolicLinkKey,.isDirectoryKey,.fileSizeKey]); guard v.isSymbolicLink != true else { throw SharedError.invalid }
            let dir = v.isDirectory == true; guard total + (v.fileSize ?? 0) <= maximumContent else { throw SharedError.tooLarge }
            let bytes = dir ? Data() : try Data(contentsOf: url); total += bytes.count; guard total <= maximumContent else { throw SharedError.tooLarge }
            files.append(SharedFile(path: relative, directory: dir, data: bytes.base64EncodedString()))
            if dir { for child in try FileManager.default.contentsOfDirectory(at: url, includingPropertiesForKeys: nil) { try add(child, relative: relative + "/" + child.lastPathComponent, depth: depth + 1) } }
        }
        var used = Set<String>()
        for url in urls { var name = url.lastPathComponent; var suffix = 2; while !used.insert(name.lowercased()).inserted { name = url.lastPathComponent + " (\(suffix))"; suffix += 1 }; try add(url, relative: name, depth: 0) }
        let clip = SharedClip(id: uniqueID(), kind: kind, title: title, text: "", created: milliseconds(), files: files); try check(clip); return clip
    }
    static func exchange(_ packet: SyncPacket, pairing: Pairing) throws -> SyncPacket {
        guard let key = Data(base64Encoded: pairing.key), key.count == 32, pairing.port > 0, pairing.port < 65536 else { throw SharedError.invalid }
        let body = try seal(JSONEncoder().encode(packet), key: key)
        let response = try JSONDecoder().decode(SyncPacket.self, from: open(request(body,host:pairing.host,port:pairing.port), key: key)); guard response.version == 1, response.id == packet.id, response.device == pairing.device, response.kind == "response", abs(milliseconds() - response.issued) < 60_000 else { throw SharedError.invalid }; return response
    }
    static func request(_ body:Data,host:String,port:Int) throws -> Data {
        var hints = addrinfo(); hints.ai_family = AF_INET; hints.ai_socktype = SOCK_STREAM; var info: UnsafeMutablePointer<addrinfo>?
        guard getaddrinfo(host, String(port), &hints, &info) == 0, let address = info else { throw SharedError.unavailable }; defer { freeaddrinfo(info) }
        let fd = socket(AF_INET, SOCK_STREAM, 0); guard fd >= 0 else { throw SharedError.unavailable }; defer { close(fd) }
        var one: Int32 = 1; setsockopt(fd, SOL_SOCKET, SO_NOSIGPIPE, &one, socklen_t(MemoryLayout.size(ofValue: one)))
        let flags = fcntl(fd, F_GETFL, 0); _ = fcntl(fd, F_SETFL, flags | O_NONBLOCK)
        let connected = connect(fd, address.pointee.ai_addr, address.pointee.ai_addrlen)
        if connected != 0 { guard errno == EINPROGRESS else { throw SharedError.unavailable }; var event = pollfd(fd: fd, events: Int16(POLLOUT), revents: 0); guard poll(&event, 1, 5000) > 0 else { throw SharedError.unavailable }; var error: Int32 = 0; var length = socklen_t(4); getsockopt(fd, SOL_SOCKET, SO_ERROR, &error, &length); guard error == 0 else { throw SharedError.unavailable } }
        _ = fcntl(fd, F_SETFL, flags); var timeout = timeval(tv_sec: 20, tv_usec: 0); setsockopt(fd, SOL_SOCKET, SO_RCVTIMEO, &timeout, socklen_t(MemoryLayout.size(ofValue: timeout))); setsockopt(fd, SOL_SOCKET, SO_SNDTIMEO, &timeout, socklen_t(MemoryLayout.size(ofValue: timeout)))
        func write(_ data: Data) throws { try data.withUnsafeBytes { raw in var n = 0; while n < data.count { let count = Darwin.send(fd, raw.baseAddress!.advanced(by: n), data.count - n, 0); guard count > 0 else { throw SharedError.unavailable }; n += count } } }
        func read(_ count: Int) throws -> Data { var data = Data(count: count); try data.withUnsafeMutableBytes { raw in var n = 0; while n < count { let got = recv(fd, raw.baseAddress!.advanced(by: n), count - n, 0); guard got > 0 else { throw SharedError.unavailable }; n += got } }; return data }
        var length = UInt32(body.count).bigEndian; try write(withUnsafeBytes(of: &length) { Data($0) }); try write(body)
        let header = try read(4); let count = header.reduce(0) { ($0 << 8) | Int($1) }; guard count >= 28, count <= maximumFrame else { throw SharedError.invalid }
        return try read(count)
    }
    static func discover(_ pairing: Pairing, device: String) throws -> Pairing {
        guard let key=Data(base64Encoded:pairing.key) else{throw SharedError.invalid};let packet=SyncPacket(version:1,device:device,id:uniqueID(),issued:milliseconds(),kind:"discover",ack:"",entry:nil);let body=try seal(JSONEncoder().encode(packet),key:key)
        let fd=socket(AF_INET,SOCK_DGRAM,0);guard fd>=0 else{throw SharedError.unavailable};defer{close(fd)};var one:Int32=1;setsockopt(fd,SOL_SOCKET,SO_BROADCAST,&one,socklen_t(4));var timeout=timeval(tv_sec:2,tv_usec:0);setsockopt(fd,SOL_SOCKET,SO_RCVTIMEO,&timeout,socklen_t(MemoryLayout.size(ofValue:timeout)))
        var target=sockaddr_in();target.sin_len=UInt8(MemoryLayout<sockaddr_in>.size);target.sin_family=sa_family_t(AF_INET);target.sin_port=UInt16(pairing.port+1).bigEndian;target.sin_addr.s_addr=INADDR_BROADCAST
        _ = body.withUnsafeBytes{bytes in withUnsafePointer(to:&target){pointer in pointer.withMemoryRebound(to:sockaddr.self,capacity:1){sendto(fd,bytes.baseAddress,body.count,0,$0,socklen_t(MemoryLayout<sockaddr_in>.size))}}}
        var buffer=[UInt8](repeating:0,count:2048);var source=sockaddr_in();var size=socklen_t(MemoryLayout<sockaddr_in>.size)
        let count=withUnsafeMutablePointer(to:&source){pointer in pointer.withMemoryRebound(to:sockaddr.self,capacity:1){recvfrom(fd,&buffer,buffer.count,0,$0,&size)}};guard count>0 else{throw SharedError.unavailable};let answer=try JSONDecoder().decode(SyncPacket.self,from:open(Data(buffer.prefix(count)),key:key));guard answer.id==packet.id,answer.kind=="discovered",answer.device==pairing.device else{throw SharedError.invalid};var address=source.sin_addr;var chars=[CChar](repeating:0,count:Int(INET_ADDRSTRLEN));guard inet_ntop(AF_INET,&address,&chars,socklen_t(chars.count)) != nil else{throw SharedError.invalid};var updated=pairing;updated.host=String(cString:chars);return updated
    }
}

enum PairingVault {
    static let service = "app.kopra.pairing"
    static func save(_ pairing: Pairing?) throws {
        let query: [String:Any] = [kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:service,kSecAttrAccount as String:"windows"]
        if let pairing { let data = try JSONEncoder().encode(pairing); let result = SecItemUpdate(query as CFDictionary, [kSecValueData as String:data] as CFDictionary); if result == errSecItemNotFound { var add = query; add[kSecValueData as String] = data; add[kSecAttrAccessible as String] = kSecAttrAccessibleAfterFirstUnlockThisDeviceOnly; guard SecItemAdd(add as CFDictionary,nil) == errSecSuccess else { throw SharedError.storage } } else if result != errSecSuccess { throw SharedError.storage } } else { let status=SecItemDelete(query as CFDictionary);guard status==errSecSuccess || status==errSecItemNotFound else{throw SharedError.storage} }
    }
    static func load() -> Pairing? { let query:[String:Any] = [kSecClass as String:kSecClassGenericPassword,kSecAttrService as String:service,kSecAttrAccount as String:"windows",kSecReturnData as String:true,kSecMatchLimit as String:kSecMatchLimitOne]; var result:CFTypeRef?; guard SecItemCopyMatching(query as CFDictionary,&result) == errSecSuccess, let data = result as? Data else { return nil }; return try? JSONDecoder().decode(Pairing.self,from:data) }
}
