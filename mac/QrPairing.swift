import Foundation
import CryptoKit

struct QrLink {
    let host:String,device:String,session:String,publicKey:Data,token:Data
    let port:Int,expires:Int64
    init(_ text:String) throws {
        guard text.utf8.count<=2048,let url=URLComponents(string:text),url.scheme=="kopra",url.host=="pair",let items=url.queryItems,Set(items.map{$0.name}).count==items.count else{throw SharedError.invalid}
        let fields=Dictionary(uniqueKeysWithValues:items.map{($0.name,$0.value ?? "")});guard fields["v"]=="2",let host=fields["host"],let port=Int(fields["port"] ?? ""),port==53328,let expires=Int64(fields["expires"] ?? ""),expires>milliseconds(),expires<=milliseconds()+360000 else{throw SharedError.invalid}
        let bytes=host.split(separator:".").compactMap{Int($0)};guard host==bytes.map(String.init).joined(separator:"."),bytes.count==4,bytes.allSatisfy({$0>=0 && $0<256}),bytes[0]==10 || bytes[0]==127 || (bytes[0]==192 && bytes[1]==168) || (bytes[0]==172 && bytes[1]>=16 && bytes[1]<=31) || (bytes[0]==169 && bytes[1]==254) else{throw SharedError.invalid}
        let device=fields["device"] ?? "",session=fields["session"] ?? "";guard device.count==32,session.count==32,device.allSatisfy({$0.isHexDigit}),session.allSatisfy({$0.isHexDigit}) else{throw SharedError.invalid}
        let pub=try PairV2.decode(fields["pub"] ?? ""),token=try PairV2.decode(fields["token"] ?? "");guard pub.count==65,token.count==32 else{throw SharedError.invalid};_ = try P256.KeyAgreement.PublicKey(x963Representation:pub)
        self.host=host;self.port=port;self.device=device;self.session=session;self.publicKey=pub;self.token=token;self.expires=expires
    }
}
enum PairV2 {
    static func base64(_ data:Data)->String {data.base64EncodedString().replacingOccurrences(of:"+",with:"-").replacingOccurrences(of:"/",with:"_").replacingOccurrences(of:"=",with:"")}
    static func decode(_ text:String) throws -> Data {guard text.count<=16384 else{throw SharedError.invalid};var base=text.replacingOccurrences(of:"-",with:"+").replacingOccurrences(of:"_",with:"/");while base.count%4 != 0{base+="="};guard let value=Data(base64Encoded:base) else{throw SharedError.invalid};return value}
    static func derive(_ shared:SharedSecret,token:Data,session:String,sync:Bool=false)->Data {let key=shared.hkdfDerivedSymmetricKey(using:SHA256.self,salt:Data(SHA256.hash(data:token)),sharedInfo:Data(((sync ? "kopra-sync-v1:":"kopra-pair-v2:")+session).utf8),outputByteCount:32);return key.withUnsafeBytes{Data($0)}}
    static func code(_ key:Data)->String {let digest=Array(SHA256.hash(data:key+Data("kopra-confirm-v2".utf8)));let number=digest.prefix(4).reduce(UInt32(0)){($0<<8)|UInt32($1)};return String(format:"%06u",number%1000000)}
}
struct PairReply {let status:String,code:String}
struct PairClient {
    let link:QrLink,device:String,publicKey:String,handshake:Data,sync:Data,proof:String
    var code:String {PairV2.code(handshake)}
    init(_ text:String,device:String) throws {
        let link=try QrLink(text);guard link.device != device else{throw SharedError.invalid};let key=P256.KeyAgreement.PrivateKey();let pub=PairV2.base64(key.publicKey.x963Representation);let shared=try key.sharedSecretFromKeyAgreement(with:P256.KeyAgreement.PublicKey(x963Representation:link.publicKey));let proof=HMAC<SHA256>.authenticationCode(for:Data(("kopra-pair-v2|"+link.session+"|"+device+"|"+pub).utf8),using:SymmetricKey(data:link.token));self.link=link;self.device=device;self.publicKey=pub;self.handshake=PairV2.derive(shared,token:link.token,session:link.session);self.sync=PairV2.derive(shared,token:link.token,session:link.session,sync:true);self.proof=PairV2.base64(Data(proof))
    }
    func begin() throws -> PairReply {try send(["kind":"pair-request","session":link.session,"device":device,"pub":publicKey,"proof":proof])}
    func action(_ action:String) throws -> PairReply {let inner=try JSONSerialization.data(withJSONObject:["device":device,"action":action]);return try send(["kind":"pair-exchange","session":link.session,"cipher":PairV2.base64(SharedProtocol.seal(inner,key:handshake))])}
    func send(_ payload:[String:String]) throws -> PairReply {
        let data=try SharedProtocol.request(JSONSerialization.data(withJSONObject:payload),host:link.host,port:link.port);guard let envelope=try JSONSerialization.jsonObject(with:data) as? [String:Any],envelope["kind"] as? String=="pair-reply",envelope["session"] as? String==link.session,let cipher=envelope["cipher"] as? String else{throw SharedError.unavailable}
        let plain=try SharedProtocol.open(PairV2.decode(cipher),key:handshake);guard let reply=try JSONSerialization.jsonObject(with:plain) as? [String:Any],reply["device"] as? String==link.device,reply["session"] as? String==link.session,reply["code"] as? String==code,let status=reply["status"] as? String,["waiting","approved","complete","rejected"].contains(status) else{throw SharedError.invalid};return PairReply(status:status,code:code)
    }
    var pairing:Pairing {Pairing(host:link.host,port:link.port,device:link.device,key:sync.base64EncodedString())}
}
