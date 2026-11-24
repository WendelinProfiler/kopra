import Foundation
import CryptoKit
import Darwin
import Security

enum CodeCrypto {
    static let prime = "AC6BDB41324A9A9BF166DE5E1389582FAF72B6651987EE07FC3192943DB56050A37329CBB4A099ED8193E0757767A13DD52312AB4B03310DCD7F48A9DA04FD50E8083969EDB767B0CF6095179A163AB3661A05FBD5FAAAE82918A9962F0B93B855F97993EC975EEAA80D740ADBF4FF747359D041D5C33EA71D281E446B14773BCA97B43A23FB801676BD207A436C6481F1D2B9078717461A5B9D32E688F87748544523B524B0D57D5EA77A2775D2ECFA032CFBDBF52FB3786160279004E57AE6AF874E7303CE53299CCC041C7BC308D82A5698F3A8D0C38271AE35F8E9DBFBB694B5C803D89F7AE435DE236D525F54759B65E372FCD68EF20FA7111F9E4AFF73"
    static let n = BigUInt(prime, radix:16)!
    static func hash(_ data:Data)->Data { Data(SHA256.hash(data:data)) }
    static func pad(_ value:BigUInt)->Data { let bytes=value.serialize();return Data(repeating:0,count:256-bytes.count)+bytes }
    static let k = BigUInt(hash(pad(n)+pad(2)))
    static func secret(code:String,salt:Data,a:BigUInt,b:BigUInt,session:String,device:String)throws->(Data,Data){
        guard b>0,b<n,salt.count==32 else{throw SharedError.invalid}
        let publicA=BigUInt(2).power(a,modulus:n)
        let u=BigUInt(hash(pad(publicA)+pad(b)));guard u>0 else{throw SharedError.invalid}
        let x=BigUInt(hash(salt+hash(Data(("Kopra:"+code).utf8))))
        let base=(b+n-(k*BigUInt(2).power(x,modulus:n))%n)%n;guard base>0 else{throw SharedError.invalid}
        let shared=base.power(a+u*x,modulus:n)
        let context=hash(Data(("kopra-code-v1|"+session+"|"+device+"|").utf8)+salt+pad(publicA)+pad(b))
        let key=HKDF<SHA256>.deriveKey(inputKeyMaterial:SymmetricKey(data:hash(pad(shared))),salt:salt,info:context,outputByteCount:32)
        return (key.withUnsafeBytes{Data($0)},context)
    }
    static func proof(_ key:Data,_ context:Data,_ role:String)->Data {Data(HMAC<SHA256>.authenticationCode(for:context+Data(role.utf8),using:SymmetricKey(data:key)))}
}

enum CodePairing {
    static func discover()throws->[(String,String)]{
        let fd=socket(AF_INET,SOCK_DGRAM,0);guard fd>=0 else{throw SharedError.unavailable};defer{close(fd)}
        var one:Int32=1;setsockopt(fd,SOL_SOCKET,SO_BROADCAST,&one,socklen_t(4));var timeout=timeval(tv_sec:1,tv_usec:0);setsockopt(fd,SOL_SOCKET,SO_RCVTIMEO,&timeout,socklen_t(MemoryLayout.size(ofValue:timeout)))
        let id=uniqueID();let body=try JSONSerialization.data(withJSONObject:["kind":"code-locate","id":id])
        var targets=Set<String>(["255.255.255.255"]);var interfaces:UnsafeMutablePointer<ifaddrs>?
        if getifaddrs(&interfaces)==0{defer{freeifaddrs(interfaces)};var cursor=interfaces;while let item=cursor{defer{cursor=item.pointee.ifa_next};guard let address=item.pointee.ifa_addr,address.pointee.sa_family==UInt8(AF_INET),let mask=item.pointee.ifa_netmask else{continue};let ip=UnsafeRawPointer(address).assumingMemoryBound(to:sockaddr_in.self).pointee.sin_addr.s_addr;let netmask=UnsafeRawPointer(mask).assumingMemoryBound(to:sockaddr_in.self).pointee.sin_addr.s_addr;var broadcast=in_addr(s_addr:ip | ~netmask);var chars=[CChar](repeating:0,count:Int(INET_ADDRSTRLEN));if inet_ntop(AF_INET,&broadcast,&chars,socklen_t(chars.count)) != nil{targets.insert(String(cString:chars))}}}
        var offers=[(String,String)]();var seen=Set<String>()
        for _ in 0..<2 {
            for host in targets{var target=sockaddr_in();target.sin_len=UInt8(MemoryLayout<sockaddr_in>.size);target.sin_family=sa_family_t(AF_INET);target.sin_port=UInt16(53329).bigEndian;inet_pton(AF_INET,host,&target.sin_addr);_ = body.withUnsafeBytes{bytes in withUnsafePointer(to:&target){pointer in pointer.withMemoryRebound(to:sockaddr.self,capacity:1){sendto(fd,bytes.baseAddress,body.count,0,$0,socklen_t(MemoryLayout<sockaddr_in>.size))}}}}
            let deadline=Date().addingTimeInterval(2)
            while Date()<deadline,offers.count<8{var buffer=[UInt8](repeating:0,count:512);var source=sockaddr_in();var size=socklen_t(MemoryLayout<sockaddr_in>.size);let count=withUnsafeMutablePointer(to:&source){pointer in pointer.withMemoryRebound(to:sockaddr.self,capacity:1){recvfrom(fd,&buffer,buffer.count,0,$0,&size)}};if count<=0{break};guard let value=try? JSONSerialization.jsonObject(with:Data(buffer.prefix(count))) as? [String:String],value["kind"]=="code-offer",value["id"]==id,let session=value["session"],session.count==32,session.allSatisfy({$0.isHexDigit}) else{continue};var address=source.sin_addr;var chars=[CChar](repeating:0,count:Int(INET_ADDRSTRLEN));guard inet_ntop(AF_INET,&address,&chars,socklen_t(chars.count)) != nil else{continue};let host=String(cString:chars);if seen.insert(host+session).inserted{offers.append((host,session))}}
            if !offers.isEmpty{break}
        }
        return offers
    }
    static func resolve(_ code:String,device:String)throws->String{
        guard code.count==6,code.utf8.allSatisfy({$0>=48 && $0<=57}) else{throw SharedError.invalid}
        let offers=try discover();for (host,session) in offers{do{return try resolve(code,device:device,host:host,session:session)}catch{continue}};throw SharedError.unavailable
    }
    static func resolve(_ code:String,device:String,host:String,session:String)throws->String{
        var random=Data(count:32);let status=random.withUnsafeMutableBytes{SecRandomCopyBytes(kSecRandomDefault,32,$0.baseAddress!)};guard status==errSecSuccess else{throw SharedError.unavailable}
        let a=BigUInt(random);let publicA=BigUInt(2).power(a,modulus:CodeCrypto.n)
        func request(_ fields:[String:String])throws->[String:String]{let data=try SharedProtocol.request(JSONSerialization.data(withJSONObject:fields),host:host,port:53328);guard data.count<=4096,let result=try JSONSerialization.jsonObject(with:data) as? [String:String],result["session"]==session else{throw SharedError.invalid};return result}
        let challenge=try request(["kind":"code-start","session":session,"device":device,"pub":PairV2.base64(CodeCrypto.pad(publicA))]);guard challenge["kind"]=="code-challenge",let id=challenge["attempt"],let saltText=challenge["salt"],let publicText=challenge["pub"] else{throw SharedError.invalid}
        let salt=try PairV2.decode(saltText),publicB=try PairV2.decode(publicText);guard publicB.count==256 else{throw SharedError.invalid}
        let (key,context)=try CodeCrypto.secret(code:code,salt:salt,a:a,b:BigUInt(publicB),session:session,device:device)
        let response=try request(["kind":"code-finish","session":session,"attempt":id,"proof":PairV2.base64(CodeCrypto.proof(key,context,"client"))]);guard response["kind"]=="code-ready",let proof=response["proof"],let cipher=response["cipher"],try PairV2.decode(proof)==CodeCrypto.proof(key,context,"server") else{throw SharedError.invalid}
        let plain=try SharedProtocol.open(PairV2.decode(cipher),key:key);guard let link=String(data:plain,encoding:.utf8),try QrLink(link).host==host else{throw SharedError.invalid};return link
    }
}
